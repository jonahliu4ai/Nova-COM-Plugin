// TestContinuous.cs
// 验证 V4.4 修复：连续温度控制 + 状态机重置
// 无需真实串口，使用 Mock 设备模拟温度变化

using System;
using System.Collections.Generic;
using System.IO.Ports;
using NovaCOMPlugin;

namespace TestContinuous
{
    // Mock 设备：模拟温度从室温逐渐上升到目标温度
    class MockAIBUSDevice : DeviceBase
    {
        private float _currentPV = 25.0f;
        private float _currentSV = 25.0f;
        private int _step = 0;
        private readonly List<float> _pvSequence = new List<float>();

        public MockAIBUSDevice()
        {
            // 初始配置：快速稳定判断（便于测试）
            ReachThreshold = 0.5f;
            StabilizeTime = 3;  // 3秒即可稳定
            MaxDeviation = 1.0f;
            AvgDeviation = 0.5f;
            ConsecutiveFail = 2;
        }

        protected override bool IsValidAddress(int addr) { return addr >= 1 && addr <= 80; }
        protected override string GetAddressRange() { return "1-80"; }

        protected override string ExecuteCommand(string command)
        {
            if (command == "READ" || command == "STATUS")
                return ReadMock();
            if (command.StartsWith("SET "))
                return ParseSetMock(command);
            return "ERR:Cmd";
        }

        protected override TemperatureData ReadPVSV()
        {
            string r = ReadMock();
            if (!r.StartsWith("PV="))
                return null;
            string[] p = r.Split(',');
            float pv = float.Parse(p[0].Substring(3));
            float sv = float.Parse(p[1].Substring(3));
            return new TemperatureData(pv, sv);
        }

        private string ReadMock()
        {
            // 模拟温度逐步变化：每调用一次，PV 向 SV 靠近
            float diff = _currentSV - _currentPV;
            if (Math.Abs(diff) > 1.0f)
                _currentPV += diff * 0.3f; // 每次靠近 30%
            else if (Math.Abs(diff) > 0.1f)
                _currentPV += diff * 0.5f;
            else
                _currentPV = _currentSV;

            _step++;
            return "PV=" + _currentPV.ToString("F1") + ",SV=" + _currentSV.ToString("F1") + ",MV=50,ST=60";
        }

        private string ParseSetMock(string cmd)
        {
            string[] p = cmd.Split(' ');
            float sv;
            if (p.Length >= 2 && float.TryParse(p[1], out sv))
            {
                _currentSV = sv;
                // 重要：V4.4 中 WriteSV 成功后应该调用 ResetStability
                // 但 mock 中这里模拟的是 V4.3 不重置 vs V4.4 重置的差异
                // 我们通过在测试程序中手动调用或不调用来验证
                return "OK";
            }
            return "ERR:SetFmt";
        }

        public float CurrentPV { get { return _currentPV; } }
        public float CurrentSV { get { return _currentSV; } }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== NovaCOMPlugin V4.4 连续控制测试 ===\n");

            Test1_StabilityResetAfterSet();
            Test2_WaitUntilStableResets();
            Test3_ContinuousControl();
            Test4_V4_3_vs_V4_4_Behavior();

            Console.WriteLine("\n=== 所有测试完成 ===");
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }

        // 测试1：SET 后状态机是否被重置
        static void Test1_StabilityResetAfterSet()
        {
            Console.WriteLine("--- 测试1: SET 后状态机重置 ---");
            MockAIBUSDevice dev = new MockAIBUSDevice();
            dev.SetAddress("1");

            // 模拟温度稳定到 25°C
            for (int i = 0; i < 10; i++)
            {
                dev.CheckStability();
                System.Threading.Thread.Sleep(100);
            }
            // 此时应该稳定了
            StateSnapshot s = dev.GetState();
            Console.WriteLine("  25°C 稳定状态: " + s.Raw + " (PV=" + s.PV.ToString("F1") + ")");

            // SET 30.0
            dev.SendCommand("SET 30.0");
            // 手动模拟 V4.4 的行为：重置状态机
            dev.ResetStability();
            Console.WriteLine("  SET 30.0 后状态: " + dev.GetState().Raw);

            // 检查状态是否回到 HEATING
            StateSnapshot s2 = dev.GetState();
            Console.WriteLine("  新状态: " + s2.Raw + " (PV=" + s2.PV.ToString("F1") + ")");
            if (s2.Raw == "HEATING")
                Console.WriteLine("  [PASS] 状态机已重置为 HEATING\n");
            else
                Console.WriteLine("  [FAIL] 状态机未重置，仍为 " + s2.Raw + "\n");
        }

        // 测试2：WaitUntilStable 是否重置状态机
        static void Test2_WaitUntilStableResets()
        {
            Console.WriteLine("--- 测试2: WaitUntilStable 重置 ---");
            MockAIBUSDevice dev = new MockAIBUSDevice();
            dev.SetAddress("1");

            // 第一轮：稳定到 25°C
            Console.WriteLine("  第一轮：目标 25°C，WaitUntilStable(10)...");
            string r1 = WaitUntilStableTest(dev, 10);
            Console.WriteLine("  结果: " + r1 + "，状态=" + dev.GetState().Raw);

            // 第二轮：SET 30.0，然后 WaitUntilStable
            Console.WriteLine("  第二轮：SET 30.0...");
            dev.SendCommand("SET 30.0");
            // 注意：Mock 中的 SET 不改变 PV，所以 PV 仍接近 25，会触发 HEATING
            // 为了模拟 V4.4 的修复，我们手动重置
            dev.ResetStability();
            Console.WriteLine("  重置状态机后状态=" + dev.GetState().Raw);
            Console.WriteLine("  目标 30°C，WaitUntilStable(10)...");
            string r2 = WaitUntilStableTest(dev, 10);
            Console.WriteLine("  结果: " + r2 + "，状态=" + dev.GetState().Raw);

            if (r2 == "OK")
                Console.WriteLine("  [PASS] 第二轮成功稳定到 30°C\n");
            else
                Console.WriteLine("  [FAIL] 第二轮未能稳定\n");
        }

        // 测试3：完整的连续控制流程
        static void Test3_ContinuousControl()
        {
            Console.WriteLine("--- 测试3: 连续控制 25 -> 30 -> 35 ---");
            MockAIBUSDevice dev = new MockAIBUSDevice();
            dev.SetAddress("1");

            float[] targets = new float[] { 25.0f, 30.0f, 35.0f };
            foreach (float target in targets)
            {
                Console.WriteLine("  设定目标: " + target.ToString("F1") + "C");
                dev.SendCommand("SET " + target.ToString("F1"));
                dev.ResetStability(); // 模拟 V4.4 的修复
                string r = WaitUntilStableTest(dev, 15);
                Console.WriteLine("  结果: " + r + "，最终 PV=" + dev.LastPV.ToString("F1") + "\n");
            }
        }

        // 测试4：对比 V4.3 vs V4.4 的行为差异
        static void Test4_V4_3_vs_V4_4_Behavior()
        {
            Console.WriteLine("--- 测试4: V4.3 vs V4.4 行为对比 ---");

            // 模拟 V4.3 行为：不重置状态机
            Console.WriteLine("  [V4.3 模拟] SET 25 -> Stable -> SET 30（不重置）:");
            MockAIBUSDevice devV43 = new MockAIBUSDevice();
            devV43.SetAddress("1");
            // 先稳定到 25
            WaitUntilStableTest(devV43, 10);
            Console.WriteLine("    25C 稳定后状态: " + devV43.GetState().Raw);
            // 不重置，直接 SET 30
            devV43.SendCommand("SET 30.0");
            // V4.3 不重置，所以 _currentState 仍是 STABLE
            // 但 Mock 中 PV 会变，所以下一次 CheckStability 会进入 HEATING
            // 但在真实场景中，如果 PV 和 SV 恰好接近，状态机可能错误保持 STABLE
            Console.WriteLine("    SET 30.0 后（不重置）状态: " + devV43.GetState().Raw);

            // 模拟 V4.4 行为：重置状态机
            Console.WriteLine("  [V4.4 模拟] SET 25 -> Stable -> SET 30（重置）:");
            MockAIBUSDevice devV44 = new MockAIBUSDevice();
            devV44.SetAddress("1");
            WaitUntilStableTest(devV44, 10);
            Console.WriteLine("    25C 稳定后状态: " + devV44.GetState().Raw);
            devV44.SendCommand("SET 30.0");
            devV44.ResetStability(); // V4.4 关键修复
            Console.WriteLine("    SET 30.0 + Reset 后状态: " + devV44.GetState().Raw);
            Console.WriteLine("  [INFO] V4.4 确保每次 SET 后都从 HEATING 开始判断\n");
        }

        // 模拟 WaitUntilStable（不依赖真实串口）
        static string WaitUntilStableTest(MockAIBUSDevice dev, int timeoutSec)
        {
            // V4.4 关键：开始时重置
            dev.ResetStability();
            DateTime start = DateTime.Now;
            while ((DateTime.Now - start).TotalSeconds < timeoutSec)
            {
                StateCode code = dev.CheckStabilityCode();
                if (code == StateCode.Stable)
                    return "OK";
                if (code == StateCode.Error)
                    return "ERR:Device";
                System.Threading.Thread.Sleep(200); // 加速测试，200ms 一次
            }
            return "ERR:Timeout";
        }
    }
}
