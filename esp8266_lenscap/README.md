# ESP8266 电动镜头盖 — 精简版

基于 ESP8266 + 舵机的无线电动天文望远镜盖。
**完全兼容 DLC ASCOM/INDI 驱动。**

## 特性

| 功能 | 说明 |
|------|------|
| 📱 **Web 控制** | 手机/电脑浏览器打开网页即可开关盖子 |
| 🔘 **物理按键** | 短按切换开关状态（可选，不接也行） |
| 🔌 **ASCOM/INDI** | ✅ 完整 DLC 串口协议，直接配合现有驱动使用 |
| 💾 **角度持久化** | EEPROM 保存开合角度，ASCOM Setup 界面修改自动保存 |
| 📡 **WiFi AP** | 自带热点，野外无路由器也能用 |
| ⚡ **平滑移动** | 线性插值缓动，可设移动时间 |

## ASCOM/INDI 兼容性

| 命令组 | 状态 | 说明 |
|--------|:----:|------|
| 盖子 O/C/H/P | ✅ 完整支持 | 开关盖、停止、状态查询 |
| 角度 UO/UC/uO/i | ✅ 完整支持 | ASCOM Setup 里直接改角度，自动写入 EEPROM |
| 平场板 L/B/M/T/F | ✅ 兼容应答 | 本设备无平场板，返回 NotPresent/0 |
| 加热器 R/W/Y | ✅ 兼容应答 | 本设备无加热器，返回 NotPresent/0 |
| 系统 V/Z | ✅ 完整支持 | 版本查询、握手 |

> 在 NINA / SGPro / KStars 中直接选择 "DarkLight Cover Calibrator" 驱动即可使用。
> ASCOM 驱动轮询平场板和加热器状态时不会报错。

## 硬件接线

```
Wemos D1 Mini / NodeMCU      外部器件
────────────────────────     ──────────
D2 (GPIO4)  ──────────────   舵机 信号线 (橙/黄)
5V / VIN    ──────────────   舵机 电源 (红)  ← 必须独立供电
GND         ──────────────   舵机 GND  (棕)
GND         ──────────────   电源 GND
D1 (GPIO5)  ───[按键]─── GND
D3 (GPIO0)  ───[LED]──── GND  (可选，状态指示)
```

> ⚠️ **重要**: 舵机电源必须接 **5V pin** 或外部 5V 电源，**不要接 3.3V pin**！
> MG996R 等大扭矩舵机建议独立 5V/2A 供电。

## 固件配置

打开 `esp8266_lenscap.ino`，修改顶部 `USER CONFIGURATION` 区域：

```cpp
// WiFi 热点名称和密码
const char* apSSID = "LensCap";
const char* apPassword = "telescope";  // 至少 8 位，空字符串 = 不加密

// 舵机角度 (0-180)
const uint16_t OPEN_ANGLE = 0;      // 开盖角度
const uint16_t CLOSE_ANGLE = 180;   // 关盖角度
const uint16_t MOVE_TIME_MS = 2000; // 开合时间 (毫秒)

// 舵机脉冲范围 (查舵机说明书)
const uint16_t SERVO_MIN_US = 500;
const uint16_t SERVO_MAX_US = 2500;
```

## 刷写步骤

1. **安装 ESP8266 开发板支持**  
   Arduino IDE → 文件 → 首选项 → 附加开发板管理器网址，添加：  
   `http://arduino.esp8266.com/stable/package_esp8266com_index.json`  
   然后：工具 → 开发板 → 开发板管理器 → 搜索 `ESP8266` → 安装

2. **选择开发板**  
   工具 → 开发板 → ESP8266 → **LOLIN(WEMOS) D1 R2 & mini**

3. **安装依赖库**  
   工具 → 管理库 → 搜索并安装：
   - `ESP8266WiFi` (自带)
   - `ESP8266WebServer` (自带)
   - `Servo` (自带，但 ESP8266 版在 ESP8266 包中)

4. **上传**  
   连接 USB，选择端口，点击上传。

## 使用

### 方式一：手机 Web 控制 (推荐)

1. 上电后 ESP8266 创建热点 `LensCap`（密码 `telescope`）
2. 手机连接该 WiFi
3. 浏览器打开 **http://192.168.4.1**
4. 点击按钮开关盖子

### 方式二：物理按键

按一下按键 → 切换开关状态（关→开，开→关）

### 方式三：串口命令 (ASCOM/INDI)

通过 USB 串口发送命令（115200 baud）：

| 命令 | 功能 |
|------|------|
| `<O>` | 开盖 |
| `<C>` | 关盖 |
| `<H>` | 立即停止 |
| `<P>` | 查询状态 (1=关,2=移动中,3=开,5=错误) |

此协议兼容 DLC 固件子集，可直接配合 ASCOM/INDI 驱动使用。

## 结构设计建议

### 3D 打印件

- [Thingiverse: Telescope Lens Cap Servo Mount](https://www.thingiverse.com/search?q=telescope+lens+cap+servo)
- 设计要点：舵机固定在镜筒侧面，连杆连接盖子

### 简易方案 (无需 3D 打印)

```
舵机 ──[连杆]── 盖子 (3D 打印件 / 亚克力切割 / 硬纸板+黑色植绒)
  │
  ├── 用抱箍/扎带固定在望远镜镜筒上
  └── 舵机转轴与盖子轴心对齐
```

## 可选增强

| 功能 | 实现方式 |
|------|----------|
| 接入家庭 WiFi | 取消 `WIFI_STA_MODE` 注释，填写路由器 SSID/密码 |
| MQTT 远程控制 | 添加 `PubSubClient` 库，接入 Home Assistant |
| 平场板集成 | 加 LED 灯板 + MOSFET，用 GPIO 控制亮度 |
| 电池供电 | 2S 锂电池 (7.4V) + LM2596 降压模块 → 5V |
| 低功耗 | 加 `ESP.deepSleep()`，按键唤醒 |
