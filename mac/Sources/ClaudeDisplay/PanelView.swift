import SwiftUI

/// Karte für ein Nutzungsfenster
struct LimitCard: View {
    let title: String
    let window: UsageWindow?
    var body: some View {
        let t = AppSettings.shared.displayTheme
        let col = window.map { t.forPct($0.pct).color } ?? .secondary
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                SectionLabel(text: title)
                Spacer()
                if let r = window?.reset {
                    Text(Renderer.fmtReset(r.timeIntervalSinceNow)).font(DS.font(10, .medium)).foregroundStyle(.secondary)
                }
            }
            HStack(alignment: .firstTextBaseline, spacing: 1) {
                Text(window.map { "\($0.pct)" } ?? "–").font(DS.font(26, .bold)).monospacedDigit()
                Text("%").font(DS.font(13, .semibold))
            }
            .foregroundStyle(col)
            .shadow(color: col.opacity(0.45), radius: 8)
            GradientBar(value: Double(window?.pct ?? 0) / 100, colors: [col.opacity(0.7), col], height: 5)
        }
        .glass(12)
    }
}

struct PanelView: View {
    @Bindable private var settings = AppSettings.shared
    private var engine = Engine.shared

    var body: some View {
        VStack(spacing: 14) {
            header
            notices
            DevicePreview(image: engine.preview).padding(.vertical, 4)
            sessionCard
            HStack(spacing: 10) {
                LimitCard(title: "5 Stunden", window: engine.usage?.five)
                LimitCard(title: "Woche", window: engine.usage?.week)
            }
            brightness
            footer
        }
        .padding(16)
        .frame(width: 340)
        .background(NeonBackground())
        .tint(settings.appTheme.accent.color)
        .preferredColorScheme(.dark)
    }

    private var header: some View {
        HStack(spacing: 11) {
            AppMark(size: 26, spinning: engine.session != nil)
            VStack(alignment: .leading, spacing: 1) {
                Text("Claude Display").font(DS.font(15, .bold))
                Text(subtitle).font(DS.font(11)).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            StatusChip(status: engine.status)
        }
    }

    @ViewBuilder private var notices: some View {
        let claude = ClaudeConnect.shared, updater = Updater.shared
        if !claude.isConnected && claude.state != .checking {
            notice("person.crop.circle.badge.exclamationmark", "Claude nicht verbunden", "Verbinden") { SettingsWindow.show() }
        } else if updater.appUpdate, let v = updater.latest?.appVersion {
            notice("arrow.down.circle.fill", "Update \(v) verfügbar", "Installieren") { Task { await Updater.shared.installApp() } }
        } else if updater.firmwareUpdate(device: engine.device?.version), let v = updater.latest?.firmwareVersion {
            notice("cpu", "Firmware \(v) verfügbar", "Aufspielen") { Task { await Updater.shared.installFirmware() } }
        }
    }

    private func notice(_ icon: String, _ text: String, _ action: String, run: @escaping () -> Void) -> some View {
        HStack(spacing: 10) {
            Image(systemName: icon).foregroundStyle(settings.appTheme.gradient)
            Text(text).font(DS.font(12, .medium))
            Spacer()
            Button(action, action: run).buttonStyle(NeonButtonStyle()).controlSize(.small)
        }
        .glass(10, highlight: settings.appTheme.accent.color)
    }

    private var subtitle: String {
        if settings.host.isEmpty { return "Noch nicht eingerichtet" }
        if case .error(let msg) = engine.status { return msg }
        if engine.status == .standby { return "Ein anderer Rechner zeigt gerade an" }
        if let d = engine.device { return "\(d.ip) · Firmware \(d.version)" }
        return settings.host
    }

    @ViewBuilder private var sessionCard: some View {
        if let s = engine.session {
            let t = settings.appTheme
            VStack(alignment: .leading, spacing: 9) {
                HStack {
                    Circle().fill(t.accent2.color).frame(width: 6, height: 6).shadow(color: t.accent2.color, radius: 4)
                    Text(s.name).font(DS.font(13, .semibold)).lineLimit(1)
                    Spacer()
                    Text(Renderer.fmtTimer(s.secs)).font(DS.font(12, .medium)).monospacedDigit().foregroundStyle(.secondary)
                }
                if !s.plan.isEmpty {
                    GradientBar(value: Double(s.done) / Double(s.plan.count), colors: [t.accent.color, t.accent2.color])
                    HStack(alignment: .top) {
                        Text(s.currentStep ?? "Alle Schritte erledigt").font(DS.font(12)).foregroundStyle(.secondary).lineLimit(2)
                        Spacer()
                        Text("\(s.done)/\(s.plan.count)").font(DS.font(12, .bold)).monospacedDigit()
                    }
                } else if let p = s.prompt {
                    Text(p).font(DS.font(12)).foregroundStyle(.secondary).lineLimit(2)
                }
            }
            .glass(12, highlight: t.accent.color)
        } else if settings.host.isEmpty {
            Button("Display einrichten") { SetupWindow.show() }
                .buttonStyle(NeonButtonStyle())
        }
    }

    private var brightness: some View {
        let night = settings.isNight()
        let binding = night ? $settings.nightBrightness : $settings.dayBrightness
        return HStack(spacing: 10) {
            Image(systemName: night ? "moon.stars.fill" : "sun.max.fill").foregroundStyle(.secondary).frame(width: 18)
            Slider(value: binding, in: 0...100) { editing in
                if !editing { Engine.shared.settingsChanged() }
            }
            Text("\(Int(binding.wrappedValue))%").font(DS.font(11, .semibold)).monospacedDigit()
                .foregroundStyle(.secondary).frame(width: 36, alignment: .trailing)
        }
        .glass(10)
    }

    private var footer: some View {
        HStack(spacing: 8) {
            Toggle(isOn: $settings.enabled) { Text("Display ansteuern").font(DS.font(12, .medium)) }
                .toggleStyle(.switch).controlSize(.small)
                .onChange(of: settings.enabled) { Engine.shared.settingsChanged() }
            Spacer()
            IconButton(systemName: "slider.horizontal.3", help: "Einstellungen") { SettingsWindow.show() }
            IconButton(systemName: "power", help: "Beenden") { NSApp.terminate(nil) }
        }
    }
}
