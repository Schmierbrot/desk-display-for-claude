import AppKit

// MARK: - Modell

/// Bausteine des Displays. Jeder Baustein zeichnet sich in seinen Rahmen und passt
/// Schrift und Größe an die Höhe des Rahmens an.
enum WidgetKind: String, Codable, CaseIterable {
    case header, clock, ring, status, name, timer, step, limit5, limitWeek   // Ansicht „Arbeitet“
    case ready, bigClock, date, calm                                         // Ansicht „Bereit“
    case context, model, branch, activity                                    // Details zur Sitzung
    case label, line                                                         // eigene Bausteine

    var label: String {
        switch self {
        case .header: return "Logo und Titel"
        case .clock: return "Uhrzeit klein"
        case .ring: return "Fortschrittsring"
        case .status: return "„Arbeitet“"
        case .name: return "Sitzungsname"
        case .timer: return "Laufzeit"
        case .step: return "Aktueller Schritt"
        case .limit5: return "Limit 5 Stunden"
        case .limitWeek: return "Limit Woche"
        case .ready: return "„bereit“"
        case .bigClock: return "Uhrzeit groß"
        case .date: return "Datum"
        case .calm: return "„Alles ruhig“"
        case .context: return "Kontext-Füllstand"
        case .model: return "Modell"
        case .branch: return "Git-Branch"
        case .activity: return "Tut gerade"
        case .label: return "Eigener Text"
        case .line: return "Linie"
        }
    }

    var icon: String {
        switch self {
        case .header: return "sparkle"
        case .clock, .bigClock: return "clock"
        case .ring: return "circle.dashed"
        case .status, .ready: return "dot.radiowaves.left.and.right"
        case .name: return "textformat"
        case .timer: return "stopwatch"
        case .step: return "list.bullet.rectangle"
        case .limit5, .limitWeek: return "gauge.with.dots.needle.50percent"
        case .date: return "calendar"
        case .calm: return "capsule"
        case .context: return "chart.bar.fill"
        case .model: return "cpu"
        case .branch: return "arrow.triangle.branch"
        case .activity: return "hammer"
        case .label: return "character.cursor.ibeam"
        case .line: return "line.horizontal.3"
        }
    }

    /// Bausteine mit wählbarer Ausrichtung
    var alignable: Bool { [.clock, .name, .timer, .ready, .bigClock, .date, .label, .model, .branch, .activity].contains(self) }
}

enum TextAlign: String, Codable, CaseIterable { case left, center, right }

/// Farbrolle aus dem Display-Theme (für eigene Texte und Linien)
enum ColorRole: String, Codable, CaseIterable {
    case text, muted, dim, accent, accent2, good, edge
    var label: String {
        switch self {
        case .text: return "Text"
        case .muted: return "Gedämpft"
        case .dim: return "Schwach"
        case .accent: return "Akzent"
        case .accent2: return "Akzent 2"
        case .good: return "Grün"
        case .edge: return "Rahmen"
        }
    }
    func rgb(_ t: DisplayTheme) -> RGB {
        switch self {
        case .text: return t.text
        case .muted: return t.muted
        case .dim: return t.dim
        case .accent: return t.accent
        case .accent2: return t.accent2
        case .good: return t.good
        case .edge: return t.edge
        }
    }
}

struct LayoutItem: Codable, Equatable, Identifiable {
    var id: String
    var kind: WidgetKind
    var x: Double, y: Double, w: Double, h: Double
    var visible = true
    var align: TextAlign?
    var text: String?            // nur „Eigener Text“
    var color: ColorRole?        // nur „Eigener Text“ und „Linie“

    init(_ kind: WidgetKind, _ x: Double, _ y: Double, _ w: Double, _ h: Double,
         visible: Bool = true, align: TextAlign? = nil, id: String? = nil, text: String? = nil, color: ColorRole? = nil) {
        self.id = id ?? kind.rawValue
        self.kind = kind; self.x = x; self.y = y; self.w = w; self.h = h
        self.visible = visible; self.align = align; self.text = text; self.color = color
    }

    var rect: CGRect { CGRect(x: x, y: y, width: w, height: h) }
    var isCustom: Bool { kind == .label || kind == .line }

    static let minW = 8.0, minH = 2.0
    /// Innerhalb der 240 × 240 Pixel halten
    mutating func clamp() {
        w = min(240, max(Self.minW, w.rounded())); h = min(240, max(Self.minH, h.rounded()))
        x = min(240 - w, max(0, x.rounded())); y = min(240 - h, max(0, y.rounded()))
    }
}

/// Aufbau beider Ansichten
struct DisplayLayout: Codable, Equatable {
    var active: [LayoutItem]
    var idle: [LayoutItem]

    static let activeKinds: [WidgetKind] = [.header, .clock, .ring, .status, .name, .timer, .step, .limit5, .limitWeek,
                                            .context, .model, .branch, .activity]
    static let idleKinds: [WidgetKind] = [.header, .ready, .bigClock, .date, .calm, .limit5, .limitWeek]

    /// Ergänzt fehlende Bausteine (z. B. nach einem Update) und entfernt doppelte.
    func normalized() -> DisplayLayout {
        func fix(_ items: [LayoutItem], _ kinds: [WidgetKind], _ defaults: [LayoutItem]) -> [LayoutItem] {
            var seen = Set<String>()
            var out = items.filter { (kinds.contains($0.kind) || $0.isCustom) && seen.insert($0.id).inserted }
            for d in defaults where !out.contains(where: { $0.id == d.id }) {
                var n = d; n.visible = false; out.append(n)
            }
            return out.map { var i = $0; i.clamp(); return i }
        }
        let s = LayoutPresets.standard
        return DisplayLayout(active: fix(active, Self.activeKinds, s.active), idle: fix(idle, Self.idleKinds, s.idle))
    }
}

struct LayoutPreset: Identifiable {
    let id: String
    let name: String
    let layout: DisplayLayout
}

enum LayoutPresets {
    static let standard = DisplayLayout(
        active: [
            LayoutItem(.header, 6, 6, 166, 24),
            LayoutItem(.clock, 172, 6, 58, 24, align: .right),
            LayoutItem(.ring, 12, 44, 80, 80),
            LayoutItem(.status, 104, 44, 126, 14),
            LayoutItem(.name, 104, 60, 126, 42, align: .left),
            LayoutItem(.timer, 104, 104, 126, 20, align: .left),
            LayoutItem(.step, 8, 134, 224, 44),
            LayoutItem(.limit5, 10, 186, 104, 48),
            LayoutItem(.limitWeek, 126, 186, 104, 48),
        ],
        idle: [
            LayoutItem(.header, 6, 6, 160, 24),
            LayoutItem(.ready, 166, 6, 64, 24, align: .right),
            LayoutItem(.bigClock, 10, 40, 220, 72, align: .center),
            LayoutItem(.date, 8, 116, 224, 24, align: .center),
            LayoutItem(.calm, 57, 146, 126, 30),
            LayoutItem(.limit5, 10, 186, 104, 48),
            LayoutItem(.limitWeek, 126, 186, 104, 48),
        ])

    static let focus = DisplayLayout(
        active: [
            LayoutItem(.header, 6, 6, 166, 24),
            LayoutItem(.clock, 172, 6, 58, 24, align: .right),
            LayoutItem(.ring, 10, 38, 96, 96),
            LayoutItem(.status, 116, 44, 116, 14),
            LayoutItem(.name, 116, 62, 116, 42, align: .left),
            LayoutItem(.timer, 116, 108, 116, 22, align: .left),
            LayoutItem(.step, 8, 140, 224, 50),
            LayoutItem(.limit5, 10, 196, 104, 40),
            LayoutItem(.limitWeek, 126, 196, 104, 40),
        ],
        idle: [
            LayoutItem(.header, 6, 6, 160, 24),
            LayoutItem(.ready, 166, 6, 64, 24, align: .right),
            LayoutItem(.bigClock, 4, 40, 232, 92, align: .center),
            LayoutItem(.date, 8, 136, 224, 28, align: .center),
            LayoutItem(.calm, 57, 146, 126, 30, visible: false),
            LayoutItem(.limit5, 10, 186, 104, 48),
            LayoutItem(.limitWeek, 126, 186, 104, 48),
        ])

    static let bigLimits = DisplayLayout(
        active: [
            LayoutItem(.header, 6, 6, 166, 24),
            LayoutItem(.clock, 172, 6, 58, 24, align: .right),
            LayoutItem(.ring, 12, 44, 80, 80, visible: false),
            LayoutItem(.status, 12, 38, 110, 14),
            LayoutItem(.name, 12, 56, 216, 30, align: .left),
            LayoutItem(.timer, 150, 36, 80, 18, align: .right),
            LayoutItem(.step, 8, 94, 224, 36),
            LayoutItem(.limit5, 12, 134, 216, 50),
            LayoutItem(.limitWeek, 12, 186, 216, 50),
        ],
        idle: [
            LayoutItem(.header, 6, 6, 160, 24),
            LayoutItem(.ready, 166, 6, 64, 24, align: .right),
            LayoutItem(.bigClock, 10, 34, 220, 64, align: .center),
            LayoutItem(.date, 8, 98, 224, 22, align: .center),
            LayoutItem(.calm, 57, 146, 126, 30, visible: false),
            LayoutItem(.limit5, 12, 136, 216, 44),
            LayoutItem(.limitWeek, 12, 188, 216, 44),
        ])

    static let big = DisplayLayout(
        active: [
            LayoutItem(.header, 6, 6, 166, 24),
            LayoutItem(.clock, 172, 6, 58, 24, align: .right),
            LayoutItem(.ring, 12, 44, 80, 80, visible: false),
            LayoutItem(.status, 10, 40, 140, 16),
            LayoutItem(.name, 10, 60, 220, 26, align: .left),
            LayoutItem(.timer, 150, 38, 80, 20, align: .right),
            LayoutItem(.step, 6, 90, 228, 94),
            LayoutItem(.limit5, 10, 190, 104, 46),
            LayoutItem(.limitWeek, 126, 190, 104, 46),
        ],
        idle: [
            LayoutItem(.header, 6, 6, 160, 24),
            LayoutItem(.ready, 166, 6, 64, 24, align: .right),
            LayoutItem(.bigClock, 2, 34, 236, 104, align: .center),
            LayoutItem(.date, 8, 140, 224, 30, align: .center),
            LayoutItem(.calm, 57, 146, 126, 30, visible: false),
            LayoutItem(.limit5, 10, 190, 104, 46),
            LayoutItem(.limitWeek, 126, 190, 104, 46),
        ])

    static let details = DisplayLayout(
        active: [
            LayoutItem(.header, 6, 6, 166, 24),
            LayoutItem(.clock, 172, 6, 58, 24, align: .right),
            LayoutItem(.ring, 10, 38, 70, 70),
            LayoutItem(.status, 88, 38, 144, 14),
            LayoutItem(.name, 88, 56, 144, 26, align: .left),
            LayoutItem(.activity, 88, 86, 144, 16, align: .left),
            LayoutItem(.timer, 104, 104, 126, 20, visible: false, align: .left),
            LayoutItem(.step, 8, 114, 224, 38),
            LayoutItem(.context, 10, 158, 104, 34),
            LayoutItem(.model, 126, 160, 104, 15, align: .left),
            LayoutItem(.branch, 126, 178, 104, 15, align: .left),
            LayoutItem(.limit5, 10, 198, 104, 36),
            LayoutItem(.limitWeek, 126, 198, 104, 36),
        ],
        idle: standard.idle)

    static let all: [LayoutPreset] = [
        LayoutPreset(id: "standard", name: "Standard", layout: standard),
        LayoutPreset(id: "focus", name: "Fokus", layout: focus),
        LayoutPreset(id: "limits", name: "Limits groß", layout: bigLimits),
        LayoutPreset(id: "big", name: "Groß", layout: big),
        LayoutPreset(id: "details", name: "Details", layout: details),
    ]
}

// MARK: - Zeichnen

/// Was die Bausteine zeigen
struct WidgetData {
    var session: SessionInfo?
    var usage: Usage?
    var tick: Int
    var now: Date
}

enum Widgets {
    static func draw(_ c: Canvas, _ item: LayoutItem, _ d: WidgetData) {
        let r = item.rect
        switch item.kind {
        case .header: header(c, r, active: d.session != nil, tick: d.tick)
        case .clock: line(c, r, Renderer.clock(d.now), 0.667, 560, Palette.muted, item.align ?? .right)
        case .ring: if let s = d.session { ring(c, r, s, d.tick) }
        case .status: if let s = d.session { status(c, r, s, d.tick) }
        case .context: if let s = d.session, let p = s.contextPct { meter(c, r, "KONTEXT", p, s.waiting ? Palette.warn : Palette.accent, d.tick) }
        case .model: if let m = d.session?.model { line(c, r, Self.modelName(m), 0.8, 600, Palette.muted, item.align ?? .left) }
        case .branch: if let b = d.session?.branch { branch(c, r, b, item.align ?? .left) }
        case .activity: if let s = d.session, let a = s.activity { line(c, r, a, 0.8, 560, s.waiting ? Palette.warn : Palette.muted, item.align ?? .left) }
        case .name: if let s = d.session { name(c, r, s.name, item.align ?? .left) }
        case .timer: if let s = d.session { line(c, r, Renderer.fmtTimer(s.secs), 0.75, 560, Palette.muted, item.align ?? .left) }
        case .step: if let s = d.session { step(c, r, s) }
        case .limit5: limit(c, r, "5 STD", d.usage.map { ($0.five, $0.stale) }, d.now, d.tick)
        case .limitWeek: limit(c, r, "WOCHE", d.usage.map { ($0.week, $0.stale) }, d.now, d.tick)
        case .ready: ready(c, r, item.align ?? .right)
        case .bigClock: line(c, r, Renderer.clock(d.now), 0.917, 330, Palette.text, item.align ?? .center)
        case .date: line(c, r, Renderer.dateText(d.now), 0.79, 600, Palette.muted, item.align ?? .center)
        case .calm: calm(c, r)
        case .label:
            let col = (item.color ?? .text).rgb(Palette.theme).ns
            line(c, r, item.text ?? "", 0.75, 620, col, item.align ?? .left)
        case .line:
            let col = (item.color ?? .edge).rgb(Palette.theme).ns
            c.rrect(r.minX, r.minY, r.maxX, r.maxY, min(r.width, r.height) / 2, fill: col)
        }
    }

    /// Eine Zeile Text, Schriftgröße = Anteil der Rahmenhöhe, schrumpft bei Bedarf auf die Breite.
    static func line(_ c: Canvas, _ r: CGRect, _ s: String, _ k: CGFloat, _ weight: CGFloat, _ col: NSColor, _ a: TextAlign) {
        guard !s.isEmpty else { return }
        var size = r.height * k
        var f = roundedFont(size, weight)
        while size > 6 && c.tlen(s, f) > r.width { size -= 0.5; f = roundedFont(size, weight) }
        let text = c.fit(s, f, r.width)
        let (x, h) = anchor(r, a)
        c.text(x, r.midY, text, f, col, h: h, v: .middle)
    }

    static func anchor(_ r: CGRect, _ a: TextAlign) -> (CGFloat, Canvas.HAnchor) {
        switch a {
        case .left: return (r.minX, .left)
        case .center: return (r.midX, .center)
        case .right: return (r.maxX, .right)
        }
    }

    static func header(_ c: Canvas, _ r: CGRect, active: Bool, tick: Int) {
        let t = Palette.theme, h = r.height
        c.logo(t.logo, r.minX + h * 0.458, r.midY, scale: h / 24, active: active, tick: tick)
        let x = t.logo == .none ? r.minX + 4 : r.minX + h * 1.125
        let f = roundedFont(h * 0.667, 650)
        c.text(x, r.midY, c.fit(t.title, f, r.maxX - x), f, Palette.text, v: .middle)
    }

    static func ring(_ c: Canvas, _ r: CGRect, _ s: SessionInfo, _ tick: Int) {
        let rad = min(r.width, r.height) / 2, w = rad * 0.2
        let cx = r.midX, cy = r.midY
        c.arc(cx, cy, rad, w, 0, 360, Palette.track, caps: false)
        // Wartet Claude auf dich, leuchtet der Ring in der Warnfarbe
        let a1 = s.waiting ? Palette.theme.warn : Palette.theme.accent
        let a2 = s.waiting ? Palette.theme.warn.mix(Palette.theme.bad, 0.4) : Palette.theme.accent2
        if !s.plan.isEmpty {
            let frac = CGFloat(s.done) / CGFloat(s.plan.count)
            if frac > 0 {
                c.gradientArc(cx, cy, rad, w, 0, min(359.4, 360 * frac), a1, a2)
            } else if s.waiting {
                c.arc(cx, cy, rad, w, 0, 360, Palette.warn.withAlphaComponent(0.35), caps: false)
            }
            c.text(cx, cy - rad * 0.1, "\(s.done)", roundedFont(rad * 0.75, 700), Palette.text, h: .center, v: .middle)
            c.text(cx, cy + rad * 0.475, "von \(s.plan.count)", roundedFont(rad * 0.275, 600), Palette.muted, h: .center, v: .middle)
        } else {
            let start = CGFloat((tick * 40) % 360)
            if s.waiting { c.arc(cx, cy, rad, w, 0, 360, Palette.warn, caps: false) }
            else { c.gradientArc(cx, cy, rad, w, start, start + 80, a1, a2) }
            c.text(cx, cy, s.waiting ? "?" : "···", roundedFont(rad * 0.55, 700), s.waiting ? Palette.warn : Palette.muted, h: .center, v: .middle)
        }
    }

    static func status(_ c: Canvas, _ r: CGRect, _ s: SessionInfo, _ tick: Int) {
        let k = r.height / 14
        let col = s.waiting ? Palette.warn : Palette.good
        let glow = (s.waiting ? Palette.theme.warn : Palette.theme.good).mix(RGB(0, 0, 0), 0.75).ns
        c.circle(r.minX + 4 * k, r.midY, (tick % 2 == 0 ? 5.5 : 4.2) * k, glow)
        c.circle(r.minX + 4 * k, r.midY, 3.4 * k, col)
        let f = roundedFont(10 * k, 700)
        // „1/2“ beim Durchwechseln, sonst „+1“ für weitere arbeitende Sitzungen
        let more = s.total > 1 ? "  \(s.index)/\(s.total)" : s.busyCount > 1 ? "  +\(s.busyCount - 1)" : ""
        let text = (s.waiting ? "WARTET AUF DICH" : "ARBEITET") + more
        let maxw = r.width - 13 * k
        var size = 10 * k, ff = f
        while size > 5, c.tlen(text, ff) + CGFloat(text.count) * 1.2 * k > maxw { size -= 0.5; ff = roundedFont(size, 700) }
        c.spaced(r.minX + 13 * k, r.midY - (ff.ascender - ff.descender) / 2, text, ff, col, 1.2 * k * size / (10 * k))
    }

    /// Modell-ID lesbar machen: „claude-opus-5-5“ → „Opus 5.5“
    static func modelName(_ id: String) -> String {
        var p = id.split(separator: "-").map(String.init)
        if p.first == "claude" { p.removeFirst() }
        p = p.filter { !($0.count == 8 && Int($0) != nil) && !$0.hasPrefix("[") }     // Datumsangaben, [1m]
        guard let fam = p.first else { return id }
        let nums = p.dropFirst().filter { Int($0) != nil }
        return fam.prefix(1).uppercased() + fam.dropFirst() + (nums.isEmpty ? "" : " " + nums.joined(separator: "."))
    }

    /// Kleines Zweig-Symbol und Branch-Name
    static func branch(_ c: Canvas, _ r: CGRect, _ name: String, _ a: TextAlign) {
        let k = r.height / 15, f = roundedFont(12 * k, 600)
        let icon = 12 * k, maxText = r.width - icon
        let text = c.fit(name, f, maxText)
        let total = icon + c.tlen(text, f)
        let x0: CGFloat = a == .left ? r.minX : a == .center ? r.midX - total / 2 : r.maxX - total
        let col = Palette.muted
        // zwei Punkte, verbunden – wie ein Abzweig
        c.ctx.setStrokeColor(col.cgColor); c.ctx.setLineWidth(1.4 * k); c.ctx.setLineCap(.round)
        c.ctx.move(to: CGPoint(x: x0 + 3 * k, y: r.midY - 5 * k)); c.ctx.addLine(to: CGPoint(x: x0 + 3 * k, y: r.midY + 5 * k))
        c.ctx.move(to: CGPoint(x: x0 + 3 * k, y: r.midY + 1 * k))
        c.ctx.addQuadCurve(to: CGPoint(x: x0 + 8 * k, y: r.midY - 4 * k), control: CGPoint(x: x0 + 8 * k, y: r.midY + 1 * k))
        c.ctx.strokePath()
        c.circle(x0 + 3 * k, r.midY + 5 * k, 1.7 * k, col)
        c.circle(x0 + 8 * k, r.midY - 4.5 * k, 1.7 * k, col)
        c.text(x0 + icon, r.midY, text, f, col, v: .middle)
    }

    /// Anzeige mit Prozentzahl und Balken (Kontext)
    static func meter(_ c: Canvas, _ r: CGRect, _ label: String, _ pct: Int, _ col: NSColor, _ tick: Int) {
        let k = r.height / 34
        c.spaced(r.minX, r.minY + 1 * k, label, roundedFont(9 * k, 700), Palette.dim, 1.1 * k)
        let big = roundedFont(16 * k, 700), base = r.minY + 24 * k
        let color = pct >= 90 ? Palette.bad : pct >= 75 ? Palette.warn : col
        c.text(r.minX, base, "\(pct)", big, color, v: .baseline)
        c.text(r.minX + c.tlen("\(pct)", big) + k, base, "%", roundedFont(10 * k, 650), color, v: .baseline)
        c.bar(r.minX, r.minY + 28 * k, r.maxX, 4 * k, pct, color)
    }

    static func name(_ c: Canvas, _ r: CGRect, _ name: String, _ a: TextAlign) {
        // so wenige Zeilen wie möglich – dann ist die Schrift am größten
        var n = r.height >= 24 ? 2 : 1
        for lines in 1...3 {
            let f = roundedFont(r.height / (CGFloat(lines) * 1.235), 680)
            if !c.wrap(name, f, r.width, lines).contains(where: { $0.hasSuffix("…") }) { n = lines; break }
        }
        let size = r.height / (CGFloat(n) * 1.235), f = roundedFont(size, 680), lh = size * 1.235
        var y = r.minY
        let (x, h) = anchor(r, a)
        for l in c.wrap(name, f, r.width, n) {
            c.text(x, y, l, f, Palette.text, h: h)
            y += lh
        }
    }

    static func step(_ c: Canvas, _ r: CGRect, _ s: SessionInfo) {
        let k = r.height / 44
        let text: String, col: NSColor, accent: NSColor
        if s.waiting {
            text = s.activity ?? "Wartet auf dich"; col = Palette.warn; accent = Palette.warn
        } else if !s.plan.isEmpty {
            if let cur = s.currentStep { text = cur; col = Palette.text; accent = Palette.accent }
            else { text = "Alle Schritte erledigt"; col = Palette.good; accent = Palette.good }
        } else {
            text = s.prompt ?? "Denkt nach …"; col = Palette.text; accent = Palette.accent
        }
        c.rrect(r.minX, r.minY, r.maxX, r.maxY, min(12 * k, r.width / 2, r.height / 2), stroke: s.waiting ? Palette.warn : Palette.edge)
        c.rrect(r.minX + 7 * k, r.minY + 9 * k, r.minX + 10.5 * k, r.maxY - 9 * k, 1.75 * k, fill: accent)
        let f = roundedFont(15 * k, 600), lh = 19 * k
        let maxLines = max(1, Int((r.height - 6 * k) / lh))
        let lines = c.wrap(text, f, r.width - 28 * k, maxLines)
        var sy = r.midY - CGFloat(lines.count - 1) * lh / 2
        for l in lines {
            c.text(r.minX + 19 * k, sy, l, f, col, v: .middle)
            sy += lh
        }
    }

    static func limit(_ c: Canvas, _ r: CGRect, _ label: String, _ v: (UsageWindow, Date?)?, _ now: Date, _ tick: Int = 0) {
        let k = r.height / 48
        c.spaced(r.minX, r.minY + 2 * k, label, roundedFont(10 * k, 700), Palette.dim, 1.2 * k)
        let base = r.minY + 36 * k
        guard let (w, stale) = v else {
            c.text(r.minX, base, "–", roundedFont(24 * k, 700), Palette.dim, v: .baseline)
            c.text(r.maxX, base, c.fit("nicht verfügbar", roundedFont(11 * k, 560), r.width * 0.7),
                   roundedFont(11 * k, 560), Palette.dim, h: .right, v: .baseline)
            c.bar(r.minX, r.minY + 41 * k, r.maxX, 5 * k, 0, Palette.dim)
            return
        }
        let col = Palette.forPct(w.pct)
        let big = roundedFont(24 * k, 700)
        c.text(r.minX, base, "\(w.pct)", big, col, v: .baseline)
        c.text(r.minX + c.tlen("\(w.pct)", big) + k, base, "%", roundedFont(13 * k, 650), col, v: .baseline)
        let small = roundedFont(11 * k, 560)
        // Limit fast erreicht: pulsierender Rahmen und deutlicher Hinweis auf den Reset
        let critical = w.pct >= 90 && stale == nil
        if critical {
            let pad = 3 * k
            c.rrect(r.minX - pad, r.minY - pad, r.maxX + pad, r.maxY + pad, 6 * k,
                    stroke: tick % 2 == 0 ? Palette.bad : Palette.bad.withAlphaComponent(0.35), width: max(1, 1.5 * k))
        }
        if let stale {
            c.text(r.maxX, base, "Stand " + Renderer.clock(stale), small, Palette.dim, h: .right, v: .baseline)
        } else if let reset = w.reset {
            let t = Renderer.fmtReset(reset.timeIntervalSince(now))
            let f = critical ? roundedFont(11 * k, 700) : small
            // „Reset 45 min“, wenn Platz ist – sonst nur die Zeit
            let pctWidth = c.tlen("\(w.pct)%", big) + 6 * k
            let label = critical && c.tlen("Reset " + t, f) <= r.width - pctWidth ? "Reset " + t : t
            c.text(r.maxX, base, label, f, critical ? Palette.bad : Palette.muted, h: .right, v: .baseline)
        }
        c.bar(r.minX, r.minY + 41 * k, r.maxX, 5 * k, w.pct, col)
    }

    static func ready(_ c: Canvas, _ r: CGRect, _ a: TextAlign) {
        let k = r.height / 24, f = roundedFont(13 * k, 600), tw = c.tlen("bereit", f)
        let dotSpace = 8 * k
        let left: CGFloat
        switch a {
        case .left: left = r.minX + dotSpace
        case .center: left = r.midX - (tw - dotSpace) / 2
        case .right: left = r.maxX - tw
        }
        c.text(left, r.midY, "bereit", f, Palette.dim, v: .middle)
        c.circle(left - dotSpace, r.midY, 3 * k, Palette.good)
    }

    static func calm(_ c: Canvas, _ r: CGRect) {
        c.rrect(r.minX, r.minY, r.maxX, r.maxY, min(r.height, r.width) / 2, stroke: Palette.edge)
        var size = r.height * 0.533
        var f = roundedFont(size, 620)
        let maxw = r.width - r.height * 0.6
        while size > 6 && c.tlen("Alles ruhig", f) > maxw { size -= 0.5; f = roundedFont(size, 620) }
        c.text(r.midX, r.midY, "Alles ruhig", f, Palette.muted, h: .center, v: .middle)
    }
}
