using System;
using System.IO.Ports;
using System.Threading;
using ComComMiddleware;

// SevenStar 引擎端到端联调：COM0COM 虚拟串口对
// 模拟从机按手册行为应答：ACK 立即返回，响应帧延迟 100ms（协议 §3.5.2），
// 并在响应前注入 2 个残留垃圾字节，验证鲁棒接收的 ACK 扫描能力。
public class TestSevenStarE2E
{
    private static int _pass, _fail;

    private static void Check(string name, bool ok)
    {
        Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + name);
        if (ok) _pass++; else _fail++;
    }

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string masterPort = args.Length > 0 ? args[0] : "COM7";
        string slavePort  = args.Length > 1 ? args[1] : "COM8";

        // ---- 模拟从机 ----
        var slave = new SerialPort(slavePort, 19200, Parity.None, 8, StopBits.One) { ReadTimeout = 5000 };
        slave.Open();
        Console.WriteLine("  [dbg] slave opened on " + slavePort);
        var t = new Thread(() =>
        {
            byte[] buf = new byte[64];
            try
            {
                while (true)
                {
                    DateTime t0 = DateTime.Now;
                    while (slave.BytesToRead < 9 && (DateTime.Now - t0).TotalSeconds < 3) Thread.Sleep(10);
                    int got = 0;
                    while (got < 9) got += slave.Read(buf, got, 9 - got);
                    Console.WriteLine("  [dbg] slave got frame: " + BitConverter.ToString(buf, 0, got).Replace("-", " "));
                    int dataLen = buf[3];
                    int total = 6 + dataLen;
                    while (got < total) got += slave.Read(buf, got, total - got);

                    byte service = buf[2], cls = buf[4], attr = buf[6];
                    // 1) 立即回 ACK（设备 2~4 字符时间内应答）
                    slave.Write(new byte[] { 0x06 }, 0, 1);
                    // 2) 延迟 100ms 模拟设备处理时间
                    Thread.Sleep(100);
                    // 3) 故意先丢 2 个残留垃圾字节，再发响应帧
                    slave.Write(new byte[] { 0x42, 0x2C }, 0, 2);

                    byte[] resp;
                    if (service == 0x80 && cls == 0x68 && attr == 0xB9)
                        resp = new byte[] { 0x00, 0x02, 0x80, 0x05, 0x68, 0x01, 0xB9, 0x3D, 0x4F, 0x00, 0x35 };
                    else if (service == 0x80 && cls == 0x69 && attr == 0xA5)
                        resp = new byte[] { 0x00, 0x02, 0x80, 0x05, 0x69, 0x01, 0xA5, 0xCC, 0x4C, 0x00, 0x00 };
                    else if (service == 0x81 && cls == 0x69 && attr == 0xA4)
                        resp = new byte[] { 0x00, 0x02, 0x81, 0x05, 0x69, 0x01, 0xA4, buf[7], buf[8], 0x00, 0x00 };
                    else if (service == 0x81 && cls == 0x69 && attr == 0x06)
                        resp = new byte[] { 0x00, 0x02, 0x81, 0x04, 0x69, 0x01, 0x06, 0x01, 0x00, 0x00 };
                    else if (service == 0x81 && cls == 0x69 && attr == 0x03)
                        resp = new byte[] { 0x00, 0x02, 0x81, 0x04, 0x69, 0x01, 0x03, 0x01, 0x00, 0x00 };
                    else
                        resp = new byte[] { 0x15 };
                    resp[resp.Length - 1] = 0;
                    for (int i = 0; i < resp.Length - 1; i++) resp[resp.Length - 1] += resp[i];
                    slave.Write(resp, 0, resp.Length);
                }
            }
            catch (Exception ex) { Console.WriteLine("  [dbg] slave thread died: " + ex.GetType().Name + ": " + ex.Message); }
        });
        t.IsBackground = true;
        t.Start();
        Thread.Sleep(300);

        // ---- 主站：中间件引擎（加载真实 JSON 配置） ----
        Console.WriteLine("=== SevenStar E2E（100ms 延迟 + 残留字节 + ACK 扫描）===");
        var port = new SerialPort(masterPort, 19200, Parity.None, 8, StopBits.One) { ReadTimeout = 1000 };
        port.Open();
        Console.WriteLine("  [dbg] master opened " + port.PortName + " IsOpen=" + port.IsOpen);
        var bus = new SerialBus(port, new object());

        object root = JsonLite.Parse(System.IO.File.ReadAllText("七星华创_CS200A.json"));
        DeviceProfile p = JsonLite.MapProfile(JsonLite.AsObj(root));
        var engine = ProtocolEngineFactory.Create("sevenstar");
        engine.Init(p, 66, bus);

        // 手册 §5.2：读瞬时流量（残留字节 + 延迟响应）
        // 直接测 Bus 层：手册读流量帧（地址 0x42）
        byte[] txFrame = new byte[] { 0x42, 0x02, 0x80, 0x03, 0x68, 0x01, 0xB9, 0x00, 0xE9 };
        byte[] rawRsp;
        try {
            rawRsp = bus.SendAndReceiveSevenStar(txFrame, 1500);
            Console.WriteLine("  [dbg] bus raw rsp (" + (rawRsp == null ? -1 : rawRsp.Length) + " bytes): " +
                (rawRsp == null ? "null" : BitConverter.ToString(rawRsp).Replace("-", " ")));
        } catch (Exception ex) {
            Console.WriteLine("  [dbg] bus EX: " + ex.GetType().Name + ": " + ex.Message);
            rawRsp = null;
        }
        string r;
        try { r = engine.Execute("read_flow", ""); }
        catch (Exception ex) { r = "EX:" + ex.GetType().Name + ":" + ex.Message; }
        Console.WriteLine("  read_flow -> " + r);
        Check("read_flow=11.905sccm（穿透残留字节与100ms延迟）", r != null && r.StartsWith("11.9"));

        // 手册 §4.3：写 10 sccm
        r = engine.Execute("set_flow", "10");
        Console.WriteLine("  set_flow 10 -> " + r);
        Check("set_flow 10=OK", r == "OK");

        // 读设定值回读（回显帧；ufrac16 定点量化 10%→9.997%，允许 ±0.05 容差）
        r = engine.Execute("read_setpoint", "");
        Console.WriteLine("  read_setpoint -> " + r);
        double sp;
        bool spOk = r != null && r.EndsWith("sccm") && double.TryParse(r.Substring(0, r.Length - 4),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sp) && Math.Abs(sp - 10.0) < 0.05;
        Check("回读设定≈10 sccm（ufrac16 量化容差内）", spOk);

        // NAK 路径（未定义命令走通用分支会返回 NAK）
        port.Close();
        slave.Close();

        Console.WriteLine("==============================");
        Console.WriteLine("PASS=" + _pass + " FAIL=" + _fail);
        return _fail;
    }
}
