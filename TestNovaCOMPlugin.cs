// TestNovaCOMPlugin.cs
// 测试 NovaCOMPlugin.dll 的脚本
// 支持命令行参数传入 COM 口和波特率

using System;
using NovaCOMPlugin;

namespace TestNovaCOMPlugin
{
    class Program
    {
        static void Main(string[] args)
        {
            // // 默认参数
            // string port = "COM7";
            // string baud = "9600";

            // // 从命令行读取参数
            // if (args.Length >= 1)
            //     port = args[0];
            // if (args.Length >= 2)
            //     baud = args[1];
            // 替换 args 解析部分
            Console.Write("请输入串口号 (默认 COM7): ");
            string port = Console.ReadLine().Trim();
            if (string.IsNullOrEmpty(port)) port = "COM7";

            Console.Write("请输入波特率 (默认 9600): ");
            string baud = Console.ReadLine().Trim();
            if (string.IsNullOrEmpty(baud)) baud = "9600";

            Console.WriteLine("=== NovaCOMPlugin 测试 ===");
            Console.WriteLine("串口: " + port + ", 波特率: " + baud + "\n");

            // 1. 测试 SerialPortManager.Open
            Console.WriteLine("1. 测试打开串口");
            string result = SerialPortManager.Open(port, baud);
            Console.WriteLine("   结果: " + result);

            if (!SerialPortManager.IsOpen)
            {
                Console.WriteLine("   串口打开失败，终止测试");
                Console.WriteLine("\n=== 测试终止 ===");
                Console.ReadKey();
                return;
            }

            // 2. 测试 SerialPortManager.SendCommand (ASCII)
            Console.WriteLine("\n2. 测试发送 ASCII 命令");
            result = SerialPortManager.SendCommand("READ");
            Console.WriteLine("   结果: " + result);

            // 3. 测试 SerialPortManager.SendCommand (HEX 转义)
            Console.WriteLine("\n3. 测试发送 HEX 转义命令");
            result = SerialPortManager.SendCommand("\\x81\\x81\\x52\\x00\\x00\\x00\\x53\\x00");
            Console.WriteLine("   结果: " + result);

            // 4. 测试 SerialPortManager.SendCommand (纯 HEX)
            Console.WriteLine("\n4. 测试发送纯 HEX 命令");
            result = SerialPortManager.SendCommand("81 81 52 00 00 00 53 00");
            Console.WriteLine("   结果: " + result);

            // 5. 测试 AIBUSDevice
            Console.WriteLine("\n5. 测试 AIBUSDevice");
            AIBUSDevice aibus = new AIBUSDevice();
            Console.WriteLine("   设置地址: " + aibus.SetAddress("1"));
            Console.WriteLine("   发送 READ: " + aibus.SendCommand("READ"));
            Console.WriteLine("   发送 SET 30.0: " + aibus.SendCommand("SET 30.0"));

            // 6. 测试 ModbusDevice
            Console.WriteLine("\n6. 测试 ModbusDevice");
            ModbusDevice modbus = new ModbusDevice();
            Console.WriteLine("   设置地址: " + modbus.SetAddress("2"));
            Console.WriteLine("   发送 READ 0 1: " + modbus.SendCommand("READ 0 1"));

            // 7. 关闭串口
            Console.WriteLine("\n7. 关闭串口");
            result = SerialPortManager.Close();
            Console.WriteLine("   结果: " + result);

            Console.WriteLine("\n=== 测试完成 ===");
            Console.ReadKey();
        }
    }
}