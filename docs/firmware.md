# 固件配置指南

固件文件：`dlc_firmware/dlc_firmware.ino`

## 当前 DLC 板烧录信息

当前通过 CH340 串口连接的 DLC 板实际使用 **ATmega328PB**。应通过
[MiniCore](https://github.com/MCUdude/MiniCore) 编译和上传；Arduino Nano 的
`arduino:avr:nano` 配置无法与该板的 bootloader 正常握手。

- 开发板索引：`https://mcudude.github.io/MiniCore/package_MCUdude_MiniCore_index.json`
- MiniCore 版本：`3.1.2`
- 开发板 FQBN：`MiniCore:avr:328:variant=modelPB`
- 默认配置：外部 16 MHz、UART0 bootloader、保留 EEPROM
- 当前 Windows 串口：`COM3`（端口号可能随 USB 接口变化）
- 固件串口速率：`115200`

安装 MiniCore：

```powershell
arduino-cli core update-index --additional-urls "https://mcudude.github.io/MiniCore/package_MCUdude_MiniCore_index.json"
arduino-cli core install MiniCore:avr --additional-urls "https://mcudude.github.io/MiniCore/package_MCUdude_MiniCore_index.json"
```

编译并烧录：

```powershell
arduino-cli compile --fqbn MiniCore:avr:328:variant=modelPB --libraries dlc_firmware/DLC_Library dlc_firmware
arduino-cli upload -p COM3 --fqbn MiniCore:avr:328:variant=modelPB dlc_firmware
```

烧录完成后，可通过串口发送 `<V>` 验证固件响应。2026-07-27 的烧录结果为
`<v1.2.0>`。

## 配置选项一览

固件顶部 **User-Adjustable Options (UA)** 区域可以调整所有参数：

### 功能开关

```cpp
#define COVER_INSTALLED        // 启用盖子（舵机）
#define LIGHT_INSTALLED        // 启用平场板
#define HEATER_INSTALLED       // 启用除露加热
#define ENABLE_SERIAL_CONTROL  // 启用串口控制（ASCOM/INDI）
#define ENABLE_MANUAL_CONTROL  // 启用手动物理按钮
#define ENABLE_SAVING_TO_MEMORY // 启用 EEPROM 掉电保存
```

> 不需要的功能直接注释掉即可，固件会自动调整状态上报。

### 串口速率

```cpp
const uint32_t serialSpeed = 115200;
```

可选值：`9600`、`19200`、`38400`、`57600`、`115200`（默认）、`230400`

### 舵机参数

```cpp
// 脉冲宽度 — 查阅舵机说明书设置
const uint16_t primaryServoMinPulseWidth = 500;   // μs
const uint16_t primaryServoMaxPulseWidth = 2500;  // μs

// 开合角度 — 可通过 ASCOM/INDI 实时修改，自动保存到 EEPROM
uint16_t primaryServoOpenCoverAngle = 0;    // 开盖角度 (0–270)
uint16_t primaryServoCloseCoverAngle = 180; // 关盖角度 (0–270)
```

### 盖子移动

```cpp
const uint32_t timeToMoveCover = 5000;  // 开合全程时间 (ms)
```

### 缓动曲线

7 种可选，只取消注释一个：
| 宏 | 效果 |
|----|------|
| `USE_LINEAR` | 匀速（默认） |
| `USE_QUAD` | 二次缓动 |
| `USE_CUBIC` | 三次缓动 |
| `USE_QUART` | 四次缓动 |
| `USE_QUINT` | 五次缓动 |
| `USE_SINE` | 正弦缓动 |
| `USE_EXPO` | 指数缓动 |
| `USE_CIRCULAR` | 圆形缓动 |

### 平场板

```cpp
uint8_t maxBrightness = 255;  // 亮度等级数（越大越细腻）
bool autoON = false;          // 仅手动模式：关盖时自动开灯
```

### 除露加热

```cpp
const uint32_t heaterShutoff = 3600000;  // 手动加热最长持续时间 (ms)
const float deltaPoint = 5.0;             // 目标温度高于环境温度 (°C)
```

### 副舵机

如果需要双舵机驱动：
```cpp
#define SECONDARY_SERVO_INSTALLED  // 取消注释
```

## 角度实时调整（新功能）

不再需要修改固件重新烧录！通过 ASCOM Setup 界面或 INDI Options 面板直接修改角度：

- **主舵机开盖角度** (`UO<N>`) — 0–270°
- **主舵机关盖角度** (`UC<N>`) — 0–270°
- **副舵机开盖角度** (`VO<N>`) — 0–270°（需启用副舵机）
- **副舵机关盖角度** (`VC<N>`) — 0–270°（需启用副舵机）

修改后自动保存到 EEPROM，断电不丢失。
