/*
  Name:    ESP8266 Lens Cap Controller
  Board:   Wemos D1 Mini / NodeMCU (ESP8266)
  Author:  Generated for DLCoverCalibrator project
  Date:    2025-07-06
  Version: 1.1.0

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

  WiFi:  Creates an Access Point (AP mode) by default.
         Connect phone/laptop to "LensCap" WiFi, then open http://192.168.4.1
         Edit SSID/PASS below to change.

  ASCOM/INDI Compatibility:
    This firmware implements the FULL DLC serial protocol.
    The ASCOM driver polls P (cover), L (calibrator), B (brightness),
    M (max brightness), R (heater) every cycle.
    Since this is a cover-only device:
      - L always returns 0 (NotPresent)
      - B always returns 0
      - M always returns 0
      - R always returns 0 (NotPresent)
    This makes the ASCOM driver happy without errors.
*/

#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <Servo.h>
#include <EEPROM.h>

// ==================== USER CONFIGURATION ====================

// --- WiFi Settings ---
// AP mode (default): ESP creates its own WiFi network
const char* apSSID = "LensCap";
const char* apPassword = "telescope";  // at least 8 chars; "" = open network

// STA mode (optional): ESP connects to your home WiFi
// Uncomment and fill in to use STA mode instead of AP
// #define WIFI_STA_MODE
const char* staSSID = "YOUR_WIFI_SSID";
const char* staPassword = "YOUR_WIFI_PASSWORD";

// --- Servo Settings ---
const uint8_t SERVO_PIN = 4;          // D2 (GPIO4) on Wemos D1 Mini
const uint16_t SERVO_MIN_US = 500;    // min pulse width (μs) — check servo datasheet
const uint16_t SERVO_MAX_US = 2500;   // max pulse width (μs)
uint16_t openAngle = 0;               // servo angle when cap is OPEN (0-270) *adjustable via ASCOM
uint16_t closeAngle = 180;            // servo angle when cap is CLOSED (0-270) *adjustable via ASCOM
const uint16_t MOVE_TIME_MS = 2000;   // time to complete open/close movement

// --- Button Settings ---
const uint8_t BUTTON_PIN = 5;         // D1 (GPIO5), button to GND, uses internal pullup
const uint16_t DEBOUNCE_MS = 200;     // debounce time

// --- LED Settings (optional) ---
const uint8_t LED_PIN = 0;            // D3 (GPIO0), active LOW on most Wemos boards
// #define LED_ACTIVE_HIGH             // uncomment if your LED is active HIGH

// --- Flat Panel Light Settings ---
const uint8_t LIGHT_PIN = 14;        // D5 (GPIO14), PWM output for flat panel LED
const uint8_t MAX_BRIGHTNESS = 255;  // 0-255 steps

// --- Serial Settings ---
const uint32_t SERIAL_BAUD = 115200;  // for USB serial control (ASCOM/INDI)

// --- EEPROM ---
#define EEPROM_MAGIC 0xDA            // magic byte to check if EEPROM is initialized
#define EEPROM_ADDR_MAGIC 0
#define EEPROM_ADDR_OPEN_LOW  1
#define EEPROM_ADDR_OPEN_HIGH 2
#define EEPROM_ADDR_CLOSE_LOW 3
#define EEPROM_ADDR_CLOSE_HIGH 4
#define EEPROM_ADDR_STATE     5      // last known cover state (1=Closed, 3=Open)

// ==================== END OF USER CONFIGURATION ====================

// --- Global State ---
Servo capServo;
ESP8266WebServer server(80);

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

// ==================== SETUP ====================

void setup() {
  // --- Servo pin: hold LOW IMMEDIATELY to prevent twitch during boot ---
  digitalWrite(SERVO_PIN, LOW);
  pinMode(SERVO_PIN, OUTPUT);
  digitalWrite(SERVO_PIN, LOW);

  Serial.begin(SERIAL_BAUD);
  Serial.println();
  Serial.println(F("ESP8266 Lens Cap Controller v1.2"));

  // --- EEPROM ---
  EEPROM.begin(16);
  loadAnglesFromEEPROM();

  // --- Servo: attach early so it holds position during WiFi init ---
  uint8_t savedState = EEPROM.read(EEPROM_ADDR_STATE);
  currentState = (savedState == COVER_OPEN) ? COVER_OPEN : COVER_CLOSED;
  uint16_t initAngle = (currentState == COVER_OPEN) ? openAngle : closeAngle;
  capServo.attach(SERVO_PIN, SERVO_MIN_US, SERVO_MAX_US);
  moveToAngle(initAngle);

  // --- Flat Panel Light ---
  pinMode(LIGHT_PIN, OUTPUT);
  digitalWrite(LIGHT_PIN, LOW);

  // --- Button ---
  pinMode(BUTTON_PIN, INPUT_PULLUP);

  // --- LED ---
  pinMode(LED_PIN, OUTPUT);
  setLED(false);

  // --- WiFi ---
#ifdef WIFI_STA_MODE
  WiFi.mode(WIFI_STA);
  WiFi.begin(staSSID, staPassword);
  Serial.print(F("Connecting to WiFi"));
  int attempts = 0;
  while (WiFi.status() != WL_CONNECTED && attempts < 30) {
    delay(500);
    Serial.print(".");
    attempts++;
  }
  if (WiFi.status() == WL_CONNECTED) {
    Serial.println();
    Serial.print(F("Connected! IP: "));
    Serial.println(WiFi.localIP());
  } else {
    Serial.println(F("\nWiFi failed. Starting AP mode..."));
    startAPMode();
  }
#else
  startAPMode();
#endif

  // --- Web Server ---
  setupWebServer();
  server.begin();
  Serial.println(F("Web server started."));
  Serial.println(F("Ready."));
}

void startAPMode() {
  WiFi.mode(WIFI_AP);
  WiFi.softAP(apSSID, apPassword);
  Serial.print(F("AP Mode: "));
  Serial.print(apSSID);
  Serial.print(F(" | IP: "));
  Serial.println(WiFi.softAPIP());
}

// ==================== WEB SERVER ====================

void setupWebServer() {
  server.on("/", handleRoot);
  server.on("/open", handleOpen);
  server.on("/close", handleClose);
  server.on("/status", handleStatus);
  server.on("/toggle", handleToggle);
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
    .info { font-size: 0.8rem; color: #888; margin-top: 20px; }
  </style>
</head>
<body>
  <div class="container">
    <h1>🔭 电动镜头盖</h1>
    <div id="status" class="status closed">● 已关闭</div>
    <button class="btn btn-open" onclick="fetch('/open');updateStatus();" id="btnOpen">📂 开盖</button>
    <button class="btn btn-close" onclick="fetch('/close');updateStatus();" id="btnClose">📁 关盖</button>
    <button class="btn btn-toggle" onclick="fetch('/toggle');updateStatus();">🔄 切换</button>
    <div class="info">Lens Cap Controller v1.0 | ESP8266</div>
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
    setInterval(getStatus, 3000);
    getStatus();
  </script>
</body>
</html>
)rawliteral";
  server.send(200, "text/html; charset=utf-8", html);
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

// ==================== COVER CONTROL ====================

void openCover() {
  if (isMoving) return;
  targetAngle = openAngle;
  movingToOpen = true;
  currentState = COVER_MOVING;
  isMoving = true;
  moveStartTime = millis();
}

void closeCover() {
  if (isMoving) return;
  targetAngle = closeAngle;
  movingToOpen = false;
  currentState = COVER_MOVING;
  isMoving = true;
  moveStartTime = millis();
}

void haltCover() {
  if (!isMoving) return;
  isMoving = false;
  currentState = movingToOpen ? COVER_CLOSED : COVER_OPEN;
}

// Move servo to angle 0-270 (DLC uses 0-270 range internally)
void moveToAngle(uint16_t angle) {
  // Clamp to 0-270 as DLC protocol, map to servo pulse
  uint16_t clamped = constrain(angle, 0, 270);
  uint16_t us = map(clamped, 0, 270, SERVO_MIN_US, SERVO_MAX_US);
  capServo.writeMicroseconds(us);
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
    processSerialCommand();
    serialComplete = false;
    serialIndex = 0;
  }
}

void processSerialCommand() {
  // All DLC commands use <X> format, sometimes <XNNN> with numeric param
  char cmd = serialBuffer[0];
  // Parse optional numeric parameter after the command letter
  int param = -1;
  if (strlen(serialBuffer) > 1) {
    param = atoi(serialBuffer + 1);
  }
  // For two-letter commands like VO<N>, parse numeric param after 2nd char
  int param2 = -1;
  if (strlen(serialBuffer) > 2) {
    param2 = atoi(serialBuffer + 2);
  }

  switch (cmd) {

    // ── Cover Commands ─────────────────────────────────
    case 'O':  // Open cover
      openCover();
      Serial.println(F("<O>"));
      break;

    case 'C':  // Close cover
      closeCover();
      Serial.println(F("<C>"));
      break;

    case 'H':  // Halt (immediate stop)
      haltCover();
      Serial.println(F("<H>"));
      break;

    case 'P':  // Poll cover state
      Serial.print("<");
      Serial.print((int)currentState);
      Serial.println(">");
      break;

    // ── Calibrator (Flat Panel) ────────────────────────
    case 'L':  // Poll calibrator state
      Serial.print("<");
      Serial.print(lightOn ? 3 : 1);  // 3=Ready, 1=Off
      Serial.println(">");
      break;

    case 'B':  // Query current brightness
      Serial.print("<");
      Serial.print(lightBrightness);
      Serial.println(">");
      break;

    case 'M':  // Query max brightness
      Serial.print("<");
      Serial.print(MAX_BRIGHTNESS);
      Serial.println(">");
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
      Serial.print("<T");
      Serial.print(lightBrightness);
      Serial.println(">");
      break;

    case 'F':  // Turn off light
      analogWrite(LIGHT_PIN, 0);
      lightBrightness = 0;
      lightOn = false;
      Serial.println(F("<F>"));
      break;

    case 'A':  // autoON=true (ignore)
      Serial.println(F("<A>"));
      break;

    case 'a':  // autoON=false (ignore)
      Serial.println(F("<a>"));
      break;

    case 'S':  // Set stabilize time (ignore)
      Serial.print("<S");
      Serial.print(param >= 0 ? param : 0);
      Serial.println(">");
      break;

    // ── Broadband/Narrowband presets (ignore) ──────────
    case 'D':  // D B/N Save presets
      if (serialBuffer[1] == 'B') Serial.println(F("<DB>"));
      else if (serialBuffer[1] == 'N') Serial.println(F("<DN>"));
      break;

    case 'G':  // G B/N Go to presets
      if (serialBuffer[1] == 'B') Serial.println(F("<0>"));
      else if (serialBuffer[1] == 'N') Serial.println(F("<0>"));
      break;

    // ── Heater Commands — we don't have one ────────────
    case 'R':  // Poll heater state -> always NotPresent
      Serial.println(F("<0>"));
      break;

    case 'W':  // Heater on (ignore)
      Serial.println(F("<W>"));
      break;

    case 'w':  // Heater off (ignore)
      Serial.println(F("<w>"));
      break;

    case 'Q':  // autoHeat=true (ignore)
      Serial.println(F("<Q>"));
      break;

    case 'q':  // autoHeat=false (ignore)
      Serial.println(F("<q>"));
      break;

    case 'E':  // heatOnClose=true (ignore)
      Serial.println(F("<E>"));
      break;

    case 'e':  // heatOnClose=false (ignore)
      Serial.println(F("<e>"));
      break;

    case 'Y':  // Query temp/humidity details -> empty (no sensor)
      Serial.println(F("<0,0,0,0>"));
      break;

    // ── Servo Angle Configuration ──────────────────────
    case 'U':  // UO<N> set primary open angle
      if (serialBuffer[1] == 'O' && param2 >= 0) {
        openAngle = constrain(param2, 0, 270);
        saveAnglesToEEPROM();
        Serial.print("<UO");
        Serial.print(openAngle);
        Serial.println(">");
      }
      // UC<N> set primary close angle
      else if (serialBuffer[1] == 'C' && param2 >= 0) {
        closeAngle = constrain(param2, 0, 270);
        saveAnglesToEEPROM();
        Serial.print("<UC");
        Serial.print(closeAngle);
        Serial.println(">");
      }
      break;

    case 'u':  // uO query primary open angle
      if (serialBuffer[1] == 'O') {
        Serial.print("<");
        Serial.print(openAngle);
        Serial.println(">");
      }
      break;

    case 'i':  // Query primary close angle (no prefix letter)
      Serial.print("<");
      Serial.print(closeAngle);
      Serial.println(">");
      break;

    // ── Secondary servo (ignore, only one servo) ───────
    case 'V':  // VO<N>/VC<N> set secondary angles, or V version
      if (serialBuffer[1] == 'O' || serialBuffer[1] == 'C') {
        // Secondary servo not installed, echo back with correct param
        int angleVal = (param2 >= 0) ? param2 : 0;
        Serial.print("<V");
        Serial.print(serialBuffer[1]);
        Serial.print(angleVal);
        Serial.println(">");
      } else {
        // V alone = version query
        Serial.println(F("<v1.1.0-esp>"));
      }
      break;

    case 'v':  // vO/vC query secondary angles -> not installed
      Serial.println(F("<?>"));
      break;

    // ── Jog commands (direct servo move, no save) ──────
    case 'J':  // J<N> jog primary servo
      if (param >= 0 && param <= 270) {
        moveToAngle(param);
        Serial.print("<J");
        Serial.print(param);
        Serial.println(">");
      }
      break;

    case 'j':  // Query current servo position (approximate)
      // Return target angle as approximation
      Serial.print("<");
      Serial.print(targetAngle);
      Serial.println(">");
      break;

    case 'K':  // K<N> jog secondary (not installed, return ?)
      Serial.println(F("<?>"));
      break;

    case 'k':  // Query secondary position (not installed, return ?)
      Serial.println(F("<?>"));
      break;

    // ── System ─────────────────────────────────────────
    case 'Z':  // Handshake
      Serial.println(F("<?>"));
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
  checkButton();
  checkSerial();
  
  // --- Handle movement ---
  if (isMoving) {
    unsigned long elapsed = millis() - moveStartTime;
    
    if (elapsed >= MOVE_TIME_MS) {
      // Movement complete
      moveToAngle(targetAngle);
      isMoving = false;
      currentState = movingToOpen ? COVER_OPEN : COVER_CLOSED;
      // Save state to EEPROM so we restore correct position after power loss
      EEPROM.write(EEPROM_ADDR_STATE, (uint8_t)currentState);
      EEPROM.commit();
    } else {
      // Smooth linear interpolation
      float progress = (float)elapsed / MOVE_TIME_MS;
      uint16_t startAngle = movingToOpen ? closeAngle : openAngle;
      int16_t currentAngle = startAngle + (targetAngle - startAngle) * progress;
      moveToAngle(constrain(currentAngle, 0, 270));
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
