# DarkLight Cover/Calibrator (DLC)

**DIY 电动天文望远镜盖 + 平场板** — 支持 ASCOM & INDI 全自动控制。

> 基于 [Nathan Woelfle 的原版设计](https://github.com/iwannabswiss/DarkLight_CoverCalibrator) 修改，遵循 CC BY-NC 4.0 协议。

---

## 三种构建方式

- **纯平场板** — 壁挂式校准面板（无需舵机）
- **纯电动盖** — 翻转盖，拍暗场和天光平场
- **Flip-Flat 组合** — 舵机 + 发光面板一体

---

## 🆕 改进亮点

| 改进 | 说明 |
|------|------|
| 🔧 **角度实时配置** | 不再需要刷固件！通过 ASCOM/INDI 软件直接调整开合角度 |
| 📡 **开源 ASCOM 驱动** | 从零编写，源码完全开放，Setup 界面配置角度 |
| 💾 **EEPROM 持久化** | 角度设置掉电保存，无需每次连接重新配置 |

---

## 📁 项目结构

```
├── dlc_firmware/          Arduino 固件
│   ├── dlc_firmware.ino   主程序
│   └── DLC_Library/       依赖库 (舵机/传感器/EEPROM)
├── ascom/                 ASCOM 驱动 (Windows)
│   ├── DarkLightDriver/   🆕 自定义开源驱动 (C#)
│   └── *.msi/*.exe        原版闭源驱动
├── indi/                  INDI 驱动 (Linux/macOS)
│   └── indi-dlc-src/      C++ 源码
├── schematics/            硬件原理图 & PCB
└── docs/                  📚 文档
    ├── getting-started.md
    ├── firmware.md
    ├── protocol.md
    ├── ascom-driver.md
    ├── indi-driver.md
    └── changelog.md
```

---

## 🚀 快速开始

### 1. 刷固件
用 Arduino IDE 打开 `dlc_firmware/dlc_firmware.ino`，按需修改配置后上传。详见 [固件指南](docs/firmware.md)。

### 2. 安装驱动

**Windows（自定义开源驱动）:**
```batch
cd ascom\DarkLightDriver
dotnet build -c Release
Install.bat
```
详见 [ASCOM 驱动指南](docs/ascom-driver.md)。

**Linux:**
```bash
cd indi/indi-dlc-src
mkdir build && cd build
cmake .. && make -j$(nproc) && sudo make install
```
详见 [INDI 驱动指南](docs/indi-driver.md)。

### 3. 配置角度（🆕）
- **ASCOM**: Properties → Setup 界面直接修改
- **INDI**: Options 面板修改角度数值
- 自动保存到 EEPROM，无需重新刷固件

详细串口协议见 [通信协议文档](docs/protocol.md)。

---

## 📜 许可

© 2020–2025 Nathan Woelfle (10th Tee Astronomy).  
修改部分 © 2025.

[CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) — 个人/学术用途自由使用，商用需授权。