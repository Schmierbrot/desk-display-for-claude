using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClaudeDisplay.Core;

namespace ClaudeDisplay.UI;

/// <summary>Einstellungsfenster mit Seitenleiste.</summary>
public sealed class SettingsWindow : Window
{
    public const string BundledVersion = "1.0";
    static readonly string[] Pages = ["Display", "Claude", "Darstellung", "Updates", "Allgemein"];
    static readonly string[] Icons = ["▣", "◉", "◐", "⟳", "⚙"];
    readonly Action openSetup, shutdown, applyMode;
    int page;
    DevicePreview? preview;
    StatusPill pill = null!;
    TextBlock statusText = null!;
    TextBlock? fwOnDevice;
    TextBlock fwMessage = null!;
    StackPanel deviceRows = null!;
    ProgressBar fwProgress = null!;
    Button fwBundled = null!, fwFile = null!;
    readonly Action<EngineState> onState;
    readonly Action onTheme;

    public SettingsWindow(Action openSetup, Action shutdown, Action applyMode)
    {
        this.openSetup = openSetup; this.shutdown = shutdown; this.applyMode = applyMode;
        Title = "Claude Display – Einstellungen";
        Icon = Look.AppIcon;
        Look.Size(this, 820, 580);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        onState = s => Dispatcher.UIThread.Post(() => Apply(s));
        onTheme = () => Dispatcher.UIThread.Post(Build);
        Action rebuild = () => Dispatcher.UIThread.Post(() => { if (page is 1 or 3) Build(); });
        Engine.Shared.Changed += onState;
        Look.Changed += onTheme;
        ClaudeConnect.Shared.Changed += rebuild;
        Updater.Shared.Changed += rebuild;
        Closed += (_, _) => { Engine.Shared.Changed -= onState; Look.Changed -= onTheme; ClaudeConnect.Shared.Changed -= rebuild; Updater.Shared.Changed -= rebuild; };
        Build();
    }

    void Build()
    {
        preview = null; fwOnDevice = null;
        var side = new StackPanel { Spacing = 4, Margin = new Thickness(12, 26, 12, 12) };
        side.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(10, 0, 0, 18),
            Children = { new AppMark(22), Look.Tx("Claude Display", 14, FontWeight.Bold) } });
        for (int i = 0; i < Pages.Length; i++)
        {
            int idx = i;
            bool sel = i == page;
            var b = new Button
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8), CornerRadius = new CornerRadius(10),
                Background = sel ? Look.Gradient(Color.FromArgb(90, Look.Accent.R, Look.Accent.G, Look.Accent.B),
                                                 Color.FromArgb(90, Look.Accent2.R, Look.Accent2.G, Look.Accent2.B)) : Brushes.Transparent,
                BorderBrush = Look.B(Colors.White, sel ? 0.18 : 0), BorderThickness = new Thickness(1),
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10,
                    Children = { Look.Tx(Icons[i], 14, color: Look.Secondary), Look.Tx(Pages[i], 13, sel ? FontWeight.SemiBold : FontWeight.Normal) } },
            };
            b.Click += (_, _) => { page = idx; Build(); };
            side.Children.Add(b);
        }
        var sideBorder = new Border { Width = 210, Background = Look.B(Colors.Black, 0.25), Child = side };

        Control content = page switch
        {
            0 => DisplayPage(),
            1 => ClaudePage(),
            2 => AppearancePage(),
            3 => UpdatesPage(),
            _ => GeneralPage(),
        };
        var body = new ScrollViewer
        {
            Content = new StackPanel { Spacing = 18, Margin = new Thickness(28, 26, 30, 26),
                Children = { Look.Tx(Pages[page], 24, FontWeight.Bold), content } },
        };
        var root = new DockPanel();
        DockPanel.SetDock(sideBorder, Dock.Left);
        root.Children.Add(sideBorder);
        root.Children.Add(body);
        Content = Look.NeonBackground(root);
        if (Engine.Shared.State is { } st) Apply(st);
    }

    // ---- Bausteine -----------------------------------------------------------

    static Control Card(string title, params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 14 };
        foreach (var r in rows) panel.Children.Add(r);
        return new StackPanel { Spacing = 8, Children = { Look.Label(title), Look.Glass(panel, 16) } };
    }

    static Control Row(string label, Control value, string? hint = null)
    {
        var d = new DockPanel();
        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Look.Tx(label, 13, FontWeight.Medium) } };
        if (hint != null) { var h = Look.Tx(hint, 11, color: Look.Secondary); h.TextWrapping = TextWrapping.Wrap; h.MaxWidth = 300; left.Children.Add(h); }
        value.VerticalAlignment = VerticalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(value, Dock.Right);
        d.Children.Add(value);
        d.Children.Add(left);
        return d;
    }

    static Control Note(string text)
    {
        var t = Look.Tx(text, 11, color: Look.Secondary);
        t.TextWrapping = TextWrapping.Wrap;
        return t;
    }

    static ToggleSwitch Switch(bool on, Action<bool> set)
    {
        var t = new ToggleSwitch { IsChecked = on, OnContent = "", OffContent = "" };
        t.IsCheckedChanged += (_, _) => set(t.IsChecked == true);
        return t;
    }

    // ---- Display -------------------------------------------------------------

    Control DisplayPage()
    {
        var s = AppSettings.Shared;
        preview = new DevicePreview(0.62);
        pill = new StatusPill();
        statusText = Look.Tx("", 13, color: Look.Secondary);
        statusText.TextWrapping = TextWrapping.Wrap;
        deviceRows = new StackPanel { Spacing = 10 };
        var hero = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22,
            Children = { preview, new StackPanel { Spacing = 10, VerticalAlignment = VerticalAlignment.Center, Width = 320, Children = { pill, statusText } } } };

        var host = new TextBox { Text = s.Host, Width = 170, Watermark = "z. B. 192.168.1.50" };
        void Apply_()
        {
            s.Host = (host.Text ?? "").Trim();
            s.SetupDone = s.Host.Length > 0;
            s.Save();
            Engine.Shared.SettingsChanged();
        }
        host.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Apply_(); };
        Button search = null!;
        search = Look.NeonButton("Suchen", async () =>
        {
            search.IsEnabled = false;
            var found = await Discovery.Scan();
            search.IsEnabled = true;
            if (found.FirstOrDefault(d => d.Kind == DeviceKind.Ready) is { } d) { host.Text = d.Ip; Apply_(); }
        }, prominent: false);

        return new StackPanel
        {
            Spacing = 20,
            Children =
            {
                hero,
                Card("Verbindung",
                    Row("Display ansteuern", Switch(s.Enabled, v => { s.Enabled = v; s.Save(); Engine.Shared.SettingsChanged(); })),
                    Row("IP-Adresse", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { host, Look.NeonButton("Übernehmen", Apply_, prominent: false), search } })),
                BrightnessPage(),
                Card("Gerät", deviceRows),
            },
        };
    }

    // ---- Darstellung ---------------------------------------------------------

    Control AppearancePage()
    {
        var s = AppSettings.Shared;
        void Changed() { s.Save(); Engine.Shared.SettingsChanged(); Look.NotifyChanged(); }

        var tiles = new WrapPanel();
        foreach (var p in Themes.Presets)
            tiles.Children.Add(ThemeTile(p.Name, Look.ThemePreview(p.Display), s.DisplayThemeId == p.Id, () => { s.DisplayThemeId = p.Id; Changed(); }));
        tiles.Children.Add(ThemeTile("Eigene", Look.ThemePreview(s.DisplayCustom), s.DisplayThemeId == "custom", () =>
        {
            if (s.DisplayThemeId != "custom") s.DisplayCustom = s.DisplayTheme;
            s.DisplayThemeId = "custom"; Changed();
        }));

        var stack = new StackPanel { Spacing = 20 };
        stack.Children.Add(Card("Display – Vorlage", tiles));

        if (s.DisplayThemeId == "custom")
        {
            var c = s.DisplayCustom;
            var title = new TextBox { Text = c.Title, Width = 220 };
            title.LostFocus += (_, _) => { s.DisplayCustom = s.DisplayCustom with { Title = title.Text ?? "" }; Changed(); };
            var logo = new ComboBox { Width = 160, ItemsSource = new[] { "Ring", "Stern", "Punkt", "Kein Logo" }, SelectedIndex = (int)c.Logo };
            logo.SelectionChanged += (_, _) => { s.DisplayCustom = s.DisplayCustom with { Logo = (LogoStyle)Math.Max(0, logo.SelectedIndex) }; Changed(); };
            Control Col(string label, RGB value, Func<RGB, DisplayTheme> with) =>
                Row(label, ColorField(value, v => { s.DisplayCustom = with(v); Changed(); }));
            stack.Children.Add(Card("Eigenes Display-Design",
                Row("Titel oben", title, "Text neben dem Logo"),
                Row("Logo", logo),
                Col("Akzent", c.Accent, v => s.DisplayCustom with { Accent = v }),
                Col("Akzent 2 (Verlauf)", c.Accent2, v => s.DisplayCustom with { Accent2 = v }),
                Col("Text", c.Text, v => s.DisplayCustom with { Text = v }),
                Col("Text gedämpft", c.Muted, v => s.DisplayCustom with { Muted = v }),
                Col("Text schwach", c.Dim, v => s.DisplayCustom with { Dim = v }),
                Col("Rahmen", c.Edge, v => s.DisplayCustom with { Edge = v }),
                Col("Balken-Hintergrund", c.Track, v => s.DisplayCustom with { Track = v }),
                Col("Gut (unter 60 %)", c.Good, v => s.DisplayCustom with { Good = v }),
                Col("Achtung (60–85 %)", c.Warn, v => s.DisplayCustom with { Warn = v }),
                Col("Kritisch (über 85 %)", c.Bad, v => s.DisplayCustom with { Bad = v }),
                Row("Aus Vorlage übernehmen", PresetMenu(p => { s.DisplayCustom = p.Display; Changed(); })),
                Note("Tipp: Dunkle Grautöne zeigt das Display heller als hier – Rahmen und Balken-Hintergrund lieber dunkel wählen.")));
        }

        var layouts = new WrapPanel();
        foreach (var p in LayoutPresets.All)
            layouts.Children.Add(ThemeTile(p.Name, Look.LayoutPreview(p.Layout), s.DisplayLayout.SameAs(p.Layout.Normalized()), () =>
            {
                s.DisplayLayout = p.Layout.Normalized(); Changed();
            }));
        if (!LayoutPresets.All.Any(p => s.DisplayLayout.SameAs(p.Layout.Normalized())))
            layouts.Children.Add(ThemeTile("Eigenes", Look.LayoutPreview(s.DisplayLayout), true, LayoutEditorWindow.Open));
        stack.Children.Add(Card("Display – Layout", layouts,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = {
                Look.NeonButton("Layout-Editor öffnen", LayoutEditorWindow.Open),
                Note("Elemente frei verschieben, skalieren, ausblenden und eigene Texte hinzufügen.") } }));

        var chips = new WrapPanel();
        foreach (var p in Themes.Presets)
            chips.Children.Add(AppChip(p.Name, p.App, s.AppThemeId == p.Id, () => { s.AppThemeId = p.Id; Changed(); }));
        chips.Children.Add(AppChip("Eigene", s.AppCustom, s.AppThemeId == "custom", () =>
        {
            if (s.AppThemeId != "custom") s.AppCustom = s.AppTheme;
            s.AppThemeId = "custom"; Changed();
        }));
        var appRows = new List<Control> { chips };
        if (s.AppThemeId == "custom")
        {
            var a = s.AppCustom;
            appRows.Add(Row("Akzent", ColorField(a.Accent, v => { s.AppCustom = s.AppCustom with { Accent = v }; Changed(); })));
            appRows.Add(Row("Akzent 2 (Verlauf)", ColorField(a.Accent2, v => { s.AppCustom = s.AppCustom with { Accent2 = v }; Changed(); })));
            appRows.Add(Row("Hintergrund", ColorField(a.Background, v => { s.AppCustom = s.AppCustom with { Background = v }; Changed(); })));
            appRows.Add(Row("Karten", ColorField(a.Surface, v => { s.AppCustom = s.AppCustom with { Surface = v }; Changed(); })));
            appRows.Add(Row("Aus Vorlage übernehmen", PresetMenu(p => { s.AppCustom = p.App; Changed(); })));
        }
        stack.Children.Add(Card("App – Farben", appRows.ToArray()));
        return stack;
    }

    static Control ThemeTile(string name, Avalonia.Media.Imaging.Bitmap img, bool selected, Action click)
    {
        var image = new Image { Source = img, Width = 128, Height = 128 };
        RenderOptions.SetBitmapInterpolationMode(image, Avalonia.Media.Imaging.BitmapInterpolationMode.HighQuality);
        var frame = new Border
        {
            Child = image, CornerRadius = new CornerRadius(10), ClipToBounds = true,
            BorderBrush = selected ? Look.AccentGradient : Look.B(Colors.White, 0.1),
            BorderThickness = new Thickness(selected ? 2.5 : 1),
            BoxShadow = selected ? new BoxShadows(new BoxShadow { Blur = 16, Color = Color.FromArgb(120, Look.Accent.R, Look.Accent.G, Look.Accent.B) }) : default,
        };
        var b = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6), Margin = new Thickness(0, 0, 10, 10),
            Content = new StackPanel { Spacing = 8, Children = { frame, new TextBlock { Text = name, HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 12, FontWeight = selected ? FontWeight.Bold : FontWeight.Medium } } },
        };
        b.Click += (_, _) => click();
        return b;
    }

    static Control AppChip(string name, AppTheme t, bool selected, Action click)
    {
        var circle = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(18),
            Background = Look.Gradient(t.Accent.Ui, t.Accent2.Ui),
            BorderBrush = Look.B(Colors.White, selected ? 0.9 : 0.15), BorderThickness = new Thickness(selected ? 2.5 : 1),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var b = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4), Margin = new Thickness(0, 0, 8, 0),
            Content = new StackPanel { Spacing = 6, Children = { circle, new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center,
                FontWeight = selected ? FontWeight.Bold : FontWeight.Normal } } },
        };
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>Hex-Eingabe mit Farbfeld</summary>
    static Control ColorField(RGB value, Action<RGB> set)
    {
        var swatch = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = Look.B(value.Ui),
            BorderBrush = Look.B(Colors.White, 0.2), BorderThickness = new Thickness(1) };
        var box = new TextBox { Text = "#" + value.HexString, Width = 96, FontFamily = new FontFamily("Consolas, Menlo, monospace") };
        void Commit()
        {
            var t = (box.Text ?? "").Trim().TrimStart('#');
            if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out _))
            {
                var c = RGB.Hex(t);
                if (c == value) return;
                swatch.Background = Look.B(c.Ui);
                set(c);
            }
        }
        box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Commit(); };
        box.LostFocus += (_, _) => Commit();
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { box, swatch } };
    }

    static Control PresetMenu(Action<ThemePreset> pick)
    {
        var cb = new ComboBox { Width = 160, PlaceholderText = "Vorlage wählen", ItemsSource = Themes.Presets.Select(p => p.Name).ToList() };
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) pick(Themes.Presets[cb.SelectedIndex]); };
        return cb;
    }

    // ---- Helligkeit ----------------------------------------------------------

    static Control BrightnessPage()
    {
        var s = AppSettings.Shared;
        Control SliderRow(string label, double value, Action<double> set)
        {
            var val = Look.Tx($"{(int)value}%", 12, color: Look.Secondary);
            val.Width = 40; val.TextAlignment = TextAlignment.Right;
            var sl = new Slider { Minimum = 0, Maximum = 100, Value = value, Width = 260 };
            sl.PropertyChanged += (_, e) =>
            {
                if (e.Property != RangeBase.ValueProperty) return;
                set(sl.Value); val.Text = $"{(int)sl.Value}%"; s.Save(); Engine.Shared.SettingsChanged();
            };
            return Row(label, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { sl, val } });
        }
        ComboBox Hours(int value, Action<int> set)
        {
            var cb = new ComboBox { Width = 110, ItemsSource = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToList(), SelectedIndex = value };
            cb.SelectionChanged += (_, _) => { set(cb.SelectedIndex); s.Save(); Engine.Shared.SettingsChanged(); };
            return cb;
        }
        var nightRows = new StackPanel { Spacing = 14, IsVisible = s.NightMode, Children =
        {
            SliderRow("Nachts", s.NightBrightness, v => s.NightBrightness = v),
            Row("Von", Hours(s.NightStart, v => s.NightStart = v)),
            Row("Bis", Hours(s.NightEnd, v => s.NightEnd = v)),
        } };
        return Card("Helligkeit",
            SliderRow("Tagsüber", s.DayBrightness, v => s.DayBrightness = v),
            Row("Nachts dimmen", Switch(s.NightMode, v => { s.NightMode = v; nightRows.IsVisible = v; s.Save(); Engine.Shared.SettingsChanged(); })),
            nightRows,
            Note("Gilt auch, wenn kein Rechner läuft – das Display merkt sich den Plan."));
    }

    // ---- Claude --------------------------------------------------------------

    Control ClaudePage()
    {
        var s = AppSettings.Shared;
        return new StackPanel { Spacing = 20, Children =
        {
            Card("Konto", ClaudeView(openSetup)),
            Card("Nutzungslimit", Row("Live-Nutzungslimit", Switch(s.LiveUsage, v => { s.LiveUsage = v; s.Save(); }),
                "Etwa jede Minute über deinen Claude-Code-Login.")),
            Card("Sitzungen", Row("Cloud-Sitzungen anzeigen", Switch(s.CloudSessions, v => { s.CloudSessions = v; s.Save(); }),
                "Auch Claude Code im Browser oder in der Cloud – über deinen Claude-Code-Login. Sitzungen auf diesem Rechner haben Vorrang."),
                Row("Mehrere Sitzungen abwechselnd zeigen", Switch(s.RotateSessions, v => { s.RotateSessions = v; s.Save(); }),
                "Arbeiten mehrere gleichzeitig, wechselt das Display alle 8 Sekunden. Wartet eine auf dich, hat sie Vorrang.")),
        } };
    }

    /// <summary>Status und Aktionen der Claude-Verbindung (auch im Assistenten)</summary>
    public static Control ClaudeView(Action? _ = null)
    {
        var c = ClaudeConnect.Shared;
        var (title, sub) = c.State switch
        {
            ClaudeState.Checking => ("Prüfe Claude …", "Claude Code"),
            ClaudeState.NotInstalled => ("Claude Code fehlt", "Die App liest Sitzungen und Limit über die offizielle Claude-Code-App. Sie wird mit einem Klick installiert."),
            ClaudeState.Installing => ("Claude Code wird installiert …", "Offizieller Installer von claude.ai – im geöffneten Fenster"),
            ClaudeState.LoggedOut => ("Nicht angemeldet", "Melde dich mit deinem eigenen Claude-Konto an (Pro oder Max)."),
            ClaudeState.LoggingIn => ("Anmeldung läuft", "Melde dich im Browser an. Fragt das Konsolenfenster nach einem Code, füge ihn dort ein."),
            ClaudeState.Connected => (c.Plan is { } pl ? $"Verbunden · {pl}" : "Verbunden", "Sitzungen und Nutzungslimit kommen aus deinem Claude-Code-Login."),
            _ => (c.Error ?? "Fehler", "Bitte erneut versuchen."),
        };
        Control action = c.State switch
        {
            ClaudeState.NotInstalled or ClaudeState.Failed => Look.NeonButton("Claude Code installieren", c.Install),
            ClaudeState.LoggedOut => Look.NeonButton("Mit Claude anmelden", c.Login),
            ClaudeState.LoggingIn => Look.NeonButton("Abbrechen", c.Cancel, prominent: false),
            ClaudeState.Connected => Look.NeonButton("Abmelden", c.Logout, prominent: false),
            _ => new ProgressBar { IsIndeterminate = true, Width = 80 },
        };
        var circle = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Background = Look.B(Look.Accent, 0.22),
            Child = new TextBlock { Text = c.IsConnected ? "✓" : "◉", FontSize = 19, FontWeight = FontWeight.Bold,
                Foreground = Look.B(c.IsConnected ? Look.D.Good.Ui : Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var subT = Look.Tx(sub, 12, color: Look.Secondary); subT.TextWrapping = TextWrapping.Wrap;
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0),
            Children = { Look.Tx(title, 15, FontWeight.SemiBold), subT } };
        var row = new DockPanel();
        DockPanel.SetDock(circle, Dock.Left); DockPanel.SetDock(action, Dock.Right);
        action.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(circle); row.Children.Add(action); row.Children.Add(text);
        return row;
    }

    // ---- Updates -------------------------------------------------------------

    Control UpdatesPage()
    {
        var up = Updater.Shared;
        var s = AppSettings.Shared;
        fwOnDevice = Look.Tx("unbekannt", 13, color: Look.Secondary);
        fwMessage = Look.Tx("", 12, color: Look.Secondary);
        fwMessage.TextWrapping = TextWrapping.Wrap;
        fwProgress = new ProgressBar { IsIndeterminate = true, IsVisible = false };

        var appButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (up.AppUpdate) appButtons.Children.Add(Look.NeonButton($"Auf {up.Latest!.AppVersion} aktualisieren", () => _ = up.InstallApp(shutdown)));
        var check = Look.NeonButton(up.Checking ? "Suche …" : "Nach Updates suchen", () => _ = up.Check(), prominent: !up.AppUpdate);
        check.IsEnabled = !up.Checking;
        appButtons.Children.Add(check);
        if (up.Latest?.Page is { } page)
            appButtons.Children.Add(Look.NeonButton("Auf GitHub ansehen", () => OpenUrl(page), prominent: false));

        fwBundled = up.FirmwareUpdate(Engine.Shared.State?.Device?.Version)
            ? Look.NeonButton($"Firmware {up.Latest!.FirmwareVersion} aufspielen", async () => await RunFw(up.InstallFirmware()))
            : Look.NeonButton($"Mitgelieferte Firmware {BundledVersion} aufspielen", async () =>
            {
                using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("firmware.bin.gz");
                if (res == null) { fwMessage.Text = "Keine Firmware in der App enthalten."; return; }
                using var ms = new MemoryStream();
                await res.CopyToAsync(ms);
                await RunFw(Engine.Shared.UpdateFirmwareAsync(ms.ToArray(), "firmware.bin.gz"));
            }, prominent: false);
        fwFile = Look.NeonButton("Datei wählen …", async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Firmware-Datei (.bin oder .bin.gz) wählen",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Firmware") { Patterns = ["*.bin", "*.gz"] }],
            });
            if (files.Count == 0) return;
            await using var st = await files[0].OpenReadAsync();
            using var ms = new MemoryStream();
            await st.CopyToAsync(ms);
            await RunFw(Engine.Shared.UpdateFirmwareAsync(ms.ToArray(), files[0].Name));
        }, prominent: false);

        return new StackPanel
        {
            Spacing = 20,
            Children =
            {
                Card("App",
                    Row("Installiert", Look.Tx(Updater.AppVersion, 13, FontWeight.SemiBold), up.Latest != null ? $"Neueste: {up.Latest.AppVersion}" : null),
                    appButtons,
                    Note(up.Message ?? ""),
                    Row("Automatisch prüfen", Switch(s.AutoUpdate, v => { s.AutoUpdate = v; s.Save(); }), "Einmal täglich auf GitHub")),
                Card("Display-Firmware",
                    Row("Auf dem Display", fwOnDevice),
                    Row("Neueste", Look.Tx(up.Latest?.FirmwareVersion ?? Updater.BundledFirmware, 13, color: Look.Secondary)),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { fwBundled, fwFile } },
                    fwProgress, fwMessage,
                    Note("Das Update läuft über WLAN und dauert etwa 20 Sekunden. Das Display startet danach neu.")),
            },
        };
    }

    static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    async Task RunFw(Task<string> job)
    {
        fwBundled.IsEnabled = fwFile.IsEnabled = false;
        fwProgress.IsVisible = true;
        fwMessage.Text = "Firmware wird übertragen – Display bitte nicht vom Strom trennen …";
        fwMessage.Text = await job;
        fwProgress.IsVisible = false;
        fwBundled.IsEnabled = fwFile.IsEnabled = true;
    }

    // ---- Allgemein -----------------------------------------------------------

    Control GeneralPage()
    {
        var s = AppSettings.Shared;
        var auto = Switch(Autostart.Enabled, Autostart.Set);
        auto.IsEnabled = Autostart.Supported;
        var modes = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,*") };
        var tray = ModeCard("tray", s.AppMode == "tray", () => { s.AppMode = "tray"; s.Save(); applyMode(); Build(); });
        var win = ModeCard("window", s.AppMode == "window", () => { s.AppMode = "window"; s.Save(); applyMode(); Build(); });
        Grid.SetColumn(win, 2);
        modes.Children.Add(tray); modes.Children.Add(win);
        var about = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Look.NeonButton("Einrichtungs-Assistent starten", openSetup, prominent: false) } };
        if (Updater.Repo is { } repo) about.Children.Add(Look.NeonButton("Projekt auf GitHub", () => OpenUrl($"https://github.com/{repo}"), prominent: false));
        return new StackPanel
        {
            Spacing = 20,
            Children =
            {
                Card("Darstellung der App", modes),
                Card("Schriftgröße", TextSizeButtons(), Note("Gilt für alle Fenster der App. Die Schrift auf dem Display stellst du im Layout-Editor ein.")),
                Card("Start", Row("Mit Windows starten", auto)),
                Card("Über",
                    Row("Version", Look.Tx(Updater.AppVersion, 13, color: Look.Secondary)),
                    Row("Rechner-Kennung", Look.Tx(s.HostId.ToString("X8"), 13, color: Look.Secondary)),
                    about),
            },
        };
    }

    /// <summary>Auswahlkarte: Infobereich oder App-Fenster</summary>
    static Control TextSizeButtons()
    {
        var s = AppSettings.Shared;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (v, name) in new[] { (0.9, "Klein"), (1.0, "Normal"), (1.15, "Groß"), (1.3, "Sehr groß") })
        {
            bool on = Math.Abs(s.TextScale - v) < 0.01;
            var b = new Button { Content = name, Padding = new Thickness(16, 7), CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(0),
                Background = on ? Look.AccentGradient : Look.B(Colors.White, 0.07), FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal };
            b.Click += (_, _) =>
            {
                if (Math.Abs(s.TextScale - v) < 0.01) return;
                s.TextScale = v; s.Save();
                Look.ApplySizes();
                Look.NotifyChanged();    // alle Fenster bauen sich neu auf
            };
            row.Children.Add(b);
        }
        return row;
    }

    public static Control ModeCard(string mode, bool selected, Action click)
    {
        bool win = mode == "window";
        var desc = Look.Tx(win ? "Großes Dashboard in der Taskleiste – wie eine normale App." : "Unauffällig im Infobereich, ein Klick öffnet das Panel.",
            11.5, color: Look.Secondary);
        desc.TextWrapping = TextWrapping.Wrap;
        var content = new StackPanel { Spacing = 8, Children =
        {
            Look.Tx(win ? "▭" : "▔", 26, color: Look.Accent),
            Look.Tx(win ? "App-Fenster" : "Infobereich", 14, FontWeight.SemiBold),
            desc,
        } };
        if (selected) content.Children.Add(Look.Tx("✓ ausgewählt", 11, FontWeight.Bold, Look.Accent));
        var b = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Look.Glass(content, 16, selected ? Look.Accent : null) };
        b.Click += (_, _) => click();
        return b;
    }

    // ---- Aktualisieren -------------------------------------------------------

    void Apply(EngineState s)
    {
        if (preview != null)
        {
            pill.Set(s);
            preview.Update(s.PreviewBgra);
            var host = AppSettings.Shared.Host;
            statusText.Text = s.Status switch
            {
                EngineStatus.Shown => "Das Display zeigt die Claude-Sitzungen dieses Rechners.",
                EngineStatus.Standby => "Ein anderer Rechner mit aktiver Claude-Sitzung hat gerade Vorrang. Danach übernimmt dieser Rechner wieder.",
                EngineStatus.Error => $"{s.Error}. Prüfe, ob das Display Strom hat und im WLAN ist.",
                EngineStatus.Off => string.IsNullOrWhiteSpace(host) ? "Noch kein Display eingerichtet." : "Pausiert – das Display zeigt seine eigene Uhr.",
                EngineStatus.Updating => "Firmware wird aktualisiert …",
                _ => $"Verbinde mit {host} …",
            };
            deviceRows.Children.Clear();
            if (s.Device is { } d)
            {
                string signal = d.Rssi > -55 ? "sehr gut" : d.Rssi > -67 ? "gut" : d.Rssi > -75 ? "mäßig" : "schwach";
                string owner = d.Owner == AppSettings.Shared.HostId ? "diesen Rechner" : d.Owner == 0 ? "eigene Uhr" : "einen anderen Rechner";
                foreach (var (k, v) in new[] {
                    ("Firmware", d.Version), ("IP-Adresse", d.Ip), ("MAC-Adresse", d.Mac),
                    ("WLAN-Signal", $"{d.Rssi} dBm · {signal}"), ("Läuft seit", Renderer.FmtReset(TimeSpan.FromSeconds(d.Uptime))),
                    ("Zeigt gerade", owner) })
                    deviceRows.Children.Add(Row(k, Look.Tx(v, 13, color: Look.Secondary)));
            }
            else deviceRows.Children.Add(Look.Tx("Keine Verbindung zum Display.", 13, color: Look.Secondary));
        }
        if (fwOnDevice != null) fwOnDevice.Text = s.Device?.Version ?? "unbekannt";
    }
}
