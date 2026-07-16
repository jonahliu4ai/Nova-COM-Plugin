# NovaCOMPlugin 测试指南

## 文件说明

| 文件                    | 说明                |
| ----------------------- | ------------------- |
| `NovaCOMPlugin.dll`     | 主插件（Nova 调用） |
| `TestNovaCOMPlugin.exe` | 独立测试程序        |
| `simCOMAutoReply.py`    | Python 串口模拟设备 |

---

## 环境准备

### 1. 虚拟串口（无真实设备时）

安装 **com0com**：
- 下载：https://com0com.sourceforge.net/
- 创建虚拟串口对：`COM5 <-> COM6`

### 2. Python 模拟设备（可选）

```bash
pip install pyserial
python simCOMAutoReply.py
```

---

## 测试方法

### 方法一：独立测试程序（推荐）

```powershell
# 编译
csc /reference:NovaCOMPlugin.dll /out:TestNovaCOMPlugin.exe TestNovaCOMPlugin.cs

# 默认参数
.\TestNovaCOMPlugin.exe

# 指定 COM 口
.\TestNovaCOMPlugin.exe COM3

# 指定 COM 口和波特率
.\TestNovaCOMPlugin.exe COM3 19200
```

### 方法二：Nova 软件内测试

| 步骤 | Command name   | Action        | Class                             | Method                      | Format                             |
| ---- | -------------- | ------------- | --------------------------------- | --------------------------- | ---------------------------------- |
| 1    | `Open Serial`  | Call static   | `NovaCOMPlugin.SerialPortManager` | `Open`                      | `COM5,9600`                        |
| 2    | `Send ASCII`   | Call static   | `NovaCOMPlugin.SerialPortManager` | `SendCommand`               | `READ`                             |
| 3    | `Send HEX`     | Call static   | `NovaCOMPlugin.SerialPortManager` | `SendCommand`               | `\x81\x81\x52\x00\x00\x00\x53\x00` |
| 4    | `Create AIBUS` | Create object | `NovaCOMPlugin.dll`               | `NovaCOMPlugin.AIBUSDevice` |                                    |
| 5    | `AIBUS Read`   | Call method   |                                   | `SendCommand`               | `READ`                             |
| 6    | `Close Serial` | Call static   | `NovaCOMPlugin.SerialPortManager` | `Close`                     |                                    |

---

## 命令格式说明

| 输入类型   | 示例                               | 识别方式                     |
| ---------- | ---------------------------------- | ---------------------------- |
| ASCII 文本 | `READ`                             | 含字母或不可打印字符         |
| HEX 转义   | `\x81\x81\x52\x00\x00\x00\x53\x00` | 含 `\x` 前缀                 |
| 纯 HEX     | `81 81 52 00 00 00 53 00`          | 纯数字/A-F，偶数长度，≥4字符 |

---

## 常见问题

| 问题                          | 解决                                   |
| ----------------------------- | -------------------------------------- |
| `ERROR: Serial port not open` | 先执行 `Open Serial`                   |
| `ERROR: No response`          | 检查设备是否连接，或运行 Python 模拟器 |
| Nova 不显示返回值             | 确认方法为 `public`，类为非 static     |
| DLL 加载失败                  | 确认 .NET Framework 版本匹配           |

---

## 扩展新设备

继承 `DeviceBase`，实现 3 个抽象方法：

```csharp
public class NewDevice : DeviceBase
{
    protected override bool IsValidAddress(int addr) { return addr >= 1 && addr <= 100; }
    protected override string GetAddressRange() { return "1-100"; }
    protected override string ExecuteCommand(string command) { /* 协议实现 */ }
}
```

---

## 版本历史

| 版本 | 日期       | 说明                        |
| ---- | ---------- | --------------------------- |
| 1.0  | 2026-07-02 | 初始版本，支持 AIBUS/Modbus |
| 2.0  | 2026-07-02 | 继承重构，添加智能 HEX 识别 |

