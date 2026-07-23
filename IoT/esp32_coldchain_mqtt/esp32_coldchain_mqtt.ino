#define TINY_GSM_MODEM_BG96 // Profile chuẩn cho Quectel EG800K
#define TINY_GSM_RX_BUFFER 1024

#include <WiFi.h>
#include <ArduinoHttpClient.h>
#include <TinyGsmClient.h>
#include <PubSubClient.h>
#include <OneWire.h>
#include <DallasTemperature.h>
#include <ArduinoJson.h>

// ===== CẤU HÌNH CHÂN KẾT NỐI =====
#define MCU_SIM_TX_PIN          17
#define MCU_SIM_RX_PIN          16
#define MCU_SIM_BAUDRATE        115200
#define DS18B20_PIN             13 
#define SIREN_PIN               25

// ===== CẤU HÌNH API & NHÀ MẠNG =====
const char* HERE_API_KEY = "iAy6vvB_FN35Lsa7gTo6E-CuaokXx7MqSCXBW4Xg8sk";
const char apn[]         = "v-internet";
const char gprsUser[]    = "";
const char gprsPass[]    = "";

// ===== CẤU HÌNH MQTT BROKER =====
const char* MQTT_HOST = "8.231.129.222";
const uint16_t MQTT_PORT = 1883;
const char* MQTT_USERNAME = "esp32user";
const char* MQTT_PASSWORD = "123456";

// ===== THÔNG SỐ THIẾT BỊ & CHU KỲ =====
const char* DEVICE_ID = "ESP32-COLDCHAIN-001";
const uint32_t TELEMETRY_INTERVAL_MS = 10000;   // 10 giây gửi 1 lần (LUÔN GỬI BẤT KỂ DỮ LIỆU CÓ ĐỔI HAY KHÔNG)
const uint32_t GPS_INTERVAL_MS = 15000;      // 15 giây quét tọa độ 1 lần (giãn ra cho đỡ tốn pin/data)
const uint32_t HEARTBEAT_INTERVAL_MS = 30000; // 30 giây ép gửi điểm danh 1 lần dù không có gì thay đổi
const uint32_t SIREN_DURATION_MS = 10000;
const uint8_t MQTT_PUBLISH_QOS = 1;

// ===== KHỞI TẠO ĐỐI TƯỢNG =====
// ===== KHỞI TẠO ĐỐI TƯỢNG =====
HardwareSerial SerialAT(2);
TinyGsm modem(SerialAT);

// ÉP MQTT SỬ DỤNG SOCKET 0
TinyGsmClient gsmClient(modem, 0); 
PubSubClient mqttClient(gsmClient);

OneWire oneWire(DS18B20_PIN);
DallasTemperature sensors(&oneWire);

// ===== BIẾN TRẠNG THÁI TOÀN CỤC =====
uint32_t lastTelemetryMs = 0;
uint32_t lastGpsMs = 0;
uint32_t lastHeartbeatMs = 0; 
bool doorOpen = false;
bool sirenActive = false;
uint32_t sirenUntilMs = 0;
bool is_streaming = false;
bool use_real_gps = true; // GPS toggle from simulator
// Tọa độ hiện tại
float currentLat = 0.0;
float currentLon = 0.0;
float lastSentTemp = -999.0;
float lastSentLat = -999.0;

// Khai báo hàm
void connect4G();
void connectMqtt();
void publishTelemetry(float currentTemp);
void updateHybridLocation();
String getIsoTimestamp();
void handleMqttMessage(char* topic, byte* payload, unsigned int length);
void handleCommandPayload(const uint8_t* payload, size_t length);
void activateSiren(uint32_t durationMs);
void updateSiren();

void setup() {
  Serial.begin(115200);
  delay(500);

  // KHỞI TẠO WI-FI NGAY TỪ ĐẦU ĐỂ KHÔNG BỊ TREO LÚC QUÉT
  WiFi.mode(WIFI_STA);
  WiFi.disconnect();
  delay(100);

  Serial.println("\n--- HỆ THỐNG COLD CHAIN AIoT (3-LAYER HYBRID) ---");
  
  sensors.begin();
  pinMode(SIREN_PIN, OUTPUT);
  digitalWrite(SIREN_PIN, LOW);

  SerialAT.begin(MCU_SIM_BAUDRATE, SERIAL_8N1, MCU_SIM_RX_PIN, MCU_SIM_TX_PIN);
  delay(3000);

  Serial.println("Đang khởi tạo Module Quectel...");
  modem.restart();
  
  connect4G();

  Serial.println("Đang kích hoạt GNSS (Vệ tinh)...");
  modem.sendAT("+QGPS=1");
  modem.waitResponse(1000); 

  mqttClient.setServer(MQTT_HOST, MQTT_PORT);
  mqttClient.setCallback(handleMqttMessage);
  mqttClient.setKeepAlive(90);
  
  // MỞ RỘNG BỘ ĐỆM ĐỂ TRÁNH LỖI OVERFLOW MQTT
  mqttClient.setBufferSize(512);

  // KẾT NỐI MQTT TRƯỚC
  connectMqtt();

  // ĐỊNH VỊ SAU KHI ĐÃ ONLINE
  updateHybridLocation();
}

void loop() {
  if (!modem.isNetworkConnected() || !modem.isGprsConnected()) {
    connect4G();
  }

  if (!mqttClient.connected()) {
    connectMqtt();
  }

  mqttClient.loop();
  updateSiren();
  
  const uint32_t now = millis();

  // 1. Cập nhật tọa độ định kỳ
  if (now - lastGpsMs >= GPS_INTERVAL_MS) {
    lastGpsMs = now;
    updateHybridLocation(); 
  }

  // 2. Kiểm tra & Gửi Telemetry
  if (now - lastTelemetryMs >= TELEMETRY_INTERVAL_MS) {
    lastTelemetryMs = now;

    if (is_streaming) {
      sensors.requestTemperatures();
      float currentTemp = sensors.getTempCByIndex(0);

      if (currentTemp == DEVICE_DISCONNECTED_C) {
        Serial.println("Lỗi: Mất kết nối cảm biến DS18B20!");
        return;
      }

      // Luôn luôn gửi dữ liệu lên MQTT mỗi chu kỳ (90s)
      publishTelemetry(currentTemp); 
      lastSentTemp = currentTemp;    
      lastSentLat = currentLat;
      lastHeartbeatMs = now;
    } else {
      Serial.println("[Zzz] Đang chế độ chờ... (Streaming OFF)");
    }
  }
}

// ==========================================
// HÀM ĐỊNH VỊ 3 LỚP ĐÃ TỐI ƯU
// ==========================================
void updateHybridLocation() {
  if (!use_real_gps) {
      Serial.println("\n--- ĐANG MÔ PHỎNG GPS. BỎ QUA ĐỊNH VỊ 3 LỚP ĐỂ TIẾT KIỆM PIN ---");
      return;
  }

  Serial.println("\n--- ĐANG ĐỊNH VỊ (GPS -> Wi-Fi -> LBS) ---");
  
  // =====================================
  // [LỚP 1]: GPS VỆ TINH
  // =====================================
  modem.sendAT("+QGPSLOC=0");
  int res = modem.waitResponse(2000, "+QGPSLOC: ", "ERROR");

  if (res == 1) { 
    String timeStr = modem.stream.readStringUntil(',');
    String latStr = modem.stream.readStringUntil(',');  
    String lonStr = modem.stream.readStringUntil(',');  
    modem.waitResponse(); 

    int dotLat = latStr.indexOf('.');
    if(dotLat >= 2) {
      currentLat = latStr.substring(0, dotLat - 2).toFloat() + (latStr.substring(dotLat - 2, latStr.length() - 1).toFloat() / 60.0);
      if (latStr.endsWith("S")) currentLat = -currentLat;
    }
    int dotLon = lonStr.indexOf('.');
    if(dotLon >= 2) {
      currentLon = lonStr.substring(0, dotLon - 2).toFloat() + (lonStr.substring(dotLon - 2, lonStr.length() - 1).toFloat() / 60.0);
      if (lonStr.endsWith("W")) currentLon = -currentLon;
    }
    Serial.printf("=> [GPS] Thành công: %.6f, %.6f\n", currentLat, currentLon);
    return;
  }

  // =====================================
  // [LỚP 2]: WI-FI HERE API
  // =====================================
  Serial.println("=> [GPS] Mất sóng. Đang quét Wi-Fi...");
  
  // Dùng chế độ quét bất đồng bộ để chống kẹt driver
  int n = WiFi.scanNetworks(false, true); 
  
  Serial.printf("=> [DEBUG Wi-Fi] Mã trả về từ lệnh quét (n) = %d\n", n);
  
  if (n < 0) {
      Serial.println("=> [LỖI] Chip Wi-Fi của ESP32 báo lỗi (Có thể chưa bật mode STA hoặc lỗi Anten)!");
  } else if (n >= 2) { 
    Serial.println("=> [Wi-Fi] Bắt đầu lấy tọa độ từ HERE API...");
    // Gửi tối đa 10 mạng Wi-Fi (thay vì 2) để tăng tỷ lệ định vị thành công trên HERE API
    String payload = "{\"wlan\":[";
    int limit = (n > 10) ? 10 : n;
    for (int i = 0; i < limit; ++i) { 
      payload += "{\"mac\":\"" + WiFi.BSSIDstr(i) + "\"}";
      if (i < limit - 1) payload += ",";
    }
    payload += "]}";

    // ÉP HTTPS SỬ DỤNG SOCKET 1 ĐỂ TRÁNH XUNG ĐỘT VỚI MQTT
    TinyGsmClientSecure secureClient(modem, 1); 
    HttpClient http(secureClient, "pos.ls.hereapi.com", 443);
    
    String url = String("/positioning/v1/locate?apiKey=") + HERE_API_KEY;
    http.beginRequest();
    http.post(url);
    http.sendHeader("Content-Type", "application/json");
    http.sendHeader("Content-Length", payload.length());
    http.beginBody();
    http.print(payload);
    http.endRequest();
    
    int statusCode = http.responseStatusCode();
    Serial.printf("=> [DEBUG HERE API] HTTP Status Code = %d\n", statusCode);

    if (statusCode == 200) {
      StaticJsonDocument<512> doc;
      deserializeJson(doc, http.responseBody());
      currentLat = doc["location"]["lat"];
      currentLon = doc["location"]["lng"];
      Serial.printf("=> [Wi-Fi HERE] Thành công (Sai số %dm): %.6f, %.6f\n", (int)doc["location"]["accuracy"], currentLat, currentLon);
      
      http.stop();
      secureClient.stop(); 
      delay(3000); 
      WiFi.scanDelete();
      return;
    } else {
      Serial.println("=> [LỖI] Server HERE từ chối (Sai Key hoặc cú pháp JSON).");
      Serial.println(http.responseBody());
    }
    
    http.stop();
    secureClient.stop(); 
    delay(3000); 
  } else {
      Serial.println("=> [Wi-Fi] Thất bại do không đủ 2 mạng (chỉ tìm thấy 0 hoặc 1 mạng).");
  }
  WiFi.scanDelete();

  // =====================================
  // [LỚP 3]: LBS NHÀ MẠNG
  // =====================================
  Serial.println("=> [Wi-Fi] Thất bại. Gọi LBS nhà mạng...");
  
  // Kiểm tra xem PDP Context đã được kích hoạt chưa
  modem.sendAT("+QIACT?");
  Serial.print("=> [DEBUG LBS] Trạng thái Context: ");
  modem.waitResponse(2000);

  // Ép LBS dùng chung Context 1 với MQTT
  modem.sendAT("+QLBSCFG=\"contextid\",1");
  modem.waitResponse(1000); 

  Serial.println("=> [DEBUG LBS] Đang gửi lệnh AT+QLBS (Timeout 30s)...");
  modem.sendAT("+QLBS");
  
  // Chờ phản hồi, bắt thêm cả chữ ERROR để in ra. LBS có thể mất tới 30 giây!
  int lbsRes = modem.waitResponse(30000, "+QLBS: 0,", "ERROR", "+CME ERROR:");
  
  if (lbsRes == 1) {
    currentLat = modem.stream.readStringUntil(',').toFloat();
    currentLon = modem.stream.readStringUntil(',').toFloat();
    modem.waitResponse();
    Serial.printf("=> [LBS] Thành công: %.6f, %.6f\n", currentLat, currentLon);
  } else if (lbsRes == 2) {
    Serial.println("=> [LBS] Lỗi: Module trả về chữ ERROR (Có thể EG800K chưa cấu hình Server LBS mặc định).");
  } else if (lbsRes == 3) {
    Serial.println("=> [LBS] Lỗi: +CME ERROR (Lỗi viễn thông cấp thấp).");
  } else {
    Serial.println("=> [LBS] Thất bại hoàn toàn (Timeout). Giữ tọa độ cũ.");
  }
}


// ==========================================
// CÁC HÀM CÒN LẠI (GIỮ NGUYÊN)
// ==========================================
void publishTelemetry(float currentTemp) {
  StaticJsonDocument<256> doc;
  doc["DeviceId"] = DEVICE_ID;
  doc["TempC"] = roundf(currentTemp * 10.0f) / 10.0f;
  doc["DoorOpen"] = doorOpen; 
  doc["Lat"] = currentLat;
  doc["Lon"] = currentLon;
  doc["Timestamp"] = getIsoTimestamp();

  char payload[256];
  serializeJson(doc, payload, sizeof(payload));

  char dataTopic[96];
  // snprintf(dataTopic, sizeof(dataTopic), "telemetry/coldchain/%s/data", DEVICE_ID);
  snprintf(dataTopic, sizeof(dataTopic), "telemetry/coldchain/%s/raw", DEVICE_ID); // Hybrid Mode Proxy
  
  bool ok = mqttClient.publish(dataTopic, payload);
  Serial.printf("[DATA SENT] %s | Topic: %s | Payload: %s\n", ok ? "OK" : "FAILED", dataTopic, payload);
}

void connect4G() {
  Serial.print("Đang chờ mạng di động...");
  if (!modem.waitForNetwork()) {
    Serial.println(" Thất bại. Thử lại...");
    delay(5000); return;
  }
  Serial.println(" Đã nhận sóng!");
  Serial.print("Đang kết nối 4G...");
  
  if (!modem.gprsConnect(apn, gprsUser, gprsPass)) {
    Serial.println(" Thất bại. Thử lại...");
    delay(5000); return;
  }
  Serial.println(" KẾT NỐI INTERNET THÀNH CÔNG!");
}

void connectMqtt() {
  String clientId = String(DEVICE_ID) + String(random(0xffff), HEX);
  char cmdTopic[96];
  snprintf(cmdTopic, sizeof(cmdTopic), "command/coldchain/%s", DEVICE_ID);

  // Khai báo thêm Status Topic để làm Di chúc (LWT)
  char statusTopic[96];
  snprintf(statusTopic, sizeof(statusTopic), "telemetry/coldchain/%s/status", DEVICE_ID);

  while (!mqttClient.connected()) {
    Serial.print("Đang kết nối MQTT (ID: ");
    Serial.print(clientId);
    Serial.print(")...");
    
    // Gắn ClientId vào LWT Payload để Backend C# kiểm tra chéo (chống Ghost Connection)
    char lwtPayload[128];
    snprintf(lwtPayload, sizeof(lwtPayload), "{\"status\": \"OFFLINE\", \"clientId\": \"%s\"}", clientId.c_str());

    if (mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD, statusTopic, 1, true, lwtPayload)) {
      Serial.println(" THÀNH CÔNG!");
      
      // Gắn ClientId vào ONLINE Payload
      char onlinePayload[128];
      snprintf(onlinePayload, sizeof(onlinePayload), "{\"status\": \"ONLINE\", \"clientId\": \"%s\"}", clientId.c_str());
      mqttClient.publish(statusTopic, onlinePayload, true);
      
      mqttClient.subscribe(cmdTopic, MQTT_PUBLISH_QOS);
    } else {
      Serial.printf(" Lỗi RC=%d. Thử lại sau 5s...\n", mqttClient.state());
      delay(5000);
    }
  }
}

void handleMqttMessage(char* topic, byte* payload, unsigned int length) {
  Serial.printf(">>> [MQTT Nhận] Topic: %s <<<\n", topic);
  handleCommandPayload(reinterpret_cast<const uint8_t*>(payload), length);
}

void handleCommandPayload(const uint8_t* payload, size_t length) {
  StaticJsonDocument<256> doc;
  if (deserializeJson(doc, payload, length)) {
    Serial.println("=> [LỖI] Parse JSON Command thất bại!");
    return;
  }
  
  const char* action = "";
  if (doc.containsKey("command")) action = doc["command"];
  else if (doc.containsKey("Command")) action = doc["Command"];
  else if (doc.containsKey("action")) action = doc["action"];
  
  if (strcmp(action, "START_STREAMING") == 0) {
    is_streaming = true;
    Serial.println(">>> LỆNH: BẮT ĐẦU GỬI DỮ LIỆU <<<");
    lastSentTemp = -999.0;
  } 
  else if (strcmp(action, "STOP_STREAMING") == 0) {
    is_streaming = false;
    Serial.println(">>> LỆNH: DỪNG GỬI DỮ LIỆU <<<");
  } 
  else if (strcmp(action, "ENABLE_GPS") == 0) {
    use_real_gps = true;
    Serial.println(">>> LỆNH: BẬT LẠI ĐỊNH VỊ 3 LỚP (MẠCH THẬT) <<<");
  } 
  else if (strcmp(action, "DISABLE_GPS") == 0) {
    use_real_gps = false;
    Serial.println(">>> LỆNH: TẮT ĐỊNH VỊ 3 LỚP (ĐANG DÙNG GIẢ LẬP) <<<");
  } 
  else if (strcmp(action, "ACTIVATE_SIREN") == 0) activateSiren(SIREN_DURATION_MS);
}

void activateSiren(uint32_t durationMs) {
  sirenActive = true;
  sirenUntilMs = millis() + durationMs;
  digitalWrite(SIREN_PIN, HIGH);
  Serial.println("Đã bật còi cảnh báo.");
}

void updateSiren() {
  if (sirenActive && static_cast<int32_t>(millis() - sirenUntilMs) >= 0) {
    sirenActive = false;
    digitalWrite(SIREN_PIN, LOW);
  }
}

String getIsoTimestamp() {
  int y, m, d, h, min, s; float tz;
  if (modem.getNetworkTime(&y, &m, &d, &h, &min, &s, &tz)) {
    char buf[32];
    snprintf(buf, sizeof(buf), "%04d-%02d-%02dT%02d:%02d:%02d+07:00", y, m, d, h, min, s);
    return String(buf);
  }
  return "2026-06-27T00:00:00+07:00"; 
}