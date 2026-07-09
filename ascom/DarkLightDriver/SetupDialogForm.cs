using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace DarkLight.CoverCalibrator
{
    public class SetupDialogForm : Form
    {
        private readonly Driver _driver;
        private bool _syncingBrightness;

        private ComboBox _cmbPort;
        private ComboBox _cmbBaud;
        private Button _btnConnect;
        private Label _lblFirmware;
        private Label _lblConnected;
        private Label _lblCoverState;
        private Label _lblCalibratorState;
        private Label _lblHeaterState;
        private Label _lblBrightness;
        private Label _lblSensor;
        private TrackBar _trkBrightness;
        private NumericUpDown _numBrightness;
        private Label _lblPrimaryCloseVal;
        private Label _lblPrimaryOpenVal;
        private Label _lblSecondaryCloseVal;
        private Label _lblSecondaryOpenVal;

        private static readonly Color BgColor = Color.FromArgb(45, 45, 50);
        private static readonly Color PanelBg = Color.FromArgb(55, 55, 60);
        private static readonly Color TextColor = Color.FromArgb(230, 230, 230);
        private static readonly Color MutedTextColor = Color.FromArgb(160, 160, 160);
        private static readonly Color AccentColor = Color.FromArgb(60, 120, 215);
        private static readonly Color BtnBg = Color.FromArgb(75, 75, 80);
        private static readonly Color BtnHover = Color.FromArgb(95, 95, 100);
        private static readonly Color OpenColor = Color.FromArgb(90, 185, 110);
        private static readonly Color CloseColor = Color.FromArgb(210, 150, 50);

        public SetupDialogForm(Driver driver)
        {
            _driver = driver;

            InitializeComponent();
            LoadValues();
            RefreshPorts();
            UpdateConnectionStatus();
            RefreshDeviceStatus();
        }

        private void InitializeComponent()
        {
            Text = "DarkLight Cover Calibrator — Setup";
            Size = new Size(760, 740);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ForeColor = TextColor;
            Font = new Font("Microsoft YaHei UI", 9.5F);

            int y = 12;

            // ── Serial Connection Bar ──
            var pnlSerial = new Panel
            {
                Location = new Point(12, y),
                Size = new Size(720, 40),
                BackColor = PanelBg,
                Padding = new Padding(10, 6, 10, 6)
            };
            Controls.Add(pnlSerial);

            pnlSerial.Controls.Add(MakeLabel("COM:", 10, 8, 40, 22, TextColor));
            _cmbPort = new ComboBox
            {
                Location = new Point(52, 8), Width = 88,
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg, ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            pnlSerial.Controls.Add(_cmbPort);

            var btnRefresh = MakeButton("\u21bb", 144, 7, 30, 24);
            btnRefresh.Click += (s, e) => RefreshPorts();
            pnlSerial.Controls.Add(btnRefresh);

            pnlSerial.Controls.Add(MakeLabel("Baud:", 184, 8, 40, 22, TextColor));
            _cmbBaud = new ComboBox
            {
                Location = new Point(226, 8), Width = 76,
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg, ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" });
            pnlSerial.Controls.Add(_cmbBaud);

            _btnConnect = MakeButton("\u8fde\u63a5", 314, 6, 80, 26);
            _btnConnect.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            _btnConnect.Click += BtnConnect_Click;
            pnlSerial.Controls.Add(_btnConnect);

            _lblConnected = MakeLabel("", 406, 8, 110, 22, MutedTextColor);
            _lblConnected.Font = new Font("Microsoft YaHei UI", 9.5F);
            pnlSerial.Controls.Add(_lblConnected);

            _lblFirmware = MakeLabel("", 526, 8, 180, 22, MutedTextColor);
            _lblFirmware.Font = new Font("Microsoft YaHei UI", 9F);
            _lblFirmware.TextAlign = ContentAlignment.MiddleRight;
            pnlSerial.Controls.Add(_lblFirmware);

            y += 52;

            // ── Device Control ──
            var grpControl = new GroupBox
            {
                Text = "  设备控制",
                Location = new Point(12, y),
                Size = new Size(720, 170),
                BackColor = PanelBg, ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            Controls.Add(grpControl);
            CreateDeviceControlPanel(grpControl);
            y += 184;

            // ── Primary Servo ──
            var grpPrimary = CreateServoPanel("主舵机角度", true, 12, y, 720, 150);
            Controls.Add(grpPrimary);
            y += 164;

            // ── Secondary Servo ──
            var grpSecondary = CreateServoPanel("副舵机角度", false, 12, y, 720, 150);
            Controls.Add(grpSecondary);
            y += 166;

            // ── Bottom Buttons ──
            var btnReset = MakeButton("重置默认角度", 12, y + 4, 120, 32);
            btnReset.Font = new Font("Microsoft YaHei UI", 9F);
            btnReset.Click += BtnReset_Click;
            Controls.Add(btnReset);

            var btnDone = MakeButton("保存并关闭", 612, y + 4, 120, 32);
            btnDone.BackColor = AccentColor;
            btnDone.ForeColor = Color.White;
            btnDone.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            btnDone.DialogResult = DialogResult.OK;
            btnDone.Click += BtnDone_Click;
            Controls.Add(btnDone);

            AcceptButton = btnDone;

            foreach (Control c in GetAllControls(this))
            {
                if (c is Button btn) AttachHover(btn);
            }
        }

        private void CreateDeviceControlPanel(GroupBox parent)
        {
            // ── Row 1: Cover Control ──
            parent.Controls.Add(MakeLabel("盖板", 18, 28, 48, 24, TextColor));
            _lblCoverState = MakeLabel("", 66, 28, 120, 24, MutedTextColor);
            _lblCoverState.Font = new Font("Microsoft YaHei UI", 9.5F);
            parent.Controls.Add(_lblCoverState);
            AddCmdBtn(parent, "开盖", 195, 26, 64, 30, "O");
            AddCmdBtn(parent, "关盖", 265, 26, 64, 30, "C");
            AddCmdBtn(parent, "停止", 335, 26, 64, 30, "H");

            // ── Row 2: Light Panel ──
            parent.Controls.Add(MakeLabel("平场灯", 18, 68, 56, 24, TextColor));
            _lblCalibratorState = MakeLabel("", 76, 68, 110, 24, MutedTextColor);
            _lblCalibratorState.Font = new Font("Microsoft YaHei UI", 9.5F);
            parent.Controls.Add(_lblCalibratorState);

            parent.Controls.Add(MakeLabel("亮度", 195, 66, 36, 20, TextColor));
            _trkBrightness = new TrackBar
            {
                Location = new Point(232, 62), Size = new Size(180, 38),
                Minimum = 0, Maximum = 255, TickFrequency = 51,
                BackColor = PanelBg
            };
            _trkBrightness.Scroll += BrightnessControl_Changed;
            parent.Controls.Add(_trkBrightness);

            _numBrightness = new NumericUpDown
            {
                Location = new Point(418, 65), Size = new Size(58, 22),
                Minimum = 0, Maximum = 255,
                BackColor = BtnBg, ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            _numBrightness.ValueChanged += BrightnessControl_Changed;
            parent.Controls.Add(_numBrightness);

            _lblBrightness = MakeLabel("", 480, 66, 56, 24, MutedTextColor);
            parent.Controls.Add(_lblBrightness);
            AddLightBtn(parent, "开灯", 542, 64, 48, 30, true);
            AddLightBtn(parent, "关灯", 596, 64, 48, 30, false);

            // ── Row 3: Heater ──
            parent.Controls.Add(MakeLabel("加热", 18, 112, 48, 24, TextColor));
            _lblHeaterState = MakeLabel("", 66, 112, 110, 24, MutedTextColor);
            _lblHeaterState.Font = new Font("Microsoft YaHei UI", 9.5F);
            parent.Controls.Add(_lblHeaterState);
            AddCmdBtn(parent, "手动开", 195, 110, 58, 28, "W");
            AddCmdBtn(parent, "关闭",  259, 110, 48, 28, "w");
            AddCmdBtn(parent, "自动开", 313, 110, 58, 28, "Q");
            AddCmdBtn(parent, "自动关", 377, 110, 58, 28, "q");
            AddCmdBtn(parent, "关盖开", 441, 110, 58, 28, "E");
            AddCmdBtn(parent, "关盖关", 505, 110, 58, 28, "e");

            var btnSensor = MakeButton("传感器", 570, 110, 60, 28);
            btnSensor.Click += BtnSensor_Click;
            parent.Controls.Add(btnSensor);

            _lblSensor = MakeLabel("", 195, 140, 510, 20, MutedTextColor);
            _lblSensor.Font = new Font("Microsoft YaHei UI", 8.5F);
            parent.Controls.Add(_lblSensor);
        }

        private GroupBox CreateServoPanel(string title, bool isPrimary, int x, int y, int w, int h)
        {
            var grp = new GroupBox
            {
                Text = "  " + title,
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = PanelBg, ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };

            CreateAngleRow(grp, isPrimary, false, 28);  // Open row
            CreateAngleRow(grp, isPrimary, true, 94);    // Close row
            return grp;
        }

        private void CreateAngleRow(GroupBox parent, bool isPrimary, bool isClose, int y)
        {
            string labelText = isClose ? "关闭位置" : "打开位置";
            Color accent = isClose ? CloseColor : OpenColor;

            // Label
            parent.Controls.Add(MakeLabel(labelText, 18, y + 4, 68, 22, accent));

            // Angle value display
            int currentAngle = GetSavedAngle(isPrimary, isClose);
            var valueLabel = MakeLabel($"{currentAngle}°", 92, y, 60, 30, TextColor);
            valueLabel.Font = new Font("Microsoft YaHei UI", 15F, FontStyle.Bold);
            valueLabel.TextAlign = ContentAlignment.MiddleLeft;
            parent.Controls.Add(valueLabel);

            if (isPrimary && isClose) _lblPrimaryCloseVal = valueLabel;
            if (isPrimary && !isClose) _lblPrimaryOpenVal = valueLabel;
            if (!isPrimary && isClose) _lblSecondaryCloseVal = valueLabel;
            if (!isPrimary && !isClose) _lblSecondaryOpenVal = valueLabel;

            // Save as Open/Close button
            var btnSet = MakeButton(isClose ? "保存为关闭" : "保存为打开", 165, y, 110, 30);
            btnSet.Font = new Font("Microsoft YaHei UI", 9F);
            btnSet.Click += (s, e) =>
            {
                if (!EnsureConnected()) return;
                int pos = isPrimary ? _driver.GetPrimaryPosition() : _driver.GetSecondaryPosition();
                SaveAngle(isPrimary, isClose, pos);
                valueLabel.Text = $"{pos}°";
            };
            parent.Controls.Add(btnSet);

            // Jog buttons
            AddJogButtons(parent, 295, y + 1, isPrimary, isClose, valueLabel);
        }

        private void AddJogButtons(Control parent, int x, int y, bool isPrimary, bool isClose, Label valLabel)
        {
            int[] steps = { -45, -10, -1, 1, 10, 45 };
            int cx = x;

            foreach (var step in steps)
            {
                string text = step > 0 ? $"+{step}" : $"{step}";
                int width = Math.Abs(step) >= 10 ? 48 : 42;
                var btn = MakeButton(text, cx, y, width, 28);
                btn.Tag = step;
                btn.Click += (s, e) =>
                {
                    if (!EnsureConnected()) return;

                    int delta = (int)((Button)s).Tag;
                    int current = isPrimary ? _driver.GetPrimaryPosition() : _driver.GetSecondaryPosition();
                    int newAngle = Clamp(current + delta, 0, Driver.MaxServoAngle);

                    if (isPrimary)
                        _driver.JogPrimary(newAngle);
                    else
                        _driver.JogSecondary(newAngle);

                    SaveAngle(isPrimary, isClose, newAngle);
                    valLabel.Text = $"{newAngle}°";
                };
                parent.Controls.Add(btn);
                cx += width + 10;
            }
        }

        private void LoadValues()
        {
            _cmbBaud.Text = _driver.BaudRate.ToString();
        }

        private void RefreshPorts()
        {
            var current = _cmbPort.SelectedItem?.ToString();
            _cmbPort.Items.Clear();
            foreach (var port in SerialPort.GetPortNames())
            {
                _cmbPort.Items.Add(port);
            }

            if (_cmbPort.Items.Count > 0)
            {
                if (current != null && _cmbPort.Items.Contains(current))
                {
                    _cmbPort.SelectedItem = current;
                }
                else if (_cmbPort.Items.Contains(_driver.PortName))
                {
                    _cmbPort.SelectedItem = _driver.PortName;
                }
                else
                {
                    _cmbPort.SelectedIndex = 0;
                }
            }
        }

        public void UpdateConnectionStatus()
        {
            bool connected = _driver.Connected;
            _btnConnect.Text = connected ? "\u65ad\u5f00" : "\u8fde\u63a5";
            _lblConnected.Text = connected ? "\u25cf \u5df2\u8fde\u63a5" : "\u25cb \u672a\u8fde\u63a5";
            _lblConnected.ForeColor = connected ? Color.FromArgb(100, 200, 100) : Color.Gray;

            if (connected)
            {
                try
                {
                    _lblFirmware.Text = $"FW: {_driver.CommandString("V")}";
                }
                catch
                {
                    _lblFirmware.Text = "";
                }
            }
            else
            {
                _lblFirmware.Text = "";
            }
        }

        private void RefreshDeviceStatus()
        {
            if (!_driver.Connected)
            {
                _lblCoverState.Text = "\u672a\u8fde\u63a5";
                _lblCalibratorState.Text = "\u672a\u8fde\u63a5";
                _lblHeaterState.Text = "\u672a\u8fde\u63a5";
                _lblBrightness.Text = "";
                return;
            }

            try
            {
                _driver.RefreshDeviceState();
                _lblCoverState.Text = _driver.CoverState.ToString();
                _lblCalibratorState.Text = _driver.CalibratorState.ToString();
                _lblHeaterState.Text = _driver.HeaterStateText;
                UpdateBrightnessControls(_driver.Brightness, _driver.MaxBrightness);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void UpdateBrightnessControls(int brightness, int maxBrightness)
        {
            int max = Math.Max(maxBrightness, 255);
            brightness = Clamp(brightness, 0, max);

            _syncingBrightness = true;
            _trkBrightness.Maximum = max;
            _numBrightness.Maximum = max;
            _trkBrightness.Value = Clamp(brightness, _trkBrightness.Minimum, _trkBrightness.Maximum);
            _numBrightness.Value = brightness;
            _lblBrightness.Text = $"/ {max}";
            _syncingBrightness = false;
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                if (_driver.Connected)
                {
                    _driver.Connected = false;
                }
                else
                {
                    _driver.PortName = _cmbPort.SelectedItem?.ToString() ?? _driver.PortName;
                    _driver.BaudRate = int.TryParse(_cmbBaud.Text, out int baud) ? baud : 115200;
                    _driver.Connected = true;
                }

                UpdateConnectionStatus();
                RefreshDeviceStatus();
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus();
                ShowError(ex);
            }
        }

        private void BrightnessControl_Changed(object sender, EventArgs e)
        {
            if (_syncingBrightness) return;

            _syncingBrightness = true;
            if (sender == _trkBrightness)
            {
                _numBrightness.Value = _trkBrightness.Value;
            }
            else
            {
                _trkBrightness.Value = Clamp((int)_numBrightness.Value, _trkBrightness.Minimum, _trkBrightness.Maximum);
            }
            _syncingBrightness = false;
        }

        private void AddCmdBtn(Control parent, string text, int x, int y, int w, int h, string command)
        {
            var button = MakeButton(text, x, y, w, h);
            button.Click += (s, e) => SendCommand(command);
            parent.Controls.Add(button);
        }

        private void AddLightBtn(Control parent, string text, int x, int y, int w, int h, bool turnOn)
        {
            var button = MakeButton(text, x, y, w, h);
            button.Click += (s, e) =>
            {
                if (!EnsureConnected()) return;
                string command = turnOn ? $"T{(int)_numBrightness.Value}" : "F";
                SendCommand(command);
            };
            parent.Controls.Add(button);
        }

        private void SendCommand(string command)
        {
            if (!EnsureConnected()) return;

            try
            {
                _driver.SendSetupCommand(command);
                RefreshDeviceStatus();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void BtnSensor_Click(object sender, EventArgs e)
        {
            if (!EnsureConnected()) return;

            try
            {
                _lblSensor.Text = _driver.QuerySensorDetails();
                RefreshDeviceStatus();
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "\u786e\u5b9a\u8981\u91cd\u7f6e\u4e3a\u9ed8\u8ba4\u503c\u5417\uff1f\n\n\u4e3b\u8235\u673a: \u5f00=0\u00b0 \u5173=180\u00b0\n\u526f\u8235\u673a: \u5f00=0\u00b0 \u5173=180\u00b0",
                "\u91cd\u7f6e\u89d2\u5ea6", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result == DialogResult.Yes)
            {
                _driver.SetPrimaryOpenAngle(0);
                _driver.SetPrimaryCloseAngle(180);
                _driver.SetSecondaryOpenAngle(0);
                _driver.SetSecondaryCloseAngle(180);
                _lblPrimaryOpenVal.Text = "0\u00b0";
                _lblPrimaryCloseVal.Text = "180\u00b0";
                _lblSecondaryOpenVal.Text = "0\u00b0";
                _lblSecondaryCloseVal.Text = "180\u00b0";
            }
        }

        private void BtnDone_Click(object sender, EventArgs e)
        {
            _driver.PortName = _cmbPort.SelectedItem?.ToString() ?? _driver.PortName;
            _driver.BaudRate = int.TryParse(_cmbBaud.Text, out int baud) ? baud : 115200;
            DialogResult = DialogResult.OK;
        }

        private bool EnsureConnected()
        {
            if (_driver.Connected) return true;
            MessageBox.Show(
                "\u8bf7\u5148\u5728\u9876\u90e8\u9009\u62e9 COM \u7aef\u53e3\u5e76\u70b9\u51fb\u8fde\u63a5\u3002",
                "\u672a\u8fde\u63a5", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private int GetSavedAngle(bool isPrimary, bool isClose)
        {
            if (isPrimary)
            {
                return isClose ? _driver.PrimaryCloseAngle : _driver.PrimaryOpenAngle;
            }

            return isClose ? _driver.SecondaryCloseAngle : _driver.SecondaryOpenAngle;
        }

        private void SaveAngle(bool isPrimary, bool isClose, int angle)
        {
            if (isPrimary && isClose) _driver.SetPrimaryCloseAngle(angle);
            if (isPrimary && !isClose) _driver.SetPrimaryOpenAngle(angle);
            if (!isPrimary && isClose) _driver.SetSecondaryCloseAngle(angle);
            if (!isPrimary && !isClose) _driver.SetSecondaryOpenAngle(angle);
        }

        private Button MakeButton(string text, int x, int y, int w, int h)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg,
                ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 9F),
                Margin = new Padding(0),
                Padding = new Padding(2)
            };
        }

        private Label MakeLabel(string text, int x, int y, int w, int h, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = color,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9.5F),
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
        }

        private void AttachHover(Button btn)
        {
            Color orig = btn.BackColor;
            btn.MouseEnter += (s, e) => btn.BackColor = BtnHover;
            btn.MouseLeave += (s, e) => btn.BackColor = orig;
        }

        private void ShowError(Exception ex)
        {
            MessageBox.Show(ex.Message, "\u64cd\u4f5c\u5931\u8d25", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static System.Collections.Generic.IEnumerable<Control> GetAllControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                yield return c;
                foreach (var child in GetAllControls(c))
                {
                    yield return child;
                }
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
