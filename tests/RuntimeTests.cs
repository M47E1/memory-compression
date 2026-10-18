using System;
using System.IO;
using System.Linq;
using System.Threading;
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
    private static void Run(string name, Options options, string output, Func<EngineResult> clean, bool click, double waitSeconds, Action<MainForm,int> verify)
    {
        options.PreviewPort = 0;
        options.PreviewLogDirectory = Path.Combine(output, "runtime", name);
        options.WindowLeft = -10000;
        options.WindowTop = -10000;
        options.IntervalSeconds = 1;
        options.InitialDelaySeconds = 0;
        DateTime started = DateTime.UtcNow;
        Exception failure = null;
        int calls = 0;
        bool clicked = false;
        using (MainForm form = new MainForm(options, false, () => { Interlocked.Increment(ref calls); return clean(); }))
        using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 100 })
        {
            form.ShowInTaskbar = false;
            timer.Tick += delegate
            {
                try
                {
                    double elapsed = (DateTime.UtcNow-started).TotalSeconds;
                    if (elapsed > 12) throw new Exception("Runtime test timed out: " + name);
                    Button button = form.Controls.OfType<Button>().Single(b => b.Text == "清理一次");
                    if (click && !clicked) { button.PerformClick(); clicked = true; }
                    if (elapsed < waitSeconds) return;
                    Check(form.PreviewBridgeStarted && form.PreviewCleanupEnabled, name + ": preview cleanup stopped.");
                    Check(form.Left == -10000 && form.Top == -10000 && !form.ShowInTaskbar, name + ": test UI is not off-screen.");
                    verify(form,calls);
                    Console.WriteLine("PASS runtime: " + name);
                    timer.Stop();
                    form.Close();
                }
                catch (Exception ex) { failure=ex;timer.Stop();form.Close(); }
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
            string output=args[0];
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Options defaults=Options.Parse(new string[0]);
            Check(defaults.OptimizerArguments == "/StandbyListLowPriority", "Unexpected engine mode.");
            Check(defaults.PreviewPort == 47832, "Unexpected public bridge port.");
            Check(defaults.PreviewLogDirectory == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryCompression", "preview-events"), "Data directory is not user-relative.");
            Run("startup-and-timer",defaults,output,()=>new EngineResult{Message="unexpected"},false,2.5,
                (f,c)=>Check(c==0 && !f.OptimizerStarted,"Startup or timer started memory cleanup."));
            string missing=Path.Combine(output,"missing","WinMemoryCleaner.exe");
            Run("missing-engine",new Options{OptimizerPath=missing},output,()=>GentleEngine.Run(missing),true,2.5,
                (f,c)=>Check(c==1 && !f.OptimizerStarted && f.ResultText.Contains("未找到"),"A missing engine was not handled independently."));
            string invalid=Path.Combine(output,"not-an-engine.exe");
            File.WriteAllText(invalid,"This is not an executable.");
            Run("invalid-engine",new Options{OptimizerPath=invalid},output,()=>GentleEngine.Run(invalid),true,2.5,
                (f,c)=>Check(c==1 && !f.OptimizerStarted && f.ResultText.Contains("已取消启动"),"An unverified executable was not refused."));
            foreach(string outcome in new[]{"failed","cancelled","success"})
            {
                string message=outcome;
                Run("manual-"+outcome,new Options(),output,()=>new EngineResult{Confirmed=message=="success",Message=message},true,2.5,
                    (f,c)=>Check(c==1 && !f.OptimizerStarted && f.ResultText==message,"A manual result was lost or rearmed the memory timer."));
            }
            Run("busy-engine",new Options(),output,()=>{Thread.Sleep(4500);return new EngineResult{Message="done"};},true,2.5,
                (f,c)=>{
                    Check(c==1 && f.OptimizerStarted,"The simulated engine was not busy.");
                    Button button=f.Controls.OfType<Button>().Single(b=>b.Text=="清理一次");
                    Check(!button.Enabled,"The running engine could be started twice.");
                    button.PerformClick();
                    Check(c==1,"A second click started a concurrent engine.");
                });
            Console.WriteLine("PASS runtime: "+_assertions+" assertions; manual-only memory cleanup and independent live preview timers.");
            return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}