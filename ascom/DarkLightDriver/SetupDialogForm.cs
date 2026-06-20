using System;
using System.Drawing;
using System.IO.Ports;
using System.Windows.Forms;

namespace DarkLight.CoverCalibrator
{
    /// <summary>
    /// Setup dialog for DarkLight Cover Calibrator ASCOM driver.
    /// Configures serial connection and servo open/close angles.
    /// </summary>
    public class SetupDialogForm : Form
    {
        private readonly Driver _driver;

        // ── Controls ──────────────────────────────────────────────
        private ComboBox _cmbPort;
        private ComboBox _cmbBaud;
        private NumericUpDown _numPrimaryOpen;
        private NumericUpDown _numPrimaryClose;
        private NumericUpDown _numSecondaryOpen;
        private NumericUpDown _numSecondaryClose;
        private NumericUpDown _numPollInterval;
        private Button _btnRefreshPorts;
        private Button _btnOK;
        private Button _btnCancel;
        private Label _lblStatus;

        public SetupDialogForm(Driver driver)
        {
            _driver = driver;
            InitializeComponent();
            LoadValues();
            RefreshPorts();
        }

        private void InitializeComponent()
        {
            this.Text = "DarkLight Cover Calibrator Setup";
            this.Size = new Size(480, 460);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Segoe UI", 9F);

            int y = 15;
            int leftCol = 20;
            int rightCol = 200;
            int spacing = 28;
            int controlWidth = 240;

            // ── Serial Port Section ──────────────────────────────
            var grpSerial = new GroupBox
            {
                Text = "Serial Connection",
                Location = new Point(10, y),
                Size = new Size(450, 95)
            };
            this.Controls.Add(grpSerial);
            y = 22;

            // Port
            grpSerial.Controls.Add(new Label { Text = "COM Port:", Location = new Point(leftCol, y), AutoSize = true });
            _cmbPort = new ComboBox { Location = new Point(rightCol, y - 2), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            grpSerial.Controls.Add(_cmbPort);

            _btnRefreshPorts = new Button { Text = "↻", Location = new Point(rightCol + 128, y - 3), Width = 30, Height = 23 };
            _btnRefreshPorts.Click += (s, e) => RefreshPorts();
            grpSerial.Controls.Add(_btnRefreshPorts);

            y += spacing;

            // Baud Rate
            grpSerial.Controls.Add(new Label { Text = "Baud Rate:", Location = new Point(leftCol, y), AutoSize = true });
            _cmbBaud = new ComboBox { Location = new Point(rightCol, y - 2), Width = 120, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbBaud.Items.AddRange(new object[] { "9600", "19200", "38400", "57600", "115200", "230400" });
            grpSerial.Controls.Add(_cmbBaud);

            y = 125;

            // ── Primary Servo Section ────────────────────────────
            var grpPrimary = new GroupBox
            {
                Text = "Primary Servo Angles (0–180°)",
                Location = new Point(10, y),
                Size = new Size(450, 80)
            };
            this.Controls.Add(grpPrimary);
            y = 22;

            // Open angle
            grpPrimary.Controls.Add(new Label { Text = "Open Angle:", Location = new Point(leftCol, y), AutoSize = true });
            _numPrimaryOpen = new NumericUpDown { Location = new Point(rightCol, y - 2), Width = 80, Minimum = 0, Maximum = 180 };
            grpPrimary.Controls.Add(_numPrimaryOpen);

            // Close angle
            grpPrimary.Controls.Add(new Label { Text = "Close Angle:", Location = new Point(rightCol + 100, y), AutoSize = true });
            _numPrimaryClose = new NumericUpDown { Location = new Point(rightCol + 175, y - 2), Width = 80, Minimum = 0, Maximum = 180 };
            grpPrimary.Controls.Add(_numPrimaryClose);

            y = 220;

            // ── Secondary Servo Section ──────────────────────────
            var grpSecondary = new GroupBox
            {
                Text = "Secondary Servo Angles (0–180°)",
                Location = new Point(10, y),
                Size = new Size(450, 80)
            };
            this.Controls.Add(grpSecondary);
            y = 22;

            // Open angle
            grpSecondary.Controls.Add(new Label { Text = "Open Angle:", Location = new Point(leftCol, y), AutoSize = true });
            _numSecondaryOpen = new NumericUpDown { Location = new Point(rightCol, y - 2), Width = 80, Minimum = 0, Maximum = 180 };
            grpSecondary.Controls.Add(_numSecondaryOpen);

            // Close angle
            grpSecondary.Controls.Add(new Label { Text = "Close Angle:", Location = new Point(rightCol + 100, y), AutoSize = true });
            _numSecondaryClose = new NumericUpDown { Location = new Point(rightCol + 175, y - 2), Width = 80, Minimum = 0, Maximum = 180 };
            grpSecondary.Controls.Add(_numSecondaryClose);

            y = 315;

            // ── Polling Section ───────────────────────────────────
            var grpPoll = new GroupBox
            {
                Text = "Status Polling",
                Location = new Point(10, y),
                Size = new Size(450, 55)
            };
            this.Controls.Add(grpPoll);
            y = 22;

            grpPoll.Controls.Add(new Label { Text = "Poll Interval (ms):", Location = new Point(leftCol, y), AutoSize = true });
            _numPollInterval = new NumericUpDown { Location = new Point(rightCol, y - 2), Width = 80, Minimum = 500, Maximum = 10000, Increment = 500 };
            grpPoll.Controls.Add(_numPollInterval);

            // ── Status Label ─────────────────────────────────────
            _lblStatus = new Label
            {
                Location = new Point(15, 385),
                Size = new Size(440, 20),
                ForeColor = Color.Gray,
                Text = "Changes are saved to driver profile immediately after clicking OK."
            };
            this.Controls.Add(_lblStatus);

            // ── Buttons ──────────────────────────────────────────
            _btnOK = new Button
            {
                Text = "OK",
                Location = new Point(280, 385),
                Size = new Size(75, 28),
                DialogResult = DialogResult.OK
            };
            _btnOK.Click += BtnOK_Click;
            this.Controls.Add(_btnOK);

            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(365, 385),
                Size = new Size(75, 28),
                DialogResult = DialogResult.Cancel
            };
            this.Controls.Add(_btnCancel);

            this.AcceptButton = _btnOK;
            this.CancelButton = _btnCancel;
        }

        private void LoadValues()
        {
            _cmbBaud.Text = _driver.BaudRate.ToString();
            _numPrimaryOpen.Value = _driver.PrimaryOpenAngle;
            _numPrimaryClose.Value = _driver.PrimaryCloseAngle;
            _numSecondaryOpen.Value = _driver.SecondaryOpenAngle;
            _numSecondaryClose.Value = _driver.SecondaryCloseAngle;
            _numPollInterval.Value = _driver.PollIntervalMs;
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
                    _cmbPort.SelectedItem = current;
                else if (_cmbPort.Items.Contains(_driver.PortName))
                    _cmbPort.SelectedItem = _driver.PortName;
                else
                    _cmbPort.SelectedIndex = 0;
            }
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            // Save all values to driver
            _driver.PortName = _cmbPort.SelectedItem?.ToString() ?? "COM3";
            _driver.BaudRate = int.TryParse(_cmbBaud.Text, out int baud) ? baud : 115200;
            _driver.SetPrimaryOpenAngle((int)_numPrimaryOpen.Value);
            _driver.SetPrimaryCloseAngle((int)_numPrimaryClose.Value);
            _driver.SetSecondaryOpenAngle((int)_numSecondaryOpen.Value);
            _driver.SetSecondaryCloseAngle((int)_numSecondaryClose.Value);
            _driver.PollIntervalMs = (int)_numPollInterval.Value;
        }
    }
}
