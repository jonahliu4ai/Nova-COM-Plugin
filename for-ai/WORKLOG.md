# NovaCOMPlugin V4.4 工作备忘

## 工作日志

### 2026-07-16
- 项目正式纳入 git 版本管理：`git init` + initial commit `834440d`（"Initial commit: NovaCOMPlugin V4.4 with continuous control fixes"），分支已重命名为 `main`。
- 此前 V3 → V4.0 → V4.1 → V4.3 → V4.4 仅靠文件名区分版本，今后改用 git 历史。
- 当日每日回顾据此生成博客草稿：`sliutech_website/for-ai/blog-drafts/20260716-nova-aibus-continuous-control.md`（date: 2026-07-16），待用户审阅后发布。

## 当前状态
- **源码**: `NovaCOMPluginV4.4.cs`
- **编译输出**: `NovaCOMPluginV4.4.dll`
- **测试程序**: `TestContinuousV2.cs` / `TestContinuousV2.exe`
- **验证结果**: 14 PASS / 1 FAIL（唯一 FAIL 是测试断言的小问题，不影响修复）

---

## 问题背景

用户需要实现 **连续的 AIBUS 温控器控制**（而非只控制一次）。

V4.3 存在两个核心问题：
1. **连续写入温度时频繁出现 `Err:Wrt8` / `Err:Wrt10`**
2. **`WaitUntilStable` 第一次稳定后，后续调用直接返回 `OK`（假阳性）**

---

## 根因分析

### 1. Err:Wrt8 / Err:Wrt10
- 每次写指令前**没有清空串口输入缓冲区**
- 上一次读操作的 10 字节响应残留在缓冲区中
- 写后读取响应时，先读到残留数据（`resp[2] = 0x52`，读指令码），不是本次写操作的响应（`resp[2]` 应为 `0x43`）
- 残留 10 字节 → `Err:Wrt10`；残留 8 字节 → `Err:Wrt8`

### 2. WaitUntilStable 假阳性
- 上一次稳定后 `_currentState = STABLE` 保持不变
- `_history` 队列中仍有 60 个旧数据（接近 0 的 diff）
- 再次调用时，若温差不大，直接返回 `STABLE`，立即返回 `"OK"`
- 新温度下的稳定判断被旧数据严重干扰

---

## V4.4 修复内容

| 位置 | 修复 | 效果 |
|------|------|------|
| `AIBUSDevice.Read()` | 写前 `DiscardInBuffer()` + `Sleep(50)` | 消除残留数据干扰 |
| `AIBUSDevice.WriteSV()` | 写前 `DiscardInBuffer()` + `Sleep(50)` | 消除残留数据干扰 |
| `AIBUSDevice.WriteSV()` | 读响应从 8 字节改为 **10 字节** | AIBUS 写响应也是 10 字节 |
| `AIBUSDevice.WriteSV()` | 写入成功后调用 `ResetStability()` + 记录 `_lastTargetSV` | SET 后从头判断稳定 |
| `DeviceBase.CheckStability()` | 检测到 SV 与 `_lastTargetSV` 偏差 > 1°C 时自动重置 | 外部修改 SV 时自动适应 |
| `DeviceHelper.WaitUntilStable()` | **开始时强制调用 `ResetStability()`** | 每次调用都从头等待 |
| `ModbusDevice` | 同样增加 `DiscardInBuffer()` 和 `Sleep(50)` | 一致性修复 |
| `SerialPortManager` | 新增 `ClearBuffers()` 公共方法 | 外部可手动清空 |

---

## 验证结果

```
Test 1: SET 后状态机重置
  25C 稳定后: STABLE  → SET 30.0 后: HEATING  ✓

Test 2: 连续控制 25C → 30C
  第一轮: OK (STABLE at 25C)  ✓
  第二轮: OK (STABLE at 30C)  ✓

Test 3: 连续控制 25 → 30 → 35
  25.0C: OK, PV=25.0, calls=17   ✓
  30.0C: OK, PV=30.0, calls=39   ✓
  35.0C: OK, PV=35.0, calls=61   ✓
```

---

## 待办事项

- [ ] 用真实串口或 com0com 虚拟串口验证物理通信
- [ ] 将 `NovaCOMPluginV4.4.dll` 重命名为 `NovaCOMPlugin.dll` 替换 Nova 中的旧版本
- [ ] 在 Nova 中测试完整实验流程（连续多个温度点）
- [ ] 考虑是否需要增加 `SerialPortManager.ClearBuffers()` 的 Nova 调用入口

---

## 文件清单

| 文件 | 说明 |
|------|------|
| `NovaCOMPluginV4.4.cs` | 修复后的源码 |
| `NovaCOMPluginV4.4.dll` | 编译好的插件 |
| `TestContinuousV2.cs` | 验证测试源码（Mock 设备） |
| `TestContinuousV2.exe` | 验证测试程序 |
| `NovaCOMPlugin 测试指南.md` | 原始测试文档 |
| `TestNovaCOMPlugin.cs` | 原始测试程序源码 |

---

## 编译命令

```batch
C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe /target:library /out:NovaCOMPluginV4.4.dll NovaCOMPluginV4.4.cs
```

## 使用方式（Nova 中）

```csharp
// 1. 初始化
AIBUSDevice heater = new AIBUSDevice();
heater.SetAddress("1");
SerialPortManager.Open("COM3", "9600");

// 2. 设定温度（自动重置状态机）
heater.SendCommand("SET 25.0");

// 3. 等待稳定（自动从头开始判断）
DeviceHelper.WaitUntilStable(heater, 300);  // 返回 "OK"

// 4. 连续控制下一个温度
heater.SendCommand("SET 30.0");
DeviceHelper.WaitUntilStable(heater, 300);  // 返回 "OK"（V4.3 这里会直接返回）
```
