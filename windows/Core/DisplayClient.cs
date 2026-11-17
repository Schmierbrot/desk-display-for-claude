using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace ClaudeDisplay.Core;

public sealed record DeviceInfo(string Version, string Ip, int Rssi, int Uptime, int Brightness, string Mac, uint Owner);

public enum PushResult { Shown, Standby }

/// <summary>Spricht das Pixel-Protokoll der Firmware (Port 7777) und die HTTP-Endpunkte.</summary>
public sealed class DisplayClient : IDisposable
{
    public string Host { get; set; }
    readonly uint hostId;
    TcpClient? tcp;
    NetworkStream? stream;
    byte[]? last;          // was gerade auf dem Display steht (RGB)
    bool owner;
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public DisplayClient(string host, uint hostId) { Host = host; this.hostId = hostId; }

    public void Dispose() => Close();

    public void Close()
    {
        try { stream?.Dispose(); tcp?.Dispose(); } catch { }
        stream = null; tcp = null; last = null; owner = false;
    }

    public PushResult Push(byte[] rgb, byte prio)
    {
        if (stream == null) Connect();
        try { return PushOnce(rgb, prio); }
        catch { Close(); throw; }
    }

    PushResult PushOnce(byte[] rgb, byte prio)
    {
        if (!owner)
        {
            // nur anklopfen – Vollbilder erst, wenn wir dran sind
            var v = Heartbeat(prio);
            if (v != 'K' && v != 'R') return PushResult.Standby;
            SendRect(rgb, (0, 0, 240, 240), prio, true);
            return owner ? PushResult.Shown : PushResult.Standby;
        }
        var box = DiffBox(last, rgb);
        if (box == null)
        {
            var v = Heartbeat(prio);
            if (v == 'K') return PushResult.Shown;
            owner = false; last = null;
            if (v == 'R') { SendRect(rgb, (0, 0, 240, 240), prio, true); return owner ? PushResult.Shown : PushResult.Standby; }
            return PushResult.Standby;
        }
        var r = SendRect(rgb, box.Value, prio, false);
        if (r == 'R') SendRect(rgb, (0, 0, 240, 240), prio, true);
        return owner ? PushResult.Shown : PushResult.Standby;
    }

    char Heartbeat(byte prio)
    {
        var msg = new byte[6];
        msg[0] = (byte)'H';
        WriteBe32(msg, 1, hostId);
        msg[5] = prio;
        stream!.Write(msg);
        return ReadVerdict();
    }

    char SendRect(byte[] rgb, (int x, int y, int w, int h) b, byte prio, bool full)
    {
        var msg = new byte[14 + b.w * b.h * 2];
        msg[0] = (byte)'Q';
        WriteBe32(msg, 1, hostId);
        msg[5] = prio;
        WriteBe16(msg, 6, b.x); WriteBe16(msg, 8, b.y); WriteBe16(msg, 10, b.w); WriteBe16(msg, 12, b.h);
        int o = 14;
        for (int row = b.y; row < b.y + b.h; row++)
        {
            int i = (row * 240 + b.x) * 3;
            for (int k = 0; k < b.w; k++, i += 3)
            {
                int v = (rgb[i] & 0xF8) << 8 | (rgb[i + 1] & 0xFC) << 3 | rgb[i + 2] >> 3;
                msg[o++] = (byte)(v >> 8); msg[o++] = (byte)v;
            }
        }
        stream!.Write(msg);
        var verdict = ReadVerdict();
        switch (verdict)
        {
            case 'K': owner = true; last = rgb; break;
            case 'B': owner = false; last = null; break;
            case 'R':
                owner = false; last = null;
                if (full) throw new IOException("Unerwartete Antwort vom Display");
                break;
            default: throw new IOException("Unerwartete Antwort vom Display");
        }
        return verdict;
    }

    static (int, int, int, int)? DiffBox(byte[]? a, byte[] b)
    {
        if (a == null) return (0, 0, 240, 240);
        int x0 = 240, y0 = 240, x1 = -1, y1 = -1;
        for (int y = 0; y < 240; y++)
            for (int x = 0; x < 240; x++)
            {
                int i = (y * 240 + x) * 3;
                if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2])
                {
                    if (x < x0) x0 = x; if (x > x1) x1 = x;
                    if (y < y0) y0 = y; if (y > y1) y1 = y;
                }
            }
        return x1 < 0 ? null : (x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    void Connect()
    {
        var c = new TcpClient { NoDelay = true, ReceiveTimeout = 5000, SendTimeout = 5000 };
        try
        {
            if (!c.ConnectAsync(Host, 7777).Wait(TimeSpan.FromSeconds(5))) throw new IOException("Display nicht erreichbar");
        }
        catch (Exception e) when (e is not IOException)
        {
            c.Dispose();
            throw new IOException("Display nicht erreichbar");
        }
        tcp = c; stream = c.GetStream(); last = null; owner = false;
    }

    char ReadVerdict()
    {
        int b = stream!.ReadByte();
        if (b < 0) throw new IOException("Verbindung abgebrochen");
        return (char)b;
    }

    static void WriteBe16(byte[] m, int o, int v) { m[o] = (byte)(v >> 8); m[o + 1] = (byte)v; }
    static void WriteBe32(byte[] m, int o, uint v) { m[o] = (byte)(v >> 24); m[o + 1] = (byte)(v >> 16); m[o + 2] = (byte)(v >> 8); m[o + 3] = (byte)v; }

    // ---- HTTP ----------------------------------------------------------------

    public DeviceInfo? Info()
    {
        try
        {
            var d = JsonDocument.Parse(http.GetStringAsync($"http://{Host}/").Result).RootElement;
            if (d.GetProperty("fw").GetString() != "claude-display") return null;
            string S(string k, string def) => d.TryGetProperty(k, out var v) ? v.GetString() ?? def : def;
            int I(string k) => d.TryGetProperty(k, out var v) && v.TryGetInt32(out var n) ? n : 0;
            uint owner = d.TryGetProperty("owner", out var o) && o.TryGetUInt32(out var u) ? u : 0;
            return new DeviceInfo(S("ver", "1.x"), S("ip", Host), I("rssi"), I("uptime"), I("brt"), S("mac", ""), owner);
        }
        catch { return null; }
    }

    /// <summary>Setzt die Helligkeit und hinterlegt den Plan für die Zeit ohne Rechner.</summary>
    public void SetBrightness(int v, int day, int night, int nightStart, int nightEnd)
    {
        var q = $"v={Math.Clamp(v, 0, 100)}&d={day}&n={night}&ns={nightStart}&ne={nightEnd}";
        try { http.GetAsync($"http://{Host}/brt?{q}").Wait(5000); } catch { }
    }

    /// <summary>Schickt das Theme für die Uhr ohne Rechner (ab Firmware 1.0).</summary>
    public void SetTheme(DisplayTheme t)
    {
        var q = $"a={t.Accent.HexString}&b={t.Accent2.HexString}&t={t.Text.HexString}&m={t.Muted.HexString}" +
                $"&d={t.Dim.HexString}&e={t.Edge.HexString}&logo={(int)t.Logo}&title={Uri.EscapeDataString(t.Title)}";
        try { http.GetAsync($"http://{Host}/theme?{q}").Wait(5000); } catch { }
    }

    /// <summary>Lädt eine Firmware (.bin oder .bin.gz) über /update hoch.</summary>
    public async Task UploadFirmwareAsync(byte[] data, string filename)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "firmware", filename);
        var resp = await client.PostAsync($"http://{Host}/update", form);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode || !text.Contains("Success"))
            throw new IOException(string.IsNullOrWhiteSpace(text) ? "Update fehlgeschlagen" : text.Trim());
    }

    /// <summary>Sucht das Display per mDNS (claude-display.local).</summary>
    public static string? Discover()
    {
        try
        {
            var task = Dns.GetHostAddressesAsync("claude-display.local");
            if (!task.Wait(TimeSpan.FromSeconds(5))) return null;
            return task.Result.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        }
        catch { return null; }
    }
}
