using System.Diagnostics;
using System.Text.Json;

namespace ClaudeDisplay.Core;

public enum ClaudeState { Checking, NotInstalled, Installing, LoggedOut, LoggingIn, Connected, Failed }

/// <summary>
/// Verbindung zu Claude: findet bzw. installiert die offizielle Claude-Code-CLI und
/// startet `claude auth login` in einem Konsolenfenster. Die App liest danach nur den
/// Login der CLI (Sitzungen und Nutzungslimit).
/// </summary>
public sealed class ClaudeConnect
{
    public static ClaudeConnect Shared { get; } = new();
    public event Action? Changed;
    public ClaudeState State { get; private set; } = ClaudeState.Checking;
    public string? Plan { get; private set; }
    public string? Error { get; private set; }
    public bool IsConnected => State == ClaudeState.Connected;

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static string? CliPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] c = OperatingSystem.IsWindows()
            ? [Path.Combine(Home, ".local", "bin", "claude.exe"), Path.Combine(appData, "npm", "claude.cmd"), Path.Combine(Home, ".claude", "local", "claude.exe")]
            : [Path.Combine(Home, ".local", "bin", "claude"), "/opt/homebrew/bin/claude", "/usr/local/bin/claude"];
        return c.FirstOrDefault(File.Exists);
    }

    void Set(ClaudeState s, string? error = null) { State = s; Error = error; Changed?.Invoke(); }

    public void Refresh()
    {
        Set(ClaudeState.Checking);
        Task.Run(() =>
        {
            var cli = CliPath();
            if (cli == null) { Set(ClaudeState.NotInstalled); return; }
            try
            {
                var out_ = Run(cli, "auth status");
                var j = JsonDocument.Parse(out_).RootElement;
                if (j.TryGetProperty("loggedIn", out var li) && li.GetBoolean())
                {
                    Plan = j.TryGetProperty("subscriptionType", out var st) ? Cap(st.GetString()) : null;
                    Set(ClaudeState.Connected);
                }
                else Set(ClaudeState.LoggedOut);
            }
            catch { Set(ClaudeState.LoggedOut); }
        });
    }

    static string? Cap(string? s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];

    /// <summary>Offizieller Installer von claude.ai (sichtbares Fenster, damit man den Fortschritt sieht).</summary>
    public void Install()
    {
        Set(ClaudeState.Installing);
        Task.Run(() =>
        {
            try
            {
                var psi = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -Command \"irm https://claude.ai/install.ps1 | iex\"") { UseShellExecute = true }
                    : new ProcessStartInfo("/bin/bash", "-c \"curl -fsSL https://claude.ai/install.sh | bash\"");
                using var p = Process.Start(psi)!;
                p.WaitForExit();
                if (CliPath() != null) Refresh(); else Set(ClaudeState.Failed, "Installation fehlgeschlagen");
            }
            catch (Exception e) { Set(ClaudeState.Failed, e.Message); }
        });
    }

    /// <summary>Startet die Anmeldung in einem Konsolenfenster und wartet, bis sie abgeschlossen ist.</summary>
    public void Login()
    {
        var cli = CliPath();
        if (cli == null) { Set(ClaudeState.NotInstalled); return; }
        Set(ClaudeState.LoggingIn);
        Task.Run(async () =>
        {
            try
            {
                var psi = OperatingSystem.IsWindows()
                    ? new ProcessStartInfo("cmd.exe", $"/c \"\"{cli}\" auth login\"") { UseShellExecute = true }
                    : new ProcessStartInfo("open", $"-a Terminal \"{cli}\"");
                Process.Start(psi);
                for (int i = 0; i < 180 && State == ClaudeState.LoggingIn; i++)
                {
                    await Task.Delay(2000);
                    try
                    {
                        var j = JsonDocument.Parse(Run(cli, "auth status")).RootElement;
                        if (j.TryGetProperty("loggedIn", out var li) && li.GetBoolean()) { Refresh(); return; }
                    }
                    catch { }
                }
                if (State == ClaudeState.LoggingIn) Refresh();
            }
            catch (Exception e) { Set(ClaudeState.Failed, e.Message); }
        });
    }

    public void Cancel() => Refresh();

    public void Logout()
    {
        var cli = CliPath();
        if (cli == null) return;
        Task.Run(() => { Run(cli, "auth logout"); Refresh(); });
    }

    static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var s = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return s;
    }
}
