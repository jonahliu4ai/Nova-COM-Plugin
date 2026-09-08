// TestIntegration.cs
// COM0COM 联调测试：COM7 (主站/本程序) <-> COM8 (MockDevice 从机)
// 验证 V4.5.1 完整链路：JSON 加载 -> 引擎组帧 -> 串口 -> Mock 响应 -> 解析
using System;
using NovaCOMPlugin;

namespace TestIntegration
{
    class Program
    {
        static int pass = 0, fail = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("=== V4.5.1 Integration Test (COM7 <-> MockDevice COM8) ===\n");

            // 1. 打开串口
            string r = SerialPortManager.Open("COM7", "9600");
            Assert(r == "OK", "Open COM7: " + r);
            if (r != "OK") return;

            // 2. AIBUS 温控器（宇电_AI708）
            Console.WriteLine("--- AIBUS (宇电_AI708, addr=1) ---");
            SmartDevice heater = new SmartDevice("宇电_AI708", "1");
            Assert(heater.ProfileName != null, "profile loaded: " + heater.ProfileName);
            if (heater.ProfileName != null)
            {
                Assert(heater.Protocol == "aibus", "protocol=aibus");

                r = heater.SendCommand("set_sv 25.0");
                Assert(r == "OK", "set_sv 25.0 -> " + r);

                r = heater.SendCommand("read_pv");
                Console.WriteLine("      read_pv -> " + r);
                Assert(r != null && r.StartsWith("PV="), "read_pv parsed: " + r);
                Assert(r.Contains("SV=25.0"), "SV echoed by Mock = 25.0");
            }

            // 3. Modbus 继电器（华控_8路继电器）
            Console.WriteLine("--- Modbus-RTU (华控_8路继电器, addr=2) ---");
            SmartDevice relay = new SmartDevice("华控_8路继电器", "2");
            Assert(relay.ProfileName != null, "profile loaded");
            if (relay.ProfileName != null)
            {
                r = relay.SendCommand("open_ch N=3");
                Assert(r == "OK", "open_ch N=3 -> " + r);

                r = relay.SendCommand("close_ch N=3");
                Assert(r == "OK", "close_ch N=3 -> " + r);

                r = relay.SendCommand("open_all");
                Assert(r == "OK", "open_all -> " + r);

                r = relay.SendCommand("close_all");
                Assert(r == "OK", "close_all -> " + r);
            }

            // 4. 固定帧继电器（某厂_4路继电器）
            Console.WriteLine("--- FixedFrame (某厂_4路继电器, addr=0) ---");
            SmartDevice fx = new SmartDevice("某厂_4路继电器", "0");
            Assert(fx.ProfileName != null, "profile loaded");
            if (fx.ProfileName != null)
            {
                r = fx.SendCommand("open_ch1");
                Console.WriteLine("      open_ch1 -> " + r);
                Assert(r != null && r.StartsWith("OK"), "open_ch1 acked: " + r);

                r = fx.SendCommand("close_all");
                Console.WriteLine("      close_all -> " + r);
            }

            // 5. SevenStar 流量计（MockDevice 可能不支持，仅验证组帧发送不报错）
            Console.WriteLine("--- SevenStar (七星华创_CS200A, addr=32) ---");
            SmartDevice flow = new SmartDevice("七星华创_CS200A", "32");
            Assert(flow.ProfileName != null, "profile loaded");
            if (flow.ProfileName != null)
            {
                Assert(flow.Protocol == "sevenstar", "protocol=sevenstar");
                r = flow.SendCommand("read_flow");
                Console.WriteLine("      read_flow -> " + r + " (Mock 不支持时超时可接受)");
            }

            // 6. V4.4 兼容路径
            Console.WriteLine("--- V4.4 backward compat (AIBUSDevice) ---");
            AIBUSDevice old = new AIBUSDevice();
            old.SetAddress("1");
            r = old.SendCommand("SET 25.0");
            Assert(r == "OK", "V4.4 SET 25.0 -> " + r);
            r = old.SendCommand("READ");
            Console.WriteLine("      READ -> " + r);
            Assert(r != null && r.StartsWith("PV="), "V4.4 READ parsed");

            SerialPortManager.Close();

            Console.WriteLine("\n==============================");
            Console.WriteLine("PASS: {0}, FAIL: {1}", pass, fail);
            Console.WriteLine("==============================");
        }

        static void Assert(bool cond, string msg)
        {
            if (cond) { Console.WriteLine("  [PASS] " + msg); pass++; }
            else { Console.WriteLine("  [FAIL] " + msg); fail++; }
        }
    }
}
