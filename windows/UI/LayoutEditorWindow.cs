using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClaudeDisplay.Core;

namespace ClaudeDisplay.UI;

/// <summary>
/// Editor für den Aufbau des Displays: Elemente ziehen, an den Griffen skalieren, ein- und
/// ausblenden, eigene Texte und Linien hinzufügen. Änderungen gehen sofort aufs Display.
/// </summary>
public sealed class LayoutEditorWindow : Window
{
    internal bool idle;
    internal string? selected;
    readonly List<DisplayLayout> undo = [], redo = [];
    readonly LayoutCanvas canvas;
    readonly StackPanel side = new() { Spacing = 18 };
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    Button undoBtn = null!, redoBtn = null!, activeTab = null!, idleTab = null!;

    static readonly SessionInfo SampleSession = new("Mein Projekt", 754,
        [new("a", "completed"), new("b", "completed"), new("Tests schreiben", "in_progress"), new("d", "pending")], null, 1);
    static readonly Usage SampleUsage = new(new(42, DateTime.UtcNow.AddHours(2)), new(67, DateTime.UtcNow.AddHours(47)), null);

    static LayoutEditorWindow? open;

    /// <summary>Öffnet den Editor (nur einmal) oder holt ihn nach vorn.</summary>
    public static void Open()
    {
        if (open is { IsVisible: true }) { open.Activate(); return; }
        open = new LayoutEditorWindow();
        open.Closed += (_, _) => { open = null; Look.NotifyChanged(); };
        open.Show();
        open.Activate();
    }

    static AppSettings S => AppSettings.Shared;
    internal List<LayoutItem> Items => idle ? S.DisplayLayout.Idle : S.DisplayLayout.Active;
    internal LayoutItem? Selected => Items.FirstOrDefault(i => i.Id == selected);

    public LayoutEditorWindow(string? select = null, bool idleView = false)
    {
        Title = "Claude Display – Display-Layout";
        Icon = Look.AppIcon;
        Look.Size(this, 1060, 720, 960, 680, track: false);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        selected = select; idle = idleView;
        canvas = new LayoutCanvas(this);

        var root = new DockPanel { Margin = new Thickness(24, 20, 24, 20) };
        var top = Toolbar();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var right = new ScrollViewer { Width = 340, Content = side, Margin = new Thickness(24, 0, 0, 0) };
        DockPanel.SetDock(right, Dock.Right);
        root.Children.Add(right);

        var frame = new Border
        {
            Padding = new Thickness(18), CornerRadius = new CornerRadius(26),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#292A33"), 0), new GradientStop(Color.Parse("#0C0D12"), 1) },
            },
            BorderBrush = Look.AccentGradient, BorderThickness = new Thickness(1.2),
            BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 6, Blur = 26, Color = Color.FromArgb(80, Look.Accent.R, Look.Accent.G, Look.Accent.B) }),
            Child = canvas, HorizontalAlignment = HorizontalAlignment.Center,
        };
        var hint = Look.Tx("Änderungen erscheinen sofort auf deinem Display. Alt beim Ziehen = ohne Einrasten.", 11.5, color: Look.Secondary);
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        root.Children.Add(new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Top, Children = { frame, hint } });
        Content = Look.NeonBackground(root);

        timer.Tick += (_, _) => canvas.Refresh(Preview());
        timer.Start();
        Closed += (_, _) => timer.Stop();
        KeyDown += OnKey;
        Changed(save: false);
    }

    // ---- Werkzeugleiste ------------------------------------------------------

    Control Toolbar()
    {
        var titles = new StackPanel { Spacing = 2, Children = {
            Look.Tx("Display-Layout", 22, FontWeight.Bold),
            Look.Tx("Ziehen zum Verschieben · Griffe zum Skalieren · Pfeiltasten für Feinarbeit", 11.5, color: Look.Secondary) } };
        activeTab = Tab("Arbeitet", () => { idle = false; selected = null; Changed(save: false); });
        idleTab = Tab("Bereit", () => { idle = true; selected = null; Changed(save: false); });
        undoBtn = Look.IconButton("↶", "Rückgängig (Strg+Z)", Undo);
        redoBtn = Look.IconButton("↷", "Wiederholen (Strg+Y)", Redo);
        var presets = new ComboBox { Width = 170, PlaceholderText = "Vorlage laden …",
            ItemsSource = LayoutPresets.All.Select(p => p.Name).Append("Nur diese Ansicht zurücksetzen").ToList() };
        presets.SelectionChanged += (_, _) =>
        {
            int i = presets.SelectedIndex;
            if (i < 0) return;
            if (i < LayoutPresets.All.Length) Commit(l => { var p = LayoutPresets.All[i].Layout; l.Active = p.Active; l.Idle = p.Idle; });
            else Commit(l => { if (idle) l.Idle = LayoutPresets.Standard.Idle; else l.Active = LayoutPresets.Standard.Active; });
            selected = null;
            presets.SelectedIndex = -1;
            Changed(save: false);
        };
        var tabs = new Border { CornerRadius = new CornerRadius(10), Background = Look.B(Colors.White, 0.06), Padding = new Thickness(3),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { activeTab, idleTab } } };
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { tabs, undoBtn, redoBtn, presets } };
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        DockPanel.SetDock(right, Dock.Right);
        d.Children.Add(right);
        d.Children.Add(titles);
        return d;
    }

    static Button Tab(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(16, 6), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(0), FontSize = 12.5 };
        b.Click += (_, _) => click();
        return b;
    }

    // ---- Vorschau ------------------------------------------------------------

    byte[] Preview()
    {
        var st = Engine.Shared.State;
        var sess = idle ? null : st?.Session ?? SampleSession;
        using var c = Renderer.Render(sess, st?.Usage ?? SampleUsage, (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var src = c.Bitmap.GetPixelSpan();
        var bgra = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4) { bgra[i] = src[i + 2]; bgra[i + 1] = src[i + 1]; bgra[i + 2] = src[i]; bgra[i + 3] = 255; }
        return bgra;
    }

    /// <summary>Alles neu zeichnen; save = Einstellungen speichern und ans Display.</summary>
    internal void Changed(bool save = true)
    {
        if (save) { S.Save(); Engine.Shared.SettingsChanged(); }
        canvas.Refresh(Preview());
        BuildSide();
        undoBtn.IsEnabled = undo.Count > 0; undoBtn.Opacity = undo.Count > 0 ? 1 : 0.4;
        redoBtn.IsEnabled = redo.Count > 0; redoBtn.Opacity = redo.Count > 0 ? 1 : 0.4;
        foreach (var (b, on) in new[] { (activeTab, !idle), (idleTab, idle) })
        {
            b.Background = on ? Look.AccentGradient : Brushes.Transparent;
            b.Foreground = Look.B(on ? Colors.White : Look.Secondary);
        }
    }

    // ---- Seitenleiste --------------------------------------------------------

    void BuildSide()
    {
        side.Children.Clear();
        var list = new StackPanel { Spacing = 2 };
        foreach (var item in Items) list.Children.Add(ElementRow(item));
        var add = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = {
            Look.NeonButton("+ Text", () => AddCustom(WidgetKind.Label), false),
            Look.NeonButton("+ Linie", () => AddCustom(WidgetKind.Line), false) } };
        side.Children.Add(Card("Elemente", list, add));
        if (Selected is { } sel) side.Children.Add(Inspector(sel));
    }

    static Control Card(string title, params Control[] rows)
    {
        var panel = new StackPanel { Spacing = 12 };
        foreach (var r in rows) panel.Children.Add(r);
        return new StackPanel { Spacing = 8, Children = { Look.Label(title), Look.Glass(panel, 14) } };
    }

    Control ElementRow(LayoutItem item)
    {
        bool sel = selected == item.Id;
        string name = item.Kind == WidgetKind.Label && !string.IsNullOrEmpty(item.Text) ? $"„{item.Text}“" : WidgetInfo.Label(item.Kind);
        var eye = new Button { Content = item.Visible ? "◉" : "○", Width = 30, Height = 26, Padding = new Thickness(0), Background = Brushes.Transparent,
            BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, Foreground = Look.B(item.Visible ? Look.Text : Look.Secondary) };
        ToolTip.SetTip(eye, item.Visible ? "Ausblenden" : "Einblenden");
        eye.Click += (_, e) =>
        {
            e.Handled = true;
            Commit(_ => Find(item.Id)!.Visible = !item.Visible);
            if (!Find(item.Id)!.Visible && selected == item.Id) selected = null;
            Changed(save: false);
        };
        var label = Look.Tx(name, 12.5, sel ? FontWeight.SemiBold : FontWeight.Normal, item.Visible ? Look.Text : Look.Secondary, trim: true);
        var icon = Look.Tx(WidgetInfo.Icon(item.Kind), 13, color: sel ? Look.Accent : Look.Secondary);
        icon.Width = 22;
        var d = new DockPanel();
        DockPanel.SetDock(eye, Dock.Right);
        d.Children.Add(eye);
        d.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { icon, label } });
        var row = new Border { Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(8), Child = d, Cursor = new Cursor(StandardCursorType.Hand),
            Background = sel ? Look.B(Look.Accent, 0.18) : Brushes.Transparent };
        row.PointerPressed += (_, _) =>
        {
            if (!item.Visible) Commit(_ => Find(item.Id)!.Visible = true);
            selected = item.Id;
            Changed(save: false);
            Focus();
        };
        return row;
    }

    Control Inspector(LayoutItem item)
    {
        var rows = new List<Control>
        {
            // Zahlenfelder bauen die Seitenleiste nicht neu – sonst verliert das Feld beim Tippen den Fokus
            Pair(Num("X", item.X, v => Edit(i => i.X = v, false)), Num("Y", item.Y, v => Edit(i => i.Y = v, false))),
            Pair(Num("Breite", item.W, v => Edit(i => i.W = v, false)), Num("Höhe", item.H, v => Edit(i => i.H = v, false))),
        };
        if (item.Kind == WidgetKind.Label)
        {
            var tb = new TextBox { Text = item.Text ?? "", Watermark = "Text" };
            tb.TextChanged += (_, _) => { if (Selected is { } s && s.Text != tb.Text) { s.Text = tb.Text; S.Save(); canvas.Refresh(Preview()); } };
            rows.Add(tb);
        }
        if (WidgetInfo.Alignable(item.Kind))
        {
            var al = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var (a, glyph) in new[] { (TextAlign.Left, "⇤ Links"), (TextAlign.Center, "↔ Mitte"), (TextAlign.Right, "Rechts ⇥") })
            {
                bool on = (item.Align ?? TextAlign.Left) == a;
                var b = new Button { Content = glyph, Padding = new Thickness(10, 5), CornerRadius = new CornerRadius(8), FontSize = 12,
                    Background = on ? Look.AccentGradient : Look.B(Colors.White, 0.07), BorderThickness = new Thickness(0) };
                b.Click += (_, _) => Edit(i => i.Align = a);
                al.Children.Add(b);
            }
            rows.Add(new StackPanel { Spacing = 4, Children = { Look.Tx("Ausrichtung", 11, FontWeight.SemiBold, Look.Secondary), al } });
        }
        if (item.IsCustom)
        {
            var roles = Enum.GetValues<ColorRole>();
            var cb = new ComboBox { ItemsSource = roles.Select(WidgetInfo.Label).ToList(), Width = 160,
                SelectedIndex = Array.IndexOf(roles, item.Color ?? (item.Kind == WidgetKind.Line ? ColorRole.Edge : ColorRole.Text)) };
            cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) Edit(i => i.Color = roles[cb.SelectedIndex]); };
            rows.Add(new StackPanel { Spacing = 4, Children = { Look.Tx("Farbe", 11, FontWeight.SemiBold, Look.Secondary), cb } });
        }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = {
            Look.IconButton("⤒", "Nach vorne", () => Reorder(true)),
            Look.IconButton("⤓", "Nach hinten", () => Reorder(false)),
            Look.IconButton("↔", "Waagerecht zentrieren", () => Edit(i => i.X = (240 - i.W) / 2)),
            item.IsCustom ? Look.NeonButton("Entfernen", RemoveSelected, false)
                          : Look.NeonButton("Ausblenden", () => { Edit(i => i.Visible = false); selected = null; Changed(save: false); }, false) } };
        rows.Add(actions);
        var hint = Look.Tx(Hint(item.Kind), 11, color: Look.Secondary);
        hint.TextWrapping = TextWrapping.Wrap;
        rows.Add(hint);
        return Card(WidgetInfo.Label(item.Kind), rows.ToArray());
    }

    static Control Pair(Control a, Control b)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*") };
        Grid.SetColumn(a, 0); Grid.SetColumn(b, 2);
        g.Children.Add(a); g.Children.Add(b);
        return g;
    }

    static Control Num(string label, double value, Action<double> set)
    {
        var n = new NumericUpDown { Value = (decimal)value, Minimum = 0, Maximum = 240, Increment = 1, FormatString = "0" };
        n.ValueChanged += (_, e) => { if (e.NewValue is { } v && (double)v != value) set((double)v); };
        return new StackPanel { Spacing = 3, Children = { Look.Tx(label, 10.5, FontWeight.SemiBold, Look.Secondary), n } };
    }

    static string Hint(WidgetKind k) => k switch
    {
        WidgetKind.Ring => "Der Ring passt sich der kürzeren Seite an.",
        WidgetKind.Name or WidgetKind.Step => "Die Höhe bestimmt die Schriftgröße, die Breite den Zeilenumbruch.",
        WidgetKind.Limit5 or WidgetKind.LimitWeek => "Die Höhe bestimmt die Größe von Zahl, Text und Balken.",
        WidgetKind.Line => "Waagerecht oder senkrecht – je nachdem, ob Breite oder Höhe größer ist.",
        _ => "Die Höhe bestimmt die Schriftgröße. Wird der Text zu breit, schrumpft er.",
    };

    // ---- Ändern --------------------------------------------------------------

    internal LayoutItem? Find(string id) => Items.FirstOrDefault(i => i.Id == id);

    /// <summary>Änderung mit Rückgängig-Schritt. Arbeitet auf einer Kopie, damit das Zeichnen im Hintergrund nie halbe Zustände sieht.</summary>
    internal void Commit(Action<DisplayLayout> f, bool undoable = true)
    {
        if (undoable) PushUndo();
        var l = S.DisplayLayout.Clone();
        var old = S.DisplayLayout;
        S.DisplayLayout = l;
        f(l);
        S.DisplayLayout = l.Normalized();
        if (!undoable) return;
        if (old.SameAs(S.DisplayLayout)) undo.RemoveAt(undo.Count - 1);
        S.Save(); Engine.Shared.SettingsChanged();
    }

    void Edit(Action<LayoutItem> f, bool rebuild = true)
    {
        if (selected == null) return;
        var id = selected;
        Commit(_ => { if (Find(id) is { } i) { f(i); i.Clamp(); } });
        if (rebuild) Changed(save: false);
        else { canvas.Refresh(Preview()); undoBtn.IsEnabled = undo.Count > 0; undoBtn.Opacity = undo.Count > 0 ? 1 : 0.4; }
    }

    /// <summary>Während des Ziehens: ohne Rückgängig-Schritt, ohne Speichern.</summary>
    internal void Live(LayoutItem n)
    {
        var l = S.DisplayLayout.Clone();
        var list = idle ? l.Idle : l.Active;
        int k = list.FindIndex(i => i.Id == n.Id);
        if (k < 0) return;
        list[k] = n.Clone();
        S.DisplayLayout = l;
        canvas.Refresh(Preview());
    }

    internal void PushUndo()
    {
        undo.Add(S.DisplayLayout.Clone());
        if (undo.Count > 80) undo.RemoveAt(0);
        redo.Clear();
    }

    internal void EndDrag()
    {
        if (undo.Count > 0 && undo[^1].SameAs(S.DisplayLayout)) undo.RemoveAt(undo.Count - 1);
        S.Save(); Engine.Shared.SettingsChanged();
        Changed(save: false);
    }

    void Undo()
    {
        if (undo.Count == 0) return;
        redo.Add(S.DisplayLayout.Clone());
        S.DisplayLayout = undo[^1]; undo.RemoveAt(undo.Count - 1);
        Changed();
    }

    void Redo()
    {
        if (redo.Count == 0) return;
        undo.Add(S.DisplayLayout.Clone());
        S.DisplayLayout = redo[^1]; redo.RemoveAt(redo.Count - 1);
        Changed();
    }

    void AddCustom(WidgetKind kind)
    {
        var id = kind.ToString().ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N")[..8];
        var item = kind == WidgetKind.Label
            ? new LayoutItem(WidgetKind.Label, 40, 100, 160, 22, align: TextAlign.Center, id: id, text: "Hallo", color: ColorRole.Text)
            : new LayoutItem(WidgetKind.Line, 20, 120, 200, 2, id: id, color: ColorRole.Edge);
        Commit(l => (idle ? l.Idle : l.Active).Add(item));
        selected = id;
        Changed(save: false);
    }

    void RemoveSelected()
    {
        if (selected == null || Selected?.IsCustom != true) return;
        var id = selected;
        Commit(l => (idle ? l.Idle : l.Active).RemoveAll(i => i.Id == id));
        selected = null;
        Changed(save: false);
    }

    void Reorder(bool front)
    {
        if (selected == null) return;
        var id = selected;
        Commit(l =>
        {
            var list = idle ? l.Idle : l.Active;
            int k = list.FindIndex(i => i.Id == id);
            if (k < 0) return;
            var it = list[k]; list.RemoveAt(k);
            if (front) list.Add(it); else list.Insert(0, it);
        });
        Changed(save: false);
    }

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || e.Source is NumericUpDown) return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (ctrl && e.Key == Key.Z) { if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Redo(); else Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
        if (selected == null) return;
        double step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        bool size = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        switch (e.Key)
        {
            case Key.Left: Edit(i => { if (size) i.W -= step; else i.X -= step; }); break;
            case Key.Right: Edit(i => { if (size) i.W += step; else i.X += step; }); break;
            case Key.Up: Edit(i => { if (size) i.H -= step; else i.Y -= step; }); break;
            case Key.Down: Edit(i => { if (size) i.H += step; else i.Y += step; }); break;
            case Key.Delete or Key.Back:
                if (Selected?.IsCustom == true) RemoveSelected();
                else { Edit(i => i.Visible = false); selected = null; Changed(save: false); }
                break;
            case Key.Escape: selected = null; Changed(save: false); break;
            default: return;
        }
        e.Handled = true;
    }
}

/// <summary>Zeichenfläche: Vorschau in doppelter Größe mit Rahmen, Griffen und Hilfslinien.</summary>
sealed class LayoutCanvas : Control
{
    const double S = 2, HandleR = 5.5;
    readonly LayoutEditorWindow ed;
    readonly WriteableBitmap bmp = Look.NewPreview();
    LayoutItem? start;
    Point startPos;
    (double x, double y)? handle;       // null = verschieben
    readonly List<(bool vertical, double pos)> guides = [];

    static readonly (double, double)[] Handles = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    public LayoutCanvas(LayoutEditorWindow ed)
    {
        this.ed = ed;
        Width = Height = 240 * S;
        ClipToBounds = true;
        Focusable = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
    }

    public void Refresh(byte[] bgra) { Look.Blit(bmp, bgra); InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        var all = new Rect(0, 0, 240 * S, 240 * S);
        using (ctx.PushClip(new RoundedRect(all, 6)))
        {
            ctx.FillRectangle(Brushes.Black, all);
            ctx.DrawImage(bmp, new Rect(0, 0, 240, 240), all);
        }
        var dashed = new Pen(Look.B(Colors.White, 0.22), 1, new DashStyle([4, 3], 0));
        foreach (var it in ed.Items.Where(i => i.Visible && i.Id != ed.selected))
            ctx.DrawRectangle(null, dashed, R(it).Deflate(0.5));
        if (ed.Selected is { Visible: true } sel)
        {
            var r = R(sel);
            ctx.DrawRectangle(Look.B(Look.Accent, 0.10), new Pen(Look.B(Look.Accent), 1.5), r.Deflate(0.75));
            foreach (var (hx, hy) in Handles)
            {
                var c = new Point(r.X + r.Width * (hx + 1) / 2, r.Y + r.Height * (hy + 1) / 2);
                ctx.DrawEllipse(Brushes.White, new Pen(Look.B(Look.Accent), 2), c, HandleR, HandleR);
            }
            var txt = new FormattedText($"{sel.X:0}, {sel.Y:0} · {sel.W:0} × {sel.H:0}", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 10.5, Brushes.White);
            double ty = r.Y > 24 ? r.Y - 21 : r.Bottom + 5;
            var bg = new Rect(r.X, ty, txt.Width + 12, txt.Height + 4);
            ctx.DrawRectangle(Look.B(Look.Accent), null, new RoundedRect(bg, 8));
            ctx.DrawText(txt, new Point(r.X + 6, ty + 2));
        }
        var gp = new Pen(Look.B(Look.Accent2, 0.9), 1);
        foreach (var (v, p) in guides)
            ctx.DrawLine(gp, v ? new Point(p * S, 0) : new Point(0, p * S), v ? new Point(p * S, 240 * S) : new Point(240 * S, p * S));
    }

    static Rect R(LayoutItem i) => new(i.X * S, i.Y * S, i.W * S, i.H * S);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var p = e.GetPosition(this);
        handle = null;
        if (ed.Selected is { Visible: true } sel)
        {
            var r = R(sel);
            foreach (var (hx, hy) in Handles)
            {
                var c = new Point(r.X + r.Width * (hx + 1) / 2, r.Y + r.Height * (hy + 1) / 2);
                if (Math.Abs(c.X - p.X) <= 8 && Math.Abs(c.Y - p.Y) <= 8) { handle = (hx, hy); break; }
            }
        }
        LayoutItem? hit = handle != null ? ed.Selected : ed.Items.LastOrDefault(i => i.Visible && R(i).Contains(p));
        if (hit == null) { ed.selected = null; ed.Changed(save: false); return; }
        if (ed.selected != hit.Id) { ed.selected = hit.Id; ed.Changed(save: false); }
        start = hit.Clone();
        startPos = p;
        ed.PushUndo();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (start == null)
        {
            Cursor = ed.Items.Any(i => i.Visible && R(i).Contains(p)) ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            return;
        }
        bool free = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        double dx = (p.X - startPos.X) / S, dy = (p.Y - startPos.Y) / S;
        var n = start.Clone();
        guides.Clear();
        if (handle is not { } h)
        {
            n.X = Math.Clamp(start.X + dx, 0, 240 - n.W);
            n.Y = Math.Clamp(start.Y + dy, 0, 240 - n.H);
            if (!free)
            {
                if (Best([n.X, n.X + n.W / 2, n.X + n.W], Targets(true)) is { } bx) { n.X += bx.d; guides.Add((true, bx.t)); }
                if (Best([n.Y, n.Y + n.H / 2, n.Y + n.H], Targets(false)) is { } by) { n.Y += by.d; guides.Add((false, by.t)); }
            }
            n.X = Math.Round(n.X); n.Y = Math.Round(n.Y);
        }
        else
        {
            double left = start.X, right = start.X + start.W, top = start.Y, bottom = start.Y + start.H;
            if (h.x < 0) left = Math.Min(right - LayoutItem.MinW, Math.Max(0, start.X + dx));
            if (h.x > 0) right = Math.Max(left + LayoutItem.MinW, Math.Min(240, right + dx));
            if (h.y < 0) top = Math.Min(bottom - LayoutItem.MinH, Math.Max(0, start.Y + dy));
            if (h.y > 0) bottom = Math.Max(top + LayoutItem.MinH, Math.Min(240, bottom + dy));
            if (!free)
            {
                if (h.x < 0) left = Snap(left, true); if (h.x > 0) right = Snap(right, true);
                if (h.y < 0) top = Snap(top, false); if (h.y > 0) bottom = Snap(bottom, false);
            }
            n.X = Math.Round(left); n.Y = Math.Round(top); n.W = Math.Round(right - left); n.H = Math.Round(bottom - top);
        }
        ed.Live(n);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (start == null) return;
        start = null;
        guides.Clear();
        e.Pointer.Capture(null);
        ed.EndDrag();
    }

    /// <summary>Kanten und Mitten der anderen Elemente und der Fläche.</summary>
    List<double> Targets(bool vertical)
    {
        var t = new List<double> { 0, 120, 240 };
        foreach (var i in ed.Items.Where(i => i.Visible && i.Id != ed.selected))
            t.AddRange(vertical ? [i.X, i.X + i.W / 2, i.X + i.W] : [i.Y, i.Y + i.H / 2, i.Y + i.H]);
        return t;
    }

    static (double d, double t)? Best(double[] edges, List<double> targets)
    {
        (double d, double t, double dist)? r = null;
        foreach (var e in edges)
            foreach (var t in targets)
                if (Math.Abs(t - e) <= 3 && (r == null || Math.Abs(t - e) < r.Value.dist)) r = (t - e, t, Math.Abs(t - e));
        return r is { } v ? (v.d, v.t) : null;
    }

    double Snap(double v, bool vertical)
    {
        var t = Targets(vertical).OrderBy(x => Math.Abs(x - v)).First();
        if (Math.Abs(t - v) > 3) return v;
        guides.Add((vertical, t));
        return t;
    }
}
