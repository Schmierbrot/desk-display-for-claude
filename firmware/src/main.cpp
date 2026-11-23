// Claude Display – Firmware für GeekMagic SmallTV-Ultra
//
// Port 80:   GET  /            Status als JSON
//            GET  /brt?v=0-100 Helligkeit
//            POST /update      Firmware-Update (Formularfeld "firmware")
// Port 7777: Pixel-Server für bis zu 3 Rechner (Protokoll siehe serveClient()).
//
// Rührt Dateisystem und EEPROM der Original-Firmware nicht an.

#include <Arduino.h>
#include <ESP8266WiFi.h>
#include <ESP8266WebServer.h>
#include <ESP8266HTTPUpdateServer.h>
#include <ESP8266mDNS.h>
#include <WiFiManager.h>

#ifdef STAGE_MINIMAL
#define STAGE "minimal"
#else
#define STAGE "display"
#include <SPI.h>
#include <TFT_eSPI.h>
#include <time.h>
#include "assets.h"
TFT_eSPI tft;
WiFiServer pixServer(7777);
#endif

#define BL_PIN 5          // Hintergrundlicht, active-low
#define AP_NAME "Claude-Display-Setup"
#define FW_VERSION "1.1"

ESP8266WebServer web(80);
ESP8266HTTPUpdateServer updater;
int brightness = 60;
uint32_t frames = 0;
uint32_t ownerId = 0, ownerSeen = 0;   // aktueller Besitzer des Displays
uint8_t ownerPrio = 0;
bool standalone = false;                // Display zeichnet selbst die Uhr

void setBrightness(int pct) {
  brightness = constrain(pct, 0, 100);
  analogWrite(BL_PIN, 1023 - (brightness * 1023) / 100);
}

void showStatus(const String &line1, const String &line2) {
#ifndef STAGE_MINIMAL
  tft.fillScreen(TFT_BLACK);
  tft.setTextDatum(MC_DATUM);
  tft.setTextColor(0xDBCA /* Claude-Orange */, TFT_BLACK);
  tft.drawString("Claude Display", 120, 90, 4);
  tft.setTextColor(TFT_WHITE, TFT_BLACK);
  tft.drawString(line1, 120, 135, 2);
  tft.setTextColor(0x8C51, TFT_BLACK);
  tft.drawString(line2, 120, 160, 2);
#else
  (void)line1; (void)line2;
#endif
}

void handleRoot() {
  String j = "{\"fw\":\"" FW_NAME "\",\"stage\":\"" STAGE "\"";
  j += ",\"ip\":\"" + WiFi.localIP().toString() + "\"";
  j += ",\"rssi\":" + String(WiFi.RSSI());
  j += ",\"heap\":" + String(ESP.getFreeHeap());
  j += ",\"uptime\":" + String(millis() / 1000);
  j += ",\"brt\":" + String(brightness);
  j += ",\"frames\":" + String(frames);
  j += ",\"free_sketch\":" + String(ESP.getFreeSketchSpace());
  j += ",\"ver\":\"" FW_VERSION "\"";
#ifndef STAGE_MINIMAL
  j += ",\"owner\":" + String(ownerId) + ",\"owner_prio\":" + String(ownerPrio);
  j += ",\"mode\":\"" + String(standalone ? "uhr" : "rechner") + "\"";
#endif
  j += ",\"mac\":\"" + WiFi.macAddress() + "\"";
  j += ",\"reset\":\"" + ESP.getResetInfo() + "\"";
  j += "}";
  web.send(200, "application/json", j);
}

// Helligkeitsplan für die Zeit ohne Rechner (nur im RAM, Standard 60 % / 8 % von 22 bis 7 Uhr)
int dayBrt = 60, nightBrt = 8, nightStart = 22, nightEnd = 7;

int scheduledBrightness(int hour) {
  bool night = nightStart > nightEnd ? (hour >= nightStart || hour < nightEnd)
                                     : (hour >= nightStart && hour < nightEnd);
  return night ? nightBrt : dayBrt;
}

// GET /brt?v=60[&d=60&n=8&ns=22&ne=7]
void handleBrt() {
  if (web.hasArg("v")) setBrightness(web.arg("v").toInt());
  if (web.hasArg("d")) dayBrt = constrain(web.arg("d").toInt(), 0, 100);
  if (web.hasArg("n")) nightBrt = constrain(web.arg("n").toInt(), 0, 100);
  if (web.hasArg("ns")) nightStart = constrain(web.arg("ns").toInt(), 0, 23);
  if (web.hasArg("ne")) nightEnd = constrain(web.arg("ne").toInt(), 0, 23);
  handleRoot();
}

#ifndef STAGE_MINIMAL
// GET /cmd?c=26&d=01&w=10  -> rohes Kommando an den ST7789 (Hex), optional Wartezeit in ms.
// Zum Abstimmen von Gamma/Spannungen ohne neu zu flashen.
void handleCmd() {
  if (!web.hasArg("c")) { web.send(400, "text/plain", "c fehlt"); return; }
  uint8_t c = strtol(web.arg("c").c_str(), nullptr, 16);
  String d = web.arg("d");
  tft.writecommand(c);
  for (unsigned i = 0; i + 1 < d.length(); i += 2)
    tft.writedata(strtol(d.substring(i, i + 2).c_str(), nullptr, 16));
  if (web.hasArg("w")) delay(constrain(web.arg("w").toInt(), 0, 500));
  web.send(200, "text/plain", "ok");
}

// SPI.writeBytes liest 32-Bit-weise: Puffer müssen auf 4 Bytes ausgerichtet sein
static uint8_t pixBuf[1460] __attribute__((aligned(4)));

// ---- Eigenständige Uhr -----------------------------------------------------
// Sendet 15 s lang kein Rechner, zeichnet das Display selbst Uhrzeit und Datum
// (Zeit vom Internet-Zeitserver) in den Theme-Farben, die ihm die App schickt.
// Ein Rechner, der danach wieder sendet, muss mit einem Vollbild beginnen.
#define HOST_TIMEOUT_MS 15000
bool hostEver = false;
uint32_t lastHostMs = 0;
int shownMin = -1, shownDay = -1, shownBrtHour = -1;
static const char *WTAG[] = {"Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"};
static const char *MONAT[] = {"Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August",
                              "September", "Oktober", "November", "Dezember"};

// Theme (nur im RAM; Standard = Vorlage "Neon"). Farben als 24-Bit-RGB.
struct Theme {
  uint32_t accent = 0x5B8CFF, accent2 = 0xB06BFF, text = 0xEEF1FF, muted = 0x9AA3C7, dim = 0x646C90, edge = 0x3A3F5C;
  uint8_t logo = 0;               // 0 Ring, 1 Stern, 2 Punkt, 3 keins
  char title[40] = "Claude Code";
} theme;

uint16_t c565(uint32_t rgb) { return tft.color565(rgb >> 16, (rgb >> 8) & 255, rgb & 255); }

uint32_t lerp(uint32_t a, uint32_t b, float t) {
  auto ch = [&](int s) { return (uint32_t)(((a >> s) & 255) + (((int)((b >> s) & 255) - (int)((a >> s) & 255)) * t)) << s; };
  return ch(16) | ch(8) | ch(0);
}

void hostSeen() {
  lastHostMs = millis();
  hostEver = true;
  standalone = false;
}

// nächstes Unicode-Zeichen aus UTF-8
uint16_t nextCp(const char *&s) {
  uint8_t c = *s++;
  if (c < 0x80) return c;
  if ((c & 0xE0) == 0xC0 && *s) return ((c & 0x1F) << 6) | (*s++ & 0x3F);
  if ((c & 0xF0) == 0xE0 && s[0] && s[1]) { uint16_t v = ((c & 0x0F) << 12) | ((s[0] & 0x3F) << 6) | (s[1] & 0x3F); s += 2; return v; }
  while ((*s & 0xC0) == 0x80) s++;
  return '?';
}

bool findGlyph(const GFont &f, uint16_t cp, Glyph &g) {
  for (uint16_t i = 0; i < f.count; i++) {
    memcpy_P(&g, &f.glyphs[i], sizeof(Glyph));
    if (g.cp == cp) return true;
  }
  return false;
}

void palette(uint32_t rgb, uint16_t pal[16]) {
  for (int a = 0; a < 16; a++) pal[a] = c565(lerp(0, rgb, a / 15.0f));
}

// SPI.writeBytes liest 32-Bit-weise: Puffer müssen auf 4 Bytes ausgerichtet sein
static uint8_t row[480] __attribute__((aligned(4)));

// Text auf der Grundlinie des Fonts in den Streifen [y0, y1) zeichnen (Streifen wird gelöscht).
// xLeft < 0 = zentriert; maxW begrenzt die Breite (Rest wird weggelassen).
void drawText(const GFont &f, const char *text, uint32_t color, int y0, int y1, int xLeft = -1, int maxW = 240) {
  Glyph gl[40]; int gx[40]; int n = 0, pen = 0;
  for (const char *p = text; *p && n < 40;) {
    Glyph g;
    if (!findGlyph(f, nextCp(p), g)) continue;
    if (pen + g.adv > maxW) break;
    gl[n] = g; gx[n] = pen; pen += g.adv; n++;
  }
  int left = xLeft < 0 ? (240 - pen) / 2 : xLeft;
  uint16_t pal[16];
  palette(color, pal);
  tft.startWrite();
  tft.setAddrWindow(0, y0, 240, y1 - y0);
  for (int y = y0; y < y1; y++) {
    memset(row, 0, sizeof(row));
    for (int i = 0; i < n; i++) {
      const Glyph &g = gl[i];
      int gy = y - (f.baseline + g.y0);
      if (gy < 0 || gy >= g.h) continue;
      int rowBytes = (g.w + 1) / 2;
      for (int x = 0; x < g.w; x++) {
        uint8_t b = pgm_read_byte(f.bits + g.off + gy * rowBytes + x / 2);
        uint8_t a = (x & 1) ? (b & 15) : (b >> 4);
        int px = left + gx[i] + g.x0 + x;
        if (!a || px < 0 || px >= 240) continue;
        row[px * 2] = pal[a] >> 8; row[px * 2 + 1] = pal[a] & 0xFF;
      }
    }
    SPI.writeBytes(row, 480);
  }
  tft.endWrite();
}

// fertige Textmaske zentriert zeichnen (nur ihr eigenes Rechteck)
void drawMask(const AMask &m, uint32_t color) {
  uint16_t pal[16];
  palette(color, pal);
  int x0 = (240 - m.w) / 2, rowBytes = (m.w + 1) / 2;
  tft.startWrite();
  tft.setAddrWindow(x0, m.y, m.w, m.h);
  for (int y = 0; y < m.h; y++) {
    for (int x = 0; x < m.w; x++) {
      uint8_t b = pgm_read_byte(m.bits + y * rowBytes + x / 2);
      uint8_t a = (x & 1) ? (b & 15) : (b >> 4);
      row[x * 2] = pal[a] >> 8; row[x * 2 + 1] = pal[a] & 0xFF;
    }
    SPI.writeBytes(row, m.w * 2);
  }
  tft.endWrite();
}

void drawLogo(int cx, int cy) {
  switch (theme.logo) {
    case 0:   // Ring mit Farbverlauf
      for (int i = 0; i < 12; i++)
        tft.drawSmoothArc(cx, cy, 9, 6, i * 30, i * 30 + 31, c565(lerp(theme.accent, theme.accent2, i / 11.0f)), TFT_BLACK, false);
      break;
    case 1:   // Stern
      for (int k = 0; k < 12; k++) {
        float a = k * PI / 6, len = (k % 2 == 0) ? 9.5f : 6.5f;
        tft.drawWideLine(cx + cosf(a) * 1.7f, cy + sinf(a) * 1.7f, cx + cosf(a) * len, cy + sinf(a) * len, 2.1f, c565(theme.accent), TFT_BLACK);
      }
      tft.fillSmoothCircle(cx, cy, 2, c565(theme.accent), TFT_BLACK);
      break;
    case 2:   // Punkt
      tft.fillSmoothCircle(cx, cy, 6, c565(lerp(0, theme.accent, 0.3f)), TFT_BLACK);
      tft.fillSmoothCircle(cx, cy, 4, c565(theme.accent), TFT_BLACK);
      break;
    default:
      break;
  }
}

void drawStandaloneFrame() {
  tft.fillScreen(TFT_BLACK);
  drawText(FONT_TITLE, theme.title, theme.text, 4, 32, theme.logo == 3 ? 10 : 33, theme.logo == 3 ? 220 : 197);
  drawLogo(17, 18);
  tft.drawSmoothRoundRect(120 - PILL_W / 2, 146, 15, 14, PILL_W, 30, c565(theme.edge), TFT_BLACK);
  drawMask(MASK_PILL, theme.muted);
  drawMask(MASK_HINT1, theme.dim);
  drawMask(MASK_HINT2, theme.dim);
  shownMin = shownDay = shownBrtHour = -1;
}

void handleStandalone() {
  time_t now = time(nullptr);
  if (now < 1700000000) return;                                   // Zeit noch nicht da
  if (hostEver && millis() - lastHostMs < HOST_TIMEOUT_MS) return;  // ein Rechner zeichnet
  struct tm t;
  localtime_r(&now, &t);
  if (!standalone) {
    standalone = true;
    ownerId = 0;                    // wer wiederkommt, muss ein Vollbild schicken
    drawStandaloneFrame();
  }
  if (t.tm_min != shownMin) {
    char buf[8];
    snprintf(buf, sizeof(buf), "%02d:%02d", t.tm_hour, t.tm_min);
    drawText(FONT_CLOCK, buf, theme.text, 34, 112);
    shownMin = t.tm_min;
  }
  if (t.tm_mday != shownDay) {
    char buf[48];
    snprintf(buf, sizeof(buf), "%s, %d. %s", WTAG[t.tm_wday], t.tm_mday, MONAT[t.tm_mon]);
    drawText(FONT_DATE, buf, theme.muted, 112, 140);
    shownDay = t.tm_mday;
  }
  if (t.tm_hour != shownBrtHour) {
    setBrightness(scheduledBrightness(t.tm_hour));
    shownBrtHour = t.tm_hour;
  }
}

// GET /theme?a=5B8CFF&b=B06BFF&t=…&m=…&d=…&e=…&logo=0&title=Claude%20Code
void handleTheme() {
  auto hex = [](const char *k, uint32_t &v) { if (web.hasArg(k)) v = strtoul(web.arg(k).c_str(), nullptr, 16) & 0xFFFFFF; };
  hex("a", theme.accent); hex("b", theme.accent2); hex("t", theme.text);
  hex("m", theme.muted); hex("d", theme.dim); hex("e", theme.edge);
  if (web.hasArg("logo")) theme.logo = constrain(web.arg("logo").toInt(), 0, 3);
  if (web.hasArg("title")) strlcpy(theme.title, web.arg("title").c_str(), sizeof(theme.title));
  if (standalone) drawStandaloneFrame();
  web.send(200, "text/plain", "ok");
}

// ---- Mehrere Rechner teilen sich das Display -------------------------------
// Jeder Rechner hat eine ID und eine Priorität (2 = aktive Claude-Sitzung,
// 1 = Leerlauf). Angenommen wird ein Rahmen, wenn er vom aktuellen Besitzer
// kommt, der Besitzer seit LEASE_MS schweigt oder die Priorität höher ist.
// Ein neuer Besitzer muss mit einem Vollbild beginnen.
#define MAX_CLIENTS 3
#define LEASE_MS 5000
#define CLIENT_IDLE_MS 20000   // Rechner schicken jede Sekunde etwas – länger Stille = tote Verbindung
WiFiClient clients[MAX_CLIENTS];
uint32_t clientSeen[MAX_CLIENTS];

// liest genau n Bytes oder bricht nach Timeout ab
bool readExact(WiFiClient &c, uint8_t *dst, size_t n) {
  uint32_t t0 = millis();
  while (n) {
    int a = c.available();
    if (a <= 0) {
      if (!c.connected() || millis() - t0 > 3000) return false;
      delay(0);
      continue;
    }
    int r = c.read(dst, min((size_t)a, n));
    if (r > 0) { dst += r; n -= r; t0 = millis(); }
  }
  return true;
}

// Pixeldaten an den Bildschirm (draw=true) oder verwerfen
bool streamPixels(WiFiClient &c, uint32_t remaining, bool draw) {
  uint32_t t0 = millis();
  while (remaining) {
    int a = c.available();
    if (a <= 0) {
      if (!c.connected() || millis() - t0 > 3000) return false;
      delay(0);
      continue;
    }
    size_t n = min((uint32_t)a, min(remaining, (uint32_t)sizeof(pixBuf)));
    n = c.read(pixBuf, n);
    if (n) {
      // Bytes unverändert per SPI weiterreichen. Nicht tft.pushColors(): das
      // teilt die Länge durch 2 und verliert bei ungeraden Paketen ein Byte.
      if (draw) SPI.writeBytes(pixBuf, n);
      remaining -= n;
      t0 = millis();
    }
  }
  return true;
}

// 'K' = angenommen, 'B' = besetzt, 'R' = erst ein Vollbild schicken
char arbitrate(uint32_t id, uint8_t prio, bool full) {
  bool leaseValid = ownerId && millis() - ownerSeen < LEASE_MS;
  if (id == ownerId || !leaseValid || prio > ownerPrio) {
    if (id != ownerId && !full) return 'R';
    ownerId = id; ownerPrio = prio; ownerSeen = millis();
    hostSeen();
    return 'K';
  }
  return 'B';
}

// Protokoll:
//  'P' x y w h + Pixel              (alt, ID 1 / Priorität 2)
//  'Q' id[4] prio x y w h + Pixel   Rechteck, Koordinaten uint16 big-endian
//  'H' id[4] prio                   Lebenszeichen ohne Bildänderung
void serveClient(WiFiClient &c) {
  while (c.available() > 0) {
    int t = c.peek();
    size_t need = t == 'P' ? 9 : t == 'Q' ? 14 : t == 'H' ? 6 : 0;
    if (!need) { c.stop(); return; }
    if ((size_t)c.available() < need) return;
    uint8_t h[14];
    if (!readExact(c, h, need)) { c.stop(); return; }
    uint32_t id = 1; uint8_t prio = 2; uint8_t *r = h + 1;
    if (t != 'P') {
      id = (uint32_t)h[1] << 24 | (uint32_t)h[2] << 16 | (uint32_t)h[3] << 8 | h[4];
      prio = h[5]; r = h + 6;
    }
    if (t == 'H') {
      char v = (id == ownerId || !(ownerId && millis() - ownerSeen < LEASE_MS) || prio > ownerPrio)
                   ? (id == ownerId ? 'K' : 'R') : 'B';
      if (v == 'K') { ownerPrio = prio; ownerSeen = millis(); hostSeen(); }
      c.write((uint8_t)v);
      continue;
    }
    uint16_t x = r[0] << 8 | r[1], y = r[2] << 8 | r[3];
    uint16_t w = r[4] << 8 | r[5], hh = r[6] << 8 | r[7];
    if (!w || !hh || x + w > 240 || y + hh > 240) { c.stop(); return; }
    char v = arbitrate(id, prio, x == 0 && y == 0 && w == 240 && hh == 240);
    if (v == 'K') {
      tft.startWrite();
      tft.setAddrWindow(x, y, w, hh);
    }
    bool ok = streamPixels(c, (uint32_t)w * hh * 2, v == 'K');
    if (v == 'K') { tft.endWrite(); frames++; }
    if (!ok) { c.stop(); return; }
    c.write((uint8_t)v);
  }
}

void handlePixels() {
  uint32_t now = millis();
  // Verbindungen, die abgerissen sind, ohne dass es das Display merkt (Rechner
  // schläft, WLAN weg), würden sonst die Plätze dauerhaft belegen.
  for (int i = 0; i < MAX_CLIENTS; i++)
    if (clients[i] && clients[i].connected() && now - clientSeen[i] > CLIENT_IDLE_MS) clients[i].stop();
  WiFiClient n = pixServer.accept();
  if (n) {
    n.setNoDelay(true);
    n.keepAlive(10, 5, 3);     // tote Gegenstelle nach etwa 25 s erkennen
    int slot = -1;
    for (int i = 0; i < MAX_CLIENTS; i++)
      if (!clients[i] || !clients[i].connected()) { slot = i; break; }
    if (slot < 0) {
      // alles belegt: die am längsten stille Verbindung weicht – der Neue ist sicher lebendig
      slot = 0;
      for (int i = 1; i < MAX_CLIENTS; i++)
        if (now - clientSeen[i] > now - clientSeen[slot]) slot = i;
      clients[slot].stop();
    }
    clients[slot] = n;
    clientSeen[slot] = now;
  }
  for (int i = 0; i < MAX_CLIENTS; i++)
    if (clients[i] && clients[i].connected()) {
      if (clients[i].available() > 0) clientSeen[i] = millis();
      serveClient(clients[i]);
    }
}

// WLAN-Wächter: das ESP verbindet sich meist selbst neu; hängt es trotzdem, nachhelfen
uint32_t wifiLostMs = 0;
bool wifiKicked = false;
void watchWifi() {
  if (WiFi.status() == WL_CONNECTED) { wifiLostMs = 0; wifiKicked = false; return; }
  uint32_t now = millis();
  if (!wifiLostMs) { wifiLostMs = now; return; }
  if (!wifiKicked && now - wifiLostMs > 30000) { WiFi.reconnect(); wifiKicked = true; }
  if (now - wifiLostMs > 300000) ESP.restart();   // 5 Minuten ohne WLAN: sauber neu starten
}
#endif

void setup() {
  Serial.begin(115200);
  analogWriteRange(1023);
  pinMode(BL_PIN, OUTPUT);
#ifdef STAGE_MINIMAL
  digitalWrite(BL_PIN, HIGH);   // Licht aus – Stufe 1 hat keine Anzeige
#else
  tft.init();
  // Die Spannungs-/Gamma-Werte von TFT_eSPI passen nicht zu diesem Panel
  // (dunkle Töne leuchten, Grünstich). Darum Controller auf Werkswerte
  // zurücksetzen und nur das Nötigste wieder einschalten.
  tft.writecommand(0x01); delay(150);          // Software-Reset
  tft.writecommand(0x11); delay(150);          // Sleep out
  tft.writecommand(0x3A); tft.writedata(0x55); // 16 Bit pro Pixel
  tft.writecommand(0x36); tft.writedata(0x00); // Ausrichtung
  tft.writecommand(0xC0); tft.writedata(0x0C); // LCMCTRL: Werkswert 0x2C vertauscht Rot/Blau
  tft.writecommand(0x13);                      // Normalmodus
  tft.writecommand(0x29); delay(50);           // Display an
  tft.setRotation(0);
  tft.invertDisplay(true);
  tft.fillScreen(TFT_BLACK);
  setBrightness(60);
  showStatus("Verbinde mit WLAN ...", "");
#endif

  WiFi.mode(WIFI_STA);
  WiFi.hostname("claude-display");
  WiFiManager wm;
  wm.setConfigPortalTimeout(300);
  wm.setAPCallback([](WiFiManager *) {
    showStatus("WLAN einrichten:", AP_NAME);
  });
  if (!wm.autoConnect(AP_NAME)) ESP.restart();
  // Kein WLAN-Stromsparen: sonst kommen Bilder verzögert an oder Verbindungen reißen ab
  WiFi.setSleepMode(WIFI_NONE_SLEEP);
  WiFi.setAutoReconnect(true);

  MDNS.begin("claude-display");
#ifndef STAGE_MINIMAL
  // Uhrzeit für die eigenständige Uhr (Mitteleuropa mit Sommerzeit)
  configTime("CET-1CEST,M3.5.0,M10.5.0/3", "pool.ntp.org", "time.cloudflare.com");
#endif
  updater.setup(&web, "/update");
  web.on("/", handleRoot);
  web.on("/brt", handleBrt);
#ifndef STAGE_MINIMAL
  web.on("/theme", handleTheme);
#endif
#ifndef STAGE_MINIMAL
  web.on("/cmd", handleCmd);
#endif
  web.begin();
  MDNS.addService("http", "tcp", 80);
#ifndef STAGE_MINIMAL
  pixServer.begin();
  pixServer.setNoDelay(true);
  showStatus(WiFi.localIP().toString(), "warte auf Daten ...");
#endif
}

void loop() {
  web.handleClient();
  MDNS.update();
#ifndef STAGE_MINIMAL
  handlePixels();
  handleStandalone();
  watchWifi();
#endif
}
