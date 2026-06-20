# 改动记录

> 本仓库基于 [DarkLight_CoverCalibrator](https://github.com/iwannabswiss/DarkLight_CoverCalibrator) (by Nathan Woelfle)  
> 遵循 CC BY-NC 4.0 协议进行修改。

## 2025-06 — 舵机角度实时配置

### 问题
原版设计中，舵机开合角度（`primaryServoOpenCoverAngle`、`primaryServoCloseCoverAngle`）是固件编译时常量。每次调整角度都需要修改 `.ino` 文件重新编译烧录，非常不便。

### 改动

#### 固件 (`dlc_firmware/dlc_firmware.ino`)
- 角度变量从 `const` 改为可修改的 `uint8_t`
- 新增 4 个 EEPROM 存储位（掉电不丢失）
- 新增 8 条串口命令：`UO`/`UC`/`VO`/`VC`（设置角度）、`uO`/`i`/`vO`/`vC`（查询角度）
- 启动时自动从 EEPROM 加载角度，首次使用默认值

#### INDI 驱动 (`indi/indi-dlc-src/`)
- 新增 `PrimaryOpenAngleNP`、`PrimaryCloseAngleNP`、`SecondaryOpenAngleNP`、`SecondaryCloseAngleNP` 四个属性
- 支持 INDI 面板直接修改角度
- 连接时自动同步角度到设备
- 角度值持久化到 INDI 配置

#### 🆕 自定义 ASCOM 驱动 (`ascom/DarkLightDriver/`)
- 从头编写开源 ASCOM CoverCalibrator V2 驱动（原版无源码）
- Setup 界面中直接配置角度
- 连接时自动同步到固件 EEPROM
- 完整支持 Cover / Calibrator 所有标准接口
- .NET Framework 4.8 / C# 实现

### 串口协议新增命令

| 命令 | 功能 |
|------|------|
| `UO<N>` / `UC<N>` | 设置主舵机开/关角度 |
| `VO<N>` / `VC<N>` | 设置副舵机开/关角度 |
| `uO` / `i` | 查询主舵机开/关角度 |
| `vO` / `vC` | 查询副舵机开/关角度 |

详见 [串口协议文档](protocol.md)。
