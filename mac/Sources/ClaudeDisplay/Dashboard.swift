import AppKit
import SwiftUI

// MARK: - Fenster

/// Fenster ohne Titelleiste, das SwiftUI-Inhalt zeigt (Dashboard, Einstellungen, Einrichtung).
final class HostedWindow {
    private var window: NSWindow?
    private let size: NSSize
    private let content: () -> AnyView
    private let resizable: Bool

    init(size: NSSize, resizable: Bool = false, content: @escaping () -> AnyView) {
        self.size = size; self.resizable = resizable; self.content = content
    }

    var isVisible: Bool { window?.isVisible ?? false }

    func show() {
        if window == nil {
            var style: NSWindow.StyleMask = [.titled, .closable, .miniaturizable, .fullSizeContentView]
            if resizable { style.insert(.resizable) }
            let w = NSWindow(contentRect: NSRect(origin: .zero, size: size), styleMask: style, backing: .buffered, defer: false)
            w.titlebarAppearsTransparent = true
            w.titleVisibility = .hidden
            w.isMovableByWindowBackground = false   // nur oben an der Titelleiste verschieben – sonst zieht jedes Ziehen im Fenster mit
            w.isReleasedWhenClosed = false
            w.appearance = NSAppearance(named: .darkAqua)
            w.contentView = FullWindowHost.make(content())
            w.backgroundColor = AppSettings.shared.appTheme.background.ns
            w.center()
            window = w
        }
        if let window { DockPresence.shown(window) }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func close() {
        window?.close()
        window = nil
    }
}

/// Inhalt füllt das ganze Fenster: kein Sicherheitsabstand für die (ausgeblendete)
/// Titelleiste – sonst rutscht der Inhalt nach unten und unten bleibt ein grauer Streifen.
enum FullWindowHost {
    /// Der Inhalt füllt das ganze Fenster (wie hoch es auch ist), der Hintergrund reicht
    /// hinter die ausgeblendete Titelleiste. Das Fenster richtet sich nicht nach dem
    /// Inhalt – sonst entsteht eine Größen-Endlosschleife.
    static func make<V: View>(_ view: V) -> NSView {
        let host = NSHostingView(rootView: AnyView(view.frame(maxWidth: .infinity, maxHeight: .infinity)))
        host.sizingOptions = []
        host.autoresizingMask = [.width, .height]
        // Unsichtbarer Streifen oben (anstelle der ausgeblendeten Titelleiste) zum Verschieben.
        let drag = TitleDragView(frame: .zero)
        drag.autoresizingMask = [.width]
        host.addSubview(drag)
        return host
    }
}

/// Fenster nur hier oben verschieben – ziehen woanders (z. B. im Layout-Editor) bewegt das Fenster nicht.
final class TitleDragView: NSView {
    static let height: CGFloat = 28
    override var mouseDownCanMoveWindow: Bool { false }
    override func viewDidMoveToSuperview() { place() }
    override func resize(withOldSuperviewSize oldSize: NSSize) { place() }
    /// immer ganz oben, über die volle Breite
    private func place() {
        guard let s = superview else { return }
        frame = NSRect(x: 0, y: s.isFlipped ? 0 : s.bounds.height - Self.height, width: s.bounds.width, height: Self.height)
    }
    override func mouseDown(with event: NSEvent) {
        if event.clickCount == 2 {
            // Doppelklick wie bei einer normalen Titelleiste (Systemeinstellung: Zoomen oder Ablegen)
            let action = UserDefaults.standard.string(forKey: "AppleActionOnDoubleClick") ?? "Maximize"
            if action == "Minimize" { window?.performMiniaturize(nil) }
            else if window?.styleMask.contains(.resizable) == true { window?.performZoom(nil) }
            return
        }
        window?.performDrag(with: event)
    }
}

enum MainWindow {
    static let shared = HostedWindow(size: NSSize(width: 960, height: 640)) { AnyView(DashboardView()) }
    static func show() { shared.show() }
    static func close() { shared.close() }
}

enum SettingsWindow {
    static let shared = HostedWindow(size: NSSize(width: 820, height: 600)) { AnyView(SettingsView()) }
    static func show() { shared.show() }
}

/// Menüleiste oder App-Fenster mit Dock-Symbol
enum AppMode {
    static func apply() {
        let window = AppSettings.shared.windowMode
        NSApp.setActivationPolicy(window ? .regular : .accessory)
        if window { MainWindow.show() } else { MainWindow.close(); DockPresence.update() }
    }
}

/// Im Menüleisten-Modus erscheint die App im Dock (mit Punkt), solange eines ihrer
/// Fenster offen ist, und verschwindet wieder, wenn das letzte geschlossen wird.
enum DockPresence {
    private static var observed = Set<ObjectIdentifier>()

    static func shown(_ w: NSWindow) {
        if NSApp.activationPolicy() != .regular { NSApp.setActivationPolicy(.regular) }
        let id = ObjectIdentifier(w)
        guard !observed.contains(id) else { return }
        observed.insert(id)
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: w, queue: .main) { _ in
            DispatchQueue.main.async { update() }
        }
    }

    static func update() {
        guard !AppSettings.shared.windowMode else { return }
        let anyOpen = NSApp.windows.contains { $0.isVisible && $0.styleMask.contains(.titled) && !($0 is NSPanel) }
        if !anyOpen { NSApp.setActivationPolicy(.accessory) }
    }
}

// MARK: - Dashboard (Fenster-Modus)

struct DashboardView: View {
    @Bindable private var settings = AppSettings.shared
    private var engine = Engine.shared
    private var claude = ClaudeConnect.shared
    private var updater = Updater.shared

    var body: some View {
        HStack(spacing: 0) {
            left.frame(width: 360)
            Divider().opacity(0.15)
            ScrollView { right.padding(.horizontal, 28).padding(.bottom, 28).padding(.top, 6) }.scrollIndicators(.never)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)   // Größe gibt das Fenster vor
        .background(NeonBackground(theme: settings.appTheme))   // reicht hinter die Titelleiste
        .tint(settings.appTheme.accent.color)
        .preferredColorScheme(.dark)
    }

    // links: Display
    private var left: some View {
        VStack(spacing: 22) {
            HStack(spacing: 11) {
                AppMark(size: 28, spinning: engine.session != nil)
                VStack(alignment: .leading, spacing: 1) {
                    Text("Claude Display").font(DS.font(17, .bold))
                    Text(subtitle).font(DS.font(11.5)).foregroundStyle(.secondary).lineLimit(1)
                }
                Spacer()
            }
            .padding(.top, 8)
            DevicePreview(image: engine.preview, size: 250)
            StatusChip(status: engine.status)
            VStack(spacing: 12) {
                brightness
                Toggle(isOn: $settings.enabled) { Text("Display ansteuern").font(DS.font(12.5, .medium)) }
                    .toggleStyle(.switch).controlSize(.small)
                    .onChange(of: settings.enabled) { Engine.shared.settingsChanged() }
            }
            .glass(14)
            Spacer()
            HStack {
                Button { SettingsWindow.show() } label: { Label("Einstellungen", systemImage: "slider.horizontal.3") }
                    .buttonStyle(NeonButtonStyle(prominent: false))
                Spacer()
            }
            .padding(.bottom, 22)
        }
        .padding(.horizontal, 24)
        .background(Color.black.opacity(0.22).ignoresSafeArea())
    }

    private var subtitle: String {
        if settings.host.isEmpty { return "Noch kein Display eingerichtet" }
        if let d = engine.device { return "\(d.ip) · Firmware \(d.version)" }
        return settings.host
    }

    private var brightness: some View {
        let night = settings.isNight()
        let binding = night ? $settings.nightBrightness : $settings.dayBrightness
        return HStack(spacing: 10) {
            Image(systemName: night ? "moon.stars.fill" : "sun.max.fill").foregroundStyle(.secondary).frame(width: 18)
            Slider(value: binding, in: 0...100) { e in if !e { Engine.shared.settingsChanged() } }
            Text("\(Int(binding.wrappedValue))%").font(DS.font(11, .semibold)).monospacedDigit()
                .foregroundStyle(.secondary).frame(width: 36, alignment: .trailing)
        }
    }

    // rechts: Inhalt
    var right: some View {
        VStack(alignment: .leading, spacing: 18) {
            HStack(alignment: .firstTextBaseline) {
                Text(greeting).font(DS.font(28, .bold))
                Spacer()
                Text(Date.now.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                    .font(DS.font(13)).foregroundStyle(.secondary)
            }
            banners
            sessionHero
            HStack(spacing: 14) {
                LimitCard(title: "5 Stunden", window: engine.usage?.five)
                LimitCard(title: "Woche", window: engine.usage?.week)
            }
        }
    }

    private var greeting: String {
        let h = Calendar.current.component(.hour, from: Date())
        return h < 11 ? "Guten Morgen" : h < 18 ? "Hallo" : "Guten Abend"
    }

    @ViewBuilder private var banners: some View {
        if !claude.isConnected, claude.state != .checking {
            banner(icon: "person.crop.circle.badge.exclamationmark", text: "Claude ist nicht verbunden – ohne Login gibt es kein Nutzungslimit.",
                   action: "Verbinden") { SetupWindow.show() }
        }
        if settings.host.isEmpty {
            banner(icon: "display.trianglebadge.exclamationmark", text: "Noch kein Display eingerichtet.", action: "Einrichten") { SetupWindow.show() }
        }
        if updater.appUpdate, let v = updater.latest?.appVersion {
            banner(icon: "arrow.down.circle.fill", text: "Version \(v) ist verfügbar.", action: "Aktualisieren") {
                Task { await Updater.shared.installApp() }
            }
        } else if updater.firmwareUpdate(device: engine.device?.version), let v = updater.latest?.firmwareVersion {
            banner(icon: "cpu", text: "Display-Firmware \(v) ist verfügbar.", action: "Aufspielen") {
                Task { await Updater.shared.installFirmware() }
            }
        }
    }

    private func banner(icon: String, text: String, action: String, run: @escaping () -> Void) -> some View {
        HStack(spacing: 12) {
            Image(systemName: icon).font(.system(size: 18)).foregroundStyle(settings.appTheme.gradient)
            Text(text).font(DS.font(13))
            Spacer()
            Button(action, action: run).buttonStyle(NeonButtonStyle())
        }
        .glass(12, highlight: settings.appTheme.accent.color)
    }

    @ViewBuilder private var sessionHero: some View {
        let t = settings.appTheme
        if let s = engine.session {
            VStack(alignment: .leading, spacing: 14) {
                HStack {
                    Circle().fill(t.accent2.color).frame(width: 8, height: 8).shadow(color: t.accent2.color, radius: 5)
                    Text("ARBEITET").font(DS.font(11, .bold)).tracking(1.2).foregroundStyle(t.accent2.color)
                    Spacer()
                    Text(Renderer.fmtTimer(s.secs)).font(DS.font(15, .semibold)).monospacedDigit().foregroundStyle(.secondary)
                }
                Text(s.name).font(DS.font(22, .bold)).lineLimit(2)
                if !s.plan.isEmpty {
                    HStack {
                        GradientBar(value: Double(s.done) / Double(s.plan.count), colors: [t.accent.color, t.accent2.color], height: 8)
                        Text("\(s.done)/\(s.plan.count)").font(DS.font(13, .bold)).monospacedDigit()
                    }
                    VStack(alignment: .leading, spacing: 9) {
                        ForEach(Array(s.plan.prefix(8).enumerated()), id: \.offset) { _, item in stepRow(item) }
                    }
                } else if let p = s.prompt {
                    Text(p).font(DS.font(14)).foregroundStyle(.secondary).lineLimit(3)
                }
            }
            .glass(20, highlight: t.accent.color)
        } else {
            VStack(spacing: 10) {
                Image(systemName: "moon.zzz.fill").font(.system(size: 30)).foregroundStyle(t.gradient)
                Text("Gerade läuft keine Claude-Sitzung").font(DS.font(16, .semibold))
                Text("Sobald Claude Code arbeitet, siehst du hier Fortschritt und Schritte.")
                    .font(DS.font(12.5)).foregroundStyle(.secondary).multilineTextAlignment(.center)
            }
            .frame(maxWidth: .infinity).padding(.vertical, 22)
            .glass(20)
        }
    }

    private func stepRow(_ item: PlanItem) -> some View {
        let t = settings.appTheme
        let (icon, color): (String, Color) = switch item.status {
        case "completed": ("checkmark.circle.fill", Color(nsColor: settings.displayTheme.good.ns))
        case "in_progress": ("play.circle.fill", t.accent.color)
        default: ("circle", .secondary)
        }
        return HStack(spacing: 10) {
            Image(systemName: icon).foregroundStyle(color).font(.system(size: 14))
            Text(item.text).font(DS.font(13, item.status == "in_progress" ? .semibold : .regular))
                .foregroundStyle(item.status == "pending" ? .secondary : .primary)
                .strikethrough(item.status == "completed", color: .secondary.opacity(0.5))
                .lineLimit(1)
        }
    }
}
