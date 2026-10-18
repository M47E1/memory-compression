using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using MemoryCompression;

internal static class RuntimeTests
{
    private static int _assertions;

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _assertions++;
    }

    private static void Run(string name, Options options, string output, Func<MainForm, bool> ready, Action<MainForm> verify)
    {
        options.PreviewPort = 0;
        options.PreviewLogDirectory = Path.Combine(output, "runtime", name);
        options.WindowLeft = -10000;
        options.WindowTop = -10000;
        options.IntervalSeconds = 2;
        DateTime started = DateTime.UtcNow;
        Exception failure = null;
        using (MainForm form = new MainForm(options, false))
        using (Timer timer = new Timer { Interval = 100 })
        {
            form.ShowInTaskbar = false;
            timer.Tick += delegate
            {
                try
                {
                    if ((DateTime.UtcNow - started).TotalSeconds > 12) throw new Exception("Runtime test timed out: " + name);
                    if ((DateTime.UtcNow - started).TotalSeconds < 2.5 || !ready(form)) return;
                    Check(form.IsRunning, name + ": optimizer state paused preview cleanup.");
                    Check(form.PreviewBridgeStarted && form.PreviewCleanupEnabled, name + ": preview bridge is unavailable.");
                    Check(form.Left == -10000 && form.Top == -10000 && !form.ShowInTaskbar, name + ": test UI is not off-screen.");
                    verify(form);
                    Console.WriteLine("PASS runtime: " + name);
                    timer.Stop();
                    form.Close();
                }
                catch (Exception ex) { failure = ex; timer.Stop(); form.Close(); }
            };
            form.Shown += delegate { timer.Start(); };
            Application.Run(form);
        }
        if (failure != null) throw failure;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            string output = args[0];
            string stub = args[1];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Options defaults = Options.Parse(new string[0]);
            Check(defaults.OptimizerPath == string.Empty && defaults.OptimizerArguments == string.Empty, "Default startup requires an external engine.");
            Check(defaults.PreviewLogDirectory == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryCompression", "preview-events"), "Default data directory is not user-relative.");
            Check(defaults.PreviewPort == 47832, "Unexpected default bridge port.");
            Run("no-engine", defaults, output, f => true, f => Check(f.ResultText.Contains("未配置引擎") && !f.OptimizerStarted, "No-engine mode did not remain active."));
            Run("missing-engine", new Options { OptimizerPath = Path.Combine(output, "not-installed", "engine.exe") }, output,
                f => true, f => Check(f.ResultText.Contains("引擎不存在") && !f.OptimizerStarted, "Missing engine blocked preview cleanup."));
            string invalid = Path.Combine(output, "not-an-executable.exe");
            File.WriteAllText(invalid, "This is a test, not an executable.");
            Run("invalid-engine", new Options { OptimizerPath = invalid }, output,
                f => true, f => Check(f.ResultText.Contains("引擎启动失败") && !f.OptimizerStarted, "Invalid engine blocked preview cleanup."));
            foreach (int exitCode in new[] { 0, 7 })
            {
                string marker = Path.Combine(output, "engine-" + exitCode + ".txt");
                Run("configured-engine-" + exitCode,
                    new Options { OptimizerPath = stub, OptimizerArguments = "\"" + marker + "\" 0 " + exitCode }, output,
                    f => File.Exists(marker) && !f.OptimizerStarted,
                    f => Check(f.ResultText.Contains(exitCode == 0 ? "引擎完成" : "引擎返回失败"), "Configured engine result was not reported."));
            }
            string slowMarker = Path.Combine(output, "slow-engine.txt");
            Run("busy-engine", new Options { OptimizerPath = stub, OptimizerArguments = "\"" + slowMarker + "\" 3500" }, output,
                f => File.Exists(slowMarker) && !f.OptimizerStarted,
                f => Check(File.ReadAllLines(slowMarker).Length == 1, "A running external engine was started twice."));
            Console.WriteLine("PASS runtime: " + _assertions + " assertions; preview cleanup survives absent, invalid, failing, and busy optional engines.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
