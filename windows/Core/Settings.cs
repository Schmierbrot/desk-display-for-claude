using System.Text.Json;

namespace ClaudeDisplay.Core;

/// <summary>Dauerhafte Einstellungen (%APPDATA%\ClaudeDisplay\settings.json).</summary>
public sealed class AppSettings
{
    public string Host { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public double DayBrightness { get; set; } = 60;
    public double NightBrightness { get; set; } = 8;
    public bool NightMode { get; set; } = true;
    public int NightStart { get; set; } = 22;
    public int NightEnd { get; set; } = 7;
    public bool LiveUsage { get; set; } = true;
    /// <summary>Cloud-Sitzungen (Claude Code im Browser/in der Cloud) mit anzeigen</summary>
    public bool CloudSessions { get; set; } = true;
    /// <summary>"tray" = nur Infobereich, "window" = App-Fenster in der Taskleiste</summary>
    public string AppMode { get; set; } = "tray";
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Schriftgröße der App: 0,9 klein · 1 normal · 1,15 groß · 1,3 sehr groß</summary>
    public double TextScale { get; set; } = 1.0;
    [System.Text.Json.Serialization.JsonIgnore] public bool WindowMode => AppMode == "window";
    /// <summary>Einrichtungs-Assistent abgeschlossen</summary>
    public bool SetupDone { get; set; }
    /// <summary>Vorlage ("neon", "claude", …) oder "custom" – getrennt für Display und App</summary>
    public string DisplayThemeId { get; set; } = "neon";
    public string AppThemeId { get; set; } = "neon";
    public DisplayTheme DisplayCustom { get; set; } = Themes.Preset("neon").Display;
    public AppTheme AppCustom { get; set; } = Themes.Preset("neon").App;
    /// <summary>Aufbau des Displays (Layout-Editor)</summary>
    public DisplayLayout DisplayLayout { get; set; } = LayoutPresets.Standard;

    [System.Text.Json.Serialization.JsonIgnore]
    public DisplayTheme DisplayTheme => DisplayThemeId == "custom" ? DisplayCustom : Themes.Preset(DisplayThemeId).Display;
    [System.Text.Json.Serialization.JsonIgnore]
    public AppTheme AppTheme => AppThemeId == "custom" ? AppCustom : Themes.Preset(AppThemeId).App;
    /// <summary>Eindeutige Kennung dieses Rechners gegenüber dem Display.</summary>
    public uint HostId { get; set; }

    public static AppSettings Shared { get; } = Load();

    static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClaudeDisplay");
    static string FilePath => Path.Combine(Dir, "settings.json");

    static AppSettings Load()
    {
        AppSettings s;
        try { s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { s = new(); }
        s.DisplayLayout = (s.DisplayLayout ?? LayoutPresets.Standard).Normalized();
        s.TextScale = new[] { 0.9, 1.0, 1.15, 1.3 }.MinBy(v => Math.Abs(v - s.TextScale));   // auf die nächste Stufe runden
        if (s.HostId < 2)
        {
            s.HostId = (uint)Random.Shared.NextInt64(2, uint.MaxValue);
            s.Save();
        }
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* nicht kritisch */ }
    }

    public bool IsNight(DateTime? at = null)
    {
        if (!NightMode) return false;
        int h = (at ?? DateTime.Now).Hour;
        return NightStart > NightEnd ? (h >= NightStart || h < NightEnd) : (h >= NightStart && h < NightEnd);
    }

    public int Brightness(DateTime? at = null) => (int)Math.Round(IsNight(at) ? NightBrightness : DayBrightness);
}
