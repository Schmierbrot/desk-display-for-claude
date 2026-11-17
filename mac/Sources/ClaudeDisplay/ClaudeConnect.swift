import AppKit
import Observation

/// Verbindung zu Claude: findet bzw. installiert die offizielle Claude-Code-CLI und
/// meldet den Benutzer über `claude auth login` an. Die App liest danach nur den
/// Login der CLI (Sitzungen und Nutzungslimit).
@Observable
final class ClaudeConnect {
    static let shared = ClaudeConnect()

    enum State: Equatable {
        case checking
        case notInstalled
        case installing
        case loggedOut
        case loggingIn
        case connected(plan: String?)
        case failed(String)
    }

    private(set) var state: State = .checking
    /// Ausgabe des Installers bzw. der Anmeldung (letzte Zeilen)
    private(set) var log = ""
    /// Die CLI wartet auf einen Code aus dem Browser
    private(set) var needsCode = false
    private(set) var loginURL: URL?

    @ObservationIgnored private var process: Process?
    @ObservationIgnored private var input: FileHandle?

    var isConnected: Bool { if case .connected = state { return true }; return false }

    /// Mögliche Orte der CLI (offizieller Installer, Homebrew, npm)
    static func cliPath() -> String? {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        let candidates = ["\(home)/.local/bin/claude", "\(home)/.claude/local/claude",
                          "/opt/homebrew/bin/claude", "/usr/local/bin/claude"]
        return candidates.first { FileManager.default.isExecutableFile(atPath: $0) }
    }

    // MARK: Status

    func refresh() {
        state = .checking
        DispatchQueue.global().async {
            let result: State
            if let cli = Self.cliPath() {
                let out = Self.run(cli, ["auth", "status"])
                if let d = out.data(using: .utf8),
                   let j = try? JSONSerialization.jsonObject(with: d) as? [String: Any],
                   j["loggedIn"] as? Bool == true {
                    result = .connected(plan: (j["subscriptionType"] as? String).map { $0.capitalized })
                } else {
                    result = .loggedOut
                }
            } else {
                result = .notInstalled
            }
            DispatchQueue.main.async { self.state = result }
        }
    }

    // MARK: Installation (offizieller Installer von claude.ai)

    func install() {
        state = .installing
        log = ""
        start(executable: "/bin/bash", ["-c", "curl -fsSL https://claude.ai/install.sh | bash"]) { ok in
            if ok && Self.cliPath() != nil { self.refresh() }
            else { self.state = .failed("Installation fehlgeschlagen") }
        }
    }

    // MARK: Anmeldung

    func login() {
        guard let cli = Self.cliPath() else { state = .notInstalled; return }
        state = .loggingIn
        needsCode = false
        loginURL = nil
        log = ""
        // über `script` bekommt die CLI ein Terminal, wie bei einer normalen Anmeldung
        start(executable: "/usr/bin/script", ["-q", "/dev/null", cli, "auth", "login"]) { _ in
            self.needsCode = false
            self.refresh()
        }
    }

    /// Code aus dem Browser an die wartende CLI geben
    func submitCode(_ code: String) {
        let c = code.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !c.isEmpty else { return }
        input?.write((c + "\n").data(using: .utf8)!)
        needsCode = false
    }

    func cancel() {
        process?.terminate()
        process = nil
        refresh()
    }

    func logout() {
        guard let cli = Self.cliPath() else { return }
        DispatchQueue.global().async {
            _ = Self.run(cli, ["auth", "logout"])
            DispatchQueue.main.async { self.refresh() }
        }
    }

    func openLoginPage() {
        if let loginURL { NSWorkspace.shared.open(loginURL) }
    }

    // MARK: Prozesse

    private func start(executable: String, _ args: [String], done: @escaping (Bool) -> Void) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: executable)
        p.arguments = args
        var env = ProcessInfo.processInfo.environment
        env["PATH"] = "\(FileManager.default.homeDirectoryForCurrentUser.path)/.local/bin:/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin"
        p.environment = env
        let out = Pipe(), inp = Pipe()
        p.standardOutput = out
        p.standardError = out
        p.standardInput = inp
        input = inp.fileHandleForWriting
        out.fileHandleForReading.readabilityHandler = { [weak self] h in
            let data = h.availableData
            guard !data.isEmpty, let text = String(data: data, encoding: .utf8) else { return }
            DispatchQueue.main.async { self?.consume(text) }
        }
        p.terminationHandler = { [weak self] proc in
            out.fileHandleForReading.readabilityHandler = nil
            DispatchQueue.main.async {
                self?.process = nil
                done(proc.terminationStatus == 0)
            }
        }
        do {
            try p.run()
            process = p
        } catch {
            state = .failed(error.localizedDescription)
        }
    }

    private func consume(_ text: String) {
        let clean = text.replacingOccurrences(of: "\u{1B}\\[[0-9;?]*[A-Za-z]", with: "", options: .regularExpression)
                        .replacingOccurrences(of: "\r", with: "\n")
        log = String((log + clean).suffix(4000))
        if loginURL == nil, let r = clean.range(of: "https://[^\\s]+", options: .regularExpression) {
            loginURL = URL(string: String(clean[r]))
        }
        if clean.localizedCaseInsensitiveContains("paste code") { needsCode = true }
    }

    private static func run(_ exe: String, _ args: [String]) -> String {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: exe)
        p.arguments = args
        let out = Pipe()
        p.standardOutput = out
        p.standardError = Pipe()
        guard (try? p.run()) != nil else { return "" }
        let data = out.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return String(data: data, encoding: .utf8) ?? ""
    }
}
