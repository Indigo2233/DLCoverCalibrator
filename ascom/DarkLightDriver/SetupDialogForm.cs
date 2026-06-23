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

        private static readonly Color BgColor = Color.FromArgb(50, 50, 55);
        private static readonly Color PanelBg = Color.FromArgb(60, 60, 65);
        private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
        private static readonly Color MutedTextColor = Color.FromArgb(150, 150, 150);
        private static readonly Color AccentColor = Color.FromArgb(70, 130, 220);
        private static readonly Color BtnBg = Color.FromArgb(80, 80, 85);
        private static readonly Color BtnHover = Color.FromArgb(100, 100, 105);

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
            Text = "DLCover Calibrator - Configure";
            Size = new Size(720, 700);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ForeColor = TextColor;
            Font = new Font("Microsoft YaHei UI", 9F);

            int y = 10;

            var pnlSerial = new Panel
            {
                Location = new Point(10, y),
                Size = new Size(680, 36),
                BackColor = PanelBg
            };
            Controls.Add(pnlSerial);

            pnlSerial.Controls.Add(CreateLabel("COM:", 10, 9, 40, 18, TextColor));
            _cmbPort = new ComboBox
            {
                Location = new Point(52, 6),
                Width = 92,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg,
                ForeColor = TextColor
            };
            pnlSerial.Controls.Add(_cmbPort);

            var btnRefresh = CreateButton("\u21bb", 148, 5, 28, 26);
            btnRefresh.Click += (s, e) => RefreshPorts();
            pnlSerial.Controls.Add(btnRefresh);

            pnlSerial.Controls.Add(CreateLabel("Baud:", 190, 9, 45, 18, TextColor));
            _cmbBaud = new ComboBox
            {
                Location = new Point(238, 6),
                Width = 86,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg,
                ForeColor = TextColor
            };
            _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" });
            pnlSerial.Controls.Add(_cmbBaud);

            _btnConnect = CreateButton("\u8fde\u63a5", 340, 5, 72, 26);
            _btnConnect.Click += BtnConnect_Click;
            pnlSerial.Controls.Add(_btnConnect);

            _lblConnected = CreateLabel("", 426, 9, 100, 18, MutedTextColor);
            pnlSerial.Controls.Add(_lblConnected);

            _lblFirmware = CreateLabel("", 535, 9, 130, 18, MutedTextColor);
            pnlSerial.Controls.Add(_lblFirmware);

            y += 44;

            var grpControl = new GroupBox
            {
                Text = "  \u8bbe\u5907\u63a7\u5236",
                Location = new Point(10, y),
                Size = new Size(680, 158),
                BackColor = PanelBg,
                ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };
            Controls.Add(grpControl);
            CreateDeviceControlPanel(grpControl);
            y += 168;

            var grpPrimary = CreateServoPanel("\u4e3b\u8235\u673a\u89d2\u5ea6", true, 10, y, 680, 150);
            Controls.Add(grpPrimary);
            y += 160;

            var grpSecondary = CreateServoPanel("\u526f\u8235\u673a\u89d2\u5ea6", false, 10, y, 680, 150);
            Controls.Add(grpSecondary);
            y += 162;

            var btnReset = CreateButton("\u91cd\u7f6e\u89d2\u5ea6", 480, y, 92, 32);
            btnReset.Click += BtnReset_Click;
            Controls.Add(btnReset);

            var btnDone = CreateButton("Done", 584, y, 92, 32);
            btnDone.BackColor = AccentColor;
            btnDone.ForeColor = Color.White;
            btnDone.DialogResult = DialogResult.OK;
            btnDone.Click += BtnDone_Click;
            Controls.Add(btnDone);

            AcceptButton = btnDone;

            foreach (Control c in GetAllControls(this))
            {
                if (c is Button btn)
                {
                    AttachHover(btn);
                }
            }
        }

        private void CreateDeviceControlPanel(GroupBox parent)
        {
            parent.Controls.Add(CreateLabel("\u76d6\u677f", 15, 28, 54, 20, TextColor));
            _lblCoverState = CreateLabel("", 70, 28, 110, 20, MutedTextColor);
            parent.Controls.Add(_lblCoverState);
            AddCommandButton(parent, "\u5f00\u76d6", 190, 24, 70, 28, "O");
            AddCommandButton(parent, "\u5173\u76d6", 268, 24, 70, 28, "C");
            AddCommandButton(parent, "\u505c\u6b62", 346, 24, 70, 28, "H");

            parent.Controls.Add(CreateLabel("\u5e73\u573a\u706f", 15, 68, 64, 20, TextColor));
            _lblCalibratorState = CreateLabel("", 80, 68, 110, 20, MutedTextColor);
            parent.Controls.Add(_lblCalibratorState);

            parent.Controls.Add(CreateLabel("\u4eae\u5ea6", 190, 68, 42, 20, TextColor));
            _trkBrightness = new TrackBar
            {
                Location = new Point(232, 61),
                Size = new Size(210, 40),
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 25,
                BackColor = PanelBg
            };
            _trkBrightness.Scroll += BrightnessControl_Changed;
            parent.Controls.Add(_trkBrightness);

            _numBrightness = new NumericUpDown
            {
                Location = new Point(444, 66),
                Size = new Size(62, 22),
                Minimum = 0,
                Maximum = 255,
                BackColor = BtnBg,
                ForeColor = TextColor
            };
            _numBrightness.ValueChanged += BrightnessControl_Changed;
            parent.Controls.Add(_numBrightness);

            _lblBrightness = CreateLabel("", 512, 68, 60, 20, MutedTextColor);
            parent.Controls.Add(_lblBrightness);
            AddLightButton(parent, "\u5f00\u706f", 580, 62, 42, 28, true);
            AddLightButton(parent, "\u5173\u706f", 628, 62, 42, 28, false);

            parent.Controls.Add(CreateLabel("\u52a0\u70ed", 15, 110, 54, 20, TextColor));
            _lblHeaterState = CreateLabel("", 70, 110, 110, 20, MutedTextColor);
            parent.Controls.Add(_lblHeaterState);
            AddCommandButton(parent, "\u624b\u52a8\u5f00", 190, 104, 66, 28, "W");
            AddCommandButton(parent, "\u5173\u95ed", 262, 104, 54, 28, "w");
            AddCommandButton(parent, "\u81ea\u52a8\u5f00", 322, 104, 66, 28, "Q");
            AddCommandButton(parent, "\u81ea\u52a8\u5173", 394, 104, 66, 28, "q");
            AddCommandButton(parent, "\u5173\u76d6\u5f00", 466, 104, 66, 28, "E");
            AddCommandButton(parent, "\u5173\u76d6\u5173", 538, 104, 66, 28, "e");

            var btnSensor = CreateButton("\u4f20\u611f\u5668", 610, 104, 54, 28);
            btnSensor.Click += BtnSensor_Click;
            parent.Controls.Add(btnSensor);

            _lblSensor = CreateLabel("", 190, 132, 470, 18, MutedTextColor);
            parent.Controls.Add(_lblSensor);
        }

        private GroupBox CreateServoPanel(string title, bool isPrimary, int x, int y, int w, int h)
        {
            var grp = new GroupBox
            {
                Text = "  " + title,
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = PanelBg,
                ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold)
            };

            int rowY = 32;
            CreateAngleRow(grp, isPrimary, true, rowY);
            CreateAngleRow(grp, isPrimary, false, rowY + 62);
            return grp;
        }

        private void CreateAngleRow(GroupBox parent, bool isPrimary, bool isClose, int y)
        {
            string labelText = isClose ? "\u5173\u95ed\u4f4d\u7f6e" : "\u6253\u5f00\u4f4d\u7f6e";
            Color labelColor = isClose ? Color.FromArgb(200, 160, 80) : Color.FromArgb(120, 200, 120);
            parent.Controls.Add(CreateLabel(labelText, 15, y, 78, 22, labelColor));

            int currentAngle = GetSavedAngle(isPrimary, isClose);
            var valueLabel = CreateLabel($"{currentAngle}\u00b0", 105, y - 2, 70, 24, TextColor);
            valueLabel.Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold);
            parent.Controls.Add(valueLabel);

            if (isPrimary && isClose) _lblPrimaryCloseVal = valueLabel;
            if (isPrimary && !isClose) _lblPrimaryOpenVal = valueLabel;
            if (!isPrimary && isClose) _lblSecondaryCloseVal = valueLabel;
            if (!isPrimary && !isClose) _lblSecondaryOpenVal = valueLabel;

            var btnSet = CreateButton(isClose ? "\u4fdd\u5b58\u4e3a\u5173\u95ed" : "\u4fdd\u5b58\u4e3a\u6253\u5f00", 190, y - 4, 118, 28);
            btnSet.Click += (s, e) =>
            {
                if (!EnsureConnected()) return;
                int pos = isPrimary ? _driver.GetPrimaryPosition() : _driver.GetSecondaryPosition();
                SaveAngle(isPrimary, isClose, pos);
                valueLabel.Text = $"{pos}\u00b0";
            };
            parent.Controls.Add(btnSet);

            AddJogButtons(parent, 330, y - 3, isPrimary, isClose, valueLabel);
        }

        private void AddJogButtons(Control parent, int x, int y, bool isPrimary, bool isClose, Label valLabel)
        {
            int[] steps = { -45, -10, -1, 1, 10, 45 };
            int cx = x;

            foreach (var step in steps)
            {
                string text = step > 0 ? $"+{step}\u00b0" : $"{step}\u00b0";
                int width = Math.Abs(step) == 1 ? 42 : 46;
                var btn = CreateButton(text, cx, y, width, 28);
                btn.Tag = step;
                btn.Click += (s, e) =>
                {
                    if (!EnsureConnected()) return;

                    int delta = (int)((Button)s).Tag;
                    int current = isPrimary ? _driver.GetPrimaryPosition() : _driver.GetSecondaryPosition();
                    int newAngle = Clamp(current + delta, 0, Driver.MaxServoAngle);

                    if (isPrimary)
                    {
                        _driver.JogPrimary(newAngle);
                    }
                    else
                    {
                        _driver.JogSecondary(newAngle);
                    }

                    SaveAngle(isPrimary, isClose, newAngle);
                    valLabel.Text = $"{newAngle}\u00b0";
                };
                parent.Controls.Add(btn);
                cx += width + 6;
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

        private void AddCommandButton(Control parent, string text, int x, int y, int w, int h, string command)
        {
            var button = CreateButton(text, x, y, w, h);
            button.Click += (s, e) => SendCommand(command);
            parent.Controls.Add(button);
        }

        private void AddLightButton(Control parent, string text, int x, int y, int w, int h, bool turnOn)
        {
            var button = CreateButton(text, x, y, w, h);
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

        private Button CreateButton(string text, int x, int y, int w, int h)
        {
            return new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg,
                ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 8.5F)
            };
        }

        private Label CreateLabel(string text, int x, int y, int w, int h, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                ForeColor = color,
                AutoEllipsis = true,
                Font = new Font("Microsoft YaHei UI", 9F)
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
