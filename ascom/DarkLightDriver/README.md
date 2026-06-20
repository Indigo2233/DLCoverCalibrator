# DarkLight Cover Calibrator — Custom ASCOM Driver

这是一个从头编写的 ASCOM CoverCalibrator V2 驱动，支持直接配置舵机开合角度。

## 与原版驱动的区别

| 功能 | 原版驱动 | 本驱动 |
|------|---------|--------|
| Cover 开/关/停 | ✓ | ✓ |
| 平场板亮度控制 | ✓ | ✓ |
| 除露加热控制 | ✓ | ✓ |
| **主舵机开合角度配置** | ✗ (需刷固件) | **✓ (Setup 界面)** |
| **副舵机开合角度配置** | ✗ (需刷固件) | **✓ (Setup 界面)** |
| 源码可修改 | ✗ (闭源) | **✓ (开源)** |

## 前提条件

- Windows 10/11
- [ASCOM Platform 7](https://ascom-standards.org/) 或更新版本
- .NET Framework 4.8
- DLC 固件已刷入并支持角度命令（`UO`、`UC`、`VO`、`VC`、`uO`、`i`、`vO`、`vC`）

## 编译

```bash
# 在 Windows 上使用 Visual Studio 或命令行：
cd ascom/DarkLightDriver
dotnet build -c Release
```

或者用 Visual Studio 打开 `DarkLightDriver.csproj`，直接 Build。

## 安装

1. 确保已安装 ASCOM Platform 7+
2. 编译项目得到 `DarkLight.CoverCalibrator.dll`
3. **以管理员身份**运行 `Install.bat`

## 配置（Setup 界面）

在 ASCOM Chooser 中选中 "DarkLight Cover Calibrator"，点击 **Properties**：

### Serial Connection
- **COM Port** — 选择设备串口（可点击 ↻ 刷新）
- **Baud Rate** — 默认 115200（与固件一致）

### Primary Servo Angles（主舵机角度）
- **Open Angle** — 开盖时舵机目标角度 (0–180°)
- **Close Angle** — 关盖时舵机目标角度 (0–180°)

### Secondary Servo Angles（副舵机角度）
- **Open Angle** — 副舵机开盖角度 (0–180°)
- **Close Angle** — 副舵机关盖角度 (0–180°)

> 如果固件未编译 `SECONDARY_SERVO_INSTALLED`，副舵机命令会返回 `?`，不影响正常使用。

### Polling
- **Poll Interval** — 状态轮询间隔 (ms)，默认 1000ms

## 卸载

**以管理员身份**运行 `Uninstall.bat`。

## 串口协议

本驱动与 DLC 固件通过以下串口命令通信（`<` 和 `>` 包裹）：

| 命令 | 功能 |
|------|------|
| `O` / `C` / `H` | 开盖 / 关盖 / 停止 |
| `P` | 查询盖子状态 |
| `L` / `B` / `M` | 查询平场板状态 / 亮度 / 最大亮度 |
| `T<值>` / `F` | 开平场板 / 关平场板 |
| `UO<角度>` / `UC<角度>` | 设置主舵机开/关角度 |
| `VO<角度>` / `VC<角度>` | 设置副舵机开/关角度 |
| `uO` / `i` | 查询主舵机开/关角度 |
| `vO` / `vC` | 查询副舵机开/关角度 |
| `Z` | 握手（返回 `?`） |
| `V` | 查询固件版本 |

## 工作原理

1. **Setup 界面**配置角度 → 保存到 Windows 注册表 (ASCOM Profile)
2. 驱动**连接设备**时 → 自动将配置的角度推送到固件 EEPROM
3. 每次 **Open/Close** 命令前 → 重新同步角度确保一致
4. 固件 EEPROM 保存角度 → 即使不连接 ASCOM 也能记住上次的角度

## 故障排查

| 问题 | 解决方案 |
|------|---------|
| 设备连接失败 | 检查 COM 端口号和波特率 (115200) |
| 握手失败 (返回不是 `?`) | 确认固件已编译 `ENABLE_SERIAL_CONTROL` |
| 角度设置无效 | 确认已更新固件支持 `UO`/`UC` 等命令 |
| 副舵机角度不生效 | 检查固件是否编译了 `SECONDARY_SERVO_INSTALLED` |
| 驱动不出现在 ASCOM Chooser | 以管理员运行 `Install.bat` |
