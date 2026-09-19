using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Linq;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Timers;

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
            _gateway.SubmitCommand(new NovaCommand { RawLine = line, Source = "UI" });
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

    public class GatewayRuntimeConfig
    {
        public string ComA;
        public int BaudA;
        public string ComB;
        public int BaudB;
        public string ConfigDirectory;
        public int ReadTimeoutMs;
    }

    public class NovaCommand
    {
        public string RawLine;
        public string Source;
        public bool RawMode;
        public byte[] RawBytes;
        public string DeviceName;
        public string Address;
        public string Command;
        public string Args;
    }

    public static class NovaCommandParser
    {
        public static NovaCommand Parse(string line)
        {
            if (line == null)
            {
                return null;
            }

            string txt = line.Trim();
            if (txt.Length == 0)
            {
                return null;
            }

            if (txt.StartsWith("RAW:", StringComparison.OrdinalIgnoreCase))
            {
                string body = txt.Substring(4).Trim();
                if (body.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
                {
                    return new NovaCommand
                    {
                        RawMode = true,
                        RawBytes = HexUtil.ParseHexBytes(body.Substring(4).Trim()),
                        RawLine = txt
                    };
                }
                if (body.StartsWith("TXT:", StringComparison.OrdinalIgnoreCase))
                {
                    return new NovaCommand
                    {
                        RawMode = true,
                        RawBytes = Encoding.UTF8.GetBytes(body.Substring(4).Trim()),
                        RawLine = txt
                    };
                }
                return null;
            }

            if (txt.StartsWith("DEVICE=", StringComparison.OrdinalIgnoreCase) || txt.IndexOf(";CMD=", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ParseKv(txt);
            }

            if (txt.StartsWith("@"))
            {
                return ParseAt(txt);
            }

            return null;
        }

        private static NovaCommand ParseKv(string txt)
        {
            string device = null;
            string addr = null;
            string cmd = null;

            string[] parts = txt.Split(new char[] { ';' }, StringSplitOptions.None);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i];
                int eq = p.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string k = p.Substring(0, eq).Trim().ToUpperInvariant();
                string v = p.Substring(eq + 1).Trim();
                if (k == "DEVICE") device = v;
                else if (k == "ADDR" || k == "ADDRESS") addr = v;
                else if (k == "CMD") cmd = v;
            }

            if (cmd == null) cmd = string.Empty;
            string cmdName = cmd;
            string args = string.Empty;

            int sp = cmd.IndexOf(' ');
            if (sp >= 0)
            {
                cmdName = cmd.Substring(0, sp);
                args = cmd.Substring(sp + 1);
            }

            return new NovaCommand
            {
                RawLine = txt,
                DeviceName = device,
                Address = addr,
                Command = cmdName,
                Args = args,
                Source = "KV"
            };
        }

        private static NovaCommand ParseAt(string txt)
        {
            if (txt.Length <= 1)
            {
                return null;
            }

            string work = txt.Substring(1);
            MatchCollection ms = Regex.Matches(work, "(\\S+)");
            if (ms.Count == 0)
            {
                return null;
            }

            string device = ms[0].Value;
            string addr = null;
            int cmdIndex = -1;
            var argsAfterCmd = new List<string>();

            for (int i = 1; i < ms.Count; i++)
            {
                string t = ms[i].Value;
                int eq = t.IndexOf('=');
                if (eq > 0)
                {
                    string k = t.Substring(0, eq).ToUpperInvariant();
                    string v = t.Substring(eq + 1);
                    if (k == "ADDR" || k == "ADDRESS" || k == "A")
                    {
                        addr = v;
                    }
                    else if (cmdIndex >= 0)
                    {
                        argsAfterCmd.Add(t);
                    }
                    continue;
                }

                if (cmdIndex < 0)
                {
                    cmdIndex = i;
                    continue;
                }
                argsAfterCmd.Add(t);
            }

            if (cmdIndex < 0)
            {
                return null;
            }

            string cmd = ms[cmdIndex].Value;
            string args = string.Empty;
            if (argsAfterCmd.Count > 0)
            {
                args = string.Join(" ", argsAfterCmd.ToArray());
            }

            return new NovaCommand
            {
                RawLine = txt,
                DeviceName = device,
                Address = addr,
                Command = cmd,
                Args = args,
                Source = "AT"
            };
        }
    }

    public class ComGatewayService
    {
        private readonly object _serialLock = new object();
        private readonly ProfileRepository _profiles;
        private BlockingCommandQueue _queue;
        private SerialPort _comA;
        private SerialPort _comB;
        private bool _running;
        private string _buffer = string.Empty;

        public event Action<string> OnLog;
        public event Action<string> OnReply;
        public event Action<string> OnStatusChanged;

        public ComGatewayService(ProfileRepository profiles)
        {
            _profiles = profiles;
            _queue = new BlockingCommandQueue(ProcessCommand);
        }

        public bool IsRunning
        {
            get
            {
                return _running;
            }
        }

        public void Start(GatewayRuntimeConfig cfg)
        {
            if (_running)
            {
                return;
            }

            StopInternal();

            if (string.IsNullOrWhiteSpace(cfg.ConfigDirectory) || !Directory.Exists(cfg.ConfigDirectory))
            {
                throw new Exception("Config directory not found: " + cfg.ConfigDirectory);
            }

            _profiles.SetDirectory(cfg.ConfigDirectory);
            _profiles.LoadAll();

            _comA = new SerialPort(cfg.ComA, cfg.BaudA, Parity.None, 8, StopBits.One);
            _comB = new SerialPort(cfg.ComB, cfg.BaudB, Parity.None, 8, StopBits.One);
            _comA.NewLine = "\n";
            _comA.ReadTimeout = cfg.ReadTimeoutMs;
            _comA.WriteTimeout = cfg.ReadTimeoutMs;
            _comB.ReadTimeout = cfg.ReadTimeoutMs;
            _comB.WriteTimeout = cfg.ReadTimeoutMs;

            _comA.Open();
            _comB.Open();
            _comA.DataReceived += OnDataFromNova;

            _running = true;
            _queue.Start();
            if (OnStatusChanged != null)
            {
                OnStatusChanged("Running");
            }
            Log("COM ports opened");
        }

        public void SubmitCommand(NovaCommand cmd)
        {
            if (!_running)
            {
                if (OnReply != null)
                {
                    OnReply("ERR:GatewayStopped");
                }
                return;
            }
            _queue.Enqueue(cmd);
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            StopInternal();
            if (OnStatusChanged != null)
            {
                OnStatusChanged("Stopped");
            }
            Log("Gateway stopped");
        }

        private void StopInternal()
        {
            _running = false;
            if (_queue != null)
            {
                _queue.Stop();
                _queue = new BlockingCommandQueue(ProcessCommand);
            }

            if (_comA != null)
            {
                try
                {
                    _comA.DataReceived -= OnDataFromNova;
                    if (_comA.IsOpen)
                    {
                        _comA.Close();
                    }
                }
                catch
                {
                }
                _comA = null;
            }

            if (_comB != null)
            {
                try
                {
                    if (_comB.IsOpen)
                    {
                        _comB.Close();
                    }
                }
                catch
                {
                }
                _comB = null;
            }
        }

        private void OnDataFromNova(object sender, SerialDataReceivedEventArgs e)
        {
            try
            {
                if (_comA == null || !_comA.IsOpen)
                {
                    return;
                }

                string chunk = _comA.ReadExisting();
                if (string.IsNullOrEmpty(chunk))
                {
                    return;
                }

                _buffer += chunk;
                while (true)
                {
                    int idx = _buffer.IndexOf('\n');
                    if (idx < 0)
                    {
                        break;
                    }
                    string line = _buffer.Substring(0, idx + 1);
                    _buffer = _buffer.Substring(idx + 1);
                    SubmitCommand(new NovaCommand { RawLine = line, Source = "COMA" });
                }
            }
            catch (Exception ex)
            {
                Log("Failed to read COM-A: " + ex.Message);
            }
        }

        private void ProcessCommand(NovaCommand cmd)
        {
            if (!_running || _comB == null || !_comB.IsOpen)
            {
                if (OnReply != null)
                {
                    OnReply("ERR:GatewayStopped");
                }
                return;
            }

            try
            {
                NovaCommand parsed = NovaCommandParser.Parse(cmd.RawLine);
                if (parsed == null)
                {
                    return;
                }

                if (parsed.RawMode)
                {
                    byte[] rx = SendRaw(parsed.RawBytes);
                    string reply = BuildRawReply(rx);
                    WriteToNova(reply);
                    return;
                }

                if (string.IsNullOrWhiteSpace(parsed.DeviceName))
                {
                    WriteToNova("ERR:NoDevice");
                    return;
                }

                DeviceProfile profile = _profiles.Get(parsed.DeviceName);
                if (profile == null)
                {
                    WriteToNova("ERR:ProfileNotFound:" + parsed.DeviceName);
                    Log("Profile not found: " + parsed.DeviceName);
                    return;
                }

                IProtocolEngine engine = ProtocolEngineFactory.Create(profile.protocol);
                if (engine == null)
                {
                    WriteToNova("ERR:UnsupportedProtocol:" + profile.protocol);
                    Log("Unsupported protocol: " + profile.protocol);
                    return;
                }

                int address = profile.default_address;
                if (!string.IsNullOrWhiteSpace(parsed.Address))
                {
                    int a;
                    if (int.TryParse(parsed.Address, out a))
                    {
                        address = a;
                    }
                }

                byte[] args = null;
                using (engine)
                {
                    engine.Init(profile, address, new SerialBus(_comB, _serialLock));
                    args = Encoding.UTF8.GetBytes(parsed.Args);
                    Log("CMD " + parsed.DeviceName + "(" + profile.protocol + ") " + parsed.Command + " " + parsed.Args);
                    string result = engine.Execute(parsed.Command, parsed.Args);
                    WriteToNova(string.IsNullOrEmpty(result) ? "OK" : result);
                }
            }
            catch (Exception ex)
            {
                Log("Command processing failed: " + ex.Message);
                WriteToNova("ERR:" + ex.Message);
            }
        }

        private byte[] SendRaw(byte[] data)
        {
            lock (_serialLock)
            {
                if (_comB == null || !_comB.IsOpen)
                {
                    throw new Exception("COM-B closed");
                }

                _comB.DiscardInBuffer();
                _comB.DiscardOutBuffer();
                if (data != null && data.Length > 0)
                {
                    _comB.Write(data, 0, data.Length);
                    Log("COMB <- " + HexUtil.ToHex(data));
                }

                Thread.Sleep(40);
                int n = _comB.BytesToRead;
                if (n <= 0)
                {
                    return new byte[0];
                }
                byte[] b = new byte[n];
                _comB.Read(b, 0, n);
                return b;
            }
        }

        private string BuildRawReply(byte[] resp)
        {
            if (resp == null || resp.Length == 0)
            {
                return "OK|NoRsp";
            }
            return "OK|" + HexUtil.ToHex(resp);
        }

        private void WriteToNova(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                if (_comA != null && _comA.IsOpen)
                {
                    _comA.WriteLine(text);
                }
            }
            catch (Exception ex)
            {
                Log("Failed to write COM-A: " + ex.Message);
            }

            if (OnReply != null)
            {
                OnReply(text);
            }
        }

        private void Log(string text)
        {
            if (OnLog != null)
            {
                OnLog(text);
            }
        }
    }

    public class ProfileRepository
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, DeviceProfile> _cache = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);
        private string _dir;
        private FileSystemWatcher _watch;
        private System.Timers.Timer _timer;

        public event Action OnProfilesUpdated;

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _cache.Count;
                }
            }
        }

        public void SetDirectory(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return;
            }

            if (_dir != null && string.Equals(_dir, dir, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _dir = dir;
            SetupWatcher();
        }

        public DeviceProfile Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            lock (_lock)
            {
                if (_cache.ContainsKey(name))
                {
                    return _cache[name];
                }

                string n = Path.GetFileNameWithoutExtension(name);
                if (_cache.ContainsKey(n))
                {
                    return _cache[n];
                }
            }
            return null;
        }

        public IEnumerable<string> ListProfiles()
        {
            lock (_lock)
            {
                return _cache.Keys.OrderBy(k => k).ToList();
            }
        }

        public void LoadAll()
        {
            if (string.IsNullOrWhiteSpace(_dir) || !Directory.Exists(_dir))
            {
                return;
            }

            var next = new Dictionary<string, DeviceProfile>(StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(_dir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                try
                {
                    string json = File.ReadAllText(file, Encoding.UTF8);
                    object root = JsonLite.Parse(json);
                    Dictionary<string, object> obj = JsonLite.AsObj(root);
                    DeviceProfile profile = JsonLite.MapProfile(obj);
                    if (profile == null)
                    {
                        continue;
                    }

                    string key = string.IsNullOrWhiteSpace(profile.name) ? Path.GetFileNameWithoutExtension(file) : profile.name;
                    next[key] = profile;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Load profile failed: " + file + ":" + ex.Message);
                }
            }

            lock (_lock)
            {
                _cache.Clear();
                foreach (KeyValuePair<string, DeviceProfile> kv in next)
                {
                    _cache[kv.Key] = kv.Value;
                }
            }

            if (OnProfilesUpdated != null)
            {
                OnProfilesUpdated();
            }
        }

        private void SetupWatcher()
        {
            if (_watch != null)
            {
                _watch.EnableRaisingEvents = false;
                _watch.Dispose();
                _watch = null;
            }

            if (string.IsNullOrWhiteSpace(_dir) || !Directory.Exists(_dir))
            {
                return;
            }

            _watch = new FileSystemWatcher(_dir, "*.json");
            _watch.IncludeSubdirectories = false;
            _watch.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
            _watch.Changed += delegate(object s, FileSystemEventArgs e) { DebounceReload(); };
            _watch.Created += delegate(object s, FileSystemEventArgs e) { DebounceReload(); };
            _watch.Deleted += delegate(object s, FileSystemEventArgs e) { DebounceReload(); };
            _watch.Renamed += delegate(object s, RenamedEventArgs e) { DebounceReload(); };
            _watch.EnableRaisingEvents = true;
        }

        private void DebounceReload()
        {
            if (_timer == null)
            {
                _timer = new System.Timers.Timer(500);
                _timer.AutoReset = false;
                _timer.Elapsed += delegate(object s, System.Timers.ElapsedEventArgs e)
                {
                    _timer.Stop();
                    try
                    {
                        LoadAll();
                    }
                    catch
                    {
                    }
                };
                _timer.Start();
            }
            else
            {
                _timer.Stop();
                _timer.Start();
            }
        }
    }

    public interface IProtocolEngine : IDisposable
    {
        void Init(DeviceProfile profile, int address, SerialBus bus);
        string Execute(string cmd, string args);
    }

    public static class ProtocolEngineFactory
    {
        public static IProtocolEngine Create(string protocol)
        {
            if (string.IsNullOrWhiteSpace(protocol))
            {
                return null;
            }

            string p = protocol.Trim().ToLowerInvariant();
            if (p == "modbus" || p == "modbus-rtu") return new ModbusEngine();
            if (p == "fixed-frame" || p == "fixedframe") return new FixedFrameEngine();
            if (p == "custom") return new CustomEngine();
            return null;
        }
    }

    public abstract class ProtocolEngineBase : IProtocolEngine
    {
        protected DeviceProfile Profile;
        protected int Address;
        protected SerialBus Bus;

        public virtual void Init(DeviceProfile profile, int address, SerialBus bus)
        {
            Profile = profile;
            Address = address;
            Bus = bus;
        }

        public abstract string Execute(string cmd, string args);

        public virtual void Dispose() { }

        protected Dictionary<string, string> ParseArgsToMap(string args)
        {
            var m = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(args))
            {
                return m;
            }

            string[] parts = args.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string p in parts)
            {
                int eq = p.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }
                string k = p.Substring(0, eq);
                string v = p.Substring(eq + 1);
                m[k] = v;
            }
            return m;
        }

        protected int ParseIntOrDefault(string s, int def)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return def;
            }

            int a;
            if (int.TryParse(s, out a))
            {
                return a;
            }

            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                int b;
                if (int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                {
                    return b;
                }
            }

            return def;
        }

        protected int GetOrDefault(int? value, int def)
        {
            if (value.HasValue)
            {
                return value.Value;
            }
            return def;
        }
    }

    public class ModbusEngine : ProtocolEngineBase
    {
        public override string Execute(string cmd, string args)
        {
            if (Profile == null || Profile.commands == null)
            {
                return "ERR:NoProfile";
            }

            string c = cmd == null ? string.Empty : cmd.ToLowerInvariant();
            if (Profile.commands.ContainsKey(c))
            {
                CommandDef def = Profile.commands[c];

                if (string.Equals(def.action, "read", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(def.register))
                {
                    RegisterDef reg;
                    if (!Profile.registers.TryGetValue(def.register, out reg))
                    {
                        return "ERR:NoReg:" + def.register;
                    }

                    int cnt = ResolveReadLen(reg);
                    ushort[] raw = Bus.ModbusReadHolding(Address, GetOrDefault(reg.addr, 0), (ushort)cnt);
                    if (raw == null)
                    {
                        return "ERR:NoRsp";
                    }
                    if (string.Equals(reg.action, "read_coil", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] frame = BuildReadCoilFrame(GetOrDefault(reg.addr, 0), cnt);
                        byte[] r = Bus.SendAndReceive(frame, true, 0, true);
                        return "COILS=" + ParseCoils(r);
                    }

                    return FormatRegister(reg, raw);
                }

                if (string.Equals(def.action, "write", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(def.register))
                {
                    RegisterDef reg;
                    if (!Profile.registers.TryGetValue(def.register, out reg))
                    {
                        return "ERR:NoReg:" + def.register;
                    }

                    int val;
                    if (!int.TryParse(args, out val))
                    {
                        return "ERR:ValueFormat";
                    }
                    int rawValue = val;
                    if (Math.Abs(reg.scale) > 0.0000001)
                    {
                        rawValue = (int)(val / reg.scale);
                    }
                    bool ok = Bus.ModbusWriteSingle(Address, GetOrDefault(reg.addr, 0), (ushort)rawValue);
                    return ok ? "OK" : "ERR:NoAck";
                }

                if (string.Equals(def.action, "write_coil", StringComparison.OrdinalIgnoreCase))
                {
                    Dictionary<string, string> map = ParseArgsToMap(args);
                    int addr = ResolveCoilAddr(def, regValue(def, map), regValue(def, map));
                    int st = 0;
                    if (map.ContainsKey("state"))
                    {
                        int.TryParse(map["state"], out st);
                    }
                    else if (def.value.HasValue)
                    {
                        st = def.value.Value;
                    }

                    bool ok = Bus.ModbusWriteCoil(Address, (ushort)addr, st != 0);
                    return ok ? "OK" : "ERR:NoAck";
                }

                if (string.Equals(def.action, "read_coil", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(def.register))
                {
                    RegisterDef reg;
                    if (!Profile.registers.TryGetValue(def.register, out reg))
                    {
                        return "ERR:NoReg:" + def.register;
                    }

                    byte[] frame = BuildReadCoilFrame(GetOrDefault(reg.addr, 0), GetOrDefault(reg.length, 8));
                    byte[] r = Bus.SendAndReceive(frame, true, 0, true);
                    return "COILS=" + ParseCoils(r);
                }

                if (string.Equals(def.action, "read_multi", StringComparison.OrdinalIgnoreCase))
                {
                    if (def.registers == null || def.registers.Count == 0)
                    {
                        return "ERR:NoRegs";
                    }

                    var parts = new List<string>();
                    foreach (string rk in def.registers)
                    {
                        if (!Profile.registers.ContainsKey(rk))
                        {
                            continue;
                        }
                        RegisterDef reg = Profile.registers[rk];
                        ushort[] raw = Bus.ModbusReadHolding(Address, GetOrDefault(reg.addr, 0), (ushort)ResolveReadLen(reg));
                        if (raw == null)
                        {
                            parts.Add(rk + "=ERR");
                        }
                        else
                        {
                            parts.Add(rk + "=" + FormatRegister(reg, raw));
                        }
                    }
                    return string.Join(";", parts.ToArray());
                }
            }

            string[] ps = c.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (ps.Length >= 3)
            {
                if (ps[0] == "read_hold")
                {
                    int reg = ParseIntOrDefault(ps[1], 0);
                    int cnt = ParseIntOrDefault(ps[2], 1);
                    ushort[] raw = Bus.ModbusReadHolding(Address, reg, (ushort)cnt);
                    return raw == null ? "ERR:NoRsp" : string.Join(",", raw.Select(n => n.ToString()).ToArray());
                }
                if (ps[0] == "write_hold")
                {
                    int reg = ParseIntOrDefault(ps[1], 0);
                    int val = ParseIntOrDefault(ps[2], 0);
                    return Bus.ModbusWriteSingle(Address, reg, (ushort)val) ? "OK" : "ERR:NoAck";
                }
            }

            return "ERR:UnknownCmd";
        }

        private int regValue(CommandDef def, Dictionary<string, string> map)
        {
            if (def == null || map == null)
            {
                return 0;
            }
            if (!string.IsNullOrWhiteSpace(def.addr_expr))
            {
                string t = def.addr_expr.ToUpperInvariant();
                if (t == "N-1" && map.ContainsKey("N"))
                {
                    return ParseIntOrDefault(map["N"], 1) - 1;
                }
            }
            if (def.value.HasValue)
            {
                return def.value.Value;
            }
            return 0;
        }

        private int ResolveCoilAddr(CommandDef def, int fallback1, int fallback2)
        {
            if (!string.IsNullOrWhiteSpace(def.addr_expr))
            {
                if (def.addr_expr.IndexOf("-") > 0)
                {
                    string[] sp = def.addr_expr.Split('-');
                    if (sp.Length == 2 && string.Equals(sp[1], "1", StringComparison.OrdinalIgnoreCase))
                    {
                        return Math.Max(0, fallback1);
                    }
                }
            }

            if (def.value.HasValue)
            {
                return def.value.Value;
            }
            return fallback2;
        }

        private int ResolveReadLen(RegisterDef reg)
        {
            if (reg == null)
            {
                return 1;
            }
            string t = reg.type == null ? "uint16" : reg.type.ToLowerInvariant();
            if (t == "float" || t == "uint32" || t == "int32")
            {
                return 2;
            }
            if (t == "bitmap")
            {
                if (reg.length.HasValue && reg.length.Value > 0)
                {
                    return reg.length.Value;
                }
                return 8;
            }
            return 1;
        }

        private string ParseCoils(byte[] resp)
        {
            if (resp == null || resp.Length < 4)
            {
                return string.Empty;
            }

            int bc = resp[2];
            var sb = new StringBuilder();
            for (int i = 0; i < bc; i++)
            {
                int idx = 3 + (i / 8);
                if (idx >= resp.Length)
                {
                    break;
                }
                sb.Append(((resp[idx] & (1 << (i % 8))) != 0) ? '1' : '0');
            }
            return sb.ToString();
        }

        private string FormatRegister(RegisterDef reg, ushort[] raw)
        {
            string t = reg == null || reg.type == null ? "uint16" : reg.type.ToLowerInvariant();
            if (t == "float")
            {
                if (raw == null || raw.Length < 2)
                {
                    return "ERR";
                }
                byte[] f = new byte[]
                {
                    (byte)(raw[0] >> 8),
                    (byte)(raw[0]),
                    (byte)(raw[1] >> 8),
                    (byte)(raw[1])
                };
                float v = BitConverter.ToSingle(f, 0);
                return (v).ToString("0.###") + (reg.unit ?? string.Empty);
            }

            if (t == "bitmap" && raw.Length > 0)
            {
                return "0x" + raw[0].ToString("X4");
            }

            double vv = raw != null && raw.Length > 0 ? raw[0] : 0;
            if (Math.Abs(reg.scale) > 0.0000001)
            {
                vv = vv * reg.scale + reg.offset;
            }
            return vv.ToString("0.###") + (reg.unit ?? string.Empty);
        }

        private byte[] BuildReadCoilFrame(int addr, int count)
        {
            byte[] f = new byte[]
            {
                (byte)Address,
                0x01,
                (byte)(addr >> 8),
                (byte)addr,
                (byte)(count >> 8),
                (byte)count
            };
            ushort crc = Crc16(f);
            return new byte[]
            {
                f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8)
            };
        }

        private ushort Crc16(byte[] d)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < d.Length; i++)
            {
                crc ^= d[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }
    }

    public class FixedFrameEngine : ProtocolEngineBase
    {
        public override string Execute(string cmd, string args)
        {
            if (Profile == null || Profile.commands == null)
            {
                return "ERR:NoProfile";
            }

            string c = cmd == null ? string.Empty : cmd.ToLowerInvariant();
            if (!Profile.commands.ContainsKey(c))
            {
                return "ERR:UnknownCmd";
            }

            CommandDef def = Profile.commands[c];
            Dictionary<string, string> map = ParseArgsToMap(args);
            byte[] frame = null;

            if (def.bytes != null && def.bytes.Count > 0)
            {
                frame = BuildFromBytes(def.bytes);
            }
            else if (!string.IsNullOrWhiteSpace(def.template))
            {
                frame = BuildFromTemplate(def.template, map);
            }
            else
            {
                return "ERR:BadCmdDef";
            }

            string csType = def.checksum;
            if (string.IsNullOrWhiteSpace(csType) && Profile.registers != null && Profile.registers.ContainsKey("header"))
            {
                csType = Profile.registers["header"].checksum;
            }

            if (!string.IsNullOrWhiteSpace(csType))
            {
                frame = AppendChecksum(frame, csType);
            }

            bool waitResp = true;
            if (!string.IsNullOrWhiteSpace(def.response_mode) && def.response_mode.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                waitResp = false;
            }

            int fixedLen = def.response_length.HasValue ? def.response_length.Value : 0;
            byte[] rsp = Bus.SendAndReceive(frame, waitResp, fixedLen, true);
            if (!waitResp)
            {
                return "OK|NoWait";
            }
            if (rsp == null || rsp.Length == 0)
            {
                return "OK|NoRsp";
            }

            if (string.Equals(def.response_parse, "text", StringComparison.OrdinalIgnoreCase))
            {
                return "OK|" + Encoding.UTF8.GetString(rsp);
            }
            return "OK|" + HexUtil.ToHex(rsp);
        }

        private byte[] BuildFromBytes(List<string> bytes)
        {
            List<byte> rs = new List<byte>();
            foreach (string s in bytes)
            {
                rs.AddRange(HexUtil.FromHex(s));
            }
            return rs.ToArray();
        }

        private byte[] BuildFromTemplate(string tpl, Dictionary<string, string> map)
        {
            string[] segs = tpl.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<byte> rs = new List<byte>();
            foreach (string segRaw in segs)
            {
                string seg = segRaw.Trim();
                if (seg.Length == 0)
                {
                    continue;
                }

                if (seg.StartsWith("{") && seg.EndsWith("}"))
                {
                    string key = seg.Substring(1, seg.Length - 2);
                    byte b = 0;
                    if (string.Equals(key, "addr", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "address", StringComparison.OrdinalIgnoreCase))
                    {
                        b = (byte)Address;
                    }
                    else if (key.Equals("N", StringComparison.OrdinalIgnoreCase) || key.Equals("channel", StringComparison.OrdinalIgnoreCase) || key.Equals("ch", StringComparison.OrdinalIgnoreCase))
                    {
                        if (map.ContainsKey("N")) b = (byte)ParseIntOrDefault(map["N"], 1);
                        else if (map.ContainsKey("channel")) b = (byte)ParseIntOrDefault(map["channel"], 1);
                        else b = 1;
                    }
                    else if (key.Equals("state", StringComparison.OrdinalIgnoreCase))
                    {
                        b = (byte)ParseIntOrDefault(GetMapValue(map, "state"), 0);
                    }
                    else
                    {
                        b = (byte)ParseIntOrDefault(GetMapValue(map, key), 0);
                    }

                    rs.Add(b);
                }
                else
                {
                    rs.AddRange(HexUtil.FromHex(seg));
                }
            }
            return rs.ToArray();
        }

        private string GetMapValue(Dictionary<string, string> map, string key)
        {
            if (map != null && map.ContainsKey(key))
            {
                return map[key];
            }
            return string.Empty;
        }

        private byte[] AppendChecksum(byte[] data, string c)
        {
            if (data == null)
            {
                return null;
            }
            if (string.IsNullOrWhiteSpace(c))
            {
                return data;
            }

            byte cs = 0;
            if (c.Equals("sum8", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < data.Length; i++)
                {
                    cs += data[i];
                }
            }
            else if (c.Equals("xor8", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < data.Length; i++)
                {
                    cs ^= data[i];
                }
            }
            else
            {
                return data;
            }

            byte[] o = new byte[data.Length + 1];
            Array.Copy(data, 0, o, 0, data.Length);
            o[o.Length - 1] = cs;
            return o;
        }
    }

    public class CustomEngine : ProtocolEngineBase
    {
        public override string Execute(string cmd, string args)
        {
            if (Profile == null || Profile.commands == null)
            {
                return "ERR:NoProfile";
            }

            string c = cmd == null ? string.Empty : cmd.ToLowerInvariant();
            if (!Profile.commands.ContainsKey(c))
            {
                return "ERR:UnknownCmd";
            }

            CommandDef def = Profile.commands[c];
            Dictionary<string, string> map = ParseArgsToMap(args);

            byte[] frame = null;
            if (!string.IsNullOrWhiteSpace(def.send))
            {
                try
                {
                    frame = BuildSendTemplate(def.send, map);
                }
                catch (Exception ex)
                {
                    var parts = ex.Message.Split(new[] { ':' }, 3);
                    var token = parts.Length >= 2 ? parts[1] : string.Empty;
                    var val = parts.Length == 3 ? parts[2] : string.Empty;
                    Debug.WriteLine("CustomEngine: token '" + token + "' parse error on value '" + val + "'");
                    return "ERR:CustomParse:" + token;
                }
            }
            else if (def.bytes != null && def.bytes.Count > 0)
            {
                frame = BuildFromBytes(def.bytes);
            }
            else if (!string.IsNullOrWhiteSpace(def.template))
            {
                frame = BuildFromTemplate(def.template, map);
            }

            if (frame == null)
            {
                return "ERR:BadCmdDef";
            }

            bool waitResp = true;
            if (!string.IsNullOrWhiteSpace(def.response_mode) && def.response_mode.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                waitResp = false;
            }

            int fixedLen = def.response_length.HasValue ? def.response_length.Value : 0;
            byte[] rsp = Bus.SendAndReceive(frame, waitResp, fixedLen, true);
            if (!waitResp)
            {
                return "OK|NoWait";
            }
            if (rsp == null || rsp.Length == 0)
            {
                return "OK|NoRsp";
            }

            if (string.Equals(def.response_parse, "text", StringComparison.OrdinalIgnoreCase))
            {
                return "OK|" + Encoding.UTF8.GetString(rsp);
            }
            return "OK|" + HexUtil.ToHex(rsp);
        }

        private byte[] BuildFromTemplate(string template, Dictionary<string, string> map)
        {
            string[] segs = template.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            List<byte> rs = new List<byte>();
            foreach (string segRaw in segs)
            {
                string seg = segRaw.Trim();
                if (seg.StartsWith("{") && seg.EndsWith("}"))
                {
                    string key = seg.Substring(1, seg.Length - 2);
                    byte v;
                    if (string.Equals(key, "addr", StringComparison.OrdinalIgnoreCase) || string.Equals(key, "address", StringComparison.OrdinalIgnoreCase))
                    {
                        v = (byte)Address;
                    }
                    else
                    {
                        v = 0;
                        if (map.ContainsKey(key))
                        {
                            v = (byte)ParseIntOrDefault(map[key], 0);
                        }
                    }
                    rs.Add(v);
                }
                else
                {
                    rs.AddRange(HexUtil.FromHex(seg));
                }
            }
            return rs.ToArray();
        }

        private byte[] BuildSendTemplate(string send, Dictionary<string, string> map)
        {
            if (string.IsNullOrWhiteSpace(send))
            {
                return new byte[0];
            }

            var segments = send.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var bytes = new List<byte>();

            foreach (var rawSeg in segments)
            {
                var seg = rawSeg.Trim();
                if (seg.Length == 0)
                {
                    continue;
                }

                if (seg.StartsWith("{") && seg.EndsWith("}"))
                {
                    var inner = seg.Substring(1, seg.Length - 2).Trim();
                    if (inner.Length == 0)
                    {
                        throw new Exception("CustomParse:" + inner + ":" + inner);
                    }

                    if (inner.Equals("addr", StringComparison.OrdinalIgnoreCase) ||
                        inner.Equals("address", StringComparison.OrdinalIgnoreCase))
                    {
                        bytes.Add((byte)Address);
                        continue;
                    }

                    var parts = inner.Split(':');
                    if (parts.Length == 2 && parts[1].Equals("u8", StringComparison.OrdinalIgnoreCase))
                    {
                        var key = parts[0];
                        var val = string.Empty;

                        if (map != null)
                        {
                            string mapped;
                            if (map.TryGetValue(key, out mapped))
                            {
                                val = mapped;
                            }
                        }

                        if ((val == null || val.Length == 0) && (key.Equals("addr", StringComparison.OrdinalIgnoreCase) ||
                                                                 key.Equals("address", StringComparison.OrdinalIgnoreCase)))
                        {
                            val = Address.ToString(CultureInfo.InvariantCulture);
                        }

                        byte parsed;
                        if (!TryParseU8(val, out parsed))
                        {
                            throw new Exception("CustomParse:" + key + ":" + val);
                        }

                        bytes.Add(parsed);
                        continue;
                    }

                    throw new Exception("CustomParse:" + inner + ":" + inner);
                }

                try
                {
                    bytes.AddRange(HexUtil.ParseHexBytes(seg));
                }
                catch
                {
                    throw new Exception("CustomParse:" + seg + ":" + seg);
                }
            }

            return bytes.ToArray();
        }

        private static bool TryParseU8(string value, out byte result)
        {
            result = 0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var text = value.Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                var hex = text.Substring(2);
                if (hex.Length == 0)
                {
                    return false;
                }
                return byte.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result);
            }

            return byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        private byte[] BuildFromBytes(List<string> list)
        {
            List<byte> rs = new List<byte>();
            foreach (string s in list)
            {
                rs.AddRange(HexUtil.FromHex(s));
            }
            return rs.ToArray();
        }
    }

    public class SerialBus
    {
        private readonly SerialPort _port;
        private readonly object _lock;

        public SerialBus(SerialPort port, object sync)
        {
            _port = port;
            _lock = sync;
        }

        public byte[] SendAndReceive(byte[] frame, bool waitResponse, int fixedLen, bool isComB)
        {
            lock (_lock)
            {
                if (_port == null || !_port.IsOpen)
                {
                    throw new Exception("COM-B closed");
                }

                _port.DiscardInBuffer();
                _port.DiscardOutBuffer();
                if (frame != null && frame.Length > 0)
                {
                    _port.Write(frame, 0, frame.Length);
                }

                if (!waitResponse)
                {
                    return new byte[0];
                }

                if (fixedLen > 0)
                {
                    return ReadExact(fixedLen, 1200);
                }

                Thread.Sleep(40);
                int n = _port.BytesToRead;
                if (n <= 0)
                {
                    return new byte[0];
                }
                byte[] b = new byte[n];
                _port.Read(b, 0, n);
                return b;
            }
        }

        public ushort[] ModbusReadHolding(int addr, int reg, ushort n)
        {
            byte[] frame = BuildReadHoldingFrame(addr, reg, n);
            byte[] rsp = SendAndReceive(frame, true, 0, true);
            if (rsp == null || rsp.Length < 3)
            {
                return null;
            }
            if (rsp.Length < 3 + rsp[2])
            {
                return null;
            }
            int bc = rsp[2];
            ushort[] r = new ushort[bc / 2];
            for (int i = 0; i < r.Length; i++)
            {
                r[i] = (ushort)((rsp[3 + i * 2] << 8) | rsp[3 + i * 2 + 1]);
            }
            return r;
        }

        public bool ModbusWriteSingle(int addr, int reg, ushort val)
        {
            byte[] frame = BuildWriteSingleFrame(addr, reg, val);
            byte[] rsp = SendAndReceive(frame, true, 0, true);
            return rsp != null && rsp.Length >= 8;
        }

        public bool ModbusWriteCoil(int addr, int coil, bool on)
        {
            byte[] frame = BuildWriteCoilFrame(addr, coil, on);
            byte[] rsp = SendAndReceive(frame, true, 0, true);
            return rsp != null && rsp.Length >= 8;
        }

        private byte[] ReadExact(int len, int timeoutMs)
        {
            byte[] buf = new byte[len];
            int got = 0;
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);

            while (got < len && DateTime.Now < deadline)
            {
                if (_port.BytesToRead > 0)
                {
                    got += _port.Read(buf, got, Math.Min(_port.BytesToRead, len - got));
                }
                else
                {
                    Thread.Sleep(5);
                }
            }

            if (got == len)
            {
                return buf;
            }
            if (got == 0)
            {
                return new byte[0];
            }

            byte[] rs = new byte[got];
            Array.Copy(buf, rs, got);
            return rs;
        }

        private ushort Crc16(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                    {
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }
            return crc;
        }

        private byte[] BuildReadHoldingFrame(int addr, int reg, ushort n)
        {
            byte[] frame = new byte[8];
            frame[0] = (byte)addr;
            frame[1] = 0x03;
            frame[2] = (byte)(reg >> 8);
            frame[3] = (byte)reg;
            frame[4] = (byte)(n >> 8);
            frame[5] = (byte)n;
            ushort crc = Crc16(frame, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        private byte[] BuildWriteSingleFrame(int addr, int reg, ushort val)
        {
            byte[] frame = new byte[8];
            frame[0] = (byte)addr;
            frame[1] = 0x06;
            frame[2] = (byte)(reg >> 8);
            frame[3] = (byte)reg;
            frame[4] = (byte)(val >> 8);
            frame[5] = (byte)val;
            ushort crc = Crc16(frame, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }

        private byte[] BuildWriteCoilFrame(int addr, int coil, bool on)
        {
            byte[] frame = new byte[8];
            frame[0] = (byte)addr;
            frame[1] = 0x05;
            frame[2] = (byte)(coil >> 8);
            frame[3] = (byte)coil;
            frame[4] = on ? (byte)0xFF : (byte)0x00;
            frame[5] = 0x00;
            ushort crc = Crc16(frame, 6);
            frame[6] = (byte)(crc & 0xFF);
            frame[7] = (byte)(crc >> 8);
            return frame;
        }
    }

    public class BlockingCommandQueue
    {
        private readonly Action<NovaCommand> _worker;
        private readonly BlockingCollection<NovaCommand> _queue = new BlockingCollection<NovaCommand>();
        private Thread _thread;
        private volatile bool _running;

        public BlockingCommandQueue(Action<NovaCommand> worker)
        {
            _worker = worker;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }
            _running = true;
            _thread = new Thread(new ThreadStart(Loop));
            _thread.IsBackground = true;
            _thread.Start();
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }
            _running = false;
            _queue.CompleteAdding();
        }

        public void Enqueue(NovaCommand cmd)
        {
            if (!_running)
            {
                if (!_queue.IsAddingCompleted)
                {
                    _queue.CompleteAdding();
                }
            }
            try
            {
                _queue.Add(cmd);
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void Loop()
        {
            foreach (NovaCommand cmd in _queue.GetConsumingEnumerable())
            {
                if (_worker != null)
                {
                    _worker(cmd);
                }
            }
        }
    }

    public class RegisterDef
    {
        public string action { get; set; }
        public int? fc { get; set; }
        public int? addr { get; set; }
        public string param { get; set; }
        public string service { get; set; }
        public string cls { get; set; }
        public string instance { get; set; }
        public string attribute { get; set; }
        public string type { get; set; }
        public double scale { get; set; }
        public double offset { get; set; }
        public string unit { get; set; }
        public int? length { get; set; }
        public List<string> bytes { get; set; }
        public string checksum { get; set; }
    }

    public class CommandDef
    {
        public string action { get; set; }
        public string register { get; set; }
        public string input { get; set; }
        public string addr_expr { get; set; }
        public int? value { get; set; }
        public List<string> @params { get; set; }
        public List<string> bytes { get; set; }
        public string template { get; set; }
        public List<string> registers { get; set; }
        public string checksum { get; set; }
        public string send { get; set; }
        public int? response_length { get; set; }
        public string response_mode { get; set; }
        public string response_parse { get; set; }
    }

    public class DeviceProfile
    {
        public string name { get; set; }
        public string protocol { get; set; }
        public int default_baudrate { get; set; }
        public int default_address { get; set; }
        public double? full_scale { get; set; }
        public string gas_type { get; set; }
        public Dictionary<string, RegisterDef> registers { get; set; }
        public Dictionary<string, CommandDef> commands { get; set; }
    }

    public static class JsonLite
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            p.SkipWs();
            object v = p.ParseValue();
            p.SkipWs();
            return v;
        }

        public static Dictionary<string, object> AsObj(object o)
        {
            return o as Dictionary<string, object>;
        }

        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                return v.ToString();
            }
            return null;
        }

        public static int Int(Dictionary<string, object> d, string key, int def)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                return def;
            }

            if (v is double)
            {
                return (int)(double)v;
            }
            if (v is string)
            {
                int n;
                string s = (string)v;
                if (int.TryParse(s, out n))
                {
                    return n;
                }
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                {
                    return n;
                }
            }
            return def;
        }

        public static int? IntN(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                return null;
            }
            if (v is double)
            {
                return (int)(double)v;
            }
            if (v is string)
            {
                int n;
                string s = (string)v;
                if (int.TryParse(s, out n))
                {
                    return n;
                }
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n))
                {
                    return n;
                }
            }
            return null;
        }

        public static double Dbl(Dictionary<string, object> d, string key, double def)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                return def;
            }
            if (v is double)
            {
                return (double)v;
            }
            if (v is string)
            {
                double dv;
                if (double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                {
                    return dv;
                }
            }
            return def;
        }

        public static double? DblN(Dictionary<string, object> d, string key)
        {
            object v;
            if (d == null || !d.TryGetValue(key, out v) || v == null)
            {
                return null;
            }
            if (v is double)
            {
                return (double)v;
            }
            if (v is string)
            {
                double dv;
                if (double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out dv))
                {
                    return dv;
                }
            }
            return null;
        }

        public static List<string> StrList(object o)
        {
            var r = new List<string>();
            if (!(o is List<object>))
            {
                return r;
            }

            List<object> arr = (List<object>)o;
            foreach (object it in arr)
            {
                if (it == null)
                {
                    continue;
                }
                if (it is double)
                {
                    double d = (double)it;
                    if (Math.Abs(d - Math.Truncate(d)) < 0.000001)
                    {
                        r.Add(((long)d).ToString(CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        r.Add(d.ToString(CultureInfo.InvariantCulture));
                    }
                }
                else
                {
                    r.Add(it.ToString());
                }
            }
            return r;
        }

        public static DeviceProfile MapProfile(Dictionary<string, object> o)
        {
            if (o == null)
            {
                return null;
            }

            DeviceProfile p = new DeviceProfile();
            p.name = Str(o, "name");
            p.protocol = Str(o, "protocol");
            p.default_baudrate = Int(o, "default_baudrate", 9600);
            p.default_address = Int(o, "default_address", 1);
            p.full_scale = DblN(o, "full_scale");
            p.gas_type = Str(o, "gas_type");

            object rd;
            if (o.TryGetValue("registers", out rd))
            {
                Dictionary<string, object> o2 = AsObj(rd);
                if (o2 != null)
                {
                    p.registers = new Dictionary<string, RegisterDef>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, object> kv in o2)
                    {
                        Dictionary<string, object> item = AsObj(kv.Value);
                        if (item != null)
                        {
                            p.registers[kv.Key] = MapRegister(item);
                        }
                    }
                }
            }

            object cd;
            if (o.TryGetValue("commands", out cd))
            {
                Dictionary<string, object> o3 = AsObj(cd);
                if (o3 != null)
                {
                    p.commands = new Dictionary<string, CommandDef>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, object> kv in o3)
                    {
                        Dictionary<string, object> item = AsObj(kv.Value);
                        if (item != null)
                        {
                            p.commands[kv.Key] = MapCommand(item);
                        }
                    }
                }
            }

            return p;
        }

        public static RegisterDef MapRegister(Dictionary<string, object> d)
        {
            RegisterDef r = new RegisterDef();
            r.action = Str(d, "action");
            r.fc = IntN(d, "fc");
            r.addr = IntN(d, "addr");
            r.param = Str(d, "param");
            r.service = Str(d, "service");
            r.cls = Str(d, "class");
            r.instance = Str(d, "instance");
            r.attribute = Str(d, "attribute");
            r.type = Str(d, "type");
            r.scale = Dbl(d, "scale", 0);
            r.offset = Dbl(d, "offset", 0);
            r.unit = Str(d, "unit");
            r.length = IntN(d, "length");
            r.checksum = Str(d, "checksum");

            object bytes;
            if (d.TryGetValue("bytes", out bytes))
            {
                r.bytes = StrList(bytes);
            }
            return r;
        }

        public static CommandDef MapCommand(Dictionary<string, object> d)
        {
            CommandDef c = new CommandDef();
            c.action = Str(d, "action");
            c.register = Str(d, "register");
            c.input = Str(d, "input");
            c.addr_expr = Str(d, "addr_expr");
            c.value = IntN(d, "value");
            c.template = Str(d, "template");
            c.send = Str(d, "send");
            c.checksum = Str(d, "checksum");
            c.response_mode = Str(d, "response_mode");
            c.response_parse = Str(d, "response_parse");
            c.response_length = IntN(d, "response_length");

            object prm;
            if (d.TryGetValue("params", out prm)) c.@params = StrList(prm);
            object bs;
            if (d.TryGetValue("bytes", out bs)) c.bytes = StrList(bs);
            object rg;
            if (d.TryGetValue("registers", out rg)) c.registers = StrList(rg);
            return c;
        }

        public class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
                _i = 0;
            }

            public void SkipWs()
            {
                while (_i < _s.Length)
                {
                    char ch = _s[_i];
                    if (ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public object ParseValue()
            {
                SkipWs();
                if (_i >= _s.Length)
                {
                    throw new FormatException("JSON: unexpected end");
                }
                char c = _s[_i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't')
                {
                    Expect("true");
                    return true;
                }
                if (c == 'f')
                {
                    Expect("false");
                    return false;
                }
                if (c == 'n')
                {
                    Expect("null");
                    return null;
                }
                return ParseNumber();
            }

            private void Expect(string w)
            {
                if (_i + w.Length > _s.Length || _s.Substring(_i, w.Length) != w)
                {
                    throw new FormatException("JSON: expected " + w + " at " + _i);
                }
                _i += w.Length;
            }

            private Dictionary<string, object> ParseObject()
            {
                Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                _i++;
                SkipWs();
                if (_i < _s.Length && _s[_i] == '}')
                {
                    _i++;
                    return d;
                }
                while (true)
                {
                    SkipWs();
                    string k = ParseString();
                    SkipWs();
                    if (_i >= _s.Length || _s[_i] != ':')
                    {
                        throw new FormatException("JSON: expected ':'");
                    }
                    _i++;
                    object v = ParseValue();
                    d[k] = v;

                    SkipWs();
                    if (_i >= _s.Length)
                    {
                        throw new FormatException("JSON: unexpected end object");
                    }
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == '}')
                    {
                        _i++;
                        break;
                    }
                    throw new FormatException("JSON: expected ',' or '}'");
                }
                return d;
            }

            private List<object> ParseArray()
            {
                List<object> a = new List<object>();
                _i++;
                SkipWs();
                if (_i < _s.Length && _s[_i] == ']')
                {
                    _i++;
                    return a;
                }
                while (true)
                {
                    a.Add(ParseValue());
                    SkipWs();
                    if (_i >= _s.Length)
                    {
                        throw new FormatException("JSON: unexpected end array");
                    }
                    if (_s[_i] == ',')
                    {
                        _i++;
                        continue;
                    }
                    if (_s[_i] == ']')
                    {
                        _i++;
                        break;
                    }
                    throw new FormatException("JSON: expected ',' or ']'");
                }
                return a;
            }

            private string ParseString()
            {
                StringBuilder sb = new StringBuilder();
                _i++;
                while (_i < _s.Length)
                {
                    char c = _s[_i++];
                    if (c == '"')
                    {
                        return sb.ToString();
                    }
                    if (c == '\\')
                    {
                        if (_i >= _s.Length)
                        {
                            break;
                        }
                        char e = _s[_i++];
                        switch (e)
                        {
                            case '"':
                                sb.Append('"');
                                break;
                            case '\\':
                                sb.Append('\\');
                                break;
                            case '/':
                                sb.Append('/');
                                break;
                            case 'b':
                                sb.Append('\b');
                                break;
                            case 'f':
                                sb.Append('\f');
                                break;
                            case 'n':
                                sb.Append('\n');
                                break;
                            case 'r':
                                sb.Append('\r');
                                break;
                            case 't':
                                sb.Append('\t');
                                break;
                            case 'u':
                                if (_i + 4 <= _s.Length)
                                {
                                    sb.Append((char)Convert.ToInt32(_s.Substring(_i, 4), 16));
                                    _i += 4;
                                }
                                break;
                            default:
                                sb.Append(e);
                                break;
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                throw new FormatException("JSON: unterminated string");
            }

            private double ParseNumber()
            {
                int start = _i;
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E')
                    {
                        _i++;
                    }
                    else
                    {
                        break;
                    }
                }
                return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
            }
        }
    }

    public static class HexUtil
    {
        public static byte[] ParseHexBytes(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return new byte[0];
            }

            string cleaned = Regex.Replace(s, "0x", string.Empty, RegexOptions.IgnoreCase);
            cleaned = cleaned.Replace(" ", string.Empty).Replace("\t", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            if (cleaned.Length % 2 == 1)
            {
                cleaned = "0" + cleaned;
            }
            if (cleaned.Length == 0)
            {
                return new byte[0];
            }

            byte[] d = new byte[cleaned.Length / 2];
            for (int i = 0; i < d.Length; i++)
            {
                string xx = cleaned.Substring(i * 2, 2);
                d[i] = byte.Parse(xx, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }
            return d;
        }

        public static byte[] FromHex(string s)
        {
            return ParseHexBytes(s);
        }

        public static string ToHex(byte[] d)
        {
            if (d == null || d.Length == 0)
            {
                return "(empty)";
            }
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < d.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(d[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }

    public static class SampleTemplates
    {
        public const string Modbus = "{\n"
            + "  \"name\": \"Sample_Modbus\",\n"
            + "  \"protocol\": \"modbus-rtu\",\n"
            + "  \"default_baudrate\": 9600,\n"
            + "  \"default_address\": 1,\n"
            + "  \"registers\": {\n"
            + "    \"pv\": { \"fc\": 3, \"addr\": 0, \"type\": \"int16\", \"scale\": 0.1, \"unit\": \"℃\" },\n"
            + "    \"sv\": { \"fc\": 3, \"addr\": 1, \"type\": \"int16\", \"scale\": 0.1, \"unit\": \"℃\" }\n"
            + "  },\n"
            + "  \"commands\": {\n"
            + "    \"read_pv\": { \"action\": \"read\", \"register\": \"pv\" },\n"
            + "    \"set_sv\": { \"action\": \"write\", \"register\": \"sv\", \"input\": \"float\" }\n"
            + "  }\n"
            + "}";

        public const string Fixed = "{\n"
            + "  \"name\": \"Sample_FixedFrame\",\n"
            + "  \"protocol\": \"fixed-frame\",\n"
            + "  \"default_baudrate\": 9600,\n"
            + "  \"default_address\": 1,\n"
            + "  \"registers\": {\n"
            + "    \"header\": { \"bytes\": [\"0xA0\"], \"checksum\": \"sum8\" }\n"
            + "  },\n"
            + "  \"commands\": {\n"
            + "    \"open_ch\": { \"template\": \"0xA0,{N},0x00,0x01\", \"params\": [\"N\", \"state\"] },\n"
            + "    \"close_ch\": { \"template\": \"0xA0,{N},0x00,0x00\", \"params\": [\"N\", \"state\"] },\n"
            + "    \"ping\": { \"bytes\": [\"A0\", \"00\", \"00\", \"00\"], \"response_mode\": \"none\" }\n"
            + "  }\n"
            + "}";

        public const string Custom = "{\n"
            + "  \"name\": \"Sample_Custom\",\n"
            + "  \"protocol\": \"custom\",\n"
            + "  \"default_baudrate\": 9600,\n"
            + "  \"default_address\": 1,\n"
            + "  \"registers\": {\n"
            + "    \"header\": { \"bytes\": [\"0xAA\"], \"checksum\": \"none\" }\n"
            + "  },\n"
            + "  \"commands\": {\n"
            + "    \"ping\": {\n"
            + "      \"send\": \"AA 55 01 {addr}\",\n"
            + "      \"response_mode\": \"text\",\n"
            + "      \"response_parse\": \"text\"\n"
            + "    },\n"
            + "    \"echo\": {\n"
            + "      \"send\": \"AA 55 02 {msg}\",\n"
            + "      \"response_mode\": \"text\",\n"
            + "      \"response_parse\": \"text\"\n"
            + "    }\n"
            + "  }\n"
            + "}";
    }
}










