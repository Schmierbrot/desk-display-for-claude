import AppKit
import Observation

enum EngineStatus: Equatable {
    case starting, off, shown, standby, updating
    case error(String)

    var title: String {
        switch self {
        case .starting: return "Verbinde …"
        case .off: return "Pausiert"
        case .shown: return "Verbunden"
        case .standby: return "Anderer Rechner"
        case .updating: return "Firmware-Update"
        case .error: return "Offline"
        }
    }
}

/// Herzstück: liest jede Sekunde die Claude-Daten, zeichnet und schickt ans Display.
@Observable
final class Engine {
    static let shared = Engine()

    private(set) var status: EngineStatus = .starting
    private(set) var preview: CGImage?
    private(set) var session: SessionInfo?
    private(set) var usage: Usage?
    private(set) var device: DeviceInfo?
    private(set) var updateProgress: Double?
    private(set) var updateMessage: String?

    @ObservationIgnored private let queue = DispatchQueue(label: "claude-display.engine", qos: .utility)
    @ObservationIgnored private var timer: DispatchSourceTimer?
    @ObservationIgnored private let data = ClaudeData()
    @ObservationIgnored private let client: DisplayClient
    @ObservationIgnored private var tick = 0
    @ObservationIgnored private var lastBrightness: Int?
    @ObservationIgnored private var lastTheme: DisplayTheme?
    @ObservationIgnored private var lastInfo = Date.distantPast
    @ObservationIgnored private var backoffUntil = Date.distantPast
    @ObservationIgnored private var paused = false

    private init() {
        let s = AppSettings.shared
        client = DisplayClient(host: s.host, hostId: s.hostId)
    }

    func start() {
        guard timer == nil else { return }
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now(), repeating: 1.0, leeway: .milliseconds(50))
        t.setEventHandler { [weak self] in self?.step() }
        t.resume()
        timer = t
    }

    /// Nach Änderungen an Helligkeit/Adresse sofort neu anwenden.
    func settingsChanged() {
        queue.async { [weak self] in
            guard let self else { return }
            self.lastBrightness = nil
            self.lastTheme = nil
            self.lastInfo = .distantPast
            self.backoffUntil = .distantPast
        }
    }

    private func step() {
        let s = AppSettings.shared
        let host = s.host, enabled = s.enabled, live = s.liveUsage, brightness = s.brightness()
        let sess = data.currentSession()
        let use = data.usage(live: live, fetch: enabled && status != .standby)
        tick += 1
        let canvas = Renderer.render(session: sess, usage: use, tick: tick)
        let image = canvas.image

        var st: EngineStatus
        var dev: DeviceInfo?? = nil      // nil = unverändert lassen
        if paused {
            st = .updating
        } else if !enabled || host.isEmpty {
            client.close()
            st = .off
        } else if Date() < backoffUntil {
            st = status
        } else {
            if client.host != host { client.close(); client.host = host; lastInfo = .distantPast }
            do {
                let r = try client.push(rgb: canvas.rgb, priority: sess != nil ? 2 : 1)
                st = r == .shown ? .shown : .standby
                if r == .shown {
                    if brightness != lastBrightness {
                        client.setBrightness(brightness, day: Int(s.dayBrightness), night: s.nightMode ? Int(s.nightBrightness) : Int(s.dayBrightness),
                                             nightStart: s.nightStart, nightEnd: s.nightEnd)
                        lastBrightness = brightness
                    }
                    let theme = s.displayTheme
                    if theme != lastTheme { client.setTheme(theme); lastTheme = theme }
                } else {
                    lastBrightness = nil
                    lastTheme = nil
                }
            } catch {
                st = .error(error.localizedDescription)
                lastBrightness = nil
                lastTheme = nil
                backoffUntil = Date().addingTimeInterval(3)
            }
            if Date().timeIntervalSince(lastInfo) > 15 {
                lastInfo = Date()
                dev = .some(client.info())
            }
        }
        if st != status { Log.write("Status: \(st)") }
        DispatchQueue.main.async {
            self.status = st
            self.preview = image
            if self.session != sess { self.session = sess }
            if self.usage != use { self.usage = use }
            if let dev { self.device = dev }
        }
    }

    // MARK: Firmware

    func updateFirmware(data fw: Data, filename: String) {
        updateMessage = "Lade Firmware hoch …"
        updateProgress = 0
        queue.async { [weak self] in
            guard let self else { return }
            self.paused = true
            self.client.close()
            self.client.uploadFirmware(fw, filename: filename, progress: { p in
                DispatchQueue.main.async { self.updateProgress = p }
            }, completion: { result in
                switch result {
                case .failure(let e):
                    self.finishUpdate("Fehlgeschlagen: \(e.localizedDescription)")
                case .success:
                    DispatchQueue.main.async { self.updateMessage = "Display startet neu …"; self.updateProgress = nil }
                    self.queue.async { self.waitForReboot() }
                }
            })
        }
    }

    private func waitForReboot() {
        Thread.sleep(forTimeInterval: 4)
        for _ in 0..<40 {
            if let info = client.info(), info.uptime < 60 {
                DispatchQueue.main.async { self.device = info }
                return finishUpdate("Fertig – Firmware \(info.version) läuft.")
            }
            Thread.sleep(forTimeInterval: 2)
        }
        finishUpdate("Display meldet sich nicht zurück. Bitte Stromversorgung prüfen.")
    }

    private func finishUpdate(_ msg: String) {
        queue.async {
            self.paused = false
            self.lastBrightness = nil
            self.lastInfo = .distantPast
        }
        DispatchQueue.main.async {
            self.updateMessage = msg
            self.updateProgress = nil
        }
    }
}
