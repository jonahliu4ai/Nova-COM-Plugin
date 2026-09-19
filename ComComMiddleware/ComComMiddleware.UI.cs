using System;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace ComComMiddleware
{
    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private readonly ProfileRepository _profiles = new ProfileRepository();
        private readonly ComGatewayService _gateway;

        private ComboBox _cmbComA;
        private ComboBox _cmbComB;
        private ComboBox _cmbBaudA;
        private ComboBox _cmbBaudB;
        private TextBox _txtConfigDir;
        private ListBox _lstProfile;
        private ListBox _lstLog;
        private TextBox _txtDevice;
        private TextBox _txtAddr;
        private TextBox _txtSend;

        private Button _btnConnect;
        private Button _btnReload;
        private Button _btnSend;
        private Label _lblStatus;

        public MainForm()
        {
            Text = "Nova COM2COM Middleware";
            Width = 1040;
            Height = 840;
            MinimumSize = new Size(860, 640);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;

            BuildUi();

            _gateway = new ComGatewayService(_profiles);
            _gateway.OnLog += AddLog;
            _gateway.OnReply += delegate(string reply)
            {
                AddLog("A <- " + reply);
            };
            _gateway.OnStatusChanged += delegate(string s)
            {
                SafeSetStatus(s);
            };
            _profiles.OnProfilesUpdated += delegate()
            {
                SafeRefreshProfileList();
                AddLog("Profiles reloaded: " + _profiles.Count);
            };

            RefreshPorts();
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string dir1 = Path.Combine(exeDir, "devices");
            string dir2 = Path.Combine(exeDir, "..", "..", "devices");
            string configDir = exeDir;

            if (Directory.Exists(dir1))
            {
                configDir = dir1;
            }
            else if (Directory.Exists(dir2))
            {
                configDir = Path.GetFullPath(dir2);
            }

            _txtConfigDir.Text = configDir;
            LoadProfiles();
            AddLog("Ready. Please set ports and config directory before start.");
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.RowCount = 4;
            root.ColumnCount = 1;
            root.Padding = new Padding(8);
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Row 0: top COM / config groups
            var topRow = BuildTopRow();
            root.Controls.Add(topRow, 0, 0);

            // Row 1: manual send
            var sendRow = BuildManualSendRow();
            root.Controls.Add(sendRow, 0, 1);

            // Row 2: profiles + log
            var middleRow = BuildMiddleRow();
            root.Controls.Add(middleRow, 0, 2);

            // Row 3: status
            _lblStatus = new Label();
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Height = 28;
            _lblStatus.Font = this.Font;
            _lblStatus.Text = "Stopped";
            _lblStatus.ForeColor = Color.DarkBlue;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            root.Controls.Add(_lblStatus, 0, 3);
        }

        private TableLayoutPanel BuildTopRow()
        {
            var top = new TableLayoutPanel();
            top.Dock = DockStyle.Fill;
            top.ColumnCount = 3;
            top.RowCount = 1;
            top.Padding = new Padding(0, 0, 0, 4);
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33f));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
            top.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            top.Height = 230;

            var gA = CreateGroup("Uplink (Nova) COM");
            var gB = CreateGroup("Downlink (Device) COM");
            var gCfg = CreateGroup("Config Directory");

            top.Controls.Add(gA, 0, 0);
            top.Controls.Add(gB, 1, 0);
            top.Controls.Add(gCfg, 2, 0);

            FillComGroup(gA, out _cmbComA, out _cmbBaudA, true);
            FillComGroup(gB, out _cmbComB, out _cmbBaudB, false);
            FillConfigGroup(gCfg);

            return top;
        }

        private void FillComGroup(GroupBox group, out ComboBox comBox, out ComboBox baudBox, bool withRefresh)
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 2;
            layout.RowCount = 4;
            layout.Padding = new Padding(6, 8, 6, 6);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            for (int i = 0; i < 4; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            group.Controls.Add(layout);

            comBox = CreateCombo();
            baudBox = CreateCombo(new string[] { "9600", "19200", "38400", "115200" });

            layout.Controls.Add(MakeLabel("COM"), 0, 0);
            layout.Controls.Add(comBox, 1, 0);
            layout.Controls.Add(MakeLabel("Baud"), 0, 1);
            layout.Controls.Add(baudBox, 1, 1);

            if (withRefresh)
            {
                var btnRefresh = CreateButton("Refresh", RefreshPorts);
                btnRefresh.Dock = DockStyle.Fill;
                btnRefresh.Margin = new Padding(2, 6, 2, 2);
                layout.Controls.Add(btnRefresh, 0, 3);
                layout.SetColumnSpan(btnRefresh, 2);
            }
        }

        private void FillConfigGroup(GroupBox group)
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 3;
            layout.RowCount = 4;
            layout.Padding = new Padding(6, 8, 6, 6);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            for (int i = 0; i < 4; i++)
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
            group.Controls.Add(layout);

            layout.Controls.Add(MakeLabel("Folder"), 0, 0);
            _txtConfigDir = new TextBox();
            _txtConfigDir.Dock = DockStyle.Fill;
            StyleTextInput(_txtConfigDir);
            layout.Controls.Add(_txtConfigDir, 0, 1);

            var btnBrowse = CreateButton("...", delegate()
            {
                using (var fd = new FolderBrowserDialog())
                {
                    if (Directory.Exists(_txtConfigDir.Text))
                    {
                        fd.SelectedPath = _txtConfigDir.Text;
                    }
                    if (fd.ShowDialog() == DialogResult.OK)
                    {
                        _txtConfigDir.Text = fd.SelectedPath;
                        LoadProfiles();
                    }
                }
            });
            btnBrowse.Dock = DockStyle.Fill;
            btnBrowse.Margin = new Padding(4, 0, 0, 0);
            layout.Controls.Add(btnBrowse, 1, 1);

            _btnReload = CreateButton("Load", LoadProfiles);
            _btnReload.Dock = DockStyle.Fill;
            _btnReload.Margin = new Padding(4, 0, 0, 0);
            layout.Controls.Add(_btnReload, 2, 1);

            var btnSamples = CreateButton("Samples", CreateSamples);
            btnSamples.Dock = DockStyle.Fill;
            btnSamples.Margin = new Padding(0, 6, 0, 0);
            layout.Controls.Add(btnSamples, 0, 2);

            _btnConnect = CreateButton("Start", ToggleConnect);
            _btnConnect.Dock = DockStyle.Fill;
            _btnConnect.Margin = new Padding(0, 6, 0, 0);
            layout.Controls.Add(_btnConnect, 0, 3);
            layout.SetColumnSpan(_btnConnect, 2);
        }

        private GroupBox BuildManualSendRow()
        {
            var group = CreateGroup("Manual Send");
            group.Dock = DockStyle.Fill;
            group.Height = 160;
            group.MinimumSize = new Size(0, 140);

            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.ColumnCount = 5;
            layout.RowCount = 3;
            layout.Padding = new Padding(6, 8, 6, 6);
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 82f));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            group.Controls.Add(layout);

            // row 0 labels
            layout.Controls.Add(MakeLabel("Device"), 1, 0);
            layout.Controls.Add(MakeLabel("Address"), 2, 0);
            layout.Controls.Add(MakeLabel("Command"), 3, 0);

            // row 1 inputs
            _txtDevice = new TextBox();
            _txtDevice.Dock = DockStyle.Fill;
            _txtDevice.Text = "Nova_AI708";
            StyleTextInput(_txtDevice);
            layout.Controls.Add(_txtDevice, 1, 1);

            _txtAddr = new TextBox();
            _txtAddr.Dock = DockStyle.Fill;
            _txtAddr.Width = 55;
            _txtAddr.Text = "1";
            StyleTextInput(_txtAddr);
            layout.Controls.Add(_txtAddr, 2, 1);

            _txtSend = new TextBox();
            _txtSend.Dock = DockStyle.Fill;
            _txtSend.Text = "set_sv 25.0";
            StyleTextInput(_txtSend);
            layout.Controls.Add(_txtSend, 3, 1);

            _btnSend = CreateButton("Send", SendManual);
            _btnSend.Dock = DockStyle.Fill;
            _btnSend.Margin = new Padding(4, 0, 0, 0);
            _btnSend.Width = 72;
            layout.Controls.Add(_btnSend, 4, 1);

            // row 2 hint
            var hint = new Label
            {
                Text = "DEVICE=<name>;ADDR=<addr>;CMD=<command> or @<name> <cmd> ...",
                Dock = DockStyle.Fill,
                ForeColor = Color.DimGray,
                Font = this.Font,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft
            };
            layout.Controls.Add(hint, 1, 2);
            layout.SetColumnSpan(hint, 4);

            return group;
        }

        private TableLayoutPanel BuildMiddleRow()
        {
            var middle = new TableLayoutPanel();
            middle.Dock = DockStyle.Fill;
            middle.ColumnCount = 2;
            middle.RowCount = 1;
            middle.Padding = new Padding(0, 4, 0, 4);
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32f));
            middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68f));
            middle.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var gList = CreateGroup("Loaded Profiles");
            var gLog = CreateGroup("Runtime Log");
            middle.Controls.Add(gList, 0, 0);
            middle.Controls.Add(gLog, 1, 0);

            _lstProfile = new ListBox();
            _lstProfile.Dock = DockStyle.Fill;
            StyleList(_lstProfile, new Font("Microsoft YaHei UI", 10f), 30);
            gList.Controls.Add(_lstProfile);

            var logPanel = new TableLayoutPanel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.ColumnCount = 1;
            logPanel.RowCount = 2;
            logPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            logPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            gLog.Controls.Add(logPanel);

            _lstLog = new ListBox();
            _lstLog.Dock = DockStyle.Fill;
            StyleList(_lstLog, new Font("Consolas", 10f), 25);
            logPanel.Controls.Add(_lstLog, 0, 0);

            var btnClear = CreateButton("Clear", delegate() { _lstLog.Items.Clear(); });
            btnClear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnClear.Width = 70;
            btnClear.Height = 26;
            btnClear.Margin = new Padding(0, 4, 0, 0);
            logPanel.Controls.Add(btnClear, 0, 1);

            return middle;
        }

        private void CreateSamples()
        {
            string dir = _txtConfigDir.Text;
            if (string.IsNullOrWhiteSpace(dir))
            {
                MessageBox.Show("Please set the config directory first");
                return;
            }

            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string p1 = Path.Combine(dir, "Sample_Modbus_Example.json");
            string p2 = Path.Combine(dir, "Sample_FixedFrame.json");
            string p3 = Path.Combine(dir, "Sample_Custom.json");
            string p4 = Path.Combine(dir, "Sample_AT.json");

            if (!File.Exists(p1)) File.WriteAllText(p1, SampleTemplates.Modbus, Encoding.UTF8);
            if (!File.Exists(p2)) File.WriteAllText(p2, SampleTemplates.Fixed, Encoding.UTF8);
            if (!File.Exists(p3)) File.WriteAllText(p3, SampleTemplates.Custom, Encoding.UTF8);
            if (!File.Exists(p4)) File.WriteAllText(p4, SampleTemplates.AT, Encoding.UTF8);

            AddLog("Generated sample files: " + dir);
            LoadProfiles();
        }

        private GroupBox CreateGroup(string title)
        {
            var g = new GroupBox();
            g.Text = title;
            g.Dock = DockStyle.Fill;
            g.Font = new Font("Microsoft YaHei UI", 11f);
            g.Padding = new Padding(6, 8, 6, 6);
            g.Margin = new Padding(4);
            return g;
        }

        private void StyleList(ListBox list, Font font, int itemHeight)
        {
            list.Font = font;
            list.IntegralHeight = false;
            list.ItemHeight = itemHeight;
            list.BorderStyle = BorderStyle.FixedSingle;
        }

        private Label MakeLabel(string text)
        {
            var l = new Label();
            l.Text = text;
            l.AutoSize = true;
            l.Font = new Font("Microsoft YaHei UI", 10f);
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Margin = new Padding(2, 6, 4, 2);
            return l;
        }

        private TextBox CreateTextInput(int width, int height)
        {
            var t = new TextBox();
            t.Width = width;
            t.Height = height;
            StyleTextInput(t);
            return t;
        }

        private void StyleTextInput(TextBox t)
        {
            t.Font = this.Font;
            t.Height = 28;
            t.BorderStyle = BorderStyle.Fixed3D;
            t.Multiline = false;
            t.AutoSize = false;
            t.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            t.TextAlign = HorizontalAlignment.Left;
        }

        private ComboBox CreateCombo()
        {
            return CreateCombo(new string[0]);
        }

        private ComboBox CreateCombo(string[] items)
        {
            var c = new ComboBox();
            c.Dock = DockStyle.Fill;
            c.IntegralHeight = false;
            c.Height = 28;
            c.Font = this.Font;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string s in items)
            {
                c.Items.Add(s);
            }
            return c;
        }

        private Button CreateButton(string text, Action click)
        {
            var b = new Button();
            b.Text = text;
            b.Height = 32;
            b.Font = this.Font;
            b.Click += delegate(object s, EventArgs e)
            {
                click();
            };
            return b;
        }

        private void RefreshPorts()
        {
            string[] ports = SerialPort.GetPortNames();
            if (ports.Length == 0)
            {
                ports = new string[] { "COM1" };
            }

            _cmbComA.Items.Clear();
            _cmbComB.Items.Clear();
            foreach (string p in ports)
            {
                _cmbComA.Items.Add(p);
                _cmbComB.Items.Add(p);
            }

            if (_cmbComA.Items.Count > 0)
            {
                _cmbComA.SelectedIndex = 0;
                _cmbComB.SelectedIndex = 0;
            }

            if (_cmbBaudA.Items.Count > 0) return;
            _cmbBaudA.Items.Add("9600");
            _cmbBaudA.Items.Add("19200");
            _cmbBaudA.Items.Add("38400");
            _cmbBaudA.Items.Add("115200");
            _cmbBaudB.Items.Add("9600");
            _cmbBaudB.Items.Add("19200");
            _cmbBaudB.Items.Add("38400");
            _cmbBaudB.Items.Add("115200");
            _cmbBaudA.SelectedIndex = 0;
            _cmbBaudB.SelectedIndex = 0;
        }

        private void LoadProfiles()
        {
            string d = _txtConfigDir.Text.Trim();
            if (string.IsNullOrWhiteSpace(d))
            {
                MessageBox.Show("Please fill in the config directory first");
                return;
            }

            _profiles.SetDirectory(d);
            _profiles.LoadAll();
            SafeRefreshProfileList();
        }

        private void SafeRefreshProfileList()
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { SafeRefreshProfileList(); });
                return;
            }

            _lstProfile.Items.Clear();
            foreach (string n in _profiles.ListProfiles())
            {
                _lstProfile.Items.Add(n);
            }

            if (_lstProfile.Items.Count > 0)
            {
                _lstProfile.SelectedIndex = 0;
            }
        }

        private void SafeSetStatus(string status)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { SafeSetStatus(status); });
                return;
            }
            _lblStatus.Text = status;
        }

        private void ToggleConnect()
        {
            if (!_gateway.IsRunning)
            {
                try
                {
                    var cfg = new GatewayRuntimeConfig();
                    cfg.ConfigDirectory = _txtConfigDir.Text.Trim();
                    cfg.ComA = _cmbComA.Text.Trim();
                    cfg.ComB = _cmbComB.Text.Trim();
                    cfg.BaudA = int.Parse(_cmbBaudA.Text);
                    cfg.BaudB = int.Parse(_cmbBaudB.Text);
                    cfg.ReadTimeoutMs = 800;

                    _gateway.Start(cfg);
                    _btnConnect.Text = "Stop";
                    AddLog("Gateway started: COM-A=" + cfg.ComA + " COM-B=" + cfg.ComB);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    AddLog("Start failed: " + ex.Message);
                }
            }
            else
            {
                _gateway.Stop();
                _btnConnect.Text = "Start";
                AddLog("Gateway stopped");
            }
        }

        private void SendManual()
        {
            if (!_gateway.IsRunning)
            {
                MessageBox.Show("Please start the gateway first");
                return;
            }

            string name = _txtDevice.Text.Trim();
            string addr = _txtAddr.Text.Trim();
            string cmd = _txtSend.Text.Trim();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd))
            {
                MessageBox.Show("Please fill in device name and command");
                return;
            }

            string line = string.Format("DEVICE={0};ADDR={1};CMD={2}", name, addr, cmd);
            _gateway.SubmitCommand(new ComCommand { RawLine = line, Source = "UI" });
        }

        private void AddLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke((MethodInvoker)delegate { AddLog(text); });
                return;
            }

            _lstLog.Items.Add(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
            _lstLog.TopIndex = Math.Max(0, _lstLog.Items.Count - 1);
        }
    }
}
