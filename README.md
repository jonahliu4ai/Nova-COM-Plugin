# Nova COM Plugin v2.0 Final (继承版)

Nova 软件 .NET 插件，支持 AIBUS/Modbus 协议转换。

## 特性

- **智能命令识别**：ASCII/HEX 自动判断
  - 含 `\xHH` 转义 → 解析为 HEX
  - 纯数字和空格（如 `"81 52 00"`）→ 解析为 HEX
  - 其他 → 作为 ASCII 发送
- **单串口多设备共享**：静态全局串口管理器
- **抽象基类设计**：便于扩展新协议

## 编译

```powershell
csc /target:library /out:NovaCOMPlugin.dll NovaCOMPlugin.cs
```

## Nova 配置速查表

| 功能 | Command name | Action | Class | Method | Format |
|------|-------------|--------|-------|--------|--------|
| 打开串口 | `Open Serial` | Call static | `NovaCOMPlugin.SerialPortManager` | `Open` | `COM5,9600` |
| 关闭串口 | `Close Serial` | Call static | `NovaCOMPlugin.SerialPortManager` | `Close` | |
| 智能发送 | `Send Command` | Call static | `NovaCOMPlugin.SerialPortManager` | `SendCommand` | `READ` 或 `\x81\x81\x52\x00\x00\x00\x53\x00` 或 `81 81 52 00 00 00 53 00` |
| 测试回声 | `Test Echo` | Call static | `NovaCOMPlugin.SerialPortManager` | `TestEcho` | `Hello` |
| 创建 AIBUS | `Create AIBUS` | Create object | `NovaCOMPlugin.dll` | `NovaCOMPlugin.AIBUSDevice` | |
| AIBUS 设地址 | `AIBUS Set Addr` | Call method | | `SetAddress` | `1` |
| AIBUS 读取 | `AIBUS Read` | Call method | | `SendCommand` | `READ` |
| AIBUS 设置 | `AIBUS Set SV` | Call method | | `SendCommand` | `SET 30.0` |
| 创建 Modbus | `Create Modbus` | Create object | `NovaCOMPlugin.dll` | `NovaCOMPlugin.ModbusDevice` | |
| Modbus 设地址 | `Modbus Set Addr` | Call method | | `SetAddress` | `2` |
| Modbus 读取 | `Modbus Read` | Call method | | `SendCommand` | `READ 0 1` |
| Modbus 写入 | `Modbus Write` | Call method | | `SendCommand` | `WRITE 0 100` |

## 扩展新设备示例

```csharp
public class NewProtocolDevice : DeviceBase
{
    protected override bool IsValidAddress(int addr)
    {
        return addr >= 1 && addr <= 100; // 你的地址范围
    }

    protected override string GetAddressRange()
    {
        return "1-100";
    }

    protected override string ExecuteCommand(string command)
    {
        // 实现你的协议逻辑
        if (command == "READ")
            return ReadData();
        else
            return "ERROR: Unknown command";
    }

    private string ReadData()
    {
        // 发送帧、读取响应、解析
        return "OK: Data=123";
    }
}
```

## 文件

- `NovaCOMPlugin.cs` — 主代码
- `README.md` — 本文档
