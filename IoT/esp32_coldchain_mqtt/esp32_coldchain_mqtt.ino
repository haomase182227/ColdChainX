#include <WiFi.h>
#include <PubSubClient.h>
#include <OneWire.h>
#include <DallasTemperature.h>
#include <ArduinoJson.h>
#include <time.h>

// ===== Wi-Fi =====
const char* WIFI_SSID = "YOUR_WIFI_SSID";
const char* WIFI_PASSWORD = "YOUR_WIFI_PASSWORD";

// ===== MQTT Broker =====
const char* MQTT_HOST = "YOUR_MQTT_BROKER_HOST";
const uint16_t MQTT_PORT = 1883;
const char* MQTT_USERNAME = "YOUR_MQTT_USERNAME";
const char* MQTT_PASSWORD = "YOUR_MQTT_PASSWORD";

// ===== Device =====
const char* DEVICE_ID = "ESP32-COLDCHAIN-001";
const uint8_t DS18B20_PIN = 4; // D4 / GPIO4
const uint32_t PUBLISH_INTERVAL_MS = 15000;

// Door sensor simulation.
// Change this manually or replace with a real GPIO input later.
bool doorOpen = false;

WiFiClient wifiClient;
PubSubClient mqttClient(wifiClient);
OneWire oneWire(DS18B20_PIN);
DallasTemperature sensors(&oneWire);

uint32_t lastPublishMs = 0;

void connectWifi();
void connectMqtt();
void publishTelemetry();
String getIsoTimestamp();

void setup() {
  Serial.begin(115200);
  delay(500);

  sensors.begin();

  connectWifi();

  // Vietnam timezone UTC+7. Timestamp is ISO-like local time.
  configTime(7 * 3600, 0, "pool.ntp.org", "time.nist.gov");

  mqttClient.setServer(MQTT_HOST, MQTT_PORT);
  mqttClient.setKeepAlive(30);
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
  WiFi.mode(WIFI_STA);
  WiFi.begin(WIFI_SSID, WIFI_PASSWORD);

  while (WiFi.status() != WL_CONNECTED) {
    Serial.print(".");
    delay(500);
  }

  Serial.println();
  Serial.print("Wi-Fi connected. IP: ");
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

  // PubSubClient publish is QoS 0 only. Use AsyncMqttClient/ArduinoMqttClient
  // if broker-level QoS 1 publish acknowledgement is mandatory.
  bool ok = mqttClient.publish(topic, reinterpret_cast<const uint8_t*>(payload), length, false);

  Serial.print("Publish ");
  Serial.print(ok ? "OK" : "FAILED");
  Serial.print(" topic=");
  Serial.print(topic);
  Serial.print(" payload=");
  Serial.println(payload);
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
