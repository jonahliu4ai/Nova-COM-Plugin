// NovaCOMPlugin V4.5 调用逻辑示例（Nova 兼容写法）
// 用途：在 Nova 流程脚本（C#）中演示配置化设备的使用方式
//
// 推荐写法：new SmartDevice("配置文件名", "地址")
//   例：new SmartDevice("宇电_AI708", "1")
//
// 前置条件：
//   1. NovaCOMPluginV4.5.dll 已注册到 Nova（COM 可见）
//   2. devices/ 目录与 DLL 同级，包含对应的 .json 配置文件
//   3. 硬件已通过 RS-485 总线连接到电脑（如 COM3）

using System;
using NovaCOMPlugin;

public class NovaDemo
{
    // ================================================================
    // 示例 1：温控器（宇电 AI708）
    // ================================================================
    public static void Demo_TemperatureController()
    {
        Console.WriteLine("=== 示例 1：温控器 ===");

        // 1. 打开串口（所有设备共用同一个串口）
        string result = SerialPortManager.Open("COM7", "9600");
        if (result != "OK") { Console.WriteLine("串口打开失败: " + result); return; }

        // 2. 一步创建设备：new SmartDevice("配置名", "地址")
        SmartDevice heater = new SmartDevice("宇电_AI708", "1");

        // 3. 检查是否加载成功
        if (heater.ProfileName == null)
        { Console.WriteLine("配置加载失败，检查 devices/ 目录是否有 宇电_AI708.json"); return; }

        Console.WriteLine("已加载设备: " + heater.ProfileName + ", 协议: " + heater.Protocol);

        // 4. 设置温度（语义化命令，不需要记寄存器）
        result = heater.SendCommand("set_sv 25.0");
        Console.WriteLine("设定温度 25.0°C: " + result);  // OK

        // 5. 读取当前温度
        result = heater.SendCommand("read_pv");
        Console.WriteLine("当前温度: " + result);  // PV=24.5,SV=25.0,MV=45,ST=00

        // 6. 等待稳定（利用 V4.4 的稳定性状态机）
        Console.WriteLine("等待温度稳定...");
        result = DeviceHelper.WaitUntilStable(heater, 300);  // 最多等 300 秒
        Console.WriteLine("稳定结果: " + result);  // OK

        // 7. 连续控制下一个温度点
        result = heater.SendCommand("set_sv 30.0");
        Console.WriteLine("设定温度 30.0°C: " + result);

        result = DeviceHelper.WaitUntilStable(heater, 300);
        Console.WriteLine("稳定结果: " + result);  // OK

        // 8. 获取状态快照
        StateSnapshot snap = heater.GetState();
        Console.WriteLine(string.Format(
            "状态: {0}, PV={1}°C, SV={2}°C, Diff={3}, 耗时={4}s",
            snap.Code, snap.PV, snap.SV, snap.Diff, snap.Elapsed
        ));

        SerialPortManager.Close();
    }

    // ================================================================
    // 示例 2：继电器（Modbus / 固定帧）
    // ================================================================
    public static void Demo_Relay()
    {
        Console.WriteLine("\n=== 示例 2：继电器 ===");

        SerialPortManager.Open("COM7", "9600");

        // --- Modbus 继电器（华控 8 路）---
        SmartDevice relay = new SmartDevice("华控_8路继电器", "2");

        // 开第 3 路（引擎自动计算 coil addr = N-1 = 2）
        string result = relay.SendCommand("open_ch N=3");
        Console.WriteLine("开第3路: " + result);  // OK

        // 关第 3 路
        result = relay.SendCommand("close_ch N=3");
        Console.WriteLine("关第3路: " + result);  // OK

        // 读取所有 8 路状态
        result = relay.SendCommand("read_all");
        Console.WriteLine("全部状态: " + result);  // COILS=00100000

        // 关闭所有
        result = relay.SendCommand("close_all");
        Console.WriteLine("全部关闭: " + result);  // OK

        // --- 固定帧继电器（某厂 4 路）---
        SmartDevice relay2 = new SmartDevice("某厂_4路继电器", "0");

        // 固定字节命令
        result = relay2.SendCommand("open_ch2");
        Console.WriteLine("固定帧开第2路: " + result);

        // 模板命令（带参数）
        result = relay2.SendCommand("set_channel channel=4,state=1");
        Console.WriteLine("模板帧开第4路: " + result);

        SerialPortManager.Close();
    }

    // ================================================================
    // 示例 3：质量流量计（七星华创 CS200A）
    // ================================================================
    public static void Demo_FlowMeter()
    {
        Console.WriteLine("\n=== 示例 3：质量流量计 ===");

        SerialPortManager.Open("COM7", "9600");

        SmartDevice flow = new SmartDevice("七星华创_CS200A", "32");

        // 读取当前流量
        string result = flow.SendCommand("read_flow");
        Console.WriteLine("当前流量: " + result);  // VALUE=45.234sccm,RAW=17534,PCT=45.23

        // 设置目标流量
        result = flow.SendCommand("set_flow 50.0");
        Console.WriteLine("设定流量 50.0 sccm: " + result);  // OK

        // 读取当前生效的设定值
        result = flow.SendCommand("read_setpoint");
        Console.WriteLine("生效设定值: " + result);

        // 读取气体信息
        result = flow.SendCommand("read_gas_info");
        Console.WriteLine("气体信息: " + result);  // GAS=N2,FS=100

        SerialPortManager.Close();
    }

    // ================================================================
    // 示例 4：混合工作流（温控 + 阀门联动）
    // ================================================================
    public static void Demo_Workflow()
    {
        Console.WriteLine("\n=== 示例 4：温控+阀门联动工作流 ===");

        SerialPortManager.Open("COM7", "9600");

        SmartDevice heater = new SmartDevice("宇电_AI708", "1");
        SmartDevice valve  = new SmartDevice("华控_8路继电器", "2");

        // 步骤 1：开阀进样
        valve.SendCommand("open_ch N=1");
        Console.WriteLine("阀门1已打开（进样）");
        System.Threading.Thread.Sleep(2000);  // 等待 2 秒

        // 步骤 2：设定反应温度
        heater.SendCommand("set_sv 80.0");
        Console.WriteLine("设定反应温度 80.0°C，等待稳定...");

        string result = DeviceHelper.WaitUntilStable(heater, 300);
        if (result == "OK")
        {
            Console.WriteLine("温度已稳定，开始反应");
            // ... 这里可以插入 Nova 的电化学测量步骤 ...
            System.Threading.Thread.Sleep(10000);  // 模拟反应 10 秒
        }
        else
        {
            Console.WriteLine("温度稳定超时: " + result);
        }

        // 步骤 3：降温
        heater.SendCommand("set_sv 25.0");
        Console.WriteLine("降温至 25.0°C...");
        DeviceHelper.WaitUntilStable(heater, 300);

        // 步骤 4：关阀
        valve.SendCommand("close_ch N=1");
        Console.WriteLine("阀门1已关闭");

        SerialPortManager.Close();
    }

    // ================================================================
    // 示例 5：V4.4 兼容（旧代码无需修改）
    // ================================================================
    public static void Demo_BackwardCompatible()
    {
        Console.WriteLine("\n=== 示例 5：V4.4 兼容模式 ===");

        SerialPortManager.Open("COM7", "9600");

        // V4.4 的硬编码设备仍然可用，代码无需改动
        AIBUSDevice heater = new AIBUSDevice();
        heater.SetAddress("1");
        heater.SendCommand("SET 25.0");

        string result = DeviceHelper.WaitUntilStable(heater, 300);
        Console.WriteLine("V4.4 设备稳定结果: " + result);

        SerialPortManager.Close();
    }

    // ================================================================
    // 示例 6：新增设备后的零代码改动
    // ================================================================
    public static void Demo_NewDeviceWithoutCodeChange()
    {
        Console.WriteLine("\n=== 示例 6：新增设备（零代码改动）===");

        // 假设你刚刚在 devices/ 目录下新建了 台达_DTA4848.json
        // 不需要修改任何 C# 代码，不需要重新编译 DLL
        // 直接调用：

        SerialPortManager.Open("COM7", "9600");

        SmartDevice newHeater = new SmartDevice("台达_DTA4848", "1");
        if (newHeater.ProfileName != null)
        {
            newHeater.SendCommand("set_sv 100.0");
            string result = DeviceHelper.WaitUntilStable(newHeater, 300);
            Console.WriteLine("新设备运行结果: " + result);
        }
        else
        {
            Console.WriteLine("设备未找到，请检查 devices/台达_DTA4848.json 是否存在");
        }

        SerialPortManager.Close();
    }

    // ================================================================
    // 主入口
    // ================================================================
    public static void Main()
    {
        Demo_TemperatureController();
        Demo_Relay();
        Demo_FlowMeter();
        Demo_Workflow();
        Demo_BackwardCompatible();
        Demo_NewDeviceWithoutCodeChange();

        Console.WriteLine("\n=== 所有示例执行完毕 ===");
        Console.ReadKey();
    }
}
