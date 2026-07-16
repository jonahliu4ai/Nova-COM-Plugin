// NovaCOMPlugin.cs
// 版本: 2.0 Final (继承版)
// 功能: Nova 软件 .NET 插件，支持 AIBUS/Modbus 协议转换
// 智能命令识别（ASCII/HEX自动判断）
// 单串口多设备共享
// 抽象基类设计，便于扩展

using System;
using System.IO.Ports;
using System.Text;

namespace NovaCOMPlugin
{
    // ========== 串口管理器（Static，全局共享）==========

    public static class SerialPortManager
    {
        private static SerialPort _serialPort;
        private static bool _isOpen = false;

        /// <summary>
        /// 打开串口
        /// </summary>
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

        /// <summary>
        /// 关闭串口
        /// </summary>
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

        /// <summary>
        /// 获取串口实例
        /// </summary>
        public static SerialPort GetPort()
        {
            return _serialPort;
        }

        /// <summary>
        /// 串口是否打开
        /// </summary>
        public static bool IsOpen
        {
            get { return _isOpen; }
        }

        // ========== 智能命令发送 ==========

        /// <summary>
        /// 智能发送命令
        /// 自动判断 ASCII 或 HEX：
        /// - 含 \xHH 转义 → 解析为 HEX
        /// - 纯数字和空格（如 "81 52 00"）→ 解析为 HEX
        /// - 其他 → 作为 ASCII 发送
        /// </summary>
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

        // ========== 判断方法 ==========

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

        // ========== 发送方法 ==========

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

        // ========== 读取响应 ==========

        private static string WaitAndRead(string sentInfo)
        {
            System.Threading.Thread.Sleep(100);

            byte[] resp = new byte[256];
            int bytesRead = 0;

            try
            {
                bytesRead = _serialPort.Read(resp, 0, resp.Length);
            }
            catch (TimeoutException)
            {
                // 超时无响应
            }

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

        // ========== 测试方法 ==========

        public static string TestEcho(string input)
        {
            return "Echo: " + input;
        }
    }

    // ========== 设备抽象基类 ==========

    public abstract class DeviceBase
    {
        protected int _addr = 1;

        // 无参构造函数（Nova 必需）
        public DeviceBase() { }

        // 公共方法：设置地址
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

        // 公共方法：发送命令入口
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

        // 抽象方法：子类实现具体协议
        protected abstract string ExecuteCommand(string command);

        // 抽象方法：子类提供地址范围验证
        protected abstract bool IsValidAddress(int addr);

        // 抽象方法：子类提供地址范围说明
        protected abstract string GetAddressRange();
    }

    // ========== AIBUS 设备 ==========

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
            else if (command.StartsWith("SET "))
                return ParseSetCommand(command);
            else
                return "ERROR: Unknown command. Use: READ, SET xx.x, STATUS";
        }

        private string Read()
        {
            SerialPort port = SerialPortManager.GetPort();
            byte[] frame = BuildReadFrame(_addr, 0x00);
            port.Write(frame, 0, frame.Length);

            System.Threading.Thread.Sleep(50);

            byte[] resp = new byte[10];
            int bytesRead = port.Read(resp, 0, 10);

            if (bytesRead == 10)
                return ParseResponse(resp);
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

            System.Threading.Thread.Sleep(50);

            byte[] resp = new byte[10];
            int bytesRead = port.Read(resp, 0, 10);

            if (bytesRead >= 8 && resp[2] == 0x43)
                return "OK: SV set to " + sv.ToString("F1");
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
