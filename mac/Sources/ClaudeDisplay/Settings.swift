import Foundation
import Observation

/// Dauerhafte Einstellungen der App (UserDefaults).
@Observable
final class AppSettings {
    static let shared = AppSettings()
    @ObservationIgnored private let d = UserDefaults.standard

    var host: String { didSet { d.set(host, forKey: "host") } }
    var enabled: Bool { didSet { d.set(enabled, forKey: "enabled") } }
    var dayBrightness: Double { didSet { d.set(dayBrightness, forKey: "dayBrightness") } }
    var nightBrightness: Double { didSet { d.set(nightBrightness, forKey: "nightBrightness") } }
    var nightMode: Bool { didSet { d.set(nightMode, forKey: "nightMode") } }
    var nightStart: Int { didSet { d.set(nightStart, forKey: "nightStart") } }
    var nightEnd: Int { didSet { d.set(nightEnd, forKey: "nightEnd") } }
    var liveUsage: Bool { didSet { d.set(liveUsage, forKey: "liveUsage") } }
    /// Cloud-Sitzungen (Claude Code im Browser/in der Cloud) mit anzeigen
    var cloudSessions: Bool { didSet { d.set(cloudSessions, forKey: "cloudSessions") } }
    /// Mehrere arbeitende Sitzungen abwechselnd zeigen
    var rotateSessions: Bool { didSet { d.set(rotateSessions, forKey: "rotateSessions") } }
    /// "menubar" = nur Menüleiste, "window" = App-Fenster mit Dock-Symbol
    var appMode: String { didSet { d.set(appMode, forKey: "appMode") } }
    var autoUpdate: Bool { didSet { d.set(autoUpdate, forKey: "autoUpdate") } }
    /// Schriftgröße der App: 0,9 klein · 1 normal · 1,15 groß · 1,3 sehr groß
    var textScale: Double { didSet { d.set(textScale, forKey: "textScale"); HostedWindow.rescaleAll() } }
    var windowMode: Bool { appMode == "window" }
    /// für MenuBarExtra(isInserted:) – nur im Menüleisten-Modus sichtbar
    var menuBarShown: Bool {
        get { setupDone && !windowMode }
        set { }
    }
    /// Einrichtungs-Assistent abgeschlossen
    var setupDone: Bool { didSet { d.set(setupDone, forKey: "setupDone") } }

    /// Vorlage ("neon", "claude", …) oder "custom" – getrennt für Display und App
    var displayThemeId: String { didSet { d.set(displayThemeId, forKey: "displayThemeId") } }
    var appThemeId: String { didSet { d.set(appThemeId, forKey: "appThemeId") } }
    var displayCustom: DisplayTheme { didSet { save(displayCustom, "displayCustom") } }
    var appCustom: AppTheme { didSet { save(appCustom, "appCustom") } }
    /// Aufbau des Displays (Layout-Editor)
    var displayLayout: DisplayLayout { didSet { save(displayLayout, "displayLayout") } }

    var displayTheme: DisplayTheme {
        displayThemeId == "custom" ? displayCustom : Themes.preset(displayThemeId).display
    }
    var appTheme: AppTheme {
        appThemeId == "custom" ? appCustom : Themes.preset(appThemeId).app
    }

    private func save<T: Encodable>(_ v: T, _ key: String) {
        if let data = try? JSONEncoder().encode(v) { d.set(data, forKey: key) }
    }
    private static func load<T: Decodable>(_ key: String, _ d: UserDefaults) -> T? {
        d.data(forKey: key).flatMap { try? JSONDecoder().decode(T.self, from: $0) }
    }
    /// Eindeutige Kennung dieses Rechners gegenüber dem Display.
    let hostId: UInt32

    private init() {
        d.register(defaults: [
            "host": "", "enabled": true,
            "dayBrightness": 60.0, "nightBrightness": 8.0,
            "nightMode": true, "nightStart": 22, "nightEnd": 7, "liveUsage": true, "cloudSessions": true, "rotateSessions": true,
            "displayThemeId": "neon", "appThemeId": "neon",
            "appMode": "menubar", "autoUpdate": true, "textScale": 1.0,
        ])
        appMode = d.string(forKey: "appMode") ?? "menubar"
        autoUpdate = d.bool(forKey: "autoUpdate")
        let ts = d.double(forKey: "textScale")    // auf die nächste Stufe runden
        textScale = [0.9, 1.0, 1.15, 1.3].min { abs($0 - ts) < abs($1 - ts) } ?? 1.0
        // Wer schon eine Display-Adresse gespeichert hat, braucht den Assistenten nicht
        let stored = d.persistentDomain(forName: Bundle.main.bundleIdentifier ?? "") ?? [:]
        setupDone = stored["setupDone"] as? Bool ?? !((stored["host"] as? String) ?? "").isEmpty
        displayThemeId = d.string(forKey: "displayThemeId") ?? "neon"
        appThemeId = d.string(forKey: "appThemeId") ?? "neon"
        displayCustom = AppSettings.load("displayCustom", d) ?? Themes.preset("neon").display
        appCustom = AppSettings.load("appCustom", d) ?? Themes.preset("neon").app
        displayLayout = (AppSettings.load("displayLayout", d) as DisplayLayout?)?.normalized() ?? LayoutPresets.standard
        host = d.string(forKey: "host") ?? ""
        enabled = d.bool(forKey: "enabled")
        dayBrightness = d.double(forKey: "dayBrightness")
        nightBrightness = d.double(forKey: "nightBrightness")
        nightMode = d.bool(forKey: "nightMode")
        nightStart = d.integer(forKey: "nightStart")
        nightEnd = d.integer(forKey: "nightEnd")
        liveUsage = d.bool(forKey: "liveUsage")
        cloudSessions = d.bool(forKey: "cloudSessions")
        rotateSessions = d.bool(forKey: "rotateSessions")
        if let id = d.object(forKey: "hostId") as? Int, id > 1 {
            hostId = UInt32(truncatingIfNeeded: id)
        } else {
            let id = UInt32.random(in: 2...UInt32.max)
            d.set(Int(id), forKey: "hostId")
            hostId = id
        }
    }

    func isNight(_ date: Date = Date()) -> Bool {
        guard nightMode else { return false }
        let h = Calendar.current.component(.hour, from: date)
        return nightStart > nightEnd ? (h >= nightStart || h < nightEnd)
                                     : (h >= nightStart && h < nightEnd)
    }

    func brightness(_ date: Date = Date()) -> Int {
        Int((isNight(date) ? nightBrightness : dayBrightness).rounded())
    }
}
