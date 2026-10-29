using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MTGUnited
{
    /// <summary>
    /// Starts the Forge bridge (java -jar forge-bridge-all.jar) for you, so pressing Play is enough.
    /// It looks for the repository layout created by scripts/setup.ps1:
    ///   &lt;repo&gt;/bridge/target/forge-bridge-all.jar  and  &lt;repo&gt;/forge/forge-gui/res
    /// Paths can be overridden with the environment variables MTGU_BRIDGE_JAR, FORGE_GUI_DIR and JAVA_HOME.
    /// </summary>
    public class BridgeLauncher : MonoBehaviour
    {
        Process process;
        volatile string lastLine = "";
        bool autoTried;

        public string StatusLine
        {
            get
            {
                if (process != null && !process.HasExited) return "Bridge running: " + lastLine;
                if (process != null && process.HasExited) return "Bridge stopped (exit " + process.ExitCode + "). " + lastLine;
                return lastLine;
            }
        }

        void Start()
        {
            // Give an already-running bridge a moment to accept the connection before starting our own.
            Invoke(nameof(AutoLaunch), 2.5f);
        }

        void AutoLaunch()
        {
            if (autoTried) return;
            autoTried = true;
            if (GameController.Instance != null && !GameController.Instance.Bridge.Connected) Launch();
        }

        public void Launch()
        {
            if (process != null && !process.HasExited) { lastLine = "already starting..."; return; }
            string root = FindRepoRoot();
            string jar = Environment.GetEnvironmentVariable("MTGU_BRIDGE_JAR");
            if (string.IsNullOrEmpty(jar) && root != null) jar = Path.Combine(root, "bridge", "target", "forge-bridge-all.jar");
            string forgeGui = Environment.GetEnvironmentVariable("FORGE_GUI_DIR");
            if (string.IsNullOrEmpty(forgeGui) && root != null) forgeGui = Path.Combine(root, "forge", "forge-gui");

            if (jar == null || !File.Exists(jar))
            {
                lastLine = "Bridge jar not found. Run scripts\\setup.ps1 first (looked for " + (jar ?? "bridge/target/forge-bridge-all.jar") + ").";
                return;
            }
            if (forgeGui == null || !Directory.Exists(Path.Combine(forgeGui, "res")))
            {
                lastLine = "Forge not found at " + forgeGui + ". Run scripts\\setup.ps1 first.";
                return;
            }

            string java = "java";
            string javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                string candidate = Path.Combine(javaHome, "bin", Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer ? "java.exe" : "java");
                if (File.Exists(candidate)) java = candidate;
            }

            int port = GameController.Instance != null ? GameController.Instance.Bridge.Port : 7777;
            var psi = new ProcessStartInfo
            {
                FileName = java,
                Arguments = "-Xmx3g -jar \"" + jar + "\" --forge-gui \"" + forgeGui + "\" --port " + port,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(jar),
            };
            try
            {
                process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                process.OutputDataReceived += (s, e) => OnLine(e.Data);
                process.ErrorDataReceived += (s, e) => OnLine(e.Data);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                lastLine = "starting Forge (loading cards takes ~30-60 s)...";
                Debug.Log("[MTGUnited] Started bridge: " + psi.FileName + " " + psi.Arguments);
            }
            catch (Exception ex)
            {
                lastLine = "Could not start Java: " + ex.Message + ". Is Java 17+ installed and on PATH?";
                process = null;
            }
        }

        void OnLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.StartsWith("[bridge")) lastLine = line.Length > 120 ? line.Substring(0, 120) : line;
            Debug.Log("[Forge] " + line);
        }

        static string FindRepoRoot()
        {
            foreach (var start in new[] { Application.dataPath, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                {
                    if (Directory.Exists(Path.Combine(dir.FullName, "bridge")) && Directory.Exists(Path.Combine(dir.FullName, "unity")))
                        return dir.FullName;
                }
            }
            return null;
        }

        void OnApplicationQuit()
        {
            // Only stop a bridge we started ourselves.
            try
            {
                if (process != null && !process.HasExited) process.Kill();
            }
            catch { }
        }
    }
}
