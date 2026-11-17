"""Gestaltung der Claude-Display-Anzeige (240x240).

Alles wird in dreifacher Auflösung gezeichnet und dann geglättet verkleinert,
damit Bögen, Kreise und Schrift sauber kantengeglättet sind.
Koordinaten in diesem Modul sind immer Display-Pixel (0..240).
"""
import math
from datetime import datetime

from PIL import Image, ImageDraw, ImageFont

W = H = 240
S = 3                                    # Supersampling-Faktor

# Farben – warm, dunkel, Claude-Orange als Akzent
BG = (0, 0, 0)   # reines Schwarz: dunkle Grautöne zeigt das Panel viel zu hell
# Dunkle Flächen zeigt das Panel viel zu hell – darum keine Füllungen,
# nur feine Umrandungen auf Schwarz.
CARD_EDGE = (62, 57, 51)
CREAM = (245, 240, 232)
MUTED = (158, 149, 138)
DIM = (104, 97, 89)
TRACK = (52, 48, 44)
ORANGE = (222, 122, 88)
ORANGE_HI = (245, 160, 122)
ORANGE_GLOW = (60, 34, 26)
GREEN = (128, 205, 152)
GREEN_GLOW = (34, 58, 42)
AMBER = (236, 188, 92)
RED = (234, 104, 92)

ROUNDED = "/System/Library/Fonts/SFNSRounded.ttf"
_font_cache = {}


def font(size, weight=500):
    key = (size, weight)
    if key not in _font_cache:
        try:
            f = ImageFont.truetype(ROUNDED, size * S)
            f.set_variation_by_axes([400, weight])       # [GRAD, Weight]
        except (OSError, ValueError):
            f = ImageFont.truetype("/System/Library/Fonts/HelveticaNeue.ttc", size * S,
                                   index=1 if weight >= 600 else 0)
        _font_cache[key] = f
    return _font_cache[key]


WOCHENTAGE = ["Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag"]
MONATE = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August",
          "September", "Oktober", "November", "Dezember"]


class Canvas:
    """Zeichenfläche in Display-Koordinaten mit Supersampling."""

    def __init__(self):
        self.img = Image.new("RGB", (W * S, H * S), BG)
        self.d = ImageDraw.Draw(self.img)

    # -- Grundformen
    def rrect(self, x0, y0, x1, y1, r, fill=None, outline=None, width=1):
        self.d.rounded_rectangle((x0 * S, y0 * S, x1 * S, y1 * S), radius=r * S,
                                 fill=fill, outline=outline, width=width * S)

    def circle(self, cx, cy, r, fill):
        self.d.ellipse(((cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S), fill=fill)

    def arc(self, cx, cy, r, width, start, end, fill, caps=True):
        """Bogen mit runden Enden; Winkel in Grad, 0 = oben, im Uhrzeigersinn."""
        if end - start <= 0.5:
            return
        box = ((cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S)
        self.d.arc(box, start - 90, end - 90, fill=fill, width=width * S)
        if caps:
            rm = r - width / 2
            for a in (start, end):
                rad = math.radians(a - 90)
                self.circle(cx + rm * math.cos(rad), cy + rm * math.sin(rad), width / 2, fill)

    def bar(self, x0, y, x1, h, pct, color):
        self.rrect(x0, y, x1, y + h, h / 2, fill=TRACK)
        w = (x1 - x0) * max(0, min(100, pct)) / 100
        if w >= h:
            self.rrect(x0, y, x0 + w, y + h, h / 2, fill=color)

    # -- Text
    def text(self, x, y, s, f, fill, anchor="la"):
        self.d.text((x * S, y * S), s, font=f, fill=fill, anchor=anchor)

    def tlen(self, s, f):
        return self.d.textlength(s, font=f) / S

    def fit(self, s, f, maxw):
        if self.tlen(s, f) <= maxw:
            return s
        while s and self.tlen(s + "…", f) > maxw:
            s = s[:-1]
        return s.rstrip() + "…"

    def wrap(self, s, f, maxw, maxlines):
        words, lines, line = s.split(), [], ""
        for i, w in enumerate(words):
            t = (line + " " + w).strip()
            if self.tlen(t, f) <= maxw:
                line = t
                continue
            if line:
                lines.append(line)
            line = w
            if len(lines) == maxlines:
                lines[-1] = self.fit(lines[-1] + " " + " ".join(words[i:]), f, maxw)
                return lines
        if line:
            lines.append(self.fit(line, f, maxw))
        return lines[:maxlines]

    def spaced(self, x, y, s, f, fill, spacing=1.2):
        """Kleine Großbuchstaben mit Laufweite."""
        for ch in s:
            self.text(x, y, ch, f, fill)
            x += self.tlen(ch, f) + spacing
        return x

    def spark(self, cx, cy, r, color, angle=0.0):
        """Claude-Stern: zwölf abgerundete Strahlen, abwechselnd lang und kurz."""
        for k in range(12):
            a = math.radians(angle + k * 30)
            length = r if k % 2 == 0 else r * 0.68
            x1, y1 = cx + math.cos(a) * r * 0.18, cy + math.sin(a) * r * 0.18
            x2, y2 = cx + math.cos(a) * length, cy + math.sin(a) * length
            w = r * 0.22
            self.d.line((x1 * S, y1 * S, x2 * S, y2 * S), fill=color, width=round(w * S))
            self.circle(x2, y2, w / 2, color)
        self.circle(cx, cy, r * 0.2, color)

    def done(self):
        return self.img.resize((W, H), Image.LANCZOS)


def pct_color(p):
    return GREEN if p < 60 else AMBER if p < 85 else RED


def fmt_reset(seconds):
    m = max(0, int(seconds)) // 60
    if m < 60:
        return f"{m} min"
    h, m = divmod(m, 60)
    if h < 24:
        return f"{h}h {m:02d}"
    d, h = divmod(h, 24)
    return f"{d}d {h}h"


def fmt_timer(secs, fast):
    if secs >= 3600:
        h, m = divmod(secs // 60, 60)
        return f"{h}:{m:02d} h"
    if fast:
        return f"{secs // 60}:{secs % 60:02d}"
    return f"{secs // 60} min" if secs >= 60 else "gerade eben"


# ------------------------------------------------------------------ Bausteine
def header(c, now, active, tick, show_clock):
    c.spark(17, 18, 9.5, ORANGE, angle=(tick * 15) if active else 0)
    c.text(33, 18, "Claude", font(16, 650), CREAM, anchor="lm")
    if show_clock:
        c.text(W - 10, 18, now.strftime("%H:%M"), font(16, 560), MUTED, anchor="rm")


def limits(c, use, now_utc):
    if not use:
        c.text(W / 2, 214, "Limit gerade nicht verfügbar", font(12, 500), DIM, anchor="mm")
        return
    cols = ((10, 114, "5 STD", use["five"]), (126, 230, "WOCHE", use["week"]))
    for x0, x1, label, (pct, reset) in cols:
        c.spaced(x0, 188, label, font(10, 700), DIM)
        col = pct_color(pct)
        c.text(x0, 222, f"{pct}", font(24, 700), col, anchor="ls")
        px = x0 + c.tlen(f"{pct}", font(24, 700)) + 1
        c.text(px, 222, "%", font(13, 650), col, anchor="ls")
        if reset:
            c.text(x1, 222, fmt_reset((reset - now_utc).total_seconds()), font(11, 560), MUTED,
                   anchor="rs")
        elif use.get("stale"):
            c.text(x1, 222, use["stale"].strftime("%H:%M"), font(11, 560), DIM, anchor="rs")
        c.bar(x0, 227, x1, 5, pct, col)


# ------------------------------------------------------------------ Ansichten
def render_active(info, use, now, now_utc, tick, fast):
    c = Canvas()
    header(c, now, True, tick, True)

    # Fortschrittsring links
    cx, cy, r, w = 52, 84, 40, 8
    c.arc(cx, cy, r, w, 0, 360, TRACK, caps=False)
    plan = info["plan"]
    if plan:
        done = sum(1 for _, s in plan if s == "completed")
        frac = done / len(plan)
        c.arc(cx, cy, r, w, 0, 360 * frac, ORANGE)
        if 0 < frac < 1:                                  # leuchtender Kopf am Ende
            a = math.radians(360 * frac - 90)
            rm = r - w / 2
            c.circle(cx + rm * math.cos(a), cy + rm * math.sin(a), w / 2 + 0.6, ORANGE_HI)
        c.text(cx, cy - 4, f"{done}", font(30, 700), CREAM, anchor="mm")
        c.text(cx, cy + 19, f"von {len(plan)}", font(11, 600), MUTED, anchor="mm")
    else:                                                 # ohne Plan: kreisender Bogen
        start = (tick * 40) % 360
        c.arc(cx, cy, r, w, start, start + 70, ORANGE)
        c.text(cx, cy, "···", font(22, 700), MUTED, anchor="mm")

    # rechts: Status, Name, Zeit
    x = 104
    pulse = tick % 2 == 0
    c.circle(x + 4, 51, 5.5 if pulse else 4.2, GREEN_GLOW)
    c.circle(x + 4, 51, 3.4, GREEN)
    status = "ARBEITET" + (f"  +{info['n_busy'] - 1}" if info["n_busy"] > 1 else "")
    c.spaced(x + 13, 45, status, font(10, 700), GREEN)
    lines = c.wrap(info["name"], font(17, 680), W - x - 10, 2)
    y = 60
    for line in lines:
        c.text(x, y, line, font(17, 680), CREAM)
        y += 21
    c.text(x, max(y + 4, 104), fmt_timer(info["secs"], fast), font(15, 560), MUTED)

    # Karte mit dem aktuellen Schritt
    cur = None
    if plan:
        cur = next((t for t, s in plan if s == "in_progress"), None) \
            or next((t for t, s in plan if s == "pending"), None)
        step, col = (cur, CREAM) if cur else ("Alle Schritte erledigt", GREEN)
    else:
        step, col = (info.get("prompt") or "Denkt nach …", CREAM)
    c.rrect(8, 134, W - 8, 178, 12, outline=CARD_EDGE, width=1)
    c.rrect(15, 143, 18.5, 169, 1.75, fill=ORANGE if cur or not plan else GREEN)
    step_lines = c.wrap(step, font(15, 600), W - 44, 2)
    sy = 156 - (len(step_lines) - 1) * 9.5
    for line in step_lines:
        c.text(27, sy, line, font(15, 600), col, anchor="lm")
        sy += 19

    limits(c, use, now_utc)
    return c.done()


def render_idle(use, now, now_utc):
    c = Canvas()
    header(c, now, False, 0, False)
    c.text(W - 10, 18, "bereit", font(13, 600), DIM, anchor="rm")
    c.circle(W - 10 - c.tlen("bereit", font(13, 600)) - 8, 18, 3, GREEN)

    c.text(W / 2, 76, now.strftime("%H:%M"), font(66, 330), CREAM, anchor="mm")
    datum = f"{WOCHENTAGE[now.weekday()]}, {now.day}. {MONATE[now.month - 1]}"
    size = 19
    while size > 15 and c.tlen(datum, font(size, 600)) > W - 16:
        size -= 1
    c.text(W / 2, 128, datum, font(size, 600), MUTED, anchor="mm")
    t = "Alles ruhig"
    pw = c.tlen(t, font(16, 620)) / 2 + 20
    c.rrect(W / 2 - pw, 146, W / 2 + pw, 176, 15, outline=CARD_EDGE, width=1)
    c.text(W / 2, 161, t, font(16, 620), MUTED, anchor="mm")

    limits(c, use, now_utc)
    return c.done()


def render(info, use, tick=0, fast=False):
    """info = None (keine aktive Sitzung) oder dict(name, secs, plan, prompt, n_busy)."""
    now = datetime.now()
    now_utc = datetime.now().astimezone()
    if info is None:
        return render_idle(use, now, now_utc)
    return render_active(info, use, now, now_utc, tick, fast)
