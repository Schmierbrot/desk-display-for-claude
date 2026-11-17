using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace ClaudeDisplay.Core;

/// <summary>
/// Updates über GitHub-Releases: App (ClaudeDisplay.exe) und Display-Firmware (firmware.bin.gz).
/// Das Repository (owner/name) setzt der Build über die MSBuild-Eigenschaft UpdateRepo.
/// </summary>
public sealed class Updater
{
    public static Updater Shared { get; } = new();
    public event Action? Changed;

    public sealed record Release(string Tag, string AppVersion, string? FirmwareVersion, string Notes, string? Page, string? AppAsset, string? FirmwareAsset);

    public Release? Latest { get; private set; }
    public bool Checking { get; private set; }
    public string? Message { get; private set; }
    DateTime lastCheck = DateTime.MinValue;
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(3) };

    static string? Meta(string key) => Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == key)?.Value is { Length: > 0 } v ? v : null;

    public static string? Repo => Meta("UpdateRepo");
    public static string BundledFirmware => Meta("FirmwareVersion") ?? "1.0";
    public static string AppVersion => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public bool AppUpdate => Latest != null && Newer(Latest.AppVersion, AppVersion) && Latest.AppAsset != null;
    public bool FirmwareUpdate(string? device) =>
        Latest?.FirmwareVersion is { } fw && Latest.FirmwareAsset != null && device != null && Newer(fw, device);

    public static bool Newer(string a, string b)
    {
        int[] P(string s) => s.TrimStart('v').Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray();
        var pa = P(a); var pb = P(b);
        for (int i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            int x = i < pa.Length ? pa[i] : 0, y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x > y;
        }
        return false;
    }

    void Notify() => Changed?.Invoke();

    public void CheckIfDue()
    {
        if (!AppSettings.Shared.AutoUpdate || DateTime.UtcNow - lastCheck < TimeSpan.FromHours(24)) return;
        _ = Check();
    }

    public async Task Check()
    {
        if (Repo == null) { Message = "Kein Update-Kanal in dieser Version"; Notify(); return; }
        Checking = true; Message = null; Notify();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd($"ClaudeDisplay/{AppVersion}");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) { Message = "GitHub gerade nicht erreichbar"; return; }
            var j = JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
            var tag = j.GetProperty("tag_name").GetString() ?? "";
            var assets = j.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: (a.GetProperty("name").GetString() ?? "").ToLowerInvariant(),
                              Url: a.GetProperty("browser_download_url").GetString(), Api: a.GetProperty("url").GetString()))
                .ToList();
            string? Asset(Func<string, bool> m) => assets.FirstOrDefault(a => m(a.Name)).Url;
            string? fw = null;
            // Versionsdatei über die API laden – der normale Download-Link kann aus
            // GitHubs Zwischenspeicher einen veralteten Stand liefern.
            if (assets.FirstOrDefault(a => a.Name == "release.json").Api is { } manifest)
            {
                try
                {
                    using var mreq = new HttpRequestMessage(HttpMethod.Get, manifest);
                    mreq.Headers.UserAgent.ParseAdd($"ClaudeDisplay/{AppVersion}");
                    mreq.Headers.Accept.ParseAdd("application/octet-stream");
                    mreq.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    var m = JsonDocument.Parse(await (await http.SendAsync(mreq)).Content.ReadAsStringAsync()).RootElement;
                    fw = m.TryGetProperty("firmware", out var f) ? f.GetString() : null;
                }
                catch { }
            }
            Latest = new Release(tag, tag.TrimStart('v'), fw, j.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                j.TryGetProperty("html_url", out var h) ? h.GetString() : null,
                Asset(n => n == "claudedisplay.exe"), Asset(n => n == "firmware.bin.gz"));
            Message = AppUpdate ? $"Version {Latest.AppVersion} ist verfügbar" : "Die App ist aktuell";
        }
        catch { Message = "GitHub gerade nicht erreichbar"; }
        finally { Checking = false; lastCheck = DateTime.UtcNow; Notify(); }
    }

    async Task<byte[]?> Download(string url)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd($"ClaudeDisplay/{AppVersion}");
            using var resp = await http.SendAsync(req);
            return resp.IsSuccessStatusCode ? await resp.Content.ReadAsByteArrayAsync() : null;
        }
        catch { return null; }
    }

    /// <summary>Lädt die neue exe, tauscht sie nach dem Beenden aus und startet sie neu.</summary>
    public async Task<bool> InstallApp(Action shutdown)
    {
        if (Latest?.AppAsset == null || !OperatingSystem.IsWindows()) return false;
        Message = "Lade Update …"; Notify();
        var data = await Download(Latest.AppAsset);
        if (data == null) { Message = "Download fehlgeschlagen"; Notify(); return false; }
        var exe = Environment.ProcessPath!;
        var tmp = Path.Combine(Path.GetTempPath(), "ClaudeDisplay-update.exe");
        await File.WriteAllBytesAsync(tmp, data);
        var script = Path.Combine(Path.GetTempPath(), "ClaudeDisplay-update.cmd");
        await File.WriteAllTextAsync(script,
            "@echo off\r\n" +
            $":wait\r\ntasklist /FI \"PID eq {Environment.ProcessId}\" | find \"{Environment.ProcessId}\" >nul && (timeout /t 1 >nul & goto wait)\r\n" +
            $"copy /Y \"{tmp}\" \"{exe}\" >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n" +
            "del \"%~f0\"\r\n");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{script}\"") { CreateNoWindow = true, UseShellExecute = false });
        shutdown();
        return true;
    }

    public async Task<string> InstallFirmware()
    {
        if (Latest?.FirmwareAsset == null) return "Keine Firmware im Release";
        Message = "Lade Firmware …"; Notify();
        var data = await Download(Latest.FirmwareAsset);
        Message = null; Notify();
        if (data == null) return "Download fehlgeschlagen";
        return await Engine.Shared.UpdateFirmwareAsync(data, "firmware.bin.gz");
    }
}
