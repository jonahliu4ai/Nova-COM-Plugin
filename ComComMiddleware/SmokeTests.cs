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
        {
            Check("Engine factory", m != null && f != null && c != null && ProtocolEngineFactory.Create("none") == null);
        }
    }
}

