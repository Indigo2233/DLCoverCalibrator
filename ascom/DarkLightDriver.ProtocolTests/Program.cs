using DarkLight.CoverCalibrator;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

internal static class Program
{
    private static int _failed;

    [STAThread]
    private static int Main()
    {
        Run("DeviceTcp handshake, framing, and response parsing", TestTcpProtocol);
        Run("DeviceSerial recreates the port after a failed handshake", TestSerialHandshakeRecovery);
        Run("DeviceSerial handshake recovery is bounded", TestSerialHandshakeRecoveryIsBounded);
        Run("DeviceSerial disposes a replacement port that cannot open", TestSerialFailedReplacementOpenIsDisposed);
        Run("Driver rebuilds the connection for its second connect attempt", TestConnectionRetryRebuildsDevice);
        Run("ResetDevice recovers the current connection", TestResetDeviceRecoversCurrentConnection);
        Run("ResetDevice rebuilds the connection when direct recovery fails", TestResetDeviceRebuildsConnection);
        Run("Driver state polling is non-reentrant", TestPollingIsNonReentrant);
        Run("Driver automatically recovers after consecutive polling failures", TestAutomaticRecovery);
        Run("ASCOM connection ignores secondary-servo commands", TestSecondaryServoCommandsAreAbsent);
        Run("Setup dialog fits common high-DPI working areas", TestSetupDialogFitsHighDpiWorkingAreas);

        if (_failed == 0)
        {
            Console.WriteLine("PASS: all protocol and recovery tests");
            return 0;
        }

        Console.Error.WriteLine($"FAIL: {_failed} test(s) failed");
        return 1;
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception ex)
        {
            _failed++;
            Console.Error.WriteLine($"FAIL: {name}: {ex.Message}");
        }
    }

    private static void TestTcpProtocol()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = new List<string>();
        Exception serverError = null;

        var server = new Thread(() =>
        {
            try
            {
                using (var client = listener.AcceptTcpClient())
                using (var stream = client.GetStream())
                {
                    var command = new StringBuilder();
                    bool inCommand = false;
                    while (received.Count < 2)
                    {
                        int value = stream.ReadByte();
                        if (value < 0) break;
                        char c = (char)value;
                        if (c == '<')
                        {
                            command.Clear();
                            inCommand = true;
                        }
                        else if (c == '>' && inCommand)
                        {
                            string text = command.ToString();
                            received.Add(text);
                            byte[] response = Encoding.ASCII.GetBytes(text == "Z" ? "<?>\r\n" : "<1>\r\n");
                            stream.Write(response, 0, response.Length);
                            inCommand = false;
                        }
                        else if (inCommand)
                        {
                            command.Append(c);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                serverError = ex;
            }
        });
        server.Start();

        try
        {
            using (var device = new DeviceTcp())
            {
                device.Open("127.0.0.1", port);
                Assert(device.Handshake(), "TCP handshake failed");
                Assert(device.SendCommand("P") == "1", "TCP status response failed");
            }
            server.Join(3000);
            if (serverError != null) throw serverError;
            Assert(received.Count == 2 && received[0] == "Z" && received[1] == "P", "TCP command framing failed");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static void TestSerialHandshakeRecovery()
    {
        var firstPort = new FakeSerialPort(null);
        var recoveredPort = new FakeSerialPort("<?>");
        var ports = new Queue<FakeSerialPort>(new[] { firstPort, recoveredPort });
        var factoryArguments = new List<string>();
        var delays = new List<int>();

        using (var device = new DeviceSerial(
            (portName, baudRate) =>
            {
                factoryArguments.Add($"{portName}:{baudRate}");
                return ports.Dequeue();
            },
            milliseconds => delays.Add(milliseconds)))
        {
            device.Open("COM9", 115200);
            Assert(device.Handshake(), "handshake should recover after rebuilding the serial port");
        }

        Assert(firstPort.Writes.Count == 1 && firstPort.Writes[0] == "<Z>",
            "the initial serial port should receive one bounded handshake attempt");
        int assertedIndex = firstPort.DtrValues.IndexOf(true);
        Assert(assertedIndex >= 0 &&
               assertedIndex + 1 < firstPort.DtrValues.Count &&
               !firstPort.DtrValues[assertedIndex + 1],
            "the recovery sequence should pulse DTR before closing the old port");
        Assert(firstPort.WasDisposed, "the failed serial port should be disposed");
        Assert(recoveredPort.WasOpened, "the replacement serial port should be opened");
        Assert(recoveredPort.Writes.Count == 1 && recoveredPort.Writes[0] == "<Z>",
            "the replacement serial port should receive the recovery handshake");
        Assert(factoryArguments.Count == 2 &&
               factoryArguments[0] == "COM9:115200" &&
               factoryArguments[1] == "COM9:115200",
            "the replacement port should reuse the configured endpoint and baud rate");
        Assert(delays.Count == 2 && delays[0] == 100 && delays[1] == 1500,
            "serial recovery should use one DTR pulse delay and one settle delay");
        Assert(recoveredPort.ReadTimeout == 5000 && recoveredPort.WriteTimeout == 2000 &&
               !recoveredPort.DtrEnable && !recoveredPort.RtsEnable && recoveredPort.NewLine == "\n",
            "the replacement port should receive the production serial settings");
    }

    private static void TestSerialHandshakeRecoveryIsBounded()
    {
        var firstPort = new FakeSerialPort(null);
        var secondPort = new FakeSerialPort(null);
        var ports = new Queue<FakeSerialPort>(new[] { firstPort, secondPort });

        using (var device = new DeviceSerial(
            (portName, baudRate) => ports.Dequeue(),
            milliseconds => { }))
        {
            device.Open("COM9", 115200);
            Assert(!device.Handshake(), "handshake should fail when both bounded attempts time out");
        }

        Assert(ports.Count == 0, "handshake should use exactly two serial-port instances");
        Assert(firstPort.Writes.Count == 1 && secondPort.Writes.Count == 1,
            "each serial-port instance should receive exactly one handshake command");
    }

    private static void TestSerialFailedReplacementOpenIsDisposed()
    {
        var firstPort = new FakeSerialPort(null);
        var replacementPort = new FakeSerialPort(null) { ThrowOnOpen = true };
        var ports = new Queue<FakeSerialPort>(new[] { firstPort, replacementPort });

        using (var device = new DeviceSerial(
            (portName, baudRate) => ports.Dequeue(),
            milliseconds => { }))
        {
            device.Open("COM9", 115200);
            Assert(!device.Handshake(), "handshake should fail when the replacement port cannot open");
            Assert(!device.IsOpen, "device should remain closed after replacement open failure");
        }

        Assert(firstPort.WasDisposed, "the original port should be disposed before replacement");
        Assert(replacementPort.WasDisposed, "a replacement port that fails to open should be disposed");
    }

    private static void TestConnectionRetryRebuildsDevice()
    {
        var firstDevice = new FakeDeviceConnection { HandshakeResult = false };
        var secondDevice = new FakeDeviceConnection();
        var devices = new Queue<FakeDeviceConnection>(new[] { firstDevice, secondDevice });
        var delays = new List<int>();

        using (var driver = new Driver(
            () => devices.Dequeue(),
            milliseconds => delays.Add(milliseconds)))
        {
            driver.Connect();
            Assert(driver.Connected, "the second connection attempt should connect the driver");
            Assert(firstDevice.WasDisposed, "the failed connection instance should be disposed");
            Assert(firstDevice.OpenCount == 1 && firstDevice.HandshakeCount == 1,
                "the first connection should receive one bounded attempt");
            Assert(secondDevice.OpenCount == 1 && secondDevice.HandshakeCount == 1,
                "the second connection should use a fresh instance and one handshake");
            Assert(delays.Count == 1 && delays[0] == 1500,
                "connection recovery should settle exactly once between two attempts");
        }
    }

    private static void TestResetDeviceRecoversCurrentConnection()
    {
        var device = new FakeDeviceConnection();
        using (var driver = new Driver(() => device, milliseconds => { }))
        {
            driver.Connect();
            string result = driver.Action("resetdevice", string.Empty);

            Assert(result.StartsWith("OK:", StringComparison.Ordinal),
                "ResetDevice should report successful direct recovery");
            Assert(device.RecoveryCount == 1, "ResetDevice should first recover the current connection");
            Assert(device.OpenCount == 1, "direct recovery should retain the current connection instance");
            Assert(driver.Connected, "the driver should remain connected after direct recovery");
        }
    }

    private static void TestResetDeviceRebuildsConnection()
    {
        var firstDevice = new FakeDeviceConnection { RecoverResult = false };
        var replacementDevice = new FakeDeviceConnection();
        var devices = new Queue<FakeDeviceConnection>(new[] { firstDevice, replacementDevice });

        using (var driver = new Driver(() => devices.Dequeue(), milliseconds => { }))
        {
            driver.Connect();
            string result = driver.Action("ResetDevice", string.Empty);

            Assert(result.StartsWith("OK:", StringComparison.Ordinal),
                "ResetDevice should report success after rebuilding the connection");
            Assert(firstDevice.RecoveryCount == 1 && firstDevice.WasDisposed,
                "ResetDevice should release the current instance after direct recovery fails");
            Assert(replacementDevice.OpenCount == 1 && replacementDevice.HandshakeCount == 1,
                "ResetDevice should open and handshake a fresh connection instance");
            Assert(driver.Connected, "the driver should reconnect after rebuilding the connection");
        }
    }

    private static void TestPollingIsNonReentrant()
    {
        var device = new FakeDeviceConnection();
        using (var driver = new Driver(() => device))
        {
            driver.Connect();
            device.BlockPolling = true;

            var firstPoll = new Thread(driver.RefreshState);
            firstPoll.Start();
            Assert(device.PollEntered.WaitOne(1000), "first poll did not enter the device");

            var overlappingPoll = new Thread(driver.RefreshState);
            overlappingPoll.Start();
            Assert(overlappingPoll.Join(500), "overlapping poll waited behind the active poll");
            Assert(device.BlockedPollCount == 1, "more than one polling callback entered the device");

            device.ReleasePoll.Set();
            Assert(firstPoll.Join(1000), "first poll did not finish after release");
        }
    }

    private static void TestAutomaticRecovery()
    {
        var device = new FakeDeviceConnection();
        using (var driver = new Driver(() => device))
        {
            driver.Connect();
            device.PollsToFail = 2;

            driver.RefreshState();
            Assert(device.RecoveryCount == 0, "a single failed polling cycle should not reset the device");

            driver.RefreshState();
            Assert(device.RecoveryCount == 1, "two consecutive failed polling cycles should reset the device once");
            Assert(driver.Connected, "the driver should remain connected after successful recovery");
        }
    }

    private static void TestSecondaryServoCommandsAreAbsent()
    {
        var device = new FakeDeviceConnection();
        using (var driver = new Driver(() => device))
        {
            driver.Connect();
        }

        string[] secondaryCommands = { "vO", "vC", "VO", "VC", "K", "k" };
        foreach (string command in secondaryCommands)
        {
            Assert(!device.Commands.Contains(command),
                $"ASCOM driver sent the unsupported secondary-servo command {command}");
        }
    }

    private static void TestSetupDialogFitsHighDpiWorkingAreas()
    {
        using (var driver = new Driver(() => new FakeDeviceConnection(), milliseconds => { }))
        using (var dialog = new SetupDialogForm(driver))
        {
            Assert(dialog.AutoScaleMode == System.Windows.Forms.AutoScaleMode.Dpi,
                "setup dialog should use DPI scaling");
            Assert(dialog.AutoScroll, "setup dialog should make clipped controls scrollable");

            AssertDialogFits(dialog, new System.Drawing.Size(760, 503), new System.Drawing.Size(1920, 1040));
            AssertDialogFits(dialog, new System.Drawing.Size(1140, 755), new System.Drawing.Size(1366, 728));
            AssertDialogFits(dialog, new System.Drawing.Size(1520, 1006), new System.Drawing.Size(1920, 1040));
        }
    }

    private static void AssertDialogFits(
        SetupDialogForm dialog,
        System.Drawing.Size desired,
        System.Drawing.Size workingArea)
    {
        const int margin = 16;
        dialog.Size = desired;
        dialog.FitToWorkingArea(new System.Drawing.Rectangle(System.Drawing.Point.Empty, workingArea));

        Assert(dialog.Width <= workingArea.Width - (margin * 2),
            $"dialog width {dialog.Width} exceeds working area {workingArea.Width}");
        Assert(dialog.Height <= workingArea.Height - (margin * 2),
            $"dialog height {dialog.Height} exceeds working area {workingArea.Height}");
        Assert(dialog.Left >= 0 && dialog.Right <= workingArea.Width,
            "dialog should be horizontally reachable");
        Assert(dialog.Top >= 0 && dialog.Bottom <= workingArea.Height,
            "dialog should be vertically reachable");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FakeSerialPort : ISerialPortAdapter
    {
        private readonly Queue<char> _response;

        public FakeSerialPort(string response)
        {
            _response = response == null ? null : new Queue<char>(response);
        }

        public bool IsOpen { get; private set; }
        public int ReadTimeout { get; set; }
        public int WriteTimeout { get; set; }
        public string NewLine { get; set; }
        public bool RtsEnable { get; set; }
        public bool WasOpened { get; private set; }
        public bool WasDisposed { get; private set; }
        public bool ThrowOnOpen { get; set; }
        public List<bool> DtrValues { get; } = new List<bool>();
        public List<string> Writes { get; } = new List<string>();

        public bool DtrEnable
        {
            get => DtrValues.Count > 0 && DtrValues[DtrValues.Count - 1];
            set => DtrValues.Add(value);
        }

        public void Open()
        {
            if (ThrowOnOpen)
                throw new InvalidOperationException("simulated serial open failure");
            IsOpen = true;
            WasOpened = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        public void DiscardInBuffer()
        {
        }

        public void DiscardOutBuffer()
        {
        }

        public void Write(string value)
        {
            Writes.Add(value);
        }

        public int ReadChar()
        {
            if (_response == null || _response.Count == 0)
                throw new TimeoutException();
            return _response.Dequeue();
        }

        public void Dispose()
        {
            WasDisposed = true;
            IsOpen = false;
        }
    }

    private sealed class FakeDeviceConnection : IDeviceConnection, IRecoverableDeviceConnection
    {
        public bool IsOpen { get; private set; }
        public bool BlockPolling { get; set; }
        public int PollsToFail;
        public int RecoveryCount;
        public int BlockedPollCount;
        public bool HandshakeResult { get; set; } = true;
        public bool RecoverResult { get; set; } = true;
        public int OpenCount { get; private set; }
        public int HandshakeCount { get; private set; }
        public bool WasDisposed { get; private set; }
        public List<string> Commands { get; } = new List<string>();
        public ManualResetEvent PollEntered { get; } = new ManualResetEvent(false);
        public ManualResetEvent ReleasePoll { get; } = new ManualResetEvent(false);

        public void Open(string endpoint, int parameter)
        {
            OpenCount++;
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        public string SendCommand(string command)
        {
            Commands.Add(command);

            if (command == "P" && PollsToFail > 0)
            {
                Interlocked.Decrement(ref PollsToFail);
                return null;
            }

            if (command == "P" && BlockPolling)
            {
                Interlocked.Increment(ref BlockedPollCount);
                PollEntered.Set();
                ReleasePoll.WaitOne();
            }

            switch (command)
            {
                case "P": return "1";
                case "L": return "1";
                case "B": return "0";
                case "M": return "255";
                case "R": return "1";
                default: return "?";
            }
        }

        public bool Handshake()
        {
            HandshakeCount++;
            return HandshakeResult;
        }

        public bool TryRecover()
        {
            Interlocked.Increment(ref RecoveryCount);
            return RecoverResult;
        }

        public void Dispose()
        {
            WasDisposed = true;
            Close();
            PollEntered.Dispose();
            ReleasePoll.Dispose();
        }
    }
}
