using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace MemoryCompression
{
    internal sealed class BrowserObservation
    {
        public int schema_version { get; set; }
        public string source_id { get; set; }
        public long source_sequence { get; set; }
        public string observed_at_utc { get; set; }
        public int[] open_ports { get; set; }
    }

    internal sealed class PreviewProcess
    {
        public int pid { get; set; }
        public string created { get; set; }
        public string executable { get; set; }
        public int parent_pid { get; set; }
    }

    internal sealed class PreviewCandidate
    {
        public int port { get; set; }
        public PreviewProcess[] processes { get; set; }
    }

    internal sealed class PreviewEvent
    {
        public int schema_version { get; set; }
        public long sequence { get; set; }
        public string event_id { get; set; }
        public string time_utc { get; set; }
        public string run_id { get; set; }
        public string type { get; set; }
        public string source_id { get; set; }
        public long source_sequence { get; set; }
        public int[] open_ports { get; set; }
        public PreviewCandidate candidate { get; set; }
        public int port { get; set; }
        public string reason { get; set; }
    }

    // This is an append-only record, not a mutable list of past actions.
    internal sealed class PreviewJournal
    {
        private readonly string _path;
        private readonly string _runId = Guid.NewGuid().ToString("N");
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private long _sequence;

        internal PreviewJournal(string directory)
        {
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "preview-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "-" + _runId + ".jsonl");
        }

        internal void Append(PreviewEvent item)
        {
            item.schema_version = 1;
            item.sequence = ++_sequence;
            item.event_id = Guid.NewGuid().ToString("N");
            item.time_utc = DateTime.UtcNow.ToString("o");
            item.run_id = _runId;
            byte[] bytes = Encoding.UTF8.GetBytes(_json.Serialize(item) + "\n");
            using (FileStream file = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }
        }

        internal static IEnumerable<PreviewEvent> Read(string directory)
        {
            if (!Directory.Exists(directory)) yield break;
            JavaScriptSerializer json = new JavaScriptSerializer();
            foreach (string path in Directory.GetFiles(directory, "preview-*.jsonl").OrderBy(x => x, StringComparer.Ordinal))
            {
                using (StreamReader reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        // An interrupted append is not an instruction to stop a process.
                        PreviewEvent item = null;
                        try { item = json.Deserialize<PreviewEvent>(line); }
                        catch (ArgumentException) { }
                        if (item != null && item.schema_version == 1) yield return item;
                        else yield return new PreviewEvent { schema_version = 1, type = "prior_record_unreadable", reason = Path.GetFileName(path) };
                    }
                }
            }
        }
    }

    internal static class PreviewProcesses
    {
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CommandLineToArgvW(string command, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        internal static HashSet<int> Owners(int port)
        {
            HashSet<int> owners = new HashSet<int>();
            foreach (int family in new[] { 2, 23 })
            {
                int size = 0;
                uint status = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0);
                if (status != 122 && status != 0) throw new System.ComponentModel.Win32Exception((int)status);
                if (size < 4 || size > 16 * 1024 * 1024) throw new IOException("Invalid TCP table size.");
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    status = GetExtendedTcpTable(buffer, ref size, false, family, 5, 0);
                    if (status != 0) throw new System.ComponentModel.Win32Exception((int)status);
                    int count = Marshal.ReadInt32(buffer);
                    int rowSize = family == 2 ? 24 : 56;
                    if (count < 0 || (long)count * rowSize + 4 > size) throw new IOException("Invalid TCP table.");
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr row = IntPtr.Add(buffer, 4 + i * rowSize);
                        int state = Marshal.ReadInt32(row, family == 2 ? 0 : 48);
                        int wirePort = Marshal.ReadInt32(row, family == 2 ? 8 : 20);
                        int localPort = ((wirePort & 255) << 8) | ((wirePort >> 8) & 255);
                        if (state == 2 && localPort == port) owners.Add(Marshal.ReadInt32(row, family == 2 ? 20 : 52));
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            return owners;
        }

        private static ManagementObject Get(int pid)
        {
            using (ManagementObjectSearcher search = new ManagementObjectSearcher("SELECT ProcessId,ParentProcessId,CreationDate,ExecutablePath,CommandLine,Name FROM Win32_Process WHERE ProcessId=" + pid.ToString(CultureInfo.InvariantCulture)))
            using (ManagementObjectCollection results = search.Get())
            {
                foreach (ManagementObject process in results) return process;
            }
            return null;
        }

        private static bool IsPreview(ManagementObject process, bool server)
        {
            // A mention of a framework in an API argument or inline script is not
            // ownership. Inspect only Node's actual entry script, never a shell.
            if (!string.Equals(Convert.ToString(process["Name"]), "node.exe", StringComparison.OrdinalIgnoreCase)) return false;
            string[] arguments = ParseArguments(Convert.ToString(process["CommandLine"]));
            int scriptIndex = NodeScriptIndex(arguments);
            if (scriptIndex < 0) return false;
            string script = NormalizeScriptPath(arguments[scriptIndex]);
            if (script == null) return false;
            int modeIndex = scriptIndex + 1;
            string mode = modeIndex < arguments.Length ? arguments[modeIndex] : null;
            if (IsEntry(script, @"next\dist\server\lib\start-server.js")) return mode == null;
            if (IsEntry(script, @"next\dist\bin\next")) return mode == null || mode == "dev" || mode == "start";
            if (IsEntry(script, @"vite\bin\vite.js")) return mode == null || mode == "dev" || mode == "serve" || mode == "preview";
            if (IsEntry(script, @"astro\astro.js")) return mode == "dev" || mode == "preview";
            return !server && IsEntry(script, @"npm\bin\npm-cli.js") && mode == "run" &&
                modeIndex + 1 < arguments.Length && new[] { "dev", "start", "preview" }.Contains(arguments[modeIndex + 1]);
        }

        private static string[] ParseArguments(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return new string[0];
            int count;
            IntPtr memory = CommandLineToArgvW(command, out count);
            if (memory == IntPtr.Zero) return new string[0];
            try
            {
                if (count < 2 || count > 32768) return new string[0];
                string[] arguments = new string[count];
                for (int i = 0; i < count; i++) arguments[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(memory, i * IntPtr.Size));
                return arguments;
            }
            finally { LocalFree(memory); }
        }

        private static int NodeScriptIndex(string[] arguments)
        {
            if (arguments.Length < 2 || string.IsNullOrEmpty(arguments[0])) return -1;
            for (int i = 1; i < arguments.Length; i++)
            {
                string option = arguments[i];
                if (option == "--") return i + 1 < arguments.Length ? i + 1 : -1;
                if (!option.StartsWith("-", StringComparison.Ordinal)) return string.IsNullOrEmpty(option) ? -1 : i;
                // Deliberately allow only unambiguous Node options. Unknown,
                // eval/print, stdin and test-runner modes are never managed.
                if (new[] { "--no-warnings", "--trace-warnings", "--enable-source-maps", "--inspect", "--inspect-brk" }.Contains(option)) continue;
                if (Regex.IsMatch(option, @"^--(?:inspect(?:-brk)?|max-old-space-size|max-semi-space-size)=.+$")) continue;
                if (new[] { "-r", "--require", "--import", "--loader", "--experimental-loader", "--conditions", "-C" }.Contains(option))
                {
                    if (++i >= arguments.Length || string.IsNullOrEmpty(arguments[i]) || arguments[i].StartsWith("-", StringComparison.Ordinal)) return -1;
                    continue;
                }
                if (Regex.IsMatch(option, @"^--(?:require|import|loader|experimental-loader|conditions)=.+$")) continue;
                return -1;
            }
            return -1;
        }

        private static string NormalizeScriptPath(string script)
        {
            if (string.IsNullOrEmpty(script) || script.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            string path = script.Replace('/', '\\');
            try
            {
                // Do not resolve relative scripts against this tool's working
                // directory: the Node process may have an entirely different one.
                if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
                List<string> parts = new List<string>();
                foreach (string part in path.Split('\\'))
                {
                    if (part == "." || part.Length == 0) continue;
                    if (part == ".." && parts.Count > 0 && parts[parts.Count - 1] != "..") parts.RemoveAt(parts.Count - 1);
                    else parts.Add(part);
                }
                return string.Join("\\", parts);
            }
            catch (ArgumentException) { return null; }
            catch (NotSupportedException) { return null; }
            catch (PathTooLongException) { return null; }
        }

        private static bool IsEntry(string script, string entry)
        {
            string suffix = "node_modules\\" + entry;
            return string.Equals(script, suffix, StringComparison.OrdinalIgnoreCase) ||
                script.EndsWith("\\" + suffix, StringComparison.OrdinalIgnoreCase);
        }

        internal static PreviewCandidate Capture(int port)
        {
            HashSet<int> owners = Owners(port);
            // Several unrelated listeners on the same port are ambiguous: leave them alone.
            if (owners.Count != 1) return null;
            List<PreviewProcess> family = new List<PreviewProcess>();
            int currentPid = owners.First();
            string childCreated = null;
            for (int i = 0; i < 8; i++)
            {
                using (ManagementObject process = Get(currentPid))
                {
                    if (process == null || !IsPreview(process, i == 0)) break;
                    string created = Convert.ToString(process["CreationDate"]);
                    if (string.IsNullOrEmpty(created) || string.IsNullOrEmpty(Convert.ToString(process["ExecutablePath"]))) break;
                    if (childCreated != null && ManagementDateTimeConverter.ToDateTime(created) > ManagementDateTimeConverter.ToDateTime(childCreated)) break;
                    if (family.Any(p => p.pid == currentPid)) break;
                    family.Add(new PreviewProcess { pid = currentPid, created = created, executable = Convert.ToString(process["ExecutablePath"]), parent_pid = Convert.ToInt32(process["ParentProcessId"]) });
                    currentPid = Convert.ToInt32(process["ParentProcessId"]);
                    childCreated = created;
                }
            }
            return family.Count == 0 ? null : new PreviewCandidate { port = port, processes = family.ToArray() };
        }

        internal static bool Same(PreviewProcess identity, bool server)
        {
            using (ManagementObject current = Get(identity.pid))
            {
                return current != null && string.Equals(Convert.ToString(current["CreationDate"]), identity.created, StringComparison.Ordinal) &&
                    string.Equals(Convert.ToString(current["ExecutablePath"]), identity.executable, StringComparison.OrdinalIgnoreCase) && IsPreview(current, server);
            }
        }

        internal static string Stop(PreviewCandidate candidate)
        {
            PreviewProcess server = candidate.processes[0];
            HashSet<int> owners = Owners(candidate.port);
            if (owners.Count == 0) return "already_stopped";
            if (owners.Count != 1 || !owners.Contains(server.pid) || !Same(server, true)) return "denied_identity_changed";
            HashSet<int> family = new HashSet<int>(candidate.processes.Select(p => p.pid));
            // Stop only verified launchers without unrelated children; never kill a whole Node tree.
            foreach (PreviewProcess identity in candidate.processes.Reverse())
            {
                if (!Same(identity, identity.pid == server.pid)) continue;
                bool hasOtherChild = false;
                using (ManagementObjectSearcher search = new ManagementObjectSearcher("SELECT ProcessId FROM Win32_Process WHERE ParentProcessId=" + identity.pid.ToString(CultureInfo.InvariantCulture)))
                using (ManagementObjectCollection children = search.Get())
                {
                    foreach (ManagementObject child in children)
                        using (child) if (!family.Contains(Convert.ToInt32(child["ProcessId"]))) hasOtherChild = true;
                }
                if (identity.pid != server.pid && hasOtherChild) continue;
                try
                {
                    using (Process process = Process.GetProcessById(identity.pid))
                    {
                        // Holding this handle prevents PID reuse between the identity check and Kill.
                        IntPtr handle = process.Handle;
                        if (!Same(identity, identity.pid == server.pid)) continue;
                        process.Kill();
                        process.WaitForExit(1500);
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
            }
            return Owners(candidate.port).Count == 0 ? "stopped" : "retry_required";
        }
    }

    internal sealed class PreviewCleanup : IDisposable
    {
        internal const int DefaultPort = 47832;
        internal const string ExtensionId = "dgpfiadablpfojbalengndogkiidplcb";
        internal const string ExtensionOrigin = "chrome-extension://" + ExtensionId;
        private readonly object _sync = new object();
        private readonly Dictionary<string, BrowserObservation> _sources = new Dictionary<string, BrowserObservation>();
        private readonly Dictionary<int, PreviewCandidate> _candidates = new Dictionary<int, PreviewCandidate>();
        private readonly Dictionary<int, DateTime> _pending = new Dictionary<int, DateTime>();
        private readonly HashSet<int> _failedFast = new HashSet<int>();
        private readonly HashSet<string> _denied = new HashSet<string>();
        private readonly PreviewJournal _journal;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly TcpListener _listener;
        private readonly Thread _thread;
        private readonly Func<int, PreviewCandidate> _capture;
        private readonly Func<PreviewCandidate, string> _stop;
        private readonly Func<DateTime> _now;
        private bool _enabled = true;
        private bool _journalOk = true;
        private bool _historyIncomplete;
        private volatile bool _disposed;
        private int _closedCount;
        private volatile string _status = "预览收尾：等待浏览器扩展连接";

        internal PreviewCleanup(int port, string directory, Func<int, PreviewCandidate> capture = null,
            Func<PreviewCandidate, string> stop = null, Func<DateTime> now = null, bool listen = true)
        {
            _capture = capture ?? PreviewProcesses.Capture;
            _stop = stop ?? PreviewProcesses.Stop;
            _now = now ?? (() => DateTime.UtcNow);
            foreach (PreviewEvent item in PreviewJournal.Read(directory)) Replay(item);
            _journal = new PreviewJournal(directory);
            if (_historyIncomplete)
            {
                Record(new PreviewEvent { type = "prior_evidence_incomplete", reason = "Prior event records were incomplete; automatic stops are disabled." });
                _journalOk = false;
                _status = "预览收尾：旧记录不完整，暂不自动关闭";
            }
            if (!listen) return;
            _listener = new TcpListener(IPAddress.Loopback, port);
            _listener.Start(8);
            _thread = new Thread(Listen) { IsBackground = true, Name = "LocalPreviewBridge" };
            _thread.Start();
        }

        internal string Status { get { return _status; } }
        internal void SetEnabled(bool value) { lock (_sync) { _enabled = value; if (!value) _status = "预览收尾：已暂停"; } }

        private static string Identity(PreviewCandidate candidate)
        {
            PreviewProcess server = candidate.processes[0];
            return candidate.port + ":" + server.pid + ":" + server.created;
        }

        private bool IsDenied(int port)
        {
            PreviewCandidate candidate;
            return _candidates.TryGetValue(port, out candidate) && _denied.Contains(Identity(candidate));
        }

        private void Replay(PreviewEvent item)
        {
            if (item.type == "tabs_observed" && item.source_id != null && item.open_ports != null)
                _sources[item.source_id] = new BrowserObservation { schema_version = 1, source_id = item.source_id, source_sequence = item.source_sequence, open_ports = item.open_ports };
            else if (item.type == "preview_registered" && item.candidate != null) _candidates[item.candidate.port] = item.candidate;
            else if (item.type == "preview_stopped") _candidates.Remove(item.port);
            else if (item.type == "preview_denied" && item.candidate != null) _denied.Add(Identity(item.candidate));
            else if (item.type == "prior_record_unreadable" || item.type == "prior_evidence_incomplete") _historyIncomplete = true;
        }

        private void Record(PreviewEvent item)
        {
            try { _journal.Append(item); }
            catch { _journalOk = false; _status = "预览收尾：记录写入失败，已停止自动关服务"; throw; }
        }

        internal string Observe(BrowserObservation item)
        {
            Guid source;
            DateTime observed;
            if (item == null || item.schema_version != 1 || !Guid.TryParse(item.source_id, out source) || item.source_sequence < 1 ||
                item.open_ports == null || item.open_ports.Length > 128 || item.open_ports.Any(p => p < 1 || p > 65535 || p == DefaultPort) ||
                !DateTime.TryParse(item.observed_at_utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out observed) || Math.Abs((_now() - observed.ToUniversalTime()).TotalSeconds) > 30)
                throw new ArgumentException("Invalid or stale browser observation.");
            lock (_sync)
            {
                BrowserObservation previous;
                _sources.TryGetValue(item.source_id, out previous);
                if (previous != null && item.source_sequence <= previous.source_sequence) return "duplicate";
                item.open_ports = item.open_ports.Distinct().OrderBy(p => p).ToArray();
                bool changed = previous == null || !previous.open_ports.SequenceEqual(item.open_ports);
                if (changed) Record(new PreviewEvent { type = "tabs_observed", source_id = item.source_id, source_sequence = item.source_sequence, open_ports = item.open_ports });
                _sources[item.source_id] = item;
                HashSet<int> open = new HashSet<int>(_sources.Values.SelectMany(s => s.open_ports));
                foreach (int p in open)
                {
                    _pending.Remove(p);
                    _failedFast.Remove(p);
                    if (_candidates.ContainsKey(p) && !IsDenied(p)) continue;
                    PreviewCandidate candidate = _capture(p);
                    if (candidate != null)
                    {
                        // A new service may reuse a port; denial of the old identity remains permanent.
                        if (_denied.Contains(Identity(candidate))) continue;
                        Record(new PreviewEvent { type = "preview_registered", candidate = candidate, source_id = item.source_id, source_sequence = item.source_sequence });
                        _candidates[p] = candidate;
                    }
                }
                foreach (int p in _candidates.Keys.Where(p => !open.Contains(p) && !IsDenied(p)).ToArray())
                    if (!_pending.ContainsKey(p)) _pending[p] = _now().AddSeconds(3);
                if (_enabled && _journalOk) _status = string.Format("预览收尾：已连接，监看 {0} 个；已关闭 {1} 个", open.Count(p => _candidates.ContainsKey(p)), _closedCount);
                return "accepted";
            }
        }

        internal void Tick(bool fallback)
        {
            lock (_sync)
            {
                if (_disposed || !_enabled || !_journalOk) return;
                foreach (int p in _pending.Keys.ToArray())
                {
                    if (_now() < _pending[p] || IsDenied(p) || (!fallback && _failedFast.Contains(p))) continue;
                    if (_sources.Values.Any(s => s.open_ports.Contains(p))) { _pending.Remove(p); continue; }
                    try
                    {
                        Record(new PreviewEvent { type = "stop_evaluated", port = p, candidate = _candidates[p], reason = fallback ? "timer_fallback" : "last_localhost_tab_closed" });
                        Record(new PreviewEvent { type = "stop_requested", port = p, reason = fallback ? "timer_fallback" : "last_localhost_tab_closed" });
                        string result = _stop(_candidates[p]);
                        if (result.StartsWith("denied", StringComparison.Ordinal))
                        {
                            Record(new PreviewEvent { type = "preview_denied", port = p, candidate = _candidates[p], reason = result });
                            _denied.Add(Identity(_candidates[p]));
                            _pending.Remove(p);
                        }
                        else if (result == "retry_required")
                        {
                            Record(new PreviewEvent { type = "stop_failed", port = p, reason = result });
                            _failedFast.Add(p);
                            _status = "预览收尾：有服务未退出，等待下轮重试";
                        }
                        else
                        {
                            Record(new PreviewEvent { type = "preview_stopped", port = p, reason = result });
                            _pending.Remove(p);
                            _candidates.Remove(p);
                            _closedCount++;
                            _status = string.Format("预览收尾：已关闭 {0} 个，最近端口 {1}", _closedCount, p);
                        }
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        bool denied = ex.NativeErrorCode == 5;
                        try { Record(new PreviewEvent { type = denied ? "preview_denied" : "stop_failed", port = p, candidate = _candidates[p], reason = denied ? "access_denied" : ex.GetType().Name }); }
                        catch { return; }
                        if (denied) { _denied.Add(Identity(_candidates[p])); _pending.Remove(p); }
                        else _failedFast.Add(p);
                        _status = denied ? "预览收尾：权限不足，未强行停止" : "预览收尾：等待下轮重试";
                    }
                    catch (Exception)
                    {
                        _failedFast.Add(p);
                        if (_journalOk)
                        {
                            try { Record(new PreviewEvent { type = "stop_failed", port = p, reason = "verification_or_apply_failed" }); }
                            catch { return; }
                        }
                        _status = _journalOk ? "预览收尾：未能确认服务，暂不处理" : "预览收尾：记录写入失败，已停止自动关服务";
                    }
                }
            }
        }

        private void Listen()
        {
            while (!_disposed)
            {
                try { using (TcpClient client = _listener.AcceptTcpClient()) Handle(client); }
                catch (SocketException) { if (_disposed) return; }
                catch (ObjectDisposedException) { return; }
                catch (Exception) { /* A bad client cannot authorize a stop. */ }
            }
        }

        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 2000;
            client.SendTimeout = 2000;
            using (NetworkStream stream = client.GetStream())
            {
                List<byte> bytes = new List<byte>();
                int last;
                while (bytes.Count < 8192 && (last = stream.ReadByte()) >= 0)
                {
                    bytes.Add((byte)last);
                    int n = bytes.Count;
                    if (n >= 4 && bytes[n - 4] == 13 && bytes[n - 3] == 10 && bytes[n - 2] == 13 && bytes[n - 1] == 10) break;
                }
                string[] lines = Encoding.ASCII.GetString(bytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in lines.Skip(1)) { int colon = line.IndexOf(':'); if (colon > 0) headers[line.Substring(0, colon)] = line.Substring(colon + 1).Trim(); }
                string origin;
                headers.TryGetValue("Origin", out origin);
                if (request.Length != 3 || origin != ExtensionOrigin)
                {
                    string rejectedLength;
                    int remaining;
                    if (headers.TryGetValue("Content-Length", out rejectedLength) && int.TryParse(rejectedLength, out remaining) && remaining > 0 && remaining <= 16384)
                        while (remaining-- > 0 && stream.ReadByte() >= 0) { }
                    Reply(stream, 403, "forbidden", false);
                    return;
                }
                if (request[0] == "OPTIONS") { Reply(stream, 200, "ok", true); return; }
                if (request[0] != "POST" || request[1] != "/v1/tabs") { Reply(stream, 404, "not_found", true); return; }
                string lengthText, type;
                int length;
                if (!headers.TryGetValue("Content-Length", out lengthText) || !int.TryParse(lengthText, out length) || length < 2 || length > 16384 ||
                    headers.ContainsKey("Transfer-Encoding") || !headers.TryGetValue("Content-Type", out type) || !type.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                { Reply(stream, 400, "invalid_request", true); return; }
                byte[] body = new byte[length];
                int offset = 0;
                while (offset < length) { int read = stream.Read(body, offset, length - offset); if (read == 0) return; offset += read; }
                try { Reply(stream, 200, Observe(_json.Deserialize<BrowserObservation>(Encoding.UTF8.GetString(body))), true); }
                catch (ArgumentException) { Reply(stream, 400, "invalid_observation", true); }
                catch (Exception) { Reply(stream, 503, "not_applied", true); }
            }
        }

        private static void Reply(Stream stream, int status, string result, bool allowOrigin)
        {
            string body = "{\"result\":\"" + result + "\"}";
            string headers = "HTTP/1.1 " + status + " Result\r\nContent-Type: application/json\r\nConnection: close\r\nCache-Control: no-store\r\n";
            if (allowOrigin) headers += "Access-Control-Allow-Origin: " + ExtensionOrigin + "\r\nAccess-Control-Allow-Methods: POST, OPTIONS\r\nAccess-Control-Allow-Headers: Content-Type\r\n";
            byte[] output = Encoding.UTF8.GetBytes(headers + "Content-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\n\r\n" + body);
            stream.Write(output, 0, output.Length);
        }

        public void Dispose()
        {
            lock (_sync) _disposed = true;
            if (_listener != null) _listener.Stop();
            if (_thread != null) _thread.Join(2500);
        }
    }
}
