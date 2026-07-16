// NovaCOMPlugin.cs
// 版本: 3.3 Static State
// 修改: 新增静态状态判断方法，适配无循环逻辑的仪器

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace NovaCOMPlugin
{
    // ========== 状态枚举 ==========

    public enum StateCode
    {
        Error = -1,
        Heating = 0,
        Reaching = 1,
        Stabilizing = 2,
        Stable = 3
    }

    // ========== 状态快照（静态传递）==========

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

        public bool IsStable => Code == StateCode.Stable;
        public bool IsError => Code == StateCode.Error;
        public bool IsHeating => Code == StateCode.Heating;
        public bool IsReaching => Code == StateCode.Reaching;
    }

    // ========== 串口管理器 ==========

    public static class SerialPortManager
    {
        private static SerialPort _serialPort;
        private static bool _isOpen = false;

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
                _serialPort = null; _isOpen = false;
                return "OK";
            }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        public static SerialPort GetPort() => _serialPort;
        public static bool IsOpen => _isOpen;

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

        private static bool IsHexEscape(string s) => s.Contains("\\x") || s.Contains("\\X");
        private static bool IsPureHex(string s)
        {
            s = s.Replace(" ", "").Replace("\t", "");
            if (s.Length % 2 != 0 || s.Length < 4) return false;
            foreach (char c in s) if (!IsHexChar(c)) return false;
            return true;
        }
        private static bool IsHexChar(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

        private static string SendAscii(string text)
        {
            _serialPort.Write(Encoding.ASCII.GetBytes(text + "\r\n"), 0, text.Length + 2);
            return ReadResponse("A:" + text);
        }

        private static string SendHexEscape(string hex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < hex.Length; i++)
                if (i + 3 < hex.Length && hex[i] == '\\' && (hex[i + 1] == 'x' || hex[i + 1] == 'X'))
                { sb.Append(hex[i + 2]); sb.Append(hex[i + 3]); i += 3; }
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
            byte[] buf = new byte[256];
            int n = 0;
            try { n = _serialPort.Read(buf, 0, 256); } catch (TimeoutException) { }
            if (n == 0) return sent + "|";
            bool ascii = true;
            for (int i = 0; i < n; i++) if ((buf[i] < 32 && buf[i] != 13 && buf[i] != 10) || buf[i] > 126) { ascii = false; break; }
            if (ascii) return sent + "|" + Encoding.ASCII.GetString(buf, 0, n).Trim();
            var h = new StringBuilder();
            for (int i = 0; i < n; i++) h.Append(buf[i].ToString("X2"));
            return sent + "|" + h.ToString();
        }

        public static string TestEcho(string input) => input;
    }

    // ========== 设备基类 ==========

    public abstract class DeviceBase
    {
        protected int _addr = 1;
        public string SetAddress(string addr)
        {
            int a; if (int.TryParse(addr, out a) && IsValidAddress(a)) { _addr = a; return "OK"; }
            return "ERR:Addr" + GetAddressRange();
        }
        public string SendCommand(string command)
        {
            if (!SerialPortManager.IsOpen) return "ERR:Closed";
            try { return ExecuteCommand(command.Trim().ToUpper()); }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }
        protected abstract string ExecuteCommand(string command);
        protected abstract bool IsValidAddress(int addr);
        protected abstract string GetAddressRange();
    }

    // ========== AIBUS 设备（含静态判断）==========

    public class AIBUSDevice : DeviceBase
    {
        // 配置
        public float ReachThreshold = 0.5f;
        public int StabilizeTime = 60;
        public float MaxDeviation = 1.0f;
        public float AvgDeviation = 0.5f;
        public int ConsecutiveFail = 3;

        // 实时数据
        public float LastPV = 0;
        public float LastSV = 0;
        public float LastDiff = 0;
        public StateCode CurrentStateCode => (StateCode)_currentState;
        public int HistoryCount => _history.Count;
        public int FailCount => _failCount;

        // 状态机
        private enum State { HEATING, REACHING, STABILIZING, STABLE }
        private State _currentState = State.HEATING;
        private DateTime _reachTime;
        private Queue<float> _history = new Queue<float>();
        private int _failCount = 0;

        protected override bool IsValidAddress(int addr) => addr >= 1 && addr <= 80;
        protected override string GetAddressRange() => "1-80";

        protected override string ExecuteCommand(string command)
        {
            if (command == "READ" || command == "STATUS") return Read();
            if (command.StartsWith("SET ")) return ParseSet(command);
            return "ERR:Cmd";
        }

        // ========== 核心方法：静态判断 ==========

        /// <summary>
        /// 最短返回：H / R / OK / ERR
        /// 每次调用自动推进状态机，无需循环
        /// </summary>
        public string CheckStability()
        {
            var result = Read();
            if (!result.StartsWith("PV=")) return "ERR";

            var p = result.Split(',');
            float pv = float.Parse(p[0].Substring(3));
            float sv = float.Parse(p[1].Substring(3));
            float diff = Math.Abs(pv - sv);

            LastPV = pv; LastSV = sv; LastDiff = diff;

            _history.Enqueue(diff);
            while (_history.Count > StabilizeTime) _history.Dequeue();

            float maxDiff = 0, sumDiff = 0;
            foreach (var d in _history) { if (d > maxDiff) maxDiff = d; sumDiff += d; }
            float avgDiff = _history.Count > 0 ? sumDiff / _history.Count : 0;

            switch (_currentState)
            {
                case State.HEATING:
                    if (diff <= ReachThreshold)
                    {
                        _currentState = State.REACHING;
                        _reachTime = DateTime.Now;
                        _failCount = 0;
                    }
                    return _currentState == State.REACHING ? "R" : "H";

                case State.REACHING:
                    double elapsed = (DateTime.Now - _reachTime).TotalSeconds;
                    if (elapsed >= StabilizeTime) { _currentState = State.STABILIZING; goto case State.STABILIZING; }
                    if (diff > ReachThreshold) { _currentState = State.HEATING; return "H"; }
                    return "R";

                case State.STABILIZING:
                    if (IsStable(diff, maxDiff, avgDiff)) { _currentState = State.STABLE; return "OK"; }
                    _currentState = State.HEATING; _failCount = 0;
                    return "H";

                case State.STABLE:
                    if (diff > MaxDeviation) { _currentState = State.HEATING; _failCount = 0; return "H"; }
                    return "OK";

                default:
                    return "ERR";
            }
        }

        /// <summary>
        /// 枚举状态码
        /// </summary>
        public StateCode CheckStabilityCode()
        {
            string r = CheckStability();
            switch (r)
            {
                case "H": return StateCode.Heating;
                case "R": return StateCode.Reaching;
                case "S": return StateCode.Stabilizing;
                case "OK": return StateCode.Stable;
                default: return StateCode.Error;
            }
        }

        /// <summary>
        /// 返回完整状态快照
        /// </summary>
        public StateSnapshot GetState()
        {
            var code = CheckStabilityCode();
            int elapsed = 0;
            if (_currentState == State.REACHING)
                elapsed = (int)(DateTime.Now - _reachTime).TotalSeconds;

            return new StateSnapshot
            {
                Code = code,
                PV = LastPV,
                SV = LastSV,
                Diff = LastDiff,
                Elapsed = elapsed,
                TotalTime = StabilizeTime,
                HistoryCount = HistoryCount,
                FailCount = FailCount,
                Raw = code.ToString()
            };
        }

        // ========== ⭐ 静态判断方法（供 Nova 直接调用）==========

        /// <summary>
        /// 静态判断：当前是否已稳定
        /// Nova 用法: if (AIBUSDevice.IsStable()) { ... }
        /// </summary>
        public static bool IsStable(AIBUSDevice dev)
        {
            if (dev == null) return false;
            return dev.CheckStabilityCode() == StateCode.Stable;
        }

        /// <summary>
        /// 静态判断：当前是否处于错误状态
        /// </summary>
        public static bool IsError(AIBUSDevice dev)
        {
            if (dev == null) return true;
            return dev.CheckStabilityCode() == StateCode.Error;
        }

        /// <summary>
        /// 静态判断：当前是否还在加热中（未达阈值）
        /// </summary>
        public static bool IsHeating(AIBUSDevice dev)
        {
            if (dev == null) return true;
            return dev.CheckStabilityCode() == StateCode.Heating;
        }

        /// <summary>
        /// 静态判断：当前是否处于稳定观察计时中
        /// </summary>
        public static bool IsReaching(AIBUSDevice dev)
        {
            if (dev == null) return false;
            return dev.CheckStabilityCode() == StateCode.Reaching;
        }

        /// <summary>
        /// 静态获取状态快照
        /// </summary>
        public static StateSnapshot GetSnapshot(AIBUSDevice dev)
        {
            if (dev == null) return new StateSnapshot { Code = StateCode.Error, Raw = "ERR:Null" };
            return dev.GetState();
        }

        /// <summary>
        /// 静态获取温度字符串（用于 Nova 显示）
        /// 返回: "25.3/25.0" (PV/SV)
        /// </summary>
        public static string GetTempString(AIBUSDevice dev)
        {
            if (dev == null) return "ERR";
            dev.CheckStability(); // 先更新状态
            return $"{dev.LastPV:F1}/{dev.LastSV:F1}";
        }

        /// <summary>
        /// 静态等待稳定（阻塞式，适合 Nova 顺序执行）
        /// 返回: "OK" 或 "ERR:原因"
        /// </summary>
        public static string WaitUntilStable(AIBUSDevice dev, int timeoutSec = 300)
        {
            if (dev == null) return "ERR:Null";
            var start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < timeoutSec)
            {
                var code = dev.CheckStabilityCode();
                if (code == StateCode.Stable) return "OK";
                if (code == StateCode.Error) return "ERR:Device";
                System.Threading.Thread.Sleep(1000);
            }
            return "ERR:Timeout";
        }

        // ========== 辅助方法 ==========

        private bool IsStable(float cur, float max, float avg)
        {
            if (cur > ReachThreshold) { _failCount++; if (_failCount >= ConsecutiveFail) return false; }
            else _failCount = 0;
            if (_history.Count < StabilizeTime * 0.5f) return false;
            if (max > MaxDeviation) return false;
            if (avg > AvgDeviation) return false;
            return true;
        }

        public string ResetState()
        {
            _currentState = State.HEATING;
            _history.Clear();
            _failCount = 0;
            return "OK";
        }

        // ========== 底层通信 ==========

        private string Read()
        {
            var port = SerialPortManager.GetPort();
            port.Write(BuildReadFrame(_addr, 0x00), 0, 8);

            byte[] resp = new byte[10];
            int n = 0, wait = 0;
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
            var p = cmd.Split(' ');
            if (p.Length >= 2 && float.TryParse(p[1], out float sv)) return WriteSV(sv);
            return "ERR:SetFmt";
        }

        private string WriteSV(float sv)
        {
            var port = SerialPortManager.GetPort();
            ushort v = (ushort)(sv * 10);
            port.Write(BuildWriteSVFrame(_addr, v), 0, 8);

            byte[] resp = new byte[10];
            int n = 0, wait = 0;
            while (n < 8 && wait < 500)
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
            return $"PV={pv / 10.0f:F1},SV={sv / 10.0f:F1},MV={r[4]},ST={r[5]:X2}";
        }
    }

    // ========== Modbus 设备 ==========

    public class ModbusDevice : DeviceBase
    {
        protected override bool IsValidAddress(int addr) => addr >= 1 && addr <= 247;
        protected override string GetAddressRange() => "1-247";

        protected override string ExecuteCommand(string command)
        {
            var p = command.Split(' ');
            if (command.StartsWith("READ ") && p.Length >= 3) return ReadHold(p[1], p[2]);
            if (command.StartsWith("WRITE ") && p.Length >= 3) return WriteHold(p[1], p[2]);
            return "ERR:Cmd";
        }

        private string ReadHold(string reg, string num)
        {
            var port = SerialPortManager.GetPort();
            ushort r = ushort.Parse(reg), n = ushort.Parse(num);
            port.Write(BuildReadFrame(_addr, r, n), 0, 8);
            System.Threading.Thread.Sleep(50);

            byte[] h = new byte[3];
            if (port.Read(h, 0, 3) < 3) return "ERR:NoRsp";
            byte bc = h[2];
            byte[] resp = new byte[3 + bc + 2];
            resp[0] = h[0]; resp[1] = h[1]; resp[2] = h[2];
            if (port.Read(resp, 3, bc + 2) < bc + 2) return "ERR:Inc";

            var sb = new StringBuilder();
            for (int i = 0; i < bc / 2; i++)
            {
                ushort v = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);
                sb.Append($"R{r + i}={v};");
            }
            return sb.ToString().TrimEnd(';');
        }

        private string WriteHold(string reg, string val)
        {
            var port = SerialPortManager.GetPort();
            ushort r = ushort.Parse(reg), v = ushort.Parse(val);
            port.Write(BuildWriteFrame(_addr, r, v), 0, 8);
            System.Threading.Thread.Sleep(50);
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
                for (int j = 0; j < 8; j++) crc = (crc & 1) != 0 ? (ushort)((crc >> 1) ^ 0xA001) : (ushort)(crc >> 1);
            }
            return crc;
        }
    }
}