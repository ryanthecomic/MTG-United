using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MTGUnited
{
    /// <summary>
    /// Where Forge and the bridge live, and the install / update / uninstall actions behind the Forge tab.
    /// Two setups are supported:
    ///  • bundled  - MTG United downloads Forge's source at the pinned version and builds it (scripts/setup.ps1);
    ///  • existing - a Forge the player already has: the bridge is compiled against its program file and reads its
    ///               card data through a link, so nothing is downloaded twice and their Forge folder isn't modified.
    /// The choice is stored by the setup script in &lt;repo&gt;/mtgunited-forge.json.
    /// </summary>
    public static class ForgeSetup
    {
        public enum Mode { None, Bundled, Existing }

        public static Mode CurrentMode { get; private set; }
        public static string ForgeDir, BridgeJar, ForgeJar, GsonJar, Assets, ForgeCommit;

        public static string Root => root ?? (root = FindRepoRoot());
        static string root;
        static string ConfigPath => Root == null ? null : Path.Combine(Root, "mtgunited-forge.json");
        static string Script => Root == null ? null : Path.Combine(Root, "scripts", "setup.ps1");

        // ---------------------------------------------------------------- state

        /// <summary>Re-reads the saved setup (or recognises an older install made before the config existed).</summary>
        public static void Load()
        {
            CurrentMode = Mode.None;
            ForgeDir = BridgeJar = ForgeJar = GsonJar = Assets = ForgeCommit = null;
            if (Root == null) return;
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var d = Json.ParseObject(File.ReadAllText(ConfigPath).TrimStart('\uFEFF'));
                    CurrentMode = d.Str("mode") == "existing" ? Mode.Existing : Mode.Bundled;
                    ForgeDir = d.Str("forgeDir"); BridgeJar = d.Str("bridgeJar"); ForgeJar = d.Str("forgeJar");
                    GsonJar = d.Str("gson"); Assets = d.Str("assets"); ForgeCommit = d.Str("forgeCommit");
                    return;
                }
            }
            catch (Exception ex) { Debug.LogWarning("[MTGUnited] Couldn't read mtgunited-forge.json: " + ex.Message); }

            // Installed by an older setup script: bundled layout.
            string jar = Path.Combine(Root, "bridge", "target", "forge-bridge-all.jar");
            string gui = Path.Combine(Root, "forge", "forge-gui");
            if (File.Exists(jar) && Directory.Exists(Path.Combine(gui, "res")))
            {
                CurrentMode = Mode.Bundled; BridgeJar = jar; Assets = gui; ForgeDir = Path.Combine(Root, "forge");
            }
        }

        /// <summary>Everything the bridge needs is on disk.</summary>
        public static bool Installed
        {
            get
            {
                if (CurrentMode == Mode.None || BridgeJar == null || !File.Exists(BridgeJar)) return false;
                if (Assets == null || !Directory.Exists(Path.Combine(Assets, "res"))) return false;
                if (CurrentMode == Mode.Existing && (ForgeJar == null || !File.Exists(ForgeJar))) return false;
                return true;
            }
        }

        /// <summary>The bridge's own log (written next to its data folder).</summary>
        public static string LogFile
        {
            get
            {
                if (Assets == null) return null;
                var parent = Directory.GetParent(Path.GetFullPath(Assets));
                return parent == null ? null : Path.Combine(parent.FullName, "mtgunited-data", "bridge.log");
            }
        }

        /// <summary>A folder that looks like a Forge install: has res/ and Forge's program .jar.</summary>
        public static bool LooksLikeForge(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(Path.Combine(dir, "res"))) return false;
                return Directory.GetFiles(dir, "*jar-with-dependencies*.jar").Length > 0 || Directory.GetFiles(dir, "forge-gui-desktop*.jar").Length > 0;
            }
            catch { return false; }
        }

        /// <summary>Looks in the usual places for Forge installs (and unzipped Forge releases).</summary>
        public static List<string> Detect()
        {
            var found = new List<string>();
            var roots = new List<string>();
            void Add(string p) { if (!string.IsNullOrEmpty(p) && Directory.Exists(p) && !roots.Contains(p)) roots.Add(p); }
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Add(home);
            Add(Path.Combine(home, "Desktop"));
            Add(Path.Combine(home, "Downloads"));
            Add(Path.Combine(home, "Documents"));
            Add(Path.Combine(home, "Games"));
            foreach (var drive in new[] { "C:\\", "D:\\", "E:\\" }) { Add(drive); Add(Path.Combine(drive, "Games")); }

            foreach (var r in roots)
            {
                string[] children;
                try { children = Directory.GetDirectories(r); } catch { continue; }
                foreach (var c in children)
                {
                    string name = Path.GetFileName(c).ToLowerInvariant();
                    if (!name.Contains("forge")) continue;
                    if (LooksLikeForge(c) && !found.Contains(c)) found.Add(c);
                    // One level deeper: "Forge\forge-gui-desktop-2.0.x" style folders.
                    try { foreach (var g in Directory.GetDirectories(c)) if (LooksLikeForge(g) && !found.Contains(g)) found.Add(g); } catch { }
                }
            }
            // Our own bundled checkout isn't a "Forge install".
            if (Root != null) found.RemoveAll(f => f.StartsWith(Path.Combine(Root, "forge"), StringComparison.OrdinalIgnoreCase));
            return found;
        }

        // ---------------------------------------------------------------- Java

        public static string JavaVersion { get; private set; } = "checking...";

        /// <summary>Runs "java -version" once in the background.</summary>
        public static void CheckJava()
        {
            try
            {
                var psi = new ProcessStartInfo { FileName = "java", Arguments = "-version", UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true, CreateNoWindow = true };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                string first = null;
                p.ErrorDataReceived += (s, e) => { if (first == null && !string.IsNullOrEmpty(e.Data)) first = e.Data; };
                p.Exited += (s, e) => JavaVersion = first != null ? first.Replace("\"", "") : "not found";
                p.Start();
                p.BeginErrorReadLine();
            }
            catch { JavaVersion = "not found (install Java 17+)"; }
            try
            {
                var g = new Process { StartInfo = new ProcessStartInfo { FileName = "git", Arguments = "--version", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true }, EnableRaisingEvents = true };
                g.Exited += (s, e) => GitFound = true;
                g.Start();
            }
            catch { GitFound = false; }
        }

        public static bool JavaMissing => JavaVersion.StartsWith("not found");
        public static bool GitFound { get; private set; }

        /// <summary>Installs a prerequisite with winget (Windows' package manager); MTG United must restart to see it.</summary>
        public static void InstallTool(string name, string wingetId)
            => Run("Installing " + name, "winget", "install --id " + wingetId + " -e --accept-source-agreements --accept-package-agreements",
                   "When this finishes, close and reopen MTG United so it can see " + name + ".");

        // ---------------------------------------------------------------- running the setup script

        public static bool Busy => task != null && !task.HasExited;
        public static string TaskName { get; private set; }
        public static int LastExitCode { get; private set; } = -1;
        static Process task;
        static readonly List<string> log = new List<string>();
        static readonly object logLock = new object();
        public static event Action<bool> TaskFinished;   // true = success

        public static void Log(string line)
        {
            if (line == null) return;
            lock (logLock)
            {
                log.Add(line);
                if (log.Count > 400) log.RemoveRange(0, log.Count - 400);
            }
        }

        public static string LogTail(int lines)
        {
            lock (logLock)
            {
                int start = Mathf.Max(0, log.Count - lines);
                return string.Join("\n", log.GetRange(start, log.Count - start));
            }
        }

        public static void Install(bool clean) => Run(clean ? "Reinstalling Forge" : "Installing Forge", clean ? "-Clean" : "");
        public static void UseExisting(string dir) => Run("Setting up your Forge", "-ForgeInstall \"" + dir.TrimEnd('\\') + "\"");
        public static void Uninstall(bool keepData) => Run("Uninstalling", "-Uninstall" + (keepData ? " -KeepData" : ""));

        /// <summary>Update = rerun the setup for the current mode (picks up a new pinned Forge or new bridge code).</summary>
        public static void Update()
        {
            if (CurrentMode == Mode.Existing && ForgeDir != null) UseExisting(ForgeDir);
            else Install(false);
        }

        static void Run(string name, string args)
        {
            if (Script == null || !File.Exists(Script)) { Log("Can't find scripts\\setup.ps1 next to MTG United. Keep the 'scripts' and 'bridge' folders next to MTG United.exe."); return; }
            if (Busy) return;
            GameController.Instance?.Launcher.Stop();   // the bridge holds its jar open
            Run(name, "powershell", "-NoProfile -ExecutionPolicy Bypass -File \"" + Script + "\" " + args, null);
        }

        static void Run(string name, string exe, string args, string note)
        {
            if (Busy) return;
            TaskName = name;
            Log("");
            Log("==> " + name);
            if (note != null) Log(note);
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
                WorkingDirectory = Root ?? Directory.GetCurrentDirectory(),
                StandardOutputEncoding = System.Text.Encoding.UTF8, StandardErrorEncoding = System.Text.Encoding.UTF8,
            };
            try
            {
                task = new Process { StartInfo = psi, EnableRaisingEvents = true };
                task.OutputDataReceived += (s, e) => Log(e.Data);
                task.ErrorDataReceived += (s, e) => Log(e.Data);
                task.Exited += (s, e) => { LastExitCode = task.ExitCode; finishedPending = true; };
                task.Start();
                task.BeginOutputReadLine();
                task.BeginErrorReadLine();
            }
            catch (Exception ex) { Log("Couldn't run PowerShell: " + ex.Message); task = null; }
        }

        static volatile bool finishedPending;

        /// <summary>Called every frame by the Forge page: reports a finished task on the main thread.</summary>
        public static void Poll()
        {
            if (!finishedPending) return;
            finishedPending = false;
            Load();
            bool ok = LastExitCode == 0;
            Log(ok ? "==> " + TaskName + ": done." : "==> " + TaskName + " failed (exit " + LastExitCode + ").");
            TaskFinished?.Invoke(ok);
        }

        // ---------------------------------------------------------------- Windows helpers

        /// <summary>Opens Windows' folder picker (through PowerShell, since the game has no native one).</summary>
        public static void BrowseFolder(Action<string> chosen)
        {
            Dialog("$d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = " + Ps("Select your Forge folder (the one with forge.exe and res)") +
                   "; $d.ShowNewFolderButton = $false; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $r = $d.SelectedPath }", chosen);
        }

        /// <summary>Opens Windows' file picker for one file (e.g. a Cockatrice set .xml).</summary>
        public static void BrowseFile(string title, string filter, Action<string> chosen)
        {
            Dialog("$d = New-Object System.Windows.Forms.OpenFileDialog; $d.Title = " + Ps(title) + "; $d.Filter = " + Ps(filter) +
                   "; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $r = $d.FileName }", chosen);
        }

        /// <summary>Windows' "Save as" picker. The answer is the chosen path, or null when cancelled.</summary>
        public static void BrowseSaveFile(string title, string filter, string defaultName, Action<string> chosen)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) defaultName = defaultName.Replace(c, '_');
            Dialog("$d = New-Object System.Windows.Forms.SaveFileDialog; $d.Title = " + Ps(title) + "; $d.Filter = " + Ps(filter) +
                   "; $d.FileName = " + Ps(defaultName) + "; $d.OverwritePrompt = $true; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $r = $d.FileName }", chosen);
        }

        /// <summary>
        /// A PowerShell string literal that survives any characters (ã, é, ç, emoji...): sent as UTF-8 base64 and
        /// decoded inside PowerShell, so neither the command line nor the console code page can mangle it.
        /// </summary>
        static string Ps(string text) =>
            "([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? "")) + "')))";

        /// <summary>
        /// Runs a Windows Forms dialog script that leaves its answer in $r. The answer comes back as UTF-8 base64,
        /// because PowerShell writes to a redirected stdout in the console's code page, which turned folders like
        /// "C:\Users\Gaião" into "Gai?o" in the built game.
        /// </summary>
        static void Dialog(string script, Action<string> chosen)
        {
            string ps = "Add-Type -AssemblyName System.Windows.Forms; $r = ''; " + script +
                        "; if ($r) { [Console]::Out.WriteLine([Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($r))) }";
            try
            {
                var psi = new ProcessStartInfo { FileName = "powershell", Arguments = "-NoProfile -STA -Command \"" + ps + "\"", UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                string result = null;
                p.OutputDataReceived += (s, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) result = e.Data.Trim(); };
                p.Exited += (s, e) =>
                {
                    try { p.WaitForExit(); } catch { }   // let the last output line arrive
                    string path = null;
                    if (!string.IsNullOrEmpty(result))
                    {
                        try { path = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(result)); }
                        catch { path = null; }
                    }
                    browseResult = path; browseCallback = chosen; browsePending = true;
                };
                p.Start();
                p.BeginOutputReadLine();
            }
            catch (Exception ex) { Log("Couldn't open the file picker: " + ex.Message); }
        }

        static volatile bool browsePending;
        static string browseResult;
        static Action<string> browseCallback;

        /// <summary>Delivers the folder picker's answer on the main thread.</summary>
        public static void PollBrowse()
        {
            if (!browsePending) return;
            browsePending = false;
            browseCallback?.Invoke(browseResult);
        }

        public static void OpenFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { Process.Start(new ProcessStartInfo { FileName = File.Exists(path) ? "notepad" : "explorer", Arguments = "\"" + path + "\"", UseShellExecute = true }); }
            catch (Exception ex) { Log("Couldn't open " + path + ": " + ex.Message); }
        }

        static string FindRepoRoot()
        {
            foreach (var start in new[] { Application.dataPath, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "bridge")) && Directory.Exists(Path.Combine(dir.FullName, "scripts")))
                        return dir.FullName;
            }
            return null;
        }
    }
}
