import AppKit
import Observation

/// Updates über GitHub-Releases: App (Installer .pkg) und Display-Firmware (firmware.bin.gz).
/// Das Repository (owner/name) setzt das Build-Skript aus dem Git-Remote in die Info.plist.
@Observable
final class Updater {
    static let shared = Updater()

    struct Release: Equatable {
        let tag: String
        let appVersion: String
        let firmwareVersion: String?
        let notes: String
        let page: URL?
        let appAsset: URL?
        let firmwareAsset: URL?
    }

    private(set) var latest: Release?
    private(set) var checking = false
    private(set) var lastCheck: Date?
    private(set) var message: String?
    private(set) var progress: Double?

    static var repo: String? {
        let r = Bundle.main.infoDictionary?["CDUpdateRepo"] as? String
        return (r?.isEmpty ?? true) ? nil : r
    }
    static var appVersion: String { Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "0" }
    static var bundledFirmware: String { Bundle.main.infoDictionary?["CDFirmwareVersion"] as? String ?? "1.0" }

    var appUpdate: Bool { latest.map { Self.newer($0.appVersion, than: Self.appVersion) } ?? false }
    func firmwareUpdate(device: String?) -> Bool {
        guard let fw = latest?.firmwareVersion, latest?.firmwareAsset != nil, let device else { return false }
        return Self.newer(fw, than: device)
    }

    /// "2.10.0" > "2.9.1"
    static func newer(_ a: String, than b: String) -> Bool {
        let pa = a.trimmingCharacters(in: CharacterSet(charactersIn: "v")).split(separator: ".").map { Int($0) ?? 0 }
        let pb = b.trimmingCharacters(in: CharacterSet(charactersIn: "v")).split(separator: ".").map { Int($0) ?? 0 }
        for i in 0..<max(pa.count, pb.count) {
            let x = i < pa.count ? pa[i] : 0, y = i < pb.count ? pb[i] : 0
            if x != y { return x > y }
        }
        return false
    }

    /// Einmal täglich automatisch (falls in den Einstellungen aktiviert)
    func checkIfDue() {
        guard AppSettings.shared.autoUpdate else { return }
        if let l = lastCheck, Date().timeIntervalSince(l) < 86_400 { return }
        Task { await check() }
    }

    @MainActor
    func check() async {
        guard let repo = Self.repo else { message = "Kein Update-Kanal in dieser Version"; return }
        checking = true; message = nil
        defer { checking = false; lastCheck = Date() }
        var req = URLRequest(url: URL(string: "https://api.github.com/repos/\(repo)/releases/latest")!,
                             cachePolicy: .reloadIgnoringLocalCacheData)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        req.setValue("ClaudeDisplay/\(Self.appVersion)", forHTTPHeaderField: "User-Agent")
        guard let (data, resp) = try? await URLSession.shared.data(for: req),
              (resp as? HTTPURLResponse)?.statusCode == 200,
              let j = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            message = "GitHub gerade nicht erreichbar"; return
        }
        let tag = j["tag_name"] as? String ?? ""
        let assets = (j["assets"] as? [[String: Any]]) ?? []
        func asset(_ match: (String) -> Bool) -> URL? {
            assets.first { match(($0["name"] as? String ?? "").lowercased()) }
                .flatMap { ($0["browser_download_url"] as? String).flatMap(URL.init) }
        }
        // Versionsdatei über die API laden – der normale Download-Link kann aus
        // GitHubs Zwischenspeicher einen veralteten Stand liefern.
        var fwVersion: String?
        if let api = assets.first(where: { ($0["name"] as? String) == "release.json" })?["url"] as? String,
           let apiURL = URL(string: api) {
            var mreq = URLRequest(url: apiURL, cachePolicy: .reloadIgnoringLocalCacheData)
            mreq.setValue("application/octet-stream", forHTTPHeaderField: "Accept")
            mreq.setValue("ClaudeDisplay/\(Self.appVersion)", forHTTPHeaderField: "User-Agent")
            if let (md, _) = try? await URLSession.shared.data(for: mreq),
               let m = try? JSONSerialization.jsonObject(with: md) as? [String: Any] {
                fwVersion = m["firmware"] as? String
            }
        }
        latest = Release(tag: tag, appVersion: tag.trimmingCharacters(in: CharacterSet(charactersIn: "v")),
                         firmwareVersion: fwVersion, notes: j["body"] as? String ?? "",
                         page: (j["html_url"] as? String).flatMap(URL.init),
                         appAsset: asset { $0.hasSuffix(".pkg") },
                         firmwareAsset: asset { $0 == "firmware.bin.gz" })
        message = appUpdate ? "Version \(latest!.appVersion) ist verfügbar" : "Die App ist aktuell"
    }

    /// Lädt den Installer und öffnet ihn – er beendet die App und startet die neue Version.
    @MainActor
    func installApp() async {
        guard let url = latest?.appAsset else { return }
        message = "Lade Update …"; progress = 0
        guard let file = await download(url) else { message = "Download fehlgeschlagen"; progress = nil; return }
        progress = nil
        message = "Installer geöffnet"
        NSWorkspace.shared.open(file)
    }

    /// Lädt die Firmware von GitHub und spielt sie per WLAN aufs Display.
    @MainActor
    func installFirmware() async {
        guard let url = latest?.firmwareAsset else { return }
        message = "Lade Firmware …"; progress = 0
        guard let file = await download(url), let data = try? Data(contentsOf: file) else {
            message = "Download fehlgeschlagen"; progress = nil; return
        }
        progress = nil
        message = nil
        Engine.shared.updateFirmware(data: data, filename: "firmware.bin.gz")
    }

    @MainActor
    private func download(_ url: URL) async -> URL? {
        var req = URLRequest(url: url)
        req.setValue("ClaudeDisplay/\(Self.appVersion)", forHTTPHeaderField: "User-Agent")
        guard let (tmp, resp) = try? await URLSession.shared.download(for: req),
              (resp as? HTTPURLResponse)?.statusCode == 200 else { return nil }
        let dest = FileManager.default.temporaryDirectory.appendingPathComponent(url.lastPathComponent)
        try? FileManager.default.removeItem(at: dest)
        try? FileManager.default.moveItem(at: tmp, to: dest)
        progress = 1
        return dest
    }
}
