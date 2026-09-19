# NovaCOMPlugin 使用与本地测试指南

> V4.5.1 起零外部依赖，单 DLL 即可运行。

---

## 一、部署到 NOVA（真实使用）

### 1. 安装内容

把以下两项放到同一目录（NOVA 2.1 插件目录）：

```
<NOVA插件目录>/
├── NovaCOMPluginV4.5.dll      ← 必需，单文件零依赖
└── devices/                   ← 必需，与 DLL 同级
    ├── 宇电_AI708.json
    ├── 欧姆龙_E5CC.json
    ├── 华控_8路继电器.json
    ├── 某厂_4路继电器.json
    ├── 七星华创_CS200A.json
    └── README.md
```

> ⚠️ `devices/` 目录必须与 DLL **同级**，插件按 `DLL所在目录\devices\` 查找配置。

### 2. Nova 脚本中的调用

```csharp
using NovaCOMPlugin;

// 1. 打开串口（所有设备共用一个串口，RS-485 总线）
SerialPortManager.Open("COM3", "9600");

// 2. 一步创建设备（配置名 = devices/ 下的 JSON 文件名，不带后缀）
SmartDevice heater = new SmartDevice("宇电_AI708", "1");
if (heater.ProfileName == null) {
    Console.WriteLine("配置加载失败，检查 devices/ 目录");
    return;
}

// 3. 语义化命令（命令名 = JSON 中 commands 的 key）
heater.SendCommand("set_sv 80.0");           // 设定温度
heater.SendCommand("read_pv");               // 读当前温度
DeviceHelper.WaitUntilStable(heater, 300);   // 等稳定（V4.4 状态机，自动重置）

// 4. 总线上加第二台设备（不同地址）
SmartDevice relay = new SmartDevice("华控_8路继电器", "2");
relay.SendCommand("open_ch N=3");
relay.SendCommand("close_all");

// 5. 结束
SerialPortManager.Close();
```

### 3. 新增设备（零代码改动）

1. 在 `devices/` 下新建 `厂商_型号.json`（格式参考同目录现有文件）
2. **不需要重新编译 DLL**
3. Nova 脚本直接 `new SmartDevice("厂商_型号", "地址")`

---

## 二、本地测试（无真实硬件）

### 环境：COM0COM 虚拟串口对

已在本机创建虚拟串口对 **COM7 ↔ COM8**（一次配置永久有效）。

验证/重建（管理员 cmd）：

```batch
"C:\Program Files (x86)\com0com\setupc.exe" list
"C:\Program Files (x86)\com0com\setupc.exe" install PortName=COM7 PortName=COM8
```

### 测试 1：JSON 配置加载（不需要串口）

```batch
cd D:\src\nova-com-plugin
TestJsonLoader.exe
```
期望：5 个配置全部 `[PASS]`（16 PASS / 0 FAIL）。

### 测试 2：完整链路联调（COM0COM + MockDevice）

需要两个终端窗口：

**窗口 1 — 虚拟从机（MockDevice）**：
```batch
cd D:\src\nova-com-plugin
MockDevice.exe COM8
```
保持运行（Ctrl+C 退出）。支持 AIBUS / Modbus-RTU / FixedFrame 三种协议的伪响应。

**窗口 2 — 主站测试程序**：
```batch
cd D:\src\nova-com-plugin
TestIntegration.exe
```
期望：16 PASS / 1 FAIL。唯一的 FAIL（SV=6400.0 vs 25.0）是 MockDevice 的模拟字节序显示问题，**真实 AI-518 无此问题**（V4.3 实测验证过 PV/10 正确），且 V4.4 兼容路径返回相同值，行为一致。

### 测试 3：SevenStar 引擎联调（COM0COM，无需真实 MFC）

```batch
cd D:\src\nova-com-plugin
TestSevenStar.exe
```
模拟从机按官方协议示例（0x4F3D=11.9%FS 等）应答，验证组帧/校验/DataLen 解析。期望 6 PASS / 0 FAIL。

### 测试 4：状态机/连续控制（纯逻辑，Mock 设备）

```batch
TestContinuousV2.exe
```
验证 V4.4 修复：SET 后状态机重置为 HEATING、连续 25→30→35°C 控制。（14 PASS / 1 FAIL，FAIL 为断言写法小问题）

---

## 三、常用命令速查

| 任务 | 命令 |
|------|------|
| 编译 DLL | `csc /target:library /out:NovaCOMPluginV4.5.dll NovaCOMPluginV4.5.cs` |
| 本地打包 | `create-plugin-release.sh` |
| 打包+上传 | `create-plugin-release.sh --upload`（需 `GH_TOKEN`） |
| 跑全部 JSON 测试 | `TestJsonLoader.exe` |
| SevenStar 引擎联调 | `TestSevenStar.exe`（COM0COM COM7↔COM8） |
| 串口联调 | `MockDevice.exe COM8` + `TestIntegration.exe` |

---

## 四、已知限制

1. **SevenStar 协议**：MockDevice 不支持，需真实 CS200A 验证；但可用 COM0COM 虚拟串口对跑 `TestSevenStar.exe`（模拟从机按官方协议示例应答，6 PASS / 0 FAIL）。
2. **FixedFrame 无响应设备**：返回 `OK|NoRsp`，调用方需知晓。

---

*最后更新：2026-09-08（V4.5.2，SevenStar 引擎修复：DataLen 索引、写帧长度、数据区偏移、uint8/string/scale 支持、read_multi 实现）*
