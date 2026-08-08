# NOVA Modbus RTU Patcher

让 NOVA 2.1 的 External RS-232 模块支持**二进制/十六进制发送**与**多格式接收解析**，并通过前缀路由到不同协议处理。

## 快速使用

```
Release/
├── Patcher.exe              ; 自动 Patch 工具
├── MyModbusExtension.dll    ; 扩展 DLL（协议路由 + CRC16 + 接收解析）
├── Mono.Cecil.dll           ; Mono.Cecil 库
└── Patch.bat                ; 右键管理员运行
```

将 4 个文件复制到 `C:\Program Files\Metrohm Autolab\Nova 2.1\`，右键管理员运行 `Patch.bat`。

> **注意**：Patch 前会自动备份 `EcoChemie100.dll.bak`，如需还原直接覆盖即可。

---

## 发送模式

| 输入 | 发送行为 | 接收格式 |
|------|---------|---------|
| `01 03 00 00 00 0A` | **RAW HEX**：直接发送二进制 | HEX（默认） |
| `MB:01 03 00 00 00 0A` | **Modbus RTU**：解析 HEX + 自动 CRC16 | HEX（默认） |
| `MB_FLOAT:01 03 00 00 00 0A` | **Modbus RTU + CRC16** | **FLOAT32** |
| `MB_DEC16:01 03 00 00 00 0A` | **Modbus RTU + CRC16** | **DEC16** |
| `HEX:3F 80 00 00` | **RAW HEX 别名** | HEX（默认） |
| `HEX_FLOAT:3F 80 00 00` | **RAW HEX** | **FLOAT32** |
| `DEC32:01 02 03 04` | **RAW HEX** | **DEC32** |
| `FLOAT:3F 80 00 00` | **RAW HEX** | **FLOAT32** |
| `DOUBLE:40 09 21 FB 54 44 2D 18` | **RAW HEX** | **FLOAT64** |
| `ASCII:*idn?` | ASCII fallback：原 `WriteLine` 逻辑 | ASCII |
| `*idn?` / `SET 123` | 非 HEX fallback：原 `WriteLine` 逻辑 | 原 `ReadLine` 逻辑 |

### 前缀语法规则

```
[SEND_FMT][_RECV_FMT]:HEX_DATA
```

- **SEND_FMT**：发送格式，决定如何组装串口数据帧
  - `MB` → Modbus RTU 帧（自动追加 CRC16）
  - `HEX` / `DEC16` / `DEC32` / `FLOAT` / `DOUBLE` → 原始二进制帧
  - `ASCII` → 走原 `SerialPort.WriteLine()` 逻辑
- **RECV_FMT**（可选）：接收解析格式
  - `HEX` → 十六进制字符串，如 `"01 03 00 0A"`
  - `ASCII` → ASCII 字符串
  - `DEC16` → 16-bit 有符号整数（大端），如 `"10 256 -1"`
  - `DEC32` → 32-bit 有符号整数（大端），如 `"1000 65536"`
  - `FLOAT` → 32-bit IEEE-754 浮点数（大端），如 `"3.14 2.718"`
  - `DOUBLE` → 64-bit IEEE-754 浮点数（大端），如 `"3.14159265"`
- 省略 `RECV_FMT` 时，`MB` 和 `HEX` 默认使用 `HEX`，其余前缀使用与 SEND_FMT 匹配的解析格式

---

## 接收模式（Modbus 自动帧剥离）

当使用 `MB:` 前缀发送时，`Receive()` 返回的结果会自动进行** Modbus RTU 协议解析**：

1. **CRC16 校验** —— 验证响应帧完整性
2. **去掉帧头** —— 剥离从机地址(1B) + 功能码(1B)
3. **去掉 CRC 尾部** —— 剥离最后 2 字节 CRC
4. **按 RECV_FMT 解析数据负载** —— 剩余纯数据按指定格式转换

### 示例

发送 `MB_FLOAT:01 03 00 00 00 02`（读 2 个保持寄存器），设备返回原始帧：

```
01 03 04 3F 80 00 00 C5 3A
│  │  │  └────┬────┘ └──┘
│  │  │       │       CRC
│  │  │       数据负载（4 字节）
│  │  字节数
│  功能码 0x03
从机地址
```

Patch 后 `Receive()` 的返回结果：
- CRC 校验通过
- 去掉地址/功能码/字节数/CRC，提取负载 `3F 80 00 00`
- 按 `FLOAT` 解析 → 返回 `"1"`（即 `3F800000` 对应的 IEEE-754 值）

---

## 源码

- `src/Patcher.cs` — Mono.Cecil IL Patch 工具
  - Patch `ExternalRS232.Send()`：插入 `ModbusHelper.TrySendModbus()` 前缀路由
  - Patch `ExternalRS232.Receive()`：插入 `ModbusHelper.TryReceiveModbus()` 多格式解析
  - **fallback 逻辑**：若不走 Modbus/HEX 路径，则完整保留原始的 `WriteLine()` / `ReadLine()` 行为
- `src/MyModbusExtension.cs` — 协议路由 + CRC16 + 接收解析 + Modbus 帧剥离
  - `ReceiveParseMode` 枚举：Hex / Ascii / DecInt16 / DecInt32 / Float32 / Float64
  - `TrySendModbus()`：前缀解析 + CRC 组装 + 记录期望接收格式
  - `TryReceiveModbus()`：读取串口字节 → Modbus 帧剥离 → 按格式解析
  - `StripModbusFrame()`：支持功能码 0x01~0x06 / 0x0F / 0x10 的响应帧解析
- `src/TestPatch.cs` — 串口测试程序

## 依赖

- .NET Framework 4.x
- Mono.Cecil（已包含）
- EcoChemie100.dll（NOVA 安装目录自带，Patch 目标）

## 注意事项

1. **原始 `Receive()` 是有返回值的**：返回类型为 `System.String`，Patch 后保持该签名不变。
2. **Patch 前自动备份**：首次运行会在目标目录生成 `EcoChemie100.dll.bak`。
3. **修改后需重启 NOVA**：Patch 完成后必须重新启动 NOVA 2.1 才能生效。
4. **Modbus 帧剥离仅对 `MB:` 前缀生效**：`HEX:` / `FLOAT:` 等原始二进制前缀不会进行 Modbus 协议解析，返回完整原始字节。
5. **CRC 校验失败时保守回退**：若 Modbus 响应帧 CRC 不匹配，则原样返回整帧 HEX 字符串，不丢弃任何数据。
6. **⚠️ NOVA Receive action 必须通过 placeholder 捕获返回值**：NOVA 的 action 机制不会自动显示 `Receive()` 的返回值，必须在 action 中用 `{0}`（或其他 placeholder）显式绑定，否则运行后界面上看起来"没有任何输出"。这与 Patch 无关，是 NOVA 本身的行为。详见博客排错记录。

---

S.Liu Studio
