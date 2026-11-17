using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;

namespace ClaudeDisplay.Core;

public enum DeviceKind { Ready, Stock }

public sealed record FoundDevice(string Ip, DeviceKind Kind, string Detail);

/// <summary>Sucht Displays im eigenen Netz: unsere Firmware oder SmallTV-Ultra mit Original-Firmware.</summary>
public static class Discovery
{
    static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(1.5) };

    /// <summary>Alle Adressen der eigenen IPv4-Netze (höchstens das /24 um die eigene Adresse).</summary>
    public static List<string> Candidates()
    {
        var result = new HashSet<string>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in nic.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = ua.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue;
                uint ip = (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
                int prefix = Math.Max(ua.PrefixLength, 24);
                uint mask = prefix >= 32 ? 0xFFFFFFFF : ~(0xFFFFFFFFu >> prefix);
                uint net = ip & mask, count = ~mask;
                if (count <= 1 || count >= 1024) continue;
                for (uint h = 1; h < count; h++)
                {
                    uint a = net | h;
                    if (a != ip) result.Add($"{a >> 24}.{(a >> 16) & 255}.{(a >> 8) & 255}.{a & 255}");
                }
            }
        }
        return result.ToList();
    }

    static async Task<JsonElement?> Json(string url)
    {
        try
        {
            using var resp = await http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return null;
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync()).RootElement;
        }
        catch { return null; }
    }

    /// <summary>Prüft eine Adresse.</summary>
    public static async Task<FoundDevice?> Probe(string ip)
    {
        var d = await Json($"http://{ip}/");
        if (d is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty("fw", out var fw) && fw.GetString() == "claude-display")
            return new(ip, DeviceKind.Ready, $"Claude Display · Firmware {(o.TryGetProperty("ver", out var v) ? v.GetString() : "1.x")}");
        var s = await Json($"http://{ip}/v.json");
        if (s is { ValueKind: JsonValueKind.Object } so && so.TryGetProperty("m", out var m) && (m.GetString() ?? "").Contains("SmallTV", StringComparison.OrdinalIgnoreCase))
            return new(ip, DeviceKind.Stock, $"{m.GetString()} · Original-Firmware {(so.TryGetProperty("v", out var sv) ? sv.GetString() : "")}");
        return null;
    }

    /// <summary>Sucht per mDNS (claude-display.local) und im ganzen eigenen Netz.</summary>
    public static async Task<List<FoundDevice>> Scan()
    {
        var found = new Dictionary<string, FoundDevice>();
        if (DisplayClient.Discover() is { } mdns && await Probe(mdns) is { } hit) found[mdns] = hit;
        var ips = Candidates().Where(ip => !found.ContainsKey(ip)).ToList();
        using var gate = new SemaphoreSlim(48);
        var tasks = ips.Select(async ip =>
        {
            await gate.WaitAsync();
            try { return await Probe(ip); } finally { gate.Release(); }
        });
        foreach (var r in await Task.WhenAll(tasks))
            if (r != null) found[r.Ip] = r;
        return found.Values.OrderBy(d => d.Kind == DeviceKind.Ready ? 0 : 1).ThenBy(d => d.Ip).ToList();
    }
}
