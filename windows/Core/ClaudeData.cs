using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClaudeDisplay.Core;

/// <summary>Id = Aufgaben-Nummer bei TaskCreate/TaskUpdate (Claude Code im Terminal).</summary>
public sealed record PlanItem(string Text, string Status, string? Id = null);

/// <summary>Die gerade arbeitende Claude-Code-Sitzung.</summary>
public sealed record SessionInfo(string Name, int Secs, IReadOnlyList<PlanItem> Plan, string? Prompt, int BusyCount)
{
    public int Done => Plan.Count(p => p.Status == "completed");
    public string? CurrentStep =>
        Plan.FirstOrDefault(p => p.Status == "in_progress")?.Text ?? Plan.FirstOrDefault(p => p.Status == "pending")?.Text;
}

public sealed record UsageWindow(int Pct, DateTime? Reset);
/// <summary>Stale = Zeitpunkt des Stands, wenn der Wert älter als 3 Minuten ist.</summary>
public sealed record Usage(UsageWindow Five, UsageWindow Week, DateTime? Stale);

/// <summary>Liest ein Sitzungsprotokoll (JSONL) inkrementell und spielt den Aufgabenplan nach.</summary>
sealed class TranscriptState
{
    readonly string? path;
    /// <summary>Cloud-Sitzung: Ereignisse kommen per Feed, Eingaben gelten als Aufgabe.</summary>
    readonly bool promptsFromUser;
    long offset;
    byte[] rest = [];
    List<PlanItem> plan = [];
    public IReadOnlyList<PlanItem> Plan => plan;
    public string? LastPrompt { get; private set; }

    public TranscriptState(string path) => this.path = path;
    public TranscriptState() { path = null; promptsFromUser = true; }

    /// <summary>Ein Ereignis (gleiches Format wie eine Protokollzeile).</summary>
    public void Feed(JsonElement d) { try { Handle(d); } catch { } }

    public void Update()
    {
        if (path == null) return;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < offset) { offset = 0; rest = []; plan = []; LastPrompt = null; }
            if (fs.Length == offset) return;
            fs.Seek(offset, SeekOrigin.Begin);
            var data = new byte[fs.Length - offset];
            int read = 0;
            while (read < data.Length) { int n = fs.Read(data, read, data.Length - read); if (n <= 0) break; read += n; }
            offset += read;
            var buf = rest.Concat(data.Take(read)).ToArray();
            int lastNl = Array.LastIndexOf(buf, (byte)'\n');
            if (lastNl < 0) { rest = buf; return; }
            rest = buf[(lastNl + 1)..];
            foreach (var line in Encoding.UTF8.GetString(buf, 0, lastNl).Split('\n'))
            {
                if (line.Length == 0) continue;
                try { using var doc = JsonDocument.Parse(line); Handle(doc.RootElement); } catch { }
            }
        }
        catch { /* Datei gerade nicht lesbar */ }
    }

    static string? Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    static bool Bool(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    void Handle(JsonElement d)
    {
        var type = Str(d, "type");
        if (type == "last-prompt") { LastPrompt = Str(d, "lastPrompt"); return; }
        // Unteragenten: lokal isSidechain, in der Cloud parent_tool_use_id
        if (Bool(d, "isSidechain") || Str(d, "parent_tool_use_id") != null) return;
        if (!d.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object) return;
        if (promptsFromUser && type == "user" && msg.TryGetProperty("content", out var pc))
        {
            var text = pc.ValueKind == JsonValueKind.String ? pc.GetString()
                : pc.ValueKind == JsonValueKind.Array ? string.Join(" ", pc.EnumerateArray().Where(x => Str(x, "type") == "text").Select(x => Str(x, "text"))) : null;
            text = text?.Trim();
            if (!string.IsNullOrEmpty(text) && !text.StartsWith('<')) LastPrompt = text;
        }
        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return;
        if (type == "user")
        {
            var tr = d.TryGetProperty("toolUseResult", out var a) ? a : d.TryGetProperty("tool_use_result", out var b) ? b : default;
            HandleResults(content, tr); return;
        }
        if (type != "assistant") return;
        foreach (var c in content.EnumerateArray())
        {
            if (Str(c, "type") != "tool_use") continue;
            var name = Str(c, "name") ?? "";
            var input = c.TryGetProperty("input", out var i) ? i : default;
            if (name == "TaskCreate")
            {
                var subject = Str(input, "subject") ?? Str(input, "description") ?? "";
                // neue Aufgabenliste, sobald die alte ganz erledigt ist
                if (plan.Count > 0 && plan.All(p => p.Status == "completed") && plan.Any(p => p.Id != null)) plan = [];
                plan.Add(new PlanItem(subject, "pending"));
                if (Str(c, "id") is { } tid) pendingCreates[tid] = plan.Count - 1;
            }
            else if (name == "TaskUpdate")
            {
                var id = IdString(input, "taskId");
                int k = id == null ? -1 : plan.FindIndex(p => p.Id == id);
                if (k < 0) continue;
                if (Str(input, "status") is { } st)
                {
                    if (st == "deleted") { plan.RemoveAt(k); continue; }
                    plan[k] = plan[k] with { Status = st };
                }
                if (Str(input, "subject") is { Length: > 0 } sub) plan[k] = plan[k] with { Text = sub };
            }
            else if (name.EndsWith("update_plan")) ApplyPlanUpdate(input);
            else if (name.EndsWith("wrap_up") && !Bool(input, "blocked"))
                plan = plan.Select(p => p with { Status = "completed" }).ToList();   // blocked = wartet auf dich
            else if (name == "TodoWrite" && input.ValueKind == JsonValueKind.Object && input.TryGetProperty("todos", out var todos))
                plan = Items(todos);
        }
    }

    /// <summary>TaskCreate-Aufrufe, deren Nummer erst im Ergebnis steht: tool_use_id → Index im Plan.</summary>
    readonly Dictionary<string, int> pendingCreates = [];

    static string? IdString(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v)
            ? v.ValueKind switch { JsonValueKind.String => v.GetString(), JsonValueKind.Number => v.GetRawText(), _ => null }
            : null;

    /// <summary>Ergebnis von TaskCreate: „Task #3 created successfully: …“ → Nummer merken.</summary>
    void HandleResults(JsonElement content, JsonElement result)
    {
        foreach (var c in content.EnumerateArray())
        {
            if (Str(c, "type") != "tool_result" || Str(c, "tool_use_id") is not { } tid) continue;
            if (!pendingCreates.Remove(tid, out int idx) || idx >= plan.Count) continue;
            string? id = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("task", out var task) ? IdString(task, "id") : null;
            if (id == null)
            {
                var text = Str(c, "content") ?? (c.TryGetProperty("content", out var arr) && arr.ValueKind == JsonValueKind.Array
                    ? string.Concat(arr.EnumerateArray().Select(x => Str(x, "text") ?? "")) : "");
                var m = System.Text.RegularExpressions.Regex.Match(text, @"Task #(\S+) created");
                if (m.Success) id = m.Groups[1].Value;
            }
            if (id != null) plan[idx] = plan[idx] with { Id = id };
            else if (Bool(c, "is_error")) plan.RemoveAt(idx);
        }
    }

    static List<PlanItem> Items(JsonElement arr) =>
        arr.ValueKind != JsonValueKind.Array ? [] :
        arr.EnumerateArray().Select(x => new PlanItem(Str(x, "content") ?? "", Str(x, "status") ?? "pending")).ToList();

    void ApplyPlanUpdate(JsonElement inp)
    {
        if (inp.ValueKind != JsonValueKind.Object) return;
        if (inp.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array) plan = Items(items);
        if (Bool(inp, "advance"))
        {
            int cur = plan.FindIndex(p => p.Status == "in_progress");
            if (cur >= 0) plan[cur] = plan[cur] with { Status = "completed" };
            int nxt = plan.FindIndex(p => p.Status == "pending");
            if (nxt >= 0) plan[nxt] = plan[nxt] with { Status = "in_progress" };
        }
        if (inp.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.Array)
            foreach (var n in done.EnumerateArray())
                if (n.TryGetInt32(out int k) && k >= 1 && k <= plan.Count) plan[k - 1] = plan[k - 1] with { Status = "completed" };
        if (inp.TryGetProperty("start", out var start) && start.TryGetInt32(out int s))
        {
            for (int j = 0; j < plan.Count; j++) if (plan[j].Status == "in_progress") plan[j] = plan[j] with { Status = "pending" };
            if (s >= 1 && s <= plan.Count) plan[s - 1] = plan[s - 1] with { Status = "in_progress" };
        }
    }
}

/// <summary>Liest Claude-Code-Sitzungen und Nutzungslimit.</summary>
public sealed class ClaudeData
{
    static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string SessionsDir = Path.Combine(Home, ".claude", "sessions");
    static readonly string ProjectsDir = Path.Combine(Home, ".claude", "projects");
    readonly Dictionary<string, TranscriptState> transcripts = [];
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public SessionInfo? CurrentSession()
    {
        var busy = new List<JsonElement>();
        bool anyStatus = false;
        try
        {
            foreach (var f in Directory.EnumerateFiles(SessionsDir, "*.json"))
            {
                try
                {
                    var s = JsonDocument.Parse(File.ReadAllText(f)).RootElement;
                    if (!s.TryGetProperty("pid", out var pidEl) || !pidEl.TryGetInt32(out int pid) || !PidAlive(pid)) continue;
                    var status = s.TryGetProperty("status", out var st) ? st.GetString() : null;
                    if (status != null) anyStatus = true;
                    if (status != null && status != "idle") busy.Add(s);
                }
                catch { }
            }
        }
        catch { /* noch keine Sitzungsdateien */ }
        // Ältere Claude-Code-Versionen schreiben keinen Status: dann gilt die Sitzung
        // als aktiv, deren Protokoll gerade wächst.
        if (!anyStatus && RecentlyActiveTranscript() is { } recent) busy.Add(recent);

        static double Updated(JsonElement s) => s.TryGetProperty("statusUpdatedAt", out var v) && v.TryGetDouble(out var d) ? d : 0;
        var cur = busy.OrderByDescending(Updated).FirstOrDefault();
        // Cloud-Sitzungen (Claude Code im Browser/in der Cloud) – lokale Arbeit hat Vorrang
        var cloud = CloudSessions.Shared.Running();
        if (cur.ValueKind != JsonValueKind.Object)
            return cloud.Count > 0 ? cloud[0] with { BusyCount = cloud.Count } : null;

        var id = cur.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? "" : "";
        var tr = Transcript(id);
        tr?.Update();
        var cwd = cur.TryGetProperty("cwd", out var c) ? c.GetString() ?? "" : "";
        var name = cur.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(cwd.TrimEnd('/', '\\'));
        if (string.IsNullOrWhiteSpace(name)) name = "Sitzung";
        var secs = (int)Math.Max(0, DateTimeOffset.UtcNow.ToUnixTimeSeconds() - Updated(cur) / 1000);
        return new SessionInfo(name, secs, tr?.Plan.ToList() ?? [], tr?.LastPrompt, busy.Count + cloud.Count);
    }

    readonly Dictionary<string, double> activeSince = [];
    DateTime lastScan = DateTime.MinValue;
    JsonElement? lastFound;

    /// <summary>Für den Fall ohne Status: jüngstes Protokoll, das in den letzten 20 s geschrieben wurde.</summary>
    JsonElement? RecentlyActiveTranscript()
    {
        if (DateTime.UtcNow - lastScan < TimeSpan.FromSeconds(3)) return lastFound;
        lastScan = DateTime.UtcNow;
        FileInfo? best = null;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(ProjectsDir))
                foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*.jsonl"))
                    if (best == null || f.LastWriteTimeUtc > best.LastWriteTimeUtc) best = f;
        }
        catch { }
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
        if (best == null || DateTime.UtcNow - best.LastWriteTimeUtc > TimeSpan.FromSeconds(20))
        { activeSince.Clear(); return lastFound = null; }
        var id = Path.GetFileNameWithoutExtension(best.Name);
        var since = activeSince.TryGetValue(id, out var v) ? v : now;
        activeSince.Clear(); activeSince[id] = since;
        // Projektordner heißt wie der Pfad, mit „-“ statt „\“ – der letzte Teil reicht als Name.
        var name = best.Directory?.Name.Split('-').LastOrDefault() ?? "";
        return lastFound = JsonSerializer.SerializeToElement(new Dictionary<string, object>
            { ["sessionId"] = id, ["cwd"] = name, ["statusUpdatedAt"] = since * 1000 });
    }

    static bool PidAlive(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return !p.HasExited; }
        catch { return false; }
    }

    TranscriptState? Transcript(string sessionId)
    {
        if (transcripts.TryGetValue(sessionId, out var t)) return t;
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(ProjectsDir))
            {
                var f = Path.Combine(dir, sessionId + ".jsonl");
                if (File.Exists(f)) return transcripts[sessionId] = new TranscriptState(f);
            }
        }
        catch { }
        return null;
    }

    // ---- Nutzungslimit -------------------------------------------------------
    // Live von der API, so oft sie es erlaubt (etwa jede Minute; sie lehnt
    // häufigere Abfragen mit HTTP 429 ab). Ein Wert wird höchstens 10 Minuten
    // gezeigt, ab 3 Minuten mit "Stand"-Uhrzeit. Läuft ein Fenster ab, zeigt es 0 %.

    static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10), StaleAfter = TimeSpan.FromMinutes(3);
    sealed record Sample(UsageWindow Five, UsageWindow Week, DateTime Fetched);
    static string SampleFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeDisplay", "last-usage.json");
    Sample? lastLive = LoadSample();
    DateTime nextFetch = DateTime.MinValue;

    /// <summary>fetch: nur der Rechner, der gerade aufs Display zeichnet, fragt die API ab.</summary>
    public Usage? GetUsage(bool live, bool fetch = true)
    {
        if (!live) return null;
        var now = DateTime.UtcNow;
        if (fetch && now >= nextFetch)
        {
            var (sample, limited, why) = UsageFromApi();
            if (sample != null) { lastLive = sample; SaveSample(sample); nextFetch = now.AddSeconds(60); }
            else { nextFetch = now.AddSeconds(30); if (!limited) Log.Write("Limit: " + why); }
        }
        var best = lastLive;
        if (best == null || now - best.Fetched >= MaxAge) return null;

        // abgelaufene Fenster: neues Fenster beginnt bei 0 %, sofort neu abfragen
        UsageWindow Roll(UsageWindow w)
        {
            if (w.Reset is { } rs && rs <= now)
            {
                if (best.Fetched < rs && nextFetch > now) nextFetch = now;
                return new UsageWindow(0, null);
            }
            return w;
        }
        var five = Roll(best.Five);
        var week = Roll(best.Week);
        DateTime? stale = now - best.Fetched > StaleAfter ? best.Fetched.ToLocalTime() : null;
        return new Usage(five, week, stale);
    }

    static Sample? LoadSample()
    {
        try { return JsonSerializer.Deserialize<Sample>(File.ReadAllText(SampleFile)); } catch { return null; }
    }

    static void SaveSample(Sample s)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(SampleFile)!); File.WriteAllText(SampleFile, JsonSerializer.Serialize(s)); } catch { }
    }

    const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    internal const string UserAgent = "ClaudeDisplay/1.0";
    static DateTime refreshRetryAfter = DateTime.MinValue;

    /// <summary>
    /// Zugangsschlüssel von Claude Code (Windows/Linux: ~/.claude/.credentials.json, macOS: Schlüsselbund).
    /// Läuft er ab, wird er wie von der CLI erneuert und in die Datei zurückgeschrieben.
    /// </summary>
    /// <summary>Limit und Cloud-Sitzungen laufen auf verschiedenen Threads – erneuert wird nur einmal gleichzeitig.</summary>
    static readonly object refreshGate = new();

    internal static (string? token, string? why) AccessToken(bool force = false)
    {
        lock (refreshGate) return AccessTokenLocked(force);
    }

    static (string? token, string? why) AccessTokenLocked(bool force)
    {
        try
        {
            var file = Path.Combine(Home, ".claude", ".credentials.json");
            string? json = null;
            bool fromFile = File.Exists(file);
            if (fromFile) json = File.ReadAllText(file);
            else if (OperatingSystem.IsMacOS())
            {
                var psi = new ProcessStartInfo("/usr/bin/security") { RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var a in new[] { "find-generic-password", "-s", "Claude Code-credentials", "-w" }) psi.ArgumentList.Add(a);
                using var p = Process.Start(psi)!;
                json = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
            }
            if (string.IsNullOrWhiteSpace(json)) return (null, "kein Claude-Code-Login gefunden (einmal in Claude Code anmelden)");

            var root = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            var oauth = root["claudeAiOauth"]!.AsObject();
            var token = oauth["accessToken"]?.GetValue<string>();
            double.TryParse(oauth["expiresAt"]?.ToString(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out var expiresAt);
            if (token != null && !force && DateTimeOffset.FromUnixTimeMilliseconds((long)expiresAt) > DateTimeOffset.UtcNow.AddMinutes(5))
                return (token, null);

            // erneuern – nur mit Datei, den macOS-Schlüsselbund beschreibt die Mac-App
            if (!fromFile) return (null, "Claude-Code-Login abgelaufen");
            if (DateTime.UtcNow < refreshRetryAfter) return (null, "Login abgelaufen, Erneuerung pausiert");
            var refresh = oauth["refreshToken"]?.GetValue<string>();
            if (refresh == null) return (null, "Login abgelaufen und nicht erneuerbar");
            var scopes = oauth["scopes"] is System.Text.Json.Nodes.JsonArray arr ? string.Join(' ', arr.Select(x => x!.GetValue<string>())) : "";
            var body = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = ClientId, ["scope"] = scopes,
            });
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var resp = http.Send(req);
            var text = resp.Content.ReadAsStringAsync().Result;
            if (!resp.IsSuccessStatusCode)
            {
                // Hat Claude Code den Login gerade selbst erneuert? Dann den neuen nehmen.
                try
                {
                    var o2 = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!["claudeAiOauth"]!;
                    var t2 = o2["accessToken"]?.GetValue<string>();
                    double.TryParse(o2["expiresAt"]?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e2);
                    if (t2 != null && t2 != token && DateTimeOffset.FromUnixTimeMilliseconds((long)e2) > DateTimeOffset.UtcNow.AddMinutes(1)) return (t2, null);
                }
                catch { }
                refreshRetryAfter = DateTime.UtcNow.AddMinutes((int)resp.StatusCode == 400 ? 10 : 2);
                Log.Write($"Login-Erneuerung fehlgeschlagen: HTTP {(int)resp.StatusCode} {text[..Math.Min(120, text.Length)]}");
                return (null, (int)resp.StatusCode == 400 ? "Login ungültig – bitte in Claude Code neu anmelden" : $"Login-Erneuerung: HTTP {(int)resp.StatusCode}");
            }
            var d = JsonDocument.Parse(text).RootElement;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var newToken = d.GetProperty("access_token").GetString()!;
            oauth["accessToken"] = newToken;
            if (d.TryGetProperty("refresh_token", out var rt)) oauth["refreshToken"] = rt.GetString();
            oauth["expiresAt"] = now + (long)(d.GetProperty("expires_in").GetDouble() * 1000);
            if (d.TryGetProperty("scope", out var sc) && !string.IsNullOrWhiteSpace(sc.GetString()))
                oauth["scopes"] = new System.Text.Json.Nodes.JsonArray(sc.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(x => (System.Text.Json.Nodes.JsonNode?)x).ToArray());
            if (d.TryGetProperty("refresh_token_expires_in", out var re))
                oauth["refreshTokenExpiresAt"] = now + (long)(re.GetDouble() * 1000);

            // atomar zurückschreiben
            var tmp = file + ".claude-display.tmp";
            File.WriteAllText(tmp, root.ToJsonString());
            File.Move(tmp, file, true);
            Log.Write("Claude-Code-Login erneuert");
            return (newToken, null);
        }
        catch (Exception e) { return (null, "Login nicht lesbar: " + e.Message); }
    }

    static (Sample? sample, bool limited, string? why) UsageFromApi()
    {
        var (token, why) = AccessToken();
        if (token == null) return (null, false, why);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            req.Headers.UserAgent.ParseAdd(UserAgent);
            using var resp = http.Send(req);
            if ((int)resp.StatusCode == 429) return (null, true, null);
            if (!resp.IsSuccessStatusCode) return (null, false, $"HTTP {(int)resp.StatusCode}");
            var d = JsonDocument.Parse(resp.Content.ReadAsStream()).RootElement;
            UsageWindow Win(string key)
            {
                if (!d.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) return new(0, null);
                int pct = w.TryGetProperty("utilization", out var u) && u.TryGetDouble(out var x) ? (int)Math.Round(x) : 0;
                DateTime? reset = w.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
                                  && DateTimeOffset.TryParse(r.GetString(), out var dt) ? dt.UtcDateTime : null;
                return new(pct, reset);
            }
            return (new Sample(Win("five_hour"), Win("seven_day"), DateTime.UtcNow), false, null);
        }
        catch (Exception e) { return (null, false, "API nicht erreichbar: " + e.Message); }
    }

}

/// <summary>Kleine Protokolldatei: %LOCALAPPDATA%\ClaudeDisplay\log.txt</summary>
public static class Log
{
    static readonly string File_ = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeDisplay", "log.txt");
    static readonly object gate = new();

    public static void Write(string msg)
    {
        try
        {
            lock (gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(File_)!);
                File.AppendAllText(File_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}\n");
            }
        }
        catch { }
    }
}
