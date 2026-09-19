## 🤖 编程助手CodeAgentt


下面是一个针对 **ComComMiddleware** 设备配置 JSON 的详细 README 草稿，涵盖整体结构、主要字段、协议约束以及针对 `Sample_FixedFrame` 这类 fixed-frame 设备的命令写法。你可以直接保存为 `devices/README_new.md` 或补充到现有文档中，后续为新设备写 JSON 时照此格式扩展即可。

---

# ComComMiddleware 设备配置 JSON 说明

> 本文档说明如何为 ComComMiddleware 定义设备 JSON 文件，并给出命令写法与常见协议注意事项。所有 JSON 文件放置在 `ComComMiddleware/devices/` 目录（或在 UI 中手动选择的 Config Directory）。

## 1. 顶层结构

每个设备 JSON 顶层字段：

```json
{
  "name": "Device_Name",
  "protocol": "<protocol-name>",
  "default_baudrate": 9600,
  "default_address": 1,
  "registers": { ... },
  "commands": { ... }
}
```

- `name`：唯一设备名称，在发送命令时使用（区分大小写）。
- `protocol`：取值之一：
 - `modbus-rtu`
 - `fixed-frame`
 - `custom`
 - （可扩展：`sevenstar` 等内部协议）
- `default_baudrate`：UI 中默认波特率（可被用户覆盖）。
- `default_address`：默认站号/地址（Modbus 1–247，fixed-frame/custom 由协议决定）。
- `registers`：可选。对 Modbus/fixed-frame/自定义协议需要的寄存器或模板进行定义。
- `commands`：必填。定义用户可调用的命令。

## 2. 常见字段详解

### 2.1 registers

`registers` 块用于存放可复用的数据段定义。不同协议含义不同：

- Modbus：
```json
  "registers": {
    "pv": { "fc": 3, "addr": 0, "type": "int16", "scale": 0.1, "unit": "°C" }
  }
  ```
  - `fc`：功能码（3/4=读保持寄存器，6=写单寄存器等）。
  - `addr`：起始地址。
  - `type`：数据类型（`int16`/`uint16`/`float` 等）。
  - `scale`/`unit`：可选，显示时换算。

- Fixed-frame：
  ```json
  "registers": {
    "header": { "bytes": ["0xA0"], "checksum": "sum8" }
  }
  ```
  - `bytes`：固定字节数组，用于插入段或校验。
  - `checksum`：`sum8` / `xor8` / `none`；发送时自动附加校验。

- Custom：
  - 常通过 `commands` 的 `send` 模板直接构建，不必在 `registers` 定义。

### 2.2 commands

命令定义格式（根据协议不同）：

#### a) Fixed-frame

```json
"commands": {
"open_ch": {
 "template": "0xA0,{N},0x00,0x01",
 "params": ["N", "state"],
 "response_mode": "none"
}
}
```

- `template`：逗号分隔的字节列表，可嵌 `{param}` 占位符。
- `{param}` 占位符支持：
  - 纯 `{NAME}`：在执行命令时替换成用户提供值（十六进制字符串或十进制）。
  - `{addr}` / `{address}`：自动替换为当前地址（UI 输入或默认地址）。
- `params`：命令需要哪些参数，供 UI/日志提示。
- `response_mode`（可选）：
  - `none`：发送后不等待响应。
  - `text`：读取串口文本（用于 ASCII 响应）。
  - 缺省：默认等待一帧并以 HEX 返回（`OK|AA BB ...`）。

#### b) Custom

```json
"commands": {
"echo_hex": {
 "send": "AA 55 02 {d1:u8} {d2:u8}",
 "response_mode": "text"
}
}
```

- `send`：空格分隔的 token。
  - 固定 token：`AA`、`55` 等，按 hex 解析成字节。
  - 占位符：`{key:u8}` 表示从参数 map 取键 `key`，严格限制 0–255（支持十进制或 `0x??`），解析失败会返回 `ERR:CustomParse:key`。
- 无 `params` 字段，需要在手动输入时显式写 `d1=... d2=...`。

#### c) Modbus-RTU

```json
"commands": {
"read_pv": { "action": "read", "register": "pv" },
"set_sv": { "action": "write", "register": "sv", "input": "float" }
}
```

- `action`：`read`/`write`/`read_multi` 等。
- `register`：引用 `registers` 中定义的条目。
- `input`：写命令的输入类型（`float`/`int` 等）。

## 3. 命令输入格式（Manual Send 面板）

Manual Send 文本框支持两种方式：

1. `DEVICE=<name>;ADDR=<addr>;CMD=<command> [params]`
   - 如：`DEVICE=Sample_FixedFrame;ADDR=1;CMD=open_ch N=2 state=1`
2. 简写：`@<name> <cmd> ...`
   - 如：`@Sample_FixedFrame open_ch N=2 state=1`

命令行解析遵循：
- 参数以空格、逗号或分号分隔。
- 每个参数写成 `key=value`。

## 4. 示例：Sample_FixedFrame

`Sample_FixedFrame.json`（随项目提供）：

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

- `open_ch`/`close_ch`：执行时需传 `N`（通道号），`state` 参数用于 UI 提示，可选。
 - `open_ch N=3 state=1` → 帧：`A0 03 00 01` + checksum。
 - `close_ch N=3 state=0` → 帧：`A0 03 00 00` + checksum。
- `ping`：固定字节帧，无响应。

### 使用示例

1. 打开通道 3
```
DEVICE=Sample_FixedFrame;ADDR=1;CMD=open_ch N=3 state=1
```
2. 关闭通道 3
```
@Sample_FixedFrame close_ch N=3 state=0
```
3. 发送 ping
```
@Sample_FixedFrame ping
```

串口抓包可见发送帧 + 校验字节。`response_mode = none` 时 UI 会显示 `OK|NoWait`。

## 5. 自定义校验/帧尾

- Fixed-frame 支持 `checksum`：`sum8`/`xor8`/`none`。若指定，系统自动在帧末尾追加校验字节，命令模板中无需再写。
- 若需要额外固定尾部，可在 `template` 中写明（如 `0xAA,{data},0x55`）。

## 6. 新设备定义步骤

1. 复制现有 JSON 样例（如 `Sample_FixedFrame.json`），重命名为目标设备名。
2. 修改 `name`、`protocol`、默认波特率/地址。
3. 结合协议文档填写 `registers`（若需要）。
4. 为每个操作定义 `commands`：
 - Fixed-frame：设置 `template`、`params`、`response_mode`。
 - Custom：使用 `send` + `{key:u8}` 等占位符。
 - Modbus：使用 `action` + `register`。
5. 保存到 `devices/` 或自定目录，UI 中配置 `Config Directory` 指向该路径并点击 `Load`。
6. 通过 Manual Send 或脚本调用验证：
 - 使用 `build.ps1` 编译并运行 `ComComMiddleware.exe`。
 - 打开虚拟串口/真实设备后发送命令，确认帧内容正确。

## 7. 常见问题

- **参数缺失/超范围**：Custom 协议解析严格，缺少 `key=u8` 或值超 0–255 会返回 `ERR:CustomParse:key`。
- **响应模式**：不需要响应时设 `response_mode: "none"`，否则默认等待 `SerialBus` 读取数据。
- **默认地址未生效**：Manual Send 文本中未写 `ADDR` 时使用 `default_address`。
- **JSON 语法**：保持标准 UTF-8 文本、英文标点，确保末尾无额外逗号。
- **配置目录**：运行时需确保 `Config Directory` 指向 JSON 所在文件夹。

---

你可以依据这份 README 给其它设备编写 JSON。建议将该文档补充到 `devices/README.md`，或新建英文说明文件，方便团队成员快速上手。配置完成后可使用前面提供的示例控制代码/Manual Send 命令进行验证。
