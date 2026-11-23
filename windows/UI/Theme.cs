using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ClaudeDisplay.Core;
using SkiaSharp;

namespace ClaudeDisplay.UI;

/// <summary>Gemeinsame Bausteine der Oberfläche (dunkel, Neon-Akzente aus dem App-Theme).</summary>
public static class Look
{
    public static AppTheme T => AppSettings.Shared.AppTheme;
    public static DisplayTheme D => AppSettings.Shared.DisplayTheme;
    public static Color Accent => T.Accent.Ui;
    public static Color Accent2 => T.Accent2.Ui;
    public static readonly Color Text = Color.Parse("#F2F4FF");
    public static readonly Color Secondary = Color.Parse("#9AA0B8");

    /// <summary>Wird ausgelöst, wenn sich App- oder Display-Theme ändert.</summary>
    public static event Action? Changed;
    public static void NotifyChanged() => Changed?.Invoke();

    public static IBrush B(Color c) => new SolidColorBrush(c);
    public static IBrush B(Color c, double opacity) => new SolidColorBrush(c, opacity);

    public static IBrush Gradient(Color a, Color b) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
    };
    public static IBrush AccentGradient => Gradient(Accent, Accent2);

    public static TextBlock Tx(string text, double size, FontWeight weight = FontWeight.Normal, Color? color = null, bool trim = false) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = B(color ?? Text),
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
    };

    public static TextBlock Label(string text)
    {
        var t = Tx(text.ToUpperInvariant(), 10.5, FontWeight.Bold, Secondary);
        t.LetterSpacing = 1.2;
        return t;
    }

    /// <summary>Glas-Karte</summary>
    public static Border Glass(Control child, double padding = 14, Color? highlight = null) => new()
    {
        Child = child,
        Padding = new Thickness(padding),
        CornerRadius = new CornerRadius(14),
        Background = highlight is { } h ? B(Blend(T.Surface.Ui, h, 0.08), 0.78) : B(T.Surface.Ui, 0.72),
        BorderBrush = B(highlight ?? Colors.White, highlight != null ? 0.30 : 0.10),
        BorderThickness = new Thickness(1),
    };

    public static Color Blend(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>Hintergrund: tiefe Grundfarbe mit zwei weichen Leuchtflecken.</summary>
    public static Panel NeonBackground(Control content)
    {
        var g = new Grid { Background = B(T.Background.Ui) };
        g.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0, 0, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0, 0, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(70, Accent.R, Accent.G, Accent.B), 0), new GradientStop(Colors.Transparent, 1) },
            },
        });
        g.Children.Add(new Border
        {
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(1, 1, RelativeUnit.Relative), GradientOrigin = new RelativePoint(1, 1, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.7, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(52, Accent2.R, Accent2.G, Accent2.B), 0), new GradientStop(Colors.Transparent, 1) },
            },
        });
        // Schriftgröße der App: der ganze Inhalt wird gleichmäßig skaliert (Schrift, Bedienelemente, Abstände)
        g.Children.Add(Math.Abs(Scale - 1) < 0.01 ? content
            : new LayoutTransformControl { LayoutTransform = new ScaleTransform(Scale, Scale), Child = content });
        return g;
    }

    /// <summary>Schriftgröße der App: 0,9 klein · 1 normal · 1,15 groß · 1,3 sehr groß</summary>
    public static double Scale => AppSettings.Shared.TextScale;

    static readonly List<(WeakReference<Window> w, double width, double? height, double? minW, double? minH)> sized = [];

    /// <summary>Fenstergröße passend zur Schriftgröße setzen (und bei Änderung nachführen).</summary>
    /// <param name="track">false für Fenster, die ihren Inhalt nicht neu aufbauen – sie gelten bis zum nächsten Öffnen.</param>
    public static void Size(Window w, double width, double? height = null, double? minW = null, double? minH = null, bool track = true)
    {
        if (track) sized.Add((new WeakReference<Window>(w), width, height, minW, minH));
        Apply(w, width, height, minW, minH);
    }

    static void Apply(Window w, double width, double? height, double? minW, double? minH)
    {
        double k = Scale;
        if (minW is { } mw) w.MinWidth = mw * k;
        if (minH is { } mh) w.MinHeight = mh * k;
        w.Width = width * k;
        if (height is { } h) w.Height = h * k;
    }

    /// <summary>Nach Änderung der Schriftgröße alle Fenster anpassen.</summary>
    public static void ApplySizes()
    {
        sized.RemoveAll(x => !x.w.TryGetTarget(out _));
        foreach (var (wr, width, height, minW, minH) in sized)
            if (wr.TryGetTarget(out var w)) Apply(w, width, height, minW, minH);
    }

    public static Button NeonButton(string text, Action click, bool prominent = true)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = B(prominent ? Colors.White : Text) },
            Background = prominent ? AccentGradient : B(Colors.White, 0.08),
            BorderBrush = B(Colors.White, prominent ? 0.25 : 0.12),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 7),
        };
        b.Click += (_, _) => click();
        return b;
    }

    public static Button IconButton(string glyph, string tip, Action click)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = glyph, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            Width = 32, Height = 32, Padding = new Thickness(0),
            Background = B(Colors.White, 0.07), BorderBrush = B(Colors.White, 0.12), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    public static WindowIcon AppIcon => new(AssetLoader.Open(new Uri("avares://ClaudeDisplay/Assets/app.ico")));

    /// <summary>Kopiert BGRA-Bytes in ein WriteableBitmap (240×240).</summary>
    public static void Blit(WriteableBitmap bmp, byte[] bgra)
    {
        using var fb = bmp.Lock();
        int row = 240 * 4;
        for (int y = 0; y < 240; y++)
            Marshal.Copy(bgra, y * row, fb.Address + y * fb.RowBytes, row);
    }

    public static WriteableBitmap NewPreview() =>
        new(new PixelSize(240, 240), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

    /// <summary>Display-Ansicht in einem Theme als Bild (für die Vorlagen-Auswahl).</summary>
    public static Bitmap ThemePreview(DisplayTheme theme) => Preview(theme, null);

    /// <summary>Display-Ansicht in einem Layout als Bild (für die Layout-Vorlagen).</summary>
    public static Bitmap LayoutPreview(DisplayLayout layout) => Preview(null, layout);

    static Bitmap Preview(DisplayTheme? theme, DisplayLayout? layout)
    {
        var sess = new SessionInfo("Mein Projekt", 754,
            [new("a", "completed"), new("b", "completed"), new("Tests schreiben", "in_progress"), new("d", "pending")], null, 1);
        var use = new Usage(new(42, DateTime.UtcNow.AddHours(2)), new(67, DateTime.UtcNow.AddHours(47)), null);
        using var c = Renderer.Render(sess, use, 3, theme, layout);
        using var img = SKImage.FromBitmap(c.Bitmap);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        return new Bitmap(new MemoryStream(data.ToArray()));
    }
}

/// <summary>App-Zeichen: Ring mit Farbverlauf und Punkt, optional drehend.</summary>
public sealed class AppMark : Control
{
    double angle = -90;
    bool spinning;
    readonly Avalonia.Threading.DispatcherTimer timer;

    public AppMark(double size)
    {
        Width = Height = size;
        timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) => { angle = (angle + 4) % 360; InvalidateVisual(); };
    }

    public bool Spinning
    {
        get => spinning;
        set { if (spinning == value) return; spinning = value; if (value) timer.Start(); else { timer.Stop(); angle = -90; InvalidateVisual(); } }
    }

    public override void Render(DrawingContext ctx)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height), w = s * 0.16, r = s / 2 - w / 2, cx = Bounds.Width / 2, cy = Bounds.Height / 2;
        double sweep = spinning ? 300 : 330, start = angle + (spinning ? 0 : 15);
        int steps = 40;
        for (int i = 0; i < steps; i++)
        {
            double a0 = (start + sweep * i / steps) * Math.PI / 180, a1 = (start + sweep * (i + 1) / steps + 1) * Math.PI / 180;
            var col = Look.Blend(Look.Accent, Look.Accent2, (double)i / (steps - 1));
            var pen = new Pen(Look.B(col), w, lineCap: i == 0 || i == steps - 1 ? PenLineCap.Round : PenLineCap.Flat);
            ctx.DrawLine(pen, new Point(cx + Math.Cos(a0) * r, cy + Math.Sin(a0) * r), new Point(cx + Math.Cos(a1) * r, cy + Math.Sin(a1) * r));
        }
        ctx.DrawEllipse(Look.B(Look.Accent2), null, new Point(cx, cy), s * 0.11, s * 0.11);
    }
}

/// <summary>Statuskapsel mit leuchtendem Punkt.</summary>
public sealed class StatusPill : Border
{
    readonly Ellipse dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock label = Look.Tx("", 11, FontWeight.SemiBold);

    public StatusPill()
    {
        CornerRadius = new CornerRadius(20);
        Padding = new Thickness(10, 4);
        BorderThickness = new Thickness(1);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Left;
        Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, label } };
    }

    public void Set(EngineState s)
    {
        var d = Look.D;
        var c = s.Status switch
        {
            EngineStatus.Shown => d.Good.Ui,
            EngineStatus.Standby => d.Warn.Ui,
            EngineStatus.Error => d.Bad.Ui,
            EngineStatus.Updating => Look.Accent,
            _ => Look.Secondary,
        };
        dot.Fill = Look.B(c);
        label.Text = s.Title;
        Background = Look.B(c, 0.14);
        BorderBrush = Look.B(c, 0.35);
    }
}

/// <summary>Zeichnet das 240×240-Bild exakt in seine Fläche, mit runden Ecken.</summary>
public sealed class PreviewSurface : Control
{
    public WriteableBitmap Bitmap { get; } = Look.NewPreview();
    readonly double radius;

    public PreviewSurface(double size, double radius)
    {
        Width = Height = size;
        this.radius = radius;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public override void Render(DrawingContext ctx)
    {
        var rect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        using (ctx.PushClip(new RoundedRect(rect, radius)))
        {
            ctx.FillRectangle(Brushes.Black, rect);
            ctx.DrawImage(Bitmap, new Rect(0, 0, 240, 240), rect);
        }
    }
}

/// <summary>Live-Vorschau des Displays in einem leuchtenden Gehäuse.</summary>
public sealed class DevicePreview : Border
{
    readonly PreviewSurface surface;

    public DevicePreview(double scale = 1)
    {
        Width = Height = 272 * scale;
        CornerRadius = new CornerRadius(26 * scale);
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#292A33"), 0), new GradientStop(Color.Parse("#0C0D12"), 1) },
        };
        BorderBrush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(150, Look.Accent.R, Look.Accent.G, Look.Accent.B), 0),
                              new GradientStop(Color.FromArgb(60, Look.Accent2.R, Look.Accent2.G, Look.Accent2.B), 1) },
        };
        BorderThickness = new Thickness(1.2);
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 26, Color = Color.FromArgb(90, Look.Accent.R, Look.Accent.G, Look.Accent.B) });
        HorizontalAlignment = HorizontalAlignment.Center;
        surface = new PreviewSurface(240 * scale, 6 * scale) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Child = surface;
    }

    public void Update(byte[] bgra)
    {
        Look.Blit(surface.Bitmap, bgra);
        surface.InvalidateVisual();
    }
}

/// <summary>Schmaler Balken mit Verlauf.</summary>
public sealed class SlimBar : Grid
{
    readonly Border fill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    double value;

    public SlimBar(double height = 6)
    {
        Height = height;
        var radius = new CornerRadius(height / 2);
        fill.CornerRadius = radius;
        Children.Add(new Border { CornerRadius = radius, Background = Look.B(Colors.White, 0.08) });
        Children.Add(fill);
        SizeChanged += (_, _) => Layout_();
    }

    public void Set(double fraction, Color a, Color b)
    {
        value = Math.Clamp(fraction, 0, 1);
        fill.Background = Look.Gradient(a, b);
        fill.IsVisible = value > 0;
        Layout_();
    }

    void Layout_() => fill.Width = Math.Max(Height, Bounds.Width * value);
}

/// <summary>Karte für ein Nutzungsfenster.</summary>
public sealed class LimitCard : Border
{
    readonly TextBlock pct = Look.Tx("–", 26, FontWeight.Bold);
    readonly TextBlock unit = Look.Tx("%", 13, FontWeight.SemiBold);
    readonly TextBlock reset = Look.Tx("", 10, FontWeight.Medium, Look.Secondary);
    readonly SlimBar bar = new(5);

    public LimitCard(string title)
    {
        var head = new DockPanel();
        DockPanel.SetDock(reset, Dock.Right);
        head.Children.Add(reset);
        head.Children.Add(Look.Label(title));
        unit.VerticalAlignment = VerticalAlignment.Bottom;
        unit.Margin = new Thickness(0, 0, 0, 5);
        var value = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { pct, unit } };
        Child = new StackPanel { Spacing = 8, Children = { head, value, bar } };
        Padding = new Thickness(12);
        CornerRadius = new CornerRadius(14);
        Background = Look.B(Look.T.Surface.Ui, 0.72);
        BorderBrush = Look.B(Colors.White, 0.10);
        BorderThickness = new Thickness(1);
    }

    public void Set(UsageWindow? w)
    {
        var c = w == null ? Look.Secondary : Look.D.ForPct(w.Pct).Ui;
        pct.Text = w == null ? "–" : w.Pct.ToString();
        pct.Foreground = unit.Foreground = Look.B(c);
        bar.Set((w?.Pct ?? 0) / 100.0, Look.Blend(c, Colors.Black, 0.3), c);
        reset.Text = w?.Reset is { } r ? Renderer.FmtReset(r - DateTime.UtcNow) : "";
    }
}
