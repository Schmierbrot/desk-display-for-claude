#!/usr/bin/env python3
"""Hintergründe für das Installationsprogramm (hell und dunkel), unten links mit dem App-Zeichen."""
import math
import os
import sys

from PIL import Image, ImageDraw, ImageFilter

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
W, H = 620, 418   # Installer-Fenster: Inhaltsbereich (2x für Retina)
S = 2


def ring(img, cx, cy, r, w, c1, c2):
    d = ImageDraw.Draw(img)
    start, end, steps = -65, 245, 120
    for i in range(steps):
        t = i / (steps - 1)
        a0 = start + (end - start) * i / steps
        a1 = start + (end - start) * (i + 1) / steps + 0.8
        col = tuple(int(c1[k] + (c2[k] - c1[k]) * t) for k in range(3)) + (255,)
        d.arc((cx - r, cy - r, cx + r, cy + r), a0, a1, fill=col, width=w)
    for ang, col in ((start, c1), (end, c2)):
        rad = math.radians(ang)
        px, py = cx + (r - w / 2) * math.cos(rad), cy + (r - w / 2) * math.sin(rad)
        d.ellipse((px - w / 2, py - w / 2, px + w / 2, py + w / 2), fill=col + (255,))
    d.ellipse((cx - r * 0.25, cy - r * 0.25, cx + r * 0.25, cy + r * 0.25), fill=c2 + (255,))


def make(dark):
    img = Image.new("RGBA", (W * S, H * S), (11, 13, 20, 255) if dark else (246, 247, 252, 255))
    glow = Image.new("RGBA", img.size, (0, 0, 0, 0))
    g = ImageDraw.Draw(glow)
    a = 120 if dark else 70
    g.ellipse((-200 * S, 180 * S, 260 * S, 640 * S), fill=(91, 140, 255, a))
    g.ellipse((60 * S, 300 * S, 420 * S, 660 * S), fill=(176, 107, 255, a))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(70 * S)))
    ring(img, 92 * S, 330 * S, 46 * S, 13 * S, (91, 140, 255), (176, 107, 255))
    name = "background-dark.png" if dark else "background.png"
    img.save(os.path.join(OUT, name))


make(False)
make(True)
print("Hintergründe erzeugt")
