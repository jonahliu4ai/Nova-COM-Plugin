using System;
using ComComMiddleware;

public class SmokeTests
{
    private static int _pass;
    private static int _fail;

    public static int Main(string[] args)
    {
        var outEnc = Console.OutputEncoding;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        TestParser();
        TestJsonLite();
        TestHexUtil();
        TestFactory();
        TestSevenStar();
        TestAlias();

        Console.WriteLine("PASS=" + _pass + " FAIL=" + _fail);

        Console.OutputEncoding = outEnc;
        return _fail;
    }

    private static void Check(string name, bool ok)
    {
        if (ok)
        {
            _pass++;
            Console.WriteLine("[PASS] " + name);
        }
        else
        {
            _fail++;
            Console.WriteLine("[FAIL] " + name);
        }
    }

    private static void TestParser()
    {
        ComCommand c1 = ComCommandParser.Parse("DEVICE=dev01;ADDR=2;CMD=set_sv 25.5");
        Check("Parse KV", c1 != null && c1.DeviceName == "dev01" && c1.Address == "2" && c1.Command == "set_sv" && c1.Args == "25.5");

        ComCommand c2 = ComCommandParser.Parse("@devA ADDR=3 read_pv");
        Check("Parse AT", c2 != null && c2.DeviceName == "devA" && c2.Address == "3" && c2.Command == "read_pv" && string.IsNullOrEmpty(c2.Args));

        ComCommand c3 = ComCommandParser.Parse("@devB state=0 set_sv 10");
        Check("Parse AT with state", c3 != null && c3.DeviceName == "devB" && c3.Address == null && c3.Command == "set_sv" && c3.Args == "10");

        ComCommand c4 = ComCommandParser.Parse("RAW:HEX:0A 1B 2C");
        Check("Parse RAW HEX", c4 != null && c4.RawMode && c4.RawBytes.Length == 3 && c4.RawBytes[0] == 0x0A && c4.RawBytes[2] == 0x2C);

        ComCommand c5 = ComCommandParser.Parse("RAW:TXT:hello world");
        string rawText = c5 != null && c5.RawMode ? System.Text.Encoding.UTF8.GetString(c5.RawBytes) : string.Empty;
        Check("Parse RAW TXT", c5 != null && c5.RawMode && rawText == "hello world");

        ComCommand c6 = ComCommandParser.Parse("@devC state=0 read_multi 1,2,3");
        Check("Parse AT state as named arg", c6 != null && c6.DeviceName == "devC" && c6.Command == "read_multi" && c6.Args == "1,2,3" && c6.Address == null);
    }

    private static void TestJsonLite()
    {
        string json = "{\"name\":\"dev\",\"protocol\":\"modbus-rtu\",\"default_baudrate\":9600,\"default_address\":1,\"registers\":{\"pv\":{\"fc\":3,\"addr\":10,\"type\":\"int16\",\"scale\":0.1,\"unit\":\"C\"}},\"commands\":{\"read_pv\":{\"action\":\"read\",\"register\":\"pv\"}}}";
        object root = JsonLite.Parse(json);
        DeviceProfile p = JsonLite.MapProfile(JsonLite.AsObj(root));
        RegisterDef reg = null;
        CommandDef cmd = null;

        if (p != null && p.registers != null)
        {
            p.registers.TryGetValue("pv", out reg);
        }
        if (p != null && p.commands != null)
        {
            p.commands.TryGetValue("read_pv", out cmd);
        }

        Check("JsonLite", p != null && p.name == "dev" && p.protocol == "modbus-rtu" && reg != null && reg.addr == 10 && cmd != null && cmd.action == "read");

        string customJson = "{\"name\":\"dev2\",\"protocol\":\"custom\",\"default_baudrate\":9600,\"default_address\":2,\"commands\":{\"at_cmd\":{\"send\":\"AT+CMD={ch}\\r\\n\",\"send_mode\":\"ascii\"}}}";
        object customRoot = JsonLite.Parse(customJson);
        DeviceProfile customProfile = JsonLite.MapProfile(JsonLite.AsObj(customRoot));
        CommandDef atCmd = null;
        if (customProfile != null && customProfile.commands != null)
        {
            customProfile.commands.TryGetValue("at_cmd", out atCmd);
        }
        Check("JsonLite send_mode", atCmd != null && atCmd.send == "AT+CMD={ch}\r\n" && atCmd.send_mode == "ascii");
    }

    private static void TestHexUtil()
    {
        byte[] bytes = HexUtil.ParseHexBytes("AA 01 0f");
        Check("Hex parse", bytes != null && bytes.Length == 3 && bytes[0] == 0xAA && bytes[2] == 0x0F);
        Check("Hex to text", HexUtil.ToHex(bytes) == "AA 01 0F");
    }

    private static void TestFactory()
    {
        using (var m = ProtocolEngineFactory.Create("modbus-rtu"))
        using (var f = ProtocolEngineFactory.Create("fixed-frame"))
        using (var c = ProtocolEngineFactory.Create("custom"))
        using (var s = ProtocolEngineFactory.Create("sevenstar"))
        {
            Check("Engine factory", m != null && f != null && c != null && s != null && ProtocolEngineFactory.Create("none") == null);
        }
    }

    private static void TestSevenStar()
    {
        // 依据 SevenStar_Control_Manual.md（2026-09-25 手动校验版）验证引擎编解码
        DeviceProfile p = new DeviceProfile();
        p.full_scale = 100;
        var engine = ProtocolEngineFactory.Create("sevenstar");
        engine.Init(p, 66, null);

        // 手册 §3.2: 10%FS -> raw = 0x4CCC
        var enc = InvokePrivate(engine, "UFrac16Encode", new System.Type[] { typeof(double) }, new object[] { 10.0 });
        Check("SS UFrac16Encode(10%)=0x4CCC", enc is ushort && (ushort)enc == 0x4CCC);

        // 手册 §5.2: 0x4F3D -> 11.9%FS（官方 V2.3 示例）
        var dec = InvokePrivate(engine, "UFrac16Decode", new System.Type[] { typeof(ushort) }, new object[] { (ushort)0x4F3D });
        Check("SS UFrac16Decode(0x4F3D)~11.9%", dec is double && System.Math.Abs((double)dec - 11.9049) < 0.01);

        // 手册 §3.3: 0x3F3E -> -0.59%FS（负流量，阀门关闭后正常）
        var decNeg = InvokePrivate(engine, "UFrac16Decode", new System.Type[] { typeof(ushort) }, new object[] { (ushort)0x3F3E });
        Check("SS UFrac16Decode(0x3F3E)~-0.59%", decNeg is double && System.Math.Abs((double)decNeg - (-0.59)) < 0.01);

        // 手册 §4.3: 地址 66 写 10 sccm 完整帧 = 42 02 81 05 69 01 A4 CC 4C 00 F0
        var regFlow = new RegisterDef { type = "ufrac16", unit = "sccm" };
        var data = InvokePrivate(engine, "EncodeWriteData", new System.Type[] { typeof(RegisterDef), typeof(string), typeof(string) },
            new object[] { regFlow, "10", "float" }) as byte[];
        Check("SS EncodeWriteData(10 sccm)=CC 4C", data != null && data.Length == 2 && data[0] == 0xCC && data[1] == 0x4C);
        var frame = InvokePrivate(engine, "BuildFrame", new System.Type[] { typeof(byte), typeof(byte), typeof(byte), typeof(byte), typeof(byte[]) },
            new object[] { (byte)0x81, (byte)0x69, (byte)0x01, (byte)0xA4, data }) as byte[];
        string hex = frame == null ? "" : HexUtil.ToHex(frame).Replace(" ", "");
        Check("SS BuildFrame(写10sccm)=420281056901A4CC4C00F0", hex == "420281056901A4CC4C00F0");

        // 官方 V2.3 示例响应 06 00 02 80 05 68 01 B9 3D 4F 00 35 -> 11.905 sccm
        byte[] rsp = HexUtil.ParseHexBytes("06 00 02 80 05 68 01 B9 3D 4F 00 35");
        var parsed = InvokePrivate(engine, "ParseResponse", new System.Type[] { typeof(byte[]), typeof(bool), typeof(RegisterDef) },
            new object[] { rsp, false, regFlow }) as string;
        Check("SS ParseResponse 官方示例=11.905sccm", parsed != null && parsed.StartsWith("11.9"));

        // 手册 §3.3 负流量响应（数据 3E 3F, 校验和按协议重算 0x26）-> 负值
        byte[] rspNeg = HexUtil.ParseHexBytes("06 00 02 80 05 68 01 B9 3E 3F 00 26");
        var parsedNeg = InvokePrivate(engine, "ParseResponse", new System.Type[] { typeof(byte[]), typeof(bool), typeof(RegisterDef) },
            new object[] { rspNeg, false, regFlow }) as string;
        Check("SS ParseResponse 负流量=-0.586sccm", parsedNeg != null && parsedNeg.StartsWith("-0.5"));

        // NAK 单字节必须先判（手册 §2.2）
        var nak = InvokePrivate(engine, "ParseResponse", new System.Type[] { typeof(byte[]), typeof(bool), typeof(RegisterDef) },
            new object[] { new byte[] { 0x15 }, false, regFlow }) as string;
        Check("SS NAK 单字节=ERR:NAK", nak == "ERR:NAK");

        // 校验和错误必须被拒绝
        byte[] rspBad = HexUtil.ParseHexBytes("06 00 02 80 05 68 01 B9 3D 4F 00 00");
        var bad = InvokePrivate(engine, "ParseResponse", new System.Type[] { typeof(byte[]), typeof(bool), typeof(RegisterDef) },
            new object[] { rspBad, false, regFlow }) as string;
        Check("SS 错误校验和=ERR:BadChecksum", bad == "ERR:BadChecksum");

        // 读控制模式（uint8，手册 §4.1）数据区单字节
        var regCm = new RegisterDef { type = "uint8", unit = "" };
        byte[] rspCm = HexUtil.ParseHexBytes("06 00 02 80 04 69 01 03 02 00 F5");
        var cm = InvokePrivate(engine, "ParseResponse", new System.Type[] { typeof(byte[]), typeof(bool), typeof(RegisterDef) },
            new object[] { rspCm, false, regCm }) as string;
        Check("SS 控制模式响应=2", cm == "2");
        engine.Dispose();
    }

    private static void TestAlias()
    {
        // alias：纯 ASCII 别名经 COM A 串口命令引用设备，绕开中文设备名的编码问题
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ccm_alias_test");
        System.IO.Directory.CreateDirectory(dir);
        System.IO.File.Copy("七星华创_CS200A.json", System.IO.Path.Combine(dir, "七星华创_CS200A.json"), true);

        var repo = new ProfileRepository();
        repo.SetDirectory(dir);
        repo.LoadAll();

        DeviceProfile byAlias = repo.Get("cs200a");
        DeviceProfile byName = repo.Get("七星华创_CS200A");
        Check("Profile alias 命中（cs200a→七星华创_CS200A）",
            byAlias != null && byAlias.name == "七星华创_CS200A" && byAlias.alias == "CS200A" && byAlias.protocol == "sevenstar");
        Check("Profile 中文名精确命中", byName != null && byName.alias == "CS200A");
        Check("Profile 未知名返回 null", repo.Get("NO_SUCH_DEV") == null);
    }

    private static object InvokePrivate(object obj, string name, System.Type[] argTypes, object[] args)
    {
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
        var m = obj.GetType().GetMethod(name, flags, null, argTypes, null);
        if (m == null) return null;
        return m.Invoke(m.IsStatic ? null : obj, args);
    }
}
