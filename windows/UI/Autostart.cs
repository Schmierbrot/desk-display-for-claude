namespace ClaudeDisplay.UI;

/// <summary>Start mit Windows über HKCU\...\Run.</summary>
public static class Autostart
{
    const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "ClaudeDisplay";

    public static bool Supported => OperatingSystem.IsWindows();

    public static bool Enabled
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key);
            return k?.GetValue(Name) is string;
        }
    }

    public static void Set(bool on)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
        if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue(Name, false);
    }
}
