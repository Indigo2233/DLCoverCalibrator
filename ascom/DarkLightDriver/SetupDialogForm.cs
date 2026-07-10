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
        private static readonly Color HeaderBg = Color.FromArgb(50, 50, 55);
        private static readonly Color TextColor = Color.FromArgb(236, 236, 236);
        private static readonly Color MutedTextColor = Color.FromArgb(172, 172, 172);
        private static readonly Color AccentColor = Color.FromArgb(60, 120, 215);
        private static readonly Color BtnBg = Color.FromArgb(75, 75, 80);
        private static readonly Color BtnHover = Color.FromArgb(95, 95, 100);
        private static readonly Color OpenColor = Color.FromArgb(90, 185, 110);
        private static readonly Color CloseColor = Color.FromArgb(210, 150, 50);

        public SetupDialogForm(Driver driver)
        {
            _driver = driver;
            if (_driver.Connected)
                _driver.ReadDeviceAngles();

            InitializeComponent();
            LoadValues();
            RefreshPorts();
            UpdateConnectionStatus();
            RefreshDeviceStatus();
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            AutoScaleMode = AutoScaleMode.Dpi;
            Text = "DarkLight Cover Calibrator — Setup";
            ClientSize = new Size(860, 620);
            MinimumSize = new Size(820, 560);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = BgColor;
            ForeColor = TextColor;
            Font = new Font("Microsoft YaHei UI", 10F);

            var scrollHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = BgColor,
                Padding = new Padding(10)
            };
            Controls.Add(scrollHost);

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = BgColor,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(0),
                Margin = new Padding(0)
            };
            main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (int i = 0; i < main.RowCount; i++)
            {
                main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }
            scrollHost.Controls.Add(main);

            main.Controls.Add(CreateSerialBar(), 0, 0);
            main.Controls.Add(CreateSection("设备控制", CreateDeviceControlPanel()), 0, 1);
            main.Controls.Add(CreateServoPanel("主舵机角度", true), 0, 2);
            main.Controls.Add(CreateServoPanel("副舵机角度", false), 0, 3);
            main.Controls.Add(CreateBottomBar(), 0, 4);

            AcceptButton = main.GetControlFromPosition(0, 4) is TableLayoutPanel bottom
                ? bottom.GetControlFromPosition(2, 0) as Button
                : null;

            foreach (Control c in GetAllControls(this))
            {
                if (c is Button btn) AttachHover(btn);
            }

            ResumeLayout(false);
        }

        private Control CreateSerialBar()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = PanelBg,
                ColumnCount = 8,
                RowCount = 1,
                Padding = new Padding(12, 10, 12, 10),
                Margin = new Padding(0, 0, 0, 8)
            };

            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            table.Controls.Add(MakeLabel("COM:", TextColor), 0, 0);

            _cmbPort = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BtnBg,
                ForeColor = TextColor,
                Font = Font,
                Margin = new Padding(4, 2, 4, 2)
            };
            table.Controls.Add(_cmbPort, 1, 0);

            var btnRefresh = MakeButton("↻", new Size(38, 34));
            btnRefresh.Click += (s, e) => RefreshPorts();
            table.Controls.Add(btnRefresh, 2, 0);

            table.Controls.Add(MakeLabel("Baud:", TextColor), 3, 0);

            _cmbBaud = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = BtnBg,
                ForeColor = TextColor,
                Font = Font,
                Margin = new Padding(4, 2, 8, 2)
            };
            _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" });
            table.Controls.Add(_cmbBaud, 4, 0);

            _btnConnect = MakeButton("连接", new Size(96, 34));
            _btnConnect.Font = new Font(Font, FontStyle.Bold);
            _btnConnect.Click += BtnConnect_Click;
            table.Controls.Add(_btnConnect, 5, 0);

            _lblConnected = MakeLabel("", MutedTextColor);
            table.Controls.Add(_lblConnected, 6, 0);

            _lblFirmware = MakeLabel("", MutedTextColor, ContentAlignment.MiddleRight);
            _lblFirmware.Font = new Font(Font.FontFamily, 9F);
            table.Controls.Add(_lblFirmware, 7, 0);

            return table;
        }

        private Control CreateDeviceControlPanel()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                BackColor = PanelBg,
                ColumnCount = 3,
                RowCount = 4,
                Height = 168,
                Padding = new Padding(10, 8, 10, 8),
                Margin = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));

            _lblCoverState = MakeStatusLabel();
            AddDeviceRow(table, 0, "盖板", _lblCoverState, CreateCoverButtons());

            _lblCalibratorState = MakeStatusLabel();
            AddDeviceRow(table, 1, "平场灯", _lblCalibratorState, CreateLightControls());

            _lblHeaterState = MakeStatusLabel();
            AddDeviceRow(table, 2, "加热", _lblHeaterState, CreateHeaterButtons());

            _lblSensor = MakeLabel("", MutedTextColor, ContentAlignment.MiddleLeft, new Size(520, 22));
            _lblSensor.Font = new Font(Font.FontFamily, 9F);
            table.Controls.Add(_lblSensor, 2, 3);

            return table;
        }

        private void AddDeviceRow(TableLayoutPanel table, int row, string title, Label status, Control controls)
        {
            table.Controls.Add(MakeLabel(title, TextColor), 0, row);
            table.Controls.Add(status, 1, row);
            table.Controls.Add(controls, 2, row);
        }

        private Control CreateCoverButtons()
        {
            var flow = MakeFlow();
            AddCmdBtn(flow, "开盖", "O");
            AddCmdBtn(flow, "关盖", "C");
            AddCmdBtn(flow, "停止", "H");
            return flow;
        }

        private Control CreateLightControls()
        {
            var flow = MakeFlow();
            flow.Controls.Add(MakeLabel("亮度", TextColor, ContentAlignment.MiddleLeft, new Size(48, 34)));

            _trkBrightness = new TrackBar
            {
                Size = new Size(220, 38),
                Minimum = 0,
                Maximum = 255,
                TickFrequency = 51,
                BackColor = PanelBg,
                Margin = new Padding(4, 0, 4, 0)
            };
            _trkBrightness.Scroll += BrightnessControl_Changed;
            flow.Controls.Add(_trkBrightness);

            _numBrightness = new NumericUpDown
            {
                Size = new Size(78, 34),
                Minimum = 0,
                Maximum = 255,
                BackColor = BtnBg,
                ForeColor = TextColor,
                Font = Font,
                TextAlign = HorizontalAlignment.Center,
                Margin = new Padding(4, 4, 4, 0)
            };
            _numBrightness.ValueChanged += BrightnessControl_Changed;
            flow.Controls.Add(_numBrightness);

            _lblBrightness = MakeLabel("", MutedTextColor, ContentAlignment.MiddleLeft, new Size(56, 34));
            flow.Controls.Add(_lblBrightness);

            AddLightBtn(flow, "开灯", true);
            AddLightBtn(flow, "关灯", false);

            return flow;
        }

        private Control CreateHeaterButtons()
        {
            var flow = MakeFlow();
            AddCmdBtn(flow, "手动开", "W", new Size(72, 32));
            AddCmdBtn(flow, "关闭", "w", new Size(64, 32));
            AddCmdBtn(flow, "自动开", "Q", new Size(72, 32));
            AddCmdBtn(flow, "自动关", "q", new Size(72, 32));
            AddCmdBtn(flow, "关盖开", "E", new Size(72, 32));
            AddCmdBtn(flow, "关盖关", "e", new Size(72, 32));

            var btnSensor = MakeButton("传感器", new Size(72, 32));
            btnSensor.Click += BtnSensor_Click;
            flow.Controls.Add(btnSensor);

            return flow;
        }

        private Control CreateServoPanel(string title, bool isPrimary)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = PanelBg,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(8, 6, 8, 6),
                Margin = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            table.Height = 92;

            CreateAngleRow(table, isPrimary, false, 0);
            CreateAngleRow(table, isPrimary, true, 1);

            return CreateSection(title, table);
        }

        private void CreateAngleRow(TableLayoutPanel parent, bool isPrimary, bool isClose, int row)
        {
            string labelText = isClose ? "关闭位置" : "打开位置";
            Color accent = isClose ? CloseColor : OpenColor;

            parent.Controls.Add(MakeLabel(labelText, accent), 0, row);

            int currentAngle = GetSavedAngle(isPrimary, isClose);
            var valueLabel = MakeLabel($"{currentAngle}°", TextColor, ContentAlignment.MiddleLeft, new Size(68, 32));
            valueLabel.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
            parent.Controls.Add(valueLabel, 1, row);

            if (isPrimary && isClose) _lblPrimaryCloseVal = valueLabel;
            if (isPrimary && !isClose) _lblPrimaryOpenVal = valueLabel;
            if (!isPrimary && isClose) _lblSecondaryCloseVal = valueLabel;
            if (!isPrimary && !isClose) _lblSecondaryOpenVal = valueLabel;

            var btnSet = MakeButton(isClose ? "保存为关闭" : "保存为打开", new Size(118, 30));
            btnSet.Click += (s, e) =>
            {
                if (!EnsureConnected()) return;
                int pos = ParseAngleLabel(valueLabel, GetSavedAngle(isPrimary, isClose));
                SaveAngle(isPrimary, isClose, pos);
                valueLabel.Text = $"{pos}°";
            };
            parent.Controls.Add(btnSet, 2, row);

            parent.Controls.Add(CreateJogButtons(isPrimary, valueLabel), 3, row);
        }

        private Control CreateJogButtons(bool isPrimary, Label valueLabel)
        {
            var flow = MakeFlow();
            int[] steps = { -45, -10, -1, 1, 10, 45 };

            foreach (var step in steps)
            {
                string text = step > 0 ? $"+{step}" : $"{step}";
                var btn = MakeButton(text, new Size(52, 30));
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

                    valueLabel.Text = $"{newAngle}°";
                };
                flow.Controls.Add(btn);
            }

            return flow;
        }

        private Control CreateBottomBar()
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = BgColor,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(0, 10, 0, 0),
                Margin = new Padding(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));

            var btnReset = MakeButton("重置默认角度", new Size(150, 38));
            btnReset.Click += BtnReset_Click;
            table.Controls.Add(btnReset, 0, 0);

            var spacer = new Label { Dock = DockStyle.Fill };
            table.Controls.Add(spacer, 1, 0);

            var btnDone = MakeButton("保存并关闭", new Size(150, 38));
            btnDone.BackColor = AccentColor;
            btnDone.ForeColor = Color.White;
            btnDone.Font = new Font(Font, FontStyle.Bold);
            btnDone.DialogResult = DialogResult.OK;
            btnDone.Click += BtnDone_Click;
            table.Controls.Add(btnDone, 2, 0);

            return table;
        }

        private Control CreateSection(string title, Control content)
        {
            var section = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = PanelBg,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(1),
                Margin = new Padding(0, 0, 0, 12)
            };
            section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            section.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var header = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = HeaderBg,
                ForeColor = TextColor,
                Font = new Font(Font.FontFamily, 10F, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0),
                Margin = new Padding(0)
            };
            section.Controls.Add(header, 0, 0);

            content.Dock = DockStyle.Top;
            section.Controls.Add(content, 0, 1);

            return section;
        }

        private FlowLayoutPanel MakeFlow()
        {
            return new FlowLayoutPanel
            {
                Dock = DockStyle.None,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = PanelBg,
                Margin = new Padding(0),
                Padding = new Padding(0),
                Anchor = AnchorStyles.Left | AnchorStyles.Top,
                WrapContents = false
            };
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
                UseWaitCursor = true;
                _btnConnect.Enabled = false;

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
                RefreshAngleLabels();
                RefreshDeviceStatus();
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus();
                ShowError(ex);
            }
            finally
            {
                _btnConnect.Enabled = true;
                UseWaitCursor = false;
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

        private void AddCmdBtn(Control parent, string text, string command)
        {
            AddCmdBtn(parent, text, command, new Size(70, 34));
        }

        private void AddCmdBtn(Control parent, string text, string command, Size size)
        {
            var button = MakeButton(text, size);
            button.Click += (s, e) => SendCommand(command);
            parent.Controls.Add(button);
        }

        private void AddLightBtn(Control parent, string text, bool turnOn)
        {
            var button = MakeButton(text, new Size(70, 34));
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

        private void RefreshAngleLabels()
        {
            if (_driver.Connected)
                _driver.ReadDeviceAngles();

            if (_lblPrimaryOpenVal != null) _lblPrimaryOpenVal.Text = $"{_driver.PrimaryOpenAngle}\u00b0";
            if (_lblPrimaryCloseVal != null) _lblPrimaryCloseVal.Text = $"{_driver.PrimaryCloseAngle}\u00b0";
            if (_lblSecondaryOpenVal != null) _lblSecondaryOpenVal.Text = $"{_driver.SecondaryOpenAngle}\u00b0";
            if (_lblSecondaryCloseVal != null) _lblSecondaryCloseVal.Text = $"{_driver.SecondaryCloseAngle}\u00b0";
        }

        private static int ParseAngleLabel(Label label, int fallback)
        {
            var text = (label.Text ?? string.Empty).Replace("\u00b0", string.Empty).Trim();
            return int.TryParse(text, out int angle) ? angle : fallback;
        }

        private void SaveAngle(bool isPrimary, bool isClose, int angle)
        {
            if (isPrimary && isClose) _driver.SetPrimaryCloseAngle(angle);
            if (isPrimary && !isClose) _driver.SetPrimaryOpenAngle(angle);
            if (!isPrimary && isClose) _driver.SetSecondaryCloseAngle(angle);
            if (!isPrimary && !isClose) _driver.SetSecondaryOpenAngle(angle);
        }

        private Button MakeButton(string text, Size size)
        {
            return new Button
            {
                Text = text,
                Size = size,
                MinimumSize = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = BtnBg,
                ForeColor = TextColor,
                Font = Font,
                Margin = new Padding(3),
                Padding = new Padding(3, 0, 3, 0),
                UseVisualStyleBackColor = false
            };
        }

        private Label MakeStatusLabel()
        {
            var label = MakeLabel("", MutedTextColor);
            label.Font = new Font(Font, FontStyle.Bold);
            return label;
        }

        private Label MakeLabel(string text, Color color)
        {
            return MakeLabel(text, color, ContentAlignment.MiddleLeft, new Size(0, 30));
        }

        private Label MakeLabel(string text, Color color, ContentAlignment align)
        {
            return MakeLabel(text, color, align, new Size(0, 30));
        }

        private Label MakeLabel(string text, Color color, ContentAlignment align, Size size)
        {
            return new Label
            {
                Text = text,
                Dock = size.Width == 0 ? DockStyle.Fill : DockStyle.None,
                Size = size.Width == 0 ? new Size(10, size.Height) : size,
                MinimumSize = size.Width == 0 ? new Size(0, size.Height) : size,
                ForeColor = color,
                AutoEllipsis = true,
                Font = Font,
                TextAlign = align,
                Margin = new Padding(2),
                Padding = new Padding(2, 0, 2, 0)
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
