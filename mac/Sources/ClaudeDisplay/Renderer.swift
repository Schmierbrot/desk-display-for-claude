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
    static var warn: NSColor { theme.warn.ns }
    static var bad: NSColor { theme.bad.ns }
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

    /// Logo im gewählten Stil; `tick` animiert es, solange gearbeitet wird. scale 1 = 18 px.
    func logo(_ style: LogoStyle, _ cx: CGFloat, _ cy: CGFloat, scale k: CGFloat = 1, active: Bool, tick: Int) {
        let t = Palette.theme
        switch style {
        case .ring:
            if active {
                let s = CGFloat((tick * 30) % 360)
                gradientArc(cx, cy, 9 * k, 3 * k, s, s + 290, t.accent, t.accent2)
            } else {
                gradientArc(cx, cy, 9 * k, 3 * k, 0, 359.4, t.accent, t.accent2)
            }
        case .spark:
            spark(cx, cy, 9.5 * k, Palette.accent, angle: active ? CGFloat(tick * 15) : 0)
        case .dot:
            circle(cx, cy, (active && tick % 2 == 0 ? 8 : 6.5) * k, t.accent.mix(RGB(0, 0, 0), 0.7).ns)
            circle(cx, cy, 4.5 * k, Palette.accent)
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

    /// Zeichnet eine Ansicht im angegebenen Theme und Layout (Standard: Einstellungen).
    static func render(session: SessionInfo?, usage: Usage?, tick: Int, now: Date = Date(),
                       theme: DisplayTheme? = nil, layout: DisplayLayout? = nil) -> Canvas {
        lock.lock(); defer { lock.unlock() }
        Palette.theme = theme ?? AppSettings.shared.displayTheme
        let l = layout ?? AppSettings.shared.displayLayout
        let c = Canvas()
        let data = WidgetData(session: session, usage: usage, tick: tick, now: now)
        for item in (session != nil ? l.active : l.idle) where item.visible {
            Widgets.draw(c, item, data)
        }
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

    static func dateText(_ now: Date) -> String {
        let cal = Calendar.current
        return "\(wochentage[cal.component(.weekday, from: now) - 1]), \(cal.component(.day, from: now)). \(monate[cal.component(.month, from: now) - 1])"
    }
}
