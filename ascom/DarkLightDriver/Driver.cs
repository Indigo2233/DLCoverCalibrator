using ASCOM;
using ASCOM.DeviceInterface;
using ASCOM.Utilities;
using System;
using System.Collections;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Timers;
using Timer = System.Timers.Timer;

namespace DarkLight.CoverCalibrator
{
    /// <summary>
    /// ASCOM CoverCalibrator V2 driver for DarkLight Cover Calibrator (DLC).
    /// Communicates with DLC firmware via USB serial or ESP8266 TCP port 4030.
    /// Supports configurable servo open/close angles.
    /// </summary>
    [Guid("D1E2F3A4-B5C6-4789-A0B1-C2D3E4F5A6B7")]
    [ProgId("DarkLight.CoverCalibrator")]
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    [ServedClassName("DarkLight Cover Calibrator")]
    public class Driver : ICoverCalibratorV2, IDisposable
    {
        // ── Driver identity ────────────────────────────────────────
        private const string DriverId = "DarkLight.CoverCalibrator";
        private const string DriverName = "DarkLight Cover Calibrator";
        private const string DriverDescription = "ASCOM driver for the DIY DarkLight Cover Calibrator";
        private const string DriverVersionString = "1.2.0";
        private const int MaxConnectionAttempts = 2;
        private const int ConnectionRetryDelayMs = 1500;
        private const int PollFailuresBeforeRecovery = 2;
        public const int MaxServoAngle = 270;

        // ── Internal state ─────────────────────────────────────────
        private IDeviceConnection _device;
        private Timer _pollTimer;
        private bool _connected;
        private CoverStatus _coverStatus = CoverStatus.Unknown;
        private CalibratorStatus _calibratorStatus = CalibratorStatus.Unknown;
        private int _heaterState = 4;
        private int _brightness;
        private int _maxBrightness;
        private int _primaryOpenAngle = 0;
        private int _primaryCloseAngle = 180;
        private int? _primaryCurrentPosition;
        private string _portName = "COM3";
        private int _baudRate = 115200;
        private string _transport = "Serial";
        private string _tcpHost = "192.168.4.1";
        private int _tcpPort = 4030;
        private int _pollIntervalMs = 1000;
        private bool _disposed;
        private bool _connecting;
        private readonly object _connectionLock = new object();
        private readonly Func<IDeviceConnection> _deviceFactory;
        private readonly Action<int> _connectionDelay = Thread.Sleep;
        private readonly bool _enablePolling = true;
        private int _refreshInProgress;
        private int _consecutivePollFailures;

        // ── ASCOM profile keys ─────────────────────────────────────
        private const string ProfilePortName = "PortName";
        private const string ProfileBaudRate = "BaudRate";
        private const string ProfileTransport = "Transport";
        private const string ProfileTcpHost = "TcpHost";
        private const string ProfileTcpPort = "TcpPort";
        private const string ProfilePollInterval = "PollIntervalMs";
        private const string ProfilePrimaryOpenAngle = "PrimaryOpenAngle";
        private const string ProfilePrimaryCloseAngle = "PrimaryCloseAngle";
        private const string TraceName = "DarkLight.CoverCalibrator";

        // ── ASCOM trace logger ─────────────────────────────────────
        private TraceLogger _tl;

        // ── Constructor ────────────────────────────────────────────
        public Driver()
        {
            _tl = new TraceLogger("", TraceName)
            {
                Enabled = true
            };
            ReadProfile();
            _device = CreateDeviceConnection();
            LogMessage("Driver", "Constructor complete");
        }

        internal Driver(Func<IDeviceConnection> deviceFactory, Action<int> connectionDelay = null)
        {
            _deviceFactory = deviceFactory ?? throw new ArgumentNullException(nameof(deviceFactory));
            _connectionDelay = connectionDelay ?? Thread.Sleep;
            _enablePolling = false;
        }

        // ────────────────────────────────────────────────────────────
        // ICoverCalibratorV2 Implementation
        // ────────────────────────────────────────────────────────────

        #region CoverCalibrator Properties

        public CoverStatus CoverState
        {
            get
            {
                LogMessage("CoverState", $"get → {_coverStatus}");
                return _coverStatus;
            }
        }

        public CalibratorStatus CalibratorState
        {
            get
            {
                LogMessage("CalibratorState", $"get → {_calibratorStatus}");
                return _calibratorStatus;
            }
        }

        public int Brightness
        {
            get
            {
                LogMessage("Brightness", $"get → {_brightness}");
                return _brightness;
            }
        }

        public int MaxBrightness
        {
            get
            {
                LogMessage("MaxBrightness", $"get → {_maxBrightness}");
                return _maxBrightness;
            }
        }

        #endregion

        #region CoverCalibrator Methods

        public void OpenCover()
        {
            LogMessage("OpenCover", "called");
            if (!CheckConnected("OpenCover")) return;

            if (_coverStatus == CoverStatus.Open || _coverStatus == CoverStatus.Moving)
                return;

            var resp = _device.SendCommand("O");
            LogMessage("OpenCover", $"device response: {resp}");

            // Wait for cover to finish moving (with timeout)
            WaitForCoverSettle(false);
            if (_coverStatus == CoverStatus.Open)
            {
                _primaryCurrentPosition = _primaryOpenAngle;
            }
        }

        public void CloseCover()
        {
            LogMessage("CloseCover", "called");
            if (!CheckConnected("CloseCover")) return;

            if (_coverStatus == CoverStatus.Closed || _coverStatus == CoverStatus.Moving)
                return;

            var resp = _device.SendCommand("C");
            LogMessage("CloseCover", $"device response: {resp}");

            // Wait for cover to finish moving (with timeout)
            WaitForCoverSettle(true);
            if (_coverStatus == CoverStatus.Closed)
            {
                _primaryCurrentPosition = _primaryCloseAngle;
            }
        }

        public void HaltCover()
        {
            LogMessage("HaltCover", "called");
            if (!CheckConnected("HaltCover")) return;

            if (_coverStatus != CoverStatus.Moving)
                return;

            var resp = _device.SendCommand("H");
            LogMessage("HaltCover", $"device response: {resp}");
        }

        public void CalibratorOn(int Brightness)
        {
            LogMessage("CalibratorOn", $"called with Brightness={Brightness}");
            if (!CheckConnected("CalibratorOn")) return;

            if (Brightness < 0 || Brightness > _maxBrightness)
                throw new InvalidValueException("CalibratorOn", Brightness.ToString(), $"0 to {_maxBrightness}");

            // Convert from ASCOM brightness (0-MaxBrightness) to device brightness steps
            int deviceValue = Brightness;
            var resp = _device.SendCommand($"T{deviceValue}");
            LogMessage("CalibratorOn", $"device response: {resp}");

            // Let the light stabilize then update state
            Thread.Sleep(300);
            RefreshState();
        }

        public void CalibratorOff()
        {
            LogMessage("CalibratorOff", "called");
            if (!CheckConnected("CalibratorOff")) return;

            var resp = _device.SendCommand("F");
            LogMessage("CalibratorOff", $"device response: {resp}");

            _calibratorStatus = CalibratorStatus.Off;
            _brightness = 0;
        }

        #endregion

        #region Driver Properties

        public bool Connected
        {
            get
            {
                LogMessage("Connected", $"get → {_connected}");
                return _connected;
            }
            set
            {
                LogMessage("Connected", $"set → {value}");
                if (value == _connected) return;

                if (value)
                {
                    Connect();
                }
                else
                {
                    Disconnect();
                }
                _connected = value;
            }
        }

        public string Description => DriverDescription;

        public string DriverInfo
        {
            get
            {
                var info = $"DarkLight Cover Calibrator Driver {DriverVersionString}\n" +
                          $"Connected: {_connected}\n" +
                          $"Port: {_portName} @ {_baudRate} baud\n" +
                          $"Open Angle: {_primaryOpenAngle}°  Close Angle: {_primaryCloseAngle}°";
                return info;
            }
        }

        public string DriverVersion => DriverVersionString;

        public short InterfaceVersion => 2;

        public string Name => DriverName;

        public bool Connecting => _connecting;

        public bool CalibratorChanging => _calibratorStatus == CalibratorStatus.NotReady;

        public bool CoverMoving => _coverStatus == CoverStatus.Moving;

        public IStateValueCollection DeviceState
        {
            get
            {
                var state = new StateValueCollection();
                state.Add("Connected", _connected);
                state.Add("Connecting", _connecting);
                state.Add("CoverState", _coverStatus);
                state.Add("CoverMoving", CoverMoving);
                state.Add("CalibratorState", _calibratorStatus);
                state.Add("CalibratorChanging", CalibratorChanging);
                state.Add("HeaterState", HeaterStateText);
                state.Add("Brightness", _brightness);
                state.Add("MaxBrightness", _maxBrightness);
                state.Add("PortName", _portName);
                state.Add("BaudRate", _baudRate);
                state.AddUtcDateTime();
                return state;
            }
        }

        #endregion

        #region Optional ASCOM methods

        private static readonly ArrayList _supportedActions = new ArrayList
        {
            "ResetDevice"
        };
        public ArrayList SupportedActions
        {
            get { return _supportedActions; }
        }

        public string Action(string ActionName, string ActionParameters)
        {
            LogMessage("Action", $"name={ActionName}, params={ActionParameters}");
            if (string.Equals(ActionName, "ResetDevice", StringComparison.OrdinalIgnoreCase))
                return ResetDevice();

            throw new ActionNotImplementedException("Action " + ActionName + " is not implemented by this driver");
        }

        public void CommandBlind(string Command, bool Raw = false)
        {
            CheckConnected("CommandBlind");
            LogMessage("CommandBlind", $"cmd={Command} raw={Raw}");
            _device.SendCommand(Command);
        }

        public bool CommandBool(string Command, bool Raw = false)
        {
            CheckConnected("CommandBool");
            LogMessage("CommandBool", $"cmd={Command} raw={Raw}");
            var resp = _device.SendCommand(Command);
            return resp != null && resp != "?";
        }

        public string CommandString(string Command, bool Raw = false)
        {
            CheckConnected("CommandString");
            LogMessage("CommandString", $"cmd={Command} raw={Raw}");
            var resp = _device.SendCommand(Command);
            return resp ?? "?";
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    Disconnect();
                    _device?.Dispose();
                    _tl?.Dispose();
                }
                _disposed = true;
            }
        }

        #endregion

        // ────────────────────────────────────────────────────────────
        // Connection Management
        // ────────────────────────────────────────────────────────────

        public void Connect()
        {
            lock (_connectionLock)
            {
                _connecting = true;
                try
                {
                    StopPolling();
                    _connected = false;

                    if (!OpenConnectionWithRecovery())
                    {
                        throw new ASCOM.NotConnectedException(
                            "Failed handshake with the DLC device after bounded recovery attempts. " +
                            "Check the configured endpoint and firmware. For a serial device, physically reconnect USB " +
                            "or disable and re-enable the CH340 device in Windows Device Manager.");
                    }

                    CompleteConnection();
                    StartPolling();
                    LogMessage("Connect", "Driver connected successfully");
                }
                catch
                {
                    _connected = false;
                    StopPolling();
                    try { _device?.Close(); } catch { }
                    ResetCachedState();
                    throw;
                }
                finally
                {
                    _connecting = false;
                }
            }
        }

        public void Disconnect()
        {
            lock (_connectionLock)
            {
                LogMessage("Disconnect", "called");
                _connected = false;
                StopPolling();
                try { _device?.Close(); } catch { }
                ResetCachedState();
            }
        }

        private string ResetDevice()
        {
            lock (_connectionLock)
            {
                LogMessage("ResetDevice", "Recovery requested");
                _connecting = true;
                try
                {
                    StopPolling();
                    _connected = false;

                    bool recovered = TryRecoverCurrentDevice();
                    if (!recovered)
                        recovered = OpenConnectionWithRecovery();

                    if (!recovered)
                    {
                        ResetCachedState();
                        return "Reset failed: the device did not answer after the serial port was released and rebuilt. " +
                               "Physically reconnect USB or disable and re-enable the CH340 device in Windows Device Manager.";
                    }

                    CompleteConnection();
                    StartPolling();
                    LogMessage("ResetDevice", "Recovery succeeded");
                    return "OK: device reset and reconnected.";
                }
                catch (Exception ex)
                {
                    _connected = false;
                    StopPolling();
                    try { _device?.Close(); } catch { }
                    ResetCachedState();
                    LogMessage("ResetDevice", $"Recovery error: {ex.Message}");
                    return "Reset failed: " + ex.Message;
                }
                finally
                {
                    _connecting = false;
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        // State Refresh (polled)
        // ────────────────────────────────────────────────────────────

        internal void RefreshState()
        {
            if (Interlocked.Exchange(ref _refreshInProgress, 1) != 0)
                return;

            try
            {
                lock (_connectionLock)
                {
                    if (!_connected)
                        return;

                    if (RefreshStateCore())
                    {
                        _consecutivePollFailures = 0;
                        return;
                    }

                    _consecutivePollFailures++;
                    LogMessage("RefreshState",
                        $"No response ({_consecutivePollFailures}/{PollFailuresBeforeRecovery})");
                    if (_consecutivePollFailures >= PollFailuresBeforeRecovery)
                        RecoverAfterPollingFailure();
                }
            }
            catch (Exception ex)
            {
                LogMessage("RefreshState", $"Error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInProgress, 0);
            }
        }

        private bool RefreshStateCore()
        {
            try
            {
                // Poll cover state
                var p = _device.SendCommand("P");
                if (p == null) return false;
                if (p != null && int.TryParse(p, out int coverVal))
                {
                    _coverStatus = coverVal switch
                    {
                        0 => CoverStatus.NotPresent,
                        1 => CoverStatus.Closed,
                        2 => CoverStatus.Moving,
                        3 => CoverStatus.Open,
                        5 => CoverStatus.Error,
                        _ => CoverStatus.Unknown
                    };
                }

                // Poll calibrator state
                var l = _device.SendCommand("L");
                if (l == null) return false;
                if (l != null && int.TryParse(l, out int calVal))
                {
                    _calibratorStatus = calVal switch
                    {
                        0 => CalibratorStatus.NotPresent,
                        1 => CalibratorStatus.Off,
                        2 => CalibratorStatus.NotReady,
                        3 => CalibratorStatus.Ready,
                        5 => CalibratorStatus.Error,
                        _ => CalibratorStatus.Unknown
                    };
                }

                // Poll brightness
                var b = _device.SendCommand("B");
                if (b == null) return false;
                if (b != null && int.TryParse(b, out int brightVal))
                {
                    _brightness = brightVal;
                }

                // Poll max brightness
                var m = _device.SendCommand("M");
                if (m == null) return false;
                if (m != null && int.TryParse(m, out int maxVal))
                {
                    _maxBrightness = maxVal;
                }

                var r = _device.SendCommand("R");
                if (r == null) return false;
                if (r != null && int.TryParse(r, out int heatVal))
                {
                    _heaterState = heatVal;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogMessage("RefreshStateCore", $"Error: {ex.Message}");
                return false;
            }
        }

        private void RecoverAfterPollingFailure()
        {
            LogMessage("AutoRecover", "Polling stopped responding; rebuilding the connection");
            StopPolling();
            _connected = false;

            bool recovered = TryRecoverCurrentDevice();
            if (!recovered)
                recovered = OpenConnectionWithRecovery();

            if (!recovered)
            {
                ResetCachedState();
                LogMessage("AutoRecover", "Recovery exhausted; driver is disconnected");
                return;
            }

            try
            {
                CompleteConnection();
                StartPolling();
                LogMessage("AutoRecover", "Connection restored");
            }
            catch (Exception ex)
            {
                _connected = false;
                StopPolling();
                try { _device?.Close(); } catch { }
                ResetCachedState();
                LogMessage("AutoRecover", $"Recovery initialization failed: {ex.Message}");
            }
        }

        private bool TryRecoverCurrentDevice()
        {
            return _device is IRecoverableDeviceConnection recoverable && recoverable.TryRecover();
        }

        private bool OpenConnectionWithRecovery()
        {
            string endpoint = IsTcpTransport ? _tcpHost : _portName;
            int parameter = IsTcpTransport ? _tcpPort : _baudRate;

            for (int attempt = 1; attempt <= MaxConnectionAttempts; attempt++)
            {
                try
                {
                    try { _device?.Dispose(); } catch { }
                    _device = CreateDeviceConnection();
                    LogMessage("Connect",
                        $"Opening {_transport} {endpoint}:{parameter} (attempt {attempt}/{MaxConnectionAttempts})");
                    _device.Open(endpoint, parameter);
                    if (_device.Handshake())
                    {
                        LogMessage("Connect", "Handshake successful");
                        return true;
                    }

                    LogMessage("Connect", $"Handshake failed on attempt {attempt}");
                }
                catch (Exception ex)
                {
                    LogMessage("Connect", $"Attempt {attempt} failed: {ex.Message}");
                }

                try { _device?.Close(); } catch { }
                if (attempt < MaxConnectionAttempts)
                    _connectionDelay(ConnectionRetryDelayMs);
            }

            return false;
        }

        private void CompleteConnection()
        {
            _connected = true;
            _consecutivePollFailures = 0;
            ReadDeviceAngles();
            if (!RefreshStateCore())
                LogMessage("Connect", "Connected, but the initial state refresh was incomplete");
        }

        private void StartPolling()
        {
            if (!_enablePolling || !_connected)
                return;

            StopPolling();
            _pollTimer = new Timer(_pollIntervalMs);
            _pollTimer.Elapsed += (s, e) => RefreshState();
            _pollTimer.AutoReset = true;
            _pollTimer.Start();
        }

        private void StopPolling()
        {
            var timer = _pollTimer;
            _pollTimer = null;
            if (timer == null)
                return;

            try { timer.Stop(); } catch { }
            try { timer.Dispose(); } catch { }
        }

        private void ResetCachedState()
        {
            _coverStatus = CoverStatus.Unknown;
            _calibratorStatus = CalibratorStatus.Unknown;
            _heaterState = 4;
            _primaryCurrentPosition = null;
        }

        // ────────────────────────────────────────────────────────────
        // Angle Management
        // ────────────────────────────────────────────────────────────

        public void ReadDeviceAngles()
        {
            if (!_connected) return;

            bool changed = false;

            changed |= TryReadDeviceAngle("uO", ref _primaryOpenAngle, "PrimaryOpenAngle");
            changed |= TryReadDeviceAngle("i", ref _primaryCloseAngle, "PrimaryCloseAngle");

            if (changed)
                WriteProfile();
        }

        private bool TryReadDeviceAngle(string command, ref int field, string name)
        {
            var response = _device.SendCommand(command);
            LogMessage("ReadDeviceAngles", $"{command} -> {response}");

            if (response == null || response == "?" || !int.TryParse(response, out int value))
                return false;

            value = Clamp(value, 0, MaxServoAngle);
            if (field == value)
                return false;

            field = value;
            LogMessage("ReadDeviceAngles", $"{name}={value}");
            return true;
        }

        public void SetPrimaryOpenAngle(int angle)
        {
            angle = Clamp(angle, 0, MaxServoAngle);
            _primaryOpenAngle = angle;
            if (_connected)
                _device.SendCommand($"UO{angle}");
            WriteProfile();
        }

        public void SetPrimaryCloseAngle(int angle)
        {
            angle = Clamp(angle, 0, MaxServoAngle);
            _primaryCloseAngle = angle;
            if (_connected)
                _device.SendCommand($"UC{angle}");
            WriteProfile();
        }

        public int PrimaryOpenAngle => _primaryOpenAngle;
        public int PrimaryCloseAngle => _primaryCloseAngle;

        public int HeaterState => _heaterState;

        public string HeaterStateText => FormatHeaterState(_heaterState);

        public void RefreshDeviceState()
        {
            RefreshState();
        }

        public string SendSetupCommand(string command)
        {
            CheckConnected("SendSetupCommand");
            var resp = _device.SendCommand(command);
            LogMessage("SendSetupCommand", $"cmd={command} resp={resp}");
            RefreshState();
            return resp ?? "?";
        }

        public string QuerySensorDetails()
        {
            CheckConnected("QuerySensorDetails");
            var resp = _device.SendCommand("Y");
            LogMessage("QuerySensorDetails", $"resp={resp}");
            return resp ?? "?";
        }

        /// <summary>Jog primary servo directly to a raw angle (0-270). Requires connected.</summary>
        public int JogPrimary(int angle)
        {
            angle = Clamp(angle, 0, MaxServoAngle);
            if (_connected)
            {
                HaltMotionBeforeJog();
                var resp = _device.SendCommand($"J{angle}");
                LogMessage("JogPrimary", $"angle={angle} resp={resp}");
                if (resp != null && resp != "?")
                    _primaryCurrentPosition = angle;
            }
            return angle;
        }

        /// <summary>Get primary servo current physical position. Requires connected.</summary>
        public int GetPrimaryPosition()
        {
            if (_primaryCurrentPosition.HasValue)
                return _primaryCurrentPosition.Value;
            if (!_connected) return _primaryCloseAngle;
            var resp = _device.SendCommand("j");
            if (resp != null && int.TryParse(resp, out int pos))
            {
                _primaryCurrentPosition = pos;
                return pos;
            }
            return _primaryCloseAngle;
        }

        /// <summary>Save current servo position as the new open angle.</summary>
        public void SetCurrentAsOpen()
        {
            if (!_connected) return;
            var pos = GetPrimaryPosition();
            SetPrimaryOpenAngle(pos);
            LogMessage("SetCurrentAsOpen", $"angle={pos}");
        }

        /// <summary>Save current servo position as the new close angle.</summary>
        public void SetCurrentAsClose()
        {
            if (!_connected) return;
            var pos = GetPrimaryPosition();
            SetPrimaryCloseAngle(pos);
            LogMessage("SetCurrentAsClose", $"angle={pos}");
        }

        public string PortName
        {
            get => _portName;
            set { _portName = value; WriteProfile(); }
        }

        public int BaudRate
        {
            get => _baudRate;
            set { _baudRate = value; WriteProfile(); }
        }

        public string Transport
        {
            get => _transport;
            set
            {
                _transport = string.Equals(value, "TCP", StringComparison.OrdinalIgnoreCase) ? "TCP" : "Serial";
                WriteProfile();
            }
        }

        public string TcpHost
        {
            get => _tcpHost;
            set { _tcpHost = string.IsNullOrWhiteSpace(value) ? "192.168.4.1" : value.Trim(); WriteProfile(); }
        }

        public int TcpPort
        {
            get => _tcpPort;
            set { _tcpPort = value > 0 && value <= 65535 ? value : 4030; WriteProfile(); }
        }

        private bool IsTcpTransport => string.Equals(_transport, "TCP", StringComparison.OrdinalIgnoreCase);

        private IDeviceConnection CreateDeviceConnection()
        {
            if (_deviceFactory != null)
                return _deviceFactory();

            return IsTcpTransport ? (IDeviceConnection)new DeviceTcp() : new DeviceSerial();
        }

        public int PollIntervalMs
        {
            get => _pollIntervalMs;
            set { _pollIntervalMs = value; WriteProfile(); }
        }

        // ────────────────────────────────────────────────────────────
        // ASCOM Profile (Registry) Persistence
        // ────────────────────────────────────────────────────────────

        private void ReadProfile()
        {
            using (var profile = new Profile())
            {
                profile.DeviceType = "CoverCalibrator";
                _portName = profile.GetValue(DriverId, ProfilePortName, string.Empty, "COM3");
                _baudRate = Convert.ToInt32(profile.GetValue(DriverId, ProfileBaudRate, string.Empty, "115200"), CultureInfo.InvariantCulture);
                _transport = profile.GetValue(DriverId, ProfileTransport, string.Empty, "Serial");
                _tcpHost = profile.GetValue(DriverId, ProfileTcpHost, string.Empty, "192.168.4.1");
                _tcpPort = Convert.ToInt32(profile.GetValue(DriverId, ProfileTcpPort, string.Empty, "4030"), CultureInfo.InvariantCulture);
                _pollIntervalMs = Convert.ToInt32(profile.GetValue(DriverId, ProfilePollInterval, string.Empty, "1000"), CultureInfo.InvariantCulture);
                _primaryOpenAngle = Convert.ToInt32(profile.GetValue(DriverId, ProfilePrimaryOpenAngle, string.Empty, "0"), CultureInfo.InvariantCulture);
                _primaryCloseAngle = Convert.ToInt32(profile.GetValue(DriverId, ProfilePrimaryCloseAngle, string.Empty, "180"), CultureInfo.InvariantCulture);
            }
            LogMessage("ReadProfile", $"Transport={_transport} Serial={_portName}@{_baudRate} TCP={_tcpHost}:{_tcpPort} PO={_primaryOpenAngle} PC={_primaryCloseAngle}");
        }

        public void WriteProfile()
        {
            using (var profile = new Profile())
            {
                profile.DeviceType = "CoverCalibrator";
                profile.WriteValue(DriverId, ProfilePortName, _portName);
                profile.WriteValue(DriverId, ProfileBaudRate, _baudRate.ToString(CultureInfo.InvariantCulture));
                profile.WriteValue(DriverId, ProfileTransport, _transport);
                profile.WriteValue(DriverId, ProfileTcpHost, _tcpHost);
                profile.WriteValue(DriverId, ProfileTcpPort, _tcpPort.ToString(CultureInfo.InvariantCulture));
                profile.WriteValue(DriverId, ProfilePollInterval, _pollIntervalMs.ToString(CultureInfo.InvariantCulture));
                profile.WriteValue(DriverId, ProfilePrimaryOpenAngle, _primaryOpenAngle.ToString(CultureInfo.InvariantCulture));
                profile.WriteValue(DriverId, ProfilePrimaryCloseAngle, _primaryCloseAngle.ToString(CultureInfo.InvariantCulture));
            }
            LogMessage("WriteProfile", "Profile saved");
        }

        // ────────────────────────────────────────────────────────────
        // Setup Dialog
        // ────────────────────────────────────────────────────────────

        public void SetupDialog()
        {
            LogMessage("SetupDialog", "Opening setup dialog");
            using (var dlg = new SetupDialogForm(this))
            {
                dlg.ShowDialog();
            }
            LogMessage("SetupDialog", "Closed, saving profile");
            WriteProfile();
        }

        // ────────────────────────────────────────────────────────────
        // Helpers
        // ────────────────────────────────────────────────────────────

        private bool CheckConnected(string method)
        {
            if (!_connected)
                throw new ASCOM.NotConnectedException($"{method}: Driver is not connected");
            return true;
        }

        private void HaltMotionBeforeJog()
        {
            if (_coverStatus != CoverStatus.Moving)
                return;

            var resp = _device.SendCommand("H");
            LogMessage("HaltMotionBeforeJog", $"device response: {resp}");
            _coverStatus = CoverStatus.Unknown;
            Thread.Sleep(50);
        }

        private void WaitForCoverSettle(bool closing)
        {
            var targetState = closing ? CoverStatus.Closed : CoverStatus.Open;
            int maxWaitMs = 30000; // 30 second timeout
            int elapsed = 0;
            const int stepMs = 500;

            while (elapsed < maxWaitMs)
            {
                Thread.Sleep(stepMs);
                elapsed += stepMs;
                RefreshState();

                if (_coverStatus == targetState)
                {
                    LogMessage("WaitForCoverSettle", $"Cover reached {targetState} in {elapsed}ms");
                    return;
                }
                if (_coverStatus == CoverStatus.Error)
                {
                    LogMessage("WaitForCoverSettle", "Cover reported error");
                    return;
                }
            }
            LogMessage("WaitForCoverSettle", $"Timeout waiting for cover to reach {targetState}");
        }

        private void LogMessage(string method, string message)
        {
            _tl?.LogMessage(method, message);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static string FormatHeaterState(int state)
        {
            switch (state)
            {
                case 0:
                    return "NotPresent";
                case 1:
                    return "Off";
                case 2:
                    return "Auto";
                case 3:
                    return "On";
                case 5:
                    return "Error";
                case 6:
                    return "HeatOnClose";
                default:
                    return "Unknown";
            }
        }

        // ────────────────────────────────────────────────────────────
        // COM Registration
        // ────────────────────────────────────────────────────────────

        #region COM Registration

        [ComRegisterFunction]
        public static void RegisterClass(string key)
        {
            using (var profile = new Profile())
            {
                profile.DeviceType = "CoverCalibrator";
                if (profile.IsRegistered("ASCOM.DarkLight.CoverCalibrator"))
                {
                    profile.Unregister("ASCOM.DarkLight.CoverCalibrator");
                }
                profile.Register(DriverId, DriverName);
            }
        }

        [ComUnregisterFunction]
        public static void UnregisterClass(string key)
        {
            using (var profile = new Profile())
            {
                profile.DeviceType = "CoverCalibrator";
                if (profile.IsRegistered(DriverId))
                {
                    profile.Unregister(DriverId);
                }
            }
        }

        #endregion
    }
}
