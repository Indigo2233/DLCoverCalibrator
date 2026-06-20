using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace DarkLight.CoverCalibrator
{
    /// <summary>
    /// Gemini FlatPanel-style setup dialog with live servo jogging
    /// and one-click &quot;Set current as open/close&quot; angle configuration.
    /// </summary>
    public class SetupDialogForm : Form
    {
        private readonly Driver _driver;

        // ── State ──────────────────────────────────────────────────
        private int _primaryAngle;
        private int _secondaryAngle;

        // ── Controls: Serial ───────────────────────────────────────
        private ComboBox _cmbPort;
        private ComboBox _cmbBaud;

        // ── Controls: Panel 1 (Primary) ────────────────────────────
        private Label _lblPrimaryCloseVal;
        private Label _lblPrimaryOpenVal;

        // ── Controls: Panel 2 (Secondary) ──────────────────────────
        private Label _lblSecondaryCloseVal;
        private Label _lblSecondaryOpenVal;

        // ── Controls: Status ───────────────────────────────────────
        private Label _lblFirmware;
        private Label _lblConnected;

        // ── Colors ─────────────────────────────────────────────────
        private static readonly Color BgColor = Color.FromArgb(50, 50, 55);
        private static readonly Color PanelBg = Color.FromArgb(60, 60, 65);
        private static readonly Color TextColor = Color.FromArgb(220, 220, 220);
        private static readonly Color AccentColor = Color.FromArgb(70, 130, 220);
        private static readonly Color BtnBg = Color.FromArgb(80, 80, 85);
        private static readonly Color BtnHover = Color.FromArgb(100, 100, 105);

        public SetupDialogForm(Driver driver)
        {
            _driver = driver;
            _primaryAngle = _driver.PrimaryCloseAngle;
            _secondaryAngle = _driver.SecondaryCloseAngle;

            InitializeComponent();
            LoadValues();
            RefreshPorts();
            UpdateConnectionStatus();
        }

        private void InitializeComponent()
        {
            this.Text = "DLCover Calibrator \u2014 Configure";
            this.Size = new Size(620, 520);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = BgColor;
            this.ForeColor = TextColor;
            this.Font = new Font("Microsoft YaHei UI", 9F);

            int y = 10;

            // ── Serial Connection Bar ──────────────────────────────
            var pnlSerial = new Panel
            {
                Location = new Point(10, y), Size = new Size(590, 32), BackColor = PanelBg
            };
            this.Controls.Add(pnlSerial);

            pnlSerial.Controls.Add(new Label { Text = "COM:", ForeColor = TextColor, Location = new Point(10, 7), AutoSize = true });
            _cmbPort = new ComboBox { Location = new Point(52, 4), Width = 90, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor };
            pnlSerial.Controls.Add(_cmbPort);

            var btnRefresh = new Button { Text = "\u21bb", Location = new Point(145, 3), Width = 26, Height = 24, FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor };
            btnRefresh.Click += (s, e) => RefreshPorts();
            pnlSerial.Controls.Add(btnRefresh);

            pnlSerial.Controls.Add(new Label { Text = "Baud:", ForeColor = TextColor, Location = new Point(182, 7), AutoSize = true });
            _cmbBaud = new ComboBox { Location = new Point(225, 4), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor };
            _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" });
            pnlSerial.Controls.Add(_cmbBaud);

            _lblConnected = new Label { Text = "", Location = new Point(320, 7), AutoSize = true, ForeColor = Color.Gray };
            pnlSerial.Controls.Add(_lblConnected);

            _lblFirmware = new Label { Text = "", Location = new Point(460, 7), AutoSize = true, ForeColor = Color.FromArgb(150, 150, 150) };
            pnlSerial.Controls.Add(_lblFirmware);

            y += 42;

            // ── Panel 1 (Primary Servo) ────────────────────────────
            var grp1 = CreateServoPanel(1, 10, y, 590, 170);
            this.Controls.Add(grp1);
            y += 180;

            // ── Panel 2 (Secondary Servo) ──────────────────────────
            var grp2 = CreateServoPanel(2, 10, y, 590, 170);
            this.Controls.Add(grp2);
            y += 180;

            // ── Bottom Buttons ─────────────────────────────────────
            var btnReset = new Button
            {
                Text = "\u91cd\u8bbe", Location = new Point(380, y + 2), Size = new Size(90, 32),
                FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor
            };
            btnReset.Click += BtnReset_Click;
            this.Controls.Add(btnReset);

            var btnDone = new Button
            {
                Text = "Done", Location = new Point(480, y + 2), Size = new Size(90, 32),
                FlatStyle = FlatStyle.Flat, BackColor = AccentColor, ForeColor = Color.White,
                DialogResult = DialogResult.OK
            };
            btnDone.Click += BtnDone_Click;
            this.Controls.Add(btnDone);

            this.AcceptButton = btnDone;

            // Hover effects for all buttons
            foreach (Control c in GetAllControls(this))
                if (c is Button btn) AttachHover(btn);
        }

        private GroupBox CreateServoPanel(int index, int x, int y, int w, int h)
        {
            var grp = new GroupBox
            {
                Text = $"  {index}",
                Location = new Point(x, y), Size = new Size(w, h),
                BackColor = PanelBg, ForeColor = TextColor,
                Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold)
            };

            bool isPrimary = index == 1;
            int cy = 28;

            // ── Row: Close ─────────────────────────────────────────
            grp.Controls.Add(new Label
            {
                Text = "\u5173\u95ed\u65b9\u5411", Location = new Point(15, cy), AutoSize = true,
                ForeColor = Color.FromArgb(200, 160, 80), Font = new Font("Microsoft YaHei UI", 9F)
            });

            var lblCloseVal = new Label
            {
                Text = isPrimary ? $"{_driver.PrimaryCloseAngle}\u00b0" : $"{_driver.SecondaryCloseAngle}\u00b0",
                Location = new Point(105, cy - 1), AutoSize = true,
                ForeColor = TextColor, Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold)
            };
            grp.Controls.Add(lblCloseVal);
            if (isPrimary) _lblPrimaryCloseVal = lblCloseVal; else _lblSecondaryCloseVal = lblCloseVal;

            var btnSetClose = new Button
            {
                Text = "\u8bbe\u4e3a\u5173\u95ed\u4f4d\u7f6e", Location = new Point(195, cy - 4), Size = new Size(150, 28),
                FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor, Font = new Font("Microsoft YaHei UI", 8F)
            };
            btnSetClose.Click += (s, e) =>
            {
                if (!_driver.Connected) { WarnNotConnected(); return; }
                var pos = _driver.GetPrimaryPosition();
                if (isPrimary) { _driver.SetPrimaryCloseAngle(pos); _lblPrimaryCloseVal.Text = $"{pos}\u00b0"; }
                else { _driver.SetSecondaryCloseAngle(pos); _lblSecondaryCloseVal.Text = $"{pos}\u00b0"; }
            };
            AttachHover(btnSetClose);
            grp.Controls.Add(btnSetClose);

            cy += 32;
            AddJogButtons(grp, 15, cy, isPrimary, lblCloseVal, true);

            // ── Separator ─────────────────────────────────────────
            cy += 34;
            grp.Controls.Add(new Label
            {
                Text = new string('\u2500', 50), Location = new Point(15, cy), AutoSize = true,
                ForeColor = Color.FromArgb(90, 90, 95), Font = new Font("Consolas", 7F)
            });

            // ── Row: Open ─────────────────────────────────────────
            cy += 16;
            grp.Controls.Add(new Label
            {
                Text = "\u6253\u5f00\u65b9\u5411", Location = new Point(15, cy), AutoSize = true,
                ForeColor = Color.FromArgb(120, 200, 120), Font = new Font("Microsoft YaHei UI", 9F)
            });

            var lblOpenVal = new Label
            {
                Text = isPrimary ? $"{_driver.PrimaryOpenAngle}\u00b0" : $"{_driver.SecondaryOpenAngle}\u00b0",
                Location = new Point(105, cy - 1), AutoSize = true,
                ForeColor = TextColor, Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold)
            };
            grp.Controls.Add(lblOpenVal);
            if (isPrimary) _lblPrimaryOpenVal = lblOpenVal; else _lblSecondaryOpenVal = lblOpenVal;

            var btnSetOpen = new Button
            {
                Text = "\u8bbe\u4e3a\u6253\u5f00\u4f4d\u7f6e", Location = new Point(195, cy - 4), Size = new Size(150, 28),
                FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor, Font = new Font("Microsoft YaHei UI", 8F)
            };
            btnSetOpen.Click += (s, e) =>
            {
                if (!_driver.Connected) { WarnNotConnected(); return; }
                var pos = _driver.GetPrimaryPosition();
                if (isPrimary) { _driver.SetPrimaryOpenAngle(pos); _lblPrimaryOpenVal.Text = $"{pos}\u00b0"; }
                else { _driver.SetSecondaryOpenAngle(pos); _lblSecondaryOpenVal.Text = $"{pos}\u00b0"; }
            };
            AttachHover(btnSetOpen);
            grp.Controls.Add(btnSetOpen);

            cy += 32;
            AddJogButtons(grp, 15, cy, isPrimary, lblOpenVal, false);

            return grp;
        }

        private void AddJogButtons(GroupBox parent, int x, int y, bool isPrimary, Label valLabel, bool isClose)
        {
            int[] steps = { -45, -10, -1, 1, 10, 45 };
            int cx = x;

            foreach (var step in steps)
            {
                string text = step > 0 ? $"+{step}\u00b0" : $"{step}\u00b0";
                int w = (step == 1 || step == -1) ? 38 : 42;
                var btn = new Button
                {
                    Text = text, Location = new Point(cx, y), Size = new Size(w, 26),
                    FlatStyle = FlatStyle.Flat, BackColor = BtnBg, ForeColor = TextColor,
                    Font = new Font("Microsoft YaHei UI", 8F), Tag = step
                };
                btn.Click += (s, e) =>
                {
                    if (!_driver.Connected) { WarnNotConnected(); return; }

                    int delta = (int)((Button)s).Tag;
                    int current = isPrimary ? _driver.GetPrimaryPosition() : _driver.GetSecondaryPosition();
                    int newAngle = Clamp(current + delta, 0, 180);

                    if (isPrimary)
                        _driver.JogPrimary(newAngle);
                    else
                        _driver.JogSecondary(newAngle);

                    valLabel.Text = $"{newAngle}\u00b0";

                    if (isClose)
                    {
                        if (isPrimary) _driver.SetPrimaryCloseAngle(newAngle);
                        else _driver.SetSecondaryCloseAngle(newAngle);
                    }
                    else
                    {
                        if (isPrimary) _driver.SetPrimaryOpenAngle(newAngle);
                        else _driver.SetSecondaryOpenAngle(newAngle);
                    }
                };
                AttachHover(btn);
                parent.Controls.Add(btn);

                cx += w + 6;

                // Gear icon between -1 and +1
                if (step == -1)
                {
                    var gear = new Label
                    {
                        Text = "\u2699", Location = new Point(cx - 2, y + 2), AutoSize = true,
                        ForeColor = Color.FromArgb(120, 120, 130), Font = new Font("Segoe UI Symbol", 14F)
                    };
                    parent.Controls.Add(gear);
                    cx += 6;
                }
            }
        }

        private void LoadValues()
        {
            _cmbBaud.Text = _driver.BaudRate.ToString();
            _primaryAngle = _driver.PrimaryCloseAngle;
            _secondaryAngle = _driver.SecondaryCloseAngle;
        }

        private void RefreshPorts()
        {
            var current = _cmbPort.SelectedItem?.ToString();
            _cmbPort.Items.Clear();
            foreach (var port in SerialPort.GetPortNames())
                _cmbPort.Items.Add(port);
            if (_cmbPort.Items.Count > 0)
            {
                if (current != null && _cmbPort.Items.Contains(current))
                    _cmbPort.SelectedItem = current;
                else if (_cmbPort.Items.Contains(_driver.PortName))
                    _cmbPort.SelectedItem = _driver.PortName;
                else
                    _cmbPort.SelectedIndex = 0;
            }
        }

        public void UpdateConnectionStatus()
        {
            if (_driver.Connected)
            {
                _lblConnected.Text = "\u25cf \u5df2\u8fde\u63a5";
                _lblConnected.ForeColor = Color.FromArgb(100, 200, 100);
                var ver = _driver.CommandString("V");
                _lblFirmware.Text = $"FW: {ver}";
            }
            else
            {
                _lblConnected.Text = "\u25cb \u672a\u8fde\u63a5";
                _lblConnected.ForeColor = Color.Gray;
                _lblFirmware.Text = "";
            }
        }

        private void WarnNotConnected()
        {
            MessageBox.Show("\u8bf7\u5148\u8fde\u63a5\u8bbe\u5907\uff0c\u7136\u540e\u518d\u4f7f\u7528\u5b9e\u65f6\u8c03\u6574\u529f\u80fd\u3002",
                "\u672a\u8fde\u63a5", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void BtnReset_Click(object sender, EventArgs e)
        {
            var result = MessageBox.Show(
                "\u786e\u5b9a\u8981\u91cd\u7f6e\u4e3a\u9ed8\u8ba4\u503c\u5417\uff1f\n\n\u4e3b\u673a: \u5f00=0\u00b0 \u5173=180\u00b0\n\u526f\u673a: \u5f00=0\u00b0 \u5173=180\u00b0",
                "\u91cd\u8bbe", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

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
            _driver.PortName = _cmbPort.SelectedItem?.ToString() ?? "COM3";
            _driver.BaudRate = int.TryParse(_cmbBaud.Text, out int baud) ? baud : 115200;
            this.DialogResult = DialogResult.OK;
        }

        // ── Helpers ────────────────────────────────────────────────

        private void AttachHover(Button btn)
        {
            var orig = btn.BackColor;
            btn.MouseEnter += (s, e) => btn.BackColor = BtnHover;
            btn.MouseLeave += (s, e) => btn.BackColor = orig;
        }

        private static System.Collections.Generic.IEnumerable<Control> GetAllControls(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                yield return c;
                foreach (var child in GetAllControls(c))
                    yield return child;
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
