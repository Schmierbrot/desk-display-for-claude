import Foundation

/// Cloud-Sitzungen von Claude Code (im Browser, in der Cloud) – sie laufen nicht auf diesem
/// Rechner, deshalb gibt es keine lokalen Dateien. Abgefragt über den Claude-Code-Login, auf
/// einem eigenen Hintergrund-Takt, damit der Sekundentakt fürs Display nie aufs Netz wartet.
///
/// Inoffizielle Schnittstelle (wie beim Nutzungslimit): ändert sie sich, fehlen nur die Cloud-Sitzungen.
final class CloudSessions {
    static let shared = CloudSessions()

    private let queue = DispatchQueue(label: "claude-display.cloud", qos: .utility)
    private var timer: DispatchSourceTimer?
    private let lock = NSLock()
    private var current: [SessionInfo] = []

    private struct Tracked {
        let state = TranscriptState(cloud: ())
        var seen = Set<String>()          // bekannte event_ids
        var loaded = false
        var since = Date()
        var title = ""
        var lastEvent = Date.distantPast
    }
    private var tracked: [String: Tracked] = [:]
    private var nextList = Date.distantPast
    private var pausedUntil = Date.distantPast
    private var runningIds: [String] = []    // IDs der gerade arbeitenden Sitzungen, neueste zuerst

    /// Zum Testen: auch lokale Sitzungen mit Fernsteuerung („bridge“) einbeziehen
    var includeBridge = false

    static let base = "https://api.anthropic.com"

    func start() {
        guard timer == nil else { return }
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + 1, repeating: 4, leeway: .milliseconds(500))
        t.setEventHandler { [weak self] in self?.tick() }
        t.resume()
        timer = t
    }

    /// Gerade arbeitende Cloud-Sitzungen (neueste zuerst)
    func running() -> [SessionInfo] {
        lock.lock(); defer { lock.unlock() }
        return current
    }

    /// Ein Durchgang (auch direkt aufrufbar, z. B. für --cloud-test)
    func tick() {
        guard AppSettings.shared.cloudSessions else { publish([]); return }
        let now = Date()
        guard now >= pausedUntil else { return }
        // Liste: alle 8 s, wenn etwas läuft alle 4 s
        if now >= nextList {
            guard let list = request("/v1/code/sessions?limit=25") else { return }
            nextList = now.addingTimeInterval(runningIds.isEmpty ? 8 : 4)
            var ids: [(String, Date)] = []
            for s in (list["data"] as? [[String: Any]]) ?? [] {
                guard let id = s["id"] as? String else { continue }
                let kind = s["environment_kind"] as? String ?? ""
                if kind == "bridge" && !includeBridge { continue }          // läuft lokal – lesen wir aus den Dateien
                let status = s["status"] as? String, bucket = s["status_bucket"] as? String
                let worker = s["worker_status"] as? String
                let last = Self.date(s["last_event_at"] as? String) ?? .distantPast
                let working = status != "archived" && (bucket == "working" || worker == "running")
                guard working, now.timeIntervalSince(last) < 15 * 60 else { continue }
                var t = tracked[id] ?? Tracked()
                t.title = (s["title"] as? String) ?? ""
                t.lastEvent = last
                tracked[id] = t
                ids.append((id, last))
            }
            let active = Set(ids.map(\.0))
            for id in tracked.keys where !active.contains(id) { tracked[id] = nil }   // fertig → nächstes Mal neue Laufzeit
            runningIds = ids.sorted { $0.1 > $1.1 }.map(\.0)
        }
        for id in runningIds { loadEvents(id) }
        publish(runningIds.compactMap { id in
            guard let t = tracked[id] else { return nil }
            return SessionInfo(name: t.title.isEmpty ? "Cloud-Sitzung" : t.title,
                               secs: max(0, Int(now.timeIntervalSince(t.since))),
                               plan: t.state.plan, prompt: t.state.lastPrompt, busyCount: runningIds.count)
        })
    }

    private func publish(_ s: [SessionInfo]) {
        lock.lock(); current = s; lock.unlock()
    }

    /// Neue Ereignisse holen (beim ersten Mal bis zu 600 zurück) und dem Parser in zeitlicher Reihenfolge geben.
    private func loadEvents(_ id: String) {
        guard var t = tracked[id] else { return }
        var fresh: [[String: Any]] = []
        var cursor: String?
        let pages = t.loaded ? 3 : 6
        page: for _ in 0..<pages {
            var path = "/v1/code/sessions/\(id)/events?limit=100&sort_order=desc"
            if let cursor, let c = cursor.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) { path += "&cursor=\(c)" }
            guard let r = request(path), let data = r["data"] as? [[String: Any]] else { return }
            for e in data {
                guard let eid = e["event_id"] as? String else { continue }
                if t.seen.contains(eid) { break page }
                fresh.append(e)
            }
            guard let next = r["next_cursor"] as? String, !next.isEmpty, data.count == 100 else { break }
            cursor = next
        }
        for e in fresh.reversed() {
            if let eid = e["event_id"] as? String { t.seen.insert(eid) }
            guard let p = e["payload"] as? [String: Any] else { continue }
            let before = t.state.lastPrompt
            t.state.feed(p)
            // Laufzeit wie lokal: ab der letzten Eingabe (Beginn der aktuellen Runde)
            if t.state.lastPrompt != before, let d = Self.date(e["created_at"] as? String) { t.since = d }
        }
        t.loaded = true
        tracked[id] = t
    }

    // MARK: HTTP

    private func request(_ path: String, retried: Bool = false) -> [String: Any]? {
        let token: String
        switch ClaudeLogin.accessToken() {
        case .success(let tk): token = tk
        case .failure: pausedUntil = Date().addingTimeInterval(60); return nil
        }
        var req = URLRequest(url: URL(string: Self.base + path)!)
        req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        req.setValue("2023-06-01", forHTTPHeaderField: "anthropic-version")
        req.setValue("ccr-byoc-2025-07-29", forHTTPHeaderField: "anthropic-beta")
        req.setValue(ClaudeLogin.userAgent, forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 10
        guard let (data, resp) = syncRequest(req), let http = resp as? HTTPURLResponse else { return nil }
        switch http.statusCode {
        case 200:
            return try? JSONSerialization.jsonObject(with: data) as? [String: Any]
        case 401 where !retried:
            _ = ClaudeLogin.accessToken(force: true)
            return request(path, retried: true)
        case 429:
            pausedUntil = Date().addingTimeInterval(60)
        default:
            // z. B. Konto ohne Cloud-Sitzungen oder geänderte Schnittstelle: eine Weile Ruhe
            Log.write("Cloud-Sitzungen: HTTP \(http.statusCode)")
            pausedUntil = Date().addingTimeInterval(10 * 60)
        }
        return nil
    }

    static func date(_ s: String?) -> Date? {
        guard let s else { return nil }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: s)
    }
}
