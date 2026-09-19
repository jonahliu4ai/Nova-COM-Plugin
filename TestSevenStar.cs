// TestSevenStar.cs
// 用 COM0COM 虚拟串口对验证 SevenStar 引擎修复（协议官方示例帧）
using System;
using System.IO.Ports;
using System.Threading;
using NovaCOMPlugin;

namespace TestSevenStar
{
    class Program
    {
        static int pass = 0, fail = 0;

        static void Assert(bool cond, string msg)
        {
            Console.WriteLine((cond ? "  [PASS] " : "  [FAIL] ") + msg);
            if (cond) pass++; else fail++;
        }

        static void Main(string[] args)
        {
            string masterPort = args.Length > 0 ? args[0] : "COM7";   // 插件侧
            string slavePort  = args.Length > 1 ? args[1] : "COM8";   // 模拟设备侧

            ProfileLoader.ProfileDirectory = "devices";
            ProfileLoader.ClearCache();

            // ---- 模拟设备线程：按官方协议示例应答 ----
            var slave = new SerialPort(slavePort, 19200, Parity.None, 8, StopBits.One);
            slave.ReadTimeout = 5000;
            slave.Open();
            var t = new Thread(() =>
            {
                byte[] buf = new byte[64];
                while (true)
                {
                    int n;
                    try { n = slave.Read(buf, 0, 1); } catch { break; }
                    if (n == 0) continue;
                    // 收完一帧（按协议地址过滤：0x20）
                    try
                    {
                        // 已收到第1字节(地址)，继续收 8 字节（最小帧 9 字节）
                        int got = 1;
                        while (got < 9) got += slave.Read(buf, got, 9 - got);
                        int dataLen = buf[3];
                        int total = 6 + dataLen;
                        while (got < total) got += slave.Read(buf, got, total - got);
                    }
                    catch { break; }

                    byte service = buf[2], cls = buf[4], attr = buf[6];
                    byte[] resp;
                    if (service == 0x80 && cls == 0x68 && attr == 0xB9)
                        resp = new byte[] { 0x06,0x00,0x02,0x80,0x05,0x68,0x01,0xB9,0x3D,0x4F,0x00,0x35 };
                    else if (service == 0x80 && cls == 0x69 && attr == 0x03)
                        resp = new byte[] { 0x06,0x00,0x02,0x80,0x04,0x69,0x01,0x03,0x02,0x00,0xF3 };
                    else if (service == 0x81 && cls == 0x69 && attr == 0xA4)
                        resp = new byte[] { 0x06,0x00,0x02,0x80,0x05,0x69,0x01,0xA4, buf[7],buf[8],0x00,0x00 };
                    else if (service == 0x81 && cls == 0x69 && attr == 0x06)
                        resp = new byte[] { 0x06,0x00,0x02,0x80,0x04,0x69,0x01,0x06,0x01,0x00,0xF6 };
                    else
                    {
                        // 通用回显：ACK + 空数据
                        resp = new byte[] { 0x06,0x00,0x02,0x80,0x03,cls,0x01,attr,0x00,0x00 };
                    }
                    resp[resp.Length-1] = 0;
                    for (int i = 1; i < resp.Length - 1; i++) resp[resp.Length-1] += resp[i];
                    slave.Write(resp, 0, resp.Length);
                }
            });
            t.IsBackground = true;
            t.Start();
            Thread.Sleep(300);

            // ---- 主站：插件引擎 ----
            Console.WriteLine("=== SevenStar 引擎虚拟串口联调 ===");
            SerialPortManager.Open(masterPort, "19200");
            SmartDevice dev = new SmartDevice("七星华创_CS200A", "32");
            Assert(dev.ProfileName != null, "profile loaded: " + dev.ProfileName);

            string r;

            r = dev.SendCommand("read_flow");
            Console.WriteLine("  read_flow -> " + r);
            Assert(r.StartsWith("VALUE=11.905"), "read_flow = 11.905 sccm（官方示例 0x4F3D=11.9%FS）");

            r = dev.SendCommand("read_control_mode");
            Console.WriteLine("  read_control_mode -> " + r);
            Assert(r.Contains("VALUE=2"), "control_mode = 2（模拟电压模式）");

            r = dev.SendCommand("set_control_mode 1");
            Console.WriteLine("  set_control_mode 1 -> " + r);
            Assert(r == "OK", "set_control_mode 写帧 DataLen=4 被正确解析");

            r = dev.SendCommand("set_flow 50");
            Console.WriteLine("  set_flow 50 -> " + r);
            Assert(r == "OK", "set_flow 写帧 DataLen=5 被正确解析");

            r = dev.SendCommand("eeprom_program 1");
            Console.WriteLine("  eeprom_program 1 -> " + r);
            Assert(r == "OK", "eeprom_program OK");

            SerialPortManager.Close();
            slave.Close();

            Console.WriteLine("\n==============================");
            Console.WriteLine("PASS: {0}, FAIL: {1}", pass, fail);
            Console.WriteLine("==============================");
            Environment.Exit(fail > 0 ? 1 : 0);
        }
    }
}
