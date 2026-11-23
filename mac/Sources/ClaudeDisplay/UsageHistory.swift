import Charts
import Foundation
import Observation
import SwiftUI

/// Verlauf des Nutzungslimits: höchstens alle 5 Minuten ein Wert, 7 Tage lang.
/// Datei: ~/Library/Application Support/Claude Display/usage-history.json
@Observable
final class UsageHistory {
    static let shared = UsageHistory()

    struct Point: Codable, Equatable {
        var t: Date
        var five: Int
        var week: Int
    }

    private(set) var points: [Point] = []
    @ObservationIgnored private let url: URL = {
        let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("Claude Display", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
        return dir.appendingPathComponent("usage-history.json")
    }()

    private init() {
        if let d = try? Data(contentsOf: url), let p = try? JSONDecoder().decode([Point].self, from: d) { points = p }
    }

    /// Neuer Messwert (von jedem Thread aus)
    func add(five: Int, week: Int, at t: Date = Date()) {
        DispatchQueue.main.async { [self] in
            if let last = points.last, t.timeIntervalSince(last.t) < 300, last.five == five, last.week == week { return }
            if let last = points.last, t.timeIntervalSince(last.t) < 300 { points.removeLast() }   // innerhalb 5 min: ersetzen
            points.append(Point(t: t, five: five, week: week))
            points.removeAll { t.timeIntervalSince($0.t) > 7 * 86_400 }
            if let d = try? JSONEncoder().encode(points) { try? d.write(to: url, options: .atomic) }
        }
    }
}

/// Grafik im Dashboard: 5-Stunden- und Wochenlimit über 24 Stunden oder 7 Tage
struct UsageHistoryCard: View {
    private var history = UsageHistory.shared
    @State private var days = 1

    var body: some View {
        let t = AppSettings.shared.appTheme, d = AppSettings.shared.displayTheme
        let from = Date().addingTimeInterval(-Double(days) * 86_400)
        let pts = history.points.filter { $0.t >= from }
        VStack(alignment: .leading, spacing: 10) {
            HStack {
                SectionLabel(text: "Verlauf")
                Spacer()
                Picker("", selection: $days) {
                    Text("24 Std.").tag(1)
                    Text("7 Tage").tag(7)
                }
                .pickerStyle(.segmented).labelsHidden().fixedSize()
            }
            if pts.count < 2 {
                Text("Der Verlauf füllt sich, sobald die App ein paar Mal das Limit gelesen hat.")
                    .font(DS.font(12)).foregroundStyle(.secondary)
                    .frame(maxWidth: .infinity, minHeight: 110, alignment: .center)
            } else {
                Chart {
                    ForEach(pts, id: \.t) { p in
                        LineMark(x: .value("Zeit", p.t), y: .value("Prozent", p.five), series: .value("Limit", "5 Stunden"))
                            .foregroundStyle(t.accent.color)
                            .interpolationMethod(.monotone)
                        LineMark(x: .value("Zeit", p.t), y: .value("Prozent", p.week), series: .value("Limit", "Woche"))
                            .foregroundStyle(d.warn.color)
                            .interpolationMethod(.monotone)
                    }
                    RuleMark(y: .value("Grenze", 100)).foregroundStyle(.white.opacity(0.15))
                }
                .chartYScale(domain: 0...100)
                .chartYAxis { AxisMarks(values: [0, 50, 100]) { v in
                    AxisGridLine().foregroundStyle(.white.opacity(0.08))
                    AxisValueLabel { Text("\(v.as(Int.self) ?? 0) %").font(DS.font(10)) }
                } }
                .chartXAxis { AxisMarks(values: .automatic(desiredCount: days == 1 ? 6 : 7)) { _ in
                    AxisGridLine().foregroundStyle(.white.opacity(0.05))
                    AxisValueLabel(format: days == 1 ? .dateTime.hour() : .dateTime.weekday(.abbreviated)).font(DS.font(10))
                } }
                .frame(height: DS.w(120))
                HStack(spacing: 14) {
                    legend(t.accent.color, "5 Stunden")
                    legend(d.warn.color, "Woche")
                }
            }
        }
        .glass(16)
    }

    private func legend(_ c: Color, _ s: String) -> some View {
        HStack(spacing: 6) {
            Capsule().fill(c).frame(width: 14, height: 4)
            Text(s).font(DS.font(11)).foregroundStyle(.secondary)
        }
    }
}
