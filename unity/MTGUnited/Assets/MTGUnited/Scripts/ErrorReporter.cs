using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MTGUnited
{
    /// <summary>
    /// The error routine. Only real errors (exceptions, errors, failed asserts) are handled; warnings and
    /// known engine noise are ignored. Each distinct error is:
    ///  • written once, in full, to &lt;persistentDataPath&gt;/logs/errors.log (version, where you were, the last
    ///    few things that happened, the stack), with a short "repeated ×N" line if it keeps happening;
    ///  • shown once as a friendly toast (at most one toast every few seconds, a handful per session).
    /// It also notices when the previous run didn't close normally and keeps that run's Unity log.
    /// Options → Error log opens the file, which is what to attach to a bug report.
    /// </summary>
    public class ErrorReporter : MonoBehaviour
    {
        static ErrorReporter instance;
        GameController gc;

        struct Entry { public string Message, Stack, Context; public LogType Type; public DateTime Time; }
        static readonly ConcurrentQueue<Entry> queue = new ConcurrentQueue<Entry>();

        class Seen { public int Count, Logged; public float LastWrite; }
        readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>();

        // Last few meaningful actions ("started a Forge game", "table op: move"), included with each error.
        static readonly string[] crumbs = new string[10];
        static int crumbAt;
        static readonly object crumbLock = new object();

        const long MaxLogBytes = 512 * 1024;
        const int MaxToastsPerSession = 4;
        const float ToastGap = 6f;
        int toastsShown;
        float lastToast = -100f;
        bool reporting;   // never report errors raised while reporting

        /// <summary>Engine and platform messages that aren't ours to fix and don't affect play.</summary>
        static readonly string[] Noise =
        {
            "WindowsVideoMedia", "0xc00d", "VideoPlayer", "DefaultScenario", "Mismatched state",
            "The referenced script", "d3d11", "FMOD", "Curl error", "UnityWebRequest", "Cannot connect to destination host",
            "Releasing render texture", "Assertion failed on expression: 'IsInSyncWithParentSceneRoot",
        };

        public static string LogFolder => Path.Combine(Application.persistentDataPath, "logs");
        public static string LogFile => Path.Combine(LogFolder, "errors.log");

        public static void Install(GameController gc)
        {
            if (instance != null) return;
            instance = gc.gameObject.AddComponent<ErrorReporter>();
            instance.gc = gc;
            Application.logMessageReceivedThreaded += OnLog;
            instance.CheckPreviousRun();
        }

        /// <summary>A breadcrumb: something the player did, kept for the next error report.</summary>
        public static void Crumb(string what)
        {
            if (string.IsNullOrEmpty(what)) return;
            lock (crumbLock)
            {
                crumbs[crumbAt] = DateTime.Now.ToString("HH:mm:ss") + "  " + what;
                crumbAt = (crumbAt + 1) % crumbs.Length;
            }
        }

        /// <summary>Report a caught exception with a note on what was going on (no Debug.Log needed).</summary>
        public static void Report(Exception ex, string context)
        {
            if (ex == null) return;
            queue.Enqueue(new Entry { Message = ex.GetType().Name + ": " + ex.Message, Stack = ex.StackTrace ?? "", Context = context, Type = LogType.Exception, Time = DateTime.Now });
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert) return;
            if (string.IsNullOrEmpty(message)) return;
            foreach (var n in Noise) if (message.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0) return;
            queue.Enqueue(new Entry { Message = message, Stack = stack ?? "", Type = type, Time = DateTime.Now });
        }

        void OnDestroy()
        {
            if (instance == this) { Application.logMessageReceivedThreaded -= OnLog; instance = null; }
        }

        void Update()
        {
            int budget = 20;
            while (budget-- > 0 && queue.TryDequeue(out var e))
            {
                if (reporting) continue;
                reporting = true;
                try { Handle(e); } catch { /* the reporter must never throw */ }
                reporting = false;
            }
        }

        static readonly Regex Digits = new Regex(@"\d+");

        void Handle(Entry e)
        {
            // Same error = same first message line (numbers ignored) + same first stack frame.
            string first = e.Message.Split('\n')[0].Trim();
            string frame = "";
            foreach (var l in e.Stack.Split('\n')) { if (l.Trim().Length > 0) { frame = l.Trim(); break; } }
            string key = Digits.Replace(first, "#") + "|" + frame;
            if (!seen.TryGetValue(key, out var s)) { s = new Seen(); seen[key] = s; }
            s.Count++;

            if (s.Count == 1)
            {
                Write(FullEntry(e));
                s.Logged = 1; s.LastWrite = Time.realtimeSinceStartup;
                MaybeToast(e);
            }
            else if (Time.realtimeSinceStartup - s.LastWrite > 30f)
            {
                Write(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  (repeated ×" + (s.Count - s.Logged) + ")  " + first + "\n");
                s.Logged = s.Count; s.LastWrite = Time.realtimeSinceStartup;
            }
        }

        string FullEntry(Entry e)
        {
            var sb = new StringBuilder();
            sb.Append("==== ").Append(e.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append("  ").Append(e.Type)
              .Append("  v").Append(Application.version).Append("  ").Append(Where()).Append('\n');
            if (!string.IsNullOrEmpty(e.Context)) sb.Append("While: ").Append(e.Context).Append('\n');
            sb.Append(e.Message.Trim()).Append('\n');
            if (!string.IsNullOrEmpty(e.Stack)) sb.Append(Indent(e.Stack.Trim(), 12)).Append('\n');
            lock (crumbLock)
            {
                bool any = false;
                for (int i = 0; i < crumbs.Length; i++)
                {
                    string c = crumbs[(crumbAt + i) % crumbs.Length];
                    if (c == null) continue;
                    if (!any) { sb.Append("Recently:\n"); any = true; }
                    sb.Append("   ").Append(c).Append('\n');
                }
            }
            sb.Append('\n');
            return sb.ToString();
        }

        static string Indent(string stack, int maxLines)
        {
            var lines = stack.Split('\n');
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length && i < maxLines; i++) sb.Append("   ").Append(lines[i].TrimEnd()).Append('\n');
            if (lines.Length > maxLines) sb.Append("   …\n");
            return sb.ToString().TrimEnd('\n');
        }

        string Where()
        {
            if (gc == null) return "";
            string place = gc.Table != null && gc.Table.gameObject.activeSelf ? "kitchen table"
                         : gc.InGame ? (gc.RemoteBridge ? "Forge LAN game" : "Forge game") : "menus";
            return "[" + place + ", " + Screen.width + "×" + Screen.height + ", " + Loc.Codes[Loc.Language] + "]";
        }

        void MaybeToast(Entry e)
        {
            if (gc == null || gc.Toasts == null) return;
            if (toastsShown >= MaxToastsPerSession) return;
            if (Time.realtimeSinceStartup - lastToast < ToastGap) return;
            toastsShown++;
            lastToast = Time.realtimeSinceStartup;
            gc.Toasts.Show(Friendly(e.Message), true);
        }

        /// <summary>A plain-language line for the kinds of problems players can do something about.</summary>
        static string Friendly(string msg)
        {
            string tail = " " + Loc.T("Details saved in Options → Error log.");
            if (Has(msg, "UnauthorizedAccess") || Has(msg, "IOException") || Has(msg, "Sharing violation"))
                return Loc.T("Couldn't read or write a file (is it open in another program?).") + tail;
            if (Has(msg, "DirectoryNotFound") || Has(msg, "FileNotFound"))
                return Loc.T("A file MTG United needs is missing.") + tail;
            if (Has(msg, "Socket") || Has(msg, "connection"))
                return Loc.T("Network problem: check that both PCs are on the same network.") + tail;
            if (Has(msg, "OutOfMemory"))
                return Loc.T("The game ran out of memory. Try a smaller card size or restart.") + tail;
            return Loc.T("Something went wrong, but you can keep playing.") + tail;
        }

        static bool Has(string s, string what) => s.IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;

        static void Write(string text)
        {
            try
            {
                Directory.CreateDirectory(LogFolder);
                var f = new FileInfo(LogFile);
                if (f.Exists && f.Length > MaxLogBytes)
                {
                    string old = Path.Combine(LogFolder, "errors.old.log");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogFile, old);
                }
                File.AppendAllText(LogFile, text, new UTF8Encoding(false));
            }
            catch { /* disk full / read-only: nothing sensible to do */ }
        }

        // ---------------------------------------------------------------- previous run

        /// <summary>
        /// A flag set while the game runs and cleared on a normal quit. Still set at start = the last run crashed or
        /// was killed: keep the end of that run's Unity log and say so once.
        /// </summary>
        void CheckPreviousRun()
        {
            if (Application.isEditor) return;
            bool crashed = PlayerPrefs.GetInt("runOpen", 0) == 1;
            PlayerPrefs.SetInt("runOpen", 1);
            PlayerPrefs.Save();
            if (!crashed) return;
            try
            {
                string cur = Application.consoleLogPath;
                string prev = string.IsNullOrEmpty(cur) ? null : Path.Combine(Path.GetDirectoryName(cur), "Player-prev.log");
                if (prev != null && File.Exists(prev))
                {
                    var lines = File.ReadAllLines(prev);
                    int from = Mathf.Max(0, lines.Length - 120);
                    var sb = new StringBuilder("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") +
                                               "  The previous session didn't close normally. End of its log:\n");
                    for (int i = from; i < lines.Length; i++) sb.Append("   ").Append(lines[i]).Append('\n');
                    Write(sb.Append('\n').ToString());
                }
                else Write("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  The previous session didn't close normally.\n\n");
            }
            catch { }
            StartCoroutine(SayCrashed());
        }

        System.Collections.IEnumerator SayCrashed()
        {
            yield return new WaitForSecondsRealtime(2f);
            if (gc != null && gc.Toasts != null)
                gc.Toasts.Show(Loc.T("MTG United didn't close normally last time.") + " " + Loc.T("Details saved in Options → Error log."), true);
        }

        void OnApplicationQuit()
        {
            PlayerPrefs.SetInt("runOpen", 0);
            PlayerPrefs.Save();
        }

        /// <summary>Options → Error log: opens the file (or says there's nothing in it).</summary>
        public static void OpenLog(GameController gc)
        {
            if (!File.Exists(LogFile))
            {
                gc?.Toasts?.Show(Loc.T("No errors so far."));
                return;
            }
            ForgeSetup.OpenFolder(LogFile);
        }
    }
}
