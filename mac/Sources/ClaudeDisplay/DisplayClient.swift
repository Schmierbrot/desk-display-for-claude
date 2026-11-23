import Foundation

/// Status, den das Display unter GET / meldet.
struct DeviceInfo: Equatable {
    var version: String
    var ip: String
    var rssi: Int
    var uptime: Int
    var brightness: Int
    var mac: String
    var owner: UInt32
}

enum PushResult: Equatable {
    case shown          // Display gehört uns, Bild ist aktuell
    case standby        // anderer Rechner hat Vorrang
}

enum DisplayError: LocalizedError {
    case resolve, connect(Int32), io, protocolError
    var errorDescription: String? {
        switch self {
        case .resolve: return "Display-Adresse nicht gefunden"
        case .connect: return "Display nicht erreichbar"
        case .io: return "Verbindung abgebrochen"
        case .protocolError: return "Unerwartete Antwort vom Display"
        }
    }
}

/// Spricht das Pixel-Protokoll der Firmware (Port 7777) und die HTTP-Endpunkte.
final class DisplayClient {
    var host: String
    let hostId: UInt32
    private var fd: Int32 = -1
    private var last: [UInt8]?          // was gerade auf dem Display steht (RGB)
    private var owner = false

    init(host: String, hostId: UInt32) {
        self.host = host
        self.hostId = hostId
    }

    deinit { close() }

    func close() {
        if fd >= 0 { Darwin.close(fd) }
        fd = -1; last = nil; owner = false
    }

    /// Beim nächsten Mal das ganze Bild schicken (Selbstkorrektur, falls unterwegs etwas verloren ging)
    func forceFullFrame() { last = nil }

    // MARK: Pixel

    func push(rgb: [UInt8], priority: UInt8) throws -> PushResult {
        if fd < 0 { try connect() }
        do {
            return try pushOnce(rgb, priority)
        } catch {
            close()
            throw error
        }
    }

    private func pushOnce(_ rgb: [UInt8], _ prio: UInt8) throws -> PushResult {
        if !owner {
            // Nur anklopfen – Vollbilder schicken wir erst, wenn wir dran sind.
            switch try heartbeat(prio) {
            case "K", "R": try sendRect(rgb, (0, 0, 240, 240), prio, expectFull: true)
            default: return .standby
            }
            return owner ? .shown : .standby
        }
        guard let box = diffBox(last, rgb) else {
            let v = try heartbeat(prio)
            if v == "K" { return .shown }
            owner = false; last = nil
            if v == "R" { try sendRect(rgb, (0, 0, 240, 240), prio, expectFull: true); return .shown }
            return .standby
        }
        let v = try sendRect(rgb, box, prio, expectFull: false)
        if v == "R" { try sendRect(rgb, (0, 0, 240, 240), prio, expectFull: true) }
        return owner ? .shown : .standby
    }

    private func heartbeat(_ prio: UInt8) throws -> Character {
        var msg: [UInt8] = [UInt8(ascii: "H")]
        msg += be32(hostId) + [prio]
        try sendAll(msg)
        return try readVerdict()
    }

    @discardableResult
    private func sendRect(_ rgb: [UInt8], _ box: (Int, Int, Int, Int), _ prio: UInt8, expectFull: Bool) throws -> Character {
        let (x, y, w, h) = box
        var msg = [UInt8](); msg.reserveCapacity(14 + w * h * 2)
        msg.append(UInt8(ascii: "Q"))
        msg += be32(hostId) + [prio] + be16(x) + be16(y) + be16(w) + be16(h)
        for row in y..<(y + h) {
            var i = (row * 240 + x) * 3
            for _ in 0..<w {
                let v = (UInt16(rgb[i]) & 0xF8) << 8 | (UInt16(rgb[i + 1]) & 0xFC) << 3 | UInt16(rgb[i + 2]) >> 3
                msg.append(UInt8(v >> 8)); msg.append(UInt8(v & 0xFF))
                i += 3
            }
        }
        try sendAll(msg)
        let v = try readVerdict()
        switch v {
        case "K": owner = true; last = rgb
        case "B": owner = false; last = nil
        case "R": owner = false; last = nil
            if expectFull { throw DisplayError.protocolError }
        default: throw DisplayError.protocolError
        }
        return v
    }

    /// Kleinstes Rechteck, in dem sich etwas geändert hat (nil = nichts).
    private func diffBox(_ a: [UInt8]?, _ b: [UInt8]) -> (Int, Int, Int, Int)? {
        guard let a else { return (0, 0, 240, 240) }
        var x0 = 240, y0 = 240, x1 = -1, y1 = -1
        a.withUnsafeBufferPointer { pa in
            b.withUnsafeBufferPointer { pb in
                for y in 0..<240 {
                    let rowStart = y * 720
                    for x in 0..<240 {
                        let i = rowStart + x * 3
                        if pa[i] != pb[i] || pa[i + 1] != pb[i + 1] || pa[i + 2] != pb[i + 2] {
                            if x < x0 { x0 = x }; if x > x1 { x1 = x }
                            if y < y0 { y0 = y }; if y > y1 { y1 = y }
                        }
                    }
                }
            }
        }
        return x1 < 0 ? nil : (x0, y0, x1 - x0 + 1, y1 - y0 + 1)
    }

    // MARK: Socket

    private func connect() throws {
        var hints = addrinfo(ai_flags: 0, ai_family: AF_INET, ai_socktype: SOCK_STREAM, ai_protocol: 0,
                             ai_addrlen: 0, ai_canonname: nil, ai_addr: nil, ai_next: nil)
        var res: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, "7777", &hints, &res) == 0, let ai = res else { throw DisplayError.resolve }
        defer { freeaddrinfo(res) }
        let s = socket(ai.pointee.ai_family, ai.pointee.ai_socktype, ai.pointee.ai_protocol)
        guard s >= 0 else { throw DisplayError.connect(errno) }
        var tv = timeval(tv_sec: 5, tv_usec: 0)
        setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))
        setsockopt(s, SOL_SOCKET, SO_SNDTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))
        var one: Int32 = 1
        setsockopt(s, IPPROTO_TCP, TCP_NODELAY, &one, socklen_t(MemoryLayout<Int32>.size))
        setsockopt(s, SOL_SOCKET, SO_NOSIGPIPE, &one, socklen_t(MemoryLayout<Int32>.size))
        // Verbinden mit Zeitlimit (3 s) – ohne würde ein ausgeschaltetes Display den Takt bis zu 75 s blockieren
        let flags = fcntl(s, F_GETFL, 0)
        _ = fcntl(s, F_SETFL, flags | O_NONBLOCK)
        if Darwin.connect(s, ai.pointee.ai_addr, ai.pointee.ai_addrlen) != 0 {
            guard errno == EINPROGRESS else { let e = errno; Darwin.close(s); throw DisplayError.connect(e) }
            var pfd = pollfd(fd: s, events: Int16(POLLOUT), revents: 0)
            let r = poll(&pfd, 1, 3000)
            var err: Int32 = 0
            var len = socklen_t(MemoryLayout<Int32>.size)
            getsockopt(s, SOL_SOCKET, SO_ERROR, &err, &len)
            guard r == 1, err == 0 else { Darwin.close(s); throw DisplayError.connect(r == 0 ? ETIMEDOUT : err) }
        }
        _ = fcntl(s, F_SETFL, flags)
        fd = s; last = nil; owner = false
    }

    private func sendAll(_ bytes: [UInt8]) throws {
        var off = 0
        try bytes.withUnsafeBytes { p in
            while off < bytes.count {
                let n = send(fd, p.baseAddress! + off, bytes.count - off, 0)
                if n <= 0 { throw DisplayError.io }
                off += n
            }
        }
    }

    private func readVerdict() throws -> Character {
        var b: UInt8 = 0
        guard recv(fd, &b, 1, 0) == 1 else { throw DisplayError.io }
        return Character(UnicodeScalar(b))
    }

    private func be16(_ v: Int) -> [UInt8] { [UInt8((v >> 8) & 0xFF), UInt8(v & 0xFF)] }
    private func be32(_ v: UInt32) -> [UInt8] {
        [UInt8(v >> 24), UInt8((v >> 16) & 0xFF), UInt8((v >> 8) & 0xFF), UInt8(v & 0xFF)]
    }

    // MARK: HTTP

    func info() -> DeviceInfo? {
        guard let url = URL(string: "http://\(host)/") else { return nil }
        var req = URLRequest(url: url); req.timeoutInterval = 4
        guard let (data, _) = syncRequest(req),
              let d = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              d["fw"] as? String == "claude-display" else { return nil }
        return DeviceInfo(version: d["ver"] as? String ?? "1.x", ip: d["ip"] as? String ?? host,
                          rssi: d["rssi"] as? Int ?? 0, uptime: d["uptime"] as? Int ?? 0,
                          brightness: d["brt"] as? Int ?? 0, mac: d["mac"] as? String ?? "",
                          owner: UInt32(truncatingIfNeeded: d["owner"] as? Int ?? 0))
    }

    /// Setzt die Helligkeit und hinterlegt den Plan für die Zeit ohne Rechner.
    func setBrightness(_ v: Int, day: Int, night: Int, nightStart: Int, nightEnd: Int) {
        let q = "v=\(max(0, min(100, v)))&d=\(day)&n=\(night)&ns=\(nightStart)&ne=\(nightEnd)"
        guard let url = URL(string: "http://\(host)/brt?\(q)") else { return }
        var req = URLRequest(url: url); req.timeoutInterval = 4
        _ = syncRequest(req)
    }

    /// Schickt das Theme für die Uhr ohne Rechner (ab Firmware 1.0).
    func setTheme(_ t: DisplayTheme) {
        var c = URLComponents(string: "http://\(host)/theme")!
        c.queryItems = [
            .init(name: "a", value: t.accent.hex), .init(name: "b", value: t.accent2.hex),
            .init(name: "t", value: t.text.hex), .init(name: "m", value: t.muted.hex),
            .init(name: "d", value: t.dim.hex), .init(name: "e", value: t.edge.hex),
            .init(name: "logo", value: String(t.logo.firmwareCode)), .init(name: "title", value: t.title),
        ]
        guard let url = c.url else { return }
        var req = URLRequest(url: url); req.timeoutInterval = 4
        _ = syncRequest(req)
    }

    /// Lädt eine Firmware (.bin oder .bin.gz) über /update hoch.
    func uploadFirmware(_ data: Data, filename: String, progress: @escaping (Double) -> Void,
                        completion: @escaping (Result<Void, Error>) -> Void) {
        let boundary = "claude-display-\(UUID().uuidString)"
        var body = Data()
        body.append("--\(boundary)\r\nContent-Disposition: form-data; name=\"firmware\"; filename=\"\(filename)\"\r\n".data(using: .utf8)!)
        body.append("Content-Type: application/octet-stream\r\n\r\n".data(using: .utf8)!)
        body.append(data)
        body.append("\r\n--\(boundary)--\r\n".data(using: .utf8)!)
        var req = URLRequest(url: URL(string: "http://\(host)/update")!)
        req.httpMethod = "POST"
        req.timeoutInterval = 120
        req.setValue("multipart/form-data; boundary=\(boundary)", forHTTPHeaderField: "Content-Type")
        let delegate = UploadDelegate(progress: progress)
        let session = URLSession(configuration: .ephemeral, delegate: delegate, delegateQueue: nil)
        session.uploadTask(with: req, from: body) { data, resp, err in
            session.finishTasksAndInvalidate()
            if let err { return completion(.failure(err)) }
            let text = String(data: data ?? Data(), encoding: .utf8) ?? ""
            if (resp as? HTTPURLResponse)?.statusCode == 200 && text.contains("Success") {
                completion(.success(()))
            } else {
                completion(.failure(NSError(domain: "Firmware", code: 1,
                    userInfo: [NSLocalizedDescriptionKey: text.isEmpty ? "Update fehlgeschlagen" : text])))
            }
        }.resume()
    }

    /// Sucht das Display per mDNS (claude-display.local).
    static func discover() -> String? {
        var hints = addrinfo(ai_flags: 0, ai_family: AF_INET, ai_socktype: SOCK_STREAM, ai_protocol: 0,
                             ai_addrlen: 0, ai_canonname: nil, ai_addr: nil, ai_next: nil)
        var res: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo("claude-display.local", "80", &hints, &res) == 0, let ai = res else { return nil }
        defer { freeaddrinfo(res) }
        var buf = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
        let sin = UnsafeRawPointer(ai.pointee.ai_addr).assumingMemoryBound(to: sockaddr_in.self)
        var addr = sin.pointee.sin_addr
        inet_ntop(AF_INET, &addr, &buf, socklen_t(INET_ADDRSTRLEN))
        return String(cString: buf)
    }
}

private final class UploadDelegate: NSObject, URLSessionTaskDelegate {
    let progress: (Double) -> Void
    init(progress: @escaping (Double) -> Void) { self.progress = progress }
    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64,
                    totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        progress(totalBytesExpectedToSend > 0 ? Double(totalBytesSent) / Double(totalBytesExpectedToSend) : 0)
    }
}
