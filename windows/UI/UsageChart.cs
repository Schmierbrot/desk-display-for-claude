using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClaudeDisplay.Core;

namespace ClaudeDisplay.UI;

/// <summary>Karte „Verlauf“: 5-Stunden- und Wochenlimit über 24 Stunden oder 7 Tage.</summary>
public sealed class UsageChartCard : StackPanel
{
    int days = 1;
    readonly UsageChart chart = new();
    readonly Button dayBtn, weekBtn;
    readonly TextBlock empty = Look.Tx("Der Verlauf füllt sich, sobald die App ein paar Mal das Limit gelesen hat.", 12, color: Look.Secondary);

    public UsageChartCard()
    {
        Spacing = 8;
        dayBtn = Tab("24 Std.", 1); weekBtn = Tab("7 Tage", 7);
        var head = new DockPanel();
        var tabs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { dayBtn, weekBtn } };
        DockPanel.SetDock(tabs, Dock.Right);
        head.Children.Add(tabs); head.Children.Add(Look.Label("Verlauf"));
        empty.TextWrapping = TextWrapping.Wrap;
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { Legend(Look.Accent, "5 Stunden"), Legend(Look.D.Warn.Ui, "Woche") } };
        Children.Add(head);
        Children.Add(Look.Glass(new StackPanel { Spacing = 8, Children = { chart, empty, legend } }, 16));
        UsageHistory.Shared.Changed += () => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    Button Tab(string text, int d)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), FontSize = 11.5 };
        b.Click += (_, _) => { days = d; Refresh(); };
        return b;
    }

    static Control Legend(Color c, string s) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = {
        new Border { Width = 14, Height = 4, CornerRadius = new CornerRadius(2), Background = Look.B(c), VerticalAlignment = VerticalAlignment.Center },
        Look.Tx(s, 11, color: Look.Secondary) } };

    void Refresh()
    {
        chart.Days = days;
        chart.Points = UsageHistory.Shared.Since(DateTime.UtcNow.AddDays(-days));
        bool any = chart.Points.Count >= 2;
        chart.IsVisible = any; empty.IsVisible = !any;
        foreach (var (b, on) in new[] { (dayBtn, days == 1), (weekBtn, days == 7) })
            b.Background = on ? Look.AccentGradient : Look.B(Colors.White, 0.07);
        chart.InvalidateVisual();
    }
}

/// <summary>Zeichnet die beiden Linien mit Achsen.</summary>
public sealed class UsageChart : Control
{
    public List<UsageHistory.Point> Points { get; set; } = [];
    public int Days { get; set; } = 1;

    public UsageChart() { Height = 130; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, left = 0, right = w - 40, top = 6, bottom = h - 18;
        var grid = new Pen(Look.B(Colors.White, 0.08), 1);
        var labels = new Typeface(FontFamily.Default);
        foreach (var v in new[] { 0, 50, 100 })
        {
            double y = bottom - (bottom - top) * v / 100;
            ctx.DrawLine(grid, new Point(left, y), new Point(right, y));
            var ft = new FormattedText($"{v} %", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, labels, 10, Look.B(Look.Secondary));
            ctx.DrawText(ft, new Point(right + 6, y - ft.Height / 2));
        }
        var to = DateTime.UtcNow; var from = to.AddDays(-Days);
        double X(DateTime t) => left + (right - left) * (t - from).TotalSeconds / (to - from).TotalSeconds;
        double Y(int p) => bottom - (bottom - top) * Math.Clamp(p, 0, 100) / 100.0;
        // Zeitachse
        int steps = Days == 1 ? 6 : 7;
        for (int i = 1; i <= steps; i++)
        {
            var t = from.AddSeconds((to - from).TotalSeconds * i / (steps + 1)).ToLocalTime();
            var ft = new FormattedText(Days == 1 ? t.ToString("HH") + " Uhr" : t.ToString("ddd"), System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, labels, 10, Look.B(Look.Secondary));
            double x = X(t.ToUniversalTime());
            ctx.DrawText(ft, new Point(x - ft.Width / 2, bottom + 3));
        }
        if (Points.Count < 2) return;
        foreach (var (sel, col) in new (Func<UsageHistory.Point, int>, Color)[] { (p => p.Week, Look.D.Warn.Ui), (p => p.Five, Look.Accent) })
        {
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(new Point(X(Points[0].T), Y(sel(Points[0]))), false);
                foreach (var p in Points.Skip(1)) g.LineTo(new Point(X(p.T), Y(sel(p))));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, new Pen(Look.B(col), 2, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geo);
        }
    }
}
