#define TINY_GSM_MODEM_SIM7600 // Dùng profile SIM7600 cho mạch SIMCOM A7680C
#define TINY_GSM_RX_BUFFER 1024

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

// ===== CẤU HÌNH NHÀ MẠNG 4G VIETTEL =====
const char apn[]      = "v-internet"; 
const char gprsUser[] = "";
const char gprsPass[] = "";

// ===== CẤU HÌNH MQTT BROKER =====
const char* MQTT_HOST = "coldchainx-mqtt-demo.hycub5daehamhke8.southeastasia.azurecontainer.io";
const uint16_t MQTT_PORT = 1883;
const char* MQTT_USERNAME = "esp32user";
const char* MQTT_PASSWORD = "123456";

// ===== THÔNG SỐ THIẾT BỊ =====
const char* DEVICE_ID = "ESP32-COLDCHAIN-001";
const uint32_t TELEMETRY_INTERVAL_MS = 15000; // 15 giây gửi nhiệt độ 1 lần
const uint32_t GPS_INTERVAL_MS = 120000;      // 2 phút (120s) lấy GPS 1 lần
const uint32_t SIREN_DURATION_MS = 10000;     // 10 giây bật còi khi có cảnh báo
const uint8_t MQTT_PUBLISH_QOS = 1;

// Khởi tạo các đối tượng
HardwareSerial SerialAT(2);
TinyGsm modem(SerialAT);
TinyGsmClient gsmClient(modem);
PubSubClient mqttClient(gsmClient);
OneWire oneWire(DS18B20_PIN);
DallasTemperature sensors(&oneWire);

// Biến lưu trữ trạng thái
uint32_t lastTelemetryMs = 0;
uint32_t lastGpsMs = 0;
uint16_t nextPacketId = 1;
bool doorOpen = false;
bool sirenActive = false;
uint32_t sirenUntilMs = 0;

// Biến lưu GPS hiện tại
float currentLat = 0.0;
float currentLon = 0.0;

// Khai báo hàm
void connect4G();
void connectMqtt();
void publishTelemetry();
void updateLBSLocation();
String getIsoTimestamp();
void handleMqttMessage(char* topic, byte* payload, unsigned int length);
void handleCommandPayload(const uint8_t* payload, size_t length);
void activateSiren(uint32_t durationMs);
void updateSiren();
bool publishMqttQos1(const char* topic, const uint8_t* payload, size_t payloadLength);
bool waitForPubAck(uint16_t packetId, uint32_t timeoutMs);
bool readMqttRemainingLength(uint32_t& remainingLength, uint32_t timeoutMs);
bool readMqttByte(uint8_t& value, uint32_t timeoutMs);
bool readMqttBytes(uint8_t* buffer, size_t length, uint32_t timeoutMs);
bool writeMqttRemainingLength(uint32_t remainingLength);
bool writeAll(const uint8_t* data, size_t length);
bool processIncomingPublishPacket(uint8_t fixedHeader, uint32_t remainingLength, uint32_t timeoutMs);
bool sendPubAck(uint16_t packetId);

void setup() {
  Serial.begin(115200);
  delay(500);

  Serial.println("\n--- KHỞI ĐỘNG HỆ THỐNG COLD CHAIN 4G ---");
  
  // Khởi động cảm biến nhiệt độ
  sensors.begin();
  pinMode(SIREN_PIN, OUTPUT);
  digitalWrite(SIREN_PIN, LOW);

  // Khởi động giao tiếp với Module SIM
  SerialAT.begin(MCU_SIM_BAUDRATE, SERIAL_8N1, MCU_SIM_RX_PIN, MCU_SIM_TX_PIN);
  delay(3000);

  Serial.println("Đang khởi tạo Module SIM...");
  modem.restart();
  
  // Kết nối mạng 4G
  connect4G();

  // Cấu hình MQTT
  mqttClient.setServer(MQTT_HOST, MQTT_PORT);
  mqttClient.setCallback(handleMqttMessage);
  mqttClient.setKeepAlive(90);
  mqttClient.setSocketTimeout(10);
  
  // Cập nhật GPS lần đầu tiên ngay khi khởi động
  updateLBSLocation();
}

void loop() {
  // 1. Giữ kết nối mạng 4G
  if (!modem.isNetworkConnected() || !modem.isGprsConnected()) {
    connect4G();
  }

  // 2. Giữ kết nối MQTT Broker
  if (!mqttClient.connected()) {
    connectMqtt();
  }

  mqttClient.loop();
  updateSiren();
  const uint32_t now = millis();

  // 3. Cập nhật tọa độ LBS mỗi 2 phút
  if (now - lastGpsMs >= GPS_INTERVAL_MS) {
    lastGpsMs = now;
    updateLBSLocation();
  }

  // 4. Gửi dữ liệu Telemetry mỗi 15 giây
  if (now - lastTelemetryMs >= TELEMETRY_INTERVAL_MS) {
    lastTelemetryMs = now;
    publishTelemetry();
    doorOpen = !doorOpen; // Mô phỏng trạng thái đóng mở cửa cho demo
  }
}

// ================= CÁC HÀM XỬ LÝ CHÍNH =================

void connect4G() {
  Serial.print("Đang chờ mạng di động...");
  if (!modem.waitForNetwork()) {
    Serial.println(" Thất bại. Đang thử lại...");
    delay(5000);
    return;
  }
  Serial.println(" Đã nhận sóng!");

  Serial.print("Đang kết nối 4G (APN: "); Serial.print(apn); Serial.print(")...");
  if (!modem.gprsConnect(apn, gprsUser, gprsPass)) {
    Serial.println(" Thất bại. Đang thử lại...");
    delay(5000);
    return;
  }
  Serial.println(" KẾT NỐI INTERNET THÀNH CÔNG!");
}

void connectMqtt() {
  while (!mqttClient.connected()) {
    Serial.print("Đang kết nối MQTT Broker...");

    const String clientId = String(DEVICE_ID) + "-" + String((uint32_t)ESP.getEfuseMac(), HEX);
    bool connected = mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD);

    if (connected) {
      Serial.println(" THÀNH CÔNG!");
      char commandTopic[96];
      snprintf(commandTopic, sizeof(commandTopic), "commands/coldchain/%s/commands", DEVICE_ID);
      if (mqttClient.subscribe(commandTopic, MQTT_PUBLISH_QOS)) {
        Serial.print("Đã subscribe topic lệnh: ");
        Serial.println(commandTopic);
      } else {
        Serial.println("Lỗi: Không subscribe được topic lệnh.");
      }
    } else {
      Serial.print(" Lỗi RC=");
      Serial.print(mqttClient.state());
      Serial.println(". Thử lại sau 3s...");
      delay(3000);
    }
  }
}

void handleMqttMessage(char* topic, byte* payload, unsigned int length) {
  Serial.print("Nhận MQTT command | Topic: ");
  Serial.print(topic);
  Serial.print(" | Payload: ");
  for (unsigned int i = 0; i < length; i++) {
    Serial.print(static_cast<char>(payload[i]));
  }
  Serial.println();

  handleCommandPayload(reinterpret_cast<const uint8_t*>(payload), length);
}

void handleCommandPayload(const uint8_t* payload, size_t length) {
  StaticJsonDocument<256> doc;
  DeserializationError error = deserializeJson(doc, payload, length);
  if (error) {
    Serial.print("Lỗi parse command JSON: ");
    Serial.println(error.c_str());
    return;
  }

  const char* command = doc["Command"] | "";
  const char* targetDevice = doc["DeviceCode"] | DEVICE_ID;
  if (strcmp(targetDevice, DEVICE_ID) != 0) {
    return;
  }

  if (strcmp(command, "ACTIVATE_SIREN") == 0) {
    activateSiren(SIREN_DURATION_MS);
  } else {
    Serial.print("Command không hỗ trợ: ");
    Serial.println(command);
  }
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
    Serial.println("Đã tắt còi cảnh báo.");
  }
}

// Hàm lấy tọa độ qua Trạm phát sóng (LBS)
void updateLBSLocation() {
  Serial.println("\n--- Đang cập nhật tọa độ LBS ---");
  
  // Gửi lệnh LBS thô qua TinyGSM
  modem.sendAT("+CLBS=1,1");
  
  // Chờ phản hồi "+CLBS: 0," nghĩa là thành công
  if (modem.waitResponse(15000, "+CLBS: 0,") == 1) {
    String latStr = modem.stream.readStringUntil(',');
    String lonStr = modem.stream.readStringUntil(',');
    
    currentLat = latStr.toFloat();
    currentLon = lonStr.toFloat();
    
    modem.waitResponse(); // Đọc nốt chữ OK cuối cùng
    Serial.printf("=> Tọa độ mới: Lat: %.6f, Lon: %.6f\n", currentLat, currentLon);
  } else {
    Serial.println("=> Lỗi: Không thể lấy tọa độ LBS lúc này.");
  }
}

void publishTelemetry() {
  sensors.requestTemperatures();
  float tempC = sensors.getTempCByIndex(0);

  // Kiểm tra cảm biến DS18B20
  if (tempC == DEVICE_DISCONNECTED_C) {
    Serial.println("Lỗi: Cảm biến DS18B20 bị mất kết nối! (Vui lòng kiểm tra dây và trở kéo 4.7k)");
    return;
  }

  // Tạo đối tượng JSON
  StaticJsonDocument<256> doc;
  doc["DeviceId"] = DEVICE_ID;
  doc["TempC"] = roundf(tempC * 10.0f) / 10.0f;
  doc["DoorOpen"] = doorOpen;
  doc["Lat"] = currentLat;
  doc["Lon"] = currentLon;
  doc["Timestamp"] = getIsoTimestamp();

  // Đóng gói JSON
  char payload[256];
  size_t length = serializeJson(doc, payload, sizeof(payload));

  // Tạo Topic
  char topic[96];
  snprintf(topic, sizeof(topic), "telemetry/coldchain/%s", DEVICE_ID);

  // Publish bằng hàm QoS 1 tùy chỉnh
  bool ok = publishMqttQos1(topic, reinterpret_cast<const uint8_t*>(payload), length);

  Serial.print("Publish ");
  Serial.print(ok ? "OK" : "FAILED");
  Serial.print(" | Topic: ");
  Serial.print(topic);
  Serial.print(" | Dữ liệu: ");
  Serial.println(payload);
}

// Lấy thời gian từ mạng viễn thông
String getIsoTimestamp() {
  int year, month, day, hour, minute, second;
  float timezone;
  
  if (modem.getNetworkTime(&year, &month, &day, &hour, &minute, &second, &timezone)) {
    char buffer[32];
    snprintf(buffer, sizeof(buffer), "%04d-%02d-%02dT%02d:%02d:%02d+07:00", 
             year, month, day, hour, minute, second);
    return String(buffer);
  }
  
  // Fallback nếu chưa đồng bộ được giờ mạng
  return "2026-01-01T00:00:00+07:00"; 
}

// ================= CÁC HÀM MQTT QOS 1 TÙY CHỈNH (Đã trỏ sang mạng 4G) =================

bool publishMqttQos1(const char* topic, const uint8_t* payload, size_t payloadLength) {
  if (!mqttClient.connected()) return false;

  const size_t topicLength = strlen(topic);
  if (topicLength == 0 || topicLength > 65535) return false;

  const uint16_t packetId = nextPacketId++;
  if (nextPacketId == 0) nextPacketId = 1;

  const uint32_t remainingLength = 2 + topicLength + 2 + payloadLength;
  const uint8_t fixedHeader = 0x30 | (MQTT_PUBLISH_QOS << 1);
  const uint8_t topicLengthBytes[2] = { static_cast<uint8_t>((topicLength >> 8) & 0xFF), static_cast<uint8_t>(topicLength & 0xFF) };
  const uint8_t packetIdBytes[2] = { static_cast<uint8_t>((packetId >> 8) & 0xFF), static_cast<uint8_t>(packetId & 0xFF) };

  if (!writeAll(&fixedHeader, 1) || !writeMqttRemainingLength(remainingLength) ||
      !writeAll(topicLengthBytes, sizeof(topicLengthBytes)) || !writeAll(reinterpret_cast<const uint8_t*>(topic), topicLength) ||
      !writeAll(packetIdBytes, sizeof(packetIdBytes)) || !writeAll(payload, payloadLength)) {
    return false;
  }
  return waitForPubAck(packetId, 5000);
}

bool waitForPubAck(uint16_t packetId, uint32_t timeoutMs) {
  const uint32_t startedAt = millis();
  while (millis() - startedAt < timeoutMs) {
    uint8_t fixedHeader = 0;
    if (!readMqttByte(fixedHeader, 100)) continue;

    uint32_t remainingLength = 0;
    if (!readMqttRemainingLength(remainingLength, 1000)) return false;

    if ((fixedHeader & 0xF0) == 0x40 && remainingLength == 2) {
      uint8_t packetIdMsb = 0, packetIdLsb = 0;
      if (!readMqttByte(packetIdMsb, 1000) || !readMqttByte(packetIdLsb, 1000)) return false;
      const uint16_t ackPacketId = (static_cast<uint16_t>(packetIdMsb) << 8) | packetIdLsb;
      if (ackPacketId == packetId) return true;
      continue;
    }

    if ((fixedHeader & 0xF0) == 0x30) {
      if (!processIncomingPublishPacket(fixedHeader, remainingLength, 1000)) return false;
      continue;
    }

    for (uint32_t i = 0; i < remainingLength; i++) {
      uint8_t ignored = 0;
      if (!readMqttByte(ignored, 1000)) return false;
    }
  }
  return false;
}

bool processIncomingPublishPacket(uint8_t fixedHeader, uint32_t remainingLength, uint32_t timeoutMs) {
  if (remainingLength < 2) return false;

  uint8_t topicLengthBytes[2] = { 0, 0 };
  if (!readMqttBytes(topicLengthBytes, sizeof(topicLengthBytes), timeoutMs)) return false;

  uint16_t topicLength = (static_cast<uint16_t>(topicLengthBytes[0]) << 8) | topicLengthBytes[1];
  if (topicLength > remainingLength - 2) return false;

  char topic[128];
  const bool topicFits = topicLength < sizeof(topic);
  for (uint16_t i = 0; i < topicLength; i++) {
    uint8_t value = 0;
    if (!readMqttByte(value, timeoutMs)) return false;
    if (topicFits) topic[i] = static_cast<char>(value);
  }
  if (topicFits) topic[topicLength] = '\0';

  uint32_t consumed = 2 + topicLength;
  const uint8_t qos = (fixedHeader & 0x06) >> 1;
  uint16_t incomingPacketId = 0;
  if (qos > 0) {
    uint8_t packetIdBytes[2] = { 0, 0 };
    if (!readMqttBytes(packetIdBytes, sizeof(packetIdBytes), timeoutMs)) return false;
    incomingPacketId = (static_cast<uint16_t>(packetIdBytes[0]) << 8) | packetIdBytes[1];
    consumed += 2;
  }
  if (consumed > remainingLength) return false;

  uint32_t payloadLength = remainingLength - consumed;
  uint8_t payload[256];
  const bool payloadFits = payloadLength <= sizeof(payload);
  for (uint32_t i = 0; i < payloadLength; i++) {
    uint8_t value = 0;
    if (!readMqttByte(value, timeoutMs)) return false;
    if (payloadFits) payload[i] = value;
  }

  if (qos == 1 && incomingPacketId != 0 && !sendPubAck(incomingPacketId)) {
    return false;
  }

  if (topicFits && payloadFits) {
    handleCommandPayload(payload, payloadLength);
  }
  return true;
}

bool readMqttRemainingLength(uint32_t& remainingLength, uint32_t timeoutMs) {
  remainingLength = 0;
  uint32_t multiplier = 1;
  uint8_t encodedByte = 0;
  do {
    if (!readMqttByte(encodedByte, timeoutMs)) return false;
    remainingLength += (encodedByte & 127) * multiplier;
    multiplier *= 128;
    if (multiplier > 128UL * 128UL * 128UL * 128UL) return false;
  } while ((encodedByte & 128) != 0);
  return true;
}

bool readMqttByte(uint8_t& value, uint32_t timeoutMs) {
  const uint32_t startedAt = millis();
  while (millis() - startedAt < timeoutMs) {
    if (gsmClient.available() > 0) {
      const int byteRead = gsmClient.read();
      if (byteRead >= 0) {
        value = static_cast<uint8_t>(byteRead);
        return true;
      }
    }
    delay(5);
  }
  return false;
}

bool readMqttBytes(uint8_t* buffer, size_t length, uint32_t timeoutMs) {
  for (size_t i = 0; i < length; i++) {
    if (!readMqttByte(buffer[i], timeoutMs)) return false;
  }
  return true;
}

bool writeMqttRemainingLength(uint32_t remainingLength) {
  do {
    uint8_t encodedByte = remainingLength % 128;
    remainingLength /= 128;
    if (remainingLength > 0) encodedByte |= 128;
    if (!writeAll(&encodedByte, 1)) return false;
  } while (remainingLength > 0);
  return true;
}

bool writeAll(const uint8_t* data, size_t length) {
  return gsmClient.write(data, length) == length;
}

bool sendPubAck(uint16_t packetId) {
  const uint8_t pubAck[4] = {
    0x40,
    0x02,
    static_cast<uint8_t>((packetId >> 8) & 0xFF),
    static_cast<uint8_t>(packetId & 0xFF)
  };
  return writeAll(pubAck, sizeof(pubAck));
}
