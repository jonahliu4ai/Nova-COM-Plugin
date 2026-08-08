using System;
using System.IO.Ports;
using System.Reflection;
using EcoChemie.Communication.External;

class TestPatch
{
    static void Main()
    {
        Console.WriteLine("=== NOVA ExternalRS232 Patch 测试 ===");
        Console.WriteLine("目标串口: COM7");
        Console.WriteLine("请确保 com0com 已连接 COM7 <-> COM8");
        Console.WriteLine();

        try
        {
            // 1. 创建 SerialPort 并打开 COM7
            var port = new SerialPort("COM7", 9600, Parity.None, 8, StopBits.One);
            port.Open();
            Console.WriteLine("[OK] COM7 已打开");

            // 2. 创建 ExternalRS232 实例
            var device = new ExternalRS232();

            // 3. 通过反射设置内部 _serialport（跳过 Initialize）
            var field = typeof(ExternalRS232).GetField(
                "_serialport", BindingFlags.NonPublic | BindingFlags.Instance);
            field.SetValue(device, port);
            Console.WriteLine("[OK] ExternalRS232 已绑定到 COM7");

            // 4. 测试 1: Modbus RTU HEX
            Console.WriteLine("\n--- 测试 1: Modbus RTU HEX ---");
            string hexCmd = "01 03 00 00 00 0A";
            Console.WriteLine("发送: " + hexCmd);
            int sent = device.Send(hexCmd);
            Console.WriteLine("返回值: " + sent + " bytes");

            System.Threading.Thread.Sleep(500);

            // 5. 测试 2: ASCII 命令
            Console.WriteLine("\n--- 测试 2: ASCII 命令 ---");
            string asciiCmd = "SET 123";
            Console.WriteLine("发送: " + asciiCmd);
            sent = device.Send(asciiCmd);
            Console.WriteLine("返回值: " + sent + " bytes");

            System.Threading.Thread.Sleep(500);

            // 6. 测试 3: 另一个 HEX
            Console.WriteLine("\n--- 测试 3: HEX 00 0A 00 ---");
            hexCmd = "00 0A 00";
            Console.WriteLine("发送: " + hexCmd);
            sent = device.Send(hexCmd);
            Console.WriteLine("返回值: " + sent + " bytes");

            System.Threading.Thread.Sleep(500);

            // 7. 测试 4: ASCII *idn?
            Console.WriteLine("\n--- 测试 4: ASCII *idn? ---");
            asciiCmd = "*idn?";
            Console.WriteLine("发送: " + asciiCmd);
            sent = device.Send(asciiCmd);
            Console.WriteLine("返回值: " + sent + " bytes");

            Console.WriteLine("\n=== 测试完成，请检查 COM8 接收 ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[错误] " + ex.GetType().Name + ": " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }
}
