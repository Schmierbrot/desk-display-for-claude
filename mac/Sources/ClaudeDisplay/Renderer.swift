import AppKit
import CoreGraphics

/// Farben fürs Display – kommen aus dem aktiven Theme (Einstellungen → Darstellung).
enum Palette {
    static var theme: DisplayTheme = Themes.preset("neon").display
    static let bg = NSColor.black
    static var edge: NSColor { theme.edge.ns }
    static var text: NSColor { theme.text.ns }
    static var muted: NSColor { theme.muted.ns }
    static var dim: NSColor { theme.dim.ns }
    static var track: NSColor { theme.track.ns }
    static var accent: NSColor { theme.accent.ns }
    static var accent2: NSColor { theme.accent2.ns }
    static var good: NSColor { theme.good.ns }
    static var goodGlow: NSColor { theme.good.mix(RGB(0, 0, 0), 0.75).ns }
    static func forPct(_ p: Int) -> NSColor { theme.forPct(p).ns }
}

/// SF Pro Rounded mit stufenloser Strichstärke (Werte wie CSS: 300…700).
func roundedFont(_ size: CGFloat, _ weight: CGFloat) -> NSFont {
    let stops: [(CGFloat, CGFloat)] = [(300, -0.4), (400, 0), (500, 0.23), (600, 0.3), (700, 0.4), (800, 0.56)]
    var w: CGFloat = 0
    for i in 0..<(stops.count - 1) where weight >= stops[i].0 && weight <= stops[i + 1].0 {
        let t = (weight - stops[i].0) / (stops[i + 1].0 - stops[i].0)
        w = stops[i].1 + t * (stops[i + 1].1 - stops[i].1)
    }
    if weight < 300 { w = -0.4 }
    if weight > 800 { w = 0.56 }
    var desc = NSFont.systemFont(ofSize: size).fontDescriptor
        .addingAttributes([.traits: [NSFontDescriptor.TraitKey.weight: w]])
    if let r = desc.withDesign(.rounded) { desc = r }
    return NSFont(descriptor: desc, size: size) ?? .systemFont(ofSize: size)
}

final class Canvas {
    static let size = 240
    let ctx: CGContext
    private let ns: NSGraphicsContext

    init() {
        let s = Canvas.size
        ctx = CGContext(data: nil, width: s, height: s, bitsPerComponent: 8, bytesPerRow: s * 4,
                        space: CGColorSpace(name: CGColorSpace.sRGB)!,
                        bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        ctx.translateBy(x: 0, y: CGFloat(s))
        ctx.scaleBy(x: 1, y: -1)                 // y nach unten, wie auf dem Display
        ns = NSGraphicsContext(cgContext: ctx, flipped: true)
        ctx.setFillColor(Palette.bg.cgColor)
        ctx.fill(CGRect(x: 0, y: 0, width: s, height: s))
    }

    func circle(_ cx: CGFloat, _ cy: CGFloat, _ r: CGFloat, _ c: NSColor) {
        ctx.setFillColor(c.cgColor)
        ctx.fillEllipse(in: CGRect(x: cx - r, y: cy - r, width: 2 * r, height: 2 * r))
    }

    func rrect(_ x0: CGFloat, _ y0: CGFloat, _ x1: CGFloat, _ y1: CGFloat, _ r: CGFloat,
               fill: NSColor? = nil, stroke: NSColor? = nil, width: CGFloat = 1) {
        let rect = CGRect(x: x0, y: y0, width: x1 - x0, height: y1 - y0)
        if let fill {
            ctx.addPath(CGPath(roundedRect: rect, cornerWidth: r, cornerHeight: r, transform: nil))
            ctx.setFillColor(fill.cgColor)
            ctx.fillPath()
        }
        if let stroke {
            let inset = rect.insetBy(dx: width / 2, dy: width / 2)
            let rr = max(0, r - width / 2)
            ctx.addPath(CGPath(roundedRect: inset, cornerWidth: rr, cornerHeight: rr, transform: nil))
            ctx.setStrokeColor(stroke.cgColor)
            ctx.setLineWidth(width)
            ctx.strokePath()
        }
    }

    /// Bogen; Winkel in Grad, 0 = oben, im Uhrzeigersinn. r = Außenradius.
    func arc(_ cx: CGFloat, _ cy: CGFloat, _ r: CGFloat, _ width: CGFloat,
             _ start: CGFloat, _ end: CGFloat, _ c: NSColor, caps: Bool = true) {
        guard end - start > 0.5 else { return }
        let rad = { (deg: CGFloat) in (deg - 90) * .pi / 180 }
        ctx.setStrokeColor(c.cgColor)
        ctx.setLineWidth(width)
        ctx.setLineCap(caps ? .round : .butt)
        if end - start >= 360 {
            ctx.strokeEllipse(in: CGRect(x: cx - r + width / 2, y: cy - r + width / 2,
                                         width: 2 * r - width, height: 2 * r - width))
        } else {
            ctx.addArc(center: CGPoint(x: cx, y: cy), radius: r - width / 2,
                       startAngle: rad(start), endAngle: rad(end), clockwise: false)
            ctx.strokePath()
        }
    }

    /// Bogen mit Farbverlauf von c1 nach c2 (in kleinen Stücken), runde Enden.
    func gradientArc(_ cx: CGFloat, _ cy: CGFloat, _ r: CGFloat, _ width: CGFloat,
                     _ start: CGFloat, _ end: CGFloat, _ c1: RGB, _ c2: RGB) {
        guard end - start > 0.5 else { return }
        let steps = max(2, Int((end - start) / 4))
        for i in 0..<steps {
            let a0 = start + (end - start) * CGFloat(i) / CGFloat(steps)
            let a1 = start + (end - start) * CGFloat(i + 1) / CGFloat(steps) + 0.6
            let col = c1.mix(c2, Double(i) / Double(max(1, steps - 1))).ns
            arc(cx, cy, r, width, a0, min(a1, end), col, caps: false)
        }
        let rm = r - width / 2
        for (a, col) in [(start, c1), (end, c2)] {
            let rad = (a - 90) * .pi / 180
            circle(cx + rm * cos(rad), cy + rm * sin(rad), width / 2, col.ns)
        }
    }

    /// Logo oben links im gewählten Stil; `tick` animiert es, solange gearbeitet wird.
    func logo(_ style: LogoStyle, _ cx: CGFloat, _ cy: CGFloat, active: Bool, tick: Int) {
        let t = Palette.theme
        switch style {
        case .ring:
            if active {
                let s = CGFloat((tick * 30) % 360)
                gradientArc(cx, cy, 9, 3, s, s + 290, t.accent, t.accent2)
            } else {
                gradientArc(cx, cy, 9, 3, 0, 359.4, t.accent, t.accent2)
            }
        case .spark:
            spark(cx, cy, 9.5, Palette.accent, angle: active ? CGFloat(tick * 15) : 0)
        case .dot:
            circle(cx, cy, active && tick % 2 == 0 ? 8 : 6.5, t.accent.mix(RGB(0, 0, 0), 0.7).ns)
            circle(cx, cy, 4.5, Palette.accent)
        case .none:
            break
        }
    }

    func bar(_ x0: CGFloat, _ y: CGFloat, _ x1: CGFloat, _ h: CGFloat, _ pct: Int, _ c: NSColor) {
        rrect(x0, y, x1, y + h, h / 2, fill: Palette.track)
        let w = (x1 - x0) * CGFloat(max(0, min(100, pct))) / 100
        if w >= h { rrect(x0, y, x0 + w, y + h, h / 2, fill: c) }
    }

    // MARK: Text

    enum HAnchor { case left, center, right }
    enum VAnchor { case top, middle, baseline }

    func tlen(_ s: String, _ f: NSFont) -> CGFloat {
        (s as NSString).size(withAttributes: [.font: f]).width
    }

    func text(_ x: CGFloat, _ y: CGFloat, _ s: String, _ f: NSFont, _ c: NSColor,
              h: HAnchor = .left, v: VAnchor = .top) {
        let w = tlen(s, f)
        let left = h == .left ? x : h == .center ? x - w / 2 : x - w
        let top: CGFloat
        switch v {
        case .top: top = y
        case .baseline: top = y - f.ascender
        case .middle: top = y - (f.ascender - f.descender) / 2
        }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = ns
        (s as NSString).draw(at: CGPoint(x: left, y: top), withAttributes: [.font: f, .foregroundColor: c])
        NSGraphicsContext.restoreGraphicsState()
    }

    func fit(_ s: String, _ f: NSFont, _ maxw: CGFloat) -> String {
        if tlen(s, f) <= maxw { return s }
        var t = s
        while !t.isEmpty && tlen(t + "…", f) > maxw { t.removeLast() }
        return t.trimmingCharacters(in: .whitespaces) + "…"
    }

    func wrap(_ s: String, _ f: NSFont, _ maxw: CGFloat, _ maxLines: Int) -> [String] {
        let words = s.split(separator: " ").map(String.init)
        var lines: [String] = [], line = ""
        for (i, w) in words.enumerated() {
            let t = line.isEmpty ? w : line + " " + w
            if tlen(t, f) <= maxw { line = t; continue }
            if !line.isEmpty { lines.append(line) }
            line = w
            if lines.count == maxLines {
                lines[maxLines - 1] = fit(lines[maxLines - 1] + " " + words[i...].joined(separator: " "), f, maxw)
                return lines
            }
        }
        if !line.isEmpty { lines.append(fit(line, f, maxw)) }
        return Array(lines.prefix(maxLines))
    }

    @discardableResult
    func spaced(_ x: CGFloat, _ y: CGFloat, _ s: String, _ f: NSFont, _ c: NSColor, _ spacing: CGFloat = 1.2) -> CGFloat {
        var x = x
        for ch in s {
            text(x, y, String(ch), f, c)
            x += tlen(String(ch), f) + spacing
        }
        return x
    }

    /// Claude-Stern: zwölf abgerundete Strahlen, abwechselnd lang und kurz.
    func spark(_ cx: CGFloat, _ cy: CGFloat, _ r: CGFloat, _ c: NSColor, angle: CGFloat = 0) {
        ctx.setStrokeColor(c.cgColor)
        ctx.setLineWidth(r * 0.22)
        ctx.setLineCap(.round)
        for k in 0..<12 {
            let a = (angle + CGFloat(k) * 30) * .pi / 180
            let len = k % 2 == 0 ? r : r * 0.68
            ctx.move(to: CGPoint(x: cx + cos(a) * r * 0.18, y: cy + sin(a) * r * 0.18))
            ctx.addLine(to: CGPoint(x: cx + cos(a) * len, y: cy + sin(a) * len))
        }
        ctx.strokePath()
        circle(cx, cy, r * 0.2, c)
    }

    var image: CGImage { ctx.makeImage()! }

    /// RGB-Bytes (3 pro Pixel) für die Übertragung.
    var rgb: [UInt8] {
        let n = Canvas.size * Canvas.size
        let p = ctx.data!.bindMemory(to: UInt8.self, capacity: n * 4)
        var out = [UInt8](repeating: 0, count: n * 3)
        for i in 0..<n {
            out[i * 3] = p[i * 4]; out[i * 3 + 1] = p[i * 4 + 1]; out[i * 3 + 2] = p[i * 4 + 2]
        }
        return out
    }
}

// MARK: - Ansichten

enum Renderer {
    static let wochentage = ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"]
    static let monate = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August",
                         "September", "Oktober", "November", "Dezember"]

    private static let lock = NSLock()

    /// Zeichnet eine Ansicht im angegebenen Theme (Standard: Einstellungen).
    static func render(session: SessionInfo?, usage: Usage?, tick: Int, now: Date = Date(),
                       theme: DisplayTheme? = nil) -> Canvas {
        lock.lock(); defer { lock.unlock() }
        Palette.theme = theme ?? AppSettings.shared.displayTheme
        let c = Canvas()
        if let session { active(c, session, usage, tick, now) } else { idle(c, usage, now) }
        return c
    }

    static func fmtReset(_ secs: TimeInterval) -> String {
        let m = max(0, Int(secs)) / 60
        if m < 60 { return "\(m) min" }
        let h = m / 60, mm = m % 60
        if h < 24 { return "\(h)h " + String(format: "%02d", mm) }
        return "\(h / 24)d \(h % 24)h"
    }

    static func fmtTimer(_ s: Int) -> String {
        if s >= 3600 { return "\(s / 3600):" + String(format: "%02d", (s / 60) % 60) + " h" }
        return "\(s / 60):" + String(format: "%02d", s % 60)
    }

    static func clock(_ d: Date) -> String {
        let f = DateFormatter(); f.dateFormat = "HH:mm"; return f.string(from: d)
    }

    static func header(_ c: Canvas, _ now: Date, active: Bool, tick: Int, showClock: Bool) {
        let t = Palette.theme
        c.logo(t.logo, 17, 18, active: active, tick: tick)
        let x: CGFloat = t.logo == .none ? 10 : 33
        let f = roundedFont(16, 650)
        c.text(x, 18, c.fit(t.title, f, (showClock ? 180 : 150) - x), f, Palette.text, v: .middle)
        if showClock {
            c.text(230, 18, clock(now), roundedFont(16, 560), Palette.muted, h: .right, v: .middle)
        }
    }

    static func limits(_ c: Canvas, _ use: Usage?, _ now: Date) {
        guard let use else {
            c.text(120, 214, "Limit gerade nicht verfügbar", roundedFont(12, 500), Palette.dim, h: .center, v: .middle)
            return
        }
        for (x0, x1, label, w) in [(CGFloat(10), CGFloat(114), "5 STD", use.five), (126, 230, "WOCHE", use.week)] {
            c.spaced(x0, 188, label, roundedFont(10, 700), Palette.dim)
            let col = Palette.forPct(w.pct)
            let big = roundedFont(24, 700)
            c.text(x0, 222, "\(w.pct)", big, col, v: .baseline)
            c.text(x0 + c.tlen("\(w.pct)", big) + 1, 222, "%", roundedFont(13, 650), col, v: .baseline)
            if let s = use.stale {
                c.text(x1, 222, "Stand " + clock(s), roundedFont(11, 560), Palette.dim, h: .right, v: .baseline)
            } else if let r = w.reset {
                c.text(x1, 222, fmtReset(r.timeIntervalSince(now)), roundedFont(11, 560), Palette.muted, h: .right, v: .baseline)
            }
            c.bar(x0, 227, x1, 5, w.pct, col)
        }
    }

    static func active(_ c: Canvas, _ s: SessionInfo, _ use: Usage?, _ tick: Int, _ now: Date) {
        header(c, now, active: true, tick: tick, showClock: true)

        // Fortschrittsring
        let (cx, cy, r, w): (CGFloat, CGFloat, CGFloat, CGFloat) = (52, 84, 40, 8)
        c.arc(cx, cy, r, w, 0, 360, Palette.track, caps: false)
        if !s.plan.isEmpty {
            let frac = CGFloat(s.done) / CGFloat(s.plan.count)
            if frac > 0 {
                c.gradientArc(cx, cy, r, w, 0, min(359.4, 360 * frac), Palette.theme.accent, Palette.theme.accent2)
            }
            c.text(cx, cy - 4, "\(s.done)", roundedFont(30, 700), Palette.text, h: .center, v: .middle)
            c.text(cx, cy + 19, "von \(s.plan.count)", roundedFont(11, 600), Palette.muted, h: .center, v: .middle)
        } else {
            let start = CGFloat((tick * 40) % 360)
            c.gradientArc(cx, cy, r, w, start, start + 80, Palette.theme.accent, Palette.theme.accent2)
            c.text(cx, cy, "···", roundedFont(22, 700), Palette.muted, h: .center, v: .middle)
        }

        // Status, Name, Zeit
        let x: CGFloat = 104
        c.circle(x + 4, 51, tick % 2 == 0 ? 5.5 : 4.2, Palette.goodGlow)
        c.circle(x + 4, 51, 3.4, Palette.good)
        c.spaced(x + 13, 45, "ARBEITET" + (s.busyCount > 1 ? "  +\(s.busyCount - 1)" : ""), roundedFont(10, 700), Palette.good)
        var y: CGFloat = 60
        let nameFont = roundedFont(17, 680)
        for line in c.wrap(s.name, nameFont, 240 - x - 10, 2) {
            c.text(x, y, line, nameFont, Palette.text)
            y += 21
        }
        c.text(x, max(y + 4, 104), fmtTimer(s.secs), roundedFont(15, 560), Palette.muted)

        // Karte mit dem aktuellen Schritt
        let step: String, col: NSColor, accent: NSColor
        if !s.plan.isEmpty {
            if let cur = s.currentStep { step = cur; col = Palette.text; accent = Palette.accent }
            else { step = "Alle Schritte erledigt"; col = Palette.good; accent = Palette.good }
        } else {
            step = s.prompt ?? "Denkt nach …"; col = Palette.text; accent = Palette.accent
        }
        c.rrect(8, 134, 232, 178, 12, stroke: Palette.edge)
        c.rrect(15, 143, 18.5, 169, 1.75, fill: accent)
        let stepFont = roundedFont(15, 600)
        let lines = c.wrap(step, stepFont, 240 - 44, 2)
        var sy = 156 - CGFloat(lines.count - 1) * 9.5
        for line in lines {
            c.text(27, sy, line, stepFont, col, v: .middle)
            sy += 19
        }
        limits(c, use, now)
    }

    static func idle(_ c: Canvas, _ use: Usage?, _ now: Date) {
        header(c, now, active: false, tick: 0, showClock: false)
        let rf = roundedFont(13, 600)
        c.text(230, 18, "bereit", rf, Palette.dim, h: .right, v: .middle)
        c.circle(230 - c.tlen("bereit", rf) - 8, 18, 3, Palette.good)

        c.text(120, 76, clock(now), roundedFont(66, 330), Palette.text, h: .center, v: .middle)
        let cal = Calendar.current
        let datum = "\(wochentage[cal.component(.weekday, from: now) - 1]), \(cal.component(.day, from: now)). \(monate[cal.component(.month, from: now) - 1])"
        var size: CGFloat = 19
        while size > 15 && c.tlen(datum, roundedFont(size, 600)) > 224 { size -= 1 }
        c.text(120, 128, datum, roundedFont(size, 600), Palette.muted, h: .center, v: .middle)
        let t = "Alles ruhig", tf = roundedFont(16, 620)
        let pw = c.tlen(t, tf) / 2 + 20
        c.rrect(120 - pw, 146, 120 + pw, 176, 15, stroke: Palette.edge)
        c.text(120, 161, t, tf, Palette.muted, h: .center, v: .middle)
        limits(c, use, now)
    }
}
