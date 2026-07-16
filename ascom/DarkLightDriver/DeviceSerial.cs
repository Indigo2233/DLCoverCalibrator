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

    /// <summary>
    /// Manages serial communication with the DLC firmware.
    /// Protocol: commands are wrapped in &lt; &gt; delimiters.
    /// Example: &lt;O&gt; for open, &lt;P&gt; for poll cover state.
    /// </summary>
    public class DeviceSerial : IDeviceConnection
    {
        private SerialPort _serialPort;
        private readonly object _lock = new object();
        private const int ReadTimeoutMs = 5000;
        private const int WriteTimeoutMs = 2000;
        private const int MaxRetries = 3;

        public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

        public DeviceSerial()
        {
        }

        public void Open(string portName, int baudRate)
        {
            lock (_lock)
            {
                Close();
                _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
                {
                    ReadTimeout = ReadTimeoutMs,
                    WriteTimeout = WriteTimeoutMs,
                    DtrEnable = false,
                    RtsEnable = false,
                    NewLine = "\n"
                };
                _serialPort.Open();
                // flush any stale data
                _serialPort.DiscardInBuffer();
                _serialPort.DiscardOutBuffer();
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                if (_serialPort != null)
                {
                    try
                    {
                        if (_serialPort.IsOpen)
                        {
                            _serialPort.DtrEnable = false;
                            _serialPort.RtsEnable = false;
                            _serialPort.Close();
                        }
                    }
                    catch { /* ignore */ }
                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }
        }

        /// <summary>
        /// Send a command and return the response content (without delimiters).
        /// Returns null on failure.
        /// </summary>
        public string SendCommand(string command)
        {
            lock (_lock)
            {
                if (!IsOpen)
                    return null;

                string fullCmd = $"<{command}>";

                for (int retry = 0; retry < MaxRetries; retry++)
                {
                    try
                    {
                        _serialPort.DiscardInBuffer();
                        _serialPort.Write(fullCmd);

                        // Read response: expect <response>
                        var response = ReadResponse();
                        if (response != null)
                            return response;
                    }
                    catch (TimeoutException)
                    {
                        // Retry
                        if (retry == MaxRetries - 1)
                            return null;
                        Thread.Sleep(200);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                }

                return null;
            }
        }

        private string ReadResponse()
        {
            var sb = new StringBuilder();
            bool inResponse = false;
            var startTime = Environment.TickCount;

            while ((Environment.TickCount - startTime) < ReadTimeoutMs)
            {
                try
                {
                    int b = _serialPort.ReadChar();
                    char c = (char)b;

                    if (c == '<')
                    {
                        inResponse = true;
                        sb.Clear();
                    }
                    else if (c == '>')
                    {
                        if (inResponse)
                            return sb.ToString();
                    }
                    else if (inResponse)
                    {
                        sb.Append(c);
                    }
                }
                catch (TimeoutException)
                {
                    break;
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        /// <summary>
        /// Handshake: send an unknown command ('Z'), expect '?' back.
        /// </summary>
        public bool Handshake()
        {
            var response = SendCommand("Z");
            return response != null && response == "?";
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
    public class DeviceTcp : IDeviceConnection
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private readonly object _lock = new object();
        private const int ReadTimeoutMs = 5000;
        private const int WriteTimeoutMs = 2000;
        private const int ConnectTimeoutMs = 5000;
        private const int MaxRetries = 3;

        public bool IsOpen => _client != null && _client.Connected && _stream != null;

        public void Open(string host, int port)
        {
            lock (_lock)
            {
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
                char c = (char)value;
                if (c == '<')
                {
                    inResponse = true;
                    response.Clear();
                }
                else if (c == '>' && inResponse)
                {
                    return response.ToString();
                }
                else if (inResponse)
                {
                    response.Append(c);
                }
            }
        }

        public bool Handshake()
        {
            return SendCommand("Z") == "?";
        }

        public void Dispose()
        {
            Close();
        }
    }
}
