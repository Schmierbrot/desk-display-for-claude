using SkiaSharp;

namespace ClaudeDisplay.Core;

/// <summary>Farben fürs Display – kommen aus dem aktiven Theme (Einstellungen → Darstellung).</summary>
public static class Pal
{
    public static DisplayTheme Theme = Themes.Preset("neon").Display;
    public static readonly SKColor Bg = new(0, 0, 0);
    public static SKColor Edge => Theme.Edge.Sk;
    public static SKColor Text => Theme.Text.Sk;
    public static SKColor Muted => Theme.Muted.Sk;
    public static SKColor Dim => Theme.Dim.Sk;
    public static SKColor Track => Theme.Track.Sk;
    public static SKColor Accent => Theme.Accent.Sk;
    public static SKColor Good => Theme.Good.Sk;
    public static SKColor GoodGlow => Theme.Good.Mix(new RGB(0, 0, 0), 0.75).Sk;
    public static SKColor ForPct(int p) => Theme.ForPct(p).Sk;
}

/// <summary>Zeichenfläche 240×240 für das Display.</summary>
public sealed class Canvas : IDisposable
{
    public const int Size = 240;
    public SKBitmap Bitmap { get; } = new(Size, Size, SKColorType.Rgba8888, SKAlphaType.Premul);
    readonly SKCanvas c;

    static readonly string[] Families = ["Segoe UI Variable Display", "Segoe UI Variable Text", "Segoe UI", ".SF NS Rounded", "SF Pro Rounded", "Helvetica Neue", "Arial"];
    static readonly Dictionary<(float, int), SKFont> fonts = [];
    static string? family;

    public Canvas()
    {
        c = new SKCanvas(Bitmap);
        c.Clear(Pal.Bg);
    }

    public void Dispose() { c.Dispose(); Bitmap.Dispose(); }

    public static SKFont Font(float size, int weight)
    {
        size = MathF.Max(4, MathF.Round(size * 4) / 4);   // begrenzt den Schriften-Zwischenspeicher
        if (fonts.TryGetValue((size, weight), out var f)) return f;
        if (family == null)
        {
            var installed = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
            family = Families.FirstOrDefault(installed.Contains) ?? Families.Last();
        }
        var tf = SKFontManager.Default.MatchFamily(family, new SKFontStyle(weight, 5, SKFontStyleSlant.Upright))
                 ?? SKTypeface.FromFamilyName(family, weight, 5, SKFontStyleSlant.Upright);
        f = new SKFont(tf, size) { Edging = SKFontEdging.Antialias, Subpixel = true };
        return fonts[(size, weight)] = f;
    }

    static SKPaint Fill(SKColor col) => new() { Color = col, IsAntialias = true, Style = SKPaintStyle.Fill };
    static SKPaint Stroke(SKColor col, float w, bool round = true) =>
        new() { Color = col, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = w, StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt };

    public void Circle(float cx, float cy, float r, SKColor col) { using var p = Fill(col); c.DrawCircle(cx, cy, r, p); }

    public void RRect(float x0, float y0, float x1, float y1, float r, SKColor? fill = null, SKColor? stroke = null, float width = 1)
    {
        if (fill is { } f) { using var p = Fill(f); c.DrawRoundRect(new SKRect(x0, y0, x1, y1), r, r, p); }
        if (stroke is { } s)
        {
            using var p = Stroke(s, width, false);
            float h = width / 2;
            c.DrawRoundRect(new SKRect(x0 + h, y0 + h, x1 - h, y1 - h), Math.Max(0, r - h), Math.Max(0, r - h), p);
        }
    }

    /// <summary>Bogen; Winkel in Grad, 0 = oben, im Uhrzeigersinn. r = Außenradius.</summary>
    public void Arc(float cx, float cy, float r, float width, float start, float end, SKColor col, bool caps = true)
    {
        if (end - start <= 0.5f) return;
        using var p = Stroke(col, width, caps);
        float rm = r - width / 2;
        var oval = new SKRect(cx - rm, cy - rm, cx + rm, cy + rm);
        if (end - start >= 360) { c.DrawOval(oval, p); return; }
        using var path = new SKPath();
        path.AddArc(oval, start - 90, end - start);
        c.DrawPath(path, p);
    }

    /// <summary>Bogen mit Farbverlauf von c1 nach c2, runde Enden.</summary>
    public void GradientArc(float cx, float cy, float r, float width, float start, float end, RGB c1, RGB c2)
    {
        if (end - start <= 0.5f) return;
        int steps = Math.Max(2, (int)((end - start) / 4));
        for (int i = 0; i < steps; i++)
        {
            float a0 = start + (end - start) * i / steps;
            float a1 = Math.Min(end, start + (end - start) * (i + 1) / steps + 0.6f);
            Arc(cx, cy, r, width, a0, a1, c1.Mix(c2, (double)i / Math.Max(1, steps - 1)).Sk, caps: false);
        }
        float rm = r - width / 2;
        foreach (var (a, col) in new[] { (start, c1), (end, c2) })
        {
            double rad = (a - 90) * Math.PI / 180;
            Circle(cx + rm * (float)Math.Cos(rad), cy + rm * (float)Math.Sin(rad), width / 2, col.Sk);
        }
    }

    /// <summary>Logo oben links im gewählten Stil; tick animiert es während der Arbeit.</summary>
    public void Logo(LogoStyle style, float cx, float cy, bool active, int tick, float k = 1)
    {
        var t = Pal.Theme;
        switch (style)
        {
            case LogoStyle.Ring:
                if (active) { float s = tick * 30 % 360; GradientArc(cx, cy, 9 * k, 3 * k, s, s + 290, t.Accent, t.Accent2); }
                else GradientArc(cx, cy, 9 * k, 3 * k, 0, 359.4f, t.Accent, t.Accent2);
                break;
            case LogoStyle.Spark:
                Spark(cx, cy, 9.5f * k, Pal.Accent, active ? tick * 15 : 0);
                break;
            case LogoStyle.Dot:
                Circle(cx, cy, (active && tick % 2 == 0 ? 8 : 6.5f) * k, t.Accent.Mix(new RGB(0, 0, 0), 0.7).Sk);
                Circle(cx, cy, 4.5f * k, Pal.Accent);
                break;
        }
    }

    public void Bar(float x0, float y, float x1, float h, int pct, SKColor col)
    {
        RRect(x0, y, x1, y + h, h / 2, fill: Pal.Track);
        float w = (x1 - x0) * Math.Clamp(pct, 0, 100) / 100f;
        if (w >= h) RRect(x0, y, x0 + w, y + h, h / 2, fill: col);
    }

    public enum H { Left, Center, Right }
    public enum V { Top, Middle, Baseline }

    public float TLen(string s, SKFont f) { using var p = new SKPaint(f); return p.MeasureText(s); }

    public void Text(float x, float y, string s, SKFont f, SKColor col, H h = H.Left, V v = V.Top)
    {
        float w = TLen(s, f);
        float left = h == H.Left ? x : h == H.Center ? x - w / 2 : x - w;
        var m = f.Metrics;
        float baseline = v switch
        {
            V.Top => y - m.Ascent,
            V.Middle => y - (m.Ascent + m.Descent) / 2,
            _ => y,
        };
        using var p = Fill(col);
        c.DrawText(s, left, baseline, f, p);
    }

    public string Fit(string s, SKFont f, float maxw)
    {
        if (TLen(s, f) <= maxw) return s;
        while (s.Length > 0 && TLen(s + "…", f) > maxw) s = s[..^1];
        return s.TrimEnd() + "…";
    }

    public List<string> Wrap(string s, SKFont f, float maxw, int maxLines)
    {
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var line = "";
        for (int i = 0; i < words.Length; i++)
        {
            var t = line.Length == 0 ? words[i] : line + " " + words[i];
            if (TLen(t, f) <= maxw) { line = t; continue; }
            if (line.Length > 0) lines.Add(line);
            line = words[i];
            if (lines.Count == maxLines)
            {
                lines[^1] = Fit(lines[^1] + " " + string.Join(' ', words[i..]), f, maxw);
                return lines;
            }
        }
        if (line.Length > 0) lines.Add(Fit(line, f, maxw));
        return lines.Take(maxLines).ToList();
    }

    public float Spaced(float x, float y, string s, SKFont f, SKColor col, float spacing = 1.2f)
    {
        foreach (var ch in s)
        {
            Text(x, y, ch.ToString(), f, col);
            x += TLen(ch.ToString(), f) + spacing;
        }
        return x;
    }

    /// <summary>Claude-Stern: zwölf abgerundete Strahlen, abwechselnd lang und kurz.</summary>
    public void Spark(float cx, float cy, float r, SKColor col, float angle = 0)
    {
        using var p = Stroke(col, r * 0.22f);
        for (int k = 0; k < 12; k++)
        {
            double a = (angle + k * 30) * Math.PI / 180;
            float len = k % 2 == 0 ? r : r * 0.68f;
            c.DrawLine(cx + (float)Math.Cos(a) * r * 0.18f, cy + (float)Math.Sin(a) * r * 0.18f,
                       cx + (float)Math.Cos(a) * len, cy + (float)Math.Sin(a) * len, p);
        }
        Circle(cx, cy, r * 0.2f, col);
    }

    /// <summary>RGB-Bytes (3 pro Pixel) für die Übertragung.</summary>
    public byte[] Rgb()
    {
        var src = Bitmap.GetPixelSpan();
        var out_ = new byte[Size * Size * 3];
        for (int i = 0, j = 0; i < Size * Size; i++, j += 3)
        {
            out_[j] = src[i * 4]; out_[j + 1] = src[i * 4 + 1]; out_[j + 2] = src[i * 4 + 2];
        }
        return out_;
    }
}

public static class Renderer
{
    static readonly string[] Wochentage = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"];
    static readonly string[] Monate = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];
    static readonly object renderLock = new();

    /// <summary>Zeichnet eine Ansicht im angegebenen Theme und Layout (Standard: Einstellungen).</summary>
    public static Canvas Render(SessionInfo? s, Usage? use, int tick, DisplayTheme? theme = null, DisplayLayout? layout = null)
    {
        lock (renderLock)
        {
            Pal.Theme = theme ?? AppSettings.Shared.DisplayTheme;
            var l = layout ?? AppSettings.Shared.DisplayLayout;
            var c = new Canvas();
            var d = new WidgetData(s, use, tick, DateTime.Now);
            foreach (var item in (s != null ? l.Active : l.Idle).ToArray())
                if (item.Visible) Widgets.Draw(c, item, d);
            return c;
        }
    }

    public static string FmtReset(TimeSpan t)
    {
        int m = Math.Max(0, (int)t.TotalMinutes);
        if (m < 60) return $"{m} min";
        int h = m / 60;
        if (h < 24) return $"{h}h {m % 60:00}";
        return $"{h / 24}d {h % 24}h";
    }

    public static string FmtTimer(int s) => s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00} h" : $"{s / 60}:{s % 60:00}";

    public static string DateText(DateTime now) => $"{Wochentage[(int)now.DayOfWeek]}, {now.Day}. {Monate[now.Month - 1]}";
}
