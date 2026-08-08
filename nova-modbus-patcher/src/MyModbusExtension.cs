// MyModbusExtension.cs
// ============================================================
// 通用二进制串口扩展 + Modbus RTU 辅助类（供 Mono.Cecil Patcher 调用）
// ============================================================
// 2025-08-06 更新：
//   - 新增 ReceiveParseMode 枚举：Hex / Ascii / Str / DecInt16 / DecInt32 / Float32 / Float64
//   - 命令前缀支持 SEND_FMT[_RECV_FMT]:data 格式
//   - Patch 后 Receive 可根据发送时注册的格式自动解析二进制数据
//   - Modbus RTU 响应帧自动剥离：去掉地址、功能码、CRC，只保留数据负载
// ============================================================

using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using EcoChemie.Communication.External;
using EcoChemie.Utils.Datastore;

namespace MyModbusExtension
{
    /// <summary>
    /// 接收数据解析格式（大端 / Modbus 默认字节序）
    /// </summary>
    public enum ReceiveParseMode
    {
        /// <summary>HEX 字符串，如 "01 03 00 0A"</summary>
        Hex,
        /// <summary>ASCII 字符串</summary>
        Ascii,
        /// <summary>ASCII 字符串别名</summary>
        Str,
        /// <summary>16-bit 有符号整数（大端），如 "10 256 -1"</summary>
        DecInt16,
        /// <summary>32-bit 有符号整数（大端），如 "1000 65536"</summary>
        DecInt32,
        /// <summary>32-bit IEEE-754 浮点数（大端），如 "3.14 2.718"</summary>
        Float32,
        /// <summary>64-bit IEEE-754 浮点数（大端），如 "3.14159265"</summary>
        Float64,
    }

    // 静态辅助类：Patcher 会在 EcoChemie100.dll 的 Send/Receive 中插入对它的调用
    public static class ModbusHelper
    {
        // ------------------------------------------------------------------
        // 前缀路由说明（Patcher 插入的 IL 调用此方法的签名必须不变）
        // ------------------------------------------------------------------
        // 输入格式                              → 发送行为          → 接收格式
        // -----------------------------------   ----------------    -----------------
        // "MB:01 03 00 00 00 0A"              → Modbus RTU CRC    → HEX（默认）
        // "MB_DEC16:01 03 00 00 00 0A"        → Modbus RTU CRC    → DEC16
        // "MB_FLOAT:01 03 00 00 00 0A"        → Modbus RTU CRC    → FLOAT32
        // "MB_STR:01 03 00 00 00 0A"          → Modbus RTU CRC    → ASCII
        // "HEX:3F 80 00 00"                   → 原始 HEX          → HEX（默认）
        // "HEX_FLOAT:3F 80 00 00"             → 原始 HEX          → FLOAT32
        // "DEC32:01 02 03 04"                 → 原始 HEX          → DEC32
        // "FLOAT:3F 80 00 00"                 → 原始 HEX          → FLOAT32
        // "ASCII:*idn?"                       → fallback 原逻辑   → ASCII
        // "*idn?" / "READ"                    → 非 HEX fallback   → 原逻辑
        // ------------------------------------------------------------------

        // 保存每个 ExternalRS232 实例最近一次发送时设置的接收解析模式
        private static readonly Dictionary<ExternalRS232, ReceiveParseMode> _parseModes =
            new Dictionary<ExternalRS232, ReceiveParseMode>();
        // 标记该实例上一次发送是否为 Modbus RTU 帧（需要自动去帧头/CRC）
        private static readonly Dictionary<ExternalRS232, bool> _modbusModes =
            new Dictionary<ExternalRS232, bool>();
        private static readonly object _parseLock = new object();

        // TrySendModbus(ExternalRS232 device, string command, out int bytesSent) : bool
        public static bool TrySendModbus(ExternalRS232 device, string command, out int bytesSent)
        {
            bytesSent = 0;
            if (string.IsNullOrWhiteSpace(command)) return false;

            string cmd = command.Trim();
            int colonIdx = cmd.IndexOf(':');

            // --- 没有冒号：检查是否是纯 HEX（空格分隔），否则 fallback ASCII ---
            if (colonIdx < 0)
            {
                if (IsPureHex(cmd))
                {
                    lock (_parseLock)
                    {
                        _parseModes[device] = ReceiveParseMode.Hex;
                        _modbusModes[device] = false;
                    }
                    return SendRawHex(device, cmd, false, out bytesSent);
                }
                return false; // fallback 到原 ASCII 逻辑
            }

            string prefix = cmd.Substring(0, colonIdx).Trim().ToUpperInvariant();
            string data = cmd.Substring(colonIdx + 1).Trim();

            // --- 解析前缀中的 SEND_FMT 和 RECV_FMT ---
            string sendFmt;
            string recvFmt = null;
            int underIdx = prefix.IndexOf('_');
            if (underIdx >= 0)
            {
                sendFmt = prefix.Substring(0, underIdx);
                recvFmt = prefix.Substring(underIdx + 1);
            }
            else
            {
                sendFmt = prefix;
            }

            // 默认接收格式为 HEX
            ReceiveParseMode mode = ReceiveParseMode.Hex;
            if (!string.IsNullOrEmpty(recvFmt))
            {
                mode = ParseReceiveMode(recvFmt);
            }

            // --- 根据发送格式处理 ---
            if (sendFmt == "MB")
            {
                if (!IsPureHex(data)) return false;
                lock (_parseLock)
                {
                    _parseModes[device] = mode;
                    _modbusModes[device] = true;
                }
                return SendRawHex(device, data, true, out bytesSent);
            }
            else if (sendFmt == "HEX" || sendFmt == "DEC16" || sendFmt == "DEC32" ||
                     sendFmt == "FLOAT" || sendFmt == "DOUBLE" || sendFmt == "STR")
            {
                // 这些前缀本质上都是发送原始 HEX，只是默认接收格式不同
                if (!IsPureHex(data)) return false;
                if (string.IsNullOrEmpty(recvFmt))
                {
                    switch (sendFmt)
                    {
                        case "DEC16":  mode = ReceiveParseMode.DecInt16; break;
                        case "DEC32":  mode = ReceiveParseMode.DecInt32; break;
                        case "FLOAT":  mode = ReceiveParseMode.Float32; break;
                        case "DOUBLE": mode = ReceiveParseMode.Float64; break;
                        case "STR":    mode = ReceiveParseMode.Str; break;
                        default:       mode = ReceiveParseMode.Hex; break;
                    }
                }
                lock (_parseLock)
                {
                    _parseModes[device] = mode;
                    _modbusModes[device] = false;
                }
                return SendRawHex(device, data, false, out bytesSent);
            }
            else if (sendFmt == "ASCII")
            {
                // 显式要求走原 ASCII 逻辑
                return false;
            }

            // 未知前缀，尝试将整条命令当作 HEX（兼容旧用法）
            if (IsPureHex(cmd))
            {
                lock (_parseLock)
                {
                    _parseModes[device] = ReceiveParseMode.Hex;
                    _modbusModes[device] = false;
                }
                return SendRawHex(device, cmd, false, out bytesSent);
            }

            return false; // fallback
        }

        // TryReceiveModbus(ExternalRS232 device) : string
        public static string TryReceiveModbus(ExternalRS232 device)
        {
            FieldInfo field = typeof(ExternalRS232).GetField(
                "_serialport", BindingFlags.NonPublic | BindingFlags.Instance);
            SerialPort port = (SerialPort)field.GetValue(device);
            if (port == null || !port.IsOpen) return null;

            // System.Threading.Thread.Sleep(100);
            // int count = port.BytesToRead;
            // if (count == 0) return null;

            // byte[] buf = new byte[count];
            // port.Read(buf, 0, count);

            // modified by Shuai tried to fix the problem of the timeout

            int baud = port.BaudRate;
    
             // 动态计算帧间隔：3.5 字符时间 + 余量，最低 10ms
            int silenceMs = Math.Max(10, (int)(38500.0 / baud) + 5);

            // 1. 等首字节，最多等 2 秒
            DateTime deadline = DateTime.Now.AddMilliseconds(2000);
            while (port.BytesToRead == 0)
            {
                if (DateTime.Now > deadline) return null;
                System.Threading.Thread.Sleep(5);
            }

            // 2. 帧间隔：9600bps 下 3.5 字符 ≈ 4ms，取 15ms 保险
            
            List<byte> buffer = new List<byte>();
            DateTime lastByteTime = DateTime.Now;

            while (true)
            {
                int available = port.BytesToRead;
                if (available > 0)
                {
                    byte[] temp = new byte[available];
                    port.Read(temp, 0, available);
                    buffer.AddRange(temp);
                    lastByteTime = DateTime.Now;
                }
                else if ((DateTime.Now - lastByteTime).TotalMilliseconds > silenceMs)
                {
                    break;  // 帧已结束
                }
                else if (DateTime.Now > deadline)
                {
                    break;  // 总超时
                }
                else
                {
                    System.Threading.Thread.Sleep(2);
                }
            }

            if (buffer.Count == 0) return null;

            // 获取该设备期望的解析模式（消费后移除）
            ReceiveParseMode mode;
            bool isModbus;
            lock (_parseLock)
            {
                if (!_parseModes.TryGetValue(device, out mode))
                    mode = ReceiveParseMode.Hex;
                _parseModes.Remove(device);

                if (!_modbusModes.TryGetValue(device, out isModbus))
                    isModbus = false;
                _modbusModes.Remove(device);
            }

            // 若是 Modbus RTU 帧，先剥离地址、功能码、CRC，只保留数据负载
            byte[] buf = buffer.ToArray();
            if (isModbus)
                buf = StripModbusFrame(buf);

            return FormatBytes(buf, mode);
        }

        // ------------------------------------------------------------------
        // 私有辅助方法
        // ------------------------------------------------------------------

        private static bool IsPureHex(string s)
        {
            string stripped = s.Replace(" ", "").Replace("-", "").Replace(",", "");
            return stripped.Length >= 2 && stripped.Length % 2 == 0
                && Regex.IsMatch(stripped, @"^[0-9A-Fa-f]+$");
        }

        private static bool SendRawHex(ExternalRS232 device, string hexStr, bool autoCRC, out int bytesSent)
        {
            bytesSent = 0;
            string stripped = hexStr.Replace(" ", "").Replace("-", "").Replace(",", "");
            byte[] payload = new byte[stripped.Length / 2];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = Convert.ToByte(stripped.Substring(i * 2, 2), 16);

            byte[] frame;
            if (autoCRC)
            {
                ushort crc = ComputeCRC16(payload, 0, payload.Length);
                frame = new byte[payload.Length + 2];
                Buffer.BlockCopy(payload, 0, frame, 0, payload.Length);
                frame[payload.Length]     = (byte)(crc & 0xFF);
                frame[payload.Length + 1] = (byte)(crc >> 8);
            }
            else
            {
                frame = payload;
            }

            FieldInfo field = typeof(ExternalRS232).GetField(
                "_serialport", BindingFlags.NonPublic | BindingFlags.Instance);
            SerialPort port = (SerialPort)field.GetValue(device);
            if (port == null || !port.IsOpen) return false;

            port.Write(frame, 0, frame.Length);
            bytesSent = frame.Length;
            return true;
        }

        /// <summary>
        /// 计算 Modbus RTU CRC16（CRC-16/USB 多项式 0x8005，初始值 0xFFFF）
        /// </summary>
        private static ushort ComputeCRC16(byte[] data, int offset, int length)
        {
            ushort crc = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[offset + i];
                for (int j = 0; j < 8; j++)
                    crc = (crc & 0x0001) != 0
                        ? (ushort)((crc >> 1) ^ 0xA001)
                        : (ushort)(crc >> 1);
            }
            return crc;
        }

        /// <summary>
        /// 剥离 Modbus RTU 帧：去掉从机地址(1B) + 功能码(1B) + CRC(2B)，只返回数据负载。
        /// 如果 CRC 校验失败或帧格式异常，则原样返回输入数据。
        /// </summary>
        private static byte[] StripModbusFrame(byte[] frame)
        {
            if (frame == null || frame.Length < 5) // 地址+功能码+1字节数据+CRC
                return frame;

            // 验证 CRC（最后 2 字节，低字节在前）
            int dataLen = frame.Length - 2;
            ushort crcReceived = (ushort)(frame[frame.Length - 2] | (frame[frame.Length - 1] << 8));
            ushort crcComputed = ComputeCRC16(frame, 0, dataLen);
            if (crcReceived != crcComputed)
                return frame; // CRC 不匹配，返回原始数据

            byte slaveAddr = frame[0];
            byte funcCode  = frame[1];

            // --- 读寄存器 / 读线圈 / 读离散输入（功能码 0x01~0x04） ---
            // 响应格式：[地址][功能码][字节数 N][N 字节数据][CRC]
            if (funcCode == 0x01 || funcCode == 0x02 || funcCode == 0x03 || funcCode == 0x04)
            {
                int byteCount = frame[2];
                // 长度自检：地址(1)+功能码(1)+字节数(1)+数据(N)+CRC(2) = 5+N
                if (frame.Length != 5 + byteCount)
                    return frame;
                byte[] payload = new byte[byteCount];
                Buffer.BlockCopy(frame, 3, payload, 0, byteCount);
                return payload;
            }

            // --- 写单线圈 / 写单寄存器（功能码 0x05 / 0x06） ---
            // 响应格式：[地址][功能码][寄存器地址高][寄存器地址低][值高][值低][CRC]
            if (funcCode == 0x05 || funcCode == 0x06)
            {
                if (frame.Length != 8) return frame;
                byte[] payload = new byte[4];
                Buffer.BlockCopy(frame, 2, payload, 0, 4);
                return payload;
            }

            // --- 写多线圈 / 写多寄存器（功能码 0x0F / 0x10） ---
            // 响应格式：[地址][功能码][起始地址高][起始地址低][寄存器数量高][寄存器数量低][CRC]
            if (funcCode == 0x0F || funcCode == 0x10)
            {
                if (frame.Length != 8) return frame;
                byte[] payload = new byte[4];
                Buffer.BlockCopy(frame, 2, payload, 0, 4);
                return payload;
            }

            // --- 其他功能码：保守返回数据区（功能码后面的所有字节，不含 CRC） ---
            byte[] fallback = new byte[dataLen - 2];
            Buffer.BlockCopy(frame, 2, fallback, 0, dataLen - 2);
            return fallback;
        }

        private static ReceiveParseMode ParseReceiveMode(string s)
        {
            switch (s.ToUpperInvariant())
            {
                case "HEX":     return ReceiveParseMode.Hex;
                case "ASCII":   return ReceiveParseMode.Ascii;
                case "STR":     return ReceiveParseMode.Str;
                case "DEC16":   return ReceiveParseMode.DecInt16;
                case "DEC32":   return ReceiveParseMode.DecInt32;
                case "FLOAT":   return ReceiveParseMode.Float32;
                case "DOUBLE":  return ReceiveParseMode.Float64;
                default:        return ReceiveParseMode.Hex;
            }
        }

        private static string FormatBytes(byte[] data, ReceiveParseMode mode)
        {
            switch (mode)
            {
                case ReceiveParseMode.Ascii:
                case ReceiveParseMode.Str:
                    return Encoding.ASCII.GetString(data);

                case ReceiveParseMode.DecInt16:
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i + 1 < data.Length; i += 2)
                        {
                            short val = (short)((data[i] << 8) | data[i + 1]);
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(val);
                        }
                        return sb.ToString();
                    }

                case ReceiveParseMode.DecInt32:
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i + 3 < data.Length; i += 4)
                        {
                            int val = (data[i] << 24) | (data[i + 1] << 16)
                                    | (data[i + 2] << 8) | data[i + 3];
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(val);
                        }
                        return sb.ToString();
                    }

                case ReceiveParseMode.Float32:
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i + 3 < data.Length; i += 4)
                        {
                            byte[] le = new byte[4];
                            if (BitConverter.IsLittleEndian)
                            {
                                le[0] = data[i + 3];
                                le[1] = data[i + 2];
                                le[2] = data[i + 1];
                                le[3] = data[i];
                            }
                            else
                            {
                                Array.Copy(data, i, le, 0, 4);
                            }
                            float val = BitConverter.ToSingle(le, 0);
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(val.ToString("G6"));
                        }
                        return sb.ToString();
                    }

                case ReceiveParseMode.Float64:
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i + 7 < data.Length; i += 8)
                        {
                            byte[] le = new byte[8];
                            if (BitConverter.IsLittleEndian)
                            {
                                for (int j = 0; j < 8; j++)
                                    le[j] = data[i + 7 - j];
                            }
                            else
                            {
                                Array.Copy(data, i, le, 0, 8);
                            }
                            double val = BitConverter.ToDouble(le, 0);
                            if (sb.Length > 0) sb.Append(' ');
                            sb.Append(val.ToString("G9"));
                        }
                        return sb.ToString();
                    }

                case ReceiveParseMode.Hex:
                default:
                    return BitConverter.ToString(data).Replace("-", " ");
            }
        }
    }

    // 保留原来的 ModbusRTUDevice（供 pythonnet 脚本使用）
    public class ModbusRTUDevice : ExternalRS232
    {
        private SerialPort _port;

        public ModbusRTUDevice() : base() { }

        public override void Initialize()
        {
            base.Initialize();
            FieldInfo field = typeof(ExternalRS232).GetField(
                "_serialport", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                _port = (SerialPort)field.GetValue(this);
            if (_port == null)
                throw new InvalidOperationException("无法获取底层 SerialPort。");
        }

        // pythonnet 调用入口：保持原签名，内部走相同路由逻辑
        public override int Send(string command)
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("串口未打开。");

            int bytesSent;
            if (ModbusHelper.TrySendModbus(this, command, out bytesSent))
                return bytesSent;

            // fallback：ASCII
            _port.WriteLine(command);
            return command.Length;
        }

        public override string Receive()
        {
            if (_port == null || !_port.IsOpen) return string.Empty;
            System.Threading.Thread.Sleep(100);
            int count = _port.BytesToRead;
            if (count == 0) return string.Empty;
            byte[] buf = new byte[count];
            _port.Read(buf, 0, count);
            return BitConverter.ToString(buf).Replace("-", " ");
        }
    }
}
