namespace ClaudeDisplay.Core;

public enum EngineStatus { Starting, Off, Shown, Standby, Updating, Error }

/// <summary>Momentaufnahme für die Oberfläche.</summary>
public sealed record EngineState(EngineStatus Status, string? Error, byte[] PreviewBgra, SessionInfo? Session, Usage? Usage, DeviceInfo? Device)
{
    public string Title => Status switch
    {
        EngineStatus.Starting => "Verbinde …",
        EngineStatus.Off => "Pausiert",
        EngineStatus.Shown => "Verbunden",
        EngineStatus.Standby => "Anderer Rechner",
        EngineStatus.Updating => "Firmware-Update",
        _ => "Offline",
    };
}

/// <summary>Herzstück: liest jede Sekunde die Claude-Daten, zeichnet und schickt ans Display.</summary>
public sealed class Engine
{
    public static Engine Shared { get; } = new();
    public event Action<EngineState>? Changed;
    public EngineState? State { get; private set; }

    readonly ClaudeData data = new();
    readonly DisplayClient client;
    readonly object gate = new();
    Timer? timer;
    int tick, busy;
    int? lastBrightness;
    DisplayTheme? lastTheme;
    DateTime lastInfo = DateTime.MinValue, backoffUntil = DateTime.MinValue;
    DeviceInfo? device;
    volatile bool paused;

    Engine()
    {
        var s = AppSettings.Shared;
        client = new DisplayClient(s.Host, s.HostId);
    }

    public void Start() => timer ??= new Timer(_ => Step(), null, 0, 1000);

    /// <summary>Nach Änderungen an Helligkeit/Adresse sofort neu anwenden.</summary>
    public void SettingsChanged()
    {
        lock (gate) { lastBrightness = null; lastTheme = null; lastInfo = DateTime.MinValue; backoffUntil = DateTime.MinValue; }
    }

    void Step()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return;     // letzter Durchlauf läuft noch
        try
        {
            lock (gate) StepLocked();
        }
        finally { Interlocked.Exchange(ref busy, 0); }
    }

    void StepLocked()
    {
        var s = AppSettings.Shared;
        var sess = data.CurrentSession();
        var use = data.GetUsage(s.LiveUsage, s.Enabled && !string.IsNullOrWhiteSpace(s.Host) && State?.Status != EngineStatus.Standby);
        tick++;
        using var canvas = Renderer.Render(sess, use, tick);
        var rgb = canvas.Rgb();

        EngineStatus st;
        string? err = null;
        if (paused) st = EngineStatus.Updating;
        else if (!s.Enabled || string.IsNullOrWhiteSpace(s.Host)) { client.Close(); st = EngineStatus.Off; }
        else if (DateTime.UtcNow < backoffUntil) { st = State?.Status ?? EngineStatus.Starting; err = State?.Error; }
        else
        {
            if (client.Host != s.Host) { client.Close(); client.Host = s.Host; lastInfo = DateTime.MinValue; }
            try
            {
                var r = client.Push(rgb, (byte)(sess != null ? 2 : 1));
                st = r == PushResult.Shown ? EngineStatus.Shown : EngineStatus.Standby;
                int b = s.Brightness();
                if (r == PushResult.Shown)
                {
                    if (b != lastBrightness)
                    {
                        client.SetBrightness(b, (int)s.DayBrightness, (int)(s.NightMode ? s.NightBrightness : s.DayBrightness), s.NightStart, s.NightEnd);
                        lastBrightness = b;
                    }
                    var theme = s.DisplayTheme;
                    if (theme != lastTheme) { client.SetTheme(theme); lastTheme = theme; }
                }
                else { lastBrightness = null; lastTheme = null; }
            }
            catch (Exception e)
            {
                st = EngineStatus.Error;
                err = e.Message;
                lastBrightness = null;
                lastTheme = null;
                backoffUntil = DateTime.UtcNow.AddSeconds(3);
            }
            if (DateTime.UtcNow - lastInfo > TimeSpan.FromSeconds(15))
            {
                lastInfo = DateTime.UtcNow;
                device = client.Info();
            }
        }

        // Vorschau als BGRA für Avalonia
        var src = canvas.Bitmap.GetPixelSpan();
        var bgra = new byte[src.Length];
        for (int i = 0; i < src.Length; i += 4)
        {
            bgra[i] = src[i + 2]; bgra[i + 1] = src[i + 1]; bgra[i + 2] = src[i]; bgra[i + 3] = 255;
        }
        if (State?.Status != st) Log.Write($"Status: {st} {err}");
        State = new EngineState(st, err, bgra, sess, use, device);
        Changed?.Invoke(State);
    }

    // ---- Firmware ------------------------------------------------------------

    public async Task<string> UpdateFirmwareAsync(byte[] fw, string filename)
    {
        paused = true;
        lock (gate) client.Close();
        try
        {
            await client.UploadFirmwareAsync(fw, filename);
            await Task.Delay(4000);
            for (int i = 0; i < 40; i++)
            {
                var info = await Task.Run(client.Info);
                if (info is { Uptime: < 60 })
                {
                    device = info;
                    return $"Fertig – Firmware {info.Version} läuft.";
                }
                await Task.Delay(2000);
            }
            return "Display meldet sich nicht zurück. Bitte Stromversorgung prüfen.";
        }
        catch (Exception e) { return "Fehlgeschlagen: " + e.Message; }
        finally
        {
            paused = false;
            SettingsChanged();
        }
    }
}
