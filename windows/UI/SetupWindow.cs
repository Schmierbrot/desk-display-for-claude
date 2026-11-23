using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ClaudeDisplay.Core;

namespace ClaudeDisplay.UI;

/// <summary>Einrichtungs-Assistent: Display finden, ggf. Firmware aufspielen, Look wählen.</summary>
public sealed class SetupWindow : Window
{
    enum Page { Welcome, Claude, Search, Flash, Wifi, Look, Mode, Done }

    Page page = Page.Welcome;
    List<FoundDevice> devices = [];
    FoundDevice? selected;
    bool scanning, riskAccepted, flashing;
    string? flashError, manualMessage;
    string manualIp = "";
    string themeId = "neon";
    bool startAtLogin = true;
    string mode = AppSettings.Shared.AppMode;
    CancellationTokenSource? waitCts;
    readonly Action onFinish;

    public SetupWindow(Action onFinish)
    {
        this.onFinish = onFinish;
        Action rebuild = () => Dispatcher.UIThread.Post(() => { if (page == Page.Claude) Build(); });
        ClaudeConnect.Shared.Changed += rebuild;
        Closed += (_, _) => ClaudeConnect.Shared.Changed -= rebuild;
        Title = "Claude Display – Einrichtung";
        Icon = Look.AppIcon;
        Look.Size(this, 780, 580, track: false);
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Closed += (_, _) => waitCts?.Cancel();
        Build();
    }

    void Go(Page p) { page = p; Build(); }

    void Build()
    {
        var dots = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) };
        foreach (Page p in Enum.GetValues<Page>())
            dots.Children.Add(new Border
            {
                Width = p == page ? 28 : 8, Height = 8, CornerRadius = new CornerRadius(4),
                Background = p <= page ? Look.AccentGradient : Look.B(Colors.White, 0.15),
            });

        Control body = page switch
        {
            Page.Welcome => Welcome(),
            Page.Claude => ClaudeStep(),
            Page.Search => Search(),
            Page.Flash => Flash(),
            Page.Wifi => Wifi(),
            Page.Look => LookPage(),
            Page.Mode => ModeStep(),
            _ => Done(),
        };
        var root = new DockPanel();
        DockPanel.SetDock(dots, Dock.Top);
        root.Children.Add(dots);
        root.Children.Add(new Border { MaxWidth = 580, Margin = new Thickness(40, 0), Child = body });
        Content = Look.NeonBackground(root);
    }

    static Control Heading(string title, string sub)
    {
        var t = Look.Tx(title, 28, FontWeight.Bold);
        t.HorizontalAlignment = HorizontalAlignment.Center; t.TextAlignment = TextAlignment.Center;
        var s = Look.Tx(sub, 14, color: Look.Secondary);
        s.HorizontalAlignment = HorizontalAlignment.Center; s.TextAlignment = TextAlignment.Center; s.TextWrapping = TextWrapping.Wrap;
        return new StackPanel { Spacing = 8, Margin = new Thickness(0, 24, 0, 0), Children = { t, s } };
    }

    static Control Bottom(params Control[] buttons)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 28) };
        foreach (var b in buttons) p.Children.Add(b);
        return p;
    }

    static DockPanel Layout_(Control top, Control middle, Control bottom)
    {
        var d = new DockPanel();
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        d.Children.Add(top); d.Children.Add(bottom); d.Children.Add(middle);
        return d;
    }

    // 1 – Willkommen
    Control Welcome()
    {
        var mark = new AppMark(96) { HorizontalAlignment = HorizontalAlignment.Center, Spinning = true };
        var go = Look.NeonButton("Los geht's", () => { Go(Page.Claude); ClaudeConnect.Shared.Refresh(); });
        go.HorizontalAlignment = HorizontalAlignment.Center;
        return new StackPanel
        {
            Spacing = 26, VerticalAlignment = VerticalAlignment.Center,
            Children = { mark, Heading("Willkommen bei Claude Display",
                "Dein Schreibtisch-Display für Claude Code: laufende Sitzungen, Fortschritt und dein Nutzungslimit – live."), go },
        };
    }

    // 2 – Suchen
    void StartScan()
    {
        if (scanning) return;
        scanning = true; Build();
        Task.Run(async () =>
        {
            var r = await Discovery.Scan();
            Dispatcher.UIThread.Post(() => { devices = r; scanning = false; selected ??= r.FirstOrDefault(); if (page == Page.Search) Build(); });
        });
    }

    Control Search()
    {
        var list = new StackPanel { Spacing = 10 };
        foreach (var d in devices) list.Children.Add(DeviceRow(d));
        if (scanning) list.Children.Add(Look.Glass(new TextBlock { Text = "Suche läuft …", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Look.B(Look.Secondary) }));
        else if (devices.Count == 0)
        {
            var t = Look.Tx("Nichts gefunden. Ist das Display eingeschaltet und im selben WLAN? Bei der Original-Firmware steht die IP-Adresse unten auf dem Display.", 12, color: Look.Secondary);
            t.TextWrapping = TextWrapping.Wrap; t.TextAlignment = TextAlignment.Center;
            list.Children.Add(Look.Glass(t));
        }
        var ip = new TextBox { Text = manualIp, Width = 220, Watermark = "IP-Adresse selbst eingeben" };
        ip.TextChanged += (_, _) => manualIp = ip.Text ?? "";
        async void Check()
        {
            var a = manualIp.Trim();
            if (a.Length == 0) return;
            manualMessage = $"Prüfe {a} …"; Build();
            var d = await Discovery.Probe(a);
            if (d != null) { if (!devices.Contains(d)) devices.Insert(0, d); selected = d; manualMessage = null; }
            else manualMessage = $"Unter {a} antwortet kein SmallTV-Ultra.";
            Build();
        }
        ip.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) Check(); };
        var manual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center,
            Children = { ip, Look.NeonButton("Prüfen", Check, prominent: false), Look.NeonButton("Erneut suchen", StartScan, prominent: false) } };
        var middle = new StackPanel { Spacing = 14, Margin = new Thickness(0, 20, 0, 0), Children =
        {
            new ScrollViewer { MaxHeight = 230, Content = list },
            manual,
        } };
        if (manualMessage != null) middle.Children.Add(new TextBlock { Text = manualMessage, HorizontalAlignment = HorizontalAlignment.Center, Foreground = Look.B(Look.Secondary), FontSize = 12 });
        var next = Look.NeonButton("Weiter", () =>
        {
            if (selected == null) return;
            if (selected.Kind == DeviceKind.Ready) { Connect(selected.Ip); Go(Page.Look); } else Go(Page.Flash);
        });
        next.IsEnabled = selected != null;
        return Layout_(Heading("Display finden", scanning ? "Ich suche in deinem WLAN …" : "Wähle dein Display aus."), middle, Bottom(next));
    }

    Control DeviceRow(FoundDevice d)
    {
        bool sel = selected == d;
        var good = Look.D.Good.Ui; var warn = Look.D.Warn.Ui;
        var col = d.Kind == DeviceKind.Ready ? good : warn;
        var badge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 3), Background = Look.B(col, 0.15), VerticalAlignment = VerticalAlignment.Center,
            Child = Look.Tx(d.Kind == DeviceKind.Ready ? "Bereit" : "Einrichtung nötig", 11, FontWeight.Bold, col) };
        var check = Look.Tx(sel ? "●" : "○", 18, color: sel ? Look.Accent : Look.Secondary);
        check.VerticalAlignment = VerticalAlignment.Center;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { badge, check } };
        var text = new StackPanel { Spacing = 3, Children = { Look.Tx(d.Ip, 15, FontWeight.SemiBold), Look.Tx(d.Detail, 12, color: Look.Secondary) } };
        var row = new DockPanel();
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right); row.Children.Add(text);
        var b = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Look.Glass(row, 14, sel ? Look.Accent : null) };
        b.Click += (_, _) => { selected = d; Build(); };
        return b;
    }

    // 3 – Firmware aufspielen
    Control Flash()
    {
        var warn = Look.D.Warn.Ui;
        var notes = new StackPanel { Spacing = 8 };
        foreach (var n in new[] {
            "⚠  Wetter- und Fotofunktionen der Original-Firmware sind danach weg.",
            "🔑  Zurück zur Original-Firmware kann GeekMagic einen Aktivierungscode verlangen.",
            "⚡  Während des Updates (ca. 20 s) das Display nicht vom Strom trennen." })
        {
            var t = Look.Tx(n, 12.5, color: warn); t.TextWrapping = TextWrapping.Wrap; notes.Children.Add(t);
        }
        var accept = new CheckBox { Content = "Ich habe das verstanden", IsChecked = riskAccepted, HorizontalAlignment = HorizontalAlignment.Center };
        accept.IsCheckedChanged += (_, _) => { riskAccepted = accept.IsChecked == true; Build(); };
        var middle = new StackPanel { Spacing = 16, Margin = new Thickness(0, 20, 0, 0), Children = { Look.Glass(notes, 16, warn), accept } };
        if (flashing) middle.Children.Add(new ProgressBar { IsIndeterminate = true });
        if (flashError != null) middle.Children.Add(Look.Tx("Fehlgeschlagen: " + flashError, 12, color: Look.D.Bad.Ui));
        var back = Look.NeonButton("Zurück", () => Go(Page.Search), prominent: false);
        var go = Look.NeonButton("Jetzt aufspielen", DoFlash);
        go.IsEnabled = riskAccepted && !flashing; back.IsEnabled = !flashing;
        return Layout_(Heading("Claude-Firmware aufspielen",
            "Dein SmallTV-Ultra läuft noch mit der Original-Firmware. Für die Live-Anzeige bekommt es unsere Firmware – per WLAN, ohne Kabel."),
            middle, Bottom(back, go));
    }

    async void DoFlash()
    {
        if (selected == null) return;
        using var res = Assembly.GetExecutingAssembly().GetManifestResourceStream("firmware.bin.gz");
        if (res == null) { flashError = "Keine Firmware in der App enthalten."; Build(); return; }
        using var ms = new MemoryStream();
        await res.CopyToAsync(ms);
        flashing = true; flashError = null; Build();
        try
        {
            await new DisplayClient(selected.Ip, 0).UploadFirmwareAsync(ms.ToArray(), "firmware.bin.gz");
            flashing = false;
            Go(Page.Wifi);
            WaitForDisplay(selected.Ip);
        }
        catch (Exception e) { flashing = false; flashError = e.Message; Build(); }
    }

    // 4 – WLAN
    void WaitForDisplay(string previousIp)
    {
        waitCts?.Cancel();
        waitCts = new CancellationTokenSource();
        var ct = waitCts.Token;
        Task.Run(async () =>
        {
            for (int round = 0; !ct.IsCancellationRequested; round++)
            {
                await Task.Delay(4000, ct).ContinueWith(_ => { });
                FoundDevice? hit = await Discovery.Probe(previousIp);
                if (hit?.Kind != DeviceKind.Ready) hit = DisplayClient.Discover() is { } ip ? await Discovery.Probe(ip) : null;
                if (hit?.Kind != DeviceKind.Ready && round % 5 == 4) hit = (await Discovery.Scan()).FirstOrDefault(d => d.Kind == DeviceKind.Ready);
                if (hit?.Kind == DeviceKind.Ready)
                {
                    Dispatcher.UIThread.Post(() => { selected = hit; Connect(hit.Ip); Go(Page.Look); });
                    return;
                }
            }
        }, ct);
    }

    Control Wifi()
    {
        var steps = new StackPanel { Spacing = 14 };
        string[] texts =
        [
            "Öffne auf dem Handy die WLAN-Liste und verbinde dich mit „Claude-Display-Setup“ (ohne Passwort).",
            "Die Einrichtungsseite öffnet sich von selbst – sonst http://192.168.4.1 im Browser aufrufen.",
            "Tippe auf „Configure WiFi“, wähle dein WLAN, gib das Passwort ein und speichere.",
        ];
        for (int i = 0; i < texts.Length; i++)
        {
            var num = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Look.AccentGradient,
                Child = new TextBlock { Text = (i + 1).ToString(), FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            var t = Look.Tx(texts[i], 13); t.TextWrapping = TextWrapping.Wrap; t.VerticalAlignment = VerticalAlignment.Center;
            var row = new DockPanel();
            DockPanel.SetDock(num, Dock.Left); num.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(num); row.Children.Add(t);
            steps.Children.Add(row);
        }
        var wait = Look.Tx("Warte auf das Display … es meldet sich hier automatisch.", 13, color: Look.Secondary);
        wait.HorizontalAlignment = HorizontalAlignment.Center;
        return Layout_(Heading("Display ins WLAN bringen", "Die neue Firmware ist drauf. Jetzt braucht das Display einmal deine WLAN-Daten."),
            new StackPanel { Spacing = 18, Margin = new Thickness(0, 20, 0, 0), Children = { Look.Glass(steps, 18), new ProgressBar { IsIndeterminate = true, Width = 260 }, wait } },
            new Border());
    }

    // 5 – Look
    Control LookPage()
    {
        var grid = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var p in Themes.Presets)
        {
            bool sel = themeId == p.Id;
            var img = new Image { Source = Look.ThemePreview(p.Display), Width = 104, Height = 104 };
            var frame = new Border { Child = img, CornerRadius = new CornerRadius(8), ClipToBounds = true,
                BorderBrush = sel ? Look.Gradient(p.App.Accent.Ui, p.App.Accent2.Ui) : Look.B(Colors.White, 0.1), BorderThickness = new Thickness(sel ? 2.5 : 1) };
            var b = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(8), Margin = new Thickness(4),
                Content = new StackPanel { Spacing = 6, Children = { frame, new TextBlock { Text = p.Name, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 12,
                    FontWeight = sel ? FontWeight.Bold : FontWeight.Normal } } } };
            b.Click += (_, _) =>
            {
                themeId = p.Id;
                var s = AppSettings.Shared;
                s.DisplayThemeId = p.Id; s.AppThemeId = p.Id; s.Save();
                Engine.Shared.SettingsChanged();
                Look.NotifyChanged();
                Build();
            };
            grid.Children.Add(b);
        }
        return Layout_(Heading("Wähle deinen Look", "Für Display und App. Später jederzeit unter Einstellungen → Darstellung änderbar."),
            new Border { Margin = new Thickness(0, 18, 0, 0), Child = grid }, Bottom(Look.NeonButton("Weiter", () => Go(Page.Mode))));
    }

    // Claude verbinden
    Control ClaudeStep()
    {
        var note = Look.Tx("Die Anmeldung läuft über die offizielle Claude-Code-App von Anthropic. Dein Passwort sieht Claude Display nie.", 11.5, color: Look.Secondary);
        note.TextWrapping = TextWrapping.Wrap; note.TextAlignment = TextAlignment.Center; note.HorizontalAlignment = HorizontalAlignment.Center;
        var next = Look.NeonButton("Weiter", () => { Go(Page.Search); StartScan(); });
        next.IsEnabled = ClaudeConnect.Shared.IsConnected;
        var buttons = ClaudeConnect.Shared.IsConnected ? Bottom(next)
            : Bottom(Look.NeonButton("Später", () => { Go(Page.Search); StartScan(); }, prominent: false), next);
        return Layout_(Heading("Claude verbinden", "Die App zeigt deine eigenen Claude-Code-Sitzungen und dein Nutzungslimit. Dafür meldest du dich einmal mit deinem Claude-Konto an."),
            new StackPanel { Spacing = 16, Margin = new Thickness(0, 22, 0, 0), Children = { Look.Glass(SettingsWindow.ClaudeView(), 18), note } },
            buttons);
    }

    // Infobereich oder Fenster
    Control ModeStep()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*"), Margin = new Thickness(0, 22, 0, 0) };
        var tray = SettingsWindow.ModeCard("tray", mode == "tray", () => { mode = "tray"; Build(); });
        var win = SettingsWindow.ModeCard("window", mode == "window", () => { mode = "window"; Build(); });
        Grid.SetColumn(win, 2);
        grid.Children.Add(tray); grid.Children.Add(win);
        return Layout_(Heading("Wie möchtest du die App nutzen?", "Lässt sich später unter Einstellungen → Allgemein ändern."),
            grid, Bottom(Look.NeonButton("Weiter", () => Go(Page.Done))));
    }

    // 6 – Fertig
    Control Done()
    {
        var preview = new DevicePreview(0.62);
        if (Engine.Shared.State is { } st) preview.Update(st.PreviewBgra);
        var login = new CheckBox { Content = "Mit Windows automatisch starten", IsChecked = startAtLogin, IsEnabled = Autostart.Supported };
        login.IsCheckedChanged += (_, _) => startAtLogin = login.IsChecked == true;
        var live = new CheckBox { Content = "Live-Nutzungslimit anzeigen", IsChecked = AppSettings.Shared.LiveUsage };
        live.IsCheckedChanged += (_, _) => { AppSettings.Shared.LiveUsage = live.IsChecked == true; AppSettings.Shared.Save(); };
        var opts = Look.Glass(new StackPanel { Spacing = 10, Children = { login, live } }, 16);
        opts.HorizontalAlignment = HorizontalAlignment.Center;
        return Layout_(Heading("Alles bereit!", "Dein Display zeigt ab jetzt deine Claude-Code-Sitzungen."),
            new StackPanel { Spacing = 18, Margin = new Thickness(0, 18, 0, 0), Children = { preview, opts } },
            Bottom(Look.NeonButton("Los geht's", Finish)));
    }

    static void Connect(string ip)
    {
        var s = AppSettings.Shared;
        s.Host = ip; s.Enabled = true; s.Save();
        Engine.Shared.SettingsChanged();
    }

    void Finish()
    {
        var s = AppSettings.Shared;
        s.AppMode = mode;
        s.SetupDone = true; s.Save();
        if (startAtLogin && Autostart.Supported && !Autostart.Enabled) Autostart.Set(true);
        Engine.Shared.SettingsChanged();
        waitCts?.Cancel();
        Close();
        onFinish();
    }
}
