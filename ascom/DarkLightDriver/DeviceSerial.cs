using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace DarkLight.CoverCalibrator
{
    /// <summary>
    /// Manages serial communication with the DLC firmware.
    /// Protocol: commands are wrapped in &lt; &gt; delimiters.
    /// Example: &lt;O&gt; for open, &lt;P&gt; for poll cover state.
    /// </summary>
    public class DeviceSerial : IDisposable
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
}
