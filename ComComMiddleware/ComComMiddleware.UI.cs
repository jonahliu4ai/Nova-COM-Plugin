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
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.None;
            AutoScaleDimensions = new SizeF(96F, 96F);

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
            const int leftPad = 10;
            const int rowGap = 14;
            const int gWidth = 1010;

            var topPanel = new Panel();
            topPanel.Dock = DockStyle.Top;
            topPanel.Height = 250;
            topPanel.Padding = new Padding(0, 0, 0, 4);
            Controls.Add(topPanel);

            var gA = CreateGroup("Uplink (Nova) COM", leftPad, rowGap, 300, 190);
            gA.Controls.Add(MakeLabel("COM-A", 10, 10));
            _cmbComA = CreateCombo(120, 40);
            gA.Controls.Add(MakeLabel("Baud", 10, 80));
            _cmbBaudA = CreateCombo(120, 108, new string[] { "9600", "19200", "38400", "115200" });
            gA.Controls.Add(_cmbComA);
            gA.Controls.Add(_cmbBaudA);
            gA.Controls.Add(CreateButton("Refresh", 120, 150, 120, 36, RefreshPorts));

            var gB = CreateGroup("Downlink (Device) COM", 320, rowGap, 300, 190);
            gB.Controls.Add(MakeLabel("COM-B", 10, 10));
            _cmbComB = CreateCombo(120, 40);
            gB.Controls.Add(MakeLabel("Baud", 10, 80));
            _cmbBaudB = CreateCombo(120, 108, new string[] { "9600", "19200", "38400", "115200" });
            gB.Controls.Add(_cmbComB);
            gB.Controls.Add(_cmbBaudB);

            var gCfg = CreateGroup("Config Directory", 630, rowGap, 320, 190);
            gCfg.Controls.Add(MakeLabel("Folder", 10, 10));
            _txtConfigDir = new TextBox();
            _txtConfigDir.Left = 12;
            _txtConfigDir.Top = 40;
            _txtConfigDir.Width = 190;
            StyleTextInput(_txtConfigDir);
            _txtConfigDir.TextAlign = HorizontalAlignment.Left;
            var btnBrowse = CreateButton("...", 207, 40, 40, 34, delegate()
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
            _btnReload = CreateButton("Load", 250, 40, 60, 34, LoadProfiles);
            var btnSamples = CreateButton("Samples", 250, 80, 60, 34, CreateSamples);
            _btnConnect = CreateButton("Start", 10, 120, 120, 40, ToggleConnect);

            gCfg.Controls.Add(_txtConfigDir);
            gCfg.Controls.Add(btnBrowse);
            gCfg.Controls.Add(_btnReload);
            gCfg.Controls.Add(btnSamples);
            gCfg.Controls.Add(_btnConnect);

            topPanel.Controls.Add(gA);
            topPanel.Controls.Add(gB);
            topPanel.Controls.Add(gCfg);

            var gCmd = CreateGroup("Manual Send", 10, 270, gWidth, 190);
            gCmd.Controls.Add(MakeLabel("Device", 10, 10));
            _txtDevice = new TextBox();
            _txtDevice.Left = 15;
            _txtDevice.Top = 44;
            _txtDevice.Width = 160;
            StyleTextInput(_txtDevice);
            _txtDevice.Text = "Nova_AI708";

            gCmd.Controls.Add(MakeLabel("Address", 180, 10));
            _txtAddr = new TextBox();
            _txtAddr.Left = 185;
            _txtAddr.Top = 44;
            _txtAddr.Width = 55;
            StyleTextInput(_txtAddr);
            _txtAddr.Text = "1";

            gCmd.Controls.Add(MakeLabel("Command", 250, 10));

            int btnSendWidth = 72;
            int btnGap = 10;
            int sidePad = 12;
            int sendLeft = 255;
            int txtSendWidth = gCmd.Width - sendLeft - btnSendWidth - btnGap - sidePad;

            _txtSend = new TextBox();
            _txtSend.Left = sendLeft;
            _txtSend.Top = 44;
            _txtSend.Width = txtSendWidth;
            StyleTextInput(_txtSend);
            _txtSend.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
            _txtSend.Text = "set_sv 25.0";

            _btnSend = CreateButton("Send", gCmd.Width - sidePad - btnSendWidth, 44, btnSendWidth, 36, SendManual);

            gCmd.Controls.Add(_txtDevice);
            gCmd.Controls.Add(_txtAddr);
            gCmd.Controls.Add(_txtSend);
            gCmd.Controls.Add(_btnSend);
            gCmd.Controls.Add(new Label
            {
                Text = "DEVICE=<name>;ADDR=<addr>;CMD=<command> or @<name> <cmd> ...",
                Left = 15,
                Top = 96,
                Width = 940,
                Height = 24,
                ForeColor = Color.DimGray,
                Font = this.Font
            });
            Controls.Add(gCmd);

            var gList = CreateGroup("Loaded Profiles", 10, 470, 320, 300);
            _lstProfile = new ListBox();
            _lstProfile.Left = 10;
            _lstProfile.Top = 26;
            _lstProfile.Width = 290;
            _lstProfile.Height = 252;
            StyleList(_lstProfile, new Font("Microsoft YaHei UI", 10f), 30);
            gList.Controls.Add(_lstProfile);
            Controls.Add(gList);

            var gLog = CreateGroup("Runtime Log", 340, 470, 690, 300);
            _lstLog = new ListBox();
            _lstLog.Left = 10;
            _lstLog.Top = 26;
            _lstLog.Width = 666;
            _lstLog.Height = 252;
            StyleList(_lstLog, new Font("Consolas", 10f), 25);
            gLog.Controls.Add(_lstLog);

            var btnClear = CreateButton("Clear", 620, 0, 60, 22, delegate() { _lstLog.Items.Clear(); });
            gLog.Controls.Add(btnClear);
            Controls.Add(gLog);

            _lblStatus = new Label();
            _lblStatus.Left = 15;
            _lblStatus.Top = 785;
            _lblStatus.Width = 1010;
            _lblStatus.Height = 24;
            _lblStatus.Font = new Font("Microsoft YaHei UI", 10f);
            _lblStatus.Text = "Stopped";
            _lblStatus.ForeColor = Color.DarkBlue;
            _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(_lblStatus);
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

            if (!File.Exists(p1)) File.WriteAllText(p1, SampleTemplates.Modbus, Encoding.UTF8);
            if (!File.Exists(p2)) File.WriteAllText(p2, SampleTemplates.Fixed, Encoding.UTF8);
            if (!File.Exists(p3)) File.WriteAllText(p3, SampleTemplates.Custom, Encoding.UTF8);

            AddLog("Generated sample files: " + dir);
            LoadProfiles();
        }

        private GroupBox CreateGroup(string title, int x, int y, int w, int h)
        {
            var g = new GroupBox();
            g.Text = title;
            g.Left = x;
            g.Top = y;
            g.Width = w;
            g.Height = h;
            g.Font = new Font("Microsoft YaHei UI", 11f);
            g.Padding = new Padding(6, 8, 6, 6);
            return g;
        }

        private void StyleList(ListBox list, Font font, int itemHeight)
        {
            list.Font = font;
            list.IntegralHeight = false;
            list.ItemHeight = itemHeight;
            list.BorderStyle = BorderStyle.FixedSingle;
        }

        private Label MakeLabel(string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.Left = x;
            l.Top = y;
            l.Width = 72;
            l.Font = new Font("Microsoft YaHei UI", 11f);
            l.AutoSize = false;
            l.Height = 28;
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        private TextBox CreateTextInput(int width, int height)
        {
            var t = new TextBox();
            t.Width = width;
            t.Height = 28;
            StyleTextInput(t);
            return t;
        }

        private void StyleTextInput(TextBox t)
        {
            t.Font = this.Font;
            t.Height = 34;
            t.BorderStyle = BorderStyle.Fixed3D;
            t.Multiline = false;
            t.AutoSize = false;
            t.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            t.TextAlign = HorizontalAlignment.Left;
        }

        private ComboBox CreateCombo(int x, int y)
        {
            return CreateCombo(x, y, new string[0]);
        }

        private ComboBox CreateCombo(int x, int y, string[] items)
        {
            var c = new ComboBox();
            c.Left = x;
            c.Top = y;
            c.Width = 150;
            c.IntegralHeight = false;
            c.Height = 34;
            c.Font = this.Font;
            c.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (string s in items)
            {
                c.Items.Add(s);
            }
            return c;
        }

        private Button CreateButton(string text, int x, int y, int w, int h, Action click)
        {
            var b = new Button();
            b.Text = text;
            b.Left = x;
            b.Top = y;
            b.Width = w;
            b.Height = Math.Max(28, h);
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
