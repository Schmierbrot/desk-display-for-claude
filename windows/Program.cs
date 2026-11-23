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
        int si = Array.IndexOf(args, "--render-scenarios");
        if (si >= 0 && si + 1 < args.Length) { RenderScenarios(args[si + 1]); return; }
        if (args.Contains("--sessions-test"))
        {
            foreach (var x in new ClaudeData().Sessions())
                Console.WriteLine($"Sitzung: {x.Name} | wartet: {x.Waiting} | Modell: {(x.Model is { } m ? Widgets.ModelName(m) : "-")} | Kontext: {(x.ContextPct is { } c ? c + " %" : "-")} | Branch: {x.Branch ?? "-"} | tut: {x.Activity ?? "-"} | Plan: {x.Done} / {x.Plan.Count}");
            return;
        }
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

    /// <summary>Testbilder: normal · wartet · Limit fast voll · Vorlage „Details“.</summary>
    static void RenderScenarios(string path)
    {
        List<PlanItem> plan = [new("a", "completed"), new("b", "completed"), new("Tests schreiben", "in_progress"), new("d", "pending")];
        var b = new SessionInfo("Mein Projekt", 312, plan, null, 2)
            { Model = "claude-opus-5-5", ContextPct = 63, Branch = "feature/display", Activity = "Bearbeitet Renderer.cs", Index = 1, Total = 2 };
        var use = new Usage(new(42, DateTime.UtcNow.AddSeconds(5340)), new(67, DateTime.UtcNow.AddSeconds(130_000)), null);
        var hot = use with { Five = new(93, DateTime.UtcNow.AddSeconds(2700)) };
        var wait = b with { Waiting = true, Activity = "Hat eine Frage an dich", Total = 1 };
        var views = new[] { (b, use, LayoutPresets.Standard), (wait, use, LayoutPresets.Standard), (b, hot, LayoutPresets.Standard), (b, use, LayoutPresets.Details) };
        using var outBmp = new SKBitmap(980, 245);
        using var oc = new SKCanvas(outBmp);
        oc.Clear(new SKColor(50, 50, 50));
        for (int i = 0; i < views.Length; i++)
        {
            using var c = Renderer.Render(views[i].Item1, views[i].Item2, 2, layout: views[i].Item3);
            oc.DrawBitmap(c.Bitmap, 2 + i * 245, 2);
        }
        using var img = SKImage.FromBitmap(outBmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

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
