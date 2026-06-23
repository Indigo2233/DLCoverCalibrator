# 自定义 ASCOM 驱动

源码位于 `ascom/DarkLightDriver/`，是从头编写的开源 ASCOM CoverCalibrator V2 驱动。

## 与原版驱动的对比

| 功能 | 原版驱动 (闭源) | 本驱动 (开源) |
|------|:---:|:---:|
| Cover 开/关/停 | ✓ | ✓ |
| 平场板亮度控制 | ✓ | ✓ |
| 除露加热控制 | ✓ | ✓ |
| 宽带/窄带亮度预设 | ✓ | ✓ |
| **主舵机开合角度配置** | ✗ | **✓** |
| **副舵机开合角度配置** | ✗ | **✓** |
| **源码可修改** | ✗ | **✓** |

## 项目结构

```
ascom/DarkLightDriver/
├── DarkLightDriver.csproj    # .NET Framework 4.8 项目
├── Driver.cs                 # ICoverCalibratorV2 主驱动
├── DeviceSerial.cs           # 串口通信封装
├── SetupDialogForm.cs        # 配置对话框
├── Properties/AssemblyInfo.cs
├── Install.bat               # 安装脚本
├── Uninstall.bat             # 卸载脚本
└── README.md
```

## 编译要求

- Windows 10/11
- [ASCOM Platform 7+](https://ascom-standards.org/)
- .NET Framework 4.8 SDK
- Visual Studio 2022 或 `dotnet` CLI

## 编译

```batch
cd ascom\DarkLightDriver
dotnet build -c Release
```

## 安装

1. **以管理员身份**打开命令行
2. 运行安装脚本：
   ```batch
   Install.bat
   ```
3. 驱动会注册到 COM，出现在 ASCOM Chooser 中

## 卸载

```batch
Uninstall.bat
```

## Setup 界面

在 ASCOM Chooser 中选中驱动，点击 **Properties** 打开配置：

### Serial Connection
- **COM Port** — 下拉选择 + ↻ 刷新按钮
- **Baud Rate** — 9600 / 19200 / 38400 / 57600 / **115200** / 230400

### Primary Servo Angles
- **Open Angle** — 开盖时舵机目标角度 (0–270°)
- **Close Angle** — 关盖时舵机目标角度 (0–270°)

### Secondary Servo Angles
- **Open Angle** — 副舵机开盖角度 (0–270°)
- **Close Angle** — 副舵机关盖角度 (0–270°)

### Polling
- **Poll Interval** — 状态轮询间隔 (500–10000 ms)

## 工作流程

1. 用户在 **Setup 界面**配置角度 → 保存到 Windows 注册表
2. 驱动 **连接设备**时 → 自动将角度推送到固件 EEPROM
3. 每次 **Open/Close** 命令前 → 重新同步确保角度一致
4. 驱动持续**轮询**设备状态，更新 `CoverState` / `CalibratorState`

## 故障排查

| 问题 | 解决方案 |
|------|---------|
| 驱动不在 Chooser 中 | 以管理员运行 `Install.bat` |
| 连接失败 | 检查 COM 端口和 115200 波特率 |
| 握手失败 | 固件需编译 `ENABLE_SERIAL_CONTROL` |
| 角度设置无效 | 更新固件支持 `UO`/`UC` 等命令 |
| 副舵机角度无响应 | 固件需编译 `SECONDARY_SERVO_INSTALLED` |

## 技术细节

- **接口**: `ICoverCalibratorV2` (ASCOM Platform 7)
- **通信**: Serial port, `<command>` 帧协议
- **持久化**: ASCOM Profile (Windows Registry)
- **轮询**: `System.Timers.Timer` 定时刷新状态
- **线程安全**: `lock` 保护串口操作
