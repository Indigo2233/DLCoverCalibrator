# INDI 驱动

源码位于 `indi/indi-dlc-src/`，支持 Linux/macOS 上的 INDI 协议。

## 编译与安装

```bash
cd indi/indi-dlc-src
mkdir build && cd build
cmake -DCMAKE_INSTALL_PREFIX=/usr -DCMAKE_BUILD_TYPE=Release ..
make -j$(nproc)
sudo make install
```

## 依赖

- libindi (INDI 开发库)
- CMake 3.0+

Ubuntu/Debian 安装依赖：
```bash
sudo apt install libindi-dev cmake build-essential
```

## 属性面板

连接设备后在 INDI 控制面板可看到：

### Main Control 选项卡

| 属性 | 类型 | 说明 |
|------|------|------|
| Cover State | 只读文本 | 盖子状态 (Open/Closed/Moving/Error) |
| Open / Close / Halt | 开关按钮 | 控制盖子动作 |
| Light State | 只读文本 | 平场板状态 |
| On / Off | 开关按钮 | 开关平场板 |
| Max Brightness | 只读数字 | 最大亮度值 |
| Current Brightness | 只读数字 | 当前亮度 |
| Goto Brightness | 输入数字 | 跳转到指定亮度 |
| - / + | 按钮 | 步进调整亮度 |
| Broadband / Narrowband | 按钮 | 跳转到预设值 |
| Save as Broadband / Narrowband | 按钮 | 保存当前值为预设 |
| Heater State | 只读文本 | 加热器状态 |
| On / Off / Auto / Heat on Close | 开关按钮 | 加热控制 |

### Options 选项卡

| 属性 | 说明 |
|------|------|
| Stabilize Time | 平场板稳定时间 (ms) |
| Auto ON at CLOSE | 关盖时自动开灯 |
| Disable Light while OPEN | 开盖时禁用灯光 |
| Auto Heat (always ON) | 常开自动除露 |
| Heat ON at CLOSE | 关盖时开启加热 |
| 🆕 **Open Angle (0–180)** | **主舵机开盖角度** |
| 🆕 **Close Angle (0–180)** | **主舵机关盖角度** |
| 🆕 **2nd Open Angle (0–180)** | **副舵机开盖角度** |
| 🆕 **2nd Close Angle (0–180)** | **副舵机关盖角度** |

## KStars / Ekos 集成

1. 启动 INDI 服务
2. 在 Ekos 中配置设备
3. 选择 "DarkLight Cover Calibrator"
4. 平场拍摄时自动调用 `CalibratorOn()`
5. 暗场拍摄时自动调用 `CloseCover()` + `CalibratorOff()`

## 架构

```
Ekos/KStars
    │ INDI Protocol (TCP)
    ▼
indi-dlc driver (C++)
    │ Serial Protocol (<cmd>)
    ▼
Arduino DLC Firmware
    │ PWM / GPIO
    ▼
Servo / LED / Heater
```

驱动类 `DarkLight_CoverCalibrator` 继承 `INDI::DefaultDevice`，通过 `Connection::Serial` 管理串口连接。
