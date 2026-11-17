using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClaudeDisplay.Core;

namespace ClaudeDisplay.UI;

/// <summary>Kleines Panel, das sich beim Klick aufs Tray-Symbol öffnet.</summary>
public sealed class PanelWindow : Window
{
    readonly Action openSettings, quit, openSetup;
    AppMark mark = null!;
    TextBlock subtitle = null!;
    StatusPill pill = null!;
    DevicePreview preview = null!;
    Border sessionCard = null!;
    Button setupButton = null!;
    TextBlock sessName = null!, sessTime = null!, sessStep = null!, sessCount = null!;
    SlimBar sessProgress = null!;
    LimitCard five = null!, week = null!;
    Slider brightness = null!;
    TextBlock brightnessLabel = null!, brightnessIcon = null!;
    ToggleSwitch enabledSwitch = null!;
    bool updatingUi;

    public PanelWindow(Action openSettings, Action quit, Action openSetup)
    {
        this.openSettings = openSettings; this.quit = quit; this.openSetup = openSetup;
        Title = "Claude Display";
        Icon = Look.AppIcon;
        Width = 350;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.None];
        Background = Brushes.Transparent;
        Build();
        Look.Changed += () => Dispatcher.UIThread.Post(Build);
        Deactivated += (_, _) => Hide();
        Engine.Shared.Changed += s => Dispatcher.UIThread.Post(() => Apply(s));
    }

    void Build()
    {
        mark = new AppMark(26) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0) };
        subtitle = Look.Tx("", 11, color: Look.Secondary, trim: true);
        pill = new StatusPill();
        preview = new DevicePreview();
        sessName = Look.Tx("", 13, FontWeight.SemiBold, trim: true);
        sessTime = Look.Tx("", 12, FontWeight.Medium, Look.Secondary);
        sessStep = Look.Tx("", 12, color: Look.Secondary, trim: true);
        sessCount = Look.Tx("", 12, FontWeight.Bold);
        sessProgress = new SlimBar(6);
        five = new LimitCard("5 Stunden");
        week = new LimitCard("Woche");
        brightness = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        brightnessLabel = Look.Tx("", 11, FontWeight.SemiBold, Look.Secondary);
        brightnessIcon = Look.Tx("☀", 14, color: Look.Secondary);
        enabledSwitch = new ToggleSwitch { OnContent = "", OffContent = "", IsChecked = AppSettings.Shared.Enabled };

        // Kopf
        var titles = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center,
            Children = { Look.Tx("Claude Display", 15, FontWeight.Bold), subtitle } };
        var head = new DockPanel();
        DockPanel.SetDock(mark, Dock.Left);
        DockPanel.SetDock(pill, Dock.Right);
        head.Children.Add(mark); head.Children.Add(pill); head.Children.Add(titles);

        // Sitzung
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Look.B(Look.Accent2), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var sessHead = new DockPanel();
        DockPanel.SetDock(dot, Dock.Left); DockPanel.SetDock(sessTime, Dock.Right);
        sessHead.Children.Add(dot); sessHead.Children.Add(sessTime); sessHead.Children.Add(sessName);
        var sessFoot = new DockPanel();
        DockPanel.SetDock(sessCount, Dock.Right);
        sessStep.TextWrapping = TextWrapping.Wrap; sessStep.MaxLines = 2;
        sessFoot.Children.Add(sessCount); sessFoot.Children.Add(sessStep);
        sessionCard = Look.Glass(new StackPanel { Spacing = 9, Children = { sessHead, sessProgress, sessFoot } }, 12, Look.Accent);
        setupButton = Look.NeonButton("Display einrichten", openSetup);
        setupButton.HorizontalAlignment = HorizontalAlignment.Center;

        // Limits
        var limits = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        Grid.SetColumn(week, 2);
        limits.Children.Add(five); limits.Children.Add(week);

        // Helligkeit
        var bright = new DockPanel();
        DockPanel.SetDock(brightnessIcon, Dock.Left); DockPanel.SetDock(brightnessLabel, Dock.Right);
        brightnessIcon.Width = 22; brightnessIcon.VerticalAlignment = brightnessLabel.VerticalAlignment = VerticalAlignment.Center;
        brightnessLabel.Width = 38; brightnessLabel.TextAlignment = TextAlignment.Right;
        bright.Children.Add(brightnessIcon); bright.Children.Add(brightnessLabel); bright.Children.Add(brightness);
        brightness.PropertyChanged += (_, e) =>
        {
            if (e.Property != RangeBase.ValueProperty || updatingUi) return;
            var s = AppSettings.Shared;
            if (s.IsNight()) s.NightBrightness = brightness.Value; else s.DayBrightness = brightness.Value;
            brightnessLabel.Text = $"{(int)brightness.Value}%";
            s.Save();
            Engine.Shared.SettingsChanged();
        };

        // Fuß
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { Look.IconButton("⚙", "Einstellungen", openSettings), Look.IconButton("⏻", "Beenden", quit) } };
        var foot = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        foot.Children.Add(buttons);
        foot.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { enabledSwitch, new TextBlock { Text = "Display ansteuern", FontSize = 12, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center } } });
        enabledSwitch.IsCheckedChanged += (_, _) =>
        {
            if (updatingUi) return;
            AppSettings.Shared.Enabled = enabledSwitch.IsChecked == true;
            AppSettings.Shared.Save();
            Engine.Shared.SettingsChanged();
        };

        var stack = new StackPanel
        {
            Spacing = 14, Margin = new Thickness(18),
            Children = { head, preview, sessionCard, setupButton, limits, Look.Glass(bright, 10), foot },
        };
        Content = new Border
        {
            CornerRadius = new CornerRadius(18), ClipToBounds = true,
            BorderBrush = Look.B(Colors.White, 0.14), BorderThickness = new Thickness(1),
            Child = Look.NeonBackground(stack),
        };
        if (Engine.Shared.State is { } st) Apply(st);
    }

    void Apply(EngineState s)
    {
        if (!IsVisible) return;
        updatingUi = true;
        var set = AppSettings.Shared;
        pill.Set(s);
        subtitle.Text = string.IsNullOrWhiteSpace(set.Host) ? "Noch nicht eingerichtet" : s.Status switch
        {
            EngineStatus.Error => s.Error ?? "Display nicht erreichbar",
            EngineStatus.Standby => "Ein anderer Rechner zeigt gerade an",
            _ => s.Device is { } d ? $"{d.Ip} · Firmware {d.Version}" : set.Host,
        };
        mark.Spinning = s.Session != null;
        preview.Update(s.PreviewBgra);

        sessionCard.IsVisible = s.Session != null;
        setupButton.IsVisible = s.Session == null && string.IsNullOrWhiteSpace(set.Host);
        if (s.Session is { } ses)
        {
            sessName.Text = ses.Name;
            sessTime.Text = Renderer.FmtTimer(ses.Secs);
            sessProgress.IsVisible = sessCount.IsVisible = ses.Plan.Count > 0;
            sessProgress.Set(ses.Plan.Count == 0 ? 0 : (double)ses.Done / ses.Plan.Count, Look.Accent, Look.Accent2);
            sessCount.Text = $"{ses.Done}/{ses.Plan.Count}";
            sessStep.Text = ses.Plan.Count > 0 ? ses.CurrentStep ?? "Alle Schritte erledigt" : ses.Prompt ?? "";
        }
        five.Set(s.Usage?.Five);
        week.Set(s.Usage?.Week);

        bool night = set.IsNight();
        brightnessIcon.Text = night ? "☾" : "☀";
        if (!brightness.IsPointerOver)
        {
            brightness.Value = night ? set.NightBrightness : set.DayBrightness;
            brightnessLabel.Text = $"{(int)brightness.Value}%";
        }
        enabledSwitch.IsChecked = set.Enabled;
        updatingUi = false;
    }

    public void Toggle()
    {
        if (IsVisible) Hide(); else ShowNearTray();
    }

    /// <summary>Unten rechts über der Taskleiste (Windows) bzw. oben rechts (macOS).</summary>
    public void ShowNearTray()
    {
        Show();
        if (Engine.Shared.State is { } st) Apply(st);
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var wa = screen.WorkingArea;
            double scale = screen.Scaling;
            int w = (int)(Width * scale), h = (int)(Math.Max(Bounds.Height, 700) * scale), m = (int)(12 * scale);
            Position = OperatingSystem.IsMacOS()
                ? new PixelPoint(wa.Right - w - m, wa.Y + m)
                : new PixelPoint(wa.Right - w - m, wa.Bottom - h - m);
        }
        Activate();
    }
}
