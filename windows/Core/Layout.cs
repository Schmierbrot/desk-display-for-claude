using System.Text.Json.Serialization;
using SkiaSharp;

namespace ClaudeDisplay.Core;

/// <summary>Bausteine des Displays. Jeder zeichnet sich in seinen Rahmen und passt Schrift und Größe an die Höhe an.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WidgetKind { Header, Clock, Ring, Status, Name, Timer, Step, Limit5, LimitWeek, Ready, BigClock, Date, Calm, Label, Line,
    Context, Model, Branch, Activity }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TextAlign { Left, Center, Right }

/// <summary>Farbrolle aus dem Display-Theme (für eigene Texte und Linien).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ColorRole { Text, Muted, Dim, Accent, Accent2, Good, Edge }

public static class WidgetInfo
{
    public static string Label(WidgetKind k) => k switch
    {
        WidgetKind.Header => "Logo und Titel",
        WidgetKind.Clock => "Uhrzeit klein",
        WidgetKind.Ring => "Fortschrittsring",
        WidgetKind.Status => "„Arbeitet“",
        WidgetKind.Name => "Sitzungsname",
        WidgetKind.Timer => "Laufzeit",
        WidgetKind.Step => "Aktueller Schritt",
        WidgetKind.Limit5 => "Limit 5 Stunden",
        WidgetKind.LimitWeek => "Limit Woche",
        WidgetKind.Ready => "„bereit“",
        WidgetKind.BigClock => "Uhrzeit groß",
        WidgetKind.Date => "Datum",
        WidgetKind.Calm => "„Alles ruhig“",
        WidgetKind.Label => "Eigener Text",
        WidgetKind.Context => "Kontext-Füllstand",
        WidgetKind.Model => "Modell",
        WidgetKind.Branch => "Git-Branch",
        WidgetKind.Activity => "Tut gerade",
        _ => "Linie",
    };

    public static string Icon(WidgetKind k) => k switch
    {
        WidgetKind.Header => "✦",
        WidgetKind.Clock or WidgetKind.BigClock => "◷",
        WidgetKind.Ring => "◌",
        WidgetKind.Status or WidgetKind.Ready => "●",
        WidgetKind.Name => "Aa",
        WidgetKind.Timer => "⏱",
        WidgetKind.Step => "▤",
        WidgetKind.Limit5 or WidgetKind.LimitWeek => "▰",
        WidgetKind.Date => "▦",
        WidgetKind.Calm => "⬭",
        WidgetKind.Label => "T",
        WidgetKind.Context => "▮",
        WidgetKind.Model => "◈",
        WidgetKind.Branch => "⑂",
        WidgetKind.Activity => "⚒",
        _ => "━",
    };

    public static bool Alignable(WidgetKind k) => k is WidgetKind.Clock or WidgetKind.Name or WidgetKind.Timer or WidgetKind.Ready
        or WidgetKind.BigClock or WidgetKind.Date or WidgetKind.Label or WidgetKind.Model or WidgetKind.Branch or WidgetKind.Activity;

    public static string Label(ColorRole r) => r switch
    {
        ColorRole.Text => "Text", ColorRole.Muted => "Gedämpft", ColorRole.Dim => "Schwach", ColorRole.Accent => "Akzent",
        ColorRole.Accent2 => "Akzent 2", ColorRole.Good => "Grün", _ => "Rahmen",
    };

    public static RGB Color(ColorRole r, DisplayTheme t) => r switch
    {
        ColorRole.Text => t.Text, ColorRole.Muted => t.Muted, ColorRole.Dim => t.Dim, ColorRole.Accent => t.Accent,
        ColorRole.Accent2 => t.Accent2, ColorRole.Good => t.Good, _ => t.Edge,
    };
}

public sealed class LayoutItem
{
    public string Id { get; set; } = "";
    public WidgetKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public bool Visible { get; set; } = true;
    public TextAlign? Align { get; set; }
    public string? Text { get; set; }
    public ColorRole? Color { get; set; }

    public LayoutItem() { }
    public LayoutItem(WidgetKind kind, double x, double y, double w, double h, bool visible = true, TextAlign? align = null,
                      string? id = null, string? text = null, ColorRole? color = null)
    {
        Id = id ?? kind.ToString().ToLowerInvariant(); Kind = kind; X = x; Y = y; W = w; H = h;
        Visible = visible; Align = align; Text = text; Color = color;
    }

    [JsonIgnore] public SKRect Rect => new((float)X, (float)Y, (float)(X + W), (float)(Y + H));
    [JsonIgnore] public bool IsCustom => Kind is WidgetKind.Label or WidgetKind.Line;

    public const double MinW = 8, MinH = 2;

    /// <summary>Innerhalb der 240 × 240 Pixel halten.</summary>
    public void Clamp()
    {
        W = Math.Min(240, Math.Max(MinW, Math.Round(W))); H = Math.Min(240, Math.Max(MinH, Math.Round(H)));
        X = Math.Min(240 - W, Math.Max(0, Math.Round(X))); Y = Math.Min(240 - H, Math.Max(0, Math.Round(Y)));
    }

    public LayoutItem Clone() => (LayoutItem)MemberwiseClone();

    public bool SameAs(LayoutItem o) => Id == o.Id && Kind == o.Kind && X == o.X && Y == o.Y && W == o.W && H == o.H
        && Visible == o.Visible && Align == o.Align && Text == o.Text && Color == o.Color;
}

/// <summary>Aufbau beider Ansichten.</summary>
public sealed class DisplayLayout
{
    public List<LayoutItem> Active { get; set; } = [];
    public List<LayoutItem> Idle { get; set; } = [];

    public static readonly WidgetKind[] ActiveKinds = [WidgetKind.Header, WidgetKind.Clock, WidgetKind.Ring, WidgetKind.Status,
        WidgetKind.Name, WidgetKind.Timer, WidgetKind.Step, WidgetKind.Limit5, WidgetKind.LimitWeek,
        WidgetKind.Context, WidgetKind.Model, WidgetKind.Branch, WidgetKind.Activity];
    public static readonly WidgetKind[] IdleKinds = [WidgetKind.Header, WidgetKind.Ready, WidgetKind.BigClock, WidgetKind.Date,
        WidgetKind.Calm, WidgetKind.Limit5, WidgetKind.LimitWeek];

    public DisplayLayout Clone() => new() { Active = Active.Select(i => i.Clone()).ToList(), Idle = Idle.Select(i => i.Clone()).ToList() };

    public bool SameAs(DisplayLayout o) =>
        Active.Count == o.Active.Count && Idle.Count == o.Idle.Count &&
        Active.Zip(o.Active).All(p => p.First.SameAs(p.Second)) && Idle.Zip(o.Idle).All(p => p.First.SameAs(p.Second));

    /// <summary>Ergänzt fehlende Bausteine (z. B. nach einem Update) und entfernt doppelte.</summary>
    public DisplayLayout Normalized()
    {
        static List<LayoutItem> Fix(List<LayoutItem> items, WidgetKind[] kinds, List<LayoutItem> defaults)
        {
            var seen = new HashSet<string>();
            var o = items.Where(i => (kinds.Contains(i.Kind) || i.IsCustom) && seen.Add(i.Id)).Select(i => i.Clone()).ToList();
            foreach (var d in defaults)
                if (!o.Any(i => i.Id == d.Id)) { var n = d.Clone(); n.Visible = false; o.Add(n); }
            foreach (var i in o) i.Clamp();
            return o;
        }
        var s = LayoutPresets.Standard;
        return new() { Active = Fix(Active, ActiveKinds, s.Active), Idle = Fix(Idle, IdleKinds, s.Idle) };
    }
}

public sealed record LayoutPreset(string Id, string Name, DisplayLayout Layout);

public static class LayoutPresets
{
    static LayoutItem I(WidgetKind k, double x, double y, double w, double h, bool visible = true, TextAlign? align = null) =>
        new(k, x, y, w, h, visible, align);

    public static DisplayLayout Standard => new()
    {
        Active = [
            I(WidgetKind.Header, 6, 6, 166, 24), I(WidgetKind.Clock, 172, 6, 58, 24, align: TextAlign.Right),
            I(WidgetKind.Ring, 12, 44, 80, 80), I(WidgetKind.Status, 104, 44, 126, 14),
            I(WidgetKind.Name, 104, 60, 126, 42, align: TextAlign.Left), I(WidgetKind.Timer, 104, 104, 126, 20, align: TextAlign.Left),
            I(WidgetKind.Step, 8, 134, 224, 44), I(WidgetKind.Limit5, 10, 186, 104, 48), I(WidgetKind.LimitWeek, 126, 186, 104, 48),
        ],
        Idle = [
            I(WidgetKind.Header, 6, 6, 160, 24), I(WidgetKind.Ready, 166, 6, 64, 24, align: TextAlign.Right),
            I(WidgetKind.BigClock, 10, 40, 220, 72, align: TextAlign.Center), I(WidgetKind.Date, 8, 116, 224, 24, align: TextAlign.Center),
            I(WidgetKind.Calm, 57, 146, 126, 30), I(WidgetKind.Limit5, 10, 186, 104, 48), I(WidgetKind.LimitWeek, 126, 186, 104, 48),
        ],
    };

    public static DisplayLayout Focus => new()
    {
        Active = [
            I(WidgetKind.Header, 6, 6, 166, 24), I(WidgetKind.Clock, 172, 6, 58, 24, align: TextAlign.Right),
            I(WidgetKind.Ring, 10, 38, 96, 96), I(WidgetKind.Status, 116, 44, 116, 14),
            I(WidgetKind.Name, 116, 62, 116, 42, align: TextAlign.Left), I(WidgetKind.Timer, 116, 108, 116, 22, align: TextAlign.Left),
            I(WidgetKind.Step, 8, 140, 224, 50), I(WidgetKind.Limit5, 10, 196, 104, 40), I(WidgetKind.LimitWeek, 126, 196, 104, 40),
        ],
        Idle = [
            I(WidgetKind.Header, 6, 6, 160, 24), I(WidgetKind.Ready, 166, 6, 64, 24, align: TextAlign.Right),
            I(WidgetKind.BigClock, 4, 40, 232, 92, align: TextAlign.Center), I(WidgetKind.Date, 8, 136, 224, 28, align: TextAlign.Center),
            I(WidgetKind.Calm, 57, 146, 126, 30, visible: false), I(WidgetKind.Limit5, 10, 186, 104, 48), I(WidgetKind.LimitWeek, 126, 186, 104, 48),
        ],
    };

    public static DisplayLayout BigLimits => new()
    {
        Active = [
            I(WidgetKind.Header, 6, 6, 166, 24), I(WidgetKind.Clock, 172, 6, 58, 24, align: TextAlign.Right),
            I(WidgetKind.Ring, 12, 44, 80, 80, visible: false), I(WidgetKind.Status, 12, 38, 110, 14),
            I(WidgetKind.Name, 12, 56, 216, 30, align: TextAlign.Left), I(WidgetKind.Timer, 150, 36, 80, 18, align: TextAlign.Right),
            I(WidgetKind.Step, 8, 94, 224, 36), I(WidgetKind.Limit5, 12, 134, 216, 50), I(WidgetKind.LimitWeek, 12, 186, 216, 50),
        ],
        Idle = [
            I(WidgetKind.Header, 6, 6, 160, 24), I(WidgetKind.Ready, 166, 6, 64, 24, align: TextAlign.Right),
            I(WidgetKind.BigClock, 10, 34, 220, 64, align: TextAlign.Center), I(WidgetKind.Date, 8, 98, 224, 22, align: TextAlign.Center),
            I(WidgetKind.Calm, 57, 146, 126, 30, visible: false), I(WidgetKind.Limit5, 12, 136, 216, 44), I(WidgetKind.LimitWeek, 12, 188, 216, 44),
        ],
    };

    public static DisplayLayout Big => new()
    {
        Active = [
            I(WidgetKind.Header, 6, 6, 166, 24), I(WidgetKind.Clock, 172, 6, 58, 24, align: TextAlign.Right),
            I(WidgetKind.Ring, 12, 44, 80, 80, visible: false), I(WidgetKind.Status, 10, 40, 140, 16),
            I(WidgetKind.Name, 10, 60, 220, 26, align: TextAlign.Left), I(WidgetKind.Timer, 150, 38, 80, 20, align: TextAlign.Right),
            I(WidgetKind.Step, 6, 90, 228, 94), I(WidgetKind.Limit5, 10, 190, 104, 46), I(WidgetKind.LimitWeek, 126, 190, 104, 46),
        ],
        Idle = [
            I(WidgetKind.Header, 6, 6, 160, 24), I(WidgetKind.Ready, 166, 6, 64, 24, align: TextAlign.Right),
            I(WidgetKind.BigClock, 2, 34, 236, 104, align: TextAlign.Center), I(WidgetKind.Date, 8, 140, 224, 30, align: TextAlign.Center),
            I(WidgetKind.Calm, 57, 146, 126, 30, visible: false), I(WidgetKind.Limit5, 10, 190, 104, 46), I(WidgetKind.LimitWeek, 126, 190, 104, 46),
        ],
    };

    public static DisplayLayout Details => new()
    {
        Active = [
            I(WidgetKind.Header, 6, 6, 166, 24), I(WidgetKind.Clock, 172, 6, 58, 24, align: TextAlign.Right),
            I(WidgetKind.Ring, 10, 38, 70, 70), I(WidgetKind.Status, 88, 38, 144, 14),
            I(WidgetKind.Name, 88, 56, 144, 26, align: TextAlign.Left), I(WidgetKind.Activity, 88, 86, 144, 16, align: TextAlign.Left),
            I(WidgetKind.Timer, 104, 104, 126, 20, visible: false, align: TextAlign.Left),
            I(WidgetKind.Step, 8, 114, 224, 38), I(WidgetKind.Context, 10, 158, 104, 34),
            I(WidgetKind.Model, 126, 160, 104, 15, align: TextAlign.Left), I(WidgetKind.Branch, 126, 178, 104, 15, align: TextAlign.Left),
            I(WidgetKind.Limit5, 10, 198, 104, 36), I(WidgetKind.LimitWeek, 126, 198, 104, 36),
        ],
        Idle = Standard.Idle,
    };

    public static LayoutPreset[] All => [
        new("standard", "Standard", Standard), new("focus", "Fokus", Focus),
        new("limits", "Limits groß", BigLimits), new("big", "Groß", Big), new("details", "Details", Details),
    ];
}

/// <summary>Was die Bausteine zeigen.</summary>
public sealed record WidgetData(SessionInfo? Session, Usage? Usage, int Tick, DateTime Now);

public static class Widgets
{
    static SKFont F(float s, int w) => Canvas.Font(s, w);

    public static void Draw(Canvas c, LayoutItem item, WidgetData d)
    {
        var r = item.Rect;
        var s = d.Session;
        switch (item.Kind)
        {
            case WidgetKind.Header: Header(c, r, s != null, d.Tick); break;
            case WidgetKind.Clock: Line(c, r, d.Now.ToString("HH:mm"), 0.667f, 560, Pal.Muted, item.Align ?? TextAlign.Right); break;
            case WidgetKind.Ring: if (s != null) Ring(c, r, s, d.Tick); break;
            case WidgetKind.Status: if (s != null) Status(c, r, s, d.Tick); break;
            case WidgetKind.Context: if (s?.ContextPct is { } cp) Meter(c, r, "KONTEXT", cp, s.Waiting ? Pal.Warn : Pal.Accent); break;
            case WidgetKind.Model: if (s?.Model is { } mo) Line(c, r, ModelName(mo), 0.8f, 600, Pal.Muted, item.Align ?? TextAlign.Left); break;
            case WidgetKind.Branch: if (s?.Branch is { } br) Branch(c, r, br, item.Align ?? TextAlign.Left); break;
            case WidgetKind.Activity: if (s?.Activity is { } ac) Line(c, r, ac, 0.8f, 560, s.Waiting ? Pal.Warn : Pal.Muted, item.Align ?? TextAlign.Left); break;
            case WidgetKind.Name: if (s != null) Name(c, r, s.Name, item.Align ?? TextAlign.Left); break;
            case WidgetKind.Timer: if (s != null) Line(c, r, Renderer.FmtTimer(s.Secs), 0.75f, 560, Pal.Muted, item.Align ?? TextAlign.Left); break;
            case WidgetKind.Step: if (s != null) Step(c, r, s); break;
            case WidgetKind.Limit5: Limit(c, r, "5 STD", d.Usage?.Five, d.Usage?.Stale, d.Usage != null, d.Tick); break;
            case WidgetKind.LimitWeek: Limit(c, r, "WOCHE", d.Usage?.Week, d.Usage?.Stale, d.Usage != null, d.Tick); break;
            case WidgetKind.Ready: Ready(c, r, item.Align ?? TextAlign.Right); break;
            case WidgetKind.BigClock: Line(c, r, d.Now.ToString("HH:mm"), 0.917f, 330, Pal.Text, item.Align ?? TextAlign.Center); break;
            case WidgetKind.Date: Line(c, r, Renderer.DateText(d.Now), 0.79f, 600, Pal.Muted, item.Align ?? TextAlign.Center); break;
            case WidgetKind.Calm: Calm(c, r); break;
            case WidgetKind.Label:
                Line(c, r, item.Text ?? "", 0.75f, 620, WidgetInfo.Color(item.Color ?? ColorRole.Text, Pal.Theme).Sk, item.Align ?? TextAlign.Left);
                break;
            case WidgetKind.Line:
                c.RRect(r.Left, r.Top, r.Right, r.Bottom, Math.Min(r.Width, r.Height) / 2,
                        fill: WidgetInfo.Color(item.Color ?? ColorRole.Edge, Pal.Theme).Sk);
                break;
        }
    }

    static (float, Canvas.H) Anchor(SKRect r, TextAlign a) => a switch
    {
        TextAlign.Left => (r.Left, Canvas.H.Left),
        TextAlign.Center => (r.MidX, Canvas.H.Center),
        _ => (r.Right, Canvas.H.Right),
    };

    /// <summary>Eine Zeile, Schrift = Anteil der Rahmenhöhe, schrumpft bei Bedarf auf die Breite.</summary>
    static void Line(Canvas c, SKRect r, string s, float k, int weight, SKColor col, TextAlign a)
    {
        if (s.Length == 0) return;
        float size = r.Height * k;
        while (size > 6 && c.TLen(s, F(size, weight)) > r.Width) size -= 0.5f;
        var f = F(size, weight);
        var (x, h) = Anchor(r, a);
        c.Text(x, r.MidY, c.Fit(s, f, r.Width), f, col, h, Canvas.V.Middle);
    }

    static void Header(Canvas c, SKRect r, bool active, int tick)
    {
        var t = Pal.Theme;
        float h = r.Height;
        c.Logo(t.Logo, r.Left + h * 0.458f, r.MidY, active, tick, h / 24);
        float x = t.Logo == LogoStyle.None ? r.Left + 4 : r.Left + h * 1.125f;
        var f = F(h * 0.667f, 650);
        c.Text(x, r.MidY, c.Fit(t.Title, f, r.Right - x), f, Pal.Text, v: Canvas.V.Middle);
    }

    static void Ring(Canvas c, SKRect r, SessionInfo s, int tick)
    {
        float rad = Math.Min(r.Width, r.Height) / 2, w = rad * 0.2f, cx = r.MidX, cy = r.MidY;
        c.Arc(cx, cy, rad, w, 0, 360, Pal.Track, caps: false);
        // Wartet Claude auf dich, leuchtet der Ring in der Warnfarbe
        var a1 = s.Waiting ? Pal.Theme.Warn : Pal.Theme.Accent;
        var a2 = s.Waiting ? Pal.Theme.Warn.Mix(Pal.Theme.Bad, 0.4) : Pal.Theme.Accent2;
        if (s.Plan.Count > 0)
        {
            float frac = (float)s.Done / s.Plan.Count;
            if (frac > 0) c.GradientArc(cx, cy, rad, w, 0, Math.Min(359.4f, 360 * frac), a1, a2);
            else if (s.Waiting) c.Arc(cx, cy, rad, w, 0, 360, Pal.Warn.WithAlpha(90), caps: false);
            c.Text(cx, cy - rad * 0.1f, s.Done.ToString(), F(rad * 0.75f, 700), Pal.Text, Canvas.H.Center, Canvas.V.Middle);
            c.Text(cx, cy + rad * 0.475f, $"von {s.Plan.Count}", F(rad * 0.275f, 600), Pal.Muted, Canvas.H.Center, Canvas.V.Middle);
        }
        else
        {
            float start = tick * 40 % 360;
            if (s.Waiting) c.Arc(cx, cy, rad, w, 0, 360, Pal.Warn, caps: false);
            else c.GradientArc(cx, cy, rad, w, start, start + 80, a1, a2);
            c.Text(cx, cy, s.Waiting ? "?" : "···", F(rad * 0.55f, 700), s.Waiting ? Pal.Warn : Pal.Muted, Canvas.H.Center, Canvas.V.Middle);
        }
    }

    static void Status(Canvas c, SKRect r, SessionInfo s, int tick)
    {
        float k = r.Height / 14;
        var col = s.Waiting ? Pal.Warn : Pal.Good;
        var glow = (s.Waiting ? Pal.Theme.Warn : Pal.Theme.Good).Mix(new RGB(0, 0, 0), 0.75).Sk;
        c.Circle(r.Left + 4 * k, r.MidY, (tick % 2 == 0 ? 5.5f : 4.2f) * k, glow);
        c.Circle(r.Left + 4 * k, r.MidY, 3.4f * k, col);
        // „1/2“ beim Durchwechseln, sonst „+1“ für weitere arbeitende Sitzungen
        var more = s.Total > 1 ? $"  {s.Index}/{s.Total}" : s.BusyCount > 1 ? $"  +{s.BusyCount - 1}" : "";
        var text = (s.Waiting ? "WARTET AUF DICH" : "ARBEITET") + more;
        float size = 10 * k, maxw = r.Width - 13 * k;
        while (size > 5 && c.TLen(text, F(size, 700)) + text.Length * 1.2f * size / 10 > maxw) size -= 0.5f;
        var f = F(size, 700);
        var m = f.Metrics;
        c.Spaced(r.Left + 13 * k, r.MidY - (m.Descent - m.Ascent) / 2, text, f, col, 1.2f * size / 10);
    }

    /// <summary>Modell-ID lesbar machen: „claude-opus-5-5“ → „Opus 5.5“.</summary>
    public static string ModelName(string id)
    {
        var p = id.Split('-').ToList();
        if (p.Count > 0 && p[0] == "claude") p.RemoveAt(0);
        p = p.Where(x => !(x.Length == 8 && int.TryParse(x, out _)) && !x.StartsWith('[')).ToList();
        if (p.Count == 0) return id;
        var nums = p.Skip(1).Where(x => int.TryParse(x, out _)).ToList();
        return char.ToUpper(p[0][0]) + p[0][1..] + (nums.Count == 0 ? "" : " " + string.Join(".", nums));
    }

    /// <summary>Kleines Zweig-Symbol und Branch-Name.</summary>
    static void Branch(Canvas c, SKRect r, string name, TextAlign a)
    {
        float k = r.Height / 15, icon = 12 * k;
        var f = F(12 * k, 600);
        var text = c.Fit(name, f, r.Width - icon);
        float total = icon + c.TLen(text, f);
        float x0 = a == TextAlign.Left ? r.Left : a == TextAlign.Center ? r.MidX - total / 2 : r.Right - total;
        var col = Pal.Muted;
        c.Line(x0 + 3 * k, r.MidY - 5 * k, x0 + 3 * k, r.MidY + 5 * k, col, 1.4f * k);
        c.Line(x0 + 3 * k, r.MidY + 1 * k, x0 + 8 * k, r.MidY - 4 * k, col, 1.4f * k);
        c.Circle(x0 + 3 * k, r.MidY + 5 * k, 1.7f * k, col);
        c.Circle(x0 + 8 * k, r.MidY - 4.5f * k, 1.7f * k, col);
        c.Text(x0 + icon, r.MidY, text, f, col, v: Canvas.V.Middle);
    }

    /// <summary>Anzeige mit Prozentzahl und Balken (Kontext).</summary>
    static void Meter(Canvas c, SKRect r, string label, int pct, SKColor col)
    {
        float k = r.Height / 34, b = r.Top + 24 * k;
        c.Spaced(r.Left, r.Top + 1 * k, label, F(9 * k, 700), Pal.Dim, 1.1f * k);
        var color = pct >= 90 ? Pal.Bad : pct >= 75 ? Pal.Warn : col;
        var big = F(16 * k, 700);
        c.Text(r.Left, b, pct.ToString(), big, color, v: Canvas.V.Baseline);
        c.Text(r.Left + c.TLen(pct.ToString(), big) + k, b, "%", F(10 * k, 650), color, v: Canvas.V.Baseline);
        c.Bar(r.Left, r.Top + 28 * k, r.Right, 4 * k, pct, color);
    }

    static void Name(Canvas c, SKRect r, string name, TextAlign a)
    {
        // so wenige Zeilen wie möglich – dann ist die Schrift am größten
        int n = r.Height >= 24 ? 2 : 1;
        for (int lines = 1; lines <= 3; lines++)
            if (!c.Wrap(name, F(r.Height / (lines * 1.235f), 680), r.Width, lines).Any(l => l.EndsWith('…'))) { n = lines; break; }
        float size = r.Height / (n * 1.235f), lh = size * 1.235f;
        var f = F(size, 680);
        var (x, h) = Anchor(r, a);
        float y = r.Top;
        foreach (var l in c.Wrap(name, f, r.Width, n)) { c.Text(x, y, l, f, Pal.Text, h); y += lh; }
    }

    static void Step(Canvas c, SKRect r, SessionInfo s)
    {
        float k = r.Height / 44;
        string text; SKColor col, accent;
        if (s.Waiting) { text = s.Activity ?? "Wartet auf dich"; col = Pal.Warn; accent = Pal.Warn; }
        else if (s.Plan.Count > 0)
        {
            if (s.CurrentStep is { } cur) { text = cur; col = Pal.Text; accent = Pal.Accent; }
            else { text = "Alle Schritte erledigt"; col = Pal.Good; accent = Pal.Good; }
        }
        else { text = s.Prompt ?? "Denkt nach …"; col = Pal.Text; accent = Pal.Accent; }
        c.RRect(r.Left, r.Top, r.Right, r.Bottom, Math.Min(12 * k, Math.Min(r.Width, r.Height) / 2), stroke: s.Waiting ? Pal.Warn : Pal.Edge);
        c.RRect(r.Left + 7 * k, r.Top + 9 * k, r.Left + 10.5f * k, r.Bottom - 9 * k, 1.75f * k, fill: accent);
        var f = F(15 * k, 600);
        float lh = 19 * k;
        int maxLines = Math.Max(1, (int)((r.Height - 6 * k) / lh));
        var lines = c.Wrap(text, f, r.Width - 28 * k, maxLines);
        float sy = r.MidY - (lines.Count - 1) * lh / 2;
        foreach (var l in lines) { c.Text(r.Left + 19 * k, sy, l, f, col, v: Canvas.V.Middle); sy += lh; }
    }

    static void Limit(Canvas c, SKRect r, string label, UsageWindow? w, DateTime? stale, bool available, int tick = 0)
    {
        float k = r.Height / 48, b = r.Top + 36 * k;
        c.Spaced(r.Left, r.Top + 2 * k, label, F(10 * k, 700), Pal.Dim, 1.2f * k);
        var small = F(11 * k, 560);
        if (!available || w == null)
        {
            c.Text(r.Left, b, "–", F(24 * k, 700), Pal.Dim, v: Canvas.V.Baseline);
            c.Text(r.Right, b, c.Fit("nicht verfügbar", small, r.Width * 0.7f), small, Pal.Dim, Canvas.H.Right, Canvas.V.Baseline);
            c.Bar(r.Left, r.Top + 41 * k, r.Right, 5 * k, 0, Pal.Dim);
            return;
        }
        var col = Pal.ForPct(w.Pct);
        var big = F(24 * k, 700);
        c.Text(r.Left, b, w.Pct.ToString(), big, col, v: Canvas.V.Baseline);
        c.Text(r.Left + c.TLen(w.Pct.ToString(), big) + k, b, "%", F(13 * k, 650), col, v: Canvas.V.Baseline);
        // Limit fast erreicht: pulsierender Rahmen und deutlicher Hinweis auf den Reset
        bool critical = w.Pct >= 90 && stale == null;
        if (critical)
        {
            float pad = 3 * k;
            c.RRect(r.Left - pad, r.Top - pad, r.Right + pad, r.Bottom + pad, 6 * k,
                    stroke: tick % 2 == 0 ? Pal.Bad : Pal.Bad.WithAlpha(90), width: Math.Max(1, 1.5f * k));
        }
        if (stale is { } st) c.Text(r.Right, b, "Stand " + st.ToString("HH:mm"), small, Pal.Dim, Canvas.H.Right, Canvas.V.Baseline);
        else if (w.Reset is { } rs)
        {
            var t = Renderer.FmtReset(rs - DateTime.UtcNow);
            var f = critical ? F(11 * k, 700) : small;
            // „Reset 45 min“, wenn Platz ist – sonst nur die Zeit
            float pctWidth = c.TLen(w.Pct + "%", big) + 6 * k;
            var lbl = critical && c.TLen("Reset " + t, f) <= r.Width - pctWidth ? "Reset " + t : t;
            c.Text(r.Right, b, lbl, f, critical ? Pal.Bad : Pal.Muted, Canvas.H.Right, Canvas.V.Baseline);
        }
        c.Bar(r.Left, r.Top + 41 * k, r.Right, 5 * k, w.Pct, col);
    }

    static void Ready(Canvas c, SKRect r, TextAlign a)
    {
        float k = r.Height / 24, dot = 8 * k;
        var f = F(13 * k, 600);
        float tw = c.TLen("bereit", f);
        float left = a switch { TextAlign.Left => r.Left + dot, TextAlign.Center => r.MidX - (tw - dot) / 2, _ => r.Right - tw };
        c.Text(left, r.MidY, "bereit", f, Pal.Dim, v: Canvas.V.Middle);
        c.Circle(left - dot, r.MidY, 3 * k, Pal.Good);
    }

    static void Calm(Canvas c, SKRect r)
    {
        c.RRect(r.Left, r.Top, r.Right, r.Bottom, Math.Min(r.Width, r.Height) / 2, stroke: Pal.Edge);
        float size = r.Height * 0.533f, maxw = r.Width - r.Height * 0.6f;
        while (size > 6 && c.TLen("Alles ruhig", F(size, 620)) > maxw) size -= 0.5f;
        c.Text(r.MidX, r.MidY, "Alles ruhig", F(size, 620), Pal.Muted, Canvas.H.Center, Canvas.V.Middle);
    }
}
