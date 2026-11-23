using System.Text.Json;

namespace ClaudeDisplay.Core;

/// <summary>
/// Verlauf des Nutzungslimits: höchstens alle 5 Minuten ein Wert, 7 Tage lang.
/// Datei: %APPDATA%\ClaudeDisplay\usage-history.json
/// </summary>
public sealed class UsageHistory
{
    public static UsageHistory Shared { get; } = new();
    public sealed record Point(DateTime T, int Five, int Week);

    readonly object gate = new();
    List<Point> points = [];
    public event Action? Changed;

    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeDisplay", "usage-history.json");

    UsageHistory()
    {
        try { points = JsonSerializer.Deserialize<List<Point>>(File.ReadAllText(FilePath)) ?? []; } catch { }
    }

    public List<Point> Since(DateTime from) { lock (gate) return points.Where(p => p.T >= from).ToList(); }

    /// <summary>Neuer Messwert (von jedem Thread aus).</summary>
    public void Add(int five, int week)
    {
        var t = DateTime.UtcNow;
        lock (gate)
        {
            if (points.Count > 0 && t - points[^1].T < TimeSpan.FromMinutes(5))
            {
                if (points[^1].Five == five && points[^1].Week == week) return;
                points.RemoveAt(points.Count - 1);          // innerhalb 5 min: ersetzen
            }
            points.Add(new Point(t, five, week));
            points.RemoveAll(p => t - p.T > TimeSpan.FromDays(7));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(points));
            }
            catch { }
        }
        Changed?.Invoke();
    }
}
