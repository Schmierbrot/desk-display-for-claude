import AppKit
import SwiftUI

/// Farbe als 8-Bit-RGB (speicherbar, als Hex darstellbar).
struct RGB: Codable, Equatable, Hashable {
    var r: Int, g: Int, b: Int

    init(_ r: Int, _ g: Int, _ b: Int) { self.r = r; self.g = g; self.b = b }
    init(hex: String) {
        let v = Int(hex.trimmingCharacters(in: CharacterSet(charactersIn: "# ")), radix: 16) ?? 0
        self.init((v >> 16) & 255, (v >> 8) & 255, v & 255)
    }
    init(_ c: NSColor) {
        let s = c.usingColorSpace(.sRGB) ?? .black
        self.init(Int((s.redComponent * 255).rounded()), Int((s.greenComponent * 255).rounded()), Int((s.blueComponent * 255).rounded()))
    }

    var hex: String { String(format: "%02X%02X%02X", r, g, b) }
    var ns: NSColor { NSColor(srgbRed: CGFloat(r) / 255, green: CGFloat(g) / 255, blue: CGFloat(b) / 255, alpha: 1) }
    var color: Color { Color(nsColor: ns) }

    /// Mischung: t = 0 → self, t = 1 → other
    func mix(_ o: RGB, _ t: Double) -> RGB {
        RGB(Int(Double(r) + (Double(o.r) - Double(r)) * t), Int(Double(g) + (Double(o.g) - Double(g)) * t),
            Int(Double(b) + (Double(o.b) - Double(b)) * t))
    }

    /// Binding für SwiftUI-ColorPicker
    static func binding(_ b: Binding<RGB>) -> Binding<Color> {
        Binding(get: { b.wrappedValue.color }, set: { b.wrappedValue = RGB(NSColor($0)) })
    }
}

enum LogoStyle: String, Codable, CaseIterable, Identifiable {
    case ring, spark, dot, none
    var id: String { rawValue }
    var label: String {
        switch self {
        case .ring: return "Ring"
        case .spark: return "Stern"
        case .dot: return "Punkt"
        case .none: return "Kein Logo"
        }
    }
    var firmwareCode: Int { [.ring: 0, .spark: 1, .dot: 2, .none: 3][self]! }
}

/// Aussehen des Displays.
struct DisplayTheme: Codable, Equatable {
    var accent: RGB, accent2: RGB
    var text: RGB, muted: RGB, dim: RGB
    var edge: RGB, track: RGB
    var good: RGB, warn: RGB, bad: RGB
    var logo: LogoStyle
    var title: String

    func forPct(_ p: Int) -> RGB { p < 60 ? good : p < 85 ? warn : bad }
}

/// Aussehen der App.
struct AppTheme: Codable, Equatable {
    var accent: RGB, accent2: RGB
    var background: RGB, surface: RGB

    var gradient: LinearGradient {
        LinearGradient(colors: [accent.color, accent2.color], startPoint: .leading, endPoint: .trailing)
    }
}

struct ThemePreset: Identifiable {
    let id: String
    let name: String
    let display: DisplayTheme
    let app: AppTheme
}

enum Themes {
    static let presets: [ThemePreset] = [
        ThemePreset(id: "neon", name: "Neon",
            display: DisplayTheme(accent: RGB(hex: "5B8CFF"), accent2: RGB(hex: "B06BFF"),
                                  text: RGB(hex: "EEF1FF"), muted: RGB(hex: "9AA3C7"), dim: RGB(hex: "646C90"),
                                  edge: RGB(hex: "3A3F5C"), track: RGB(hex: "2A2E44"),
                                  good: RGB(hex: "4BE3A1"), warn: RGB(hex: "FFC857"), bad: RGB(hex: "FF5C7A"),
                                  logo: .ring, title: "Claude Code"),
            app: AppTheme(accent: RGB(hex: "5B8CFF"), accent2: RGB(hex: "B06BFF"),
                          background: RGB(hex: "0B0D14"), surface: RGB(hex: "161A27"))),
        ThemePreset(id: "claude", name: "Claude",
            display: DisplayTheme(accent: RGB(hex: "DE7A58"), accent2: RGB(hex: "F5A07A"),
                                  text: RGB(hex: "F5F0E8"), muted: RGB(hex: "9E958A"), dim: RGB(hex: "686159"),
                                  edge: RGB(hex: "3E3933"), track: RGB(hex: "34302C"),
                                  good: RGB(hex: "80CD98"), warn: RGB(hex: "ECBC5C"), bad: RGB(hex: "EA685C"),
                                  logo: .spark, title: "Claude"),
            app: AppTheme(accent: RGB(hex: "DE7A58"), accent2: RGB(hex: "F5A07A"),
                          background: RGB(hex: "141210"), surface: RGB(hex: "201C19"))),
        ThemePreset(id: "aurora", name: "Aurora",
            display: DisplayTheme(accent: RGB(hex: "2DE2C5"), accent2: RGB(hex: "7CF29A"),
                                  text: RGB(hex: "EAFBF6"), muted: RGB(hex: "93B8AE"), dim: RGB(hex: "5E7A73"),
                                  edge: RGB(hex: "2F4A44"), track: RGB(hex: "22332F"),
                                  good: RGB(hex: "7CF29A"), warn: RGB(hex: "FFD166"), bad: RGB(hex: "FF6B6B"),
                                  logo: .ring, title: "Claude Code"),
            app: AppTheme(accent: RGB(hex: "2DE2C5"), accent2: RGB(hex: "7CF29A"),
                          background: RGB(hex: "08110F"), surface: RGB(hex: "12211D"))),
        ThemePreset(id: "sunset", name: "Sunset",
            display: DisplayTheme(accent: RGB(hex: "FF6B9A"), accent2: RGB(hex: "FFB86B"),
                                  text: RGB(hex: "FFF1EC"), muted: RGB(hex: "C7A39A"), dim: RGB(hex: "8A6A63"),
                                  edge: RGB(hex: "4F3A36"), track: RGB(hex: "3A2A28"),
                                  good: RGB(hex: "7BE0A0"), warn: RGB(hex: "FFB86B"), bad: RGB(hex: "FF5C7A"),
                                  logo: .ring, title: "Claude Code"),
            app: AppTheme(accent: RGB(hex: "FF6B9A"), accent2: RGB(hex: "FFB86B"),
                          background: RGB(hex: "140B10"), surface: RGB(hex: "23161C"))),
        ThemePreset(id: "mono", name: "Mono",
            display: DisplayTheme(accent: RGB(hex: "FFFFFF"), accent2: RGB(hex: "9A9A9A"),
                                  text: RGB(hex: "FFFFFF"), muted: RGB(hex: "A0A0A0"), dim: RGB(hex: "6A6A6A"),
                                  edge: RGB(hex: "3C3C3C"), track: RGB(hex: "2A2A2A"),
                                  good: RGB(hex: "3DDC97"), warn: RGB(hex: "FFD166"), bad: RGB(hex: "FF5C5C"),
                                  logo: .dot, title: "Claude Code"),
            app: AppTheme(accent: RGB(hex: "F2F2F2"), accent2: RGB(hex: "8C8C8C"),
                          background: RGB(hex: "0A0A0A"), surface: RGB(hex: "181818"))),
    ]

    static func preset(_ id: String) -> ThemePreset { presets.first { $0.id == id } ?? presets[0] }
}
