using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;

namespace MemoryCompression
{
    internal sealed class EngineResult
    {
        internal bool Confirmed;
        internal int ProcessId;
        internal int ExitCode;
        internal string Message;
    }

    internal sealed class EngineLogResult
    {
        internal bool Confirmed;
        internal string Error;
    }

    internal static class GentleEngine
    {
        internal const string Arguments = "/StandbyListLowPriority";
        // The digest of the signed executable in the upstream 3.0.8 release.
        internal const string ReleaseDigest = "8b68d56c6ee28740f76513f21b21f5a1018f0ef291467b3f3d39d43daf0c0f2f";

        internal static ProcessStartInfo StartInfo(string path, bool elevated)
        {
            return new ProcessStartInfo
            {
                FileName = Path.GetFullPath(path),
                Arguments = Arguments,
                WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)),
                UseShellExecute = true,
                Verb = elevated ? "" : "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
        }

        internal static void Verify(Stream stream)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(digest, ReleaseDigest, StringComparison.Ordinal))
                    throw new InvalidDataException("引擎不是已核对的官方 3.0.8 文件，已取消启动。");
            }
        }

        internal static EngineResult Run(string path)
        {
            try
            {
                if (!File.Exists(path)) return new EngineResult { Message = "未找到 Windows Memory Cleaner，未清理。" };
                // Keep the verified file read-locked throughout elevation and execution.
                using (FileStream locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Verify(locked);
                    foreach (Process other in Process.GetProcessesByName("WinMemoryCleaner"))
                    {
                        using (other) { return new EngineResult { Message = "已有 Windows Memory Cleaner 正在运行，本次未重复启动。" }; }
                    }
                    bool elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                    long previousRecord = LastRecordId();
                    DateTime started = DateTime.UtcNow;
                    Stopwatch timer = Stopwatch.StartNew();
                    using (Process process = Process.Start(StartInfo(path, elevated)))
                    {
                        if (process == null) return new EngineResult { Message = "未能启动清理引擎，未清理。" };
                        int id = process.Id;
                        process.WaitForExit();
                        int exitCode = process.ExitCode;
                        if (exitCode != 0) return new EngineResult { ProcessId = id, ExitCode = exitCode, Message = "引擎退出码 " + exitCode + "，未确认清理成功。" };
                        EngineLogResult log = ReadResult(id, started, previousRecord);
                        return new EngineResult
                        {
                            Confirmed = log.Confirmed,
                            ProcessId = id,
                            ExitCode = exitCode,
                            Message = log.Confirmed
                                ? string.Format("已完成：仅低优先级待机缓存（{0:0.0} 秒）。", timer.Elapsed.TotalSeconds)
                                : "未确认清理成功：" + (log.Error ?? "未收到引擎的成功记录。")
                        };
                    }
                }
            }
            catch (Win32Exception ex)
            {
                return new EngineResult { Message = ex.NativeErrorCode == 1223 ? "管理员确认已取消，未清理。" : "无法启动清理引擎：" + ex.Message };
            }
            catch (Exception ex) { return new EngineResult { Message = "清理未完成：" + ex.Message }; }
        }

        private static long LastRecordId()
        {
            EventLogQuery query = new EventLogQuery("Application", PathType.LogName, "*[System[Provider[@Name='Windows Memory Cleaner']]]") { ReverseDirection = true };
            using (EventLogReader reader = new EventLogReader(query))
            using (EventRecord record = reader.ReadEvent())
                return record == null ? 0 : record.RecordId.GetValueOrDefault();
        }

        internal static EngineLogResult ReadResult(int processId, DateTime started, long previousRecord)
        {
            EngineLogResult result = new EngineLogResult();
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    string from = started.AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    string filter = "*[System[Provider[@Name='Windows Memory Cleaner'] and EventRecordID > " + previousRecord + " and TimeCreated[@SystemTime >= '" + from + "']]]";
                    EventLogQuery query = new EventLogQuery("Application", PathType.LogName, filter) { ReverseDirection = true };
                    List<string> payloads = new List<string>();
                    using (EventLogReader reader = new EventLogReader(query))
                    {
                        for (int count = 0; count < 64; count++)
                        {
                            using (EventRecord record = reader.ReadEvent())
                            {
                                if (record == null) break;
                                // Classic EventLogTraceListener records have no process ID.
                                // Correlate those by the pre-launch record ID and run time,
                                // after refusing to run alongside an existing engine.
                                if (record.ProcessId.GetValueOrDefault() != 0 && record.ProcessId != processId) continue;
                                foreach (EventProperty property in record.Properties)
                                    if (property.Value != null) payloads.Add(property.Value.ToString());
                            }
                        }
                    }
                    result = EvaluateLogs(payloads);
                    if (result.Error != null || result.Confirmed) return result;
                }
                catch (Exception ex) { return new EngineLogResult { Error = "无法读取引擎结果记录：" + ex.Message }; }
                Thread.Sleep(250);
            }
            return result;
        }

        internal static EngineLogResult EvaluateLogs(IEnumerable<string> payloads)
        {
            EngineLogResult result = new EngineLogResult();
            JavaScriptSerializer json = new JavaScriptSerializer();
            foreach (string payload in payloads)
            {
                int first = payload.IndexOf('{');
                int last = payload.LastIndexOf('}');
                if (first < 0 || last <= first) continue;
                Dictionary<string, object> log;
                try { log = json.DeserializeObject(payload.Substring(first, last - first + 1)) as Dictionary<string, object>; }
                catch { continue; }
                if (log == null) continue;
                object level, method, message, data;
                log.TryGetValue("level", out level);
                log.TryGetValue("method", out method);
                log.TryGetValue("message", out message);
                log.TryGetValue("data", out data);
                if (string.Equals(Convert.ToString(level), "Error", StringComparison.OrdinalIgnoreCase))
                    result.Error = Convert.ToString(message);
                if (!string.Equals(Convert.ToString(method), "Optimize", StringComparison.Ordinal)) continue;
                Dictionary<string, object> details = data as Dictionary<string, object>;
                object areasValue;
                if (details == null || !details.TryGetValue("memoryAreas", out areasValue)) continue;
                object[] areas = areasValue as object[];
                if (areas == null || areas.Length != 1) { result.Error = "引擎记录中的清理范围与请求不一致。"; continue; }
                Dictionary<string, object> area = areas[0] as Dictionary<string, object>;
                object error;
                if (area == null) { result.Error = "引擎的清理结果格式不完整。"; continue; }
                if (area.TryGetValue("error", out error) && !string.IsNullOrEmpty(Convert.ToString(error)))
                    result.Error = Convert.ToString(error);
                else if (string.Equals(Convert.ToString(level), "Information", StringComparison.OrdinalIgnoreCase))
                    result.Confirmed = true;
            }
            // An error cannot be overridden by a success record encountered later.
            if (result.Error != null) result.Confirmed = false;
            return result;
        }
    }
}
