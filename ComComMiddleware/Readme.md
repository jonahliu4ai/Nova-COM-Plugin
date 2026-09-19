# ComComMiddleware

串口透传 / 协议转换中间件。UI 通过 COM-A（Uplink）接收上位机（Nova）下发的文本命令，解析设备 JSON 配置后，通过 COM-B（Downlink）以对应协议帧与真实设备通信。

---

## 目录

1. [支持的协议](#支持的协议)
2. [快速开始](#快速开始)
3. [命令输入格式](#命令输入格式)
4. [设备 JSON 说明](#设备-json-说明)
5. [如何添加新设备](#如何添加新设备)
6. [构建与测试](#构建与测试)
7. [示例](#示例)

---

## 支持的协议

当前 C# 核心实现支持以下四种协议引擎：

| 协议名 | 说明 | 引擎类 |
|--------|------|--------|
| `modbus-rtu` | 标准 Modbus-RTU，支持读保持寄存器、写单寄存器、读写线圈 | `ModbusEngine` |
| `fixed-frame` | 固定格式字节帧，支持模板占位符、`sum8`/`xor8` 校验 | `FixedFrameEngine` |
| `custom` | 自定义空格分隔模板，支持 `{key:u8}` 严格 1 字节参数 | `CustomEngine` |
| `sevenstar` | 七星华创 CS200A 系列质量流量计专用协议 | `SevenStarEngine` |

> 提示：如需支持新协议（如 `bacnet`、`opc-ua` 等），可新增一个实现 `IProtocolEngine` 的类，并在 `ProtocolEngineFactory.Create` 中注册。

### 自定义协议（custom）能做什么

`custom` 协议让你无需改 C# 代码，仅通过 JSON 定义即可生成任意定长字节帧。

- `send` 字段用空格分隔 token。
- 固定 token 按 hex 解析，如 `AA`、`55`。
- 占位符 `{key:u8}` 从命令参数取值，严格限制 `0-255`（十进制或 `0x??`）。
- 解析失败返回 `ERR:CustomParse:key`。

示例：

```json
{
  "commands": {
    "ping": { "send": "AA 55 01 {addr:u8}" },
    "echo": { "send": "AA 55 02 {d1:u8} {d2:u8}" }
  }
}
```

对应命令：

```text
@MyDevice ping
@MyDevice echo d1=10 d2=0xAB
```

---

## 快速开始

1. 编译：运行 `build.ps1`，生成 `ComComMiddleware.exe`、`ComComMiddleware.Core.dll`、`SmokeTests.exe`。
2. 启动 UI，选择 Uplink COM（COM-A）和 Downlink COM（COM-B）。
3. 设置 Config Directory（通常指向 `devices/` 目录或当前目录）。
4. 点击 **Load** 加载 JSON，或点击 **Samples** 生成示例 JSON。
5. 在 Manual Send 中输入命令并发送。

---

## 命令输入格式

### 完整格式

```text
DEVICE=<name>;ADDR=<addr>;CMD=<command> [参数]
```

示例：

```text
DEVICE=Sample_FixedFrame;ADDR=1;CMD=open_ch N=3 state=1
```

### 简写格式

```text
@<name> <command> [参数]
```

示例：

```text
@Sample_FixedFrame open_ch N=3 state=1
@Sample_Modbus read_pv
```

### RAW 透传

不依赖 JSON 配置，直接发送字节或文本：

```text
RAW:HEX:AA 55 01 01
RAW:TXT:hello world
```

---

## 设备 JSON 说明

### 顶层结构

```json
{
  "name": "Device_Name",
  "protocol": "modbus-rtu",
  "default_baudrate": 9600,
  "default_address": 1,
  "registers": { ... },
  "commands": { ... }
}
```

字段说明：

| 字段 | 必填 | 说明 |
|------|------|------|
| `name` | 是 | 设备唯一名称，命令中 `DEVICE=` 或 `@` 后使用 |
| `protocol` | 是 | `modbus-rtu`、`fixed-frame`、`custom` 之一 |
| `default_baudrate` | 否 | 默认波特率，UI 可覆盖 |
| `default_address` | 否 | 默认站号/地址；命令中不写 `ADDR` 时使用 |
| `registers` | 否 | Modbus 寄存器或 fixed-frame 可复用段定义 |
| `commands` | 是 | 用户可调用的命令定义 |

### 1. Modbus-RTU 协议

#### registers

```json
"registers": {
  "pv": { "fc": 3, "addr": 0, "type": "int16", "scale": 0.1, "unit": "℃" },
  "sv": { "fc": 3, "addr": 1, "type": "int16", "scale": 0.1, "unit": "℃" }
}
```

字段说明：

| 字段 | 说明 |
|------|------|
| `fc` | 功能码（3/4 读保持/输入寄存器，6 写单寄存器） |
| `addr` | 寄存器起始地址 |
| `type` | `int16`、`uint16`、`float`、`uint32`、`int32`、`bitmap` |
| `scale` | 显示换算系数，读取时 `value * scale`，写入时 `value / scale` |
| `unit` | 单位字符串 |
| `length` | `bitmap` 类型时有效，默认 8 |

#### commands

```json
"commands": {
  "read_pv": { "action": "read", "register": "pv" },
  "set_sv":  { "action": "write", "register": "sv", "input": "float" },
  "read_all": { "action": "read_multi", "registers": ["pv", "sv"] }
}
```

`action` 取值：

- `read`：读单个寄存器
- `write`：写单个寄存器
- `read_multi`：批量读多个寄存器
- `read_coil`：读线圈
- `write_coil`：写线圈

### 2. Fixed-Frame 协议

#### registers

```json
"registers": {
  "header": { "bytes": ["0xA0"], "checksum": "sum8" }
}
```

`checksum` 取值：`sum8`、`xor8`、`none`。若指定，发送时会自动在帧尾追加校验字节。

#### commands

```json
"commands": {
  "open_ch": {
    "template": "0xA0,{N},0x00,0x01",
    "params": ["N", "state"],
    "response_mode": "none"
  },
  "ping": {
    "bytes": ["A0", "00", "00", "00"],
    "response_mode": "none"
  }
}
```

字段说明：

| 字段 | 说明 |
|------|------|
| `template` | 逗号分隔的字节模板，支持 `{N}`、`{channel}`、`{state}`、`{addr}` 等占位符 |
| `bytes` | 固定字节数组，与 `template` 二选一 |
| `params` | 参数名列表，仅用于 UI/日志提示 |
| `response_mode` | `none` 不等待响应；默认等待响应并以 HEX 返回 |
| `response_parse` | `text` 时以 ASCII 文本解析响应 |
| `response_length` | 固定响应字节长度，大于 0 时按长度读取 |
| `checksum` | 覆盖 `registers` 中定义的校验方式 |

### 3. Custom 协议

Custom 协议支持两种发送模式，通过 `send_mode` 指定：

- `hex`（默认）：`send` 中固定 token 按 hex 解析，占位符 `{key:u8}` 插入 1 字节。
- `ascii` / `text`：`send` 整体作为 ASCII 文本模板，占位符 `{key}` 直接替换为参数文本。

#### Hex 模式示例

```json
"commands": {
  "ping": {
    "send": "AA 55 01 {addr:u8}",
    "response_mode": "text",
    "response_parse": "text"
  },
  "echo": {
    "send": "AA 55 02 {d1:u8} {d2:u8}",
    "response_mode": "text"
  }
}
```

#### ASCII / AT 模式示例

```json
"commands": {
  "identify": {
    "send": "AT+ID?\r\n",
    "send_mode": "ascii",
    "response_mode": "text",
    "response_parse": "text"
  },
  "set_ch": {
    "send": "AT+CH={channel},{state}\r\n",
    "send_mode": "ascii",
    "response_mode": "text",
    "response_parse": "text"
  }
}
```

发送命令：

```text
@MyATDevice identify
@MyATDevice set_ch channel=3 state=1
```

实际发出 ASCII 字节：`AT+CH=3,1\r\n`。

字段说明：

| 字段 | 说明 |
|------|------|
| `send` | hex 模式：空格分隔 token；ascii 模式：完整 ASCII 文本模板 |
| `send_mode` | `hex`（默认）、`ascii` 或 `text` |
| `response_mode` | `none` 不等待响应；默认等待 |
| `response_parse` | `text` 以 ASCII 解析响应；默认 HEX |

### 4. SevenStar 协议（七星华创 CS200A）

专用协议，自动拼帧、计算 `sum8` 校验、处理 `UFRAC16` 流量值编码/解码。

```json
{
  "name": "七星华创_CS200A",
  "protocol": "sevenstar",
  "default_baudrate": 9600,
  "default_address": 32,
  "registers": {
    "flow": {
      "class": "0x68",
      "instance": "0x01",
      "attribute": "0xB9",
      "type": "ufrac16",
      "unit": "%"
    },
    "setpoint": {
      "class": "0x69",
      "instance": "0x01",
      "attribute": "0xA4",
      "type": "ufrac16",
      "unit": "%"
    }
  },
  "commands": {
    "read_flow": { "action": "read", "register": "flow" },
    "write_flow": { "action": "write", "register": "setpoint", "input": "float" },
    "read_multi": { "action": "read_multi", "registers": ["flow", "setpoint"] }
  }
}
```

字段说明：

| 字段 | 说明 |
|------|------|
| `class` / `instance` / `attribute` | 命令定位字段，对应协议文档中的 Class / Instance / Attribute |
| `type` | `ufrac16` / `ufrac16_pct` / `uint16` / `int16` / `uint8` / `string` / `textXX` |
| `scale` / `offset` | 读取后的换算系数和偏移 |
| `unit` | 显示单位 |
| `action` | 命令动作：`read`、`write`、`read_multi` |
| `register` | 命令关联的寄存器名 |
| `registers` | `read_multi` 时批量读取的寄存器列表 |
| `input` | 写命令输入提示（`float`、`int`），不影响编码 |

发送命令：

```text
@七星华创_CS200A read_flow
@七星华创_CS200A write_flow 50
@七星华创_CS200A read_multi
```

写 50 表示 50% 满量程，引擎自动编码为 `UFRAC16` 原始值 `0x8000`。

---

## 如何添加新设备

1. 复制一个示例 JSON（如 `Sample_Modbus_Example.json`），重命名为目标设备名。
2. 修改 `name`、`protocol`、默认波特率/地址。
3. 根据协议文档填写 `registers`（Modbus 需要，fixed-frame/custom 可选）。
4. 为每个操作定义 `commands`：
   - Modbus：`action` + `register`
   - Fixed-frame：`template` + `params` + `response_mode`
   - Custom：`send` + `send_mode`；hex 模式用 `{key:u8}`，ascii/AT 模式用 `{key}`
   - SevenStar：在 `registers` 中定义 `class`/`instance`/`attribute`/`type`，命令中 `action` + `register`
5. 保存到配置目录，UI 中点击 **Load**。
6. 用 Manual Send 或 `Sample_Commands.txt` 中的示例命令验证帧内容。

---

## 构建与测试

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

输出：

- `ComComMiddleware.Core.dll`：核心类库（协议、配置、网关）
- `ComComMiddleware.exe`：WinForms UI
- `SmokeTests.exe`：单元/集成测试

运行测试：

```powershell
.\SmokeTests.exe
```

---

## 示例

### 打开通道 3

```text
DEVICE=Sample_FixedFrame;ADDR=1;CMD=open_ch N=3 state=1
```

发送帧：`A0 03 00 01` + `sum8` 校验。

### 读取 Modbus PV

```text
@Sample_Modbus read_pv
```

发送 Modbus-RTU 读保持寄存器帧，返回带单位温度值。

### 自定义协议 ping

```text
@Sample_Custom ping
```

发送 `AA 55 01 01`。

---

## 扩展新协议

如需新增协议引擎：

1. 在 `ComComMiddleware.Core.cs` 中实现 `IProtocolEngine`：

```csharp
public class MyProtocolEngine : ProtocolEngineBase
{
    public override string Execute(string cmd, string args)
    {
        // 解析命令，通过 Bus.SendAndReceive 发送/接收
        return "OK";
    }
}
```

2. 在 `ProtocolEngineFactory.Create` 中注册：

```csharp
if (p == "my-protocol") return new MyProtocolEngine();
```

3. 创建对应的 JSON，设置 `"protocol": "my-protocol"`。
