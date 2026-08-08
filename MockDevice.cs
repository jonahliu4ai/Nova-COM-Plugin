// MockDevice.cs
// 用途：COM0COM 虚拟串口对测试的从机端 Mock
// 连接：Demo (COM7) <-> COM0COM <-> Mock (COM8)
// 支持：AIBUS / Modbus-RTU / FixedFrame 三种协议的伪响应

using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace MockDevice
{
    class Program
    {
        static SerialPort _port;
        static bool _running = true;

        static void Main(string[] args)
        {
            string comPort = args.Length > 0 ? args[0] : "COM8";
            int baud = args.Length > 1 ? int.Parse(args[1]) : 9600;

            Console.WriteLine("=== Mock Device ===");
            Console.WriteLine("Listening on " + comPort + " @ " + baud + " baud");
            Console.WriteLine("Supported protocols: AIBUS, Modbus-RTU, FixedFrame");
            Console.WriteLine("Press Ctrl+C to exit\n");

            try
            {
                _port = new SerialPort(comPort, baud, Parity.None, 8, StopBits.One);
                _port.ReadTimeout = 500;
                _port.WriteTimeout = 500;
                _port.Open();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Open failed: " + ex.Message);
                return;
            }

            byte[] buf = new byte[256];
            while (_running)
            {
                try
                {
                    int n = _port.Read(buf, 0, 256);
                    if (n > 0)
                    {
                        byte[] frame = new byte[n];
                        Array.Copy(buf, 0, frame, 0, n);
                        LogRx(frame);
                        byte[] resp = BuildResponse(frame);
                        if (resp != null && resp.Length > 0)
                        {
                            Thread.Sleep(20); // 模拟设备响应延迟
                            _port.Write(resp, 0, resp.Length);
                            LogTx(resp);
                        }
                    }
                }
                catch (TimeoutException) { /* no data */ }
                catch (Exception ex)
                {
                    Console.WriteLine("Error: " + ex.Message);
                }
            }

            _port.Close();
        }

        // ================================================================
        // 协议识别与响应构造
        // ================================================================
        static byte[] BuildResponse(byte[] rx)
        {
            if (rx.Length < 2) return null;

            // ---- AIBUS 识别：帧长=8，前2字节相同（0x80+addr），第3字节=0x52/0x43
            if (rx.Length == 8 && rx[0] == rx[1] && (rx[0] & 0x80) != 0)
            {
                if (rx[2] == 0x52) return BuildAIBUSReadResponse(rx);   // 读 PV/SV
                if (rx[2] == 0x43) return BuildAIBUSWriteResponse(rx);  // 写 SV 确认
            }

            // ---- Modbus RTU 识别：功能码在位置1
            if (rx.Length >= 4)
            {
                byte addr = rx[0];
                byte fc = rx[1];

                // FC=0x03 Read Holding Registers
                if (fc == 0x03 && rx.Length == 8)
                {
                    ushort reg = (ushort)(rx[2] << 8 | rx[3]);
                    ushort num = (ushort)(rx[4] << 8 | rx[5]);
                    return BuildModbusReadResponse(addr, reg, num);
                }
                // FC=0x06 Write Single Register
                if (fc == 0x06 && rx.Length == 8)
                {
                    return BuildModbusEchoResponse(rx); // 写操作回显请求帧
                }
                // FC=0x01 Read Coils
                if (fc == 0x01 && rx.Length == 8)
                {
                    ushort start = (ushort)(rx[2] << 8 | rx[3]);
                    ushort count = (ushort)(rx[4] << 8 | rx[5]);
                    return BuildModbusReadCoilsResponse(addr, count);
                }
                // FC=0x05 Write Single Coil
                if (fc == 0x05 && rx.Length == 8)
                {
                    return BuildModbusEchoResponse(rx); // 回显
                }
            }

            // ---- FixedFrame：无法识别的固定帧，直接回显（让测试者看到发送内容）
            Console.WriteLine("  [FixedFrame/Unknown] -> Echo back");
            return rx;
        }

        // ================================================================
        // AIBUS 响应
        // ================================================================
        static byte[] BuildAIBUSReadResponse(byte[] rx)
        {
            // 响应：PV(2) + SV(2) + MV(1) + ST(1) + 校验(2) = 10 字节
            // 模拟 PV=250 (25.0°C), SV=250 (25.0°C), MV=45, ST=0x00
            byte[] resp = new byte[10];
            resp[0] = 0x00; resp[1] = 0xFA; // PV = 250
            resp[2] = 0x00; resp[3] = 0xFA; // SV = 250
            resp[4] = 0x2D;                 // MV = 45
            resp[5] = 0x00;                 // ST = 0x00
            // 校验（简单和校验，与宇电 AIBUS 一致）
            ushort chk = (ushort)((0x00 * 256 + 0x52 + (rx[0] - 0x80)) & 0xFFFF);
            resp[8] = (byte)(chk & 0xFF);
            resp[9] = (byte)((chk >> 8) & 0xFF);
            return resp;
        }

        static byte[] BuildAIBUSWriteResponse(byte[] rx)
        {
            // 写 SV 确认：返回原帧前 8 字节即可（宇电通常回显）
            byte[] resp = new byte[8];
            Array.Copy(rx, 0, resp, 0, 8);
            resp[2] = 0x43; // 确认码
            return resp;
        }

        // ================================================================
        // Modbus RTU 响应
        // ================================================================
        static byte[] BuildModbusReadResponse(byte addr, ushort reg, ushort num)
        {
            int bc = num * 2;
            byte[] data = new byte[3 + bc + 2];
            data[0] = addr;
            data[1] = 0x03;
            data[2] = (byte)bc;
            for (int i = 0; i < num; i++)
            {
                // 模拟寄存器值：reg0=250, reg1=250, 其余=0
                ushort val = (ushort)((reg + i == 0 || reg + i == 1) ? 250 : 0);
                data[3 + i * 2] = (byte)(val >> 8);
                data[4 + i * 2] = (byte)(val & 0xFF);
            }
            ushort crc = CRC16(data, 3 + bc);
            data[3 + bc] = (byte)(crc & 0xFF);
            data[4 + bc] = (byte)(crc >> 8);
            return data;
        }

        static byte[] BuildModbusReadCoilsResponse(byte addr, ushort count)
        {
            int bc = (count + 7) / 8;
            byte[] data = new byte[3 + bc + 2];
            data[0] = addr;
            data[1] = 0x01;
            data[2] = (byte)bc;
            // 模拟所有 coil 为 0
            for (int i = 0; i < bc; i++) data[3 + i] = 0x00;
            ushort crc = CRC16(data, 3 + bc);
            data[3 + bc] = (byte)(crc & 0xFF);
            data[4 + bc] = (byte)(crc >> 8);
            return data;
        }

        static byte[] BuildModbusEchoResponse(byte[] rx)
        {
            // Modbus 写操作通常回显请求帧
            byte[] resp = new byte[rx.Length];
            Array.Copy(rx, 0, resp, 0, rx.Length);
            return resp;
        }

        static ushort CRC16(byte[] d, int len)
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

        // ================================================================
        // 日志
        // ================================================================
        static void LogRx(byte[] data)
        {
            Console.Write("RX [" + data.Length + "] ");
            for (int i = 0; i < data.Length; i++) Console.Write(data[i].ToString("X2") + " ");
            Console.WriteLine();
        }

        static void LogTx(byte[] data)
        {
            Console.Write("TX [" + data.Length + "] ");
            for (int i = 0; i < data.Length; i++) Console.Write(data[i].ToString("X2") + " ");
            Console.WriteLine("\n");
        }
    }
}
