#!/usr/bin/env python3
"""Erzeugt das App-Symbol (1024 px): dunkler Würfel-Bildschirm mit Claude-Stern."""
import math
import sys

from PIL import Image, ImageDraw, ImageFilter

S = 1024
out = sys.argv[1] if len(sys.argv) > 1 else "icon_1024.png"

img = Image.new("RGBA", (S, S), (0, 0, 0, 0))

# Schatten
shadow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(shadow).rounded_rectangle((100, 120, 924, 944), 190, fill=(0, 0, 0, 120))
img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))

# Gehäuse mit Verlauf
body = Image.new("RGBA", (S, S), (0, 0, 0, 0))
grad = Image.new("RGBA", (S, S))
gd = ImageDraw.Draw(grad)
for y in range(S):
    t = y / S
    c = (int(40 - 28 * t), int(44 - 30 * t), int(62 - 40 * t), 255)
    gd.line((0, y, S, y), fill=c)
mask = Image.new("L", (S, S), 0)
ImageDraw.Draw(mask).rounded_rectangle((100, 100, 924, 924), 185, fill=255)
body.paste(grad, (0, 0), mask)
img.alpha_composite(body)

# Bildschirm
screen = Image.new("RGBA", (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(screen)
sd.rounded_rectangle((172, 172, 852, 852), 90, fill=(6, 7, 12, 255))
img.alpha_composite(screen)

# Neon-Leuchten
glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd2 = ImageDraw.Draw(glow)
gd2.ellipse((250, 250, 774, 774), fill=(110, 120, 255, 120))
img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(90)))

# Ring mit Farbverlauf (Blau -> Violett), oben offen wie ein Fortschrittsring
ring = Image.new("RGBA", (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(ring)
cx = cy = S / 2
R, W = 230, 62
c1, c2 = (91, 140, 255), (176, 107, 255)
start, end = -90 + 25, -90 + 335
steps = 160
for i in range(steps):
    t = i / (steps - 1)
    a0 = start + (end - start) * i / steps
    a1 = start + (end - start) * (i + 1) / steps + 0.8
    col = tuple(int(c1[k] + (c2[k] - c1[k]) * t) for k in range(3)) + (255,)
    d.arc((cx - R, cy - R, cx + R, cy + R), a0, a1, fill=col, width=W)
for ang, col in ((start, c1), (end, c2)):
    rad = math.radians(ang)
    px, py = cx + (R - W / 2) * math.cos(rad), cy + (R - W / 2) * math.sin(rad)
    d.ellipse((px - W / 2, py - W / 2, px + W / 2, py + W / 2), fill=col + (255,))
# leuchtender Punkt in der Mitte
d.ellipse((cx - 58, cy - 58, cx + 58, cy + 58), fill=(176, 107, 255, 255))
img.alpha_composite(ring)
dot_glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(dot_glow).ellipse((cx - 110, cy - 110, cx + 110, cy + 110), fill=(176, 107, 255, 90))
img.alpha_composite(dot_glow.filter(ImageFilter.GaussianBlur(40)))
img.alpha_composite(ring)

# Glanzkante oben
hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
ImageDraw.Draw(hl).rounded_rectangle((100, 100, 924, 924), 185, outline=(255, 255, 255, 40), width=4)
img.alpha_composite(hl)

img.save(out)
print("Symbol gespeichert:", out)
