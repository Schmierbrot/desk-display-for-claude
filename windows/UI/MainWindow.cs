using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClaudeDisplay.Core;

namespace ClaudeDisplay.UI;

/// <summary>Dashboard im Fenster-Modus: Display links, Sitzung mit Schritten und Limits rechts.</summary>
public sealed class MainWindow : Window
{
    readonly Action openSettings, openSetup, shutdown;
    AppMark mark = null!;
    TextBlock subtitle = null!, greeting = null!;
    DevicePreview preview = null!;
    StatusPill pill = null!;
    StackPanel banners = null!, session = null!;
    LimitCard five = null!, week = null!;
    Slider brightness = null!;
    TextBlock brightnessLabel = null!;
    bool updating;
    public bool AllowClose;

    public MainWindow(Action openSettings, Action openSetup, Action shutdown)
    {
        this.openSettings = openSettings; this.openSetup = openSetup; this.shutdown = shutdown;
        Title = "Claude Display";
        Icon = Look.AppIcon;
        Width = 980; Height = 660; MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // Schließen versteckt nur – die App läuft im Infobereich weiter
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        Build();
        Look.Changed += () => Dispatcher.UIThread.Post(Build);
        Engine.Shared.Changed += s => Dispatcher.UIThread.Post(() => Apply(s));
        ClaudeConnect.Shared.Changed += () => Dispatcher.UIThread.Post(() => { if (Engine.Shared.State is { } st) Apply(st); });
        Updater.Shared.Changed += () => Dispatcher.UIThread.Post(() => { if (Engine.Shared.State is { } st) Apply(st); });
    }

    void Build()
    {
        mark = new AppMark(28) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
        subtitle = Look.Tx("", 11.5, color: Look.Secondary, trim: true);
        preview = new DevicePreview(0.95);
        pill = new StatusPill { HorizontalAlignment = HorizontalAlignment.Center };
        brightness = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        brightnessLabel = Look.Tx("", 11, FontWeight.SemiBold, Look.Secondary);
        brightness.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || updating) return;
            var s = AppSettings.Shared;
            if (s.IsNight()) s.NightBrightness = brightness.Value; else s.DayBrightness = brightness.Value;
            brightnessLabel.Text = $"{(int)brightness.Value}%";
            s.Save(); Engine.Shared.SettingsChanged();
        };
        var bright = new DockPanel();
        DockPanel.SetDock(brightnessLabel, Dock.Right);
        brightnessLabel.Width = 38; brightnessLabel.TextAlignment = TextAlignment.Right; brightnessLabel.VerticalAlignment = VerticalAlignment.Center;
        bright.Children.Add(brightnessLabel); bright.Children.Add(brightness);

        var head = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        head.Children.Add(mark);
        head.Children.Add(new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center,
            Children = { Look.Tx("Claude Display", 17, FontWeight.Bold), subtitle } });
        var settingsBtn = Look.NeonButton("⚙  Einstellungen", openSettings, prominent: false);
        var left = new DockPanel { Margin = new Thickness(24, 26, 24, 22) };
        DockPanel.SetDock(head, Dock.Top); DockPanel.SetDock(settingsBtn, Dock.Bottom);
        left.Children.Add(head); left.Children.Add(settingsBtn);
        left.Children.Add(new StackPanel { Spacing = 20, VerticalAlignment = VerticalAlignment.Center,
            Children = { preview, pill, Look.Glass(bright, 12) } });
        var leftBorder = new Border { Width = 370, Background = Look.B(Colors.Black, 0.22), Child = left };

        greeting = Look.Tx("", 28, FontWeight.Bold);
        var date = Look.Tx(DateTime.Now.ToString("dddd, d. MMMM", new System.Globalization.CultureInfo("de-DE")), 13, color: Look.Secondary);
        date.VerticalAlignment = VerticalAlignment.Bottom;
        var top = new DockPanel();
        DockPanel.SetDock(date, Dock.Right);
        top.Children.Add(date); top.Children.Add(greeting);
        banners = new StackPanel { Spacing = 10 };
        session = new StackPanel();
        five = new LimitCard("5 Stunden"); week = new LimitCard("Woche");
        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*") };
        Grid.SetColumn(week, 2);
        limits.Children.Add(five); limits.Children.Add(week);
        var right = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 18, Margin = new Thickness(28, 26, 28, 26), Children = { top, banners, session, limits } },
        };
        var root = new DockPanel();
        DockPanel.SetDock(leftBorder, Dock.Left);
        root.Children.Add(leftBorder); root.Children.Add(right);
        Content = Look.NeonBackground(root);
        if (Engine.Shared.State is { } st) Apply(st);
    }

    Control Banner(string icon, string text, string action, Action run)
    {
        var row = new DockPanel();
        var b = Look.NeonButton(action, run);
        DockPanel.SetDock(b, Dock.Right);
        row.Children.Add(b);
        row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center,
            Children = { Look.Tx(icon, 16, color: Look.Accent), Look.Tx(text, 13) } });
        return Look.Glass(row, 12, Look.Accent);
    }

    void Apply(EngineState s)
    {
        updating = true;
        var set = AppSettings.Shared;
        int h = DateTime.Now.Hour;
        greeting.Text = h < 11 ? "Guten Morgen" : h < 18 ? "Hallo" : "Guten Abend";
        subtitle.Text = string.IsNullOrWhiteSpace(set.Host) ? "Noch kein Display eingerichtet"
            : s.Device is { } d ? $"{d.Ip} · Firmware {d.Version}" : set.Host;
        mark.Spinning = s.Session != null;
        pill.Set(s);
        preview.Update(s.PreviewBgra);
        if (!brightness.IsPointerOver)
        {
            brightness.Value = set.IsNight() ? set.NightBrightness : set.DayBrightness;
            brightnessLabel.Text = $"{(int)brightness.Value}%";
        }

        banners.Children.Clear();
        var claude = ClaudeConnect.Shared; var up = Updater.Shared;
        if (!claude.IsConnected && claude.State != ClaudeState.Checking)
            banners.Children.Add(Banner("◉", "Claude ist nicht verbunden – ohne Login gibt es kein Nutzungslimit.", "Verbinden", openSetup));
        if (string.IsNullOrWhiteSpace(set.Host))
            banners.Children.Add(Banner("▣", "Noch kein Display eingerichtet.", "Einrichten", openSetup));
        if (up.AppUpdate)
            banners.Children.Add(Banner("⬇", $"Version {up.Latest!.AppVersion} ist verfügbar.", "Aktualisieren", () => _ = up.InstallApp(shutdown)));
        else if (up.FirmwareUpdate(s.Device?.Version))
            banners.Children.Add(Banner("⌁", $"Display-Firmware {up.Latest!.FirmwareVersion} ist verfügbar.", "Aufspielen", () => _ = up.InstallFirmware()));

        session.Children.Clear();
        if (s.Session is { } ses)
        {
            var timer = Look.Tx(Renderer.FmtTimer(ses.Secs), 15, FontWeight.SemiBold, Look.Secondary);
            var label = Look.Label("● Arbeitet"); label.Foreground = Look.B(Look.Accent2);
            var hdr = new DockPanel();
            DockPanel.SetDock(timer, Dock.Right);
            hdr.Children.Add(timer); hdr.Children.Add(label);
            var name = Look.Tx(ses.Name, 22, FontWeight.Bold); name.TextWrapping = TextWrapping.Wrap;
            var inner = new StackPanel { Spacing = 14, Children = { hdr, name } };
            if (ses.Plan.Count > 0)
            {
                var bar = new SlimBar(8);
                bar.Set((double)ses.Done / ses.Plan.Count, Look.Accent, Look.Accent2);
                var cnt = Look.Tx($"{ses.Done}/{ses.Plan.Count}", 13, FontWeight.Bold);
                var pr = new DockPanel();
                DockPanel.SetDock(cnt, Dock.Right); cnt.Margin = new Thickness(10, 0, 0, 0);
                pr.Children.Add(cnt); pr.Children.Add(bar);
                inner.Children.Add(pr);
                var steps = new StackPanel { Spacing = 8 };
                foreach (var p in ses.Plan.Take(8))
                {
                    var (icon, col) = p.Status switch
                    {
                        "completed" => ("✓", Look.D.Good.Ui),
                        "in_progress" => ("▶", Look.Accent),
                        _ => ("○", Look.Secondary),
                    };
                    var t = Look.Tx(p.Text, 13, p.Status == "in_progress" ? FontWeight.SemiBold : FontWeight.Normal,
                        p.Status == "pending" ? Look.Secondary : Look.Text, trim: true);
                    if (p.Status == "completed") t.TextDecorations = TextDecorations.Strikethrough;
                    steps.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
                        Children = { Look.Tx(icon, 13, FontWeight.Bold, col), t } });
                }
                inner.Children.Add(steps);
            }
            else if (ses.Prompt != null)
            {
                var pt = Look.Tx(ses.Prompt, 14, color: Look.Secondary); pt.TextWrapping = TextWrapping.Wrap; pt.MaxLines = 3;
                inner.Children.Add(pt);
            }
            session.Children.Add(Look.Glass(inner, 20, Look.Accent));
        }
        else
        {
            var t1 = Look.Tx("Gerade läuft keine Claude-Sitzung", 16, FontWeight.SemiBold); t1.HorizontalAlignment = HorizontalAlignment.Center;
            var t2 = Look.Tx("Sobald Claude Code arbeitet, siehst du hier Fortschritt und Schritte.", 12.5, color: Look.Secondary);
            t2.HorizontalAlignment = HorizontalAlignment.Center;
            var moon = Look.Tx("☾", 30, color: Look.Accent); moon.HorizontalAlignment = HorizontalAlignment.Center;
            session.Children.Add(Look.Glass(new StackPanel { Spacing = 8, Margin = new Thickness(0, 10), Children = { moon, t1, t2 } }, 20));
        }
        five.Set(s.Usage?.Five);
        week.Set(s.Usage?.Week);
        updating = false;
    }
}
