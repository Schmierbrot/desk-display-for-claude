import AppKit
import ServiceManagement
import SwiftUI

@main
struct ClaudeDisplayApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    @Bindable private var settings = AppSettings.shared

    var body: some Scene {
        // Menüleisten-Symbol nur im Menüleisten-Modus
        MenuBarExtra(isInserted: $settings.menuBarShown) {
            PanelView()
        } label: {
            Image(nsImage: MenuIcon.image)
        }
        .menuBarExtraStyle(.window)
    }
}

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var napGuard: NSObjectProtocol?

    func applicationDidFinishLaunching(_ notification: Notification) {
        let args = CommandLine.arguments
        if let i = args.firstIndex(of: "--render-test"), i + 1 < args.count {
            RenderTest.run(to: args[i + 1], theme: i + 2 < args.count ? Themes.preset(args[i + 2]).display : nil)
            exit(0)
        }
        if args.contains("--refresh-test") {
            switch ClaudeLogin.accessToken(force: true) {
            case .success: print("Login erneuert")
            case .failure(let e): print("Fehler:", e.message)
            }
            exit(0)
        }
        if args.contains("--update-test") {
            Task { @MainActor in
                await Updater.shared.check()
                let u = Updater.shared
                print("Repo:", Updater.repo ?? "-", "| App:", Updater.appVersion, "→", u.latest?.appVersion ?? "-",
                      "| Firmware im Release:", u.latest?.firmwareVersion ?? "-", "| App-Update:", u.appUpdate)
                exit(0)
            }
            return
        }
        if args.contains("--usage-test") {
            print(String(describing: ClaudeData().usage(live: true)))
            exit(0)
        }
        // Ohne sichtbares Fenster würde macOS die App per App Nap ausbremsen – dann
        // käme der Sekundentakt fürs Display nur noch sporadisch. Ruhezustand bleibt erlaubt.
        napGuard = ProcessInfo.processInfo.beginActivity(
            options: .userInitiatedAllowingIdleSystemSleep,
            reason: "Claude Display aktualisiert das Display jede Sekunde")
        Engine.shared.start()
        ClaudeConnect.shared.refresh()
        Updater.shared.checkIfDue()
        Timer.scheduledTimer(withTimeInterval: 6 * 3600, repeats: true) { _ in Updater.shared.checkIfDue() }
        if !args.contains("--snapshot-ui") {
            if AppSettings.shared.setupDone {
                AppMode.apply()
                // Von Hand geöffnet (nicht beim Anmelden): immer ein Fenster zeigen –
                // das Menüleisten-Symbol kann hinter der Notch verschwinden.
                if !Self.launchedAtLogin && !AppSettings.shared.windowMode { MainWindow.show() }
                if args.contains("--show-settings") { SettingsWindow.show() }
            } else {
                NSApp.setActivationPolicy(.regular)
                DispatchQueue.main.asyncAfter(deadline: .now() + 0.4) { SetupWindow.show() }
            }
        }
        if let i = args.firstIndex(of: "--snapshot-ui"), i + 1 < args.count {
            // Oberfläche als Bild speichern (zum Prüfen des Layouts)
            DispatchQueue.main.asyncAfter(deadline: .now() + 4) {
                var views: [(String, AnyView)] = [("panel", AnyView(PanelView())),
                                                  ("dashboard", AnyView(DashboardView().frame(width: 960, height: 640))),
                                                  ("dashboard-right", AnyView(DashboardView().right.padding(28).frame(width: 600).background(NeonBackground())))]
                for page in SettingsPage.allCases {
                    views.append(("page-\(page.rawValue)", AnyView(VStack(alignment: .leading, spacing: 18) { SettingsView.content(page) }
                        .padding(26).frame(width: 600).background(NeonBackground()))))
                }
                for p in SetupModel.Page.allCases {
                    let m = SetupModel(); m.page = p
                    m.devices = [FoundDevice(ip: "192.168.1.50", kind: .ready, detail: "Claude Display · Firmware 1.0"),
                                 FoundDevice(ip: "192.168.1.51", kind: .stock, detail: "SmallTV-Ultra · Original-Firmware Ultra-V9.0.41")]
                    m.selected = m.devices[1]
                    views.append(("setup-\(p.rawValue)", AnyView(SetupView(model: m).frame(width: 760, height: 560))))
                }
                for (name, view) in views {
                    let r = ImageRenderer(content: view.environment(\.colorScheme, .dark))
                    r.scale = 2
                    if let img = r.nsImage, let tiff = img.tiffRepresentation,
                       let png = NSBitmapImageRep(data: tiff)?.representation(using: .png, properties: [:]) {
                        try? png.write(to: URL(fileURLWithPath: args[i + 1] + "-\(name).png"))
                    }
                }
                exit(0)
            }
        }
    }
}

extension AppDelegate {
    /// Erneut geöffnet (Doppelklick, Dock, Spotlight): Dashboard zeigen – in jedem Modus.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if AppSettings.shared.setupDone { MainWindow.show() } else { SetupWindow.show() }
        return true
    }

    /// Start über "Beim Anmelden öffnen"?
    static var launchedAtLogin: Bool {
        let e = NSAppleEventManager.shared().currentAppleEvent
        return e?.eventID == kAEOpenApplication &&
            e?.paramDescriptor(forKeyword: keyAEPropData)?.enumCodeValue == keyAELaunchedAsLogInItem
    }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { false }
}

/// Starten beim Anmelden (Anmeldeobjekte in den Systemeinstellungen).
enum LoginItem {
    static var enabled: Bool { SMAppService.mainApp.status == .enabled }
    static func set(_ on: Bool) throws {
        if on { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
    }
}

/// Symbol für die Menüleiste: kleines Display mit leuchtendem Punkt (Vorlage, passt sich hell/dunkel an).
enum MenuIcon {
    static let image: NSImage = {
        let img = NSImage(size: NSSize(width: 18, height: 18), flipped: true) { _ in
            guard let ctx = NSGraphicsContext.current?.cgContext else { return false }
            ctx.setStrokeColor(NSColor.black.cgColor)
            ctx.setFillColor(NSColor.black.cgColor)
            ctx.setLineWidth(1.6)
            ctx.addPath(CGPath(roundedRect: CGRect(x: 2.3, y: 2.3, width: 13.4, height: 13.4), cornerWidth: 3.4, cornerHeight: 3.4, transform: nil))
            ctx.strokePath()
            ctx.setLineWidth(1.8)
            ctx.setLineCap(.round)
            ctx.addArc(center: CGPoint(x: 9, y: 9), radius: 3.6, startAngle: -.pi / 2, endAngle: .pi * 1.05, clockwise: false)
            ctx.strokePath()
            ctx.fillEllipse(in: CGRect(x: 7.9, y: 7.9, width: 2.2, height: 2.2))
            return true
        }
        img.isTemplate = true
        return img
    }()
}

/// `ClaudeDisplay --render-test datei.png` zeichnet Beispielansichten (zum Prüfen ohne Display).
enum RenderTest {
    static func run(to path: String, theme: DisplayTheme? = nil) {
        let now = Date()
        let use = Usage(five: UsageWindow(pct: 32, reset: now.addingTimeInterval(5340)),
                        week: UsageWindow(pct: 62, reset: now.addingTimeInterval(130_000)), stale: nil)
        let sess = SessionInfo(name: "Mein Projekt", secs: 159,
                               plan: [PlanItem(text: "a", status: "completed"),
                                      PlanItem(text: "b", status: "completed"),
                                      PlanItem(text: "Mac-App bauen und schön machen", status: "in_progress"),
                                      PlanItem(text: "d", status: "pending")],
                               prompt: nil, busyCount: 2)
        let views = [Renderer.render(session: sess, usage: use, tick: 3, theme: theme).image,
                     Renderer.render(session: nil, usage: use, tick: 0, theme: theme).image]
        let out = CGContext(data: nil, width: 980, height: 490, bitsPerComponent: 8, bytesPerRow: 0,
                            space: CGColorSpace(name: CGColorSpace.sRGB)!,
                            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        out.setFillColor(.white); out.fill(CGRect(x: 0, y: 0, width: 980, height: 490))
        out.interpolationQuality = .none
        for (i, img) in views.enumerated() {
            out.draw(img, in: CGRect(x: 5 + i * 490, y: 5, width: 480, height: 480))
        }
        let rep = NSBitmapImageRep(cgImage: out.makeImage()!)
        try? rep.representation(using: .png, properties: [:])?.write(to: URL(fileURLWithPath: path))
    }
}
