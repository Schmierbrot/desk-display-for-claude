import Foundation

struct PlanItem: Equatable {
    var text: String
    var status: String          // pending | in_progress | completed
    var id: String? = nil       // Aufgaben-Nummer bei TaskCreate/TaskUpdate (Claude Code im Terminal)
}

/// Die gerade arbeitende Claude-Code-Sitzung.
struct SessionInfo: Equatable {
    var name: String
    var secs: Int
    var plan: [PlanItem]
    var prompt: String?
    var busyCount: Int

    var done: Int { plan.filter { $0.status == "completed" }.count }
    var currentStep: String? {
        plan.first { $0.status == "in_progress" }?.text ?? plan.first { $0.status == "pending" }?.text
    }
}

struct UsageWindow: Equatable {
    var pct: Int
    var reset: Date?
}

struct Usage: Equatable {
    var five: UsageWindow
    var week: UsageWindow
    var stale: Date?            // gesetzt, wenn der Wert älter als 3 Minuten ist (Zeitpunkt des Stands)
}

// MARK: - Sitzungsprotokoll

/// Liest ein Protokoll (JSONL) inkrementell und spielt den Aufgabenplan nach.
final class TranscriptState {
    let url: URL
    private var offset: UInt64 = 0
    private var rest = Data()
    private(set) var plan: [PlanItem] = []
    private(set) var lastPrompt: String?

    init(url: URL) { self.url = url }

    func update() {
        guard let fh = try? FileHandle(forReadingFrom: url) else { return }
        defer { try? fh.close() }
        let size = (try? fh.seekToEnd()) ?? 0
        if size < offset { offset = 0; rest = Data(); plan = []; lastPrompt = nil }
        guard size > offset else { return }
        try? fh.seek(toOffset: offset)
        guard let data = try? fh.readToEnd() else { return }
        offset += UInt64(data.count)
        var buf = rest + data
        guard let lastNL = buf.lastIndex(of: 0x0A) else { rest = buf; return }
        rest = buf[(lastNL + 1)...]
        buf = buf[..<lastNL]
        for line in buf.split(separator: 0x0A) {
            guard let obj = try? JSONSerialization.jsonObject(with: line) as? [String: Any] else { continue }
            handle(obj)
        }
    }

    /// TaskCreate-Aufrufe, deren Nummer erst im Ergebnis steht: tool_use_id → Index im Plan
    private var pendingCreates: [String: Int] = [:]

    private func handle(_ d: [String: Any]) {
        let type = d["type"] as? String
        if type == "last-prompt" { lastPrompt = d["lastPrompt"] as? String; return }
        guard (d["isSidechain"] as? Bool) != true,
              let msg = d["message"] as? [String: Any],
              let content = msg["content"] as? [[String: Any]] else { return }
        if type == "user" { handleResults(content, d["toolUseResult"] as? [String: Any]); return }
        guard type == "assistant" else { return }
        for c in content where c["type"] as? String == "tool_use" {
            let name = c["name"] as? String ?? ""
            let input = c["input"] as? [String: Any] ?? [:]
            if name == "TaskCreate" {
                let subject = (input["subject"] as? String) ?? (input["description"] as? String) ?? ""
                if plan.allSatisfy({ $0.status == "completed" }) && plan.contains(where: { $0.id != nil }) {
                    plan = []            // neue Aufgabenliste, sobald die alte ganz erledigt ist
                }
                plan.append(PlanItem(text: subject, status: "pending"))
                if let tid = c["id"] as? String { pendingCreates[tid] = plan.count - 1 }
            } else if name == "TaskUpdate" {
                guard let id = Self.idString(input["taskId"]),
                      let i = plan.firstIndex(where: { $0.id == id }) else { continue }
                if let st = input["status"] as? String {
                    if st == "deleted" { plan.remove(at: i); continue }
                    plan[i].status = st
                }
                if let sub = input["subject"] as? String, !sub.isEmpty { plan[i].text = sub }
            } else if name.hasSuffix("update_plan") {
                applyPlanUpdate(input)
            } else if name.hasSuffix("wrap_up"), (input["blocked"] as? Bool) != true {
                // Wartet Claude auf dich (blocked), ist der Plan noch nicht fertig.
                for i in plan.indices { plan[i].status = "completed" }
            } else if name == "TodoWrite", let todos = input["todos"] as? [[String: Any]] {
                plan = todos.map { PlanItem(text: $0["content"] as? String ?? "",
                                            status: $0["status"] as? String ?? "pending") }
            }
        }
    }

    /// Ergebnis von TaskCreate: „Task #3 created successfully: …“ → Nummer merken.
    private func handleResults(_ content: [[String: Any]], _ result: [String: Any]?) {
        for c in content where c["type"] as? String == "tool_result" {
            guard let tid = c["tool_use_id"] as? String, let idx = pendingCreates.removeValue(forKey: tid),
                  idx < plan.count else { continue }
            var id = Self.idString((result?["task"] as? [String: Any])?["id"])
            if id == nil {
                let text = (c["content"] as? String)
                    ?? (c["content"] as? [[String: Any]])?.compactMap { $0["text"] as? String }.joined()
                    ?? ""
                if let r = text.range(of: #"Task #(\S+) created"#, options: .regularExpression) {
                    id = String(text[r].dropFirst(6).dropLast(8))
                }
            }
            if let id { plan[idx].id = id } else if c["is_error"] as? Bool == true { plan.remove(at: idx) }
        }
    }

    private static func idString(_ v: Any?) -> String? {
        if let s = v as? String { return s }
        if let n = v as? NSNumber { return n.stringValue }
        return nil
    }

    private func applyPlanUpdate(_ inp: [String: Any]) {
        if let items = inp["items"] as? [[String: Any]] {
            plan = items.map { PlanItem(text: $0["content"] as? String ?? "",
                                        status: $0["status"] as? String ?? "pending") }
        }
        if (inp["advance"] as? Bool) == true {
            if let cur = plan.firstIndex(where: { $0.status == "in_progress" }) { plan[cur].status = "completed" }
            if let nxt = plan.firstIndex(where: { $0.status == "pending" }) { plan[nxt].status = "in_progress" }
        }
        for n in (inp["done"] as? [Int]) ?? [] where n >= 1 && n <= plan.count {
            plan[n - 1].status = "completed"
        }
        if let n = inp["start"] as? Int {
            for i in plan.indices where plan[i].status == "in_progress" { plan[i].status = "pending" }
            if n >= 1 && n <= plan.count { plan[n - 1].status = "in_progress" }
        }
    }
}

// MARK: - Datenquelle

final class ClaudeData {
    private let home = FileManager.default.homeDirectoryForCurrentUser
    private var transcripts: [String: TranscriptState] = [:]

    private var sessionsDir: URL { home.appendingPathComponent(".claude/sessions") }
    private var projectsDir: URL { home.appendingPathComponent(".claude/projects") }

    /// Sitzung, in der gerade gearbeitet wird (zuletzt aktiv), sonst nil.
    func currentSession() -> SessionInfo? {
        let files = (try? FileManager.default.contentsOfDirectory(at: sessionsDir, includingPropertiesForKeys: nil)) ?? []
        var busy: [[String: Any]] = []
        var anyStatus = false
        for f in files where f.pathExtension == "json" {
            guard let data = try? Data(contentsOf: f),
                  let s = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let pid = (s["pid"] as? NSNumber)?.int32Value, pidAlive(pid) else { continue }
            let status = s["status"] as? String
            if status != nil { anyStatus = true }
            if status != nil && status != "idle" { busy.append(s) }
        }
        // Ältere Claude-Code-Versionen schreiben keinen Status: dann gilt die Sitzung
        // als aktiv, deren Protokoll gerade wächst.
        if !anyStatus, let s = recentlyActiveTranscript() { busy.append(s) }
        busy.sort { ($0["statusUpdatedAt"] as? Double ?? 0) > ($1["statusUpdatedAt"] as? Double ?? 0) }
        guard let s = busy.first else { return nil }

        let id = s["sessionId"] as? String ?? ""
        let st = transcript(id)
        st?.update()
        let cwd = s["cwd"] as? String ?? ""
        let name = (s["name"] as? String).flatMap { $0.isEmpty ? nil : $0 }
            ?? (cwd as NSString).lastPathComponent
        let since = (s["statusUpdatedAt"] as? Double ?? 0) / 1000
        return SessionInfo(name: name.isEmpty ? "Sitzung" : name,
                           secs: max(0, Int(Date().timeIntervalSince1970 - since)),
                           plan: st?.plan ?? [], prompt: st?.lastPrompt, busyCount: busy.count)
    }

    /// Für den Fall ohne Status: jüngstes Protokoll, das in den letzten 20 s geschrieben wurde.
    private var activeSince: [String: Double] = [:]
    private var lastScan = Date.distantPast
    private var lastFound: [String: Any]?

    private func recentlyActiveTranscript() -> [String: Any]? {
        if Date().timeIntervalSince(lastScan) < 3 { return lastFound }
        lastScan = Date()
        let fm = FileManager.default
        var best: (URL, Date)?
        for dir in (try? fm.contentsOfDirectory(at: projectsDir, includingPropertiesForKeys: nil)) ?? [] {
            for f in (try? fm.contentsOfDirectory(at: dir, includingPropertiesForKeys: [.contentModificationDateKey])) ?? []
            where f.pathExtension == "jsonl" {
                guard let m = try? f.resourceValues(forKeys: [.contentModificationDateKey]).contentModificationDate else { continue }
                if best == nil || m > best!.1 { best = (f, m) }
            }
        }
        let now = Date().timeIntervalSince1970
        guard let (url, mod) = best, now - mod.timeIntervalSince1970 < 20 else {
            activeSince = [:]; lastFound = nil; return nil
        }
        let id = url.deletingPathExtension().lastPathComponent
        let since = activeSince[id] ?? now
        activeSince = [id: since]
        // Projektordner heißt wie der Pfad, mit „-“ statt „/“ – der letzte Teil reicht als Name.
        let name = url.deletingLastPathComponent().lastPathComponent.split(separator: "-").last.map(String.init) ?? ""
        lastFound = ["sessionId": id, "cwd": name, "statusUpdatedAt": since * 1000]
        return lastFound
    }

    private func pidAlive(_ pid: Int32) -> Bool {
        kill(pid, 0) == 0 || errno == EPERM
    }

    private func transcript(_ sessionId: String) -> TranscriptState? {
        if let t = transcripts[sessionId] { return t }
        let dirs = (try? FileManager.default.contentsOfDirectory(at: projectsDir, includingPropertiesForKeys: nil)) ?? []
        for dir in dirs {
            let f = dir.appendingPathComponent(sessionId + ".jsonl")
            if FileManager.default.fileExists(atPath: f.path) {
                let t = TranscriptState(url: f)
                transcripts[sessionId] = t
                return t
            }
        }
        return nil
    }

    // MARK: Nutzungslimit
    //
    // Live von der API, so oft sie es erlaubt (etwa jede Minute; sie lehnt
    // häufigere Abfragen mit HTTP 429 ab). Ein Wert wird höchstens 10 Minuten
    // gezeigt, ab 3 Minuten mit "Stand"-Uhrzeit. Läuft ein Fenster ab, zeigt es 0 %.

    static let maxAge: TimeInterval = 600
    static let staleAfter: TimeInterval = 180
    private struct Sample { var five: UsageWindow; var week: UsageWindow; var fetched: Date }
    private var lastLive: Sample? = ClaudeData.loadSample()
    private var nextFetch = Date.distantPast

    /// `fetch`: nur der Rechner, der gerade aufs Display zeichnet, fragt die API ab.
    func usage(live: Bool, fetch: Bool = true) -> Usage? {
        guard live else { return nil }
        let now = Date()
        if fetch, now >= nextFetch {
            switch usageFromAPI() {
            case .ok(let s):
                lastLive = s
                ClaudeData.saveSample(s)
                nextFetch = now.addingTimeInterval(60)
            case .rateLimited:
                nextFetch = now.addingTimeInterval(30)
            case .failed(let why):
                nextFetch = now.addingTimeInterval(30)
                Log.write("Limit: \(why)")
            }
        }
        guard var s = lastLive, now.timeIntervalSince(s.fetched) < ClaudeData.maxAge else { return nil }

        // abgelaufene Fenster: neues Fenster beginnt bei 0 %, sofort neu abfragen
        for kp in [\Sample.five, \Sample.week] {
            if let r = s[keyPath: kp].reset, r <= now {
                s[keyPath: kp] = UsageWindow(pct: 0, reset: nil)
                if s.fetched < r { nextFetch = min(nextFetch, now) }
            }
        }
        let stale = now.timeIntervalSince(s.fetched) > ClaudeData.staleAfter ? s.fetched : nil
        return Usage(five: s.five, week: s.week, stale: stale)
    }

    private enum Fetch { case ok(Sample), rateLimited, failed(String) }

    /// Fragt das Limit mit dem Claude-Code-Login ab (wird bei Bedarf erneuert).
    private func usageFromAPI() -> Fetch {
        let token: String
        switch ClaudeLogin.accessToken() {
        case .success(let t): token = t
        case .failure(let e): return .failed(e.message)
        }

        var req = URLRequest(url: URL(string: "https://api.anthropic.com/api/oauth/usage")!)
        req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        req.setValue("oauth-2025-04-20", forHTTPHeaderField: "anthropic-beta")
        req.setValue(ClaudeLogin.userAgent, forHTTPHeaderField: "User-Agent")
        req.timeoutInterval = 10
        guard let (data, resp) = syncRequest(req), let http = resp as? HTTPURLResponse else {
            return .failed("keine Antwort von der API")
        }
        if http.statusCode == 429 {
            return .rateLimited
        }
        guard http.statusCode == 200, let d = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else {
            return .failed("HTTP \(http.statusCode)")
        }
        func win(_ key: String) -> UsageWindow {
            let w = d[key] as? [String: Any] ?? [:]
            let pct = Int(((w["utilization"] as? Double) ?? 0).rounded())
            return UsageWindow(pct: pct, reset: parseDate(w["resets_at"] as? String))
        }
        return .ok(Sample(five: win("five_hour"), week: win("seven_day"), fetched: Date()))
    }

    // letzter Live-Wert übersteht einen kurzen Neustart (gilt trotzdem nur 10 Minuten)
    private static func saveSample(_ s: Sample) {
        UserDefaults.standard.set([
            "f": s.five.pct, "fr": s.five.reset?.timeIntervalSince1970 ?? 0,
            "w": s.week.pct, "wr": s.week.reset?.timeIntervalSince1970 ?? 0,
            "t": s.fetched.timeIntervalSince1970,
        ], forKey: "lastUsage")
    }

    private static func loadSample() -> Sample? {
        guard let d = UserDefaults.standard.dictionary(forKey: "lastUsage"),
              let t = d["t"] as? Double else { return nil }
        func date(_ k: String) -> Date? { (d[k] as? Double).flatMap { $0 > 0 ? Date(timeIntervalSince1970: $0) : nil } }
        return Sample(five: UsageWindow(pct: d["f"] as? Int ?? 0, reset: date("fr")),
                      week: UsageWindow(pct: d["w"] as? Int ?? 0, reset: date("wr")),
                      fetched: Date(timeIntervalSince1970: t))
    }

    private func parseDate(_ s: String?) -> Date? {
        guard let s else { return nil }
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = f.date(from: s) { return d }
        f.formatOptions = [.withInternetDateTime]
        return f.date(from: s)
    }
}

/// Synchroner HTTP-Aufruf (wir laufen ohnehin auf einem Hintergrund-Thread).
func syncRequest(_ req: URLRequest) -> (Data, URLResponse)? {
    let sem = DispatchSemaphore(value: 0)
    var result: (Data, URLResponse)?
    URLSession.shared.dataTask(with: req) { data, resp, _ in
        if let data, let resp { result = (data, resp) }
        sem.signal()
    }.resume()
    _ = sem.wait(timeout: .now() + req.timeoutInterval + 2)
    return result
}

/// Kleine Protokolldatei: ~/Library/Logs/Claude Display.log
enum Log {
    private static let url = FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Logs/Claude Display.log")
    private static let fmt: DateFormatter = { let f = DateFormatter(); f.dateFormat = "yyyy-MM-dd HH:mm:ss"; return f }()

    static func write(_ msg: String) {
        let line = "\(fmt.string(from: Date()))  \(msg)\n"
        if let h = try? FileHandle(forWritingTo: url) {
            h.seekToEndOfFile(); h.write(line.data(using: .utf8)!); try? h.close()
        } else {
            try? line.data(using: .utf8)!.write(to: url)
        }
    }
}

// MARK: - Claude-Code-Login

struct LoginError: Error { let message: String }

/// Liest den Login der Claude-Code-CLI aus dem Schlüsselbund und erneuert ihn,
/// wenn er abläuft – so wie die CLI selbst – und schreibt ihn zurück.
enum ClaudeLogin {
    static let service = "Claude Code-credentials"
    static let clientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e"
    static let tokenURL = URL(string: "https://platform.claude.com/v1/oauth/token")!
    static let userAgent = "ClaudeDisplay/1.0"
    private static var retryAfter = Date.distantPast

    static func accessToken(force: Bool = false) -> Result<String, LoginError> {
        guard var creds = read(), var oauth = creds["claudeAiOauth"] as? [String: Any],
              let token = oauth["accessToken"] as? String else {
            return .failure(LoginError(message: "kein Claude-Code-Login im Schlüsselbund (einmal `claude auth login`)"))
        }
        let expiresAt = (oauth["expiresAt"] as? Double ?? 0) / 1000
        if !force && expiresAt - Date().timeIntervalSince1970 > 300 { return .success(token) }
        if Date() < retryAfter { return .failure(LoginError(message: "Login abgelaufen, Erneuerung pausiert")) }

        // Erneuern
        guard let refresh = oauth["refreshToken"] as? String else {
            return .failure(LoginError(message: "Login abgelaufen und nicht erneuerbar"))
        }
        let scopes = (oauth["scopes"] as? [String]) ?? []
        var req = URLRequest(url: tokenURL)
        req.httpMethod = "POST"
        req.timeoutInterval = 20
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        req.setValue(userAgent, forHTTPHeaderField: "User-Agent")
        req.httpBody = try? JSONSerialization.data(withJSONObject: [
            "grant_type": "refresh_token", "refresh_token": refresh,
            "client_id": clientId, "scope": scopes.joined(separator: " "),
        ])
        guard let (data, resp) = syncRequest(req), let http = resp as? HTTPURLResponse else {
            retryAfter = Date().addingTimeInterval(60)
            return .failure(LoginError(message: "Login-Erneuerung: keine Antwort"))
        }
        guard http.statusCode == 200, let d = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let newToken = d["access_token"] as? String, let expiresIn = d["expires_in"] as? Double else {
            // vielleicht hat die CLI gerade selbst erneuert – beim nächsten Mal neu lesen
            retryAfter = Date().addingTimeInterval(http.statusCode == 400 ? 600 : 120)
            let body = String(data: data, encoding: .utf8) ?? ""
            Log.write("Login-Erneuerung fehlgeschlagen: HTTP \(http.statusCode) \(body.prefix(120))")
            return .failure(LoginError(message: http.statusCode == 400
                ? "Login ungültig – bitte `claude auth login` im Terminal" : "Login-Erneuerung: HTTP \(http.statusCode)"))
        }
        let now = Date().timeIntervalSince1970 * 1000
        oauth["accessToken"] = newToken
        if let r = d["refresh_token"] as? String { oauth["refreshToken"] = r }
        oauth["expiresAt"] = Int(now + expiresIn * 1000)
        if let sc = d["scope"] as? String, !sc.isEmpty { oauth["scopes"] = sc.split(separator: " ").map(String.init) }
        if let re = d["refresh_token_expires_in"] as? Double { oauth["refreshTokenExpiresAt"] = Int(now + re * 1000) }
        creds["claudeAiOauth"] = oauth
        if write(creds) {
            Log.write("Claude-Code-Login erneuert, gültig für \(Int(expiresIn / 60)) min")
        } else {
            Log.write("Login erneuert, aber Schlüsselbund nicht beschreibbar")
        }
        return .success(newToken)
    }

    private static func run(_ args: [String], stdin: String? = nil) -> (Int32, String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        p.arguments = args
        let out = Pipe(), err = Pipe(), inp = Pipe()
        p.standardOutput = out
        p.standardError = err
        if stdin != nil { p.standardInput = inp }
        guard (try? p.run()) != nil else { return (-1, "") }
        if let stdin {
            inp.fileHandleForWriting.write(stdin.data(using: .utf8)!)
            try? inp.fileHandleForWriting.close()
        }
        let data = out.fileHandleForReading.readDataToEndOfFile()
        let errData = err.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        let errText = String(data: errData, encoding: .utf8) ?? ""
        return (p.terminationStatus == 0 && errText.isEmpty ? 0 : max(1, p.terminationStatus),
                String(data: data, encoding: .utf8) ?? "")
    }

    private static func read() -> [String: Any]? {
        let (st, out) = run(["find-generic-password", "-s", service, "-w"])
        guard st == 0, let d = out.data(using: .utf8) else { return nil }
        return try? JSONSerialization.jsonObject(with: d) as? [String: Any]
    }

    private static func account() -> String? {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        p.arguments = ["find-generic-password", "-s", service]
        let out = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        guard (try? p.run()) != nil else { return nil }
        let text = String(data: out.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8) ?? ""
        p.waitUntilExit()
        guard let r = text.range(of: "\"acct\"<blob>=\"") else { return nil }
        return String(text[r.upperBound...].prefix { $0 != "\"" })
    }

    /// Schreibt den Login zurück – über stdin, damit er nicht in der Prozessliste auftaucht.
    private static func write(_ creds: [String: Any]) -> Bool {
        guard let acct = account(),
              let json = try? JSONSerialization.data(withJSONObject: creds, options: [.withoutEscapingSlashes]) else { return false }
        let hex = json.map { String(format: "%02x", $0) }.joined()
        let (st, _) = run(["-i"], stdin: "add-generic-password -U -a \"\(acct)\" -s \"\(service)\" -X \(hex)\n")
        return st == 0
    }
}
