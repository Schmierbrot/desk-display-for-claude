using System.Text.Json.Serialization;
using SkiaSharp;

namespace ClaudeDisplay.Core;

/// <summary>Farbe als 8-Bit-RGB (speicherbar, als Hex darstellbar).</summary>
public readonly record struct RGB(int R, int G, int B)
{
    public static RGB Hex(string hex)
    {
        int v = Convert.ToInt32(hex.Trim('#', ' '), 16);
        return new((v >> 16) & 255, (v >> 8) & 255, v & 255);
    }
    [JsonIgnore] public string HexString => $"{R:X2}{G:X2}{B:X2}";
    [JsonIgnore] public SKColor Sk => new((byte)R, (byte)G, (byte)B);
    [JsonIgnore] public Avalonia.Media.Color Ui => Avalonia.Media.Color.FromRgb((byte)R, (byte)G, (byte)B);
    public static RGB FromUi(Avalonia.Media.Color c) => new(c.R, c.G, c.B);
    /// <summary>Mischung: t = 0 → this, t = 1 → o</summary>
    public RGB Mix(RGB o, double t) => new((int)(R + (o.R - R) * t), (int)(G + (o.G - G) * t), (int)(B + (o.B - B) * t));
}

public enum LogoStyle { Ring, Spark, Dot, None }

/// <summary>Aussehen des Displays.</summary>
public sealed record DisplayTheme(RGB Accent, RGB Accent2, RGB Text, RGB Muted, RGB Dim, RGB Edge, RGB Track,
                                  RGB Good, RGB Warn, RGB Bad, LogoStyle Logo, string Title)
{
    public RGB ForPct(int p) => p < 60 ? Good : p < 85 ? Warn : Bad;
}

/// <summary>Aussehen der App.</summary>
public sealed record AppTheme(RGB Accent, RGB Accent2, RGB Background, RGB Surface);

public sealed record ThemePreset(string Id, string Name, DisplayTheme Display, AppTheme App);

public static class Themes
{
    static RGB H(string s) => RGB.Hex(s);

    public static readonly ThemePreset[] Presets =
    [
        new("neon", "Neon",
            new(H("5B8CFF"), H("B06BFF"), H("EEF1FF"), H("9AA3C7"), H("646C90"), H("3A3F5C"), H("2A2E44"),
                H("4BE3A1"), H("FFC857"), H("FF5C7A"), LogoStyle.Ring, "Claude Code"),
            new(H("5B8CFF"), H("B06BFF"), H("0B0D14"), H("161A27"))),
        new("claude", "Claude",
            new(H("DE7A58"), H("F5A07A"), H("F5F0E8"), H("9E958A"), H("686159"), H("3E3933"), H("34302C"),
                H("80CD98"), H("ECBC5C"), H("EA685C"), LogoStyle.Spark, "Claude"),
            new(H("DE7A58"), H("F5A07A"), H("141210"), H("201C19"))),
        new("aurora", "Aurora",
            new(H("2DE2C5"), H("7CF29A"), H("EAFBF6"), H("93B8AE"), H("5E7A73"), H("2F4A44"), H("22332F"),
                H("7CF29A"), H("FFD166"), H("FF6B6B"), LogoStyle.Ring, "Claude Code"),
            new(H("2DE2C5"), H("7CF29A"), H("08110F"), H("12211D"))),
        new("sunset", "Sunset",
            new(H("FF6B9A"), H("FFB86B"), H("FFF1EC"), H("C7A39A"), H("8A6A63"), H("4F3A36"), H("3A2A28"),
                H("7BE0A0"), H("FFB86B"), H("FF5C7A"), LogoStyle.Ring, "Claude Code"),
            new(H("FF6B9A"), H("FFB86B"), H("140B10"), H("23161C"))),
        new("mono", "Mono",
            new(H("FFFFFF"), H("9A9A9A"), H("FFFFFF"), H("A0A0A0"), H("6A6A6A"), H("3C3C3C"), H("2A2A2A"),
                H("3DDC97"), H("FFD166"), H("FF5C5C"), LogoStyle.Dot, "Claude Code"),
            new(H("F2F2F2"), H("8C8C8C"), H("0A0A0A"), H("181818"))),
    ];

    public static ThemePreset Preset(string id) => Presets.FirstOrDefault(p => p.Id == id) ?? Presets[0];
}
