using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MemoryCompression;

internal static class GentleEngineTests
{
    private static int _checks;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    [STAThread]
    private static int Main()
    {
        Options options = Options.Parse(new[] { "--interval", "1", "--initial-delay", "0" });
        Check(options.OptimizerPath.EndsWith(@"engines\WindowsMemoryCleaner\WinMemoryCleaner.exe"), "The old engine is still the default.");
        Check(options.OptimizerArguments == "/StandbyListLowPriority", "The default memory area is not low priority only.");
        foreach (string unsafeArgs in new[] { "/WorkingSet", "/StandbyList", "/optimize", "/StandbyListLowPriority /WorkingSet", "/Install", "" })
            Reject(() => Options.Parse(new[] { "--optimizer-arguments", unsafeArgs }), "An unsafe engine command was accepted: " + unsafeArgs);
        ProcessStartInfo info = GentleEngine.StartInfo(@"C:\Test With Spaces\WinMemoryCleaner.exe", false);
        Check(info.Arguments == GentleEngine.Arguments, "Engine arguments were expanded.");
        Check(info.Verb == "runas" && info.UseShellExecute, "A normal UAC confirmation was not requested.");
        Check(GentleEngine.StartInfo(info.FileName, true).Verb == "", "An already elevated invocation requested redundant elevation.");
        Check(info.WindowStyle == ProcessWindowStyle.Hidden, "The silent engine would open a regular window.");
        bool invalid = false;
        try { GentleEngine.Verify(new MemoryStream(Encoding.UTF8.GetBytes("not the official executable"))); }
        catch (InvalidDataException) { invalid = true; }
        Check(invalid, "An unverified executable was accepted.");
        Check(!GentleEngine.Run(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe")).Confirmed, "A missing engine reported success.");
        string ok = "{\"level\":\"Information\",\"method\":\"Optimize\",\"data\":{\"memoryAreas\":[{\"name\":\"Standby List (low priority)\"}]}}";
        string fail = "{\"level\":\"Error\",\"method\":\"Optimize\",\"message\":\"denied\",\"data\":{\"memoryAreas\":[{\"name\":\"Standby List (low priority)\",\"error\":\"access denied\"}]}}";
        Check(GentleEngine.EvaluateLogs(new[] { ok }).Confirmed, "A successful engine record was not recognized.");
        Check(GentleEngine.EvaluateLogs(new[] { "Trace prefix: " + ok }).Confirmed, "A classic Event Log prefix was not recognized.");
        Check(!GentleEngine.EvaluateLogs(new[] { fail, ok }).Confirmed, "A success overrode an earlier denial.");
        Check(!GentleEngine.EvaluateLogs(new[] { ok, fail }).Confirmed, "A later denial was ignored.");
        Check(!GentleEngine.EvaluateLogs(new[] { "{}", "invalid" }).Confirmed, "Incomplete records reported success.");
        Check(!GentleEngine.EvaluateLogs(new[] { ok.Replace("\"Optimize\"", "\"Initialize\"") }).Confirmed, "Startup logging was mistaken for a completed clean.");
        Check(!GentleEngine.EvaluateLogs(new[] { ok.Replace("[{", "[{\"name\":\"Working Set\"},{") }).Confirmed, "An unexpected extra memory area was accepted.");

        Application.EnableVisualStyles();
        int calls = 0;
        using (MainForm form = new MainForm(options, true, () => { Interlocked.Increment(ref calls); return new EngineResult { Confirmed = true, Message = "test complete" }; }))
        {
            form.Show();
            Application.DoEvents();
            MethodInfo tick = typeof(MainForm).GetMethod("TimerTick", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 1000; i++) tick.Invoke(form, new object[] { null, EventArgs.Empty });
            Check(calls == 0, "Startup or timer ticks launched memory cleaning.");
            Check(form.PreviewCleanupEnabled, "Manual-only memory mode disabled localhost cleanup.");
            Button button = form.Controls.OfType<Button>().Single(b => b.Text == "清理一次");
            Check(button.Enabled, "The manual button is unavailable.");
            button.PerformClick();
            Stopwatch wait = Stopwatch.StartNew();
            while (form.OptimizerStarted && wait.ElapsedMilliseconds < 3000) { Application.DoEvents(); Thread.Sleep(10); }
            Check(calls == 1 && !form.OptimizerStarted, "One manual click did not finish exactly one clean.");
            Check(form.ResultText == "test complete", "The engine result did not reach the UI.");
            Check(form.PreviewCleanupEnabled, "Manual cleaning changed localhost cleanup.");
            for (int i = 0; i < 1000; i++) tick.Invoke(form, new object[] { null, EventArgs.Empty });
            Check(calls == 1, "A completed clean rearmed an automatic memory timer.");
            form.Close();
        }
        Console.WriteLine("PASS: " + _checks + " gentle engine and manual-only UI assertions.");
        return 0;
    }
}
