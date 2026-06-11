#include <WiFi.h>
#include <PubSubClient.h>
#include <OneWire.h>
#include <DallasTemperature.h>
#include <ArduinoJson.h>
#include <time.h>

// ===== Wi-Fi =====
const char* WIFI_SSID = "test";
const char* WIFI_PASSWORD = "12345678";

// ===== MQTT Broker =====
// const char* MQTT_HOST = "10.220.168.115";
const char* MQTT_HOST = "coldchainx-mqtt-demo.hycub5daehamhke8.southeastasia.azurecontainer.io";
const uint16_t MQTT_PORT = 1883;
const char* MQTT_USERNAME = "esp32user";
const char* MQTT_PASSWORD = "123456";

// ===== Device =====
const char* DEVICE_ID = "ESP32-COLDCHAIN-001";
const uint8_t DS18B20_PIN = 4; // D4 / GPIO4
const uint32_t PUBLISH_INTERVAL_MS = 15000;
const uint8_t MQTT_PUBLISH_QOS = 1;

// Door sensor simulation.
// Change this manually or replace with a real GPIO input later.
bool doorOpen = false;

WiFiClient wifiClient;
PubSubClient mqttClient(wifiClient);
OneWire oneWire(DS18B20_PIN);
DallasTemperature sensors(&oneWire);

uint32_t lastPublishMs = 0;
uint16_t nextPacketId = 1;

void connectWifi();
void connectMqtt();
void publishTelemetry();
String getIsoTimestamp();
bool publishMqttQos1(const char* topic, const uint8_t* payload, size_t payloadLength);
bool waitForPubAck(uint16_t packetId, uint32_t timeoutMs);
bool readMqttRemainingLength(uint32_t& remainingLength, uint32_t timeoutMs);
bool readMqttByte(uint8_t& value, uint32_t timeoutMs);
bool writeMqttRemainingLength(uint32_t remainingLength);
bool writeAll(const uint8_t* data, size_t length);

void setup() {
  Serial.begin(115200);
  delay(500);

  sensors.begin();

  connectWifi();

  // Vietnam timezone UTC+7. Timestamp is ISO-like local time.
  configTime(7 * 3600, 0, "pool.ntp.org", "time.nist.gov");

  mqttClient.setServer(MQTT_HOST, MQTT_PORT);
  mqttClient.setKeepAlive(90);
  mqttClient.setSocketTimeout(10);
}

void loop() {
  if (WiFi.status() != WL_CONNECTED) {
    connectWifi();
  }

  if (!mqttClient.connected()) {
    connectMqtt();
  }

  mqttClient.loop();

  const uint32_t now = millis();
  if (now - lastPublishMs >= PUBLISH_INTERVAL_MS) {
    lastPublishMs = now;
    publishTelemetry();

    // Fake door status changes for demo.
    doorOpen = !doorOpen;
  }
}

void connectWifi() {
  Serial.print("Connecting Wi-Fi");
  
  // Dọn dẹp cache cũ trước khi kết nối
  WiFi.disconnect(true); 
  delay(1000);

  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  while (WiFi.status() != WL_CONNECTED) {
    Serial.print(".");
    delay(500);
  }

  Serial.println("\nWi-Fi connected.");
  Serial.print("IP: ");
  Serial.println(WiFi.localIP());
}

void connectMqtt() {
  while (!mqttClient.connected()) {
    Serial.print("Connecting MQTT...");

    const String clientId = String(DEVICE_ID) + "-" + String((uint32_t)ESP.getEfuseMac(), HEX);
    bool connected;

    if (strlen(MQTT_USERNAME) > 0) {
      connected = mqttClient.connect(clientId.c_str(), MQTT_USERNAME, MQTT_PASSWORD);
    } else {
      connected = mqttClient.connect(clientId.c_str());
    }

    if (connected) {
      Serial.println("connected");
    } else {
      Serial.print("failed, rc=");
      Serial.print(mqttClient.state());
      Serial.println(". Retry in 3s");
      delay(3000);
    }
  }
}

void publishTelemetry() {
  sensors.requestTemperatures();
  float tempC = sensors.getTempCByIndex(0);

  if (tempC == DEVICE_DISCONNECTED_C) {
    Serial.println("DS18B20 disconnected");
    return;
  }

  StaticJsonDocument<256> doc;
  doc["DeviceId"] = DEVICE_ID;
  doc["TempC"] = roundf(tempC * 10.0f) / 10.0f;
  doc["DoorOpen"] = doorOpen;
  doc["Timestamp"] = getIsoTimestamp();

  char payload[256];
  size_t length = serializeJson(doc, payload, sizeof(payload));

  char topic[96];
  snprintf(topic, sizeof(topic), "telemetry/coldchain/%s", DEVICE_ID);

  bool ok = publishMqttQos1(topic, reinterpret_cast<const uint8_t*>(payload), length);

  Serial.print("Publish ");
  Serial.print(ok ? "OK" : "FAILED");
  Serial.print(" qos=");
  Serial.print(MQTT_PUBLISH_QOS);
  Serial.print(" topic=");
  Serial.print(topic);
  Serial.print(" payload=");
  Serial.println(payload);
}

bool publishMqttQos1(const char* topic, const uint8_t* payload, size_t payloadLength) {
  if (!mqttClient.connected()) {
    return false;
  }

  const size_t topicLength = strlen(topic);
  if (topicLength == 0 || topicLength > 65535) {
    return false;
  }

  const uint16_t packetId = nextPacketId++;
  if (nextPacketId == 0) {
    nextPacketId = 1;
  }

  const uint32_t remainingLength = 2 + topicLength + 2 + payloadLength;
  const uint8_t fixedHeader = 0x30 | (MQTT_PUBLISH_QOS << 1);
  const uint8_t topicLengthBytes[2] = {
    static_cast<uint8_t>((topicLength >> 8) & 0xFF),
    static_cast<uint8_t>(topicLength & 0xFF)
  };
  const uint8_t packetIdBytes[2] = {
    static_cast<uint8_t>((packetId >> 8) & 0xFF),
    static_cast<uint8_t>(packetId & 0xFF)
  };

  if (!writeAll(&fixedHeader, 1) ||
      !writeMqttRemainingLength(remainingLength) ||
      !writeAll(topicLengthBytes, sizeof(topicLengthBytes)) ||
      !writeAll(reinterpret_cast<const uint8_t*>(topic), topicLength) ||
      !writeAll(packetIdBytes, sizeof(packetIdBytes)) ||
      !writeAll(payload, payloadLength)) {
    return false;
  }

  return waitForPubAck(packetId, 5000);
}

bool waitForPubAck(uint16_t packetId, uint32_t timeoutMs) {
  const uint32_t startedAt = millis();

  while (millis() - startedAt < timeoutMs) {
    uint8_t fixedHeader = 0;
    if (!readMqttByte(fixedHeader, 100)) {
      continue;
    }

    uint32_t remainingLength = 0;
    if (!readMqttRemainingLength(remainingLength, 1000)) {
      return false;
    }

    if ((fixedHeader & 0xF0) == 0x40 && remainingLength == 2) {
      uint8_t packetIdMsb = 0;
      uint8_t packetIdLsb = 0;
      if (!readMqttByte(packetIdMsb, 1000) || !readMqttByte(packetIdLsb, 1000)) {
        return false;
      }

      const uint16_t ackPacketId = (static_cast<uint16_t>(packetIdMsb) << 8) | packetIdLsb;
      if (ackPacketId == packetId) {
        return true;
      }

      continue;
    }

    for (uint32_t i = 0; i < remainingLength; i++) {
      uint8_t ignored = 0;
      if (!readMqttByte(ignored, 1000)) {
        return false;
      }
    }
  }

  return false;
}

bool readMqttRemainingLength(uint32_t& remainingLength, uint32_t timeoutMs) {
  remainingLength = 0;
  uint32_t multiplier = 1;
  uint8_t encodedByte = 0;

  do {
    if (!readMqttByte(encodedByte, timeoutMs)) {
      return false;
    }

    remainingLength += (encodedByte & 127) * multiplier;
    multiplier *= 128;

    if (multiplier > 128UL * 128UL * 128UL * 128UL) {
      return false;
    }
  } while ((encodedByte & 128) != 0);

  return true;
}

bool readMqttByte(uint8_t& value, uint32_t timeoutMs) {
  const uint32_t startedAt = millis();

  while (millis() - startedAt < timeoutMs) {
    if (wifiClient.available() > 0) {
      const int byteRead = wifiClient.read();
      if (byteRead >= 0) {
        value = static_cast<uint8_t>(byteRead);
        return true;
      }
    }

    delay(5);
  }

  return false;
}

bool writeMqttRemainingLength(uint32_t remainingLength) {
  do {
    uint8_t encodedByte = remainingLength % 128;
    remainingLength /= 128;

    if (remainingLength > 0) {
      encodedByte |= 128;
    }

    if (!writeAll(&encodedByte, 1)) {
      return false;
    }
  } while (remainingLength > 0);

  return true;
}

bool writeAll(const uint8_t* data, size_t length) {
  return wifiClient.write(data, length) == length;
}

String getIsoTimestamp() {
  struct tm timeInfo;
  if (!getLocalTime(&timeInfo, 1000)) {
    return "";
  }

  char buffer[32];
  strftime(buffer, sizeof(buffer), "%Y-%m-%dT%H:%M:%S+07:00", &timeInfo);
  return String(buffer);
}
