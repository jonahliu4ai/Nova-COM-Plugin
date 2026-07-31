// MyModbusExtension.cs
// ============================================================
// 通用二进制串口扩展 + Modbus RTU 辅助类（供 Mono.Cecil Patcher 调用）
// ============================================================

using System;
using System.IO.Ports;
using System.Reflection;
using System.Text.RegularExpressions;
using EcoChemie.Communication.External;
using EcoChemie.Utils.Datastore;

namespace MyModbusExtension
{
    // 静态辅助类：Patcher 会在 EcoChemie100.dll 的 Send/Receive 中插入对它的调用
    public static class ModbusHelper
    {
        // ------------------------------------------------------------------
        // 前缀路由说明（Patcher 插入的 IL 调用此方法的签名必须不变）
        // ------------------------------------------------------------------
        // 输入格式                  → 行为
        // -------------------------    -----------------------------------
        // "MB:01 03 00 00 00 0A"    → Modbus RTU：解析 HEX，自动 CRC16
        // "HEX:00 0A 00"            → 原始 HEX：解析 HEX，直接发送
        // "00 0A 00"                → 默认原始 HEX：解析 HEX，直接发送
        // "SET 123" / "*idn?"       → 非 HEX → fallback 到 ASCII 原逻辑
        // ------------------------------------------------------------------

        public static bool TrySendModbus(ExternalRS232 device, string command, out int bytesSent)
        {
            bytesSent = 0;
            if (string.IsNullOrWhiteSpace(command)) return false;

            string cmd = command.Trim();
            string hexPart = cmd;
            bool autoCRC = false;   // 默认 RAW（不 CRC）

            // --- 前缀路由 ---
            if (cmd.StartsWith("MB:", StringComparison.OrdinalIgnoreCase))
            {
                autoCRC = true;
                hexPart = cmd.Substring(3).Trim();
            }
            else if (cmd.StartsWith("HEX:", StringComparison.OrdinalIgnoreCase))
            {
                autoCRC = false;
                hexPart = cmd.Substring(4).Trim();
            }
            // 无前缀时，如果整串是纯 HEX → 默认 RAW 模式（autoCRC = false）

            // --- 解析 HEX ---
            string stripped = hexPart.Replace(" ", "").Replace("-", "").Replace(",", "");
            if (stripped.Length < 2 || stripped.Length % 2 != 0 ||
                !Regex.IsMatch(stripped, @"^[0-9A-Fa-f]+$"))
            {
                return false;   // 不是 HEX，fallback 到 ASCII
            }

            byte[] payload = new byte[stripped.Length / 2];
            for (int i = 0; i < payload.Length; i++)
                payload[i] = Convert.ToByte(stripped.Substring(i * 2, 2), 16);

            // --- 是否追加 CRC16 ---
            byte[] frame;
            if (autoCRC)
            {
                ushort crc = 0xFFFF;
                foreach (byte b in payload)
                {
                    crc ^= b;
                    for (int j = 0; j < 8; j++)
                        crc = (crc & 0x0001) != 0
                            ? (ushort)((crc >> 1) ^ 0xA001)
                            : (ushort)(crc >> 1);
                }
                frame = new byte[payload.Length + 2];
                Buffer.BlockCopy(payload, 0, frame, 0, payload.Length);
                frame[payload.Length]     = (byte)(crc & 0xFF);
                frame[payload.Length + 1] = (byte)(crc >> 8);
            }
            else
            {
                frame = payload;   // RAW：直接发送
            }

            // --- 反射写入串口 ---
            FieldInfo field = typeof(ExternalRS232).GetField(
                "_serialport", BindingFlags.NonPublic | BindingFlags.Instance);
            SerialPort port = (SerialPort)field.GetValue(device);
            if (port == null || !port.IsOpen) return false;

            port.Write(frame, 0, frame.Length);
            bytesSent = frame.Length;
            return true;
        }

        /// <summary>
        /// 接收：有数据则返回 HEX 字符串。
        /// </summary>
        public static string TryReceiveModbus(ExternalRS232 device)
        {
            FieldInfo field = typeof(ExternalRS232).GetField(
                "_serialport", BindingFlags.NonPublic | BindingFlags.Instance);
            SerialPort port = (SerialPort)field.GetValue(device);
            if (port == null || !port.IsOpen) return null;

            System.Threading.Thread.Sleep(100);
            int count = port.BytesToRead;
            if (count == 0) return null;

            byte[] buf = new byte[count];
            port.Read(buf, 0, count);
            return BitConverter.ToString(buf).Replace("-", " ");
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
