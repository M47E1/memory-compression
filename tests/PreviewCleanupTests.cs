using System;
using System.IO;
using System.Linq;
using System.ComponentModel;
using MemoryCompression;

internal static class PreviewCleanupTests
{
    private static int _assertions;
    private static DateTime _now = DateTime.UtcNow;
    private static readonly string A = Guid.NewGuid().ToString();
    private static readonly string B = Guid.NewGuid().ToString();

    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _assertions++;
    }

    private static BrowserObservation View(string id, long sequence, params int[] ports)
    {
        return new BrowserObservation { schema_version = 1, source_id = id, source_sequence = sequence, observed_at_utc = _now.ToString("o"), open_ports = ports };
    }

    private static PreviewCandidate Candidate(int port)
    {
        return new PreviewCandidate { port = port, processes = new[] { new PreviewProcess { pid = 123, created = "test-identity", executable = "test-node", parent_pid = 456 } } };
    }

    private static int Main(string[] args)
    {
        string root = args[0];
        int stopped = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "last-tab"), Candidate, c => { stopped++; return "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43001));
            _now = _now.AddHours(1);
            cleanup.Tick(true);
            Check(stopped == 0, "An open but idle tab was stopped.");
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(2);
            cleanup.Tick(false);
            Check(stopped == 0, "The refresh grace period was skipped.");
            _now = _now.AddSeconds(2);
            cleanup.Tick(false);
            Check(stopped == 1, "The last closed tab did not stop its preview.");
        }

        stopped = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "two-browsers"), Candidate, c => { stopped++; return "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43002));
            cleanup.Observe(View(B, 1, 43002));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(true);
            Check(stopped == 0, "A second browser's open preview was ignored.");
            cleanup.Observe(View(B, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(false);
            Check(stopped == 1, "The preview remained after both browsers closed it.");
        }

        stopped = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "refresh"), Candidate, c => { stopped++; return "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43003));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(1);
            cleanup.Observe(View(A, 3, 43003));
            _now = _now.AddSeconds(5);
            cleanup.Tick(true);
            Check(stopped == 0, "A reopened tab was stopped.");
            Check(cleanup.Observe(View(A, 2)) == "duplicate", "An out-of-order close was accepted.");
            cleanup.Tick(true);
            Check(stopped == 0, "An old close overrode a newer open.");
        }

        int attempts = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "fallback"), Candidate,
            c => ++attempts == 1 ? "retry_required" : "stopped", () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43004));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(false);
            cleanup.Tick(false);
            Check(attempts == 1, "A failed fast stop was repeatedly retried outside the timer.");
            cleanup.Tick(true);
            Check(attempts == 2, "The memory timer did not retry a failed fast stop.");
        }

        attempts = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "denial"), Candidate,
            c => { attempts++; return "denied_identity_changed"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43005));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(false);
            cleanup.Tick(true);
            cleanup.Observe(View(A, 3, 43005));
            cleanup.Observe(View(A, 4));
            _now = _now.AddSeconds(4);
            cleanup.Tick(true);
            Check(attempts == 1, "A later layer overrode an earlier identity denial.");
        }

        stopped = 0;
        attempts = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "access-denied"), Candidate,
            c => { attempts++; throw new Win32Exception(5); }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43009));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(false);
            cleanup.Tick(true);
            Check(attempts == 1, "The timer retried a permission denial.");
        }

        attempts = 0;
        int generation = 1;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "new-generation"),
            p => { PreviewCandidate candidate = Candidate(p); candidate.processes[0].pid = generation; return candidate; },
            c => { attempts++; return c.processes[0].pid == 1 ? "denied_identity_changed" : "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43010));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(false);
            generation = 2;
            cleanup.Observe(View(A, 3, 43010));
            cleanup.Observe(View(A, 4));
            _now = _now.AddSeconds(4);
            cleanup.Tick(false);
            Check(attempts == 2, "A denial of an old process incorrectly blocked a new preview using the same port.");
        }

        stopped = 0;
        string incomplete = Path.Combine(root, "incomplete-records");
        Directory.CreateDirectory(incomplete);
        File.WriteAllText(Path.Combine(incomplete, "preview-000-incomplete.jsonl"), "{\"schema_version\":1,");
        using (PreviewCleanup cleanup = new PreviewCleanup(0, incomplete, Candidate,
            c => { stopped++; return "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43011));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(true);
            Check(stopped == 0 && cleanup.Status.Contains("记录不完整"), "Incomplete records did not leave automatic stops disabled and visible.");
        }

        stopped = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "unknown-service"), p => null,
            c => { stopped++; return "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43006));
            cleanup.Observe(View(A, 2));
            _now = _now.AddSeconds(4);
            cleanup.Tick(true);
            Check(stopped == 0, "An unrecognized localhost service was stopped.");
        }

        stopped = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, Path.Combine(root, "paused"), Candidate,
            c => { stopped++; return "stopped"; }, () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43007));
            cleanup.Observe(View(A, 2));
            cleanup.SetEnabled(false);
            _now = _now.AddSeconds(4);
            cleanup.Tick(true);
            Check(stopped == 0, "Pause did not stop automatic process termination.");
            cleanup.SetEnabled(true);
            cleanup.Tick(true);
            Check(stopped == 1, "The paused pending close did not resume.");
        }

        string replay = Path.Combine(root, "replay");
        stopped = 0;
        using (PreviewCleanup cleanup = new PreviewCleanup(0, replay, Candidate, c => "stopped", () => _now, false))
        {
            cleanup.Observe(View(A, 1, 43008));
            cleanup.Observe(View(A, 2));
        }
        string original = Directory.GetFiles(replay).Single();
        string content = File.ReadAllText(original);
        using (PreviewCleanup cleanup = new PreviewCleanup(0, replay, Candidate, c => { stopped++; return "stopped"; }, () => _now, false))
        {
            _now = _now.AddSeconds(5);
            cleanup.Tick(true);
            Check(stopped == 0, "Replay acted without a fresh browser observation.");
            cleanup.Observe(View(A, 3));
            _now = _now.AddSeconds(4);
            cleanup.Tick(true);
            Check(stopped == 1, "The pending close was lost across a helper restart.");
        }
        Check(File.ReadAllText(original) == content, "Replay modified its source journal.");
        foreach (string directory in Directory.GetDirectories(root))
        {
            foreach (IGrouping<string, PreviewEvent> run in PreviewJournal.Read(directory).GroupBy(e => e.run_id))
            {
                if (run.Key == null) continue; // Deliberately incomplete input above is preserved as-is.
                long last = 0;
                foreach (PreviewEvent item in run)
                {
                    Check(item.sequence == ++last && !string.IsNullOrEmpty(item.event_id) && !string.IsNullOrEmpty(item.time_utc), "Invalid append-only event identity or order.");
                }
            }
        }
        Console.WriteLine("PASS: " + _assertions + " assertions; last-tab, refresh, multiple browsers, fallback, denials, pause, replay, and event records.");
        return 0;
    }
}
