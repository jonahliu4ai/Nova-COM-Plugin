// TestJsonLoader.cs
// 验证 V4.5.1 JsonLite 零依赖解析：加载全部 devices/*.json 并校验关键字段
using System;
using NovaCOMPlugin;

namespace TestJsonLoader
{
    class Program
    {
        static int pass = 0, fail = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("=== JsonLite Zero-Dependency Loader Test ===\n");

            // 指向 devices 目录（EXE 与 devices 同级）
            ProfileLoader.ProfileDirectory = "devices";
            ProfileLoader.ClearCache();

            string[] profiles = new string[]
            {
                "宇电_AI708", "欧姆龙_E5CC", "华控_8路继电器",
                "某厂_4路继电器", "七星华创_CS200A"
            };

            foreach (string name in profiles)
                TestProfile(name);

            // 负例：不存在的配置文件应返回 null
            Console.WriteLine("--- Negative test ---");
            var bad = ProfileLoader.Load("不存在设备");
            Assert(bad == null, "Missing profile returns null");

            Console.WriteLine("\n==============================");
            Console.WriteLine("PASS: {0}, FAIL: {1}", pass, fail);
            Console.WriteLine("==============================");
        }

        static void TestProfile(string name)
        {
            Console.WriteLine("--- {0} ---", name);
            DeviceProfile p = ProfileLoader.Load(name);
            if (p == null)
            {
                Assert(false, name + " loaded");
                return;
            }
            Console.WriteLine("  protocol={0}, baud={1}, addr={2}",
                p.protocol, p.default_baudrate, p.default_address);
            Assert(p.protocol != null, "protocol parsed");
            Assert(p.registers != null && p.registers.Count > 0,
                "registers parsed (" + (p.registers == null ? 0 : p.registers.Count) + ")");

            foreach (var kv in p.registers)
            {
                RegisterDef r = kv.Value;
                Console.WriteLine("    reg[{0}]: action={1} type={2} scale={3} unit={4}",
                    kv.Key, r.action, r.type, r.scale, r.unit);
            }

            Assert(p.commands != null && p.commands.Count > 0,
                "commands parsed (" + (p.commands == null ? 0 : p.commands.Count) + ")");
            foreach (var kv in p.commands)
            {
                CommandDef c = kv.Value;
                string extra = "";
                if (c.@params != null) extra += " params=[" + string.Join(",", c.@params.ToArray()) + "]";
                if (c.value.HasValue) extra += " value=" + c.value.Value;
                if (c.addr_expr != null) extra += " addr_expr=" + c.addr_expr;
                Console.WriteLine("    cmd[{0}]: action={1} register={2}{3}",
                    kv.Key, c.action, c.register, extra);
            }
        }

        static void Assert(bool cond, string msg)
        {
            if (cond) { Console.WriteLine("  [PASS] " + msg); pass++; }
            else { Console.WriteLine("  [FAIL] " + msg); fail++; }
        }
    }
}
