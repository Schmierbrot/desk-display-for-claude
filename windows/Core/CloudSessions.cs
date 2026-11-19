using System.Net.Http.Headers;
using System.Text.Json;

namespace ClaudeDisplay.Core;

/// <summary>
/// Cloud-Sitzungen von Claude Code (im Browser, in der Cloud) – sie laufen nicht auf diesem Rechner,
/// deshalb gibt es keine lokalen Dateien. Abgefragt über den Claude-Code-Login, auf einem eigenen
/// Hintergrund-Takt, damit der Sekundentakt fürs Display nie aufs Netz wartet.
/// Inoffizielle Schnittstelle (wie beim Nutzungslimit): ändert sie sich, fehlen nur die Cloud-Sitzungen.
/// </summary>
public sealed class CloudSessions
{
    public static CloudSessions Shared { get; } = new();
    const string Base = "https://api.anthropic.com";
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

    sealed class Tracked
    {
        public readonly TranscriptState State = new();
        public readonly HashSet<string> Seen = [];
        public bool Loaded;
        public DateTime Since = DateTime.UtcNow;
        public string Title = "";
    }

    readonly object gate = new();
    List<SessionInfo> current = [];
    readonly Dictionary<string, Tracked> tracked = [];
    List<string> runningIds = [];
    DateTime nextList = DateTime.MinValue, pausedUntil = DateTime.MinValue;
    Timer? timer;
    int busy;

    /// <summary>Zum Testen: auch lokale Sitzungen mit Fernsteuerung („bridge“) einbeziehen.</summary>
    public bool IncludeBridge { get; set; }

    public void Start()
    {
        timer ??= new Timer(_ =>
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;     // ein Durchgang zur Zeit
            try { Tick(); } catch { } finally { busy = 0; }
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4));
    }

    /// <summary>Gerade arbeitende Cloud-Sitzungen (neueste zuerst).</summary>
    public List<SessionInfo> Running() { lock (gate) return current; }

    public void Tick()
    {
        if (!AppSettings.Shared.CloudSessions) { Publish([]); return; }
        var now = DateTime.UtcNow;
        if (now < pausedUntil) return;
        if (now >= nextList)
        {
            if (Get("/v1/code/sessions?limit=25") is not { } list) return;
            nextList = now.AddSeconds(runningIds.Count == 0 ? 8 : 4);
            var ids = new List<(string id, DateTime last)>();
            if (list.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                foreach (var s in data.EnumerateArray())
                {
                    var id = Str(s, "id");
                    if (id == null) continue;
                    if (Str(s, "environment_kind") == "bridge" && !IncludeBridge) continue;   // läuft lokal – lesen wir aus den Dateien
                    var last = DateTimeOffset.TryParse(Str(s, "last_event_at"), out var l) ? l.UtcDateTime : DateTime.MinValue;
                    bool working = Str(s, "status") != "archived" && (Str(s, "status_bucket") == "working" || Str(s, "worker_status") == "running");
                    if (!working || now - last > TimeSpan.FromMinutes(15)) continue;
                    if (!tracked.TryGetValue(id, out var t)) tracked[id] = t = new Tracked();
                    t.Title = Str(s, "title") ?? "";
                    ids.Add((id, last));
                }
            var active = ids.Select(x => x.id).ToHashSet();
            foreach (var id in tracked.Keys.Where(k => !active.Contains(k)).ToList()) tracked.Remove(id);   // fertig → neue Laufzeit
            runningIds = ids.OrderByDescending(x => x.last).Select(x => x.id).ToList();
        }
        foreach (var id in runningIds) LoadEvents(id);
        Publish(runningIds.Where(tracked.ContainsKey).Select(id =>
        {
            var t = tracked[id];
            return new SessionInfo(t.Title.Length == 0 ? "Cloud-Sitzung" : t.Title, (int)Math.Max(0, (now - t.Since).TotalSeconds),
                                   t.State.Plan.ToList(), t.State.LastPrompt, runningIds.Count);
        }).ToList());
    }

    void Publish(List<SessionInfo> s) { lock (gate) current = s; }

    /// <summary>Neue Ereignisse holen (beim ersten Mal bis zu 600 zurück) und in zeitlicher Reihenfolge einspielen.</summary>
    void LoadEvents(string id)
    {
        var t = tracked[id];
        var fresh = new List<JsonElement>();
        string? cursor = null;
        int pages = t.Loaded ? 3 : 6;
        for (int p = 0; p < pages; p++)
        {
            var path = $"/v1/code/sessions/{id}/events?limit=100&sort_order=desc" + (cursor != null ? "&cursor=" + Uri.EscapeDataString(cursor) : "");
            if (Get(path) is not { } r || !r.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
            bool hitSeen = false;
            int n = 0;
            foreach (var e in data.EnumerateArray())
            {
                n++;
                var eid = Str(e, "event_id");
                if (eid == null) continue;
                if (t.Seen.Contains(eid)) { hitSeen = true; break; }
                fresh.Add(e.Clone());
            }
            cursor = Str(r, "next_cursor");
            if (hitSeen || string.IsNullOrEmpty(cursor) || n < 100) break;
        }
        for (int i = fresh.Count - 1; i >= 0; i--)
        {
            var e = fresh[i];
            if (Str(e, "event_id") is { } eid) t.Seen.Add(eid);
            if (!e.TryGetProperty("payload", out var p) || p.ValueKind != JsonValueKind.Object) continue;
            var before = t.State.LastPrompt;
            t.State.Feed(p);
            // Laufzeit wie lokal: ab der letzten Eingabe (Beginn der aktuellen Runde)
            if (t.State.LastPrompt != before && DateTimeOffset.TryParse(Str(e, "created_at"), out var d)) t.Since = d.UtcDateTime;
        }
        t.Loaded = true;
    }

    JsonElement? Get(string path, bool retried = false)
    {
        var (token, _) = ClaudeData.AccessToken();
        if (token == null) { pausedUntil = DateTime.UtcNow.AddMinutes(1); return null; }
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, Base + path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Headers.Add("anthropic-beta", "ccr-byoc-2025-07-29");
            req.Headers.UserAgent.ParseAdd(ClaudeData.UserAgent);
            using var resp = http.Send(req);
            int code = (int)resp.StatusCode;
            if (code == 200) return JsonDocument.Parse(resp.Content.ReadAsStream()).RootElement.Clone();
            if (code == 401 && !retried) { ClaudeData.AccessToken(force: true); return Get(path, true); }
            if (code == 429) pausedUntil = DateTime.UtcNow.AddMinutes(1);
            else
            {
                // z. B. Konto ohne Cloud-Sitzungen oder geänderte Schnittstelle: eine Weile Ruhe
                Log.Write($"Cloud-Sitzungen: HTTP {code}");
                pausedUntil = DateTime.UtcNow.AddMinutes(10);
            }
        }
        catch { /* offline – nächster Durchgang */ }
        return null;
    }

    static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
