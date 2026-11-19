import AppKit
import Observation
import SwiftUI

// MARK: - Suche im WLAN

struct FoundDevice: Identifiable, Equatable {
    enum Kind { case ready, stock }
    let ip: String
    let kind: Kind
    let detail: String
    var id: String { ip }
}

enum Discovery {
    private static let session: URLSession = {
        let c = URLSessionConfiguration.ephemeral
        c.timeoutIntervalForRequest = 1.5
        c.timeoutIntervalForResource = 3
        c.httpMaximumConnectionsPerHost = 2
        return URLSession(configuration: c)
    }()

    /// Alle Adressen der eigenen IPv4-Netze (höchstens das /24 um die eigene Adresse).
    static func candidates() -> [String] {
        var result = Set<String>()
        var ifaddr: UnsafeMutablePointer<ifaddrs>?
        guard getifaddrs(&ifaddr) == 0, let first = ifaddr else { return [] }
        defer { freeifaddrs(ifaddr) }
        for p in sequence(first: first, next: { $0.pointee.ifa_next }) {
            let ifa = p.pointee
            guard let addr = ifa.ifa_addr, addr.pointee.sa_family == UInt8(AF_INET),
                  (ifa.ifa_flags & UInt32(IFF_UP)) != 0, (ifa.ifa_flags & UInt32(IFF_LOOPBACK)) == 0,
                  let maskPtr = ifa.ifa_netmask else { continue }
            let ip = UInt32(bigEndian: addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr.s_addr })
            var mask = UInt32(bigEndian: maskPtr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee.sin_addr.s_addr })
            if (ip >> 16) == 0xA9FE { continue }               // 169.254.x.x
            if mask < 0xFFFFFF00 { mask = 0xFFFFFF00 }           // große Netze: nur das eigene /24
            let net = ip & mask
            let count = ~mask
            guard count > 1 && count < 1024 else { continue }
            for h in 1..<count where net | h != ip {
                let a = net | h
                result.insert("\(a >> 24).\((a >> 16) & 255).\((a >> 8) & 255).\(a & 255)")
            }
        }
        return Array(result)
    }

    private static func json(_ url: String) async -> [String: Any]? {
        guard let u = URL(string: url), let (data, resp) = try? await session.data(from: u),
              (resp as? HTTPURLResponse)?.statusCode == 200 else { return nil }
        return try? JSONSerialization.jsonObject(with: data) as? [String: Any]
    }

    /// Prüft eine Adresse: unsere Firmware oder SmallTV-Ultra mit Original-Firmware?
    static func probe(_ ip: String) async -> FoundDevice? {
        if let d = await json("http://\(ip)/"), d["fw"] as? String == "claude-display" {
            return FoundDevice(ip: ip, kind: .ready, detail: "Claude Display · Firmware \(d["ver"] as? String ?? "1.x")")
        }
        if let d = await json("http://\(ip)/v.json"), let m = d["m"] as? String, m.lowercased().contains("smalltv") {
            return FoundDevice(ip: ip, kind: .stock, detail: "\(m) · Original-Firmware \(d["v"] as? String ?? "")")
        }
        return nil
    }

    /// Sucht per mDNS (claude-display.local) und im ganzen eigenen Netz.
    static func scan() async -> [FoundDevice] {
        var found: [String: FoundDevice] = [:]
        if let ip = DisplayClient.discover(), let d = await probe(ip) { found[ip] = d }
        let ips = candidates().filter { found[$0] == nil }
        await withTaskGroup(of: FoundDevice?.self) { group in
            var it = ips.makeIterator()
            for _ in 0..<48 { if let ip = it.next() { group.addTask { await probe(ip) } } }
            while let r = await group.next() {
                if let r { found[r.ip] = r }
                if let ip = it.next() { group.addTask { await probe(ip) } }
            }
        }
        return found.values.sorted { ($0.kind == .ready ? 0 : 1, $0.ip) < ($1.kind == .ready ? 0 : 1, $1.ip) }
    }
}

// MARK: - Ablauf

@Observable
final class SetupModel {
    enum Page: Int, CaseIterable { case welcome, claude, search, flash, wifi, look, mode, done }

    var page: Page = .welcome
    var devices: [FoundDevice] = []
    var scanning = false
    var selected: FoundDevice?
    var manualIP = ""
    var manualMessage: String?
    var riskAccepted = false
    var flashing = false
    var flashProgress: Double = 0
    var flashError: String?
    var waitingSince: Date?
    var themeId = "neon"
    var mode = AppSettings.shared.appMode
    var startAtLogin = true
    private var waitTask: Task<Void, Never>?

    func startScan() {
        guard !scanning else { return }
        scanning = true
        Task {
            let r = await Discovery.scan()
            await MainActor.run {
                devices = r
                scanning = false
                if selected == nil { selected = r.first }
            }
        }
    }

    func checkManual() {
        let ip = manualIP.trimmingCharacters(in: .whitespaces)
        guard !ip.isEmpty else { return }
        manualMessage = "Prüfe \(ip) …"
        Task {
            let d = await Discovery.probe(ip)
            await MainActor.run {
                if let d {
                    if !devices.contains(d) { devices.insert(d, at: 0) }
                    selected = d
                    manualMessage = nil
                } else {
                    manualMessage = "Unter \(ip) antwortet kein SmallTV-Ultra."
                }
            }
        }
    }

    func next() {
        guard let d = selected else { return }
        if page == .search {
            page = d.kind == .ready ? .look : .flash
            if d.kind == .ready { connect(d.ip) }
        }
    }

    func flash() {
        guard let d = selected, let url = FirmwarePage.bundled, let data = try? Data(contentsOf: url) else {
            flashError = "Keine Firmware in der App enthalten."; return
        }
        flashing = true; flashError = nil; flashProgress = 0
        DisplayClient(host: d.ip, hostId: 0).uploadFirmware(data, filename: "firmware.bin.gz", progress: { p in
            DispatchQueue.main.async { self.flashProgress = p }
        }, completion: { result in
            DispatchQueue.main.async {
                self.flashing = false
                switch result {
                case .success:
                    self.page = .wifi
                    self.waitForDisplay(previousIP: d.ip)
                case .failure(let e):
                    self.flashError = e.localizedDescription
                }
            }
        })
    }

    /// Nach dem Flashen: warten, bis sich das Display mit unserer Firmware im WLAN meldet.
    func waitForDisplay(previousIP: String) {
        waitingSince = Date()
        waitTask?.cancel()
        waitTask = Task {
            var round = 0
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(4))
                var hit: FoundDevice?
                if let d = await Discovery.probe(previousIP), d.kind == .ready { hit = d }
                if hit == nil, let ip = DisplayClient.discover(), let d = await Discovery.probe(ip), d.kind == .ready { hit = d }
                if hit == nil && round % 5 == 4 { hit = await Discovery.scan().first { $0.kind == .ready } }
                round += 1
                if let hit {
                    await MainActor.run {
                        self.selected = hit
                        self.connect(hit.ip)
                        self.page = .look
                    }
                    return
                }
            }
        }
    }

    func connect(_ ip: String) {
        let s = AppSettings.shared
        s.host = ip
        s.enabled = true
        Engine.shared.settingsChanged()
    }

    func applyTheme(_ id: String) {
        themeId = id
        let s = AppSettings.shared
        s.displayThemeId = id
        s.appThemeId = id
        Engine.shared.settingsChanged()
    }

    func finish() {
        let s = AppSettings.shared
        s.appMode = mode
        s.setupDone = true
        if startAtLogin && !LoginItem.enabled { try? LoginItem.set(true) }
        Engine.shared.settingsChanged()
        waitTask?.cancel()
        SetupWindow.close()
        AppMode.apply()
    }
}

// MARK: - Fenster

enum SetupWindow {
    private static var window: NSWindow?

    static func show() {
        if window == nil {
            let w = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 760, height: 560),
                             styleMask: [.titled, .closable, .fullSizeContentView], backing: .buffered, defer: false)
            w.titlebarAppearsTransparent = true
            w.titleVisibility = .hidden
            w.isMovableByWindowBackground = false   // nur oben an der Titelleiste verschieben – sonst zieht jedes Ziehen im Fenster mit
            w.isReleasedWhenClosed = false
            w.contentView = FullWindowHost.make(SetupView(model: SetupModel()))
            w.backgroundColor = AppSettings.shared.appTheme.background.ns
            w.center()
            window = w
        }
        if let window { DockPresence.shown(window) }
        NSApp.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    static func close() {
        window?.close()
        window = nil
    }
}

// MARK: - Oberfläche

struct SetupView: View {
    @Bindable var model: SetupModel
    @Bindable private var settings = AppSettings.shared

    var body: some View {
        VStack(spacing: 0) {
            progress.padding(.top, 6)
            Group {
                switch model.page {
                case .welcome: welcome
                case .claude: claudeStep
                case .search: search
                case .flash: flash
                case .wifi: wifi
                case .look: look
                case .mode: modeStep
                case .done: done
                }
            }
            .frame(maxWidth: 560, maxHeight: .infinity)
            .padding(.horizontal, 40)
            .transition(.opacity.combined(with: .move(edge: .trailing)))
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)   // Größe gibt das Fenster vor
        .background(NeonBackground(theme: settings.appTheme))
        .tint(settings.appTheme.accent.color)
        .preferredColorScheme(.dark)
        .animation(.easeInOut(duration: 0.3), value: model.page)
    }

    private var progress: some View {
        HStack(spacing: 8) {
            ForEach(SetupModel.Page.allCases, id: \.self) { p in
                Capsule()
                    .fill(p.rawValue <= model.page.rawValue ? AnyShapeStyle(settings.appTheme.gradient) : AnyShapeStyle(Color.white.opacity(0.15)))
                    .frame(width: p == model.page ? 28 : 8, height: 8)
            }
        }
    }

    private func title(_ t: String, _ sub: String) -> some View {
        VStack(spacing: 8) {
            Text(t).font(DS.font(28, .bold)).multilineTextAlignment(.center)
            Text(sub).font(DS.font(14)).foregroundStyle(.secondary).multilineTextAlignment(.center)
                .fixedSize(horizontal: false, vertical: true)
        }
    }

    // 1 – Willkommen
    private var welcome: some View {
        VStack(spacing: 26) {
            Spacer()
            AppMark(size: 96, spinning: true)
            title("Willkommen bei Claude Display",
                  "Dein Schreibtisch-Display für Claude Code: laufende Sitzungen, Fortschritt und dein Nutzungslimit – live.")
            Button("Los geht's") { model.page = .claude; ClaudeConnect.shared.refresh() }
                .buttonStyle(NeonButtonStyle())
            Spacer()
        }
    }

    // 2 – Suchen
    private var search: some View {
        VStack(spacing: 18) {
            title("Display finden", model.scanning ? "Ich suche in deinem WLAN …" : "Wähle dein Display aus.")
                .padding(.top, 26)
            ScrollView {
                VStack(spacing: 10) {
                    ForEach(model.devices) { d in deviceRow(d) }
                    if model.scanning {
                        HStack(spacing: 10) { ProgressView().controlSize(.small); Text("Suche läuft …").foregroundStyle(.secondary) }
                            .frame(maxWidth: .infinity).glass(14)
                    } else if model.devices.isEmpty {
                        Text("Nichts gefunden. Ist das Display eingeschaltet und im selben WLAN? Bei der Original-Firmware steht die IP-Adresse unten auf dem Display.")
                            .font(DS.font(12)).foregroundStyle(.secondary).multilineTextAlignment(.center).glass(14)
                    }
                }
            }
            .scrollIndicators(.never)
            .frame(maxHeight: 220)
            HStack {
                TextField("IP-Adresse selbst eingeben", text: $model.manualIP).textFieldStyle(.roundedBorder).frame(width: 220)
                    .onSubmit(model.checkManual)
                Button("Prüfen", action: model.checkManual).buttonStyle(NeonButtonStyle(prominent: false))
                Button("Erneut suchen", action: model.startScan).buttonStyle(NeonButtonStyle(prominent: false)).disabled(model.scanning)
            }
            if let m = model.manualMessage { Text(m).font(DS.font(12)).foregroundStyle(.secondary) }
            Spacer()
            Button("Weiter") { model.next() }.buttonStyle(NeonButtonStyle()).disabled(model.selected == nil)
                .padding(.bottom, 28)
        }
    }

    private func deviceRow(_ d: FoundDevice) -> some View {
        let sel = model.selected == d
        let good = settings.displayTheme.good.color, warn = settings.displayTheme.warn.color
        return Button { model.selected = d } label: {
            HStack(spacing: 14) {
                Image(systemName: "display").font(.system(size: 22)).frame(width: 34)
                VStack(alignment: .leading, spacing: 3) {
                    Text(d.ip).font(DS.font(15, .semibold))
                    Text(d.detail).font(DS.font(12)).foregroundStyle(.secondary)
                }
                Spacer()
                Text(d.kind == .ready ? "Bereit" : "Einrichtung nötig").font(DS.font(11, .bold))
                    .foregroundStyle(d.kind == .ready ? good : warn)
                    .padding(.horizontal, 9).padding(.vertical, 4)
                    .background(Capsule().fill((d.kind == .ready ? good : warn).opacity(0.15)))
                Image(systemName: sel ? "checkmark.circle.fill" : "circle").font(.system(size: 18))
                    .foregroundStyle(sel ? settings.appTheme.accent.color : .secondary)
            }
            .glass(14, highlight: sel ? settings.appTheme.accent.color : nil)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    // 3 – Firmware aufspielen
    private var flash: some View {
        VStack(spacing: 18) {
            title("Claude-Firmware aufspielen",
                  "Dein SmallTV-Ultra läuft noch mit der Original-Firmware. Für die Live-Anzeige bekommt es unsere Firmware – per WLAN, ohne Kabel.")
                .padding(.top, 26)
            VStack(alignment: .leading, spacing: 10) {
                Label("Wetter- und Fotofunktionen der Original-Firmware sind danach weg.", systemImage: "exclamationmark.triangle.fill")
                Label("Zurück zur Original-Firmware kann GeekMagic einen Aktivierungscode verlangen.", systemImage: "key.fill")
                Label("Während des Updates (ca. 20 s) das Display nicht vom Strom trennen.", systemImage: "bolt.slash.fill")
            }
            .font(DS.font(12.5)).foregroundStyle(settings.displayTheme.warn.color)
            .frame(maxWidth: .infinity, alignment: .leading)
            .glass(16, highlight: settings.displayTheme.warn.color)
            Toggle("Ich habe das verstanden", isOn: $model.riskAccepted).toggleStyle(.checkbox)
            if model.flashing {
                GradientBar(value: model.flashProgress, colors: [settings.appTheme.accent.color, settings.appTheme.accent2.color], height: 8)
                Text("Firmware wird übertragen …").font(DS.font(12)).foregroundStyle(.secondary)
            }
            if let e = model.flashError { Text("Fehlgeschlagen: \(e)").font(DS.font(12)).foregroundStyle(settings.displayTheme.bad.color) }
            Spacer()
            HStack {
                Button("Zurück") { model.page = .search }.buttonStyle(NeonButtonStyle(prominent: false)).disabled(model.flashing)
                Button("Jetzt aufspielen", action: model.flash).buttonStyle(NeonButtonStyle())
                    .disabled(!model.riskAccepted || model.flashing)
            }
            .padding(.bottom, 28)
        }
    }

    // 4 – WLAN
    private var wifi: some View {
        VStack(spacing: 20) {
            title("Display ins WLAN bringen",
                  "Die neue Firmware ist drauf. Jetzt braucht das Display einmal deine WLAN-Daten.")
                .padding(.top, 26)
            VStack(alignment: .leading, spacing: 14) {
                step(1, "Öffne auf dem Handy die WLAN-Liste und verbinde dich mit „Claude-Display-Setup“ (ohne Passwort).")
                step(2, "Die Einrichtungsseite öffnet sich von selbst – sonst http://192.168.4.1 im Browser aufrufen.")
                step(3, "Tippe auf „Configure WiFi“, wähle dein WLAN, gib das Passwort ein und speichere.")
            }
            .glass(18)
            HStack(spacing: 10) {
                ProgressView().controlSize(.small)
                Text("Warte auf das Display … es meldet sich hier automatisch.").font(DS.font(13)).foregroundStyle(.secondary)
            }
            Text("Verbindet sich das Display schon von selbst, geht es gleich weiter.")
                .font(DS.font(11)).foregroundStyle(.secondary)
            Spacer()
        }
    }

    private func step(_ n: Int, _ text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Text("\(n)").font(DS.font(13, .bold)).frame(width: 26, height: 26)
                .background(Circle().fill(settings.appTheme.gradient))
            Text(text).font(DS.font(13)).fixedSize(horizontal: false, vertical: true)
        }
    }

    // 5 – Aussehen
    private var look: some View {
        VStack(spacing: 18) {
            title("Wähle deinen Look", "Für Display und App. Später jederzeit unter Einstellungen → Darstellung änderbar.")
                .padding(.top, 26)
            LazyVGrid(columns: Array(repeating: GridItem(.fixed(150), spacing: 12), count: 3), spacing: 12) {
                ForEach(Themes.presets) { p in
                    Button { model.applyTheme(p.id) } label: {
                        VStack(spacing: 6) {
                            Image(decorative: Sample.preview(p.display), scale: 1).resizable().interpolation(.high)
                                .frame(width: 104, height: 104)
                                .clipShape(RoundedRectangle(cornerRadius: 8, style: .continuous))
                                .overlay(RoundedRectangle(cornerRadius: 8, style: .continuous)
                                    .strokeBorder(model.themeId == p.id ? AnyShapeStyle(p.app.gradient) : AnyShapeStyle(Color.white.opacity(0.1)),
                                                  lineWidth: model.themeId == p.id ? 2.5 : 1))
                                .shadow(color: model.themeId == p.id ? p.app.accent.color.opacity(0.6) : .clear, radius: 10)
                            Text(p.name).font(DS.font(12, model.themeId == p.id ? .bold : .regular))
                        }
                    }
                    .buttonStyle(.plain)
                }
            }
            Spacer()
            Button("Weiter") { model.page = .mode }.buttonStyle(NeonButtonStyle()).padding(.bottom, 28)
        }
        .onAppear { model.themeId = settings.displayThemeId == "custom" ? "neon" : settings.displayThemeId }
    }

    // Claude verbinden
    private var claudeStep: some View {
        VStack(spacing: 22) {
            title("Claude verbinden",
                  "Die App zeigt deine eigenen Claude-Code-Sitzungen und dein Nutzungslimit. Dafür meldest du dich einmal mit deinem Claude-Konto an.")
                .padding(.top, 26)
            ClaudeConnectView().glass(18)
            Text("Die Anmeldung läuft über die offizielle Claude-Code-App von Anthropic. Dein Passwort sieht Claude Display nie.")
                .font(DS.font(11.5)).foregroundStyle(.secondary).multilineTextAlignment(.center)
            Spacer()
            HStack {
                if !ClaudeConnect.shared.isConnected {
                    Button("Später") { model.page = .search; model.startScan() }.buttonStyle(NeonButtonStyle(prominent: false))
                }
                Button("Weiter") { model.page = .search; model.startScan() }.buttonStyle(NeonButtonStyle())
                    .disabled(!ClaudeConnect.shared.isConnected)
            }
            .padding(.bottom, 28)
        }
    }

    // Menüleiste oder Fenster
    private var modeStep: some View {
        VStack(spacing: 22) {
            title("Wie möchtest du die App nutzen?", "Lässt sich später unter Einstellungen → Allgemein ändern.")
                .padding(.top, 26)
            HStack(spacing: 14) {
                ModeCard(mode: "menubar", selected: model.mode == "menubar") { model.mode = "menubar" }
                ModeCard(mode: "window", selected: model.mode == "window") { model.mode = "window" }
            }
            Spacer()
            Button("Weiter") { model.page = .done }.buttonStyle(NeonButtonStyle()).padding(.bottom, 28)
        }
    }

    // 6 – Fertig
    private var done: some View {
        VStack(spacing: 20) {
            title("Alles bereit!", "Dein Display zeigt ab jetzt deine Claude-Code-Sitzungen.")
                .padding(.top, 26)
            DevicePreview(image: Engine.shared.preview, size: 170)
            VStack(spacing: 12) {
                Toggle("Beim Anmelden automatisch starten", isOn: $model.startAtLogin)
                Toggle("Live-Nutzungslimit anzeigen", isOn: $settings.liveUsage)
            }
            .toggleStyle(.switch).font(DS.font(13)).frame(width: 340).glass(16)
            Spacer()
            Button("Los geht's", action: model.finish).buttonStyle(NeonButtonStyle()).padding(.bottom, 28)
        }
    }
}
