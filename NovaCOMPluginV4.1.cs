// NovaCOMPlugin.cs
// 版本: 4.1 Documented
// 功能: Nova 软件 .NET 插件
//       - AIBUS/Modbus 协议转换
//       - 智能命令识别（ASCII/HEX自动判断）
//       - 单串口多设备共享
//       - 温度稳定判断（状态机，抽象到基类）
//       - 用户可配置参数

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace NovaCOMPlugin
{
    // ========== 状态枚举 ==========

    /// <summary>
    /// 温控稳定状态码
    /// </summary>
    public enum StateCode
    {
        /// <summary>通信或解析错误</summary>
        Error = -1,
        /// <summary>加热中，实测温度(PV)尚未达到目标温度(SV)的阈值范围内</summary>
        Heating = 0,
        /// <summary>首次到达阈值，正在计时观察中</summary>
        Reaching = 1,
        /// <summary>稳定观察中（内部状态，通常不直接返回）</summary>
        Stabilizing = 2,
        /// <summary>温度已稳定，可以开始实验</summary>
        Stable = 3
    }

    // ========== 状态快照 ==========

    /// <summary>
    /// 单次状态读取的快照数据
    /// </summary>
    public struct StateSnapshot
    {
        /// <summary>当前状态码</summary>
        public StateCode Code;
        /// <summary>实测温度 PV (Process Value)</summary>
        public float PV;
        /// <summary>目标温度 SV (Set Value)</summary>
        public float SV;
        /// <summary>偏差 |PV - SV|</summary>
        public float Diff;
        /// <summary>已计时秒数（仅在 Reaching 状态有效）</summary>
        public int Elapsed;
        /// <summary>总需稳定观察时间（秒）</summary>
        public int TotalTime;
        /// <summary>历史记录数量</summary>
        public int HistoryCount;
        /// <summary>连续失败次数</summary>
        public int FailCount;
        /// <summary>状态原始字符串</summary>
        public string Raw;

        public bool IsStable => Code == StateCode.Stable;
        public bool IsError => Code == StateCode.Error;
        public bool IsHeating => Code == StateCode.Heating;
        public bool IsReaching => Code == StateCode.Reaching;
    }

    // ========== 稳定控制器（独立状态机）==========

    /// <summary>
    /// 温度稳定判断控制器
    /// 与具体通信协议无关，只负责根据 PV/SV 数值判断稳定状态
    /// </summary>
    public class StabilityController
    {
        // ----- 配置参数（用户可调整）-----

        /// <summary>
        /// 首次到达阈值：|PV - SV| ≤ 此值时认为"到达"目标温度，开始计时
        /// 默认 0.5°C
        /// </summary>
        public float ReachThreshold = 0.5f;

        /// <summary>
        /// 稳定观察时间（秒）：到达阈值后需持续观察此时间
        /// 默认 60 秒
        /// </summary>
        public int StabilizeTime = 60;

        /// <summary>
        /// 最大允许偏差：稳定状态下 |PV - SV| 不能超过此值，否则判为失稳
        /// 默认 1.0°C
        /// </summary>
        public float MaxDeviation = 1.0f;

        /// <summary>
        /// 平均偏差阈值：观察期内平均偏差需小于此值
        /// 默认 0.5°C
        /// </summary>
        public float AvgDeviation = 0.5f;

        /// <summary>
        /// 连续失败次数：当前偏差超限时允许的连续失败次数，超限则判为失稳
        /// 默认 3 次
        /// </summary>
        public int ConsecutiveFail = 3;

        // ----- 输出数据（上次更新结果）-----

        /// <summary>上次更新的实测温度 PV</summary>
        public float LastPV = 0;
        /// <summary>上次更新的目标温度 SV</summary>
        public float LastSV = 0;
        /// <summary>上次更新的偏差 |PV - SV|</summary>
        public float LastDiff = 0;
        /// <summary>历史记录数量</summary>
        public int HistoryCount => _history.Count;
        /// <summary>连续失败次数</summary>
        public int FailCount => _failCount;

        // ----- 状态机内部变量 -----

        private enum State { HEATING, REACHING, STABILIZING, STABLE }
        private State _currentState = State.HEATING;
        private DateTime _reachTime;
        private Queue<float> _history = new Queue<float>();
        private int _failCount = 0;

        /// <summary>
        /// 核心方法：输入当前 PV/SV，更新并返回状态码
        /// 每次调用自动推进状态机
        /// </summary>
        /// <param name="pv">实测温度 (Process Value)</param>
        /// <param name="sv">目标温度 (Set Value)</param>
        /// <returns>当前状态码</returns>
        public StateCode Update(float pv, float sv)
        {
            float diff = Math.Abs(pv - sv);
            LastPV = pv; LastSV = sv; LastDiff = diff;

            // 记录历史偏差
            _history.Enqueue(diff);
            while (_history.Count > StabilizeTime) _history.Dequeue();

            // 计算统计值
            float maxDiff = 0, sumDiff = 0;
            foreach (var d in _history) { if (d > maxDiff) maxDiff = d; sumDiff += d; }
            float avgDiff = _history.Count > 0 ? sumDiff / _history.Count : 0;

            // 状态机
            switch (_currentState)
            {
                case State.HEATING:
                    if (diff <= ReachThreshold)
                    {
                        _currentState = State.REACHING;
                        _reachTime = DateTime.Now;
                        _failCount = 0;
                    }
                    return _currentState == State.REACHING ? StateCode.Reaching : StateCode.Heating;

                case State.REACHING:
                    double elapsed = (DateTime.Now - _reachTime).TotalSeconds;
                    if (elapsed >= StabilizeTime) { _currentState = State.STABILIZING; goto case State.STABILIZING; }
                    if (diff > ReachThreshold) { _currentState = State.HEATING; return StateCode.Heating; }
                    return StateCode.Reaching;

                case State.STABILIZING:
                    if (CheckStable(diff, maxDiff, avgDiff)) { _currentState = State.STABLE; return StateCode.Stable; }
                    _currentState = State.HEATING; _failCount = 0;
                    return StateCode.Heating;

                case State.STABLE:
                    if (diff > MaxDeviation) { _currentState = State.HEATING; _failCount = 0; return StateCode.Heating; }
                    return StateCode.Stable;

                default:
                    return StateCode.Error;
            }
        }

        private bool CheckStable(float cur, float max, float avg)
        {
            if (cur > ReachThreshold) { _failCount++; if (_failCount >= ConsecutiveFail) return false; }
            else _failCount = 0;
            if (_history.Count < StabilizeTime * 0.5f) return false;
            if (max > MaxDeviation) return false;
            if (avg > AvgDeviation) return false;
            return true;
        }

        /// <summary>
        /// 重置状态机到初始状态
        /// 切换实验或重新设定温度时应调用
        /// </summary>
        public void Reset()
        {
            _currentState = State.HEATING;
            _history.Clear();
            _failCount = 0;
        }

        /// <summary>
        /// 获取当前状态快照
        /// </summary>
        public StateSnapshot GetSnapshot()
        {
            int elapsed = 0;
            if (_currentState == State.REACHING)
                elapsed = (int)(DateTime.Now - _reachTime).TotalSeconds;

            return new StateSnapshot
            {
                Code = (StateCode)_currentState,
                PV = LastPV,
                SV = LastSV,
                Diff = LastDiff,
                Elapsed = elapsed,
                TotalTime = StabilizeTime,
                HistoryCount = HistoryCount,
                FailCount = FailCount,
                Raw = _currentState.ToString()
            };
        }
    }

    // ========== 串口管理器（全局共享）==========

    /// <summary>
    /// 全局串口管理器，所有设备共享同一个串口实例
    /// </summary>
    public static class SerialPortManager
    {
        private static SerialPort _serialPort;
        private static bool _isOpen = false;

        /// <summary>打开串口</summary>
        /// <param name="portName">COM 口名称，如 "COM3"</param>
        /// <param name="baudRate">波特率，如 "9600"</param>
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

        /// <summary>关闭串口</summary>
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

        /// <summary>
        /// 智能命令发送：自动判断 ASCII / HEX 转义 / 纯 HEX
        /// </summary>
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

    // ========== 设备抽象基类（含稳定控制）==========

    /// <summary>
    /// 温控设备抽象基类
    /// 所有具体设备（AIBUS/Modbus）继承此类，自动获得稳定判断能力
    /// </summary>
    public abstract class DeviceBase
    {
        /// <summary>设备地址</summary>
        protected int _addr = 1;

        /// <summary>稳定控制器实例（每个设备独立）</summary>
        public readonly StabilityController Stability = new StabilityController();

        public DeviceBase() { }

        /// <summary>设置设备地址</summary>
        public string SetAddress(string addr)
        {
            int a; if (int.TryParse(addr, out a) && IsValidAddress(a)) { _addr = a; return "OK"; }
            return "ERR:Addr" + GetAddressRange();
        }

        /// <summary>发送原始命令</summary>
        public string SendCommand(string command)
        {
            if (!SerialPortManager.IsOpen) return "ERR:Closed";
            try { return ExecuteCommand(command.Trim().ToUpper()); }
            catch (Exception ex) { return "ERR:" + ex.Message; }
        }

        // ----- 稳定判断（基类实现，所有设备共用）-----

        /// <summary>
        /// 读取当前温度并判断稳定状态
        /// 返回: H=加热中 R=到达计时中 OK=已稳定 ERR=错误
        /// </summary>
        public string CheckStability()
        {
            var pvsv = ReadPVSV();
            if (!pvsv.HasValue) return "ERR";

            var code = Stability.Update(pvsv.Value.PV, pvsv.Value.SV);
            switch (code)
            {
                case StateCode.Heating: return "H";
                case StateCode.Reaching: return "R";
                case StateCode.Stable: return "OK";
                case StateCode.Error: return "ERR";
                default: return "H";
            }
        }

        /// <summary>返回枚举状态码</summary>
        public StateCode CheckStabilityCode()
        {
            string r = CheckStability();
            switch (r)
            {
                case "H": return StateCode.Heating;
                case "R": return StateCode.Reaching;
                case "OK": return StateCode.Stable;
                default: return StateCode.Error;
            }
        }

        /// <summary>获取完整状态快照</summary>
        public StateSnapshot GetState()
        {
            CheckStability();
            return Stability.GetSnapshot();
        }

        /// <summary>重置稳定状态</summary>
        public string ResetStability()
        {
            Stability.Reset();
            return "OK";
        }

        // ----- 子类必须实现 -----

        /// <summary>
        /// 读取当前 PV 和 SV 值
        /// 子类根据各自协议实现（AIBUS/Modbus）
        /// </summary>
        /// <returns>PV 和 SV 元组，失败返回 null</returns>
        protected abstract (float PV, float SV)? ReadPVSV();

        protected abstract string ExecuteCommand(string command);
        protected abstract bool IsValidAddress(int addr);
        protected abstract string GetAddressRange();
    }

    // ========== AIBUS 设备 ==========

    /// <summary>
    /// AIBUS 协议温控设备
    /// 典型设备：宇电 AI-518/708/808 系列温控器
    /// 通信参数：9600 baud, 无校验, 8 数据位, 1 停止位
    /// 帧格式：8 字节固定长度，含地址+命令+参数+校验和
    /// </summary>
    public class AIBUSDevice : DeviceBase
    {
        protected override bool IsValidAddress(int addr) => addr >= 1 && addr <= 80;
        protected override string GetAddressRange() => "1-80";

        protected override string ExecuteCommand(string command)
        {
            if (command == "READ" || command == "STATUS") return Read();
            if (command.StartsWith("SET ")) return ParseSet(command);
            return "ERR:Cmd";
        }

        /// <summary>
        /// 实现基类要求的 PV/SV 读取
        /// 通过 AIBUS 读帧获取实测温度(PV)和目标温度(SV)
        /// </summary>
        protected override (float PV, float SV)? ReadPVSV()
        {
            string result = Read();
            if (!result.StartsWith("PV=")) return null;

            var p = result.Split(',');
            float pv = float.Parse(p[0].Substring(3));  // 解析 PV=xx.x
            float sv = float.Parse(p[1].Substring(3));      // 解析 SV=xx.x
            return (pv, sv);
        }

        // ----- AIBUS 协议底层 -----

        /// <summary>
        /// 发送读帧，读取 PV/SV/MV/STATUS
        /// 返回格式: PV=25.3,SV=80.0,MV=50,ST=00
        /// </summary>
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
            if (n > 0) return "ERR:Inc" + n;  // 不完整响应
            return "ERR:NoRsp";               // 无响应
        }

        /// <summary>解析 SET 命令</summary>
        private string ParseSet(string cmd)
        {
            var p = cmd.Split(' ');
            if (p.Length >= 2 && float.TryParse(p[1], out float sv)) return WriteSV(sv);
            return "ERR:SetFmt";
        }

        /// <summary>
        /// 发送写 SV 帧，设置目标温度
        /// SV 值 × 10 后作为 16 位整数发送
        /// </summary>
        private string WriteSV(float sv)
        {
            var port = SerialPortManager.GetPort();
            ushort v = (ushort)(sv * 10);  // 温度放大 10 倍
            port.Write(BuildWriteSVFrame(_addr, v), 0, 8);

            byte[] resp = new byte[10];
            int n = 0, wait = 0;
            while (n < 8 && wait < 500)
            {
                if (port.BytesToRead > 0) { int r = Math.Min(port.BytesToRead, 10 - n); n += port.Read(resp, n, r); }
                else { System.Threading.Thread.Sleep(10); wait += 10; }
            }

            if (n >= 8 && resp[2] == 0x43) return "OK";  // 0x43 = 'C' 表示写成功
            if (n > 0) return "ERR:Wrt" + n;
            return "ERR:WrtFail";
        }

        /// <summary>
        /// 构建 AIBUS 读帧（8 字节）
        /// 帧结构: [ADDR, ADDR, CMD=0x52, PARAM, 0, 0, CHK_L, CHK_H]
        /// </summary>
        private byte[] BuildReadFrame(int addr, byte param)
        {
            byte a = (byte)(0x80 + addr);  // 地址偏移 0x80
            byte[] f = new byte[8] { a, a, 0x52, param, 0, 0, 0, 0 };
            // 校验和 = (PARAM × 256 + CMD + ADDR) & 0xFFFF
            ushort chk = (ushort)((param * 256 + 0x52 + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF);      // 校验和低字节
            f[7] = (byte)((chk >> 8) & 0xFF); // 校验和高字节
            return f;
        }

        /// <summary>
        /// 构建 AIBUS 写 SV 帧（8 字节）
        /// 帧结构: [ADDR, ADDR, CMD=0x43, 0, VAL_L, VAL_H, CHK_L, CHK_H]
        /// </summary>
        private byte[] BuildWriteSVFrame(int addr, ushort val)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x43, 0, (byte)(val & 0xFF), (byte)((val >> 8) & 0xFF), 0, 0 };
            // 校验和 = (0 × 256 + CMD + VAL + ADDR) & 0xFFFF
            ushort chk = (ushort)((0 * 256 + 0x43 + val + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF);
            f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }

        /// <summary>
        /// 解析 AIBUS 响应帧（10 字节）
        /// 字节 0-1: PV (实测温度 × 10)
        /// 字节 2-3: SV (目标温度 × 10)
        /// 字节 4:   MV (输出值 0-255)
        /// 字节 5:   STATUS (状态字节)
        /// 字节 6-7: 校验和
        /// 字节 8-9: 固定 0xFF
        /// </summary>
        private string ParseResponse(byte[] r)
        {
            ushort pvRaw = (ushort)(r[0] | (r[1] << 8));  // 小端序
            ushort svRaw = (ushort)(r[2] | (r[3] << 8));
            float pv = pvRaw / 10.0f;  // 还原温度
            float sv = svRaw / 10.0f;
            return $"PV={pv:F1},SV={sv:F1},MV={r[4]},ST={r[5]:X2}";
        }
    }

    // ========== Modbus 设备 ==========

    /// <summary>
    /// Modbus RTU 协议温控设备
    /// 支持标准 Modbus 功能码 03(读) / 06(写)
    /// </summary>
    public class ModbusDevice : DeviceBase
    {
        /// <summary>PV 所在寄存器地址（默认 0）</summary>
        public ushort PVRegister = 0;
        /// <summary>SV 所在寄存器地址（默认 1）</summary>
        public ushort SVRegister = 1;

        protected override bool IsValidAddress(int addr) => addr >= 1 && addr <= 247;
        protected override string GetAddressRange() => "1-247";

        protected override string ExecuteCommand(string command)
        {
            var p = command.Split(' ');
            if (command.StartsWith("READ ") && p.Length >= 3) return ReadHold(p[1], p[2]);
            if (command.StartsWith("WRITE ") && p.Length >= 3) return WriteHold(p[1], p[2]);
            return "ERR:Cmd";
        }

        /// <summary>
        /// 实现基类要求的 PV/SV 读取
        /// 从 Modbus 寄存器读取实测温度和目标温度
        /// </summary>
        protected override (float PV, float SV)? ReadPVSV()
        {
            // 从 PV 寄存器开始读 2 个寄存器（PV 和 SV）
            var result = ReadHoldRaw(PVRegister, 2);
            if (result == null || result.Length < 2) return null;

            // 假设寄存器值 = 实际温度 × 10
            float pv = result[0] / 10.0f;
            float sv = result[1] / 10.0f;
            return (pv, sv);
        }

        // ----- Modbus 协议底层 -----

        private string ReadHold(string reg, string num)
        {
            var vals = ReadHoldRaw(ushort.Parse(reg), ushort.Parse(num));
            if (vals == null) return "ERR:NoRsp";

            var sb = new StringBuilder();
            for (int i = 0; i < vals.Length; i++)
                sb.Append($"R{ushort.Parse(reg) + i}={vals[i]};");
            return sb.ToString().TrimEnd(';');
        }

        private ushort[] ReadHoldRaw(ushort reg, ushort num)
        {
            var port = SerialPortManager.GetPort();
            port.Write(BuildReadFrame(_addr, reg, num), 0, 8);
            System.Threading.Thread.Sleep(50);

            byte[] h = new byte[3];
            if (port.Read(h, 0, 3) < 3) return null;
            byte bc = h[2];  // 字节计数
            byte[] resp = new byte[3 + bc + 2];
            resp[0] = h[0]; resp[1] = h[1]; resp[2] = h[2];
            if (port.Read(resp, 3, bc + 2) < bc + 2) return null;

            var vals = new ushort[bc / 2];
            for (int i = 0; i < bc / 2; i++)
                vals[i] = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);  // 大端序
            return vals;
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

        /// <summary>构建 Modbus 读保持寄存器帧（功能码 0x03）</summary>
        private byte[] BuildReadFrame(int addr, ushort reg, ushort num)
        {
            byte[] f = new byte[6] { (byte)addr, 0x03, (byte)(reg >> 8), (byte)reg, (byte)(num >> 8), (byte)num };
            ushort crc = CRC16(f, 6);
            return new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }

        /// <summary>构建 Modbus 写单个寄存器帧（功能码 0x06）</summary>
        private byte[] BuildWriteFrame(int addr, ushort reg, ushort val)
        {
            byte[] f = new byte[6] { (byte)addr, 0x06, (byte)(reg >> 8), (byte)reg, (byte)(val >> 8), (byte)val };
            ushort crc = CRC16(f, 6);
            return new byte[8] { f[0], f[1], f[2], f[3], f[4], f[5], (byte)(crc & 0xFF), (byte)(crc >> 8) };
        }

        /// <summary>Modbus CRC16 校验</summary>
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

    // ========== 静态工具方法 ==========

    /// <summary>
    /// 设备辅助工具类
    /// 提供静态方法方便 Nova 脚本调用
    /// </summary>
    public static class DeviceHelper
    {
        /// <summary>判断设备是否已稳定</summary>
        public static bool IsStable(DeviceBase dev)
        {
            if (dev == null) return false;
            return dev.CheckStabilityCode() == StateCode.Stable;
        }

        /// <summary>判断设备是否错误</summary>
        public static bool IsError(DeviceBase dev)
        {
            if (dev == null) return true;
            return dev.CheckStabilityCode() == StateCode.Error;
        }

        /// <summary>判断设备是否加热中</summary>
        public static bool IsHeating(DeviceBase dev)
        {
            if (dev == null) return true;
            return dev.CheckStabilityCode() == StateCode.Heating;
        }

        /// <summary>获取温度显示字符串 "PV/SV"</summary>
        public static string GetTempString(DeviceBase dev)
        {
            if (dev == null) return "ERR";
            dev.CheckStability();
            return $"{dev.Stability.LastPV:F1}/{dev.Stability.LastSV:F1}";
        }

        /// <summary>阻塞等待稳定（带超时）</summary>
        /// <param name="timeoutSec">超时时间（秒），默认 300</param>
        public static string WaitUntilStable(DeviceBase dev, int timeoutSec = 300)
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
    }
}