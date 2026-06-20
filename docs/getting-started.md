# DarkLight Cover Calibrator (DLC) — 快速入门

## 这是什么？

**DarkLight Cover Calibrator (DLC)** 是一个 DIY 电动天文望远镜盖 + 平场板的项目。你可以把它做成：

- **纯平场板** — 固定在墙上/望远镜前，用于拍摄平场（flat frame）
- **纯电动盖** — 舵机驱动的翻转盖，保护镜头+拍暗场（dark frame）
- **Flip-Flat 组合** — 盖子内侧集成发光面板，一机两用

## 硬件需求

| 组件 | 用途 |
|------|------|
| Arduino Nano/Uno | 主控 |
| 舵机 (SG90/MG996R 等) | 驱动盖子开合 |
| LED 平板 + 匀光片 | 平场光源 |
| DHT22 或 BME280 | 温湿度传感器（除露加热用） |
| DS18B20 ×2 | 温度传感器（加热器用） |
| MOSFET 模块 | 加热器功率控制 |
| 12V DC 电源 | 供电 |

完整 BOM 和接线图见 `schematics/` 目录。

## 快速开始

### 1. 刷固件

1. 用 Arduino IDE 打开 `dlc_firmware/dlc_firmware.ino`
2. 根据你的硬件配置修改 **User-Adjustable Options**：
   - 注释/取消注释 `COVER_INSTALLED`、`LIGHT_INSTALLED`、`HEATER_INSTALLED`
   - 设置舵机脉冲宽度范围（`primaryServoMinPulseWidth` / `primaryServoMaxPulseWidth`）
   - 选择缓动曲线（默认 `USE_LINEAR`）
3. 编译上传到 Arduino

> 💡 舵机开合角度**不再需要**在固件中硬编码！可以通过 ASCOM/INDI 软件实时调整，自动保存到 EEPROM。

### 2. 安装驱动

**Windows (ASCOM):**
```batch
# 先安装 ASCOM Platform 7+
# 编译自定义驱动
cd ascom\DarkLightDriver
dotnet build -c Release
# 以管理员运行
Install.bat
```

**Linux (INDI):**
```bash
cd indi/indi-dlc-src
mkdir build && cd build
cmake -DCMAKE_INSTALL_PREFIX=/usr -DCMAKE_BUILD_TYPE=Release ..
make -j$(nproc)
sudo make install
```

### 3. 连接使用

1. USB 连接 Arduino
2. 打开 ASCOM 客户端（NINA、SGPro 等）或 INDI 客户端（KStars/Ekos）
3. 选择 "DarkLight Cover Calibrator" 驱动
4. 在 Setup/Options 中配置串口和角度参数
5. 连接即可控制！

## 下一步

- [固件配置详解](firmware.md)
- [ASCOM 驱动使用](ascom-driver.md)
- [INDI 驱动使用](indi-driver.md)
- [串口通信协议](protocol.md)
