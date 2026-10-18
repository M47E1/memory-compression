using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("MemoryCompression")]
[assembly: System.Reflection.AssemblyProduct("Memory Compression")]
[assembly: System.Reflection.AssemblyDescription("手动清理低优先级待机缓存；本地预览自动收尾。")]
[assembly: System.Reflection.AssemblyVersion("1.3.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.3.0.0")]

namespace MemoryCompression
{
    internal sealed class Options
    {
        internal string OptimizerPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "engines", "WindowsMemoryCleaner", "WinMemoryCleaner.exe");
        internal string OptimizerArguments = GentleEngine.Arguments;
        internal int InitialDelaySeconds = 0;
        internal int IntervalSeconds = 200;
        internal string TestJsonPath;
        internal string TestOffscreenPath;
        internal int PreviewPort = PreviewCleanup.DefaultPort;
        internal string PreviewLogDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MemoryCompression", "preview-events");
        internal bool PreviewHostOnly;
        internal int? WindowLeft;
        internal int? WindowTop;
        internal bool StartMinimized;

        internal static Options Parse(string[] args)
        {
            Options options = new Options();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (EqualsOption(arg, "--optimizer")) options.OptimizerPath = RequireValue(args, ref i, arg);
                else if (EqualsOption(arg, "--optimizer-arguments")) options.OptimizerArguments = RequireValue(args, ref i, arg);
                else if (EqualsOption(arg, "--initial-delay")) options.InitialDelaySeconds = ParseNonNegative(RequireValue(args, ref i, arg), arg);
                else if (EqualsOption(arg, "--interval")) options.IntervalSeconds = ParsePositive(RequireValue(args, ref i, arg), arg);
                else if (EqualsOption(arg, "--test-json")) options.TestJsonPath = RequireValue(args, ref i, arg);
                else if (EqualsOption(arg, "--test-offscreen")) options.TestOffscreenPath = RequireValue(args, ref i, arg);
                else if (EqualsOption(arg, "--preview-port")) options.PreviewPort = ParsePositive(RequireValue(args, ref i, arg), arg);
                else if (EqualsOption(arg, "--preview-log-directory")) options.PreviewLogDirectory = RequireValue(args, ref i, arg);
                else if (EqualsOption(arg, "--preview-host-only")) options.PreviewHostOnly = true;
                else if (EqualsOption(arg, "--window-left")) options.WindowLeft = int.Parse(RequireValue(args, ref i, arg));
                else if (EqualsOption(arg, "--window-top")) options.WindowTop = int.Parse(RequireValue(args, ref i, arg));
                else if (EqualsOption(arg, "--start-minimized")) options.StartMinimized = true;
                else throw new ArgumentException("未知参数：" + arg);
            }
            if (!string.Equals(options.OptimizerArguments, GentleEngine.Arguments, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("此版本只允许 /StandbyListLowPriority，不允许全局工作集或其他清理模式。");
            return options;
        }

        private static bool EqualsOption(string actual, string expected)
        {
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static string RequireValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length) throw new ArgumentException(option + " 缺少参数值。");
            index++;
            return args[index];
        }

        private static int ParsePositive(string value, string option)
        {
            int parsed;
            if (!int.TryParse(value, out parsed) || parsed <= 0) throw new ArgumentException(option + " 必须是大于零的整数。");
            return parsed;
        }

        private static int ParseNonNegative(string value, string option)
        {
            int parsed;
            if (!int.TryParse(value, out parsed) || parsed < 0) throw new ArgumentException(option + " 必须是非负整数。");
            return parsed;
        }
    }

    internal static class NativeMemory
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private sealed class MemoryStatus
        {
            internal uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
            internal uint MemoryLoad;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);

        internal static long AvailableMegabytes()
        {
            MemoryStatus status = new MemoryStatus();
            if (!GlobalMemoryStatusEx(status)) return -1;
            return (long)(status.AvailablePhysical / 1024UL / 1024UL);
        }
    }

    internal sealed class HiddenDesktopProcess : IDisposable
    {
        internal Process Process { get; private set; }
        private IntPtr _desktop;

        private HiddenDesktopProcess(Process process, IntPtr desktop)
        {
            Process = process;
            _desktop = desktop;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct StartupInfo
        {
            internal int cb;
            internal string lpReserved;
            internal string lpDesktop;
            internal string lpTitle;
            internal int dwX;
            internal int dwY;
            internal int dwXSize;
            internal int dwYSize;
            internal int dwXCountChars;
            internal int dwYCountChars;
            internal int dwFillAttribute;
            internal int dwFlags;
            internal short wShowWindow;
            internal short cbReserved2;
            internal IntPtr lpReserved2;
            internal IntPtr hStdInput;
            internal IntPtr hStdOutput;
            internal IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessInformation
        {
            internal IntPtr hProcess;
            internal IntPtr hThread;
            internal int dwProcessId;
            internal int dwThreadId;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateDesktop(
            string desktopName,
            string device,
            IntPtr deviceMode,
            int flags,
            uint desiredAccess,
            IntPtr securityAttributes);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseDesktop(IntPtr desktop);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CreateProcess(
            string applicationName,
            StringBuilder commandLine,
            IntPtr processAttributes,
            IntPtr threadAttributes,
            bool inheritHandles,
            uint creationFlags,
            IntPtr environment,
            string currentDirectory,
            ref StartupInfo startupInfo,
            out ProcessInformation processInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);

        internal static HiddenDesktopProcess Start(string fileName, string arguments)
        {
            const uint DesktopAllAccess = 0x01FF;
            const uint CreateNoWindow = 0x08000000;
            const int StartfUseShowWindow = 0x00000001;
            const short SwHide = 0;

            string desktopName = "MemoryCompression-" + Guid.NewGuid().ToString("N");
            IntPtr desktop = CreateDesktop(desktopName, null, IntPtr.Zero, 0, DesktopAllAccess, IntPtr.Zero);
            if (desktop == IntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法创建隐藏桌面，已取消清理以避免显示动效。");

            StartupInfo startup = new StartupInfo();
            startup.cb = Marshal.SizeOf(typeof(StartupInfo));
            startup.lpDesktop = "WinSta0\\" + desktopName;
            startup.dwFlags = StartfUseShowWindow;
            startup.wShowWindow = SwHide;

            ProcessInformation information;
            StringBuilder command = new StringBuilder("\"" + fileName + "\"");
            if (!string.IsNullOrWhiteSpace(arguments)) command.Append(' ').Append(arguments);

            bool created = CreateProcess(
                fileName,
                command,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CreateNoWindow,
                IntPtr.Zero,
                Path.GetDirectoryName(fileName),
                ref startup,
                out information);

            if (!created)
            {
                int error = Marshal.GetLastWin32Error();
                CloseDesktop(desktop);
                throw new System.ComponentModel.Win32Exception(error, "无法在隐藏桌面启动清理引擎。");
            }

            try
            {
                Process process = Process.GetProcessById(information.dwProcessId);
                process.EnableRaisingEvents = true;
                return new HiddenDesktopProcess(process, desktop);
            }
            catch
            {
                CloseDesktop(desktop);
                throw;
            }
            finally
            {
                CloseHandle(information.hThread);
                CloseHandle(information.hProcess);
            }
        }

        public void Dispose()
        {
            if (Process != null)
            {
                Process.Dispose();
                Process = null;
            }
            if (_desktop != IntPtr.Zero)
            {
                CloseDesktop(_desktop);
                _desktop = IntPtr.Zero;
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Options _options;
        private readonly Label _resultValue;
        private readonly Button _toggleButton;
        private readonly Func<EngineResult> _clean;
        private readonly System.Windows.Forms.Timer _timer;
        private readonly CheckBox _previewToggle;
        private readonly Label _previewStatus;
        private PreviewCleanup _previewCleanup;
        private int _previewBusy;
        private int _previewFallbackRequested;
        private bool _closing;
        private bool _optimizerRunning;
        private int _countdown;

        internal MainForm(Options options, bool offscreen, Func<EngineResult> clean = null)
        {
            _options = options;
            _clean = clean ?? (() => GentleEngine.Run(options.OptimizerPath));
            Text = "Memory Compression";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(560, 245);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = offscreen ? FormStartPosition.Manual : FormStartPosition.CenterScreen;
            if (!offscreen && options.WindowLeft.HasValue && options.WindowTop.HasValue)
            {
                StartPosition = FormStartPosition.Manual;
                Location = new Point(options.WindowLeft.Value, options.WindowTop.Value);
                if (options.WindowLeft.Value <= -10000 && options.WindowTop.Value <= -10000) ShowInTaskbar = false;
            }
            if (offscreen)
            {
                Location = new Point(-10000, -10000);
                ShowInTaskbar = false;
            }
            else if (options.StartMinimized) WindowState = FormWindowState.Minimized;

            Controls.Add(CreateLabel("清理引擎", 18, 20));
            Controls.Add(CreateLabel("Windows Memory Cleaner 3.0.8", 92, 20));
            Controls.Add(CreateLabel("清理范围", 18, 48));
            Controls.Add(CreateLabel("仅低优先级待机缓存，不清程序工作集", 92, 48));
            Controls.Add(CreateLabel("运行方式", 18, 76));
            Controls.Add(CreateLabel("手动；不定时、不在启动时清内存", 92, 76));
            Controls.Add(CreateLabel("本次结果", 18, 111));
            _resultValue = new Label { Left = 92, Top = 109, Width = 314, Height = 42, Text = File.Exists(options.OptimizerPath) ? "尚未清理" : "可选引擎未安装；预览收尾不受影响" };
            _resultValue.AccessibleName = "内存清理结果";
            Controls.Add(_resultValue);
            _toggleButton = new Button { Left = 424, Top = 105, Width = 116, Height = 34, Text = "清理一次" };
            _toggleButton.AccessibleName = "清理一次低优先级待机缓存";
            _toggleButton.Click += ToggleButtonClick;
            Controls.Add(_toggleButton);
            _previewToggle = new CheckBox { Left = 18, Top = 159, Width = 520, Height = 24, Text = "关闭最后一个 localhost 预览页时停止对应服务", Checked = true };
            _previewToggle.CheckedChanged += delegate
            {
                if (_previewCleanup != null) _previewCleanup.SetEnabled(_previewToggle.Checked);
                if (!_previewToggle.Checked) _previewStatus.Text = "预览收尾：已暂停";
            };
            Controls.Add(_previewToggle);
            _previewStatus = new Label { Left = 18, Top = 190, Width = 520, Height = 24, Text = "预览收尾：等待浏览器扩展连接" };
            Controls.Add(_previewStatus);
            Controls.Add(CreateLabel("管理员确认仅在手动清理时出现；预览收尾独立运行。", 18, 219));

            _countdown = options.IntervalSeconds;
            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += TimerTick;
            Shown += delegate
            {
                if (!offscreen)
                {
                    try { _previewCleanup = new PreviewCleanup(options.PreviewPort, options.PreviewLogDirectory); }
                    catch (Exception ex) { _previewStatus.Text = "预览收尾：未能启动，请检查本地端口或记录目录"; _previewStatus.AccessibleDescription = ex.Message; }
                    _timer.Start();
                }
            };
        }

        internal string ResultText { get { return _resultValue.Text; } }
        internal string[] Labels { get { return new[] { "清理引擎", "清理范围", "运行方式", "本次结果" }; } }
        internal int IntervalSeconds { get { return _options.IntervalSeconds; } }
        internal bool OptimizerStarted { get { return _optimizerRunning; } }
        internal bool PreviewBridgeStarted { get { return _previewCleanup != null; } }
        internal bool PreviewCleanupEnabled { get { return _previewToggle.Checked; } }
        protected override bool ShowWithoutActivation { get { return true; } }

        private static Label CreateLabel(string text, int left, int top)
        {
            return new Label { AutoSize = true, Left = left, Top = top, Text = text };
        }

        private void ToggleButtonClick(object sender, EventArgs e)
        {
            RunOptimizer();
        }

        private void TimerTick(object sender, EventArgs e)
        {
            bool fallback = --_countdown <= 0;
            if (fallback) _countdown = _options.IntervalSeconds;
            QueuePreviewTick(fallback);
            if (_previewCleanup != null) _previewStatus.Text = _previewCleanup.Status;
        }

        private void RunOptimizer()
        {
            if (_optimizerRunning || _closing) return;
            _optimizerRunning = true;
            _toggleButton.Enabled = false;
            _resultValue.Text = "等待管理员确认／清理低优先级缓存…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                EngineResult result;
                try { result = _clean(); }
                catch (Exception ex) { result = new EngineResult { Message = "清理失败：" + ex.Message }; }
                try
                {
                    if (_closing || IsDisposed) return;
                    BeginInvoke((MethodInvoker)delegate
                    {
                        _optimizerRunning = false;
                        _toggleButton.Enabled = true;
                        _resultValue.Text = result.Message;
                    });
                }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
        }

        private void QueuePreviewTick(bool fallback)
        {
            if (_closing || !_previewToggle.Checked || _previewCleanup == null) return;
            if (fallback) Interlocked.Exchange(ref _previewFallbackRequested, 1);
            if (Interlocked.CompareExchange(ref _previewBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { if (!_closing) _previewCleanup.Tick(Interlocked.Exchange(ref _previewFallbackRequested, 0) != 0); }
                finally { Interlocked.Exchange(ref _previewBusy, 0); }
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _closing = true;
                _timer.Dispose();
                if (_previewCleanup != null) _previewCleanup.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal static class TestReport
    {
        internal static void Write(string path, Options options, MainForm form, string mode)
        {
            StringBuilder json = new StringBuilder();
            json.Append("{");
            Append(json, "schema_version", "2.0", true);
            Append(json, "mode", mode, true);
            Append(json, "title", form.Text, true);
            Append(json, "result_text", form.ResultText, true);
            Append(json, "optimizer_path", options.OptimizerPath, true);
            Append(json, "optimizer_arguments", options.OptimizerArguments, true);
            json.Append("\"initial_delay_seconds\":").Append(options.InitialDelaySeconds).Append(',');
            json.Append("\"interval_seconds\":").Append(form.IntervalSeconds).Append(',');
            json.Append("\"startup_cleanup\":false,");
            json.Append("\"automatic_memory_cleanup\":false,");
            json.Append("\"manual_only\":true,");
            json.Append("\"optimizer_configured\":").Append(File.Exists(options.OptimizerPath) ? "true" : "false").Append(',');
            json.Append("\"animated_countdown\":false,");
            json.Append("\"preview_cleanup_enabled\":").Append(form.PreviewCleanupEnabled ? "true" : "false").Append(',');
            json.Append("\"preview_bridge_started\":").Append(form.PreviewBridgeStarted ? "true" : "false").Append(',');
            json.Append("\"optimizer_started\":").Append(form.OptimizerStarted ? "true" : "false");
            json.Append("}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
            if (mode == "offscreen")
            {
                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.ChangeExtension(path, ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        private static void Append(StringBuilder json, string name, string value, bool comma)
        {
            json.Append('"').Append(Escape(name)).Append("\":\"").Append(Escape(value)).Append('"');
            if (comma) json.Append(',');
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Options options;
            try { options = Options.Parse(args); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Memory Compression", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 2;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (options.PreviewHostOnly)
            {
                using (PreviewCleanup cleanup = new PreviewCleanup(options.PreviewPort, options.PreviewLogDirectory))
                {
                    int countdown = options.IntervalSeconds;
                    while (true)
                    {
                        Thread.Sleep(1000);
                        bool fallback = --countdown <= 0;
                        cleanup.Tick(fallback);
                        if (fallback) countdown = options.IntervalSeconds;
                    }
                }
            }

            bool testMode = !string.IsNullOrEmpty(options.TestJsonPath) || !string.IsNullOrEmpty(options.TestOffscreenPath);
            bool createdNew = false;
            using (Mutex mutex = new Mutex(true, "Local\\MemoryCompression-v1", out createdNew))
            {
                if (!testMode && !createdNew)
                {
                    MessageBox.Show("Memory Compression 已经在运行，请从任务栏恢复窗口。", "Memory Compression", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                if (!string.IsNullOrEmpty(options.TestJsonPath))
                {
                    using (MainForm form = new MainForm(options, true)) TestReport.Write(options.TestJsonPath, options, form, "headless");
                    return 0;
                }

                if (!string.IsNullOrEmpty(options.TestOffscreenPath))
                {
                    using (MainForm form = new MainForm(options, true))
                    {
                        form.Shown += delegate
                        {
                            TestReport.Write(options.TestOffscreenPath, options, form, "offscreen");
                            form.BeginInvoke((MethodInvoker)delegate { form.Close(); });
                        };
                        Application.Run(form);
                    }
                    return 0;
                }

                using (MainForm form = new MainForm(options, false)) Application.Run(form);
                return 0;
            }
        }
    }
}
