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

    private static int Main()
    {
        Run("DeviceTcp handshake, framing, and response parsing", TestTcpProtocol);
        Run("DeviceSerial recreates the port after a failed handshake", TestSerialHandshakeRecovery);
        Run("DeviceSerial handshake recovery is bounded", TestSerialHandshakeRecoveryIsBounded);
        Run("Driver state polling is non-reentrant", TestPollingIsNonReentrant);
        Run("Driver automatically recovers after consecutive polling failures", TestAutomaticRecovery);
        Run("ASCOM connection ignores secondary-servo commands", TestSecondaryServoCommandsAreAbsent);

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

        using (var device = new DeviceSerial(
            (portName, baudRate) => ports.Dequeue(),
            milliseconds => { }))
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
        public List<bool> DtrValues { get; } = new List<bool>();
        public List<string> Writes { get; } = new List<string>();

        public bool DtrEnable
        {
            get => DtrValues.Count > 0 && DtrValues[DtrValues.Count - 1];
            set => DtrValues.Add(value);
        }

        public void Open()
        {
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
        public List<string> Commands { get; } = new List<string>();
        public ManualResetEvent PollEntered { get; } = new ManualResetEvent(false);
        public ManualResetEvent ReleasePoll { get; } = new ManualResetEvent(false);

        public void Open(string endpoint, int parameter)
        {
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
            return true;
        }

        public bool TryRecover()
        {
            Interlocked.Increment(ref RecoveryCount);
            return true;
        }

        public void Dispose()
        {
            Close();
            PollEntered.Dispose();
            ReleasePoll.Dispose();
        }
    }
}
