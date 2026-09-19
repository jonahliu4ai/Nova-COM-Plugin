# NovaCOMPlugin 设备配置文件说明

> 版本：V4.5
> 位置：本目录下的 `.json` 文件会被 `NovaCOMPluginV4.5.dll` 自动加载。

---

## 核心原则

**换设备 = 新增 JSON 文件，0 行 C# 改动，0 次重新编译。**

C# 侧的四种协议引擎负责校验、组帧、CRC 等底层逻辑；JSON 只描述「设备长什么样」，不描述「协议怎么算」。

---

## 文件结构

每个 `.json` 文件描述一台设备的**寄存器映射**和**命令模板**。

```json
{
  "name": "设备名称",
  "protocol": "modbus-rtu",
  "default_baudrate": 9600,
  "default_address": 1,
  "registers": { ... },
  "commands": { ... }
}
```

### 根字段说明

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `name` | string | ✅ | 设备名称，仅用于显示和日志 |
| `protocol` | string | ✅ | 协议类型，决定加载哪个引擎 |
| `default_baudrate` | int | ❌ | 默认波特率（常用 9600 / 19200） |
| `default_address` | int | ❌ | 默认从机地址 |
| `registers` | object | ❌ | 寄存器定义表 |
| `commands` | object | ❌ | 命令模板表 |

### protocol 取值

| 值 | 引擎 | 适用设备 |
|----|------|---------|
| `modbus-rtu` | ModbusEngine | Modbus RTU 温控器、继电器、传感器 |
| `aibus` | AIBUSEngine | 宇电温控器（AI-708 / AI-516 等） |
| `fixed-frame` | FixedFrameEngine | 固定帧私有协议（串口继电器等） |
| `sevenstar` | SevenStarEngine | 七星华创 CS200A 质量流量计 |

---

## 一、Modbus RTU 设备

### registers 字段

每个 key 代表一个「寄存器语义名」（如 `pv`、`sv`），value 描述其在 Modbus 总线上的位置和数据格式。

```json
"pv": {
  "fc": 3,            // 功能码：1=读线圈, 3=读保持寄存器, 4=读输入寄存器
  "addr": 0,          // 寄存器地址（十进制或 0x 十六进制）
  "type": "int16",    // 数据类型：uint16 / int16 / float / ufrac16
  "scale": 0.1,       // 缩放系数：原始值 × scale = 物理值
  "unit": "℃"        // 单位，仅用于返回字符串显示
}
```

### commands 字段

每个 key 是一个「语义命令名」（如 `read_pv`、`set_sv`），Nova 中通过 `SendCommand("read_pv")` 调用。

```json
"read_pv": {
  "action": "read",        // read / write / write_coil / read_coil
  "register": "pv"         // 关联 registers 中的 key
},
"set_sv": {
  "action": "write",
  "register": "sv",
  "input": "float"         // 输入类型：float / int
}
```

#### action 类型对照表

| action | 说明 | 额外字段 |
|--------|------|---------|
| `read` | 读单个寄存器 | `register` |
| `write` | 写单个寄存器 | `register`, `input` |
| `read_coil` | 读线圈状态 | `register`（其 addr 为起始地址） |
| `write_coil` | 写单个线圈 | `addr_expr` 或 `register` |
| `read_multi` | 读多个寄存器 | `registers`（数组） |

#### addr_expr：带参数的地址

继电器类设备常用 `N-1` 表达式，`N` 是用户传入的路数（1-based），引擎自动计算 0-based 地址。

```json
"open_ch": {
  "action": "write_coil",
  "addr_expr": "N-1",
  "value": 1,
  "params": ["N"]        // 声明需要的参数名
}
```

Nova 中调用：`relay.SendCommand("open_ch N=3")`

---

## 二、AIBUS 设备（宇电）

AIBUS 是宇电温控器的私有协议，组帧和校验由引擎内部完成。JSON 只需要声明「读/写哪个参数」。

### registers 字段

```json
"pv": {
  "action": "read",
  "param": "0x00",       // AIBUS 参数码（不是寄存器地址！）
  "type": "int16",
  "scale": 0.1,
  "unit": "℃"
}
```

> 宇电常见参数码：`0x00`=PV, `0x01`=SV, `0x02`=MV, `0x03`=报警状态

### commands 字段

```json
"read_pv": {
  "action": "read",
  "register": "pv"       // 引擎自动查 param，组装 AIBUS 读帧
},
"set_sv": {
  "action": "write",
  "register": "sv",
  "input": "float"       // 引擎自动 × scale 后写入
}
```

---

## 三、固定帧设备（串口继电器）

适用于那些「发几个字节、设备就动作」的极简私有协议。

### 方式 A：固定字节序列（命令极少时）

直接在 `commands.bytes` 里写死：

```json
"open_ch1": {
  "bytes": ["0xA0", "0x00", "0x00", "0x01"]
}
```

引擎原样发送这 4 个字节（可选追加校验）。

### 方式 B：模板帧（带参数）

```json
"set_channel": {
  "template": "0xA0,{channel},0x00,{state}",
  "params": ["channel", "state"]
}
```

Nova 调用：`relay.SendCommand("set_channel channel=3,state=1")`

引擎解析模板，替换变量后组帧。

### 校验配置

在 `registers` 里声明校验方式：

```json
"registers": {
  "header": {
    "bytes": ["0xA0"],
    "checksum": "sum8"     // sum8 / xor8 / none（默认）
  }
}
```

引擎在发送前自动计算并追加校验字节。

---

## 四、SevenStar 设备（CS200A 流量计）

七星华创 CS200A 使用私有串口协议（**非 Modbus RTU**，帧结构类似简化版 CIP），JSON 需要声明 `class` / `instance` / `attribute`。

### 额外根字段

| 字段 | 说明 |
|------|------|
| `full_scale` | 满量程（sccm），用于 UFRAC16 解码 |
| `gas_type` | 默认气体类型，如 "N2" |
| `default_baudrate` | **出厂默认 19200**（旧版配置误写 9600） |
| `default_address` | 设备地址范围 **32–95（0x20–0x5F）**，出厂 32 |

### registers 字段

```json
"flow": {
  "service": "read",       // read / write
  "class": "0x68",         // 命令类（Class）
  "instance": "0x01",      // 实例号
  "attribute": "0xB9",     // 属性码
  "type": "ufrac16",       // ufrac16 / ufrac16_pct / uint16 / uint8 / string
  "scale": 0.0806,         // 仅 uint16：物理值 = raw*scale + offset（scale=0 表示原值）
  "offset": -50,           // 仅 uint16：偏移量（如环境温度 ℃）
  "unit": "sccm"
}
```

### type 取值（SevenStar 引擎）

| type | 数据格式 | 说明 |
|------|----------|------|
| `ufrac16` | UFRAC16 | 流量/设定值，按 `full_scale` 换算为 sccm |
| `ufrac16_pct` | UFRAC16 | 直接以 %FS 显示/写入（软启动、关闭值） |
| `uint16` | UINT16 | 原始值，或配 `scale`/`offset` 换算（如温度 = raw×0.0806−50） |
| `uint8` | UINT8 | 单字节（控制模式/阀命令/调零/EEPROM/Reset） |
| `string` | TEXTXX | ASCII 字符串（气体名称、序列号等），`length` 为总字节数 |

### 常用 Class / Attribute 速查（完整表见协议文档 §5）

| Class | 含义 | 常用 Attribute |
|-------|------|----------------|
| `0x68` | 流量读数/调零 | `0xB9` 读流量 / `0xBA` 调零(写1) |
| `0x69` | 控制模式/设定值 | `0x03` 控制模式 / `0xA4` 数字设定 / `0xA5` 当前设定 / `0x06` EEPROM Program |
| `0x6A` | 阀命令/软启动 | `0x01` 阀命令(0无影响/1关/2全开) / `0xA4` 软启动 / `0x91` 阀电压 |
| `0x66` | 气体参数 | `0x01` 气体名称 / `0x03` 满量程 |
| `0x64` | 设备信息 | `0x03` 制造商 / `0x04` 型号 / `0x07` 序列号 |
| `0x65` | 报警 | `0xA0` 读报警位图 |
| `0xA3` | 环境温度 | `0x07`（T = raw×0.0806−50 ℃） |
| `0x03` | RS485 配置 | `0x03` 复位(写1) |

### 完整示例（七星华创_CS200A.json）

```json
{
  "name": "七星华创_CS200A",
  "protocol": "sevenstar",
  "default_baudrate": 19200,
  "default_address": 32,
  "full_scale": 100,
  "gas_type": "N2",
  "registers": {
    "flow":     { "service": "read",  "class": "0x68", "instance": "0x01", "attribute": "0xB9", "type": "ufrac16", "unit": "sccm" },
    "setpoint": { "service": "write", "class": "0x69", "instance": "0x01", "attribute": "0xA4", "type": "ufrac16", "unit": "sccm" },
    "control_mode":      { "service": "read",  "class": "0x69", "instance": "0x01", "attribute": "0x03", "type": "uint8", "unit": "" },
    "control_mode_write":{ "service": "write", "class": "0x69", "instance": "0x01", "attribute": "0x03", "type": "uint8", "unit": "" },
    "temperature": { "service": "read", "class": "0xA3", "instance": "0x01", "attribute": "0x07", "type": "uint16", "scale": 0.0806, "offset": -50, "unit": "℃" }
  },
  "commands": {
    "read_flow":        { "action": "read",  "register": "flow" },
    "set_flow":         { "action": "write", "register": "setpoint", "input": "float" },
    "read_control_mode":{ "action": "read",  "register": "control_mode" },
    "set_control_mode": { "action": "write", "register": "control_mode_write", "input": "int" },
    "read_temperature": { "action": "read",  "register": "temperature" },
    "read_gas_info":    { "action": "read_multi", "registers": ["gas_name", "full_scale"] }
  }
}
```

> ⚠️ 读写分离寄存器：同一 Attribute 读和写要用两个寄存器名（如 `control_mode` / `control_mode_write`），
> 因为 SevenStar 读帧 DataLen=3、写帧 DataLen=4，引擎按 `service` 组帧。

### Nova 脚本调用（首次使用必读）

```csharp
SerialPortManager.Open("COM3", "19200");           // 出厂波特率 19200，不是 9600
SmartDevice mfc = new SmartDevice("七星华创_CS200A", "32");
if (mfc.ProfileName == null) { Console.WriteLine("配置加载失败"); return; }

// 出厂控制模式是模拟电压模式（0~5V），数字设定值无效！
string cm = mfc.SendCommand("read_control_mode");   // VALUE=2 = 模拟电压模式
if (cm.Contains("VALUE=2")) {
    mfc.SendCommand("set_control_mode 1");          // 切数字模式
    mfc.SendCommand("eeprom_program 1");            // 写入 EEPROM
    mfc.SendCommand("reset 1");                     // 重启后永久生效
}

mfc.SendCommand("set_flow 50.0");                   // 设 50 sccm
string r = mfc.SendCommand("read_flow");            // VALUE=49.8xxsccm
mfc.SendCommand("set_valve 2");                     // 阀全开 = 清洗
mfc.SendCommand("set_valve 1");                     // 阀关闭
```

> 固定值命令（如 `zero 1`、`reset 1`、`eeprom_program 1`）的 `1` 是参数，不能省略。

---

## 五、完整示例：新增一台设备

假设你拿到了一台新的 Modbus RTU 温控器，型号为「台达 DTA4848」：

**步骤 1：查手册，确认寄存器映射**

- PV 在保持寄存器 0x1000
- SV 在保持寄存器 0x1001
- 数据格式：int16，小数点 1 位（即 scale=0.1）

**步骤 2：新建 JSON 文件**

文件：`devices/台达_DTA4848.json`

```json
{
  "name": "台达_DTA4848",
  "protocol": "modbus-rtu",
  "default_baudrate": 9600,
  "default_address": 1,
  "registers": {
    "pv": {
      "fc": 3,
      "addr": "0x1000",
      "type": "int16",
      "scale": 0.1,
      "unit": "℃"
    },
    "sv": {
      "fc": 3,
      "addr": "0x1001",
      "type": "int16",
      "scale": 0.1,
      "unit": "℃"
    }
  },
  "commands": {
    "read_pv": {
      "action": "read",
      "register": "pv"
    },
    "read_sv": {
      "action": "read",
      "register": "sv"
    },
    "set_sv": {
      "action": "write",
      "register": "sv",
      "input": "float"
    }
  }
}
```

**步骤 3：Nova 中直接使用**

```csharp
SmartDevice dta = DeviceFactory.Create("台达_DTA4848", "1");
dta.SendCommand("set_sv 120.0");
DeviceHelper.WaitUntilStable(dta, 300);
```

> **不需要修改 C# 代码，不需要重新编译 DLL。**

---

## 六、调试技巧

### 1. 列出已加载的配置

```csharp
List<string> profiles = ProfileLoader.ListProfiles();
foreach (var p in profiles) Console.WriteLine(p);
```

### 2. 强制刷新配置缓存

修改 JSON 后，在 Nova 中调用：

```csharp
ProfileLoader.ClearCache();
```

下次 `DeviceFactory.Create()` 会重新读取磁盘文件。

### 3. 原始命令兜底

即使 JSON 里没有定义某个命令，`SmartDevice` 也支持直接发原始 Modbus 命令：

```csharp
dev.SendCommand("READ_HOLD 0 4");     // 读保持寄存器
dev.SendCommand("WRITE_HOLD 1 250");  // 写保持寄存器
dev.SendCommand("WRITE_COIL 3 1");    // 写线圈
```

### 4. 检查加载是否成功

```csharp
SmartDevice dev = DeviceFactory.Create("某设备", "1");
if (dev.ProfileName == null)
    Console.WriteLine("配置文件加载失败");
```

---

## 七、JSON 书写规范

1. **字段名区分大小写**：`action` ≠ `Action`
2. **十六进制字符串**：支持 `"0x0000"` 和 `"4096"` 两种写法
3. **注释**：JSON 标准不支持注释，请不要在 `.json` 文件中写 `//` 注释
4. **编码**：UTF-8（支持中文文件名和内容）
5. **校验**：修改后用 [JSONLint](https://jsonlint.com/) 验证格式

---

## 八、现有配置文件速查

| 文件 | 协议 | 用途 |
|------|------|------|
| `宇电_AI708.json` | aibus | 宇电温控器 |
| `欧姆龙_E5CC.json` | modbus-rtu | 欧姆龙温控器 |
| `华控_8路继电器.json` | modbus-rtu | Modbus 继电器 |
| `某厂_4路继电器.json` | fixed-frame | 固定帧继电器 |
| `七星华创_CS200A.json` | sevenstar | 质量流量计 |

---

## 九、ComComMiddleware 设备 JSON 快速说明

这一节专门说明 `ComComMiddleware` 的设备 JSON 写法，适合你后续新增 `fixed-frame` 或 `custom` 设备。

### 1. 顶层结构

```json
{
  "name": "Sample_FixedFrame",
  "protocol": "fixed-frame",
  "default_baudrate": 9600,
  "default_address": 1,
  "registers": { ... },
  "commands": { ... }
}
```

- `name`：设备名，发送命令时用它选择配置。
- `protocol`：协议类型。
  - `modbus-rtu`
  - `fixed-frame`
  - `custom`
  - `sevenstar`
- `default_baudrate`：默认波特率。
- `default_address`：默认地址。
- `registers`：可选，放寄存器/帧片段定义。
- `commands`：必填，定义具体命令。

### 2. 手动发送命令格式

手动发送栏支持两种常用格式：

#### KV 格式

```text
DEVICE=<name>;ADDR=<addr>;CMD=<command> [params...]
```

示例：

```text
DEVICE=Sample_FixedFrame;ADDR=1;CMD=open_ch N=3 state=1
```

#### @ 简写格式

```text
@<name> <command> [params...]
```

示例：

```text
@Sample_FixedFrame open_ch N=3 state=1
```

参数写法统一是 `key=value`，多个参数之间用空格分隔即可。

### 3. fixed-frame 命令写法

`fixed-frame` 适合“发固定字节就动作”的设备。

#### bytes 方式

```json
"ping": {
  "bytes": ["A0", "00", "00", "00"],
  "response_mode": "none"
}
```

- `bytes` 里的每个元素会按十六进制字节发送。
- `response_mode: "none"` 表示发送后不等回复。

#### template 方式

```json
"open_ch": {
  "template": "0xA0,{N},0x00,0x01",
  "params": ["N", "state"]
}
```

- `template` 里用逗号分隔字段。
- 固定字节可以写成 `A0`、`0xA0` 这种形式。
- 参数用 `{N}`、`{state}` 这种占位符。
- `params` 用来说明这个命令需要哪些参数。

#### checksum

如果需要尾部校验，可以在 `registers` 中写：

```json
"registers": {
  "header": {
    "bytes": ["0xA0"],
    "checksum": "sum8"
  }
}
```

支持的校验值：

- `sum8`
- `xor8`
- `none`

### 4. custom 命令写法

`custom` 适合你要精确控制每个字节，但又希望参数更严格的情况。

```json
"echo_hex": {
  "send": "AA 55 02 {d1:u8} {d2:u8} {d3:u8} {d4:u8} {d5:u8}",
  "response_mode": "text",
  "response_parse": "text"
}
```

#### 规则

- 普通 token，例如 `AA`、`55`、`02`，会被当成**固定字节**发送。
- `{name:u8}` 表示参数必须是一个字节：
  - 允许 `0` 到 `255`
  - 也允许十六进制写法，比如 `0x68`
- 如果参数非法，会返回：
  - `ERR:CustomParse:<token>`

#### 发送示例

```text
DEVICE=Sample_Custom;ADDR=1;CMD=ping
DEVICE=Sample_Custom;ADDR=1;CMD=echo_hex d1=104 d2=101 d3=108 d4=108 d5=111
```

### 5. Sample_FixedFrame 示例

当前仓库自带的 `Sample_FixedFrame.json` 可以这样理解：

```json
{
  "name": "Sample_FixedFrame",
  "protocol": "fixed-frame",
  "default_baudrate": 9600,
  "default_address": 1,
  "registers": {
    "header": { "bytes": ["0xA0"], "checksum": "sum8" }
  },
  "commands": {
    "open_ch": { "template": "0xA0,{N},0x00,0x01", "params": ["N", "state"] },
    "close_ch": { "template": "0xA0,{N},0x00,0x00", "params": ["N", "state"] },
    "ping": { "bytes": ["A0", "00", "00", "00"], "response_mode": "none" }
  }
}
```

对应命令：

- `@Sample_FixedFrame ping`
- `@Sample_FixedFrame open_ch N=3 state=1`
- `@Sample_FixedFrame close_ch N=3 state=0`

### 6. 新设备建议写法

如果你要自己写一个新设备 JSON，建议按这个顺序：

1. 先确认协议是 `modbus-rtu`、`fixed-frame`、`custom` 还是 `sevenstar`。
2. 再确认默认波特率和默认地址。
3. 写 `registers`：
   - 固定帧设备可以放校验和固定头。
   - 自定义协议可放共享片段。
4. 写 `commands`：
   - 每个功能对应一个命令名。
   - 命令里尽量只保留最必要的字段。
5. 保存到 `devices/` 目录。
6. 打开程序后在 UI 里把 Config Directory 指到这个目录，点击 Load。
7. 用手动发送栏测试：
   - 先发 `ping`
   - 再发读命令
   - 最后发写命令

### 7. 常见注意点

- JSON 文件里不要写注释。
- 十六进制字节可以写成 `A0` 或 `0xA0`。
- 参数名区分大小写时，最好统一用小写或固定格式。
- `custom` 的 `{name:u8}` 是严格字节输入，超范围会报错。
- `response_mode: "none"` 的命令不会等待回复。

如果你愿意，我下一步可以直接帮你把这份说明整理成一版更正式的 `devices/README.md` 排版，或者我可以顺手再给你补一个 `Sample_Custom.json` 的完整注释版。
