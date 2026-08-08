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

七星华创 CS200A 使用私有串口协议（类似 Allen-Bradley CIP 的简化版），JSON 需要声明 `class` / `instance` / `attribute`。

### 额外根字段

| 字段 | 说明 |
|------|------|
| `full_scale` | 满量程（sccm），用于 UFRAC16 解码 |
| `gas_type` | 默认气体类型，如 "N2" |

### registers 字段

```json
"flow": {
  "service": "read",       // read / write
  "class": "0x68",         // 命令类（Class）
  "instance": "0x01",      // 实例号
  "attribute": "0xB9",     // 属性码
  "type": "ufrac16",       // ufrac16 / uint16 / string
  "unit": "sccm"
}
```

> CS200A 常用 Class：`0x68`=流量, `0x69`=设定值, `0x66`=气体参数, `0x64`=设备信息

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
