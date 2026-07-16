// NovaCOMPlugin.cs
// 版本: 4.3 Nova Flat Access
// 修改: Stability 配置平铺到 DeviceBase，Nova 可直接访问

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;

namespace NovaCOMPlugin
{
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

        public bool IsStable
        {
            get { return Code == StateCode.Stable; }
        }

        public bool IsError
        {
            get { return Code == StateCode.Error; }
        }

        public bool IsHeating
        {
            get { return Code == StateCode.Heating; }
        }

        public bool IsReaching
        {
            get { return Code == StateCode.Reaching; }
        }
    }

    public class TemperatureData
    {
        public float PV;
        public float SV;

        public TemperatureData(float pv, float sv)
        {
            PV = pv;
            SV = sv;
        }
    }

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

        public int HistoryCount
        {
            get { return _history.Count; }
        }

        public int FailCount
        {
            get { return _failCount; }
        }

        private enum State { HEATING, REACHING, STABILIZING, STABLE }
        private State _currentState = State.HEATING;
        private DateTime _reachTime;
        private Queue<float> _history = new Queue<float>();
        private int _failCount = 0;

        public StateCode Update(float pv, float sv)
        {
            float diff = Math.Abs(pv - sv);
            LastPV = pv;
            LastSV = sv;
            LastDiff = diff;

            _history.Enqueue(diff);
            while (_history.Count > StabilizeTime)
                _history.Dequeue();

            float maxDiff = 0;
            float sumDiff = 0;
            foreach (float d in _history)
            {
                if (d > maxDiff)
                    maxDiff = d;
                sumDiff += d;
            }
            float avgDiff = _history.Count > 0 ? sumDiff / _history.Count : 0;

            switch (_currentState)
            {
                case State.HEATING:
                    if (diff <= ReachThreshold)
                    {
                        _currentState = State.REACHING;
                        _reachTime = DateTime.Now;
                        _failCount = 0;
                        return StateCode.Reaching;
                    }
                    return StateCode.Heating;

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
                        return StateCode.Heating;
                    }
                    return StateCode.Reaching;

                case State.STABILIZING:
                    if (CheckStable(diff, maxDiff, avgDiff))
                    {
                        _currentState = State.STABLE;
                        return StateCode.Stable;
                    }
                    _currentState = State.HEATING;
                    _failCount = 0;
                    return StateCode.Heating;

                case State.STABLE:
                    if (diff > MaxDeviation)
                    {
                        _currentState = State.HEATING;
                        _failCount = 0;
                        return StateCode.Heating;
                    }
                    return StateCode.Stable;

                default:
                    return StateCode.Error;
            }
        }

        private bool CheckStable(float cur, float max, float avg)
        {
            if (cur > ReachThreshold)
            {
                _failCount++;
                if (_failCount >= ConsecutiveFail)
                    return false;
            }
            else
            {
                _failCount = 0;
            }

            if (_history.Count < StabilizeTime * 0.2f)
                return false;
            if (max > MaxDeviation)
                return false;
            if (avg > AvgDeviation)
                return false;
            return true;
        }

        public void Reset()
        {
            _currentState = State.HEATING;
            _history.Clear();
            _failCount = 0;
        }

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

    public static class SerialPortManager
    {
        private static SerialPort _serialPort;
        private static bool _isOpen = false;

        public static string Open(string portName, string baudRate)
        {
            try
            {
                if (_serialPort != null && _serialPort.IsOpen)
                    return "OK";
                int baud = int.Parse(baudRate);
                _serialPort = new SerialPort(portName, baud, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 1000;
                _serialPort.WriteTimeout = 1000;
                _serialPort.Open();
                _isOpen = true;
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERR:" + ex.Message;
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
                return "OK";
            }
            catch (Exception ex)
            {
                return "ERR:" + ex.Message;
            }
        }

        public static SerialPort GetPort()
        {
            return _serialPort;
        }

        public static bool IsOpen
        {
            get { return _isOpen; }
        }

        public static string SendCommand(string command)
        {
            if (!IsOpen)
                return "ERR:Closed";
            try
            {
                command = command.Trim();
                if (IsHexEscape(command))
                    return SendHexEscape(command);
                if (IsPureHex(command))
                    return SendPureHex(command);
                return SendAscii(command);
            }
            catch (Exception ex)
            {
                return "ERR:" + ex.Message;
            }
        }

        private static bool IsHexEscape(string s)
        {
            return s.Contains("\\x") || s.Contains("\\X");
        }

        private static bool IsPureHex(string s)
        {
            s = s.Replace(" ", "").Replace("\t", "");
            if (s.Length % 2 != 0 || s.Length < 4)
                return false;
            foreach (char c in s)
            {
                if (!IsHexChar(c))
                    return false;
            }
            return true;
        }

        private static bool IsHexChar(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
        }

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
                {
                    sb.Append(hex[i + 2]);
                    sb.Append(hex[i + 3]);
                    i += 3;
                }
            }
            return SendPureHex(sb.ToString());
        }

        private static string SendPureHex(string hex)
        {
            hex = hex.Replace(" ", "").Replace("\t", "");
            if (hex.Length % 2 != 0)
                return "ERR:HexLen";
            byte[] data = new byte[hex.Length / 2];
            for (int i = 0; i < hex.Length; i += 2)
                data[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            _serialPort.Write(data, 0, data.Length);
            return ReadResponse("H:" + hex);
        }

        private static string ReadResponse(string sent)
        {
            System.Threading.Thread.Sleep(100);
            byte[] buf = new byte[256];
            int n = 0;
            try
            {
                n = _serialPort.Read(buf, 0, 256);
            }
            catch (TimeoutException) { }

            if (n == 0)
                return sent + "|";

            bool ascii = true;
            for (int i = 0; i < n; i++)
            {
                if ((buf[i] < 32 && buf[i] != 13 && buf[i] != 10) || buf[i] > 126)
                {
                    ascii = false;
                    break;
                }
            }

            if (ascii)
            {
                return sent + "|" + Encoding.ASCII.GetString(buf, 0, n).Trim();
            }
            else
            {
                StringBuilder h = new StringBuilder();
                for (int i = 0; i < n; i++)
                    h.Append(buf[i].ToString("X2"));
                return sent + "|" + h.ToString();
            }
        }

        public static string TestEcho(string input)
        {
            return input;
        }
    }

    // ========== 设备抽象基类（平铺 Stability 配置）==========

    public abstract class DeviceBase
    {
        protected int _addr = 1;
        private readonly StabilityController _stability = new StabilityController();

        // 平铺配置参数（Nova 可直接访问）
        public float ReachThreshold
        {
            get { return _stability.ReachThreshold; }
            set { _stability.ReachThreshold = value; }
        }

        public int StabilizeTime
        {
            get { return _stability.StabilizeTime; }
            set { _stability.StabilizeTime = value; }
        }

        public float MaxDeviation
        {
            get { return _stability.MaxDeviation; }
            set { _stability.MaxDeviation = value; }
        }

        public float AvgDeviation
        {
            get { return _stability.AvgDeviation; }
            set { _stability.AvgDeviation = value; }
        }

        public int ConsecutiveFail
        {
            get { return _stability.ConsecutiveFail; }
            set { _stability.ConsecutiveFail = value; }
        }

        // 平铺实时数据（Nova 可直接读取）
        public float LastPV
        {
            get { return _stability.LastPV; }
        }

        public float LastSV
        {
            get { return _stability.LastSV; }
        }

        public float LastDiff
        {
            get { return _stability.LastDiff; }
        }

        public int HistoryCount
        {
            get { return _stability.HistoryCount; }
        }

        public int FailCount
        {
            get { return _stability.FailCount; }
        }

        // 保留内部访问
        protected StabilityController Stability
        {
            get { return _stability; }
        }

        public DeviceBase() { }

        public string SetAddress(string addr)
        {
            int a;
            if (int.TryParse(addr, out a) && IsValidAddress(a))
            {
                _addr = a;
                return "OK";
            }
            return "ERR:Addr" + GetAddressRange();
        }

        public string SendCommand(string command)
        {
            if (!SerialPortManager.IsOpen)
                return "ERR:Closed";
            try
            {
                return ExecuteCommand(command.Trim().ToUpper());
            }
            catch (Exception ex)
            {
                return "ERR:" + ex.Message;
            }
        }

        public string CheckStability()
        {
            TemperatureData pvsv = ReadPVSV();
            if (pvsv == null)
                return "ERR";

            StateCode code = _stability.Update(pvsv.PV, pvsv.SV);
            switch (code)
            {
                case StateCode.Heating:
                    return "H";
                case StateCode.Reaching:
                    return "R";
                case StateCode.Stable:
                    return "OK";
                case StateCode.Error:
                    return "ERR";
                default:
                    return "H";
            }
        }

        public StateCode CheckStabilityCode()
        {
            string r = CheckStability();
            switch (r)
            {
                case "H":
                    return StateCode.Heating;
                case "R":
                    return StateCode.Reaching;
                case "OK":
                    return StateCode.Stable;
                default:
                    return StateCode.Error;
            }
        }

        public StateSnapshot GetState()
        {
            CheckStability();
            return _stability.GetSnapshot();
        }

        public string ResetStability()
        {
            _stability.Reset();
            return "OK";
        }

        protected abstract TemperatureData ReadPVSV();
        protected abstract string ExecuteCommand(string command);
        protected abstract bool IsValidAddress(int addr);
        protected abstract string GetAddressRange();
    }

    public class AIBUSDevice : DeviceBase
    {
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
            if (command.StartsWith("SET "))
                return ParseSet(command);
            return "ERR:Cmd";
        }

        protected override TemperatureData ReadPVSV()
        {
            string result = Read();
            if (!result.StartsWith("PV="))
                return null;

            string[] p = result.Split(',');
            float pv = float.Parse(p[0].Substring(3));
            float sv = float.Parse(p[1].Substring(3));
            return new TemperatureData(pv, sv);
        }

        private string Read()
        {
            SerialPort port = SerialPortManager.GetPort();
            port.Write(BuildReadFrame(_addr, 0x00), 0, 8);

            byte[] resp = new byte[10];
            int n = 0;
            int wait = 0;
            while (n < 10 && wait < 500)
            {
                if (port.BytesToRead > 0)
                {
                    int r = Math.Min(port.BytesToRead, 10 - n);
                    n += port.Read(resp, n, r);
                }
                else
                {
                    System.Threading.Thread.Sleep(10);
                    wait += 10;
                }
            }

            if (n == 10)
                return ParseResponse(resp);
            if (n > 0)
                return "ERR:Inc" + n;
            return "ERR:NoRsp";
        }

        private string ParseSet(string cmd)
        {
            string[] p = cmd.Split(' ');
            float sv;
            if (p.Length >= 2 && float.TryParse(p[1], out sv))
                return WriteSV(sv);
            return "ERR:SetFmt";
        }

        private string WriteSV(float sv)
        {
            SerialPort port = SerialPortManager.GetPort();
            ushort v = (ushort)(sv * 10);
            port.Write(BuildWriteSVFrame(_addr, v), 0, 8);

            byte[] resp = new byte[10];
            int n = 0;
            int wait = 0;
            while (n < 8 && wait < 500)
            {
                if (port.BytesToRead > 0)
                {
                    int r = Math.Min(port.BytesToRead, 10 - n);
                    n += port.Read(resp, n, r);
                }
                else
                {
                    System.Threading.Thread.Sleep(10);
                    wait += 10;
                }
            }

            if (n >= 8 && resp[2] == 0x43)
                return "OK";
            if (n > 0)
                return "ERR:Wrt" + n;
            return "ERR:WrtFail";
        }

        private byte[] BuildReadFrame(int addr, byte param)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x52, param, 0, 0, 0, 0 };
            ushort chk = (ushort)((param * 256 + 0x52 + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF);
            f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }

        private byte[] BuildWriteSVFrame(int addr, ushort val)
        {
            byte a = (byte)(0x80 + addr);
            byte[] f = new byte[8] { a, a, 0x43, 0, (byte)(val & 0xFF), (byte)((val >> 8) & 0xFF), 0, 0 };
            ushort chk = (ushort)((0 * 256 + 0x43 + val + addr) & 0xFFFF);
            f[6] = (byte)(chk & 0xFF);
            f[7] = (byte)((chk >> 8) & 0xFF);
            return f;
        }

        private string ParseResponse(byte[] r)
        {
            ushort pv = (ushort)(r[0] | (r[1] << 8));
            ushort sv = (ushort)(r[2] | (r[3] << 8));
            return "PV=" + (pv / 10.0f).ToString("F1") + ",SV=" + (sv / 10.0f).ToString("F1") + ",MV=" + r[4] + ",ST=" + r[5].ToString("X2");
        }
    }

    public class ModbusDevice : DeviceBase
    {
        public ushort PVRegister = 0;
        public ushort SVRegister = 1;

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
            string[] p = command.Split(' ');
            if (command.StartsWith("READ ") && p.Length >= 3)
                return ReadHold(p[1], p[2]);
            if (command.StartsWith("WRITE ") && p.Length >= 3)
                return WriteHold(p[1], p[2]);
            return "ERR:Cmd";
        }

        protected override TemperatureData ReadPVSV()
        {
            ushort[] result = ReadHoldRaw(PVRegister, 2);
            if (result == null || result.Length < 2)
                return null;

            float pv = result[0] / 10.0f;
            float sv = result[1] / 10.0f;
            return new TemperatureData(pv, sv);
        }

        private string ReadHold(string reg, string num)
        {
            ushort[] vals = ReadHoldRaw(ushort.Parse(reg), ushort.Parse(num));
            if (vals == null)
                return "ERR:NoRsp";

            StringBuilder sb = new StringBuilder();
            ushort baseReg = ushort.Parse(reg);
            for (int i = 0; i < vals.Length; i++)
            {
                sb.Append("R");
                sb.Append((baseReg + i).ToString());
                sb.Append("=");
                sb.Append(vals[i].ToString());
                sb.Append(";");
            }
            return sb.ToString().TrimEnd(';');
        }

        private ushort[] ReadHoldRaw(ushort reg, ushort num)
        {
            SerialPort port = SerialPortManager.GetPort();
            port.Write(BuildReadFrame(_addr, reg, num), 0, 8);
            System.Threading.Thread.Sleep(50);

            byte[] h = new byte[3];
            if (port.Read(h, 0, 3) < 3)
                return null;

            byte bc = h[2];
            byte[] resp = new byte[3 + bc + 2];
            resp[0] = h[0];
            resp[1] = h[1];
            resp[2] = h[2];
            if (port.Read(resp, 3, bc + 2) < bc + 2)
                return null;

            ushort[] vals = new ushort[bc / 2];
            for (int i = 0; i < bc / 2; i++)
                vals[i] = (ushort)(resp[3 + i * 2] << 8 | resp[4 + i * 2]);
            return vals;
        }

        private string WriteHold(string reg, string val)
        {
            SerialPort port = SerialPortManager.GetPort();
            ushort r = ushort.Parse(reg);
            ushort v = ushort.Parse(val);
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

    public static class DeviceHelper
    {
        public static bool IsStable(DeviceBase dev)
        {
            if (dev == null)
                return false;
            return dev.CheckStabilityCode() == StateCode.Stable;
        }

        public static bool IsError(DeviceBase dev)
        {
            if (dev == null)
                return true;
            return dev.CheckStabilityCode() == StateCode.Error;
        }

        public static bool IsHeating(DeviceBase dev)
        {
            if (dev == null)
                return true;
            return dev.CheckStabilityCode() == StateCode.Heating;
        }

        public static string GetTempString(DeviceBase dev)
        {
            if (dev == null)
                return "ERR";
            dev.CheckStability();
            return dev.LastPV.ToString("F1") + "/" + dev.LastSV.ToString("F1");
        }

        public static string WaitUntilStable(DeviceBase dev, int timeoutSec)
        {
            if (dev == null)
                return "ERR:Null";
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < timeoutSec)
            {
                StateCode code = dev.CheckStabilityCode();
                if (code == StateCode.Stable)
                    return "OK";
                if (code == StateCode.Error)
                    return "ERR:Device";
                System.Threading.Thread.Sleep(1000);
            }
            return "ERR:Timeout";
        }
    }
}