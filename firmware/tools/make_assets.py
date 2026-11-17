#!/usr/bin/env python3
"""Erzeugt src/assets.h für die eigenständige Uhr der Firmware.

Alles wird als 4-Bit-Helligkeitsmaske gespeichert; die Farbe setzt die Firmware
zur Laufzeit aus dem Theme ein, das ihr die App schickt.

- Glyphen in SF Pro Rounded: Uhrzeit, Datum, Titel (ASCII + Umlaute)
- feste Texte als fertige Masken: „Kein Rechner aktiv“ und der Hinweis darunter

Aufruf (vom Ordner firmware aus):  /usr/local/bin/python3 tools/make_assets.py
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import design  # noqa: E402  (gleiche Schrift wie die Apps)

OUT = os.path.join(HERE, "..", "src", "assets.h")

WOCHENTAGE = ["Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag"]
MONATE = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August",
          "September", "Oktober", "November", "Dezember"]
TITLE_CHARS = "".join(chr(c) for c in range(32, 127)) + "ÄÖÜäöüß€·–"

# Lage wie in design.render_idle (Mitte der Zeile)
CLOCK_Y, DATE_Y, TITLE_Y = 76, 128, 18


def pil_font(size, weight):
    f = ImageFont.truetype(design.ROUNDED, size)
    f.set_variation_by_axes([400, weight])
    return f


def pack4(img):
    """L-Bild → 4 Bit Alpha, zwei Pixel pro Byte, Zeilen auf Byte ausgerichtet."""
    w, h = img.size
    px = img.load()
    data = bytearray()
    for y in range(h):
        row = [min(15, (px[x, y] + 8) // 17) for x in range(w)]
        if w % 2:
            row.append(0)
        for i in range(0, len(row), 2):
            data.append(row[i] << 4 | row[i + 1])
    return data


def glyphs(font, chars):
    table, data = [], bytearray()
    for ch in sorted(set(chars), key=ord):
        x0, y0, x1, y1 = font.getbbox(ch, anchor="ls")
        w, h = max(0, x1 - x0), max(0, y1 - y0)
        off = len(data)
        if w and h:
            img = Image.new("L", (w, h), 0)
            ImageDraw.Draw(img).text((-x0, -y0), ch, font=font, fill=255, anchor="ls")
            data += pack4(img)
        table.append((ord(ch), w, h, x0, y0, round(font.getlength(ch)), off))
    return table, data


def baseline_for_middle(font, y_mid):
    asc, desc = font.getmetrics()
    return round(y_mid + (asc - desc) / 2)


def text_mask(text, font):
    """Fertige Maske eines festen Textes (für zentrierte Ausgabe)."""
    x0, y0, x1, y1 = font.getbbox(text, anchor="lt")
    img = Image.new("L", (x1 - x0 + 2, y1 - y0 + 2), 0)
    ImageDraw.Draw(img).text((1 - x0, 1 - y0), text, font=font, fill=255, anchor="lt")
    return img


def c_array(name, data, per_line=24):
    lines = [f"static const uint8_t {name}[] PROGMEM = {{"]
    for i in range(0, len(data), per_line):
        lines.append("  " + ",".join(str(b) for b in data[i:i + per_line]) + ",")
    lines.append("};")
    return "\n".join(lines)


def font_block(prefix, font, chars, baseline):
    table, data = glyphs(font, chars)
    asc, desc = font.getmetrics()
    rows = ",\n".join(f"  {{{cp}, {w}, {h}, {x0}, {y0}, {adv}, {off}}}" for cp, w, h, x0, y0, adv, off in table)
    return "\n".join([
        c_array(f"{prefix}_BITS", data),
        f"static const Glyph {prefix}_GLYPHS[] PROGMEM = {{\n{rows}\n}};",
        f"static const GFont {prefix} = {{{prefix}_GLYPHS, {len(table)}, {prefix}_BITS, {baseline}, {asc}, {desc}}};",
    ])


def mask_block(name, img, cy):
    w, h = img.size
    return "\n".join([
        c_array(f"{name}_BITS", pack4(img)),
        f"static const AMask {name} = {{{name}_BITS, {w}, {h}, {cy - h // 2}}};",
    ])


def main():
    clock = pil_font(66, 330)
    longest = max((f"{w}, {d}. {m}" for w in WOCHENTAGE for m in MONATE for d in (28, 30)),
                  key=lambda s: pil_font(19, 600).getlength(s))
    size = 19
    while size > 14 and pil_font(size, 600).getlength(longest) > 224:
        size -= 1
    date = pil_font(size, 600)
    title = pil_font(16, 650)

    pill = text_mask("Kein Rechner aktiv", pil_font(16, 620))
    hint1 = text_mask("Sitzungen und Limits erscheinen,", pil_font(12, 560))
    hint2 = text_mask("sobald Mac oder PC läuft.", pil_font(12, 560))

    parts = [
        "// Automatisch erzeugt von tools/make_assets.py – nicht von Hand ändern.",
        "#pragma once",
        "#include <Arduino.h>",
        "struct Glyph { uint16_t cp; uint8_t w, h; int8_t x0, y0; uint8_t adv; uint32_t off; };",
        "struct GFont { const Glyph *glyphs; uint16_t count; const uint8_t *bits; int16_t baseline; int16_t asc, desc; };",
        "struct AMask { const uint8_t *bits; uint16_t w, h; int16_t y; };",
        f"#define PILL_W {pill.size[0] + 40}",
        font_block("FONT_CLOCK", clock, "0123456789:", baseline_for_middle(clock, CLOCK_Y)),
        font_block("FONT_DATE", date, "".join(WOCHENTAGE + MONATE) + "0123456789,. ", baseline_for_middle(date, DATE_Y)),
        font_block("FONT_TITLE", title, TITLE_CHARS, baseline_for_middle(title, TITLE_Y)),
        mask_block("MASK_PILL", pill, 161),
        mask_block("MASK_HINT1", hint1, 198),
        mask_block("MASK_HINT2", hint2, 215),
    ]
    with open(OUT, "w") as f:
        f.write("\n\n".join(parts) + "\n")
    print(f"{OUT}: Datumsschrift {size} px, {os.path.getsize(OUT) // 1024} KB Quelltext")


if __name__ == "__main__":
    main()
