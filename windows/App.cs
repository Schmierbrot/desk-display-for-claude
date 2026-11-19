using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using ClaudeDisplay.Core;
using ClaudeDisplay.UI;

namespace ClaudeDisplay;

public sealed class App : Application
{
    PanelWindow? panel;
    MainWindow? main;
    SettingsWindow? settings;
    SetupWindow? setup;
    ColorPaletteResources palette = null!;

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        var fluent = new FluentTheme();
        palette = new ColorPaletteResources
        {
            Accent = AppSettings.Shared.AppTheme.Accent.Ui,
            RegionColor = AppSettings.Shared.AppTheme.Background.Ui,
        };
        fluent.Palettes[ThemeVariant.Dark] = palette;
        Look.Changed += () => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            palette.Accent = AppSettings.Shared.AppTheme.Accent.Ui;
            palette.RegionColor = AppSettings.Shared.AppTheme.Background.Ui;
        });
        Styles.Add(fluent);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            panel = new PanelWindow(OpenSettings, Quit, OpenSetup);
            main = new MainWindow(OpenSettings, OpenSetup, Quit);

            var tray = new TrayIcon
            {
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://ClaudeDisplay/Assets/tray.ico"))),
                ToolTipText = "Claude Display",
                Menu = new NativeMenu(),
            };
            var show = new NativeMenuItem("Anzeigen");
            show.Click += (_, _) => { if (AppSettings.Shared.WindowMode) { main.Show(); main.Activate(); } else panel.ShowNearTray(); };
            var open = new NativeMenuItem("Einstellungen …");
            open.Click += (_, _) => OpenSettings();
            var quit = new NativeMenuItem("Beenden");
            quit.Click += (_, _) => Quit();
            tray.Menu.Items.Add(show);
            tray.Menu.Items.Add(open);
            tray.Menu.Items.Add(new NativeMenuItemSeparator());
            tray.Menu.Items.Add(quit);
            tray.Clicked += (_, _) =>
            {
                if (AppSettings.Shared.WindowMode) { main.Show(); main.Activate(); } else panel.Toggle();
            };
            TrayIcon.SetIcons(this, [tray]);

            Engine.Shared.Start();
            ClaudeConnect.Shared.Refresh();
            Updater.Shared.CheckIfDue();
            var updateTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            updateTimer.Tick += (_, _) => Updater.Shared.CheckIfDue();
            updateTimer.Start();
            if (AppSettings.Shared.SetupDone) ApplyMode(); else OpenSetup();

            // ClaudeDisplay --snapshot-ui pfad  -> Panel und Einstellungen als Bild speichern (Layout prüfen)
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--snapshot-ui");
            if (i >= 0 && i + 1 < args.Length)
            {
                var prefix = args[i + 1];
                OpenSettings();
                Avalonia.Threading.DispatcherTimer.RunOnce(() => panel.ShowNearTray(), TimeSpan.FromSeconds(5));
                Avalonia.Threading.DispatcherTimer.RunOnce(() =>
                {
                    Snapshot(panel, prefix + "-panel.png");
                    if (settings != null) Snapshot(settings, prefix + "-settings.png");
                    if (setup != null) Snapshot(setup, prefix + "-setup.png");
                    var le = new ClaudeDisplay.UI.LayoutEditorWindow("step"); le.Show();
                    var li = new ClaudeDisplay.UI.LayoutEditorWindow("bigclock", idleView: true); li.Show();
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => { Snapshot(le, prefix + "-layout.png"); Snapshot(li, prefix + "-layout-idle.png"); le.Close(); li.Close(); }, TimeSpan.FromSeconds(1));
                    main.Show();
                    Avalonia.Threading.DispatcherTimer.RunOnce(() => { Snapshot(main, prefix + "-main.png"); Quit(); }, TimeSpan.FromSeconds(1.5));
                }, TimeSpan.FromSeconds(7));
            }
        }
        base.OnFrameworkInitializationCompleted();
    }

    static void Snapshot(Window w, string path)
    {
        var size = new PixelSize((int)w.Bounds.Width, (int)w.Bounds.Height);
        using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(size, new Vector(96, 96));
        rtb.Render(w);
        rtb.Save(path);
    }

    void OpenSetup()
    {
        panel?.Hide();
        if (setup is { IsVisible: true }) { setup.Activate(); return; }
        setup = new SetupWindow(ApplyMode);
        setup.Show();
        setup.Activate();
    }

    void OpenSettings()
    {
        panel?.Hide();
        if (settings is { IsVisible: true }) { settings.Activate(); return; }
        settings = new SettingsWindow(OpenSetup, Quit, ApplyMode);
        settings.Show();
        settings.Activate();
    }

    /// <summary>Infobereich oder App-Fenster</summary>
    void ApplyMode()
    {
        if (main == null) return;
        if (AppSettings.Shared.WindowMode) { main.Show(); main.Activate(); }
        else main.Hide();
    }

    void Quit()
    {
        AppSettings.Shared.Save();
        if (main != null) main.AllowClose = true;
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
    }
}
