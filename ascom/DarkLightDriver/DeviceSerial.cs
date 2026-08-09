using System;
using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace DarkLight.CoverCalibrator
{
    public interface IDeviceConnection : IDisposable
    {
        bool IsOpen { get; }
        void Open(string endpoint, int parameter);
        void Close();
        string SendCommand(string command);
        bool Handshake();
    }

    internal interface IRecoverableDeviceConnection
    {
        bool TryRecover();
    }

    internal interface ISerialPortAdapter : IDisposable
    {
        bool IsOpen { get; }
        int ReadTimeout { get; set; }
        int WriteTimeout { get; set; }
        bool DtrEnable { get; set; }
        bool RtsEnable { get; set; }
        string NewLine { get; set; }
        void Open();
        void Close();
        void DiscardInBuffer();
        void DiscardOutBuffer();
        void Write(string value);
        int ReadChar();
    }

    internal sealed class SerialPortAdapter : ISerialPortAdapter
    {
        private readonly SerialPort _port;

        public SerialPortAdapter(string portName, int baudRate)
        {
            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
        }

        public bool IsOpen => _port.IsOpen;
        public int ReadTimeout { get => _port.ReadTimeout; set => _port.ReadTimeout = value; }
        public int WriteTimeout { get => _port.WriteTimeout; set => _port.WriteTimeout = value; }
        public bool DtrEnable { get => _port.DtrEnable; set => _port.DtrEnable = value; }
        public bool RtsEnable { get => _port.RtsEnable; set => _port.RtsEnable = value; }
        public string NewLine { get => _port.NewLine; set => _port.NewLine = value; }
        public void Open() => _port.Open();
        public void Close() => _port.Close();
        public void DiscardInBuffer() => _port.DiscardInBuffer();
        public void DiscardOutBuffer() => _port.DiscardOutBuffer();
        public void Write(string value) => _port.Write(value);
        public int ReadChar() => _port.ReadChar();
        public void Dispose() => _port.Dispose();
    }

    /// <summary>
    /// Manages serial communication with the DLC firmware.
    /// Protocol: commands are wrapped in &lt; &gt; delimiters.
    /// Example: &lt;O&gt; for open, &lt;P&gt; for poll cover state.
    /// </summary>
    public class DeviceSerial : IDeviceConnection, IRecoverableDeviceConnection
    {
        private const int ReadTimeoutMs = 5000;
        private const int WriteTimeoutMs = 2000;
        private const int MaxRetries = 3;
        private const int DtrPulseMs = 100;
        private const int PortRecoveryDelayMs = 1500;

        private readonly object _lock = new object();
        private readonly Func<string, int, ISerialPortAdapter> _portFactory;
        private readonly Action<int> _delay;
        private ISerialPortAdapter _serialPort;
        private string _currentPortName;
        private int _currentBaudRate;

        public DeviceSerial()
            : this((portName, baudRate) => new SerialPortAdapter(portName, baudRate), Thread.Sleep)
        {
        }

        internal DeviceSerial(
            Func<string, int, ISerialPortAdapter> portFactory,
            Action<int> delay)
        {
            _portFactory = portFactory ?? throw new ArgumentNullException(nameof(portFactory));
            _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        }

        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

        public void Open(string portName, int baudRate)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("A serial port name is required.", nameof(portName));

            lock (_lock)
            {
                _currentPortName = portName;
                _currentBaudRate = baudRate;
                ClosePortNoThrow();
                OpenPort();
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                ClosePortNoThrow();
            }
        }

        /// <summary>
        /// Send a command and return the response content without delimiters.
        /// Returns null after the bounded retry sequence fails.
        /// </summary>
        public string SendCommand(string command)
        {
            lock (_lock)
            {
                return SendCommand(command, MaxRetries);
            }
        }

        /// <summary>
        /// Sends one bounded handshake. If it fails, pulse DTR, fully release the
        /// old serial-port instance, reopen the configured port, and try once more.
        /// </summary>
        public bool Handshake()
        {
            lock (_lock)
            {
                if (SendCommand("Z", 1) == "?")
                    return true;

                return TryRecoverLocked();
            }
        }

        public bool TryRecover()
        {
            lock (_lock)
            {
                return TryRecoverLocked();
            }
        }

        private bool TryRecoverLocked()
        {
            if (string.IsNullOrWhiteSpace(_currentPortName))
                return false;

            PulseDtrNoThrow();
            ClosePortNoThrow();
            _delay(PortRecoveryDelayMs);

            try
            {
                OpenPort();
                return SendCommand("Z", 1) == "?";
            }
            catch
            {
                ClosePortNoThrow();
                return false;
            }
        }

        private void OpenPort()
        {
            var port = _portFactory(_currentPortName, _currentBaudRate);
            try
            {
                port.ReadTimeout = ReadTimeoutMs;
                port.WriteTimeout = WriteTimeoutMs;
                port.DtrEnable = false;
                port.RtsEnable = false;
                port.NewLine = "\n";
                port.Open();
                port.DiscardInBuffer();
                port.DiscardOutBuffer();
                _serialPort = port;
            }
            catch
            {
                try { port.Dispose(); } catch { }
                throw;
            }
        }

        private void PulseDtrNoThrow()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.DtrEnable = true;
                    _delay(DtrPulseMs);
                    _serialPort.DtrEnable = false;
                }
            }
            catch
            {
                // Recovery continues by fully releasing and rebuilding the port.
            }
        }

        private void ClosePortNoThrow()
        {
            var port = _serialPort;
            _serialPort = null;
            if (port == null)
                return;

            try { port.DtrEnable = false; } catch { }
            try { port.RtsEnable = false; } catch { }
            try
            {
                if (port.IsOpen)
                    port.Close();
            }
            catch { }
            try { port.Dispose(); } catch { }
        }

        private string SendCommand(string command, int maxRetries)
        {
            if (!IsOpen)
                return null;

            string fullCommand = $"<{command}>";
            for (int retry = 0; retry < maxRetries; retry++)
            {
                try
                {
                    _serialPort.DiscardInBuffer();
                    _serialPort.Write(fullCommand);

                    var response = ReadResponse();
                    if (response != null)
                        return response;
                }
                catch (TimeoutException)
                {
                    if (retry == maxRetries - 1)
                        return null;
                    _delay(200);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }

        private string ReadResponse()
        {
            var response = new StringBuilder();
            bool inResponse = false;
            int startTime = Environment.TickCount;

            while (unchecked(Environment.TickCount - startTime) < ReadTimeoutMs)
            {
                try
                {
                    char value = (char)_serialPort.ReadChar();
                    if (value == '<')
                    {
                        inResponse = true;
                        response.Clear();
                    }
                    else if (value == '>' && inResponse)
                    {
                        return response.ToString();
                    }
                    else if (inResponse)
                    {
                        response.Append(value);
                    }
                }
                catch (TimeoutException)
                {
                    break;
                }
            }

            return null;
        }

        public void Dispose()
        {
            Close();
        }
    }

    /// <summary>
    /// Manages TCP communication with the ESP8266 DLC firmware.
    /// Uses the same &lt;command&gt;/&lt;response&gt; framing as USB serial.
    /// </summary>
    public class DeviceTcp : IDeviceConnection, IRecoverableDeviceConnection
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _lock = new object();
        private const int ReadTimeoutMs = 5000;
        private const int WriteTimeoutMs = 2000;
        private const int ConnectTimeoutMs = 5000;
        private const int MaxRetries = 3;
        private const int RecoveryDelayMs = 500;
        private string _currentHost;
        private int _currentPort;

        public bool IsOpen => _client != null && _client.Connected && _stream != null;

        public void Open(string host, int port)
        {
            lock (_lock)
            {
                _currentHost = host;
                _currentPort = port;
                Close();
                _client = new TcpClient { NoDelay = true };
                var result = _client.BeginConnect(host, port, null, null);
                try
                {
                    if (!result.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
                        throw new TimeoutException($"Timed out connecting to {host}:{port}.");
                    _client.EndConnect(result);
                }
                finally
                {
                    result.AsyncWaitHandle.Close();
                }

                _stream = _client.GetStream();
                _stream.ReadTimeout = ReadTimeoutMs;
                _stream.WriteTimeout = WriteTimeoutMs;
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                try { _stream?.Close(); } catch { }
                try { _client?.Close(); } catch { }
                _stream = null;
                _client = null;
            }
        }

        public string SendCommand(string command)
        {
            lock (_lock)
            {
                if (!IsOpen) return null;
                byte[] payload = Encoding.ASCII.GetBytes($"<{command}>");

                for (int retry = 0; retry < MaxRetries; retry++)
                {
                    try
                    {
                        while (_stream.DataAvailable) _stream.ReadByte();
                        _stream.Write(payload, 0, payload.Length);
                        _stream.Flush();
                        var response = ReadResponse();
                        if (response != null) return response;
                    }
                    catch (Exception) when (retry < MaxRetries - 1)
                    {
                        Thread.Sleep(200);
                    }
                    catch
                    {
                        return null;
                    }
                }
                return null;
            }
        }

        private string ReadResponse()
        {
            var response = new StringBuilder();
            bool inResponse = false;
            while (true)
            {
                int value = _stream.ReadByte();
                if (value < 0) return null;
                char character = (char)value;
                if (character == '<')
                {
                    inResponse = true;
                    response.Clear();
                }
                else if (character == '>' && inResponse)
                {
                    return response.ToString();
                }
                else if (inResponse)
                {
                    response.Append(character);
                }
            }
        }

        public bool Handshake()
        {
            return SendCommand("Z") == "?";
        }

        public bool TryRecover()
        {
            lock (_lock)
            {
                if (string.IsNullOrWhiteSpace(_currentHost) || _currentPort <= 0)
                    return false;

                try
                {
                    Close();
                    Thread.Sleep(RecoveryDelayMs);
                    Open(_currentHost, _currentPort);
                    return Handshake();
                }
                catch
                {
                    Close();
                    return false;
                }
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
