# NOVA Modbus RTU Patcher

子项目：让 NOVA 2.1 的 External RS-232 模块支持二进制/十六进制输入，并通过前缀路由到不同协议处理。

## 快速使用

```
Release/
├── Patcher.exe              ; 自动 Patch 工具
├── MyModbusExtension.dll    ; 扩展 DLL（协议路由 + CRC）
├── Mono.Cecil.dll           ; Mono.Cecil 库
└── Patch.bat                ; 右键管理员运行
```

将 4 个文件复制到 `C:\Program Files\Metrohm Autolab\Nova 2.1\`，右键管理员运行 `Patch.bat`。

## 发送模式

| 输入 | 行为 |
|------|------|
| `01 03 00 00 00 0A` | **默认 RAW HEX**：直接发送二进制 |
| `MB:01 03 00 00 00 0A` | **Modbus RTU**：解析 HEX + 自动 CRC16 |
| `HEX:00 0A 00` | **RAW HEX 别名**：等同于默认 |
| `SET 123` / `*idn?` | ASCII fallback：原 WriteLine 逻辑 |

## 源码

- `src/Patcher.cs` — Mono.Cecil Patch 工具
- `src/MyModbusExtension.cs` — 协议路由 + CRC16 逻辑
- `src/TestPatch.cs` — 串口测试程序

## 依赖

- .NET Framework 4.x
- Mono.Cecil（已包含）

---

S.Liu Studio
