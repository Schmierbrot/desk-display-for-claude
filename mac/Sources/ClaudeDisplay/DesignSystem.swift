import SwiftUI

/// Gemeinsame Bausteine der Oberfläche (dunkel, Neon-Akzente aus dem App-Theme).
enum DS {
    static var theme: AppTheme { AppSettings.shared.appTheme }
    static let radius: CGFloat = 14
    static func font(_ size: CGFloat, _ weight: Font.Weight = .regular) -> Font {
        .system(size: size, weight: weight, design: .rounded)
    }
}

/// Hintergrund: tiefe Grundfarbe mit zwei weichen Leuchtflecken in den Akzentfarben.
struct NeonBackground: View {
    var theme: AppTheme = DS.theme
    var body: some View {
        ZStack {
            theme.background.color
            RadialGradient(colors: [theme.accent.color.opacity(0.28), .clear], center: .topLeading, startRadius: 0, endRadius: 420)
            RadialGradient(colors: [theme.accent2.color.opacity(0.20), .clear], center: .bottomTrailing, startRadius: 0, endRadius: 460)
        }
        .ignoresSafeArea()
    }
}

/// Glas-Karte
struct GlassCard: ViewModifier {
    var padding: CGFloat = 14
    var highlight: Color? = nil
    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(
                RoundedRectangle(cornerRadius: DS.radius, style: .continuous)
                    .fill(DS.theme.surface.color.opacity(0.72))
                    .overlay(RoundedRectangle(cornerRadius: DS.radius, style: .continuous)
                        .fill((highlight ?? .clear).opacity(0.08)))
            )
            .overlay(
                RoundedRectangle(cornerRadius: DS.radius, style: .continuous)
                    .strokeBorder(LinearGradient(colors: [(highlight ?? .white).opacity(0.22), .white.opacity(0.04)],
                                                 startPoint: .topLeading, endPoint: .bottomTrailing), lineWidth: 1)
            )
    }
}

extension View {
    func glass(_ padding: CGFloat = 14, highlight: Color? = nil) -> some View {
        modifier(GlassCard(padding: padding, highlight: highlight))
    }
}

/// App-Zeichen: Ring mit Farbverlauf, optional drehend.
struct AppMark: View {
    var size: CGFloat = 26
    var spinning = false
    var theme: AppTheme = DS.theme
    var body: some View {
        TimelineView(.animation(minimumInterval: 1 / 30, paused: !spinning)) { ctx in
            let angle = spinning ? ctx.date.timeIntervalSinceReferenceDate * 120 : -90
            ZStack {
                Circle()
                    .trim(from: spinning ? 0.08 : 0, to: 1)
                    .stroke(AngularGradient(colors: [theme.accent.color, theme.accent2.color, theme.accent.color], center: .center),
                            style: StrokeStyle(lineWidth: size * 0.16, lineCap: .round))
                    .rotationEffect(.degrees(angle))
                Circle().fill(theme.accent2.color).frame(width: size * 0.22, height: size * 0.22)
                    .shadow(color: theme.accent2.color.opacity(0.8), radius: size * 0.15)
            }
            .frame(width: size, height: size)
            .shadow(color: theme.accent.color.opacity(0.45), radius: size * 0.25)
        }
    }
}

/// Schmaler Balken mit Verlauf oder fester Farbe
struct GradientBar: View {
    var value: Double                    // 0…1
    var colors: [Color]
    var height: CGFloat = 6
    var body: some View {
        GeometryReader { g in
            ZStack(alignment: .leading) {
                Capsule().fill(.white.opacity(0.08))
                Capsule()
                    .fill(LinearGradient(colors: colors, startPoint: .leading, endPoint: .trailing))
                    .frame(width: max(height, g.size.width * min(1, max(0, value))))
                    .shadow(color: (colors.last ?? .clear).opacity(0.55), radius: 5)
                    .opacity(value > 0 ? 1 : 0)
            }
        }
        .frame(height: height)
    }
}

/// Statuskapsel mit leuchtendem Punkt
struct StatusChip: View {
    let status: EngineStatus
    var color: Color {
        switch status {
        case .shown: return Color(nsColor: AppSettings.shared.displayTheme.good.ns)
        case .standby: return Color(nsColor: AppSettings.shared.displayTheme.warn.ns)
        case .error: return Color(nsColor: AppSettings.shared.displayTheme.bad.ns)
        case .updating: return DS.theme.accent.color
        default: return .secondary
        }
    }
    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(color).frame(width: 7, height: 7).shadow(color: color, radius: 4)
            Text(status.title).font(DS.font(11, .semibold))
        }
        .padding(.horizontal, 10).padding(.vertical, 5)
        .background(Capsule().fill(color.opacity(0.14)))
        .overlay(Capsule().strokeBorder(color.opacity(0.35), lineWidth: 0.8))
    }
}

/// Live-Vorschau des Displays in einem leuchtenden Gehäuse.
struct DevicePreview: View {
    let image: CGImage?
    var size: CGFloat = 240
    var body: some View {
        let t = DS.theme
        ZStack {
            RoundedRectangle(cornerRadius: size * 0.11, style: .continuous)
                .fill(LinearGradient(colors: [Color(white: 0.16), Color(white: 0.05)], startPoint: .top, endPoint: .bottom))
                .overlay(RoundedRectangle(cornerRadius: size * 0.11, style: .continuous)
                    .strokeBorder(LinearGradient(colors: [t.accent.color.opacity(0.55), t.accent2.color.opacity(0.25)],
                                                 startPoint: .topLeading, endPoint: .bottomTrailing), lineWidth: 1.2))
                .shadow(color: t.accent.color.opacity(0.35), radius: 22, y: 6)
            Group {
                if let image { Image(decorative: image, scale: 1).interpolation(.high).resizable() } else { Color.black }
            }
            .frame(width: size, height: size)
            .clipShape(RoundedRectangle(cornerRadius: size * 0.025, style: .continuous))
        }
        .frame(width: size * 1.133, height: size * 1.133)
    }
}

/// Primärer Knopf mit Verlauf
struct NeonButtonStyle: ButtonStyle {
    var prominent = true
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(DS.font(13, .semibold))
            .padding(.horizontal, 16).padding(.vertical, 8)
            .foregroundStyle(prominent ? Color.white : Color.primary)
            .background(
                Capsule().fill(prominent ? AnyShapeStyle(DS.theme.gradient) : AnyShapeStyle(Color.white.opacity(0.08)))
            )
            .overlay(Capsule().strokeBorder(.white.opacity(prominent ? 0.25 : 0.12), lineWidth: 0.8))
            .shadow(color: prominent ? DS.theme.accent.color.opacity(0.45) : .clear, radius: 10)
            .opacity(configuration.isPressed ? 0.75 : 1)
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
    }
}

/// Runder Symbol-Knopf (Glas)
struct IconButton: View {
    let systemName: String
    let help: String
    let action: () -> Void
    var body: some View {
        Button(action: action) {
            Image(systemName: systemName).font(.system(size: 13, weight: .semibold))
                .frame(width: 30, height: 30)
                .background(Circle().fill(.white.opacity(0.07)))
                .overlay(Circle().strokeBorder(.white.opacity(0.12), lineWidth: 0.8))
        }
        .buttonStyle(.plain)
        .help(help)
    }
}

/// Abschnittsüberschrift in Großbuchstaben
struct SectionLabel: View {
    let text: String
    var body: some View {
        Text(text.uppercased()).font(DS.font(10.5, .bold)).tracking(1.2).foregroundStyle(.secondary)
    }
}
