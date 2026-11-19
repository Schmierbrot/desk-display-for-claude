using Avalonia;
using ClaudeDisplay.Core;
using SkiaSharp;

namespace ClaudeDisplay;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // ClaudeDisplay --render-test datei.png  -> Beispielansichten zeichnen (zum Prüfen ohne Display)
        int i = Array.IndexOf(args, "--render-test");
        if (i >= 0 && i + 1 < args.Length) { RenderTest(args[i + 1]); return; }
        if (args.Contains("--cloud-test"))
        {
            // Cloud-Abfrage prüfen; mit „bridge“ auch lokale Sitzungen mit Fernsteuerung
            CloudSessions.Shared.IncludeBridge = args.Contains("bridge");
            CloudSessions.Shared.Tick();
            foreach (var x in CloudSessions.Shared.Running())
                Console.WriteLine($"Sitzung: {x.Name} | Laufzeit {x.Secs} s | erledigt {x.Done} / {x.Plan.Count} | Schritt: {x.CurrentStep ?? "-"} | Eingabe: {(x.Prompt ?? "-")[..Math.Min(40, (x.Prompt ?? "-").Length)]}");
            if (CloudSessions.Shared.Running().Count == 0) Console.WriteLine("keine laufende Cloud-Sitzung");
            return;
        }
        if (args.Contains("--usage-test")) { Console.WriteLine(new ClaudeData().GetUsage(true)?.ToString() ?? "kein Limit (siehe log.txt)"); return; }

        // nur eine Instanz pro Benutzer
        using var mutex = new Mutex(true, "ClaudeDisplay-" + Environment.UserName, out bool first);
        if (!first) return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    static void RenderTest(string path)
    {
        var use = new Usage(new(32, DateTime.UtcNow.AddMinutes(89)), new(62, DateTime.UtcNow.AddHours(36)), null);
        var sess = new SessionInfo("Mein Projekt", 159,
            [new("a", "completed"), new("b", "completed"), new("Windows-App bauen und schön machen", "in_progress"), new("d", "pending")],
            null, 2);
        using var a = Renderer.Render(sess, use, 3);
        using var b = Renderer.Render(null, use, 0);
        using var surface = SKSurface.Create(new SKImageInfo(980, 490));
        var c = surface.Canvas;
        c.Clear(SKColors.White);
        using var paint = new SKPaint { FilterQuality = SKFilterQuality.None };
        c.DrawBitmap(a.Bitmap, new SKRect(5, 5, 485, 485), paint);
        c.DrawBitmap(b.Bitmap, new SKRect(495, 5, 975, 485), paint);
        using var img = surface.Snapshot();
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
