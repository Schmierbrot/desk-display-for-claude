import SwiftUI

enum LayoutEditorWindow {
    static let shared = HostedWindow(size: NSSize(width: 1060, height: 720)) { AnyView(LayoutEditorView()) }
    static func show() { shared.show() }
}

enum LayoutScreen: String, CaseIterable, Identifiable {
    case active = "Arbeitet", idle = "Bereit"
    var id: String { rawValue }
}

/// Editor für den Aufbau des Displays: Elemente ziehen, an den Griffen skalieren,
/// ein- und ausblenden, eigene Texte und Linien hinzufügen. Änderungen gehen sofort aufs Display.
struct LayoutEditorView: View {
    @Bindable private var settings = AppSettings.shared
    private var engine = Engine.shared
    @State private var screen: LayoutScreen = .active
    @State private var selected: String?
    @State private var undo: [DisplayLayout] = []
    @State private var redo: [DisplayLayout] = []
    @State private var dragStart: LayoutItem?
    @State private var guides: [Guide] = []
    @FocusState private var canvasFocused: Bool

    init(selected: String? = nil, screen: LayoutScreen = .active) {
        _selected = State(initialValue: selected)
        _screen = State(initialValue: screen)
    }

    static let scale: CGFloat = 2
    private let S = LayoutEditorView.scale

    struct Guide: Hashable { let vertical: Bool; let pos: Double }

    var body: some View {
        let t = settings.appTheme
        VStack(spacing: 0) {
            toolbar.padding(.horizontal, 24).padding(.top, 34).padding(.bottom, 14)
            HStack(alignment: .top, spacing: 24) {
                canvasArea
                sidePanel.frame(width: 330)
            }
            .padding(.horizontal, 24).padding(.bottom, 22)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(NeonBackground(theme: t))
        .tint(t.accent.color)
        .preferredColorScheme(.dark)
        .onChange(of: screen) { selected = nil }
    }

    // MARK: Werkzeugleiste

    private var toolbar: some View {
        HStack(spacing: 12) {
            VStack(alignment: .leading, spacing: 2) {
                Text("Display-Layout").font(DS.font(22, .bold))
                Text("Ziehen zum Verschieben · Griffe zum Skalieren · Pfeiltasten für Feinarbeit")
                    .font(DS.font(11.5)).foregroundStyle(.secondary)
            }
            Spacer()
            Picker("", selection: $screen) {
                ForEach(LayoutScreen.allCases) { Text($0.rawValue).tag($0) }
            }
            .pickerStyle(.segmented).labelsHidden().frame(width: 190)
            IconButton(systemName: "arrow.uturn.backward", help: "Rückgängig (⌘Z)") { undoStep() }
                .disabled(undo.isEmpty).opacity(undo.isEmpty ? 0.4 : 1)
                .keyboardShortcut("z", modifiers: .command)
            IconButton(systemName: "arrow.uturn.forward", help: "Wiederholen (⇧⌘Z)") { redoStep() }
                .disabled(redo.isEmpty).opacity(redo.isEmpty ? 0.4 : 1)
                .keyboardShortcut("z", modifiers: [.command, .shift])
            Menu {
                ForEach(LayoutPresets.all) { p in
                    Button(p.name) { commit { $0 = p.layout } ; selected = nil }
                }
                Divider()
                Button("Nur diese Ansicht zurücksetzen") {
                    commit { l in
                        if screen == .active { l.active = LayoutPresets.standard.active } else { l.idle = LayoutPresets.standard.idle }
                    }
                    selected = nil
                }
            } label: {
                Label("Vorlage", systemImage: "square.grid.2x2")
            }
            .menuStyle(.borderlessButton).fixedSize()
            .padding(.horizontal, 12).padding(.vertical, 7)
            .background(Capsule().fill(.white.opacity(0.08)))
        }
    }

    // MARK: Zeichenfläche

    private var items: [LayoutItem] { screen == .active ? settings.displayLayout.active : settings.displayLayout.idle }

    private var previewImage: CGImage {
        let use = engine.usage ?? Sample.usage
        return Renderer.render(session: screen == .active ? (engine.session ?? Sample.session) : nil,
                               usage: use, tick: Int(Date().timeIntervalSince1970),
                               layout: settings.displayLayout).image
    }

    private var canvasArea: some View {
        let size = 240 * S
        return VStack(spacing: 12) {
            TimelineView(.periodic(from: .now, by: 1)) { _ in
                ZStack(alignment: .topLeading) {
                    Image(decorative: previewImage, scale: 1).interpolation(.high).resizable()
                        .frame(width: size, height: size)
                    // Klick ins Leere hebt die Auswahl auf
                    Color.clear.contentShape(Rectangle())
                        .onTapGesture { selected = nil; canvasFocused = true }
                    ForEach(items.filter(\.visible)) { item in
                        itemOverlay(item)
                    }
                    // Auswahl mit Griffen immer obenauf – auch wenn andere Elemente überlappen
                    if let item = selectedItem, item.visible { selectionOverlay(item) }
                    ForEach(guides, id: \.self) { g in
                        Rectangle().fill(settings.appTheme.accent2.color.opacity(0.9))
                            .frame(width: g.vertical ? 1 : size, height: g.vertical ? size : 1)
                            .offset(x: g.vertical ? g.pos * S : 0, y: g.vertical ? 0 : g.pos * S)
                            .allowsHitTesting(false)
                    }
                }
                .frame(width: size, height: size)
                .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
            }
            .padding(18)
            .background(
                RoundedRectangle(cornerRadius: 26, style: .continuous)
                    .fill(LinearGradient(colors: [Color(white: 0.16), Color(white: 0.05)], startPoint: .top, endPoint: .bottom))
                    .overlay(RoundedRectangle(cornerRadius: 26, style: .continuous)
                        .strokeBorder(settings.appTheme.gradient.opacity(0.6), lineWidth: 1.2))
                    .shadow(color: settings.appTheme.accent.color.opacity(0.3), radius: 24, y: 6)
            )
            .focusable()
            .focusEffectDisabled()
            .focused($canvasFocused)
            .onKeyPress(phases: .down) { press in handleKey(press) }
            Text(engine.status == .shown ? "Änderungen erscheinen sofort auf deinem Display."
                                         : "Die Vorschau zeigt Beispieldaten – aufs Display geht es, sobald es verbunden ist.")
                .font(DS.font(11.5)).foregroundStyle(.secondary)
        }
    }

    private func itemOverlay(_ item: LayoutItem) -> some View {
        Rectangle()
            .fill(Color.white.opacity(0.001))
            .overlay(Rectangle().strokeBorder(Color.white.opacity(selected == item.id ? 0 : 0.22),
                                              style: StrokeStyle(lineWidth: 1, dash: [4, 3])))
            .frame(width: item.w * S, height: item.h * S)
            .contentShape(Rectangle())
            .gesture(moveGesture(item))
            .help(item.kind.label)
            .offset(x: item.x * S, y: item.y * S)
    }

    private func selectionOverlay(_ item: LayoutItem) -> some View {
        let accent = settings.appTheme.accent.color
        return ZStack(alignment: .topLeading) {
            Rectangle()
                .fill(accent.opacity(0.10))
                .overlay(Rectangle().strokeBorder(accent, lineWidth: 1.5))
                .frame(width: item.w * S, height: item.h * S)
                .contentShape(Rectangle())
                .gesture(moveGesture(item))
            ForEach(handles, id: \.self) { h in
                Circle().fill(.white)
                    .overlay(Circle().strokeBorder(accent, lineWidth: 2))
                    .frame(width: 11, height: 11)
                    .frame(width: 18, height: 18)          // größere Trefferfläche
                    .contentShape(Rectangle())
                    .offset(x: (item.w * S) * (h.x + 1) / 2 - 9, y: (item.h * S) * (h.y + 1) / 2 - 9)
                    .gesture(resizeGesture(item, h))
            }
            Text("\(Int(item.x)), \(Int(item.y)) · \(Int(item.w)) × \(Int(item.h))")
                .font(.system(size: 10, weight: .semibold, design: .rounded)).monospacedDigit()
                .padding(.horizontal, 6).padding(.vertical, 2)
                .background(Capsule().fill(accent))
                .offset(y: item.y * S > 22 ? -20 : item.h * S + 4)
                .fixedSize()
                .allowsHitTesting(false)
        }
        .offset(x: item.x * S, y: item.y * S)
    }

    private struct Handle: Hashable { let x: Double; let y: Double }
    private let handles: [Handle] = [
        Handle(x: -1, y: -1), Handle(x: 0, y: -1), Handle(x: 1, y: -1),
        Handle(x: -1, y: 0), Handle(x: 1, y: 0),
        Handle(x: -1, y: 1), Handle(x: 0, y: 1), Handle(x: 1, y: 1),
    ]

    private func moveGesture(_ item: LayoutItem) -> some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .global)
            .onChanged { v in
                if dragStart == nil { dragStart = item; selected = item.id; canvasFocused = true; pushUndo() }
                guard let s = dragStart else { return }
                var n = s
                n.x = s.x + v.translation.width / S
                n.y = s.y + v.translation.height / S
                (n.x, n.y, guides) = snapMove(n)
                update(n)
            }
            .onEnded { _ in dragStart = nil; guides = []; dropEmptyUndo() }
    }

    private func resizeGesture(_ item: LayoutItem, _ h: Handle) -> some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .global)
            .onChanged { v in
                if dragStart == nil { dragStart = item; pushUndo() }
                guard let s = dragStart else { return }
                let dx = v.translation.width / S, dy = v.translation.height / S
                var n = s
                var left = s.x, right = s.x + s.w, top = s.y, bottom = s.y + s.h
                if h.x < 0 { left = min(right - LayoutItem.minW, max(0, s.x + dx)) }
                if h.x > 0 { right = max(left + LayoutItem.minW, min(240, s.x + s.w + dx)) }
                if h.y < 0 { top = min(bottom - LayoutItem.minH, max(0, s.y + dy)) }
                if h.y > 0 { bottom = max(top + LayoutItem.minH, min(240, s.y + s.h + dy)) }
                var g: [Guide] = []
                if h.x < 0 { (left, g) = snapEdge(left, vertical: true, g) }
                if h.x > 0 { (right, g) = snapEdge(right, vertical: true, g) }
                if h.y < 0 { (top, g) = snapEdge(top, vertical: false, g) }
                if h.y > 0 { (bottom, g) = snapEdge(bottom, vertical: false, g) }
                n.x = left.rounded(); n.y = top.rounded()
                n.w = (right - left).rounded(); n.h = (bottom - top).rounded()
                guides = g
                update(n, clamp: false)
            }
            .onEnded { _ in dragStart = nil; guides = []; dropEmptyUndo() }
    }

    // MARK: Einrasten

    /// Kanten und Mitten der anderen Elemente und der Fläche
    private func targets(vertical: Bool) -> [Double] {
        var t: [Double] = [0, 120, 240]
        for i in items where i.visible && i.id != selected {
            t += vertical ? [i.x, i.x + i.w / 2, i.x + i.w] : [i.y, i.y + i.h / 2, i.y + i.h]
        }
        return t
    }

    private func snapEdge(_ v: Double, vertical: Bool, _ g: [Guide]) -> (Double, [Guide]) {
        if NSEvent.modifierFlags.contains(.option) { return (v, g) }      // ⌥ = frei
        if let t = targets(vertical: vertical).min(by: { abs($0 - v) < abs($1 - v) }), abs(t - v) <= 3 {
            return (t, g + [Guide(vertical: vertical, pos: t)])
        }
        return (v, g)
    }

    private func snapMove(_ n: LayoutItem) -> (Double, Double, [Guide]) {
        var x = min(240 - n.w, max(0, n.x)), y = min(240 - n.h, max(0, n.y))
        var g: [Guide] = []
        if !NSEvent.modifierFlags.contains(.option) {
            func best(_ edges: [Double], _ targets: [Double]) -> (Double, Double)? {
                var r: (Double, Double, Double)?
                for e in edges {
                    for t in targets where abs(t - e) <= 3 && (r == nil || abs(t - e) < r!.2) { r = (t - e, t, abs(t - e)) }
                }
                return r.map { ($0.0, $0.1) }
            }
            if let (d, t) = best([x, x + n.w / 2, x + n.w], targets(vertical: true)) { x += d; g.append(Guide(vertical: true, pos: t)) }
            if let (d, t) = best([y, y + n.h / 2, y + n.h], targets(vertical: false)) { y += d; g.append(Guide(vertical: false, pos: t)) }
        }
        return (x.rounded(), y.rounded(), g)
    }

    // MARK: Seitenleiste

    private var selectedItem: LayoutItem? { items.first { $0.id == selected } }

    private var sidePanel: some View {
        ScrollView { panelContent }.scrollIndicators(.never)
    }

    /// Inhalt der Seitenleiste (auch für Schnappschüsse ohne ScrollView)
    var panelContent: some View {
            VStack(alignment: .leading, spacing: 18) {
                Card(title: "Elemente") {
                    VStack(spacing: 2) {
                        ForEach(items) { item in elementRow(item) }
                    }
                    HStack {
                        Button { addCustom(.label) } label: { Label("Text", systemImage: "plus") }
                            .buttonStyle(NeonButtonStyle(prominent: false))
                        Button { addCustom(.line) } label: { Label("Linie", systemImage: "plus") }
                            .buttonStyle(NeonButtonStyle(prominent: false))
                    }
                }
                if let item = selectedItem { inspector(item) }
            }
            .padding(.bottom, 10)
    }

    private func elementRow(_ item: LayoutItem) -> some View {
        let isSel = selected == item.id
        return HStack(spacing: 10) {
            Image(systemName: item.kind.icon).frame(width: 18).foregroundStyle(isSel ? settings.appTheme.accent.color : .secondary)
            Text(item.kind == .label ? (item.text?.isEmpty == false ? "„\(item.text!)“" : "Eigener Text") : item.kind.label)
                .font(DS.font(12.5, isSel ? .semibold : .regular))
                .foregroundStyle(item.visible ? .primary : .secondary)
                .lineLimit(1)
            Spacer()
            Button {
                commit { l in mutate(&l) { if let i = $0.firstIndex(where: { $0.id == item.id }) { $0[i].visible.toggle() } } }
                if item.visible && selected == item.id { selected = nil }
            } label: {
                Image(systemName: item.visible ? "eye" : "eye.slash").frame(width: 22)
                    .foregroundStyle(item.visible ? .primary : .secondary)
            }
            .buttonStyle(.plain).help(item.visible ? "Ausblenden" : "Einblenden")
        }
        .padding(.horizontal, 8).padding(.vertical, 6)
        .background(RoundedRectangle(cornerRadius: 8).fill(isSel ? settings.appTheme.accent.color.opacity(0.18) : .clear))
        .contentShape(Rectangle())
        .onTapGesture {
            if !item.visible { commit { l in mutate(&l) { if let i = $0.firstIndex(where: { $0.id == item.id }) { $0[i].visible = true } } } }
            selected = item.id
            canvasFocused = true
        }
    }

    private func inspector(_ item: LayoutItem) -> some View {
        Card(title: item.kind.label) {
            HStack(spacing: 10) {
                numberField("X", item.x) { v in edit { $0.x = v } }
                numberField("Y", item.y) { v in edit { $0.y = v } }
            }
            HStack(spacing: 10) {
                numberField("Breite", item.w) { v in edit { $0.w = v } }
                numberField("Höhe", item.h) { v in edit { $0.h = v } }
            }
            if item.kind == .label {
                TextField("Text", text: Binding(get: { selectedItem?.text ?? "" },
                                                set: { v in edit(undoable: false) { $0.text = v } }))
                    .textFieldStyle(.roundedBorder)
            }
            if item.kind.alignable {
                Picker("Ausrichtung", selection: Binding(get: { selectedItem?.align ?? .left },
                                                         set: { v in edit { $0.align = v } })) {
                    Image(systemName: "text.alignleft").tag(TextAlign.left)
                    Image(systemName: "text.aligncenter").tag(TextAlign.center)
                    Image(systemName: "text.alignright").tag(TextAlign.right)
                }
                .pickerStyle(.segmented)
            }
            if item.isCustom {
                Picker("Farbe", selection: Binding(get: { selectedItem?.color ?? (item.kind == .line ? .edge : .text) },
                                                   set: { v in edit { $0.color = v } })) {
                    ForEach(ColorRole.allCases, id: \.self) { r in Text(r.label).tag(r) }
                }
            }
            HStack {
                IconButton(systemName: "square.3.layers.3d.top.filled", help: "Nach vorne") { reorder(front: true) }
                IconButton(systemName: "square.3.layers.3d.bottom.filled", help: "Nach hinten") { reorder(front: false) }
                IconButton(systemName: "arrow.left.and.right", help: "Waagerecht zentrieren") { edit { $0.x = (240 - $0.w) / 2 } }
                Spacer()
                if item.isCustom {
                    Button("Entfernen", role: .destructive) { removeSelected() }.buttonStyle(NeonButtonStyle(prominent: false))
                } else {
                    Button("Ausblenden") {
                        edit { $0.visible = false }; selected = nil
                    }
                    .buttonStyle(NeonButtonStyle(prominent: false))
                }
            }
            Text(hint(item.kind)).font(DS.font(11)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
    }

    private func numberField(_ label: String, _ value: Double, _ set: @escaping (Double) -> Void) -> some View {
        VStack(alignment: .leading, spacing: 3) {
            Text(label).font(DS.font(10.5, .semibold)).foregroundStyle(.secondary)
            HStack(spacing: 4) {
                TextField("", value: Binding(get: { value }, set: { set($0) }), format: .number)
                    .textFieldStyle(.roundedBorder).monospacedDigit()
                Stepper("", value: Binding(get: { value }, set: { set($0) }), in: 0...240).labelsHidden()
            }
        }
    }

    private func hint(_ k: WidgetKind) -> String {
        switch k {
        case .ring: return "Der Ring passt sich der kürzeren Seite an."
        case .name, .step: return "Die Höhe bestimmt die Schriftgröße, die Breite den Zeilenumbruch."
        case .limit5, .limitWeek: return "Die Höhe bestimmt die Größe von Zahl, Text und Balken."
        case .line: return "Waagerecht oder senkrecht – je nachdem, ob Breite oder Höhe größer ist."
        default: return "Die Höhe bestimmt die Schriftgröße. Wird der Text zu breit, schrumpft er."
        }
    }

    // MARK: Ändern

    private func mutate(_ l: inout DisplayLayout, _ f: (inout [LayoutItem]) -> Void) {
        if screen == .active { f(&l.active) } else { f(&l.idle) }
    }

    /// Änderung mit Rückgängig-Schritt
    private func commit(_ f: (inout DisplayLayout) -> Void) {
        pushUndo()
        var l = settings.displayLayout
        f(&l)
        settings.displayLayout = l.normalized()
    }

    private func edit(undoable: Bool = true, _ f: (inout LayoutItem) -> Void) {
        guard let id = selected else { return }
        if undoable { pushUndo() }
        var l = settings.displayLayout
        mutate(&l) { items in
            if let i = items.firstIndex(where: { $0.id == id }) { f(&items[i]); items[i].clamp() }
        }
        settings.displayLayout = l
    }

    private func update(_ n: LayoutItem, clamp: Bool = true) {
        var l = settings.displayLayout
        var n = n
        if clamp { n.clamp() }
        mutate(&l) { items in if let i = items.firstIndex(where: { $0.id == n.id }) { items[i] = n } }
        if l != settings.displayLayout { settings.displayLayout = l }
    }

    private func pushUndo() {
        undo.append(settings.displayLayout)
        if undo.count > 80 { undo.removeFirst() }
        redo = []
    }

    /// Ein Klick ohne Bewegung soll keinen leeren Rückgängig-Schritt hinterlassen.
    private func dropEmptyUndo() {
        if let last = undo.last, last == settings.displayLayout { undo.removeLast() }
    }

    private func undoStep() {
        guard let prev = undo.popLast() else { return }
        redo.append(settings.displayLayout)
        settings.displayLayout = prev
    }

    private func redoStep() {
        guard let next = redo.popLast() else { return }
        undo.append(settings.displayLayout)
        settings.displayLayout = next
    }

    private func addCustom(_ kind: WidgetKind) {
        let id = kind.rawValue + "-" + UUID().uuidString.prefix(8)
        let item = kind == .label
            ? LayoutItem(.label, 40, 100, 160, 22, align: .center, id: id, text: "Hallo", color: .text)
            : LayoutItem(.line, 20, 120, 200, 2, id: id, color: .edge)
        commit { l in mutate(&l) { $0.append(item) } }
        selected = id
        canvasFocused = true
    }

    private func removeSelected() {
        guard let id = selected, selectedItem?.isCustom == true else { return }
        commit { l in mutate(&l) { $0.removeAll { $0.id == id } } }
        selected = nil
    }

    private func reorder(front: Bool) {
        guard let id = selected else { return }
        commit { l in
            mutate(&l) { items in
                guard let i = items.firstIndex(where: { $0.id == id }) else { return }
                let it = items.remove(at: i)
                if front { items.append(it) } else { items.insert(it, at: 0) }
            }
        }
    }

    private func handleKey(_ press: KeyPress) -> KeyPress.Result {
        guard selected != nil else { return .ignored }
        let step: Double = press.modifiers.contains(.shift) ? 10 : 1
        let resize = press.modifiers.contains(.option)
        switch press.key {
        case .leftArrow: edit { if resize { $0.w -= step } else { $0.x -= step } }
        case .rightArrow: edit { if resize { $0.w += step } else { $0.x += step } }
        case .upArrow: edit { if resize { $0.h -= step } else { $0.y -= step } }
        case .downArrow: edit { if resize { $0.h += step } else { $0.y += step } }
        case .delete, .deleteForward:
            if selectedItem?.isCustom == true { removeSelected() } else { edit { $0.visible = false }; selected = nil }
        case .escape: selected = nil
        default: return .ignored
        }
        return .handled
    }
}
