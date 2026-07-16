# DarkLight Cover Calibrator — Custom ASCOM Driver

## 1.1.0 连接方式

- `Serial`：Arduino Nano 或 ESP8266 USB 串口，默认 115200 baud
- `TCP`：ESP8266 的 STA/AP 地址，默认端口 `4030`

两种传输使用相同的 DLC `<命令>` 协议。TCP 模式下建议在路由器为设备保留固定 DHCP 地址。

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

在 ASCOM Chooser 中选中 "DarkLight Cover Calibrator"，点击 **Properties**。

界面采用 **Gemini FlatPanel 风格**，支持连接设备后的**实时角度微调**：

### Serial Connection（顶部工具栏）
- **COM Port** — 下拉选择 + ↻ 刷新
- **Baud Rate** — 9600~230400
- 连接状态指示灯 + 固件版本显示

### Panel 1 / Panel 2（主/副舵机）

每个面板包含两组控制：

**关闭方向** (橙色)
- 当前角度值（大字体）+ **±1° / ±10° / ±45°** 实时微调
- 「设为关闭位置」一键保存当前位置

**打开方向** (绿色)
- 当前角度值（大字体）+ **±1° / ±10° / ±45°** 实时微调
- 「设为打开位置」一键保存当前位置

> 如果固件未编译 `SECONDARY_SERVO_INSTALLED`，副舵机 jog 命令返回 `?`，不影响使用。

### 底部按钮
- **重设** — 恢复默认角度（开=0° 关=180°）
- **Done** — 保存并关闭

### 实时微调工作流
1. 连接设备后打开 Setup 界面
2. 点击 ±1°/±10°/±45° 按钮实时移动舵机到位
3. 点击「设为关闭/打开位置」保存
4. 角度自动写入固件 EEPROM，断电不丢失

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
| `UO<角度>` / `UC<角度>` | 设置主舵机开/关角度（写入 EEPROM） |
| `VO<角度>` / `VC<角度>` | 设置副舵机开/关角度 |
| `uO` / `i` | 查询主舵机开/关角度 |
| `vO` / `vC` | 查询副舵机开/关角度 |
| `J<角度>` / `j` | 🆕 Jog 主舵机直驱 / 查询当前位置 |
| `K<角度>` / `k` | 🆕 Jog 副舵机直驱 / 查询当前位置 |
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
