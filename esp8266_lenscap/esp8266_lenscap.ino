/*
  Name:    ESP8266 Lens Cap Controller
  Board:   Wemos D1 Mini / NodeMCU (ESP8266)
  Author:  Generated for DLCoverCalibrator project
  Date:    2025-07-06
  Version: 1.2.0

  Description: WiFi-controlled telescope lens cap using a servo.
               - Built-in web server for phone/browser control
               - Physical button for manual toggle
               - FULL DLC serial protocol support — works with ASCOM/INDI drivers
               - EEPROM-backed angle persistence

  Hardware:
    - Wemos D1 Mini (ESP8266)
    - Servo: signal -> D2 (GPIO4), power -> external 5V
    - Button: D1 (GPIO5) -> GND (internal pullup)
    - Optional LED: D3 (GPIO0) -> GND (active LOW on some boards, check your model)

  Power: Servo MUST be powered from external 5V, NOT from 3.3V pin.
         When powered via USB, the 5V pin provides USB power (sufficient for SG90).
         For MG996R or larger servos, use a separate 5V/2A supply.

  WiFi:  Creates a unique "LensCap-<chipid>" AP and optionally joins a STA network.
         Open http://192.168.4.1 for control and /config for STA configuration.

  ASCOM/INDI Compatibility:
    This firmware implements the FULL DLC serial protocol.
    The ASCOM driver polls P (cover), L (calibrator), B (brightness),
    M (max brightness), R (heater) every cycle.
    The flat-panel output supports L/B/M/T/F commands with 0-255 PWM brightness.
    Heater and environmental sensor commands return compatible placeholder values.
    USB serial and TCP port 4030 share the same framed DLC protocol.
*/

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <Servo.h>
#include <EEPROM.h>

// ==================== USER CONFIGURATION ====================

// --- WiFi Settings ---
// AP mode (default): ESP creates its own WiFi network
char apSSID[18] = {0};                // LensCap-<6-digit chip ID>
char deviceId[7] = {0};
const char* apPassword = "telescope";  // at least 8 chars; "" = open network

// STA mode is configured at runtime from http://192.168.4.1/config.
// The AP remains available while STA is enabled so configuration can be recovered.

// --- Servo Settings ---
const uint8_t SERVO_PIN = 4;          // D2 (GPIO4) on Wemos D1 Mini
const uint16_t SERVO_MIN_US = 500;    // min pulse width (μs) — check servo datasheet
const uint16_t SERVO_MAX_US = 2500;   // max pulse width (μs)
uint16_t openAngle = 0;               // servo angle when cap is OPEN (0-270) *adjustable via ASCOM
uint16_t closeAngle = 180;            // servo angle when cap is CLOSED (0-270) *adjustable via ASCOM
const uint16_t MOVE_TIME_MS = 10000;  // time to complete open/close movement (10s)
const uint16_t JOG_TIME_PER_270_DEG_MS = 10000;  // 10s for a full 270-degree jog
const uint16_t SERVO_UPDATE_INTERVAL_MS = 20;    // update pulse target at 50Hz

// --- Button Settings ---
const uint8_t BUTTON_PIN = 5;         // D1 (GPIO5), button to GND, uses internal pullup
const uint16_t DEBOUNCE_MS = 200;     // debounce time

// --- LED Settings (optional) ---
const uint8_t LED_PIN = 0;            // D3 (GPIO0), active LOW on most Wemos boards
// #define LED_ACTIVE_HIGH             // uncomment if your LED is active HIGH

// --- Flat Panel Light Settings ---
const uint8_t LIGHT_PIN = 14;        // D5 (GPIO14), PWM output for flat panel LED
const uint8_t MAX_BRIGHTNESS = 255;  // 0-255 steps
const uint32_t LIGHT_PWM_FREQ = 4000; // 4kHz PWM

// --- Serial Settings ---
const uint32_t SERIAL_BAUD = 115200;  // for USB serial control (ASCOM/INDI)
const uint16_t TCP_PORT = 4030;       // WiFi ASCOM/INDI control

// --- EEPROM ---
#define EEPROM_MAGIC 0xDA            // magic byte to check if EEPROM is initialized
#define EEPROM_ADDR_MAGIC 0
#define EEPROM_ADDR_OPEN_LOW  1
#define EEPROM_ADDR_OPEN_HIGH 2
#define EEPROM_ADDR_CLOSE_LOW 3
#define EEPROM_ADDR_CLOSE_HIGH 4
#define EEPROM_ADDR_STATE     5      // last known cover state (1=Closed, 3=Open)
#define EEPROM_SIZE           128
#define EEPROM_WIFI_MAGIC     0x57494649UL  // "WIFI"
#define EEPROM_ADDR_WIFI_MAGIC   16
#define EEPROM_ADDR_WIFI_ENABLED 20
#define EEPROM_ADDR_WIFI_SSID    21
#define EEPROM_ADDR_WIFI_PASS    54
#define WIFI_SSID_MAX_LEN        32
#define WIFI_PASS_MAX_LEN        63

// ==================== END OF USER CONFIGURATION ====================

// --- Global State ---
Servo capServo;
ESP8266WebServer server(80);
WiFiServer tcpServer(TCP_PORT);
WiFiClient tcpClient;

enum CoverState {
  COVER_CLOSED = 1,
  COVER_MOVING = 2,
  COVER_OPEN   = 3,
  COVER_ERROR  = 5
};

CoverState currentState = COVER_CLOSED;
unsigned long moveStartTime = 0;
bool isMoving = false;
bool movingToOpen = false;  // track direction: true=opening, false=closing
uint16_t targetAngle = 0;
uint16_t currentServoAngle = 0;
uint16_t currentServoPulseUs = SERVO_MIN_US;
uint16_t movementStartPulseUs = SERVO_MIN_US;
unsigned long lastServoUpdateTime = 0;

// --- Jog state (smooth temporary move) ---
bool jogActive = false;
uint16_t jogStartAngle = 0;
uint16_t jogTargetAngle = 0;
uint16_t jogStartPulseUs = SERVO_MIN_US;
unsigned long jogStartTime = 0;
uint32_t jogDurationMs = 0;

// --- Flat Panel Light State ---
uint8_t lightBrightness = 0;          // current brightness (0-255)
bool lightOn = false;

// --- Button ---
unsigned long lastButtonCheck = 0;
bool lastButtonState = HIGH;

// --- Serial Command Buffer (DLC-compatible subset) ---
const uint8_t MAX_SERIAL_CHARS = 16;
char serialBuffer[MAX_SERIAL_CHARS];
uint8_t serialIndex = 0;
bool serialComplete = false;
char tcpBuffer[MAX_SERIAL_CHARS];
uint8_t tcpIndex = 0;

// --- Runtime WiFi Settings ---
bool staEnabled = false;
char staSSID[WIFI_SSID_MAX_LEN + 1] = {0};
char staPassword[WIFI_PASS_MAX_LEN + 1] = {0};
bool restartPending = false;
unsigned long restartAt = 0;

// ==================== SETUP ====================

void setup() {
  // --- Servo pin: hold LOW IMMEDIATELY to prevent twitch during boot ---
  digitalWrite(SERVO_PIN, LOW);
  pinMode(SERVO_PIN, OUTPUT);
  digitalWrite(SERVO_PIN, LOW);

  Serial.begin(SERIAL_BAUD);
  Serial.println();
  Serial.println(F("ESP8266 Lens Cap Controller v1.2"));

  snprintf(deviceId, sizeof(deviceId), "%06X", ESP.getChipId());
  snprintf(apSSID, sizeof(apSSID), "LensCap-%s", deviceId);

  // --- EEPROM ---
  EEPROM.begin(EEPROM_SIZE);
  loadAnglesFromEEPROM();
  loadWiFiSettings();

  // Read saved state early (needed for initAngle) but DON'T attach servo yet.
  // GPIO4 stays OUTPUT LOW during WiFi init to prevent boot twitch.
  uint8_t savedState = EEPROM.read(EEPROM_ADDR_STATE);
  currentState = (savedState == COVER_OPEN) ? COVER_OPEN : COVER_CLOSED;
  uint16_t initAngle = (currentState == COVER_OPEN) ? openAngle : closeAngle;
  targetAngle = initAngle;  // track the known position

  // --- Flat Panel Light ---
  pinMode(LIGHT_PIN, OUTPUT);
  analogWrite(LIGHT_PIN, 0);        // init channel
  analogWriteFreq(LIGHT_PWM_FREQ);  // 4kHz

  // --- Button ---
  pinMode(BUTTON_PIN, INPUT_PULLUP);

  // --- LED ---
  pinMode(LED_PIN, OUTPUT);
  setLED(false);

  // --- WiFi ---
  startWiFi();

  // --- Web Server ---
  setupWebServer();
  server.begin();
  Serial.println(F("Web server started."));
  tcpServer.begin();
  tcpServer.setNoDelay(true);
  Serial.print(F("DLC TCP server started on port "));
  Serial.println(TCP_PORT);

  // --- Servo: attach AFTER WiFi init to avoid boot twitch ---
  capServo.attach(SERVO_PIN, SERVO_MIN_US, SERVO_MAX_US);
  moveToAngle(initAngle);
  Serial.println(F("Ready."));
}

void startWiFi() {
  WiFi.persistent(false);
  WiFi.mode(staEnabled ? WIFI_AP_STA : WIFI_AP);
  WiFi.hostname(apSSID);
  WiFi.softAP(apSSID, apPassword);
  Serial.print(F("AP Mode: "));
  Serial.print(apSSID);
  Serial.print(F(" | IP: "));
  Serial.println(WiFi.softAPIP());

  if (!staEnabled || staSSID[0] == '\0') return;

  WiFi.begin(staSSID, staPassword);
  Serial.print(F("Connecting STA to "));
  Serial.print(staSSID);
  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < 20) {
    delay(500);
    Serial.print('.');
    attempts++;
  }

  if (WiFi.status() == WL_CONNECTED) {
    Serial.print(F("\nSTA connected | IP: "));
    Serial.println(WiFi.localIP());
  } else {
    Serial.println(F("\nSTA connection failed; AP remains available."));
  }
}

// ==================== WEB SERVER ====================

void setupWebServer() {
  server.on("/", handleRoot);
  server.on("/open", handleOpen);
  server.on("/close", handleClose);
  server.on("/status", handleStatus);
  server.on("/toggle", handleToggle);
  server.on("/light/status", HTTP_GET, handleLightStatus);
  server.on("/light/set", HTTP_POST, handleLightSet);
  server.on("/light/off", HTTP_POST, handleLightOff);
  server.on("/config", HTTP_GET, handleConfig);
  server.on("/config/save", HTTP_POST, handleSaveConfig);
  server.on("/config/angles", HTTP_POST, handleSaveAngles);
  server.on("/config/position", HTTP_GET, handleCalibrationPosition);
  server.on("/config/jog", HTTP_POST, handleCalibrationJog);
  server.on("/config/save-position", HTTP_POST, handleSaveCalibrationPosition);
  server.onNotFound(handleRoot);
}

void handleRoot() {
  String html = R"rawliteral(
<!DOCTYPE html>
<html lang="zh">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no">
  <title>Lens Cap Control</title>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body {
      font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
      background: #1a1a2e; color: #eee; display: flex;
      justify-content: center; align-items: center; min-height: 100vh;
      padding: 20px;
    }
    .container {
      background: #16213e; border-radius: 20px; padding: 30px;
      max-width: 400px; width: 100%; text-align: center;
      box-shadow: 0 10px 40px rgba(0,0,0,0.5);
    }
    h1 { font-size: 1.6rem; margin-bottom: 8px; }
    .status {
      display: inline-block; padding: 6px 16px; border-radius: 20px;
      font-weight: bold; font-size: 0.9rem; margin: 12px 0 20px;
    }
    .status.open { background: #2ecc71; color: #fff; }
    .status.closed { background: #e74c3c; color: #fff; }
    .status.moving { background: #f39c12; color: #fff; animation: pulse 0.8s infinite; }
    @keyframes pulse { 0%,100%{opacity:1} 50%{opacity:0.5} }
    .btn {
      display: block; width: 100%; padding: 18px; margin: 12px 0;
      font-size: 1.2rem; font-weight: bold; border: none; border-radius: 12px;
      cursor: pointer; transition: transform 0.1s, opacity 0.2s;
      color: #fff;
    }
    .btn:active { transform: scale(0.96); }
    .btn:disabled { opacity: 0.4; pointer-events: none; }
    .btn-open { background: linear-gradient(135deg, #2ecc71, #27ae60); }
    .btn-close { background: linear-gradient(135deg, #e74c3c, #c0392b); }
    .btn-toggle { background: linear-gradient(135deg, #3498db, #2980b9); }
    .panel { margin-top: 22px; padding-top: 18px; border-top: 1px solid #34415f; }
    .panel h2 { font-size: 1.1rem; margin-bottom: 12px; }
    .light-row { display: flex; gap: 10px; align-items: center; }
    .light-row input[type=range] { flex: 1; }
    .light-value { width: 58px; padding: 7px; border: 1px solid #405174; border-radius: 7px; background: #101a33; color: #fff; text-align: center; }
    .light-actions { display: flex; gap: 10px; margin-top: 10px; }
    .light-actions button { flex: 1; padding: 11px; border: 0; border-radius: 9px; color: #fff; font-weight: bold; }
    .light-on { background: #9b59b6; } .light-off { background: #596275; }
    .info { font-size: 0.8rem; color: #888; margin-top: 20px; }
    .nav { display: inline-block; color: #8ab4f8; margin-top: 18px; text-decoration: none; }
  </style>
</head>
<body>
  <div class="container">
    <h1>🔭 电动镜头盖</h1>
    <div id="status" class="status closed">● 已关闭</div>
    <button class="btn btn-open" onclick="fetch('/open');updateStatus();" id="btnOpen">📂 开盖</button>
    <button class="btn btn-close" onclick="fetch('/close');updateStatus();" id="btnClose">📁 关盖</button>
    <button class="btn btn-toggle" onclick="fetch('/toggle');updateStatus();">🔄 切换</button>
    <div class="panel">
      <h2>平场灯</h2>
      <div class="light-row">
        <input id="lightSlider" type="range" min="1" max="255" value="128" oninput="syncLightValue(this.value)">
        <input id="lightValue" class="light-value" type="number" min="1" max="255" value="128" oninput="syncLightSlider(this.value)">
      </div>
      <div class="light-actions">
        <button class="light-on" onclick="turnLightOn()">开灯 / 应用亮度</button>
        <button class="light-off" onclick="turnLightOff()">关灯</button>
      </div>
      <div id="lightStatus" class="info">平场灯已关闭</div>
    </div>
    <a class="nav" href="/config">设备配置</a>
    <div class="info">设备 ID：{{DEVICE_ID}}<br>AP：{{AP_SSID}}<br>Lens Cap Controller | ESP8266</div>
  </div>
  <script>
    async function getStatus() {
      try {
        const r = await fetch('/status');
        const s = await r.text();
        const statusEl = document.getElementById('status');
        const btnOpen = document.getElementById('btnOpen');
        const btnClose = document.getElementById('btnClose');
        if (s == '3') {
          statusEl.textContent = '● 已打开'; statusEl.className = 'status open';
          btnOpen.disabled = true; btnClose.disabled = false;
        } else if (s == '1') {
          statusEl.textContent = '● 已关闭'; statusEl.className = 'status closed';
          btnOpen.disabled = false; btnClose.disabled = true;
        } else if (s == '2') {
          statusEl.textContent = '◉ 移动中...'; statusEl.className = 'status moving';
          btnOpen.disabled = true; btnClose.disabled = true;
        }
      } catch(e) {}
    }
    function updateStatus() { setTimeout(getStatus, 500); }
    function syncLightValue(v) { document.getElementById('lightValue').value = v; }
    function syncLightSlider(v) {
      v = Math.max(1, Math.min(255, Number(v) || 1));
      document.getElementById('lightSlider').value = v;
    }
    async function getLightStatus() {
      try {
        const s = await (await fetch('/light/status')).json();
        document.getElementById('lightStatus').textContent = s.on ? `平场灯已开启 · 亮度 ${s.brightness}` : '平场灯已关闭';
        if (s.brightness > 0) {
          document.getElementById('lightSlider').value = s.brightness;
          document.getElementById('lightValue').value = s.brightness;
        }
      } catch(e) {}
    }
    async function turnLightOn() {
      const value = Math.max(1, Math.min(255, Number(document.getElementById('lightValue').value) || 1));
      await fetch(`/light/set?brightness=${value}`, {method:'POST'}); getLightStatus();
    }
    async function turnLightOff() { await fetch('/light/off', {method:'POST'}); getLightStatus(); }
    setInterval(getStatus, 3000);
    setInterval(getLightStatus, 3000);
    getStatus();
    getLightStatus();
  </script>
</body>
</html>
)rawliteral";
  html.replace("{{DEVICE_ID}}", deviceId);
  html.replace("{{AP_SSID}}", apSSID);
  server.send(200, "text/html; charset=utf-8", html);
}

String htmlEscape(const char* value) {
  String escaped;
  escaped.reserve(strlen(value) + 8);
  for (const char* p = value; *p; ++p) {
    switch (*p) {
      case '&': escaped += F("&amp;"); break;
      case '<': escaped += F("&lt;"); break;
      case '>': escaped += F("&gt;"); break;
      case '"': escaped += F("&quot;"); break;
      case '\'': escaped += F("&#39;"); break;
      default: escaped += *p; break;
    }
  }
  return escaped;
}

void handleConfig() {
  String html;
  html.reserve(7600);
  html += F("<!DOCTYPE html><html lang='zh'><head><meta charset='UTF-8'>");
  html += F("<meta name='viewport' content='width=device-width,initial-scale=1'>");
  html += F("<title>Lens Cap 设备配置</title><style>");
  html += F("*{box-sizing:border-box}body{margin:0;padding:20px;min-height:100vh;background:#1a1a2e;color:#eee;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;display:flex;justify-content:center;align-items:center}");
  html += F(".card{width:100%;max-width:460px;background:#16213e;border-radius:18px;padding:28px;box-shadow:0 10px 40px rgba(0,0,0,.45)}h1{font-size:1.45rem;margin:0 0 20px}h2{font-size:1.15rem;margin:24px 0 12px}.row{margin:16px 0}label{display:block;margin-bottom:7px;color:#c8d2e6}input[type=text],input[type=password],input[type=number]{width:100%;padding:12px;border:1px solid #405174;border-radius:9px;background:#101a33;color:#fff;font-size:1rem}.check{display:flex;gap:9px;align-items:center}.check label{margin:0}.status{padding:12px;background:#101a33;border-radius:9px;line-height:1.65;color:#b8c4d8}.btn{width:100%;padding:14px;border:0;border-radius:10px;background:#2980b9;color:#fff;font-size:1rem;font-weight:700;cursor:pointer}.jog-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:8px;margin:12px 0}.jog-grid button,.save-row button{padding:11px 6px;border:0;border-radius:8px;background:#405174;color:#fff;font-weight:700}.save-row{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-top:12px}.position{font-size:1.6rem;font-weight:700;text-align:center;color:#8ab4f8;margin:12px 0}.nav{display:block;text-align:center;color:#8ab4f8;margin-top:18px;text-decoration:none}.hint{font-size:.82rem;color:#8f9bb2;margin-top:6px}</style></head><body><div class='card'>");
  html += F("<h1>设备配置</h1><div class='status'>设备 ID：");
  html += deviceId;
  html += F("<br>设备 AP：");
  html += apSSID;
  html += F("<br>AP 地址：");
  html += WiFi.softAPIP().toString();
  html += F("<br>STA 状态：");
  html += (WiFi.status() == WL_CONNECTED) ? F("已连接") : (staEnabled ? F("连接失败") : F("未启用"));
  if (WiFi.status() == WL_CONNECTED) {
    html += F("<br>STA 地址：");
    html += WiFi.localIP().toString();
  }
  html += F("</div><h2>网络</h2><form method='post' action='/config/save'>");
  html += F("<div class='row check'><input id='enabled' name='enabled' type='checkbox' value='1'");
  if (staEnabled) html += F(" checked");
  html += F("><label for='enabled'>启用 STA 模式</label></div>");
  html += F("<div class='row'><label for='ssid'>WiFi 名称（SSID）</label><input id='ssid' name='ssid' type='text' maxlength='32' value=\"");
  html += htmlEscape(staSSID);
  html += F("\" autocomplete='off'></div>");
  html += F("<div class='row'><label for='password'>WiFi 密码</label><input id='password' name='password' type='password' maxlength='63' placeholder='留空则保持现有密码' autocomplete='new-password'><div class='hint'>首次配置开放网络时可以留空。</div></div>");
  html += F("<div class='row check'><input id='clearPassword' name='clearPassword' type='checkbox' value='1'><label for='clearPassword'>清除已保存密码</label></div>");
  html += F("<button class='btn' type='submit'>保存网络并重启</button></form>");
  html += F("<section style='margin-top:26px;padding-top:4px;border-top:1px solid #34415f'><h2>盖板位置校准</h2>");
  html += F("<div id='position' class='position'>--°</div><div class='status'>开盖位置：<span id='openPosition'>--</span>°<br>关盖位置：<span id='closePosition'>--</span>°</div>");
  html += F("<div class='row'><label for='jogTarget'>目标位置（0–270°）</label><input id='jogTarget' type='number' min='0' max='270' value='0'></div><button class='btn' type='button' onclick='jogToTarget()'>移动到目标位置</button>");
  html += F("<div class='jog-grid'><button type='button' onclick='jogBy(-45)'>-45°</button><button type='button' onclick='jogBy(-10)'>-10°</button><button type='button' onclick='jogBy(-1)'>-1°</button><button type='button' onclick='jogBy(45)'>+45°</button><button type='button' onclick='jogBy(10)'>+10°</button><button type='button' onclick='jogBy(1)'>+1°</button></div>");
  html += F("<div class='save-row'><button type='button' onclick=\"savePosition('open')\">保存为开盖位置</button><button type='button' onclick=\"savePosition('close')\">保存为关盖位置</button></div><div id='calibrationMessage' class='hint'></div></section>");
  html += F("<a class='nav' href='/'>返回控制页面</a><script>let currentPosition=0,targetInitialized=false,refreshInFlight=false;async function refreshCalibration(){if(refreshInFlight)return;refreshInFlight=true;try{const s=await(await fetch('/config/position')).json();currentPosition=s.position;document.getElementById('position').textContent=`${s.position}°${s.moving?' · 移动中':''}`;document.getElementById('openPosition').textContent=s.open;document.getElementById('closePosition').textContent=s.close;if(!targetInitialized){document.getElementById('jogTarget').value=s.position;targetInitialized=true}}catch(e){}finally{refreshInFlight=false}}async function jog(angle){angle=Math.max(0,Math.min(270,Math.round(angle)));document.getElementById('jogTarget').value=angle;const r=await fetch(`/config/jog?angle=${angle}`,{method:'POST'});document.getElementById('calibrationMessage').textContent=await r.text();refreshCalibration()}function jogToTarget(){jog(Number(document.getElementById('jogTarget').value)||0)}function jogBy(delta){jog(currentPosition+delta)}async function savePosition(kind){const r=await fetch(`/config/save-position?kind=${kind}`,{method:'POST'});document.getElementById('calibrationMessage').textContent=await r.text();refreshCalibration()}async function pollCalibration(){await refreshCalibration();setTimeout(pollCalibration,500)}pollCalibration();</script></div></body></html>");
  server.send(200, "text/html; charset=utf-8", html);
}

void handleSaveConfig() {
  bool enabled = server.hasArg("enabled") && server.arg("enabled") == "1";
  String ssid = server.arg("ssid");
  ssid.trim();

  if (enabled && ssid.length() == 0) {
    server.send(400, "text/plain; charset=utf-8", "启用 STA 时必须填写 SSID");
    return;
  }

  staEnabled = enabled;
  ssid.toCharArray(staSSID, sizeof(staSSID));

  if (server.hasArg("clearPassword")) {
    staPassword[0] = '\0';
  } else {
    String password = server.arg("password");
    if (password.length() > 0) password.toCharArray(staPassword, sizeof(staPassword));
  }

  saveWiFiSettings();
  server.send(200, "text/html; charset=utf-8",
              "<!DOCTYPE html><html lang='zh'><meta charset='UTF-8'><meta name='viewport' content='width=device-width,initial-scale=1'><body style='font-family:sans-serif;background:#1a1a2e;color:#eee;text-align:center;padding:60px 20px'><h2>配置已保存</h2><p>设备正在重启。AP 地址保持为 192.168.4.1。</p></body></html>");
  restartPending = true;
  restartAt = millis() + 1200;
}

void handleSaveAngles() {
  if (!server.hasArg("openAngle") || !server.hasArg("closeAngle")) {
    server.send(400, "text/plain; charset=utf-8", "缺少开盖或关盖位置");
    return;
  }

  int newOpenAngle = server.arg("openAngle").toInt();
  int newCloseAngle = server.arg("closeAngle").toInt();
  if (newOpenAngle < 0 || newOpenAngle > 270 || newCloseAngle < 0 || newCloseAngle > 270) {
    server.send(400, "text/plain; charset=utf-8", "角度必须在 0–270 范围内");
    return;
  }
  if (newOpenAngle == newCloseAngle) {
    server.send(400, "text/plain; charset=utf-8", "开盖和关盖位置不能相同");
    return;
  }

  openAngle = (uint16_t)newOpenAngle;
  closeAngle = (uint16_t)newCloseAngle;
  saveAnglesToEEPROM();
  server.sendHeader("Location", "/config");
  server.send(303, "text/plain", "Saved");
}

void handleCalibrationPosition() {
  String json = String(F("{\"position\":")) + currentServoAngle +
                F(",\"open\":") + openAngle + F(",\"close\":") + closeAngle +
                F(",\"moving\":") + ((isMoving || jogActive) ? F("true") : F("false")) + '}';
  server.send(200, "application/json", json);
}

void handleCalibrationJog() {
  if (isMoving) {
    server.send(409, "text/plain; charset=utf-8", "盖板正在执行开关动作，请等待完成");
    return;
  }
  if (!server.hasArg("angle")) {
    server.send(400, "text/plain; charset=utf-8", "缺少目标位置");
    return;
  }

  int angle = server.arg("angle").toInt();
  if (angle < 0 || angle > 270) {
    server.send(400, "text/plain; charset=utf-8", "目标位置必须在 0–270 范围内");
    return;
  }

  startJog((uint16_t)angle);
  server.send(200, "text/plain; charset=utf-8", "正在平滑移动到目标位置");
}

void handleSaveCalibrationPosition() {
  if (isMoving || jogActive) {
    server.send(409, "text/plain; charset=utf-8", "请等待点动完成后再保存位置");
    return;
  }
  if (!server.hasArg("kind")) {
    server.send(400, "text/plain; charset=utf-8", "缺少位置类型");
    return;
  }

  String kind = server.arg("kind");
  if (kind == "open") {
    if (currentServoAngle == closeAngle) {
      server.send(400, "text/plain; charset=utf-8", "开盖位置不能与关盖位置相同");
      return;
    }
    openAngle = currentServoAngle;
    currentState = COVER_OPEN;
  } else if (kind == "close") {
    if (currentServoAngle == openAngle) {
      server.send(400, "text/plain; charset=utf-8", "关盖位置不能与开盖位置相同");
      return;
    }
    closeAngle = currentServoAngle;
    currentState = COVER_CLOSED;
  } else {
    server.send(400, "text/plain; charset=utf-8", "未知的位置类型");
    return;
  }

  saveAnglesToEEPROM();
  EEPROM.write(EEPROM_ADDR_STATE, (uint8_t)currentState);
  EEPROM.commit();
  server.send(200, "text/plain; charset=utf-8", kind == "open" ? "已保存为开盖位置" : "已保存为关盖位置");
}

void handleOpen() {
  if (currentState != COVER_OPEN) openCover();
  server.send(200, "text/plain", "OK");
}

void handleClose() {
  if (currentState != COVER_CLOSED) closeCover();
  server.send(200, "text/plain", "OK");
}

void handleToggle() {
  if (currentState == COVER_OPEN) closeCover();
  else if (currentState == COVER_CLOSED) openCover();
  server.send(200, "text/plain", "OK");
}

void handleStatus() {
  server.send(200, "text/plain", String((int)currentState));
}

void handleLightStatus() {
  String json = String(F("{\"on\":")) + (lightOn ? F("true") : F("false")) +
                F(",\"brightness\":") + lightBrightness + F(",\"max\":") + MAX_BRIGHTNESS + '}';
  server.send(200, "application/json", json);
}

void handleLightSet() {
  if (!server.hasArg("brightness")) {
    server.send(400, "text/plain; charset=utf-8", "缺少 brightness 参数");
    return;
  }
  lightBrightness = constrain(server.arg("brightness").toInt(), 1, MAX_BRIGHTNESS);
  analogWrite(LIGHT_PIN, lightBrightness);
  lightOn = true;
  handleLightStatus();
}

void handleLightOff() {
  analogWrite(LIGHT_PIN, 0);
  lightBrightness = 0;
  lightOn = false;
  handleLightStatus();
}

// ==================== COVER CONTROL ====================

void openCover() {
  if (isMoving || currentState == COVER_OPEN) return;
  movementStartPulseUs = currentServoPulseUs;
  targetAngle = openAngle;
  movingToOpen = true;
  currentState = COVER_MOVING;
  isMoving = true;
  moveStartTime = millis();
  lastServoUpdateTime = moveStartTime;
}

void closeCover() {
  if (isMoving || currentState == COVER_CLOSED) return;
  movementStartPulseUs = currentServoPulseUs;
  targetAngle = closeAngle;
  movingToOpen = false;
  currentState = COVER_MOVING;
  isMoving = true;
  moveStartTime = millis();
  lastServoUpdateTime = moveStartTime;
}

void haltCover() {
  if (!isMoving) return;
  isMoving = false;
  currentState = movingToOpen ? COVER_CLOSED : COVER_OPEN;
}

uint16_t angleToPulse(uint16_t angle) {
  return map(constrain(angle, 0, 270), 0, 270, SERVO_MIN_US, SERVO_MAX_US);
}

void moveToPulse(uint16_t pulseUs) {
  uint16_t clamped = constrain(pulseUs, SERVO_MIN_US, SERVO_MAX_US);
  capServo.writeMicroseconds(clamped);
  currentServoPulseUs = clamped;
  currentServoAngle = map(clamped, SERVO_MIN_US, SERVO_MAX_US, 0, 270);
}

// Move servo immediately to angle 0-270 (DLC uses 0-270 range internally)
void moveToAngle(uint16_t angle) {
  moveToPulse(angleToPulse(angle));
}

// Smooth jog to angle (does not change cover state)
void startJog(uint16_t angle) {
  jogStartAngle = currentServoAngle;
  jogStartPulseUs = currentServoPulseUs;
  jogTargetAngle = constrain(angle, 0, 270);
  uint16_t distance = abs((int16_t)jogTargetAngle - (int16_t)jogStartAngle);
  jogDurationMs = ((uint32_t)distance * JOG_TIME_PER_270_DEG_MS + 135) / 270;
  if (distance == 0) {
    targetAngle = jogTargetAngle;
    jogActive = false;
    return;
  }
  jogStartTime = millis();
  lastServoUpdateTime = jogStartTime;
  jogActive = true;
  // Don't change isMoving or currentState — jog is temporary
}

// ==================== EEPROM ====================

void loadAnglesFromEEPROM() {
  if (EEPROM.read(EEPROM_ADDR_MAGIC) == EEPROM_MAGIC) {
    uint8_t ol = EEPROM.read(EEPROM_ADDR_OPEN_LOW);
    uint8_t oh = EEPROM.read(EEPROM_ADDR_OPEN_HIGH);
    uint8_t cl = EEPROM.read(EEPROM_ADDR_CLOSE_LOW);
    uint8_t ch = EEPROM.read(EEPROM_ADDR_CLOSE_HIGH);
    openAngle = (oh << 8) | ol;
    closeAngle = (ch << 8) | cl;
    openAngle = constrain(openAngle, 0, 270);
    closeAngle = constrain(closeAngle, 0, 270);

    // Safety: if both angles are the same, reset close to a sensible default
    if (openAngle == closeAngle) {
      closeAngle = (openAngle == 0) ? 180 : 0;
    }
  } else {
    saveAnglesToEEPROM();
  }
}

void saveAnglesToEEPROM() {
  EEPROM.write(EEPROM_ADDR_MAGIC, EEPROM_MAGIC);
  EEPROM.write(EEPROM_ADDR_OPEN_LOW, openAngle & 0xFF);
  EEPROM.write(EEPROM_ADDR_OPEN_HIGH, (openAngle >> 8) & 0xFF);
  EEPROM.write(EEPROM_ADDR_CLOSE_LOW, closeAngle & 0xFF);
  EEPROM.write(EEPROM_ADDR_CLOSE_HIGH, (closeAngle >> 8) & 0xFF);
  EEPROM.commit();
}

void readEEPROMString(int address, char* destination, size_t capacity) {
  if (capacity == 0) return;
  size_t i = 0;
  for (; i < capacity - 1; ++i) {
    uint8_t value = EEPROM.read(address + i);
    if (value == 0 || value == 0xFF) break;
    destination[i] = (char)value;
  }
  destination[i] = '\0';
}

void writeEEPROMString(int address, const char* value, size_t capacity) {
  size_t length = strlen(value);
  for (size_t i = 0; i < capacity; ++i) {
    EEPROM.write(address + i, i < length ? (uint8_t)value[i] : 0);
  }
}

void loadWiFiSettings() {
  uint32_t magic = 0;
  EEPROM.get(EEPROM_ADDR_WIFI_MAGIC, magic);
  if (magic != EEPROM_WIFI_MAGIC) {
    staEnabled = false;
    staSSID[0] = '\0';
    staPassword[0] = '\0';
    return;
  }

  staEnabled = EEPROM.read(EEPROM_ADDR_WIFI_ENABLED) == 1;
  readEEPROMString(EEPROM_ADDR_WIFI_SSID, staSSID, sizeof(staSSID));
  readEEPROMString(EEPROM_ADDR_WIFI_PASS, staPassword, sizeof(staPassword));
}

void saveWiFiSettings() {
  uint32_t magic = EEPROM_WIFI_MAGIC;
  EEPROM.put(EEPROM_ADDR_WIFI_MAGIC, magic);
  EEPROM.write(EEPROM_ADDR_WIFI_ENABLED, staEnabled ? 1 : 0);
  writeEEPROMString(EEPROM_ADDR_WIFI_SSID, staSSID, sizeof(staSSID));
  writeEEPROMString(EEPROM_ADDR_WIFI_PASS, staPassword, sizeof(staPassword));
  EEPROM.commit();
}

// ==================== BUTTON HANDLING ====================

void checkButton() {
  bool currentButtonState = digitalRead(BUTTON_PIN);
  
  if (currentButtonState != lastButtonState) {
    lastButtonCheck = millis();
  }
  
  if ((millis() - lastButtonCheck) > DEBOUNCE_MS) {
    if (currentButtonState == LOW && lastButtonState == HIGH) {
      // Button pressed (falling edge)
      if (currentState == COVER_OPEN) {
        closeCover();
      } else if (currentState == COVER_CLOSED) {
        openCover();
      }
    }
  }
  
  lastButtonState = currentButtonState;
}

// ==================== SERIAL COMMANDS (FULL DLC PROTOCOL) ====================

void checkSerial() {
  while (Serial.available() > 0) {
    char c = Serial.read();

    if (c == '<') {
      serialIndex = 0;
      memset(serialBuffer, 0, MAX_SERIAL_CHARS);
    } else if (c == '>') {
      serialComplete = true;
    } else if (serialIndex < MAX_SERIAL_CHARS - 1) {
      serialBuffer[serialIndex++] = c;
    }
  }

  if (serialComplete) {
    processCommand(serialBuffer, Serial);
    serialComplete = false;
    serialIndex = 0;
  }
}

void checkTcp() {
  if (tcpServer.hasClient()) {
    if (tcpClient) tcpClient.stop();
    tcpClient = tcpServer.accept();
    tcpClient.setNoDelay(true);
    tcpIndex = 0;
    memset(tcpBuffer, 0, sizeof(tcpBuffer));
  }

  if (!tcpClient || !tcpClient.connected()) return;

  while (tcpClient.available() > 0) {
    char c = (char)tcpClient.read();
    if (c == '<') {
      tcpIndex = 0;
      memset(tcpBuffer, 0, sizeof(tcpBuffer));
    } else if (c == '>') {
      processCommand(tcpBuffer, tcpClient);
      tcpIndex = 0;
      memset(tcpBuffer, 0, sizeof(tcpBuffer));
    } else if (tcpIndex < MAX_SERIAL_CHARS - 1) {
      tcpBuffer[tcpIndex++] = c;
    }
  }
}

void processCommand(const char* command, Print& response) {
  // All DLC commands use <X> format, sometimes <XNNN> with numeric param
  char cmd = command[0];
  // Parse optional numeric parameter after the command letter
  int param = -1;
  if (strlen(command) > 1) {
    param = atoi(command + 1);
  }
  // For two-letter commands like VO<N>, parse numeric param after 2nd char
  int param2 = -1;
  if (strlen(command) > 2) {
    param2 = atoi(command + 2);
  }

  switch (cmd) {

    // ── Cover Commands ─────────────────────────────────
    case 'O':  // Open cover
      openCover();
      response.println(F("<O>"));
      break;

    case 'C':  // Close cover
      closeCover();
      response.println(F("<C>"));
      break;

    case 'H':  // Halt (immediate stop)
      haltCover();
      response.println(F("<H>"));
      break;

    case 'P':  // Poll cover state
      response.print("<");
      response.print((int)currentState);
      response.println(">");
      break;

    // ── Calibrator (Flat Panel) ────────────────────────
    case 'L':  // Poll calibrator state
      response.print("<");
      response.print(lightOn ? 3 : 1);  // 3=Ready, 1=Off
      response.println(">");
      break;

    case 'B':  // Query current brightness
      response.print("<");
      response.print(lightBrightness);
      response.println(">");
      break;

    case 'M':  // Query max brightness
      response.print("<");
      response.print(MAX_BRIGHTNESS);
      response.println(">");
      break;

    case 'T':  // Set brightness and turn on
      // Format: T<N>
      lightBrightness = constrain(param >= 0 ? param : 0, 0, MAX_BRIGHTNESS);
      if (lightBrightness > 0) {
        analogWrite(LIGHT_PIN, lightBrightness);
        lightOn = true;
      } else {
        analogWrite(LIGHT_PIN, 0);
        lightOn = false;
      }
      response.print("<T");
      response.print(lightBrightness);
      response.println(">");
      break;

    case 'F':  // Turn off light
      analogWrite(LIGHT_PIN, 0);
      lightBrightness = 0;
      lightOn = false;
      response.println(F("<F>"));
      break;

    case 'A':  // autoON=true (ignore)
      response.println(F("<A>"));
      break;

    case 'a':  // autoON=false (ignore)
      response.println(F("<a>"));
      break;

    case 'S':  // Set stabilize time (ignore)
      response.print("<S");
      response.print(param >= 0 ? param : 0);
      response.println(">");
      break;

    // ── Broadband/Narrowband presets (ignore) ──────────
    case 'D':  // D B/N Save presets
      if (command[1] == 'B') response.println(F("<DB>"));
      else if (command[1] == 'N') response.println(F("<DN>"));
      break;

    case 'G':  // G B/N Go to presets
      if (command[1] == 'B') response.println(F("<0>"));
      else if (command[1] == 'N') response.println(F("<0>"));
      break;

    // ── Heater Commands — we don't have one ────────────
    case 'R':  // Poll heater state -> always NotPresent
      response.println(F("<0>"));
      break;

    case 'W':  // Heater on (ignore)
      response.println(F("<W>"));
      break;

    case 'w':  // Heater off (ignore)
      response.println(F("<w>"));
      break;

    case 'Q':  // autoHeat=true (ignore)
      response.println(F("<Q>"));
      break;

    case 'q':  // autoHeat=false (ignore)
      response.println(F("<q>"));
      break;

    case 'E':  // heatOnClose=true (ignore)
      response.println(F("<E>"));
      break;

    case 'e':  // heatOnClose=false (ignore)
      response.println(F("<e>"));
      break;

    case 'Y':  // Query temp/humidity details -> empty (no sensor)
      response.println(F("<0,0,0,0>"));
      break;

    // ── Servo Angle Configuration ──────────────────────
    case 'U':  // UO<N> set primary open angle
      if (command[1] == 'O' && param2 >= 0) {
        openAngle = constrain(param2, 0, 270);
        saveAnglesToEEPROM();
        response.print("<UO");
        response.print(openAngle);
        response.println(">");
      }
      // UC<N> set primary close angle
      else if (command[1] == 'C' && param2 >= 0) {
        closeAngle = constrain(param2, 0, 270);
        saveAnglesToEEPROM();
        response.print("<UC");
        response.print(closeAngle);
        response.println(">");
      }
      break;

    case 'u':  // uO query primary open angle
      if (command[1] == 'O') {
        response.print("<");
        response.print(openAngle);
        response.println(">");
      }
      break;

    case 'i':  // Query primary close angle (no prefix letter)
      response.print("<");
      response.print(closeAngle);
      response.println(">");
      break;

    // ── Secondary servo (ignore, only one servo) ───────
    case 'V':  // VO<N>/VC<N> set secondary angles, or V version
      if (command[1] == 'O' || command[1] == 'C') {
        // Secondary servo not installed, echo back with correct param
        int angleVal = (param2 >= 0) ? param2 : 0;
        response.print("<V");
        response.print(command[1]);
        response.print(angleVal);
        response.println(">");
      } else {
        // V alone = version query
        response.println(F("<v1.2.0-esp>"));
      }
      break;

    case 'v':  // vO/vC query secondary angles -> not installed
      response.println(F("<?>"));
      break;

    // ── Jog commands (smooth servo move, no save) ──────
    case 'J':  // J<N> jog primary servo (smooth)
      if (param >= 0 && param <= 270) {
        startJog(param);
        response.print("<J");
        response.print(param);
        response.println(">");
      }
      break;

    case 'j':  // Query current servo position
      response.print("<");
      response.print(currentServoAngle);
      response.println(">");
      break;

    case 'K':  // K<N> jog secondary (not installed, return ?)
      response.println(F("<?>"));
      break;

    case 'k':  // Query secondary position (not installed, return ?)
      response.println(F("<?>"));
      break;

    // ── System ─────────────────────────────────────────
    case 'Z':  // Handshake
      response.println(F("<?>"));
      break;

    default:
      // Unknown command — ignore
      break;
  }
}

// ==================== LED ====================

void setLED(bool on) {
#ifdef LED_ACTIVE_HIGH
  digitalWrite(LED_PIN, on ? HIGH : LOW);
#else
  digitalWrite(LED_PIN, on ? LOW : HIGH);
#endif
}

// ==================== MAIN LOOP ====================

void loop() {
  server.handleClient();
  unsigned long now = millis();

  if (restartPending && (long)(millis() - restartAt) >= 0) {
    ESP.restart();
  }

  checkButton();
  checkSerial();
  checkTcp();
  
  // --- Handle open/close movement ---
  if (isMoving) {
    unsigned long elapsed = now - moveStartTime;
    
    if (elapsed >= MOVE_TIME_MS) {
      // Movement complete
      moveToAngle(targetAngle);
      isMoving = false;
      currentState = movingToOpen ? COVER_OPEN : COVER_CLOSED;
      // Save state to EEPROM so we restore correct position after power loss
      EEPROM.write(EEPROM_ADDR_STATE, (uint8_t)currentState);
      EEPROM.commit();
    } else if (now - lastServoUpdateTime >= SERVO_UPDATE_INTERVAL_MS) {
      // Interpolate pulse width directly for smooth fixed-rate motion.
      int32_t pulseDelta = (int32_t)angleToPulse(targetAngle) - movementStartPulseUs;
      uint16_t currentPulseUs = movementStartPulseUs +
                                ((int64_t)pulseDelta * elapsed) / MOVE_TIME_MS;
      moveToPulse(currentPulseUs);
      lastServoUpdateTime = now;
    }
  }

  // --- Handle jog movement (smooth, no state change) ---
  if (jogActive) {
    unsigned long elapsed = now - jogStartTime;
    if (elapsed >= jogDurationMs) {
      moveToAngle(jogTargetAngle);
      targetAngle = jogTargetAngle;
      jogActive = false;
    } else if (now - lastServoUpdateTime >= SERVO_UPDATE_INTERVAL_MS) {
      int32_t pulseDelta = (int32_t)angleToPulse(jogTargetAngle) - jogStartPulseUs;
      uint16_t currentPulseUs = jogStartPulseUs +
                                ((int64_t)pulseDelta * elapsed) / jogDurationMs;
      moveToPulse(currentPulseUs);
      lastServoUpdateTime = now;
    }
  }
  
  // --- LED heartbeat (on when moving) ---
  if (isMoving) {
    static unsigned long lastLEDToggle = 0;
    if (millis() - lastLEDToggle > 250) {
      static bool ledState = false;
      ledState = !ledState;
      setLED(ledState);
      lastLEDToggle = millis();
    }
  } else {
    setLED(currentState == COVER_OPEN);
  }
  
  yield();   // feed ESP8266 watchdog + WiFi stack
  delay(10); // yield to WiFi stack
}
