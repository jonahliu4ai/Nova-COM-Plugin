// NovaCOMPlugin.cs
// 版本: 4.5 Profile-Driven Device Framework
// 变更:
//   1. 新增 JSON 设备模板驱动 — 换仪器只需写 JSON，0 行 C# 改动
//   2. 新增 ProtocolEngine 抽象层：ModbusEngine / AIBUSEngine / FixedFrameEngine / SevenStarEngine
//   3. 新增 SmartDevice + DeviceFactory，统一创建配置化设备
//   4. 保留 V4.4 全部代码（AIBUSDevice/ModbusDevice/SerialPortManager），向后兼容
//   5. 设备模板目录：DLL 同级 devices/ 文件夹
//
// 编译命令:
//   csc.exe /target:library /out:NovaCOMPluginV4.5.dll /reference:System.Web.Extensions.dll NovaCOMPluginV4.5.cs

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Web.Script.Serialization;  // 需要引用 System.Web.Extensions.dll

namespace NovaCOMPlugin
{
    // ================================================================
    //  V4.4 保留：枚举与数据类型
    // ================================================================
    public enum StateCode
    {
        Error = -1,
        Heating = 0,
        Reaching = 1,
        Stabilizing = 2,
        Stable = 3
    }

    public struct StateSnapshot
    {
        public StateCode Code;
        public float PV;
        public float SV;
        public float Diff;
        public int Elapsed;
        public int TotalTime;
        public int HistoryCount;
        public int FailCount;
        public string Raw;

        public bool IsStable { get { return Code == StateCode.Stable; } }
        public bool IsError { get { return Code == StateCode.Error; } }
        public bool IsHeating { get { return Code == StateCode.Heating; } }
        public bool IsReaching { get { return Code == StateCode.Reaching; } }
    }

    public class TemperatureData
    {
        public float PV;
        public float SV;
        public TemperatureData(float pv, float sv) { PV = pv; SV = sv; }
    }

    // ================================================================
    //  V4.4 保留：稳定性控制器
    // ================================================================
    public class StabilityController
    {
        public float ReachThreshold = 0.5f;
        public int StabilizeTime = 60;
        public float MaxDeviation = 1.0f;
        public float AvgDeviation = 0.5f;
        public int ConsecutiveFail = 3;
        public float LastPV = 0;
        public float LastSV = 0;
        public float LastDiff = 0;
        public int HistoryCount { get { return _history.Count; } }
        public int FailCount { get { return _failCount; } }

        private enum State { HEATING, REACHING, STABILIZING, STABLE }
        private State _currentState = State.HEATING;
        private DateTime _reachTime;
        private Queue<float> _history = new Queue<float>();
        private int _failCount = 0;

        public StateCode Update(float pv, float sv)
        {
            float diff = Math.Abs(pv - sv);
            LastPV = pv; LastSV = sv; LastDiff = diff;
            _history.Enqueue(diff);
            while (_history.Count > StabilizeTime) _history.Dequeue();

            float maxDiff = 0, sumDiff = 0;
            foreach (float d in _history) { if (d > maxDiff) maxDiff = d; sumDiff += d; }
            float avgDiff = _history.Count > 0 ? sumDiff / _history.Count : 0;

            switch (_currentState)
            {
                case State.HEATING:
                    if (diff <= ReachThreshold) { _currentState = State.REACHING; _reachTime = DateTime.Now; _failCount = 0; return StateCode.Reaching; }
                    return StateCode.Heating;
                case State.REACHING:
                    double elapsed = (DateTime.Now - _reachTime).TotalSeconds;
                    if (elapsed >= StabilizeTime) { _currentState = State.STABILIZING; goto case State.STABILIZING; }
                    if (diff > ReachThreshold) { _currentState = State.HEATING; return StateCode.Heating; }
                    return StateCode.Reaching;
                case State.STABILIZING:
                    if (CheckStable(diff, maxDiff, avgDiff)) { _currentState = State.STABLE; return StateCode.Stable; }
                    _currentState = State.HEATING; _failCount = 0; return StateCode.Heating;
                case State.STABLE:
                    if (diff > MaxDeviation) { _currentState = State.HEATING; _failCount = 0; return StateCode.Heating; }
                    return StateCode.Stable;
                default: return StateCode.Error;
            }
        }

        private bool CheckStable(float cur, float max, float avg)
        {
            if (cur > ReachThreshold) { _failCount++; if (_failCount >= ConsecutiveFail) return false; }
            else _failCount = 0;
            if (_history.Count < StabilizeTime * 0.2f) return false;
            if (max > MaxDeviation) return false;
            if (avg > AvgDeviation) return false;
            return true;
        }

        public void Reset() { _currentState = State.HEATING; _history.Clear(); _failCount = 0; }

        public StateSnapshot GetSnapshot()
        {
            int elapsed = 0;
            if (_currentState == State.REACHING) elapsed = (int)(DateTime.Now - _reachTime).TotalSeconds;
            return new StateSnapshot
            {
                Code = (StateCode)_currentState, PV = LastPV, SV = LastSV, Diff = LastDiff,
                Elapsed = elapsed, TotalTime = StabilizeTime, HistoryCount = HistoryCount,
                FailCount = FailCount, Raw = _currentState.ToString()
            };
        }
    }

    // ================================================================
    //  V4.4 保留：串口管理器
    // ================================================================
    public static class SerialPortManager
    {
        private static SerialPort _serialPort;
        private static bool _isOpen = false;
        private static readonly object _lock = new object();

        public static string Open(string portName, string baudRate)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen) return "OK";
                int baud = int.Parse(baudRate);
                _serialPort = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 1000;
                _serialPort.WriteTimeout = 1000;
                _serialPort.Open();
                _isOpen = true;
                return "OK";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        public static string Close()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen) _serialPort.Close();
                _serialPort = null; _isOpen = false; return "OK";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        public static SerialPort GetPort() { return _serialPort; }
        public static bool IsOpen { get { return _isOpen; } }

        public static void ClearBuffers()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            { _serialPort.DiscardInBuffer(); _serialPort.DiscardOutBuffer(); }
        }

        public static string SendCommand(string command)
        {
            if (!IsOpen) return "ERR:Closed";
            try
            {
                command = command.Trim();
                if (IsHexEscape(command)) return SendHexEscape(command);
                if (IsPureHex(command)) return SendPureHex(command);
                return SendAscii(command);
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        private static bool IsHexEscape(string s) { return s.Contains("\\x") || s.Contains("\\X"); }
        private static bool IsPureHex(string s)
        {
            s = s.Replace(" ", "").Replace("\t", "");
            if (s.Length % 2 != 0 || s.Length < 4) return false;
            foreach (char c in s) if (!IsHexChar(c)) return false;
            return true;
        }
        private static bool IsHexChar(char c) { return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f'); }

        private static string SendAscii(string text)
        {
            _serialPort.Write(Encoding.ASCII.GetBytes(text + "\r\n"), 0, text.Length + 2);
            return ReadResponse("A:" + text);
        }
        private static string SendHexEscape(string hex)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hex.Length; i++)
            {
                if (i + 3 < hex.Length && hex[i] == '\\' && (hex[i + 1] == 'x' || hex[i + 1] == 'X'))
                { sb.Append(hex[i + 2]); sb.Append(hex[i + 3]); i += 3; }
            }
            return SendPureHex(sb.ToString());
        }
        private static string SendPureHex(string hex)
        {
            hex = hex.Replace(" ", "").Replace("\t", "");
            if (hex.Length % 2 != 0) return "ERR:HexLen";
            byte[] data = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2) data[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            _serialPort.Write(data, 0, data.Length);
            return ReadResponse("H:" + hex);
        }
        private static string ReadResponse(string sent)
        {
            System.Threading.Thread.Sleep(100);
            byte[] buf = new byte[256]; int n = 0;
            try { n = _serialPort.Read(buf, 0, 256); } catch (TimeoutException) { }
            if (n == 0) return sent + "|";
            bool ascii = true;
            for (int i = 0; i < n; i++) if ((buf[i] < 32 && buf[i] != 13 && buf[i] != 10) || buf[i] > 126) { ascii = false; break; }
            if (ascii) return sent + "|" + Encoding.ASCII.GetString(buf, 0, n).Trim();
            StringBuilder h = new StringBuilder();
            for (int i = 0; i < n; i++) h.Append(buf[i].ToString("X2"));
            return sent + "|" + h.ToString();
        }
        public static string TestEcho(string input) { return input; }
    }

    // ================================================================
    //  V4.4 保留：设备抽象基类
    // ================================================================
    public abstract class DeviceBase
    {
        protected int _addr = 1;
        private readonly StabilityController _stability = new StabilityController();
        protected float _lastTargetSV = 0;

        public float ReachThreshold { get { return _stability.ReachThreshold; } set { _stability.ReachThreshold = value; } }
        public int StabilizeTime { get { return _stability.StabilizeTime; } set { _stability.StabilizeTime = value; } }
        public float MaxDeviation { get { return _stability.MaxDeviation; } set { _stability.MaxDeviation = value; } }
        public float AvgDeviation { get { return _stability.AvgDeviation; } set { _stability.AvgDeviation = value; } }
        public int ConsecutiveFail { get { return _stability.ConsecutiveFail; } set { _stability.ConsecutiveFail = value; } }
        public float LastPV { get { return _stability.LastPV; } }
        public float LastSV { get { return _stability.LastSV; } }
        public float LastDiff { get { return _stability.LastDiff; } }
        public int HistoryCount { get { return _stability.HistoryCount; } }
        public int FailCount { get { return _stability.FailCount; } }
        public float LastTargetSV { get { return _lastTargetSV; } }
        protected StabilityController Stability { get { return _stability; } }

        public DeviceBase() { }

        public string SetAddress(string addr)
        {
            int a;
            if (int.TryParse(addr, out a) && IsValidAddress(a)) { _addr = a; return "OK"; }
            return "ERR:Addr" + GetAddressRange();
        }

        public string SendCommand(string command)
        {
            if (!SerialPortManager.IsOpen) return "ERR:Closed";
            try { return ExecuteCommand(command.Trim().ToUpper()); }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        public string CheckStability()
        {
            TemperatureData pvsv = ReadPVSV();
            if (pvsv == null) return "ERR";
            if (Math.Abs(pvsv.SV - _lastTargetSV) > 1.0f) { _stability.Reset(); _lastTargetSV = pvsv.SV; }
            StateCode code = _stability.Update(pvsv.PV, pvsv.SV);
            switch (code)
            {
                case StateCode.Heating: return "H";
                case StateCode.Reaching: return "R";
                case StateCode.Stable: return "OK";
                case StateCode.Error: return "ERR";
                default: return "H";
            }
        }

        public StateCode CheckStabilityCode()
        {
            string r = CheckStability();
            switch (r) { case "H": return StateCode.Heating; case "R": return StateCode.Reaching; case "OK": return StateCode.Stable; default: return StateCode.Error; }
        }

        public StateSnapshot GetState() { CheckStability(); return _stability.GetSnapshot(); }
        public string ResetStability() { _stability.Reset(); return "OK"; }

        protected abstract TemperatureData ReadPVSV();
        protected abstract string ExecuteCommand(string command);
        protected abstract bool IsValidAddress(int addr);
        protected abstract string GetAddressRange();
    }

    // ================================================================
    //  V4.4 保留：AIBUS 设备（硬编码，向后兼容）
    // ================================================================
    public class AIBUSDevice : DeviceBase
    {
        protected override bool IsValidAddress(int addr) { return addr >= 1 && addr <= 80; }
        protected override string GetAddressRange() { return "1-80"; }
        protected override string ExecuteCommand(string command)
        {
            if (command == "READ" || command == "STATUS") return Read();
            if (command.StartsWith("SET ")) return ParseSet(command);
            return "ERR:Cmd";
        }
        protected override TemperatureData ReadPVSV()
        {
            string result = Read();
            if (!result.StartsWith("PV=")) return null;
            string[] p = result.Split(',');
            float pv = float.Parse(p[0].Substring(3));
            float sv = float.Parse(p[1].Substring(3));
            return new TemperatureData(pv, sv);
        }
        private string Read()
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer();
            System.Threading.Thread.Sleep(50);
            port.Write(BuildReadFrame(_addr, 0x00), 0, 8);
            byte[] resp = new byte[10]; int n = 0; int wait = 0;
            while (n < 10 && wait < 500)
            {
                if (port.BytesToRead > 0) { int r = Math.Min(port.BytesToRead, 10 - n); n += port.Read(resp, n, r); }
                else { System.Threading.Thread.Sleep(10); wait += 10; }
            }
            if (n == 10) return ParseResponse(resp);
            if (n > 0) return "ERR:Inc" + n;
            return "ERR:NoRsp";
        }
        private string ParseSet(string cmd)
        {
            string[] p = cmd.Split(' ');
            float sv;
            if (p.Length >= 2 && float.TryParse(p[1], out sv)) return WriteSV(sv);
            return "ERR:SetFmt";
        }
        private string WriteSV(float sv)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            ushort v = (ushort)(sv * 10);
            port.DiscardInBuffer();
            System.Threading.Thread.Sleep(50);
            port.Write(BuildWriteSVFrame(_addr, v), 0, 8);
            byte[] resp = new byte[10]; int n = 0; int wait = 0;
            while (n < 10 && wait < 500)
            {
                if (port.BytesToRead > 0) { int r = Math.Min(port.BytesToRead, 10 - n); n += port.Read(resp, n, r); }
                else { System.Threading.Thread.Sleep(10); wait += 10; }
            }
            if (n >= 8 && resp[2] == 0x43) { _lastTargetSV = sv; ResetStability(); return "OK"; }
            if (n > 0) return "ERR:Wrt" + n;
            return "ERR:WrtFail";
        }
        private byte[] BuildReadFrame(int addr, byte param)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x52, param, 0, 0, 0, 0 };
            ushort chk = (ushort)((param * 256 + 0x52 + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF); f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }
        private byte[] BuildWriteSVFrame(int addr, ushort val)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x43, 0, (byte)(val & 0xFF), (byte)((val >> 8) & 0xFF), 0, 0 };
            ushort chk = (ushort)((0 * 256 + 0x43 + val + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF); f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }
        private string ParseResponse(byte[] r)
        {
            ushort pv = (ushort)(r[0] | (r[1] << 8));
            ushort sv = (ushort)(r[2] | (r[3] << 8));
            return "PV=" + (pv / 10.0f).ToString("F1") + ",SV=" + (sv / 10.0f).ToString("F1") + ",MV=" + r[4] + ",ST=" + r[5].ToString("X2");
        }
    }

    // ================================================================
    //  V4.4 保留：Modbus 设备（硬编码，向后兼容）
    // ================================================================
    public class ModbusDevice : DeviceBase
    {
        public ushort PVRegister = 0;
        public ushort SVRegister = 1;
        protected override bool IsValidAddress(int addr) { return addr >= 1 && addr <= 247; }
        protected override string GetAddressRange() { return "1-247"; }
        protected override string ExecuteCommand(string command)
        {
            string[] p = command.Split(' ');
            if (command.StartsWith("READ ") && p.Length >= 3) return ReadHold(p[1], p[2]);
            if (command.StartsWith("WRITE ") && p.Length >= 3) return WriteHold(p[1], p[2]);
            return "ERR:Cmd";
        }
        protected override TemperatureData ReadPVSV()
        {
            ushort[] result = ReadHoldRaw(PVRegister, 2);
            if (result == null || result.Length < 2) return null;
            float pv = result[0] / 10.0f;
            float sv = result[1] / 10.0f;
            return new TemperatureData(pv, sv);
        }
        private string ReadHold(string reg, string num)
        {
            ushort[] vals = ReadHoldRaw(ushort.Parse(reg), ushort.Parse(num));
            if (vals == null) return "ERR:NoRsp";
            StringBuilder sb = new StringBuilder();
            ushort baseReg = ushort.Parse(reg);
            for (int i = 0; i < vals.Length; i++) { sb.Append("R"); sb.Append((baseReg + i).ToString()); sb.Append("="); sb.Append(vals[i].ToString()); sb.Append(";"); }
            return sb.ToString().TrimEnd(';');
        }
        private ushort[] ReadHoldRaw(ushort reg, ushort num)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return null;
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(BuildReadFrame(_addr, reg, num), 0, 8); System.Threading.Thread.Sleep(50);
            byte[] h = new byte[3];
            if (port.Read(h, 0, 3) < 3) return null;
            byte bc = h[2];
            byte[] resp = new byte[3 + bc + 2];
            resp[0] = h[0]; resp[1] = h[1]; resp[2] = h[2];
            if (port.Read(resp, 3, bc + 2) < bc + 2) return null;
            ushort[] vals = new ushort[bc / 2];
            for (int i = 0; i < bc / 2; i++) vals[i] = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);
            return vals;
        }
        private string WriteHold(string reg, string val)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            ushort r = ushort.Parse(reg); ushort v = ushort.Parse(val);
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(BuildWriteFrame(_addr, r, v), 0, 8); System.Threading.Thread.Sleep(50);
            byte[] resp = new byte[8];
            return port.Read(resp, 0, 8) == 8 ? "OK" : "ERR:Wrt";
        }
        private byte[] BuildReadFrame(int addr, ushort reg, ushort num)
        {
            byte[] f = new byte[6] { (byte)addr, 0x03, (byte)(reg >> 8), (byte)reg, (byte)(num >> 8), (byte)num };
            ushort crc = CRC16(f, 6);
            return new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }
        private byte[] BuildWriteFrame(int addr, ushort reg, ushort val)
        {
            byte[] f = new byte[6] { (byte)addr, 0x06, (byte)(reg >> 8), (byte)reg, (byte)(val >> 8), (byte)val };
            ushort crc = CRC16(f, 6);
            return new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }
        private ushort CRC16(byte[] d, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= d[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                    else crc = (ushort)(crc >> 1);
                }
            }
            return crc;
        }
    }

    // ================================================================
    //  V4.4 保留：设备辅助类
    // ================================================================
    public static class DeviceHelper
    {
        public static bool IsStable(DeviceBase dev) { return dev != null && dev.CheckStabilityCode() == StateCode.Stable; }
        public static bool IsError(DeviceBase dev) { return dev == null || dev.CheckStabilityCode() == StateCode.Error; }
        public static bool IsHeating(DeviceBase dev) { return dev == null || dev.CheckStabilityCode() == StateCode.Heating; }
        public static string GetTempString(DeviceBase dev)
        {
            if (dev == null) return "ERR";
            dev.CheckStability();
            return dev.LastPV.ToString("F1") + "/" + dev.LastSV.ToString("F1");
        }
        public static string WaitUntilStable(DeviceBase dev, int timeoutSec)
        {
            if (dev == null) return "ERR:Null";
            dev.ResetStability();
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < timeoutSec)
            {
                StateCode code = dev.CheckStabilityCode();
                if (code == StateCode.Stable) return "OK";
                if (code == StateCode.Error) return "ERR:Device";
                System.Threading.Thread.Sleep(1000);
            }
            return "ERR:Timeout";
        }
    }


    // ================================================================
    //  V4.5 新增：JSON 配置数据模型
    // ================================================================
    public class RegisterDef
    {
        public string action { get; set; }       // read / write
        public int? fc { get; set; }             // Modbus 功能码
        public int? addr { get; set; }           // Modbus 寄存器地址
        public string param { get; set; }        // AIBUS 参数码 (如 "0x00")
        public string service { get; set; }      // SevenStar: read/write
        public string cls { get; set; }          // SevenStar Class
        public string instance { get; set; }     // SevenStar Instance
        public string attribute { get; set; }    // SevenStar Attribute
        public string type { get; set; }         // uint16 / int16 / float / ufrac16 / string
        public double scale { get; set; }        // 缩放系数
        public string unit { get; set; }
        public int? length { get; set; }         // string 长度
        public List<string> bytes { get; set; }  // FixedFrame 固定字节
        public string checksum { get; set; }     // sum8 / xor8 / none
    }

    public class CommandDef
    {
        public string action { get; set; }       // read / write / write_coil / read_coil / read_multi
        public string register { get; set; }     // 关联 registers 中的 key
        public string input { get; set; }        // float / int
        public string addr_expr { get; set; }    // 地址表达式如 "N-1"
        public int? value { get; set; }          // 固定值
        public List<string> @params { get; set; } // 参数名列表
        public List<string> bytes { get; set; }  // FixedFrame 固定字节序列
        public string template { get; set; }     // FixedFrame 模板
    }

    public class DeviceProfile
    {
        public string name { get; set; }
        public string protocol { get; set; }     // modbus-rtu / aibus / fixed-frame / sevenstar
        public int default_baudrate { get; set; }
        public int default_address { get; set; }
        public double? full_scale { get; set; }  // SevenStar 专用
        public string gas_type { get; set; }     // SevenStar 专用
        public Dictionary<string, RegisterDef> registers { get; set; }
        public Dictionary<string, CommandDef> commands { get; set; }
    }

    // ================================================================
    //  V4.5 新增：Profile 加载器
    // ================================================================
    public static class ProfileLoader
    {
        private static Dictionary<string, DeviceProfile> _cache = new Dictionary<string, DeviceProfile>();
        private static string _profileDir = null;

        public static string ProfileDirectory
        {
            get
            {
                if (_profileDir == null)
                {
                    string dllPath = typeof(ProfileLoader).Assembly.Location;
                    _profileDir = Path.Combine(Path.GetDirectoryName(dllPath), "devices");
                }
                return _profileDir;
            }
            set { _profileDir = value; }
        }

        public static DeviceProfile Load(string profileName)
        {
            if (_cache.ContainsKey(profileName)) return _cache[profileName];
            string path = Path.Combine(ProfileDirectory, profileName);
            if (!File.Exists(path))
            {
                // 尝试自动加 .json 后缀
                path = Path.Combine(ProfileDirectory, profileName + ".json");
                if (!File.Exists(path)) return null;
            }
            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var serializer = new JavaScriptSerializer();
                var profile = serializer.Deserialize<DeviceProfile>(json);
                _cache[profileName] = profile;
                return profile;
            }
            catch { return null; }
        }

        public static List<string> ListProfiles()
        {
            var result = new List<string>();
            if (!Directory.Exists(ProfileDirectory)) return result;
            foreach (var f in Directory.GetFiles(ProfileDirectory, "*.json"))
                result.Add(Path.GetFileNameWithoutExtension(f));
            return result;
        }

        public static void ClearCache() { _cache.Clear(); }
    }

    // ================================================================
    //  V4.5 新增：协议引擎抽象基类
    // ================================================================
    public abstract class ProtocolEngine
    {
        protected DeviceProfile Profile;
        protected int Address;
        protected SerialPort Port { get { return SerialPortManager.GetPort(); } }

        public virtual void Init(DeviceProfile profile, int address)
        {
            Profile = profile;
            Address = address;
        }

        public abstract string Execute(string command, string args);
        public abstract TemperatureData ReadPVSV();

        // 通用辅助
        protected static byte[] HexToBytes(string hex)
        {
            hex = hex.Replace("0x", "").Replace(" ", "").Replace("\t", "");
            if (hex.Length % 2 != 0) return new byte[0];
            byte[] result = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
                result[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            return result;
        }

        protected static int ParseHexOrInt(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x") || s.StartsWith("0X"))
                return Convert.ToInt32(s, 16);
            return int.Parse(s);
        }
    }

    // ================================================================
    //  V4.5 新增：Modbus 引擎（配置化）
    // ================================================================
    public class ModbusEngine : ProtocolEngine
    {
        public override string Execute(string command, string args)
        {
            // 优先查 JSON 配置命令
            if (Profile != null && Profile.commands != null && Profile.commands.ContainsKey(command))
            {
                var cmdDef = Profile.commands[command];
                if (cmdDef.action == "read" && cmdDef.register != null)
                {
                    var reg = Profile.registers[cmdDef.register];
                    ushort[] vals = ReadHoldRaw((ushort)(reg.addr ?? 0), 1);
                    if (vals == null) return "ERR:NoRsp";
                    float val = vals[0] * (float)reg.scale;
                    return cmdDef.register + "=" + val.ToString("F1") + reg.unit;
                }
                if (cmdDef.action == "read_multi" && cmdDef.register != null)
                {
                    var reg = Profile.registers[cmdDef.register];
                    int count = 1;
                    if (reg.type == "float" || reg.type == "ufrac16") count = 2;
                    ushort[] vals = ReadHoldRaw((ushort)(reg.addr ?? 0), (ushort)count);
                    if (vals == null) return "ERR:NoRsp";
                    // 简化解码：只返回原始值
                    StringBuilder sb = new StringBuilder();
                    for (int i = 0; i < vals.Length; i++) { sb.Append(vals[i]); sb.Append(";"); }
                    return sb.ToString().TrimEnd(';');
                }
                if (cmdDef.action == "write" && cmdDef.register != null)
                {
                    var reg = Profile.registers[cmdDef.register];
                    float val;
                    if (!float.TryParse(args, out val)) return "ERR:ValueFmt";
                    int raw = (int)(val / reg.scale);
                    return WriteHoldRaw((ushort)(reg.addr ?? 0), (ushort)raw);
                }
                if (cmdDef.action == "write_coil")
                {
                    int coilAddr = 0;
                    if (cmdDef.addr_expr != null)
                    {
                        // 解析 N-1 表达式
                        string[] p = args.Split('=');
                        if (p.Length >= 2) { int N = int.Parse(p[1]); coilAddr = N - 1; }
                    }
                    else if (cmdDef.value.HasValue)
                    {
                        coilAddr = cmdDef.value.Value;
                    }
                    return WriteCoilRaw((ushort)coilAddr, true);
                }
                if (cmdDef.action == "read_coil")
                {
                    var reg = Profile.registers[cmdDef.register];
                    int count = reg.addr ?? 8;
                    return ReadCoilRaw(0, (ushort)count);
                }
            }

            // 兜底：原始 Modbus 命令
            var parts = command.Split(' ');
            if (parts.Length >= 3 && parts[0] == "READ_HOLD")
            {
                ushort[] vals = ReadHoldRaw(ushort.Parse(parts[1]), ushort.Parse(parts[2]));
                if (vals == null) return "ERR:NoRsp";
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < vals.Length; i++) { sb.Append("R"); sb.Append((ushort.Parse(parts[1]) + i).ToString()); sb.Append("="); sb.Append(vals[i]); sb.Append(";"); }
                return sb.ToString().TrimEnd(';');
            }
            if (parts.Length >= 3 && parts[0] == "WRITE_HOLD")
                return WriteHoldRaw(ushort.Parse(parts[1]), ushort.Parse(parts[2]));
            if (parts.Length >= 2 && parts[0] == "READ_COIL")
                return ReadCoilRaw(ushort.Parse(parts[1]), 1);
            if (parts.Length >= 3 && parts[0] == "WRITE_COIL")
                return WriteCoilRaw(ushort.Parse(parts[1]), parts[2] == "1" || parts[2].ToUpper() == "TRUE");

            return "ERR:UnknownCmd";
        }

        public override TemperatureData ReadPVSV()
        {
            if (Profile != null && Profile.registers != null && Profile.registers.ContainsKey("pv") && Profile.registers.ContainsKey("sv"))
            {
                var pvReg = Profile.registers["pv"];
                var svReg = Profile.registers["sv"];
                ushort[] vals = ReadHoldRaw((ushort)(pvReg.addr ?? 0), 2);
                if (vals == null || vals.Length < 2) return null;
                float pv = vals[0] * (float)pvReg.scale;
                float sv = vals[1] * (float)svReg.scale;
                return new TemperatureData(pv, sv);
            }
            // 默认回退
            ushort[] v = ReadHoldRaw(0, 2);
            if (v == null || v.Length < 2) return null;
            return new TemperatureData(v[0] / 10.0f, v[1] / 10.0f);
        }

        // ---- Modbus RTU 底层 ----
        private ushort[] ReadHoldRaw(ushort reg, ushort num)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return null;
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(BuildReadFrame(Address, reg, num), 0, 8); System.Threading.Thread.Sleep(50);
            byte[] h = new byte[3];
            if (port.Read(h, 0, 3) < 3) return null;
            byte bc = h[2];
            byte[] resp = new byte[3 + bc + 2];
            resp[0] = h[0]; resp[1] = h[1]; resp[2] = h[2];
            if (port.Read(resp, 3, bc + 2) < bc + 2) return null;
            ushort[] vals = new ushort[bc / 2];
            for (int i = 0; i < bc / 2; i++) vals[i] = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);
            return vals;
        }

        private string WriteHoldRaw(ushort reg, ushort val)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(BuildWriteFrame(Address, reg, val), 0, 8); System.Threading.Thread.Sleep(50);
            byte[] resp = new byte[8];
            return port.Read(resp, 0, 8) == 8 ? "OK" : "ERR:Wrt";
        }

        private string ReadCoilRaw(ushort start, ushort count)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            byte[] f = new byte[6] { (byte)Address, 0x01, (byte)(start >> 8), (byte)start, (byte)(count >> 8), (byte)count };
            ushort crc = CRC16(f, 6);
            byte[] frame = new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
            port.Write(frame, 0, 8); System.Threading.Thread.Sleep(50);
            byte[] h = new byte[3];
            if (port.Read(h, 0, 3) < 3) return "ERR:NoRsp";
            byte bc = h[2];
            byte[] resp = new byte[3 + bc + 2];
            resp[0] = h[0]; resp[1] = h[1]; resp[2] = h[2];
            if (port.Read(resp, 3, bc + 2) < bc + 2) return "ERR:Incomplete";
            StringBuilder sb = new StringBuilder("COILS=");
            for (int i = 0; i < count && i < bc * 8; i++)
            {
                int byteIdx = 3 + i / 8;
                int bitIdx = i % 8;
                bool state = (resp[byteIdx] & (1 << bitIdx)) != 0;
                sb.Append(state ? "1" : "0");
            }
            return sb.ToString();
        }

        private string WriteCoilRaw(ushort addr, bool state)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            byte[] f = new byte[6] { (byte)Address, 0x05, (byte)(addr >> 8), (byte)addr, state ? (byte)0xFF : (byte)0x00, 0x00 };
            ushort crc = CRC16(f, 6);
            byte[] frame = new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
            port.Write(frame, 0, 8); System.Threading.Thread.Sleep(50);
            byte[] resp = new byte[8];
            return port.Read(resp, 0, 8) == 8 ? "OK" : "ERR:Wrt";
        }

        private byte[] BuildReadFrame(int addr, ushort reg, ushort num)
        {
            byte[] f = new byte[6] { (byte)addr, 0x03, (byte)(reg >> 8), (byte)reg, (byte)(num >> 8), (byte)num };
            ushort crc = CRC16(f, 6);
            return new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }
        private byte[] BuildWriteFrame(int addr, ushort reg, ushort val)
        {
            byte[] f = new byte[6] { (byte)addr, 0x06, (byte)(reg >> 8), (byte)reg, (byte)(val >> 8), (byte)val };
            ushort crc = CRC16(f, 6);
            return new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }
        private ushort CRC16(byte[] d, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= d[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0) crc = (ushort)((crc >> 1) ^ 0xA001);
                    else crc = (ushort)(crc >> 1);
                }
            }
            return crc;
        }
    }

    // ================================================================
    //  V4.5 新增：AIBUS 引擎（配置化）
    // ================================================================
    public class AIBUSEngine : ProtocolEngine
    {
        public override string Execute(string command, string args)
        {
            // JSON 配置命令
            if (Profile != null && Profile.commands != null && Profile.commands.ContainsKey(command))
            {
                var cmdDef = Profile.commands[command];
                if (cmdDef.action == "read" && cmdDef.register != null && Profile.registers.ContainsKey(cmdDef.register))
                {
                    var reg = Profile.registers[cmdDef.register];
                    return Read((byte)ParseHexOrInt(reg.param));
                }
                if (cmdDef.action == "write" && cmdDef.register != null && Profile.registers.ContainsKey(cmdDef.register))
                {
                    var reg = Profile.registers[cmdDef.register];
                    float val;
                    if (!float.TryParse(args, out val)) return "ERR:ValueFmt";
                    return WriteSV(val, (byte)ParseHexOrInt(reg.param), (float)reg.scale);
                }
            }

            // 兜底：原始命令
            if (command == "READ" || command == "read_pv" || command == "STATUS")
                return Read(0x00);
            if (command.StartsWith("SET ") || command == "set_sv")
            {
                float sv;
                string valStr = command.StartsWith("SET ") ? command.Substring(4) : args;
                if (float.TryParse(valStr, out sv)) return WriteSV(sv, 0x01, 10.0f);
                return "ERR:SetFmt";
            }
            return "ERR:Cmd";
        }

        public override TemperatureData ReadPVSV()
        {
            string result = Read(0x00);
            if (!result.StartsWith("PV=")) return null;
            string[] p = result.Split(',');
            float pv = float.Parse(p[0].Substring(3));
            float sv = float.Parse(p[1].Substring(3));
            return new TemperatureData(pv, sv);
        }

        private string Read(byte param)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(BuildReadFrame(Address, param), 0, 8);
            byte[] resp = new byte[10]; int n = 0; int wait = 0;
            while (n < 10 && wait < 500)
            {
                if (port.BytesToRead > 0) { int r = Math.Min(port.BytesToRead, 10 - n); n += port.Read(resp, n, r); }
                else { System.Threading.Thread.Sleep(10); wait += 10; }
            }
            if (n == 10) return ParseResponse(resp);
            if (n > 0) return "ERR:Inc" + n;
            return "ERR:NoRsp";
        }

        private string WriteSV(float sv, byte param, float scale)
        {
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            ushort v = (ushort)(sv * scale);
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(BuildWriteSVFrame(Address, param, v), 0, 8);
            byte[] resp = new byte[10]; int n = 0; int wait = 0;
            while (n < 10 && wait < 500)
            {
                if (port.BytesToRead > 0) { int r = Math.Min(port.BytesToRead, 10 - n); n += port.Read(resp, n, r); }
                else { System.Threading.Thread.Sleep(10); wait += 10; }
            }
            if (n >= 8 && resp[2] == 0x43) return "OK";
            if (n > 0) return "ERR:Wrt" + n;
            return "ERR:WrtFail";
        }

        private byte[] BuildReadFrame(int addr, byte param)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x52, param, 0, 0, 0, 0 };
            ushort chk = (ushort)((param * 256 + 0x52 + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF); f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }
        private byte[] BuildWriteSVFrame(int addr, byte param, ushort val)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x43, param, (byte)(val & 0xFF), (byte)((val >> 8) & 0xFF), 0, 0 };
            ushort chk = (ushort)((param * 256 + 0x43 + val + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF); f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }
        private string ParseResponse(byte[] r)
        {
            ushort pv = (ushort)(r[0] | (r[1] << 8));
            ushort sv = (ushort)(r[2] | (r[3] << 8));
            return "PV=" + (pv / 10.0f).ToString("F1") + ",SV=" + (sv / 10.0f).ToString("F1") + ",MV=" + r[4] + ",ST=" + r[5].ToString("X2");
        }
    }


    // ================================================================
    //  V4.5 新增：固定帧引擎
    // ================================================================
    public class FixedFrameEngine : ProtocolEngine
    {
        public override string Execute(string command, string args)
        {
            if (Profile == null || Profile.commands == null || !Profile.commands.ContainsKey(command))
                return "ERR:UnknownCmd";

            var cmdDef = Profile.commands[command];
            byte[] frame;

            // 方式1：固定字节序列
            if (cmdDef.bytes != null && cmdDef.bytes.Count > 0)
            {
                frame = BuildBytes(cmdDef.bytes);
            }
            // 方式2：模板帧（带参数解析）
            else if (cmdDef.template != null)
            {
                frame = BuildFromTemplate(cmdDef.template, args);
            }
            else if (cmdDef.action == "write" && cmdDef.addr_expr != null)
            {
                // 解析参数如 "N=3,state=1"
                var dict = ParseArgs(args);
                int N = GetIntArg(dict, "N", 1);
                int state = GetIntArg(dict, "state", 0);
                int addr = N - 1;  // 默认 N-1
                frame = BuildRelayFrame((byte)addr, (byte)state);
            }
            else
            {
                return "ERR:BadCmdDef";
            }

            // 追加校验
            frame = AppendChecksum(frame);

            // 发送
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(frame, 0, frame.Length);

            // 读取响应（简单处理）
            System.Threading.Thread.Sleep(100);
            byte[] buf = new byte[256]; int n = 0;
            try { n = port.Read(buf, 0, 256); } catch (TimeoutException) { }
            if (n == 0) return "OK|NoRsp";  // 很多继电器不返回
            StringBuilder h = new StringBuilder("OK|");
            for (int i = 0; i < n; i++) h.Append(buf[i].ToString("X2"));
            return h.ToString();
        }

        public override TemperatureData ReadPVSV()
        {
            // 固定帧设备通常不是温控器
            return null;
        }

        private byte[] BuildBytes(List<string> byteList)
        {
            List<byte> result = new List<byte>();
            foreach (var s in byteList)
                result.AddRange(HexToBytes(s));
            return result.ToArray();
        }

        private byte[] BuildFromTemplate(string template, string args)
        {
            // 简单模板解析：{header},{channel},{reserve},{state}
            var parts = template.Split(',');
            var argDict = ParseArgs(args);
            List<byte> result = new List<byte>();
            foreach (var part in parts)
            {
                string p = part.Trim();
                if (p.StartsWith("{") && p.EndsWith("}"))
                {
                    string key = p.Substring(1, p.Length - 2);
                    if (key == "channel" || key == "N")
                    {
                        int N = GetIntArg(argDict, "N", GetIntArg(argDict, "channel", 0));
                        result.Add((byte)(N - 1));
                    }
                    else if (key == "state")
                    {
                        result.Add((byte)GetIntArg(argDict, "state", 0));
                    }
                    else if (key == "header" && Profile.registers != null && Profile.registers.ContainsKey("header"))
                    {
                        result.AddRange(HexToBytes(Profile.registers["header"].bytes[0]));
                    }
                    else
                    {
                        // 尝试从参数查找
                        if (argDict.ContainsKey(key))
                            result.Add((byte)int.Parse(argDict[key]));
                    }
                }
                else
                {
                    result.AddRange(HexToBytes(p));
                }
            }
            return result.ToArray();
        }

        private byte[] BuildRelayFrame(byte channel, byte state)
        {
            byte header = 0xA0;
            if (Profile != null && Profile.registers != null && Profile.registers.ContainsKey("header"))
                header = HexToBytes(Profile.registers["header"].bytes[0])[0];
            return new byte[] { header, channel, 0x00, state };
        }

        private byte[] AppendChecksum(byte[] data)
        {
            string csType = "none";
            if (Profile != null && Profile.registers != null && Profile.registers.ContainsKey("checksum"))
                csType = Profile.registers["checksum"].checksum ?? "none";

            if (csType == "sum8")
            {
                byte sum = 0;
                foreach (byte b in data) sum += b;
                byte[] result = new byte[data.Length + 1];
                Array.Copy(data, result, data.Length);
                result[data.Length] = sum;
                return result;
            }
            else if (csType == "xor8")
            {
                byte xor = 0;
                foreach (byte b in data) xor ^= b;
                byte[] result = new byte[data.Length + 1];
                Array.Copy(data, result, data.Length);
                result[data.Length] = xor;
                return result;
            }
            return data;
        }

        private Dictionary<string, string> ParseArgs(string args)
        {
            var dict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(args)) return dict;
            var pairs = args.Split(',');
            foreach (var pair in pairs)
            {
                var kv = pair.Split('=');
                if (kv.Length == 2) dict[kv[0].Trim()] = kv[1].Trim();
            }
            return dict;
        }

        private int GetIntArg(Dictionary<string, string> dict, string key, int defaultVal)
        {
            if (dict.ContainsKey(key)) { int v; if (int.TryParse(dict[key], out v)) return v; }
            return defaultVal;
        }
    }

    // ================================================================
    //  V4.5 新增：七星华创引擎（CS200A 私有协议）
    // ================================================================
    public class SevenStarEngine : ProtocolEngine
    {
        // 协议常量
        private const byte STX = 0x02;
        private const byte SERVICE_READ = 0x80;
        private const byte SERVICE_WRITE = 0x81;
        private const byte PAD = 0x00;
        private const byte ACK_OK = 0x06;
        private const byte ACK_ERR = 0x15;
        private const ushort UFRAC16_ZERO = 0x4000;
        private const ushort UFRAC16_RANGE = 0x8000;

        private float _fullScale = 100f;
        private string _gasType = "N2";

        public override void Init(DeviceProfile profile, int address)
        {
            base.Init(profile, address);
            if (profile.full_scale.HasValue) _fullScale = (float)profile.full_scale.Value;
            if (!string.IsNullOrEmpty(profile.gas_type)) _gasType = profile.gas_type;
        }

        public override string Execute(string command, string args)
        {
            // JSON 配置命令
            if (Profile != null && Profile.commands != null && Profile.commands.ContainsKey(command))
            {
                var cmdDef = Profile.commands[command];
                if (cmdDef.action == "read" && cmdDef.register != null && Profile.registers.ContainsKey(cmdDef.register))
                {
                    var reg = Profile.registers[cmdDef.register];
                    return SevenStarRead(reg);
                }
                if (cmdDef.action == "write" && cmdDef.register != null && Profile.registers.ContainsKey(cmdDef.register))
                {
                    var reg = Profile.registers[cmdDef.register];
                    float val;
                    if (!float.TryParse(args, out val)) return "ERR:ValueFmt";
                    return SevenStarWrite(reg, val);
                }
                if (cmdDef.action == "read_multi" && cmdDef.register != null)
                {
                    // 简化：逐个读取
                    return "ERR:NotImpl";
                }
            }

            // 兜底：语义化命令
            if (command == "read_flow" || command == "READ_FLOW")
            {
                if (Profile != null && Profile.registers != null && Profile.registers.ContainsKey("flow"))
                    return SevenStarRead(Profile.registers["flow"]);
                // 硬编码回退
                return SevenStarReadDirect(0x68, 0x01, 0xB9, "ufrac16", "sccm");
            }
            if (command == "set_flow" || command == "SET_FLOW")
            {
                float val;
                if (!float.TryParse(args, out val)) return "ERR:ValueFmt";
                if (Profile != null && Profile.registers != null && Profile.registers.ContainsKey("setpoint"))
                    return SevenStarWrite(Profile.registers["setpoint"], val);
                return SevenStarWriteDirect(0x69, 0x01, 0xA4, val, "ufrac16");
            }
            if (command == "read_setpoint" || command == "READ_SETPOINT")
            {
                if (Profile != null && Profile.registers != null && Profile.registers.ContainsKey("active_setpoint"))
                    return SevenStarRead(Profile.registers["active_setpoint"]);
                return SevenStarReadDirect(0x69, 0x01, 0xA5, "ufrac16", "sccm");
            }
            if (command == "read_gas_info" || command == "READ_GAS")
            {
                string name = "N2";
                float fs = _fullScale;
                if (Profile != null && Profile.registers != null)
                {
                    if (Profile.registers.ContainsKey("gas_name"))
                    {
                        var r = Profile.registers["gas_name"];
                        var data = SevenStarReadRaw(r);
                        if (data != null) name = System.Text.Encoding.ASCII.GetString(data).TrimEnd('\0').Trim();
                    }
                    if (Profile.registers.ContainsKey("full_scale"))
                    {
                        var r = Profile.registers["full_scale"];
                        var data = SevenStarReadRaw(r);
                        if (data != null && data.Length >= 2) fs = (ushort)(data[0] | (data[1] << 8));
                    }
                }
                return "GAS=" + name + ",FS=" + fs.ToString("F0");
            }
            if (command == "read_device_info" || command == "READ_INFO")
            {
                return "ADDR=0x" + Address.ToString("X2") + ",GAS=" + _gasType + ",FS=" + _fullScale;
            }
            return "ERR:Cmd";
        }

        public override TemperatureData ReadPVSV()
        {
            // CS200A 不是温控器
            return null;
        }

        // ---- 配置化读写 ----
        private string SevenStarRead(RegisterDef reg)
        {
            byte cls = (byte)ParseHexOrInt(reg.cls ?? "0x00");
            byte inst = (byte)ParseHexOrInt(reg.instance ?? "0x01");
            byte attr = (byte)ParseHexOrInt(reg.attribute ?? "0x00");
            return SevenStarReadDirect(cls, inst, attr, reg.type, reg.unit);
        }

        private string SevenStarWrite(RegisterDef reg, float val)
        {
            byte cls = (byte)ParseHexOrInt(reg.cls ?? "0x00");
            byte inst = (byte)ParseHexOrInt(reg.instance ?? "0x01");
            byte attr = (byte)ParseHexOrInt(reg.attribute ?? "0x00");
            return SevenStarWriteDirect(cls, inst, attr, val, reg.type);
        }

        private byte[] SevenStarReadRaw(RegisterDef reg)
        {
            byte cls = (byte)ParseHexOrInt(reg.cls ?? "0x00");
            byte inst = (byte)ParseHexOrInt(reg.instance ?? "0x01");
            byte attr = (byte)ParseHexOrInt(reg.attribute ?? "0x00");
            var frame = BuildFrame(SERVICE_READ, 3, cls, inst, attr, new byte[0]);
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return null;
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(frame, 0, frame.Length);
            byte[] header = new byte[5];
            if (port.Read(header, 0, 5) < 5) return null;
            byte dataLen = header[3];
            int total = 5 + dataLen + 2;
            int remaining = total - 5;
            if (remaining <= 0) return null;
            byte[] rest = new byte[remaining];
            if (port.Read(rest, 0, remaining) < remaining) return null;
            byte[] full = new byte[total];
            Array.Copy(header, full, 5);
            Array.Copy(rest, 0, full, 5, remaining);
            var parsed = ParseResponseFrame(full);
            if (!parsed.ok) return null;
            return parsed.data;
        }

        // ---- 硬编码读写（回退） ----
        private string SevenStarReadDirect(byte cls, byte inst, byte attr, string dataType, string unit)
        {
            var frame = BuildFrame(SERVICE_READ, 3, cls, inst, attr, new byte[0]);
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(frame, 0, frame.Length);

            byte[] header = new byte[5];
            if (port.Read(header, 0, 5) < 5) return "ERR:NoRsp";
            byte dataLen = header[3];
            int total = 5 + dataLen + 2;
            int remaining = total - 5;
            if (remaining <= 0) return "ERR:BadHdr";
            byte[] rest = new byte[remaining];
            if (port.Read(rest, 0, remaining) < remaining) return "ERR:Incomplete";
            byte[] full = new byte[total];
            Array.Copy(header, full, 5);
            Array.Copy(rest, 0, full, 5, remaining);

            var parsed = ParseResponseFrame(full);
            if (!parsed.ok) return "ERR:" + parsed.error;
            if (parsed.data.Length < 2) return "ERR:NoData";

            ushort raw = (ushort)(parsed.data[0] | (parsed.data[1] << 8));

            if (dataType == "ufrac16")
            {
                float percent = UFrac16ToPercent(raw);
                float sccm = percent / 100f * _fullScale;
                return "VALUE=" + sccm.ToString("F3") + unit + ",RAW=" + raw + ",PCT=" + percent.ToString("F2");
            }
            else if (dataType == "uint16")
            {
                return "VALUE=" + raw + unit;
            }
            else
            {
                return "RAW=" + raw;
            }
        }

        private string SevenStarWriteDirect(byte cls, byte inst, byte attr, float val, string dataType)
        {
            byte[] data;
            if (dataType == "ufrac16")
            {
                float percent = val / _fullScale * 100f;
                ushort raw = PercentToUFrac16(percent);
                data = new byte[] { (byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF) };
            }
            else
            {
                ushort raw = (ushort)val;
                data = new byte[] { (byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF) };
            }

            var frame = BuildFrame(SERVICE_WRITE, (byte)(5 + data.Length), cls, inst, attr, data);
            SerialPort port = SerialPortManager.GetPort();
            if (port == null) return "ERR:NoPort";
            port.DiscardInBuffer(); System.Threading.Thread.Sleep(50);
            port.Write(frame, 0, frame.Length);

            byte[] resp = new byte[5];
            if (port.Read(resp, 0, 5) < 5) return "ERR:NoRsp";
            if (resp[0] != ACK_OK) return "ERR:NAK";
            return "OK";
        }

        // ---- 协议帧封装 ----
        private byte[] BuildFrame(byte service, byte dataLen, byte cls, byte inst, byte attr, byte[] data)
        {
            List<byte> frame = new List<byte>();
            frame.Add((byte)Address);
            frame.Add(STX);
            frame.Add(service);
            frame.Add(dataLen);
            frame.Add(cls);
            frame.Add(inst);
            frame.Add(attr);
            frame.AddRange(data);
            frame.Add(PAD);
            byte checksum = 0;
            foreach (byte b in frame) checksum += b;
            frame.Add(checksum);
            return frame.ToArray();
        }

        private struct ParsedResponse { public bool ok; public byte[] data; public string error; }

        private ParsedResponse ParseResponseFrame(byte[] raw)
        {
            if (raw.Length < 6) return new ParsedResponse { ok = false, error = "TooShort" };
            if (raw[0] == ACK_ERR) return new ParsedResponse { ok = false, error = "NAK" };
            if (raw[0] != ACK_OK && raw[0] != (byte)Address)
                return new ParsedResponse { ok = false, error = "BadACK:0x" + raw[0].ToString("X2") };

            // 校验：从 addr(索引1) 到 pad 之前
            int padIdx = raw.Length - 2;
            byte calc = 0;
            for (int i = 1; i < padIdx; i++) calc += raw[i];
            byte recv = raw[raw.Length - 1];
            if (calc != recv) return new ParsedResponse { ok = false, error = "Checksum" };

            // 数据：attr 之后(索引7) 到 pad 之前
            int dataStart = 7;
            int dataEnd = raw.Length - 2;
            int dataLen = dataEnd - dataStart;
            if (dataLen <= 0) return new ParsedResponse { ok = true, data = new byte[0], error = "" };
            byte[] data = new byte[dataLen];
            Array.Copy(raw, dataStart, data, 0, dataLen);
            return new ParsedResponse { ok = true, data = data, error = "" };
        }

        // ---- UFRAC16 编解码 ----
        private float UFrac16ToPercent(ushort raw)
        {
            return (raw - UFRAC16_ZERO) / (float)UFRAC16_RANGE * 100f;
        }
        private ushort PercentToUFrac16(float percent)
        {
            int raw = (int)(UFRAC16_ZERO + (percent / 100f) * UFRAC16_RANGE);
            return (ushort)Math.Max(UFRAC16_ZERO, Math.Min(0xE000, raw));
        }
    }


    // ================================================================
    //  V4.5 新增：引擎工厂
    // ================================================================
    public static class EngineFactory
    {
        public static ProtocolEngine Create(string protocol)
        {
            if (string.IsNullOrEmpty(protocol)) return null;
            string p = protocol.ToLower().Trim();
            if (p == "modbus-rtu" || p == "modbus") return new ModbusEngine();
            if (p == "aibus") return new AIBUSEngine();
            if (p == "fixed-frame" || p == "fixedframe") return new FixedFrameEngine();
            if (p == "sevenstar" || p == "7star" || p == "cs200a") return new SevenStarEngine();
            return null;
        }
    }

    // ================================================================
    //  V4.5 新增：配置化智能设备（SmartDevice）
    //  统一入口：加载 JSON → 自动选择引擎 → 语义化命令
    // ================================================================
    public class SmartDevice : DeviceBase
    {
        private ProtocolEngine _engine;
        private DeviceProfile _profile;
        private bool _isTempController = false;

        public string ProfileName { get; private set; }

        // 无参构造（Nova 默认需要）
        public SmartDevice() { }

        // 一步构造：new SmartDevice("宇电_AI708", "1")
        public SmartDevice(string profileName, string address)
        {
            string result = LoadProfile(profileName);
            if (result == "OK")
                SetAddress(address);
        }
        /// <summary>
        /// 加载设备模板（JSON 配置文件名，不含路径，自动在 devices/ 目录查找）
        /// </summary>
        public string LoadProfile(string profileName)
        {
            ProfileName = profileName;
            var profile = ProfileLoader.Load(profileName);
            if (profile == null) return "ERR:ProfileNotFound:" + profileName;
            _profile = profile;

            // 自动应用 JSON 中的默认地址
            if (profile.default_address > 0)
                _addr = profile.default_address;

            _engine = EngineFactory.Create(profile.protocol);
            if (_engine == null) return "ERR:UnsupportedProtocol:" + profile.protocol;
            _engine.Init(profile, _addr);

            // 判断是否为温控器（有 PV/SV 寄存器）
            _isTempController = profile.registers != null &&
                                profile.registers.ContainsKey("pv") &&
                                profile.registers.ContainsKey("sv");

            return "OK";
        }

        // 覆盖基类 SetAddress，同步更新引擎中的地址
        public new string SetAddress(string addr)
        {
            string result = base.SetAddress(addr);
            if (result == "OK" && _engine != null && _profile != null)
            {
                _engine.Init(_profile, _addr);
            }
            return result;
        }

        protected override bool IsValidAddress(int addr)
        {
            if (_profile == null) return addr >= 1 && addr <= 247;
            string p = _profile.protocol.ToLower();
            if (p == "aibus") return addr >= 1 && addr <= 80;
            if (p == "sevenstar" || p == "7star" || p == "cs200a") return addr >= 1 && addr <= 95;
            if (p == "fixed-frame" || p == "fixedframe") return addr >= 0 && addr <= 255;
            // modbus-rtu 及其他
            return addr >= 1 && addr <= 247;
        }

        protected override string GetAddressRange()
        {
            if (_profile == null) return "1-247";
            string p = _profile.protocol.ToLower();
            if (p == "aibus") return "1-80";
            if (p == "sevenstar" || p == "7star" || p == "cs200a") return "1-95";
            if (p == "fixed-frame" || p == "fixedframe") return "0-255";
            return "1-247";
        }
        protected override string ExecuteCommand(string command)
        {
            if (_engine == null) return "ERR:NoProfile";
            var parts = command.Split(new[] { ' ' }, 2);
            string cmd = parts[0].ToLower();
            string args = parts.Length > 1 ? parts[1] : "";
            return _engine.Execute(cmd, args);
        }

        protected override TemperatureData ReadPVSV()
        {
            if (_engine == null) return null;
            return _engine.ReadPVSV();
        }

        public bool IsTempController { get { return _isTempController; } }
        public string Protocol { get { return _profile != null ? _profile.protocol : "unknown"; } }
    }

    // ================================================================
    //  V4.5 新增：设备工厂（统一创建设备）
    // ================================================================
    public static class DeviceFactory
    {
        /// <summary>
        /// 创建配置化设备（推荐方式）
        /// </summary>
        public static SmartDevice Create(string profileName)
        {
            var dev = new SmartDevice();
            string result = dev.LoadProfile(profileName);
            if (result != "OK")
            {
                // 加载失败时返回一个标记失败的设备
                // 调用方可通过检查 ProfileName 是否为 null 判断
            }
            return dev;
        }

        /// <summary>
        /// 创建配置化设备并设置地址
        /// </summary>
        public static SmartDevice Create(string profileName, string address)
        {
            var dev = Create(profileName);
            dev.SetAddress(address);
            return dev;
        }

        /// <summary>
        /// 创建 V4.4 兼容设备（向后兼容）
        /// </summary>
        public static AIBUSDevice CreateAIBUS() { return new AIBUSDevice(); }
        public static ModbusDevice CreateModbus() { return new ModbusDevice(); }
    }
}
