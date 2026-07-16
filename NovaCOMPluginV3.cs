// NovaCOMPlugin.cs
// 版本: 3.0 Final
// 功能: Nova 软件 .NET 插件
//       - 支持 AIBUS/Modbus 协议转换
//       - 智能命令识别（ASCII/HEX自动判断）
//       - 单串口多设备共享
//       - 温度稳定判断（状态机）
//       - 用户可配置参数（public fields）

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace NovaCOMPlugin
{
    // ========== 串口管理器（Static，全局共享）==========

    public static class SerialPortManager
    {
        private static SerialPort _serialPort;
        private static bool _isOpen = false;

        public static string Open(string portName, string baudRate)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                    return "OK: Already open";

                int baud = int.Parse(baudRate);
                _serialPort = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 1000;
                _serialPort.WriteTimeout = 1000;
                _serialPort.Open();
                _isOpen = true;

                return "OK: Opened " + portName + " at " + baudRate;
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        public static string Close()
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                    _serialPort.Close();
                _serialPort = null;
                _isOpen = false;
                return "OK: Closed";
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        public static SerialPort GetPort() { return _serialPort; }
        public static bool IsOpen { get { return _isOpen; } }

        // ========== 智能命令发送 ==========

        public static string SendCommand(string command)
        {
            if (!IsOpen)
                return "ERROR: Serial port not open";

            try
            {
                command = command.Trim();

                if (IsHexEscape(command))
                    return SendHexEscape(command);
                else if (IsPureHex(command))
                    return SendPureHex(command);
                else
                    return SendAscii(command);
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        private static bool IsHexEscape(string input)
        {
            return input.Contains("\\x") || input.Contains("\\X");
        }

        private static bool IsPureHex(string input)
        {
            string trimmed = input.Replace(" ", "").Replace("\t", "");
            if (trimmed.Length % 2 != 0 || trimmed.Length < 4)
                return false;

            foreach (char c in trimmed)
            {
                if (!IsHexChar(c))
                    return false;
            }
            return true;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') ||
                   (c >= 'A' && c <= 'F') ||
                   (c >= 'a' && c <= 'f');
        }

        private static string SendAscii(string text)
        {
            byte[] data = Encoding.ASCII.GetBytes(text + "\r\n");
            _serialPort.Write(data, 0, data.Length);
            return WaitAndRead("ASCII: " + text);
        }

        private static string SendHexEscape(string hexEscape)
        {
            StringBuilder hexBuilder = new StringBuilder();
            int i = 0;

            while (i < hexEscape.Length)
            {
                if (i + 3 < hexEscape.Length &&
                    hexEscape[i] == '\\' &&
                    (hexEscape[i + 1] == 'x' || hexEscape[i + 1] == 'X'))
                {
                    hexBuilder.Append(hexEscape[i + 2]);
                    hexBuilder.Append(hexEscape[i + 3]);
                    i += 4;
                }
                else
                {
                    i++;
                }
            }

            return SendPureHex(hexBuilder.ToString());
        }

        private static string SendPureHex(string hexString)
        {
            hexString = hexString.Replace(" ", "").Replace("\t", "");

            if (hexString.Length % 2 != 0)
                return "ERROR: Invalid hex string length";

            byte[] data = new byte[hexString.Length / 2];
            for (int i = 0; i < hexString.Length; i += 2)
            {
                data[i / 2] = Convert.ToByte(hexString.Substring(i, 2), 16);
            }

            _serialPort.Write(data, 0, data.Length);
            return WaitAndRead("HEX: " + hexString);
        }

        private static string WaitAndRead(string sentInfo)
        {
            System.Threading.Thread.Sleep(100);

            byte[] resp = new byte[256];
            int bytesRead = 0;

            try
            {
                bytesRead = _serialPort.Read(resp, 0, resp.Length);
            }
            catch (TimeoutException) { }

            if (bytesRead > 0)
            {
                bool isAscii = true;
                for (int i = 0; i < bytesRead; i++)
                {
                    if (resp[i] < 32 && resp[i] != 13 && resp[i] != 10)
                    {
                        isAscii = false;
                        break;
                    }
                    if (resp[i] > 126)
                    {
                        isAscii = false;
                        break;
                    }
                }

                if (isAscii)
                {
                    string ascii = Encoding.ASCII.GetString(resp, 0, bytesRead);
                    return sentInfo + "\nRECV_ASCII: " + ascii.Trim();
                }
                else
                {
                    string hex = "";
                    for (int i = 0; i < bytesRead; i++)
                    {
                        hex += resp[i].ToString("X2") + " ";
                    }
                    return sentInfo + "\nRECV_HEX: " + hex.Trim();
                }
            }
            else
            {
                return sentInfo + "\nRECV: (no response)";
            }
        }

        public static string TestEcho(string input)
        {
            return "Echo: " + input;
        }
    }

    // ========== 设备抽象基类 ==========

    public abstract class DeviceBase
    {
        protected int _addr = 1;

        public DeviceBase() { }

        public string SetAddress(string addr)
        {
            int a;
            if (int.TryParse(addr, out a) && IsValidAddress(a))
            {
                _addr = a;
                return "OK: Address set to " + a;
            }
            return "ERROR: Address must be " + GetAddressRange();
        }

        public string SendCommand(string command)
        {
            if (!SerialPortManager.IsOpen)
                return "ERROR: Serial port not open";

            try
            {
                return ExecuteCommand(command.Trim().ToUpper());
            }
            catch (Exception ex)
            {
                return "ERROR: " + ex.Message;
            }
        }

        protected abstract string ExecuteCommand(string command);
        protected abstract bool IsValidAddress(int addr);
        protected abstract string GetAddressRange();
    }

    // ========== AIBUS 设备（含稳定判断）==========

    public class AIBUSDevice : DeviceBase
    {
        // ========== 配置参数（public fields，Nova 可直接读写）==========

        public float ReachThreshold = 0.5f;     // 首次达到阈值
        public int StabilizeTime = 60;           // 稳定观察时间（秒）
        public float MaxDeviation = 1.0f;       // 最大允许偏差
        public float AvgDeviation = 0.5f;       // 平均偏差阈值
        public int ConsecutiveFail = 3;         // 连续失败次数

        // ========== 状态输出（public fields，Nova 可读取）==========

        public float LastPV = 0;                 // 最新 PV
        public float LastSV = 0;                 // 最新 SV
        public float LastDiff = 0;               // 最新偏差
        public string CurrentState { get { return _currentState.ToString(); } }
        public int HistoryCount { get { return _history.Count; } }
        public int FailCount { get { return _failCount; } }

        // ========== 状态机变量 ==========

        private enum State { HEATING, REACHING, STABILIZING, STABLE }
        private State _currentState = State.HEATING;
        private DateTime _reachTime;
        private Queue<float> _history = new Queue<float>();
        private int _failCount = 0;

        // ========== 基类实现 ==========

        protected override bool IsValidAddress(int addr)
        {
            return addr >= 1 && addr <= 80;
        }

        protected override string GetAddressRange()
        {
            return "1-80";
        }

        protected override string ExecuteCommand(string command)
        {
            if (command == "READ" || command == "STATUS")
                return Read();
            else if (command.StartsWith("SET "))
                return ParseSetCommand(command);
            else
                return "ERROR: Unknown command. Use: READ, SET xx.x, STATUS";
        }

        // ========== 核心方法：稳定判断 ==========

        public string CheckStability()
        {
            // 读取当前温度
            string result = Read();
            if (!result.StartsWith("PV="))
                return "ERROR: " + result;

            // 解析 PV, SV
            string[] parts = result.Split(',');
            float pv = float.Parse(parts[0].Substring(3));
            float sv = float.Parse(parts[1].Substring(3));
            float diff = Math.Abs(pv - sv);

            // 存储到 public field（供 Nova 后续使用）
            LastPV = pv;
            LastSV = sv;
            LastDiff = diff;

            // 记录历史
            _history.Enqueue(diff);
            while (_history.Count > StabilizeTime)
                _history.Dequeue();

            // 计算统计值
            float maxDiff = 0;
            float sumDiff = 0;
            foreach (float d in _history)
            {
                if (d > maxDiff) maxDiff = d;
                sumDiff += d;
            }
            float avgDiff = _history.Count > 0 ? sumDiff / _history.Count : 0;

            // 状态机处理
            switch (_currentState)
            {
                case State.HEATING:
                    if (diff <= ReachThreshold)
                    {
                        _currentState = State.REACHING;
                        _reachTime = DateTime.Now;
                        _failCount = 0;
                        return "OK: STATE=REACHING,PV=" + pv.ToString("F1") +
                               ",SV=" + sv.ToString("F1") +
                               ",DIFF=" + diff.ToString("F2") +
                               ",TIMER=0/" + StabilizeTime;
                    }
                    return "OK: STATE=HEATING,PV=" + pv.ToString("F1") +
                           ",SV=" + sv.ToString("F1") +
                           ",DIFF=" + diff.ToString("F2");

                case State.REACHING:
                    double elapsed = (DateTime.Now - _reachTime).TotalSeconds;
                    if (elapsed >= StabilizeTime)
                    {
                        _currentState = State.STABILIZING;
                        goto case State.STABILIZING;
                    }
                    if (diff > ReachThreshold)
                    {
                        _currentState = State.HEATING;
                        return "OK: STATE=HEATING(lost),PV=" + pv.ToString("F1") +
                               ",SV=" + sv.ToString("F1") +
                               ",DIFF=" + diff.ToString("F2");
                    }
                    return "OK: STATE=REACHING,PV=" + pv.ToString("F1") +
                           ",SV=" + sv.ToString("F1") +
                           ",DIFF=" + diff.ToString("F2") +
                           ",TIMER=" + (int)elapsed + "/" + StabilizeTime;

                case State.STABILIZING:
                    bool isStable = CheckStableConditions(diff, maxDiff, avgDiff);
                    if (isStable)
                    {
                        _currentState = State.STABLE;
                        return "OK: STATE=STABLE,PV=" + pv.ToString("F1") +
                               ",SV=" + sv.ToString("F1") +
                               ",DIFF=" + diff.ToString("F2") +
                               ",MAX_DIFF=" + maxDiff.ToString("F2") +
                               ",AVG_DIFF=" + avgDiff.ToString("F2");
                    }
                    _currentState = State.HEATING;
                    _failCount = 0;
                    return "OK: STATE=HEATING(fail),PV=" + pv.ToString("F1") +
                           ",SV=" + sv.ToString("F1") +
                           ",DIFF=" + diff.ToString("F2") +
                           ",MAX_DIFF=" + maxDiff.ToString("F2") +
                           ",AVG_DIFF=" + avgDiff.ToString("F2");

                case State.STABLE:
                    if (diff > MaxDeviation)
                    {
                        _currentState = State.HEATING;
                        _failCount = 0;
                        return "OK: STATE=HEATING(lost),PV=" + pv.ToString("F1") +
                               ",SV=" + sv.ToString("F1") +
                               ",DIFF=" + diff.ToString("F2");
                    }
                    return "OK: STATE=STABLE,PV=" + pv.ToString("F1") +
                           ",SV=" + sv.ToString("F1") +
                           ",DIFF=" + diff.ToString("F2");

                default:
                    return "ERROR: Unknown state";
            }
        }

        private bool CheckStableConditions(float currentDiff, float maxDiff, float avgDiff)
        {
            // 条件1：当前偏差在范围内
            if (currentDiff > ReachThreshold)
            {
                _failCount++;
                if (_failCount >= ConsecutiveFail)
                    return false;
            }
            else
            {
                _failCount = 0;
            }

            // 条件2：历史数据足够
            if (_history.Count < StabilizeTime * 0.5)
                return false;

            // 条件3：最大偏差和平均偏差
            if (maxDiff > MaxDeviation) return false;
            if (avgDiff > AvgDeviation) return false;

            return true;
        }

        public string ResetState()
        {
            _currentState = State.HEATING;
            _history.Clear();
            _failCount = 0;
            return "OK: State reset to HEATING";
        }

        // ========== 原有方法（改进读取）=========

        private string Read()
        {
            SerialPort port = SerialPortManager.GetPort();
            byte[] frame = BuildReadFrame(_addr, 0x00);
            port.Write(frame, 0, frame.Length);

            // 改进读取：动态等待，最多 500ms
            byte[] resp = new byte[10];
            int bytesRead = 0;
            int totalWait = 0;

            while (bytesRead < 10 && totalWait < 500)
            {
                if (port.BytesToRead > 0)
                {
                    int toRead = Math.Min(port.BytesToRead, 10 - bytesRead);
                    bytesRead += port.Read(resp, bytesRead, toRead);
                }
                else
                {
                    System.Threading.Thread.Sleep(10);
                    totalWait += 10;
                }
            }

            if (bytesRead == 10)
                return ParseResponse(resp);
            else if (bytesRead > 0)
            {
                string partial = "";
                for (int i = 0; i < bytesRead; i++)
                    partial += resp[i].ToString("X2") + " ";
                return "ERROR: Incomplete response (" + bytesRead + " bytes): " + partial.Trim();
            }
            else
                return "ERROR: No response";
        }

        private string ParseSetCommand(string command)
        {
            string[] parts = command.Split(' ');
            if (parts.Length >= 2)
            {
                float sv;
                if (float.TryParse(parts[1], out sv))
                    return WriteSV(sv);
            }
            return "ERROR: Invalid SET format. Use: SET 25.0";
        }

        private string WriteSV(float sv)
        {
            SerialPort port = SerialPortManager.GetPort();
            ushort val = (ushort)(sv * 10);
            byte[] frame = BuildWriteSVFrame(_addr, val);
            port.Write(frame, 0, frame.Length);

            // 改进读取
            byte[] resp = new byte[10];
            int bytesRead = 0;
            int totalWait = 0;

            while (bytesRead < 8 && totalWait < 500)
            {
                if (port.BytesToRead > 0)
                {
                    int toRead = Math.Min(port.BytesToRead, 10 - bytesRead);
                    bytesRead += port.Read(resp, bytesRead, toRead);
                }
                else
                {
                    System.Threading.Thread.Sleep(10);
                    totalWait += 10;
                }
            }

            if (bytesRead >= 8 && resp[2] == 0x43)
                return "OK: SV set to " + sv.ToString("F1");
            else if (bytesRead > 0)
            {
                string partial = "";
                for (int i = 0; i < bytesRead; i++)
                    partial += resp[i].ToString("X2") + " ";
                return "ERROR: Write incomplete (" + bytesRead + " bytes): " + partial.Trim();
            }
            else
                return "ERROR: Write failed or no response";
        }

        private byte[] BuildReadFrame(int addr, byte param)
        {
            byte a = (byte)(0x80 + addr);
            byte[] frame = new byte[8];
            frame[0] = a; frame[1] = a;
            frame[2] = 0x52;
            frame[3] = param;
            frame[4] = 0x00; frame[5] = 0x00;
            ushort chk = (ushort)((param * 256 + 0x52 + addr) & 0xFFFF);
            frame[6] = (byte)(chk & 0xFF);
            frame[7] = (byte)((chk >> 8) & 0xFF);
            return frame;
        }

        private byte[] BuildWriteSVFrame(int addr, ushort value)
        {
            byte a = (byte)(0x80 + addr);
            byte[] frame = new byte[8];
            frame[0] = a; frame[1] = a;
            frame[2] = 0x43;
            frame[3] = 0x00;
            frame[4] = (byte)(value & 0xFF);
            frame[5] = (byte)((value >> 8) & 0xFF);
            ushort chk = (ushort)((0 * 256 + 0x43 + value + addr) & 0xFFFF);
            frame[6] = (byte)(chk & 0xFF);
            frame[7] = (byte)((chk >> 8) & 0xFF);
            return frame;
        }

        private string ParseResponse(byte[] resp)
        {
            ushort pvRaw = (ushort)(resp[0] | (resp[1] << 8));
            ushort svRaw = (ushort)(resp[2] | (resp[3] << 8));
            byte mv = resp[4];
            byte status = resp[5];

            float pv = pvRaw / 10.0f;
            float sv = svRaw / 10.0f;

            return "PV=" + pv.ToString("F1") +
                   ",SV=" + sv.ToString("F1") +
                   ",MV=" + mv +
                   ",STATUS=" + status.ToString("X2");
        }
    }

    // ========== Modbus 设备 ==========

    public class ModbusDevice : DeviceBase
    {
        protected override bool IsValidAddress(int addr)
        {
            return addr >= 1 && addr <= 247;
        }

        protected override string GetAddressRange()
        {
            return "1-247";
        }

        protected override string ExecuteCommand(string command)
        {
            if (command.StartsWith("READ "))
            {
                string[] parts = command.Split(' ');
                if (parts.Length >= 3)
                    return ReadHold(parts[1], parts[2]);
                return "ERROR: READ reg num";
            }
            else if (command.StartsWith("WRITE "))
            {
                string[] parts = command.Split(' ');
                if (parts.Length >= 3)
                    return WriteHold(parts[1], parts[2]);
                return "ERROR: WRITE reg val";
            }
            else
                return "ERROR: Unknown command. Use: READ reg num, WRITE reg val";
        }

        private string ReadHold(string reg, string num)
        {
            SerialPort port = SerialPortManager.GetPort();
            ushort register = ushort.Parse(reg);
            ushort count = ushort.Parse(num);

            byte[] frame = BuildReadFrame(_addr, register, count);
            port.Write(frame, 0, frame.Length);

            System.Threading.Thread.Sleep(50);

            byte[] header = new byte[3];
            int read = port.Read(header, 0, 3);
            if (read < 3) return "ERROR: No response";

            byte byteCount = header[2];
            byte[] resp = new byte[3 + byteCount + 2];
            resp[0] = header[0];
            resp[1] = header[1];
            resp[2] = header[2];

            read = port.Read(resp, 3, byteCount + 2);
            if (read < byteCount + 2) return "ERROR: Incomplete response";

            string result = "";
            for (int i = 0; i < byteCount / 2; i++)
            {
                ushort val = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);
                result += "REG" + (register + i) + "=" + val + "; ";
            }

            return result.TrimEnd(' ', ';');
        }

        private string WriteHold(string reg, string val)
        {
            SerialPort port = SerialPortManager.GetPort();
            ushort register = ushort.Parse(reg);
            ushort value = ushort.Parse(val);

            byte[] frame = BuildWriteFrame(_addr, register, value);
            port.Write(frame, 0, frame.Length);

            System.Threading.Thread.Sleep(50);

            byte[] resp = new byte[8];
            int read = port.Read(resp, 0, 8);
            if (read == 8)
                return "OK: REG" + register + "=" + value;
            else
                return "ERROR: Write failed";
        }

        private byte[] BuildReadFrame(int addr, ushort reg, ushort num)
        {
            byte[] frame = new byte[6];
            frame[0] = (byte)addr;
            frame[1] = 0x03;
            frame[2] = (byte)((reg >> 8) & 0xFF);
            frame[3] = (byte)(reg & 0xFF);
            frame[4] = (byte)((num >> 8) & 0xFF);
            frame[5] = (byte)(num & 0xFF);

            ushort crc = CRC16(frame, 6);
            byte[] full = new byte[8];
            Array.Copy(frame, 0, full, 0, 6);
            full[6] = (byte)(crc & 0xFF);
            full[7] = (byte)((crc >> 8) & 0xFF);
            return full;
        }

        private byte[] BuildWriteFrame(int addr, ushort reg, ushort val)
        {
            byte[] frame = new byte[6];
            frame[0] = (byte)addr;
            frame[1] = 0x06;
            frame[2] = (byte)((reg >> 8) & 0xFF);
            frame[3] = (byte)(reg & 0xFF);
            frame[4] = (byte)((val >> 8) & 0xFF);
            frame[5] = (byte)(val & 0xFF);

            ushort crc = CRC16(frame, 6);
            byte[] full = new byte[8];
            Array.Copy(frame, 0, full, 0, 6);
            full[6] = (byte)(crc & 0xFF);
            full[7] = (byte)((crc >> 8) & 0xFF);
            return full;
        }

        private ushort CRC16(byte[] data, int len)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < len; i++)
            {
                crc ^= data[i];
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) != 0)
                        crc = (ushort)((crc >> 1) ^ 0xA001);
                    else
                        crc = (ushort)(crc >> 1);
                }
            }
            return crc;
        }
    }
}