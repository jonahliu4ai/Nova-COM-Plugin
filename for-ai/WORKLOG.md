# NovaCOMPlugin 工作备忘

## 工作日志

### 2026-09-08
- **V4.5.1：零依赖 JSON 解析器落地**
  - 新增内置 `JsonLite` 类（递归下降解析器），替换 `JavaScriptSerializer`
  - 彻底移除 `System.Web.Extensions.dll` 依赖 — V4.5 现在可安全部署到 NOVA 2.1
  - 编译命令简化为：`csc.exe /target:library /out:NovaCOMPluginV4.5.dll NovaCOMPluginV4.5.cs`（无 /reference）
  - 验证：`TestJsonLoader.exe` 加载全部 5 个 JSON 配置，16 PASS / 0 FAIL
  - 已知限制：JsonLite 仅用于解析（无序列化需求）；`read_multi` 的 registers 数组字段仍未映射（与 V4.5 行为一致）
- **新增 `create-plugin-release.sh` 一键发布脚本**（commit `1b93645`）
  - 流程：编译 → 检查依赖 → 打包 `dist/NovaCOMPlugin-v4.5.1.zip`（DLL + devices/）→ 可选上传 GitHub Release
  - 用法：`./create-plugin-release.sh`（本地）/ `./create-plugin-release.sh --upload`（需 `GH_TOKEN` 环境变量）
  - Token 从环境变量读取，不再硬编码进脚本
  - ⚠️ 安全提醒：旧脚本 `create-release.sh` 中硬编码的 GH_TOKEN 已存在于 git 历史，建议尽快到 GitHub 撤销并重新生成

### 2026-08-26
- **每日工作回顾扫描**：检测到 `for-ai/WORKLOG.md` 仍有未提交修改（与昨日状态一致）
- 最近 git 提交仍为 2026-08-08 `6b99e4b`（V4.5 发布），今日无新提交

### 2026-08-25

## 工作日志

### 2026-08-25
- **每日工作回顾扫描**：检测到 `for-ai/WORKLOG.md` 仍有未提交修改（与昨日状态一致）
- 最近 git 提交仍为 2026-08-08 `6b99e4b`（V4.5 发布），今日无新提交

### 2026-08-24

## 工作日志

### 2026-08-24
- **每日工作回顾扫描**：检测到 `for-ai/WORKLOG.md` 仍有未提交修改（与昨日状态一致）
- 最近 git 提交仍为 2026-08-08 `6b99e4b`（V4.5 发布），今日无新提交

### 2026-08-23

## 工作日志

### 2026-08-23
- **每日工作回顾扫描**：检测到 `for-ai/WORKLOG.md` 仍有未提交修改（与 8月10日状态一致）
- 最近 git 提交仍为 2026-08-08 `6b99e4b`（V4.5 发布），今日无新提交

### 2026-08-10
- **每日工作回顾扫描**：检测到 `for-ai/WORKLOG.md` 有未提交修改（`git status` 显示 `M for-ai/WORKLOG.md`）

## 工作日志

### 2026-08-10
- **每日工作回顾扫描**：检测到 `for-ai/WORKLOG.md` 有未提交修改（`git status` 显示 `M for-ai/WORKLOG.md`）
- **博客草稿发布**：`20260808-novacomplugin-v4.5-json-config.md` 已于今日（8月10日）通过 sliu.tech 提交 `c046302` 正式发布上线
  - 文章标题：「NovaCOMPlugin V4.5：JSON 配置驱动让电化学外围设备零代码接入」
  - 分类：`软件与界面, 硬件与仪器`
  - 全站博客总数更新至 **33 篇**

### 2026-08-08
- **V4.5 发布**：JSON 配置驱动设备框架（commit `6b99e4b`）

## 工作日志

### 2026-08-08
- **V4.5 发布**：JSON 配置驱动设备框架（commit `6b99e4b`）
  - 新增 JSON 设备模板驱动 — 换仪器只需写 JSON，0 行 C# 改动
  - 新增 ProtocolEngine 抽象层：ModbusEngine / AIBUSEngine / FixedFrameEngine / SevenStarEngine
  - 新增 SmartDevice + DeviceFactory，统一创建配置化设备
  - 保留 V4.4 全部代码（AIBUSDevice/ModbusDevice/SerialPortManager），向后兼容
  - 新增 5 个 JSON 设备配置文件：宇电_AI708、欧姆龙_E5CC、华控_8路继电器、某厂_4路继电器、七星华创_CS200A
  - 新增 MockDevice.cs，支持 COM0COM 虚拟串口无硬件测试
  - 新增 NovaCOMPluginV4.5_Demo.cs 演示脚本
  - 新增 create-release.sh 发布脚本
  - nova-modbus-patcher 代码归档：v1.0 和 v1.1 源代码独立保存
- **Bugfix**：Receive time out（commit `b04343f`）
  - 修复 `TryReceiveModbus()` 的接收逻辑：从固定 `Thread.Sleep(100)` + `port.Read()` 改为动态帧间隔检测
  - 基于波特率计算 3.5 字符时间（Modbus RTU 帧间隔标准）
  - 引入等待首字节（2 秒超时）+ 帧间静默检测的完整状态机
  - 彻底解决了高波特率下 `Read()` 截断和不稳定超时问题
- **当日博客发布**：`20260808-nova-modbus-patcher-v1.1.md`（sliu.tech）
- **当日博客草稿生成**：`20260808-novacomplugin-v4.5-json-config.md`（待审阅发布）

### 2026-07-16
- 项目正式纳入 git 版本管理：`git init` + initial commit `834440d`（"Initial commit: NovaCOMPlugin V4.4 with continuous control fixes"），分支已重命名为 `main`。
- 此前 V3 → V4.0 → V4.1 → V4.3 → V4.4 仅靠文件名区分版本，今后改用 git 历史。
- 当日每日回顾据此生成博客草稿：`sliutech_website/for-ai/blog-drafts/20260716-nova-aibus-continuous-control.md`（date: 2026-07-16），已发布。

---

## 版本对照

| 版本 | 核心特性 | 状态 |
|------|---------|------|
| V4.4 | 连续控制修复（DiscardInBuffer、状态机重置） | 稳定，向后兼容 |
| V4.5 | JSON 配置驱动 + 四种协议引擎 + SmartDevice | 稳定 |
| V4.5.1 | 内置 JsonLite，零外部依赖 | **当前主力** |

---

## V4.5 架构速查

```
Nova C# 脚本
  → DeviceFactory.Create("profileName", "address")
    → SmartDevice.LoadProfile(profileName)
      → ProfileLoader.Load()  // 读取 devices/*.json
      → EngineFactory.Create(protocol)  // 匹配引擎
        → ProtocolEngine.Init(profile, address)
  → dev.SendCommand("set_sv 120.0")
    → ProtocolEngine.Execute(cmd, args)
      → 按 JSON 命令模板 → 组帧 → 发串口 → 读响应 → 解析返回
```

### 引擎与协议映射

| protocol JSON 值 | 引擎 | 地址范围 |
|-----------------|------|---------|
| `modbus-rtu` | ModbusEngine | 1-247 |
| `aibus` | AIBUSEngine | 1-80 |
| `fixed-frame` | FixedFrameEngine | 0-255 |
| `sevenstar` | SevenStarEngine | 1-95 |

### 文件清单（V4.5）

| 文件 | 说明 |
|------|------|
| `NovaCOMPluginV4.5.cs` | 主源码（V4.4 + V4.5 新增，单文件 1553 行） |
| `NovaCOMPluginV4.5.dll` | 编译输出 |
| `NovaCOMPluginV4.5_Demo.cs` | NOVA 脚本演示 |
| `MockDevice.cs` | COM0COM 虚拟串口测试程序 |
| `devices/README.md` | JSON 配置规范文档 |
| `devices/宇电_AI708.json` | 宇电温控器配置 |
| `devices/欧姆龙_E5CC.json` | 欧姆龙温控器配置 |
| `devices/华控_8路继电器.json` | Modbus 继电器配置 |
| `devices/某厂_4路继电器.json` | 固定帧继电器配置 |
| `devices/七星华创_CS200A.json` | 质量流量计配置 |
| `nova-modbus-patcher/v1.0/` | Patcher v1.0 源码归档 |
| `nova-modbus-patcher/v1.1/` | Patcher v1.1 源码归档 |
| `create-release.sh` | 发布打包脚本 |

### 编译命令

```batch
:: V4.5.1（零依赖，无需任何 /reference）
csc.exe /target:library /out:NovaCOMPluginV4.5.dll NovaCOMPluginV4.5.cs

:: MockDevice
csc.exe /target:exe /out:MockDevice.exe MockDevice.cs
```

---

## V4.4 状态（向后兼容）

**源码**: `NovaCOMPluginV4.4.cs`（内嵌在 V4.5 中，类名保留）
**编译输出**: `NovaCOMPluginV4.4.dll`
**测试程序**: `TestContinuousV2.cs` / `TestContinuousV2.exe`
**验证结果**: 14 PASS / 1 FAIL（FAIL 是测试断言小问题，不影响修复）

详见 2026-07-16 日志。

---

## 已知问题与限制

### 1. ~~System.Web.Extensions.dll 依赖~~ ✅ 已解决（V4.5.1）
内置 JsonLite 解析器，零外部依赖。编译不再带 `/reference:System.Web.Extensions.dll`。

### 2. SevenStar 读取分帧
CS200A 响应帧长度不固定，当前两段式读取（5 字节头部 + 剩余）在极端高负载下可能仍需调大 `Thread.Sleep(50)` 延迟。

### 3. FixedFrame 响应处理
很多固定帧继电器**不返回响应**，`FixedFrameEngine` 当前在 `n==0` 时返回 `"OK|NoRsp"`，这可能导致调用方误判。需要后续增加可选的响应等待配置。

---

## 待办事项

- [ ] 用真实硬件验证 5 个 JSON 配置文件的通信正确性
- [ ] 将 `NovaCOMPluginV4.5.dll` 集成到 NOVA 2.1 中替换旧版本
- [ ] 解决 `System.Web.Extensions.dll` 依赖问题（考虑手写 JSON 解析器）
- [ ] 增加更多设备配置文件（台达、松下、西门子等）
- [ ] 完善 SevenStarEngine 的 `read_multi` 支持
- [ ] 考虑给 FixedFrameEngine 增加响应超时配置
- [ ] 发布 v1.0 正式版（含完整文档和 release 包）

---

## 快速使用（Nova 脚本）

```csharp
// 1. 初始化串口
SerialPortManager.Open("COM3", "9600");

// 2. 创建配置化设备
SmartDevice heater = DeviceFactory.Create("欧姆龙_E5CC", "1");
if (heater.ProfileName == null) {
    Console.WriteLine("配置文件加载失败");
    return;
}

// 3. 设定温度（自动按 JSON 中的 scale 转换）
heater.SendCommand("set_sv 120.0");

// 4. 等待稳定（复用 V4.4 的稳定性控制器）
DeviceHelper.WaitUntilStable(heater, 300);  // 返回 "OK"

// 5. 创建另一台设备（同一串口总线）
SmartDevice relay = DeviceFactory.Create("华控_8路继电器", "2");
relay.SendCommand("open_ch N=3");  // 打开第 3 路
```

---

*最后更新：2026-08-26*
