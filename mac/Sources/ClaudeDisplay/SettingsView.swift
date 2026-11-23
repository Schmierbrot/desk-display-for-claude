import SwiftUI
import UniformTypeIdentifiers

enum SettingsPage: String, CaseIterable, Identifiable {
    case display = "Display", claude = "Claude", appearance = "Darstellung", updates = "Updates", general = "Allgemein"
    var id: String { rawValue }
    var icon: String {
        switch self {
        case .display: return "display"
        case .claude: return "person.crop.circle"
        case .appearance: return "paintpalette"
        case .updates: return "arrow.triangle.2.circlepath"
        case .general: return "gearshape"
        }
    }
}

struct SettingsView: View {
    @State private var page: SettingsPage = .display
    @Bindable private var settings = AppSettings.shared
    private var updater = Updater.shared

    @ViewBuilder static func content(_ page: SettingsPage) -> some View {
        switch page {
        case .display: DisplayPage()
        case .claude: ClaudePage()
        case .appearance: AppearancePage()
        case .updates: UpdatesPage()
        case .general: GeneralPage()
        }
    }

    var body: some View {
        HStack(spacing: 0) {
            sidebar
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    Text(page.rawValue).font(DS.font(26, .bold)).padding(.bottom, 2)
                    Self.content(page)
                }
                .padding(.horizontal, 30).padding(.top, 10).padding(.bottom, 30)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .scrollIndicators(.never)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)   // Größe gibt das Fenster vor
        .background(NeonBackground(theme: settings.appTheme))   // reicht hinter die Titelleiste
        .tint(settings.appTheme.accent.color)
        .preferredColorScheme(.dark)
        .appTextScale()
    }

    private var sidebar: some View {
        VStack(alignment: .leading, spacing: 4) {
            HStack(spacing: 10) {
                AppMark(size: 22)
                Text("Claude Display").font(DS.font(14, .bold))
            }
            .padding(.horizontal, 12).padding(.top, 14).padding(.bottom, 20)
            ForEach(SettingsPage.allCases) { p in
                Button { page = p } label: {
                    HStack(spacing: 10) {
                        Image(systemName: p.icon).frame(width: 18)
                        Text(p.rawValue).font(DS.font(13, page == p ? .semibold : .regular))
                        Spacer()
                        if p == .updates && updater.appUpdate {
                            Circle().fill(settings.appTheme.accent2.color).frame(width: 7, height: 7)
                        }
                    }
                    .padding(.horizontal, 12).padding(.vertical, 9)
                    .background(
                        RoundedRectangle(cornerRadius: 10, style: .continuous)
                            .fill(page == p ? AnyShapeStyle(settings.appTheme.gradient.opacity(0.35)) : AnyShapeStyle(Color.clear))
                    )
                    .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous)
                        .strokeBorder(.white.opacity(page == p ? 0.18 : 0), lineWidth: 0.8))
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer()
            Text("Version \(Updater.appVersion)").font(DS.font(11)).foregroundStyle(.secondary).padding(.horizontal, 12).padding(.bottom, 16)
        }
        .padding(10)
        .frame(width: DS.w(210))
        .background(Color.black.opacity(0.25).ignoresSafeArea())
    }
}

/// Zeile: Beschriftung links, Bedienelement rechts
struct Row<Content: View>: View {
    let label: String
    var hint: String? = nil
    @ViewBuilder let content: Content
    var body: some View {
        HStack(alignment: .center, spacing: 14) {
            VStack(alignment: .leading, spacing: 2) {
                Text(label).font(DS.font(13, .medium))
                if let hint { Text(hint).font(DS.font(11)).foregroundStyle(.secondary) }
            }
            Spacer(minLength: 12)
            content
        }
    }
}

struct Card<Content: View>: View {
    let title: String
    @ViewBuilder let content: Content
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            SectionLabel(text: title)
            VStack(alignment: .leading, spacing: 14) { content }
                .frame(maxWidth: .infinity, alignment: .leading)
                .glass(16)
        }
    }
}

// MARK: - Display

struct DisplayPage: View {
    @Bindable private var settings = AppSettings.shared
    private var engine = Engine.shared
    @State private var searching = false
    @State private var hostField = AppSettings.shared.host

    var body: some View {
        HStack(alignment: .center, spacing: 22) {
            DevicePreview(image: engine.preview, size: 170)
            VStack(alignment: .leading, spacing: 10) {
                StatusChip(status: engine.status)
                Text(statusText).font(DS.font(13)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        Card(title: "Verbindung") {
            Row(label: "Display ansteuern") { Toggle("", isOn: $settings.enabled).toggleStyle(.switch).labelsHidden() }
            Row(label: "IP-Adresse") {
                HStack {
                    TextField("z. B. 192.168.1.50", text: $hostField).textFieldStyle(.roundedBorder).frame(width: 160)
                        .onSubmit(apply)
                    Button("Übernehmen", action: apply).buttonStyle(NeonButtonStyle(prominent: false))
                        .disabled(hostField == settings.host)
                    Button { search() } label: {
                        if searching { ProgressView().controlSize(.small) } else { Text("Suchen") }
                    }
                    .buttonStyle(NeonButtonStyle(prominent: false))
                }
            }
        }
        Card(title: "Helligkeit") {
            slider("Tagsüber", $settings.dayBrightness, "sun.max.fill")
            Row(label: "Nachts dimmen") { Toggle("", isOn: $settings.nightMode).toggleStyle(.switch).labelsHidden() }
            if settings.nightMode {
                slider("Nachts", $settings.nightBrightness, "moon.stars.fill")
                Row(label: "Nachtzeit") {
                    HStack {
                        Picker("", selection: $settings.nightStart) { hours }.labelsHidden().frame(width: 90)
                        Text("bis").foregroundStyle(.secondary)
                        Picker("", selection: $settings.nightEnd) { hours }.labelsHidden().frame(width: 90)
                    }
                }
            }
            Text("Gilt auch, wenn kein Rechner läuft – das Display merkt sich den Plan.")
                .font(DS.font(11)).foregroundStyle(.secondary)
        }
        .onChange(of: settings.nightMode) { Engine.shared.settingsChanged() }
        .onChange(of: settings.nightStart) { Engine.shared.settingsChanged() }
        .onChange(of: settings.nightEnd) { Engine.shared.settingsChanged() }
        if let d = engine.device {
            Card(title: "Gerät") {
                info("Firmware", d.version)
                info("IP-Adresse", d.ip)
                info("WLAN-Signal", "\(d.rssi) dBm · \(signalWord(d.rssi))")
                info("Läuft seit", Renderer.fmtReset(TimeInterval(d.uptime)))
                info("Zeigt gerade", d.owner == settings.hostId ? "diesen Mac" : d.owner == 0 ? "eigene Uhr" : "einen anderen Rechner")
            }
        }
    }

    private var hours: some View {
        ForEach(0..<24, id: \.self) { Text(String(format: "%02d:00", $0)).tag($0) }
    }

    private func slider(_ title: String, _ value: Binding<Double>, _ icon: String) -> some View {
        Row(label: title) {
            HStack {
                Image(systemName: icon).foregroundStyle(.secondary)
                Slider(value: value, in: 0...100) { e in if !e { Engine.shared.settingsChanged() } }.frame(width: 220)
                Text("\(Int(value.wrappedValue))%").monospacedDigit().frame(width: 40, alignment: .trailing)
            }
        }
    }

    private func info(_ k: String, _ v: String) -> some View {
        Row(label: k) { Text(v).font(DS.font(13)).foregroundStyle(.secondary).textSelection(.enabled) }
    }

    private var statusText: String {
        switch engine.status {
        case .shown: return "Das Display zeigt die Claude-Sitzungen dieses Macs."
        case .standby: return "Ein anderer Rechner mit aktiver Claude-Sitzung hat gerade Vorrang. Danach übernimmt dieser Mac wieder."
        case .error(let m): return "\(m). Prüfe, ob das Display Strom hat und im WLAN ist."
        case .off: return settings.host.isEmpty ? "Noch kein Display eingerichtet." : "Pausiert – das Display zeigt seine eigene Uhr."
        case .updating: return "Firmware wird aktualisiert …"
        case .starting: return "Verbinde mit \(settings.host) …"
        }
    }

    private func apply() {
        settings.host = hostField.trimmingCharacters(in: .whitespaces)
        settings.setupDone = !settings.host.isEmpty
        Engine.shared.settingsChanged()
    }

    private func search() {
        searching = true
        Task {
            let found = await Discovery.scan()
            await MainActor.run {
                searching = false
                if let d = found.first(where: { $0.kind == .ready }) { hostField = d.ip; apply() }
            }
        }
    }

    private func signalWord(_ rssi: Int) -> String {
        rssi > -55 ? "sehr gut" : rssi > -67 ? "gut" : rssi > -75 ? "mäßig" : "schwach"
    }
}

// MARK: - Claude

struct ClaudePage: View {
    @Bindable private var settings = AppSettings.shared
    private var claude = ClaudeConnect.shared

    var body: some View {
        Card(title: "Konto") {
            ClaudeConnectView()
        }
        Card(title: "Nutzungslimit") {
            Row(label: "Live-Nutzungslimit", hint: "Etwa jede Minute über deinen Claude-Code-Login.") {
                Toggle("", isOn: $settings.liveUsage).toggleStyle(.switch).labelsHidden()
            }
        }
        Card(title: "Sitzungen") {
            Row(label: "Cloud-Sitzungen anzeigen", hint: "Auch Claude Code im Browser oder in der Cloud – über deinen Claude-Code-Login. Sitzungen auf diesem Rechner haben Vorrang.") {
                Toggle("", isOn: $settings.cloudSessions).toggleStyle(.switch).labelsHidden()
            }
        }
    }
}

/// Status und Aktionen der Claude-Verbindung (auch im Einrichtungs-Assistenten)
struct ClaudeConnectView: View {
    private var claude = ClaudeConnect.shared
    @State private var code = ""

    var body: some View {
        let t = AppSettings.shared.appTheme
        let good = Color(nsColor: AppSettings.shared.displayTheme.good.ns)
        VStack(alignment: .leading, spacing: 14) {
            HStack(spacing: 14) {
                ZStack {
                    Circle().fill(t.gradient.opacity(0.25)).frame(width: 46, height: 46)
                    Image(systemName: claude.isConnected ? "checkmark" : "person.fill")
                        .font(DS.sys(size: 19, weight: .semibold))
                        .foregroundStyle(claude.isConnected ? good : .white)
                }
                VStack(alignment: .leading, spacing: 3) {
                    Text(title).font(DS.font(15, .semibold))
                    Text(subtitle).font(DS.font(12)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }
                Spacer()
                action
            }
            if case .loggingIn = claude.state {
                VStack(alignment: .leading, spacing: 10) {
                    HStack(spacing: 8) {
                        ProgressView().controlSize(.small)
                        Text("Melde dich im Browser bei Claude an …").font(DS.font(12.5))
                        Spacer()
                        if claude.loginURL != nil {
                            Button("Seite erneut öffnen") { claude.openLoginPage() }.buttonStyle(NeonButtonStyle(prominent: false))
                        }
                    }
                    if claude.needsCode {
                        HStack {
                            TextField("Code aus dem Browser einfügen", text: $code).textFieldStyle(.roundedBorder)
                            Button("Bestätigen") { claude.submitCode(code); code = "" }.buttonStyle(NeonButtonStyle())
                        }
                    }
                }
            }
            if case .installing = claude.state, !claude.log.isEmpty {
                Text(claude.log.split(separator: "\n").suffix(5).joined(separator: "\n"))
                    .font(DS.sys(size: 10.5, design: .monospaced)).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(10).background(RoundedRectangle(cornerRadius: 8).fill(.black.opacity(0.3)))
            }
        }
    }

    private var title: String {
        switch claude.state {
        case .checking: return "Prüfe Claude …"
        case .notInstalled: return "Claude Code fehlt"
        case .installing: return "Claude Code wird installiert …"
        case .loggedOut: return "Nicht angemeldet"
        case .loggingIn: return "Anmeldung läuft"
        case .connected(let plan): return plan.map { "Verbunden · \($0)" } ?? "Verbunden"
        case .failed(let m): return m
        }
    }

    private var subtitle: String {
        switch claude.state {
        case .notInstalled: return "Die App liest Sitzungen und Limit über die offizielle Claude-Code-App. Sie wird mit einem Klick installiert."
        case .loggedOut: return "Melde dich mit deinem eigenen Claude-Konto an (Pro oder Max)."
        case .connected: return "Sitzungen und Nutzungslimit kommen aus deinem Claude-Code-Login."
        case .installing: return "Offizieller Installer von claude.ai"
        default: return "Claude Code"
        }
    }

    @ViewBuilder private var action: some View {
        switch claude.state {
        case .notInstalled, .failed: Button("Claude Code installieren") { claude.install() }.buttonStyle(NeonButtonStyle())
        case .loggedOut: Button("Mit Claude anmelden") { claude.login() }.buttonStyle(NeonButtonStyle())
        case .loggingIn: Button("Abbrechen") { claude.cancel() }.buttonStyle(NeonButtonStyle(prominent: false))
        case .connected: Button("Abmelden") { claude.logout() }.buttonStyle(NeonButtonStyle(prominent: false))
        default: ProgressView().controlSize(.small)
        }
    }
}

// MARK: - Darstellung

/// Beispieldaten für die Vorschau der Vorlagen
enum Sample {
    static let session = SessionInfo(name: "Mein Projekt", secs: 754,
                                     plan: [PlanItem(text: "a", status: "completed"), PlanItem(text: "b", status: "completed"),
                                            PlanItem(text: "Tests schreiben", status: "in_progress"), PlanItem(text: "d", status: "pending")],
                                     prompt: nil, busyCount: 1)
    static let usage = Usage(five: UsageWindow(pct: 42, reset: Date().addingTimeInterval(7200)),
                             week: UsageWindow(pct: 67, reset: Date().addingTimeInterval(170_000)), stale: nil)
    static func preview(_ t: DisplayTheme, active: Bool = true) -> CGImage {
        Renderer.render(session: active ? session : nil, usage: usage, tick: 3, theme: t).image
    }
}

struct AppearancePage: View {
    @Bindable private var settings = AppSettings.shared
    @State private var previews: [String: CGImage] = [:]

    var body: some View {
        Card(title: "Display – Vorlage") {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 150), spacing: 14)], spacing: 14) {
                ForEach(Themes.presets) { p in
                    themeTile(id: p.id, name: p.name, image: previews[p.id])
                }
                themeTile(id: "custom", name: "Eigene", image: previews["custom"])
            }
        }
        if settings.displayThemeId == "custom" { displayEditor }

        Card(title: "Display – Layout") {
            HStack(spacing: 14) {
                ForEach(LayoutPresets.all) { p in layoutTile(p) }
                if !LayoutPresets.all.contains(where: { $0.layout == settings.displayLayout }) {
                    layoutTile(LayoutPreset(id: "custom", name: "Eigenes", layout: settings.displayLayout))
                }
            }
            HStack(spacing: 12) {
                Button { LayoutEditorWindow.show() } label: { Label("Layout-Editor öffnen", systemImage: "rectangle.3.group") }
                    .buttonStyle(NeonButtonStyle())
                Text("Elemente frei verschieben, skalieren, ausblenden und eigene Texte hinzufügen.")
                    .font(DS.font(11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }

        Card(title: "App – Farben") {
            HStack(spacing: 12) {
                ForEach(Themes.presets) { p in appChip(id: p.id, name: p.name, theme: p.app) }
                appChip(id: "custom", name: "Eigene", theme: settings.appCustom)
            }
            if settings.appThemeId == "custom" {
                Divider().opacity(0.3)
                colorRow("Akzent", $settings.appCustom.accent)
                colorRow("Akzent 2 (Verlauf)", $settings.appCustom.accent2)
                colorRow("Hintergrund", $settings.appCustom.background)
                colorRow("Karten", $settings.appCustom.surface)
                Menu("Aus Vorlage übernehmen") {
                    ForEach(Themes.presets) { p in Button(p.name) { settings.appCustom = p.app } }
                }
                .frame(width: 200)
            }
        }
        .onAppear(perform: renderPreviews)
        .onChange(of: settings.displayCustom) { renderPreviews() }
    }

    private var displayEditor: some View {
        Card(title: "Eigenes Display-Design") {
            Row(label: "Titel oben", hint: "Text neben dem Logo") {
                TextField("Titel", text: $settings.displayCustom.title).textFieldStyle(.roundedBorder).frame(width: 200)
            }
            Row(label: "Logo") {
                Picker("", selection: $settings.displayCustom.logo) {
                    ForEach(LogoStyle.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: 300)
            }
            Divider().opacity(0.3)
            colorRow("Akzent", $settings.displayCustom.accent)
            colorRow("Akzent 2 (Verlauf)", $settings.displayCustom.accent2)
            colorRow("Text", $settings.displayCustom.text)
            colorRow("Text gedämpft", $settings.displayCustom.muted)
            colorRow("Text schwach", $settings.displayCustom.dim)
            colorRow("Rahmen", $settings.displayCustom.edge)
            colorRow("Balken-Hintergrund", $settings.displayCustom.track)
            colorRow("Gut (unter 60 %)", $settings.displayCustom.good)
            colorRow("Achtung (60–85 %)", $settings.displayCustom.warn)
            colorRow("Kritisch (über 85 %)", $settings.displayCustom.bad)
            Menu("Aus Vorlage übernehmen") {
                ForEach(Themes.presets) { p in Button(p.name) { settings.displayCustom = p.display } }
            }
            .frame(width: 200)
            Text("Tipp: Dunkle Grautöne zeigt das Display heller als hier – Rahmen und Balken-Hintergrund lieber dunkel wählen.")
                .font(DS.font(11)).foregroundStyle(.secondary)
        }
    }

    private func layoutTile(_ p: LayoutPreset) -> some View {
        let selected = settings.displayLayout == p.layout
        return Button {
            if p.id == "custom" { LayoutEditorWindow.show() } else { settings.displayLayout = p.layout }
        } label: {
            VStack(spacing: 8) {
                Image(decorative: Renderer.render(session: Sample.session, usage: Sample.usage, tick: 3, layout: p.layout).image, scale: 1)
                    .resizable().interpolation(.high)
                    .frame(width: 112, height: 112)
                    .clipShape(RoundedRectangle(cornerRadius: 9, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 9, style: .continuous)
                        .strokeBorder(selected ? AnyShapeStyle(settings.appTheme.gradient) : AnyShapeStyle(Color.white.opacity(0.1)),
                                      lineWidth: selected ? 2.5 : 1))
                Text(p.name).font(DS.font(12, selected ? .bold : .medium))
            }
        }
        .buttonStyle(.plain)
    }

    private func colorRow(_ label: String, _ c: Binding<RGB>) -> some View {
        Row(label: label) {
            HStack(spacing: 8) {
                Text("#" + c.wrappedValue.hex).font(DS.sys(size: 11, design: .monospaced)).foregroundStyle(.secondary)
                ColorPicker("", selection: RGB.binding(c), supportsOpacity: false).labelsHidden()
            }
        }
    }

    private func themeTile(id: String, name: String, image: CGImage?) -> some View {
        let selected = settings.displayThemeId == id
        return Button {
            if id == "custom" && settings.displayThemeId != "custom" {
                settings.displayCustom = settings.displayTheme      // aktuelles Design als Startpunkt
            }
            settings.displayThemeId = id
            Engine.shared.settingsChanged()
        } label: {
            VStack(spacing: 8) {
                Group {
                    if let image { Image(decorative: image, scale: 1).resizable().interpolation(.high) } else { Color.black }
                }
                .frame(width: 130, height: 130)
                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                .overlay(RoundedRectangle(cornerRadius: 10, style: .continuous)
                    .strokeBorder(selected ? AnyShapeStyle(settings.appTheme.gradient) : AnyShapeStyle(Color.white.opacity(0.1)),
                                  lineWidth: selected ? 2.5 : 1))
                .shadow(color: selected ? settings.appTheme.accent.color.opacity(0.5) : .clear, radius: 10)
                Text(name).font(DS.font(12, selected ? .bold : .medium))
            }
        }
        .buttonStyle(.plain)
    }

    private func appChip(id: String, name: String, theme: AppTheme) -> some View {
        let selected = settings.appThemeId == id
        return Button {
            if id == "custom" && settings.appThemeId != "custom" { settings.appCustom = settings.appTheme }
            settings.appThemeId = id
        } label: {
            VStack(spacing: 6) {
                Circle().fill(LinearGradient(colors: [theme.accent.color, theme.accent2.color], startPoint: .topLeading, endPoint: .bottomTrailing))
                    .frame(width: 34, height: 34)
                    .overlay(Circle().strokeBorder(.white.opacity(selected ? 0.9 : 0.15), lineWidth: selected ? 2.5 : 1))
                    .shadow(color: theme.accent.color.opacity(selected ? 0.7 : 0), radius: 8)
                Text(name).font(DS.font(11, selected ? .bold : .regular))
            }
        }
        .buttonStyle(.plain)
    }

    private func renderPreviews() {
        var p: [String: CGImage] = [:]
        for t in Themes.presets { p[t.id] = Sample.preview(t.display) }
        p["custom"] = Sample.preview(settings.displayCustom)
        previews = p
    }
}

// MARK: - Updates

struct UpdatesPage: View {
    @Bindable private var settings = AppSettings.shared
    private var updater = Updater.shared
    private var engine = Engine.shared
    @State private var showFirmwareFile = false

    var body: some View {
        Card(title: "App") {
            Row(label: "Installiert", hint: updater.latest.map { "Neueste: \($0.appVersion)" }) {
                Text(Updater.appVersion).font(DS.font(13, .semibold))
            }
            HStack {
                if updater.appUpdate {
                    Button("Auf \(updater.latest!.appVersion) aktualisieren") { Task { await updater.installApp() } }
                        .buttonStyle(NeonButtonStyle())
                }
                Button { Task { await updater.check() } } label: {
                    if updater.checking { ProgressView().controlSize(.small) } else { Text("Nach Updates suchen") }
                }
                .buttonStyle(NeonButtonStyle(prominent: !updater.appUpdate))
                if let page = updater.latest?.page {
                    Button("Auf GitHub ansehen") { NSWorkspace.shared.open(page) }.buttonStyle(NeonButtonStyle(prominent: false))
                }
            }
            if let m = updater.message { Text(m).font(DS.font(12)).foregroundStyle(.secondary) }
            Row(label: "Automatisch prüfen", hint: "Einmal täglich auf GitHub") {
                Toggle("", isOn: $settings.autoUpdate).toggleStyle(.switch).labelsHidden()
            }
        }
        Card(title: "Display-Firmware") {
            Row(label: "Auf dem Display") { Text(engine.device?.version ?? "unbekannt").foregroundStyle(.secondary) }
            Row(label: "Neueste") { Text(updater.latest?.firmwareVersion ?? Updater.bundledFirmware).foregroundStyle(.secondary) }
            HStack {
                if updater.firmwareUpdate(device: engine.device?.version) {
                    Button("Firmware \(updater.latest!.firmwareVersion!) aufspielen") { Task { await updater.installFirmware() } }
                        .buttonStyle(NeonButtonStyle())
                } else if let url = FirmwarePage.bundled, engine.device != nil {
                    Button("Mitgelieferte Firmware \(Updater.bundledFirmware) aufspielen") {
                        if let data = try? Data(contentsOf: url) { Engine.shared.updateFirmware(data: data, filename: "firmware.bin.gz") }
                    }
                    .buttonStyle(NeonButtonStyle(prominent: false))
                }
                Button("Datei wählen …", action: FirmwarePage.pickFile).buttonStyle(NeonButtonStyle(prominent: false))
            }
            .disabled(engine.status == .updating)
            if let p = engine.updateProgress {
                GradientBar(value: p, colors: [settings.appTheme.accent.color, settings.appTheme.accent2.color])
            }
            if let m = engine.updateMessage {
                HStack(spacing: 8) {
                    if engine.status == .updating { ProgressView().controlSize(.small) }
                    Text(m).foregroundStyle(.secondary)
                }
            }
            Text("Das Update läuft über WLAN und dauert etwa 20 Sekunden. Das Display startet danach neu.")
                .font(DS.font(11)).foregroundStyle(.secondary)
        }
    }
}

enum FirmwarePage {
    static var bundled: URL? { Bundle.main.url(forResource: "firmware", withExtension: "bin.gz") }

    static func pickFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [UTType(filenameExtension: "bin") ?? .data, UTType(filenameExtension: "gz") ?? .data]
        panel.allowsMultipleSelection = false
        panel.message = "Firmware-Datei (.bin oder .bin.gz) für das Claude Display wählen"
        guard panel.runModal() == .OK, let url = panel.url, let data = try? Data(contentsOf: url) else { return }
        Engine.shared.updateFirmware(data: data, filename: url.lastPathComponent)
    }
}

// MARK: - Allgemein

struct GeneralPage: View {
    @Bindable private var settings = AppSettings.shared
    @State private var login = LoginItem.enabled
    @State private var loginError: String?

    var body: some View {
        Card(title: "Darstellung der App") {
            HStack(spacing: 12) {
                ModeCard(mode: "menubar", selected: settings.appMode == "menubar") { setMode("menubar") }
                ModeCard(mode: "window", selected: settings.appMode == "window") { setMode("window") }
            }
        }
        Card(title: "Schriftgröße") {
            Picker("", selection: $settings.textScale) {
                Text("Klein").tag(0.9)
                Text("Normal").tag(1.0)
                Text("Groß").tag(1.15)
                Text("Sehr groß").tag(1.3)
            }
            .pickerStyle(.segmented).labelsHidden()
            Text("Gilt für alle Fenster der App. Die Schrift auf dem Display stellst du im Layout-Editor ein.")
                .font(DS.font(11)).foregroundStyle(.secondary)
        }
        Card(title: "Start") {
            Row(label: "Beim Anmelden starten") {
                Toggle("", isOn: $login).toggleStyle(.switch).labelsHidden()
                    .onChange(of: login) { _, on in
                        do { try LoginItem.set(on) } catch {
                            loginError = error.localizedDescription
                            login = LoginItem.enabled
                        }
                    }
            }
            if let loginError { Text(loginError).foregroundStyle(.red).font(DS.font(11)) }
        }
        Card(title: "Über") {
            Row(label: "Version") { Text(Updater.appVersion).foregroundStyle(.secondary) }
            Row(label: "Rechner-Kennung") { Text(String(format: "%08X", settings.hostId)).foregroundStyle(.secondary).textSelection(.enabled) }
            HStack {
                Button("Einrichtungs-Assistent starten") { SetupWindow.show() }.buttonStyle(NeonButtonStyle(prominent: false))
                if let repo = Updater.repo, let url = URL(string: "https://github.com/\(repo)") {
                    Button("Projekt auf GitHub") { NSWorkspace.shared.open(url) }.buttonStyle(NeonButtonStyle(prominent: false))
                }
            }
        }
    }

    private func setMode(_ m: String) {
        settings.appMode = m
        AppMode.apply()
    }
}

/// Auswahlkarte: Menüleiste oder App-Fenster
struct ModeCard: View {
    let mode: String
    let selected: Bool
    let action: () -> Void

    var body: some View {
        let t = AppSettings.shared.appTheme
        Button(action: action) {
            VStack(alignment: .leading, spacing: 10) {
                Image(systemName: mode == "window" ? "macwindow" : "menubar.rectangle")
                    .font(DS.sys(size: 28, weight: .light)).foregroundStyle(t.gradient)
                Text(mode == "window" ? "App-Fenster" : "Menüleiste").font(DS.font(14, .semibold))
                Text(mode == "window" ? "Großes Dashboard mit Dock-Symbol – wie eine normale App."
                                      : "Unauffällig oben in der Menüleiste, ein Klick öffnet das Panel.")
                    .font(DS.font(11.5)).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .glass(16, highlight: selected ? t.accent.color : nil)
            .overlay(alignment: .topTrailing) {
                if selected {
                    Image(systemName: "checkmark.circle.fill").foregroundStyle(t.accent.color).padding(12)
                }
            }
        }
        .buttonStyle(.plain)
    }
}
