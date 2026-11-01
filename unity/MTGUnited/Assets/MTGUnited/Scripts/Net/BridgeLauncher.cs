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

        public bool Running => process != null && !process.HasExited;

        /// <summary>Starts the bridge for whichever Forge setup is installed (see ForgeSetup).</summary>
        public void Launch()
        {
            if (Running) { lastLine = "already starting..."; return; }
            int port0 = GameController.Instance != null ? GameController.Instance.Bridge.Port : 7777;
            if (PortInUse(port0))
            {
                // A bridge is already up (started before scripts reloaded, or by hand): use it, don't start a second.
                lastLine = "A bridge is already running on port " + port0 + "; connecting to it.";
                return;
            }
            ForgeSetup.Load();
            string jar = Environment.GetEnvironmentVariable("MTGU_BRIDGE_JAR");
            string forgeGui = Environment.GetEnvironmentVariable("FORGE_GUI_DIR");
            bool overridden = !string.IsNullOrEmpty(jar) && !string.IsNullOrEmpty(forgeGui);
            if (!overridden && !ForgeSetup.Installed)
            {
                lastLine = "Forge isn't set up yet: open the Forge tab to install it or point to your Forge.";
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
            string args;
            if (overridden)
                args = "-Xmx3g -jar \"" + jar + "\" --forge-gui \"" + forgeGui + "\" --port " + port;
            else if (ForgeSetup.CurrentMode == ForgeSetup.Mode.Existing)
            {
                // Bridge compiled against the player's Forge: their program jar supplies the engine.
                string sep = Path.PathSeparator.ToString();
                string cp = ForgeSetup.BridgeJar + sep + ForgeSetup.GsonJar + sep + ForgeSetup.ForgeJar;
                args = "-Xmx3g -cp \"" + cp + "\" mtgunited.bridge.BridgeMain --forge-gui \"" + ForgeSetup.Assets + "\" --port " + port;
                jar = ForgeSetup.BridgeJar;
            }
            else
            {
                jar = ForgeSetup.BridgeJar;
                args = "-Xmx3g -jar \"" + jar + "\" --forge-gui \"" + ForgeSetup.Assets + "\" --port " + port;
            }

            var psi = new ProcessStartInfo
            {
                FileName = java,
                Arguments = args,
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
                PlayerPrefs.SetInt("bridgePid", process.Id);   // so a later session (or a script reload) can stop it
                lastLine = "starting Forge (loading cards takes ~30-60 s)...";
                ForgeSetup.Log("==> Starting the bridge");
                Debug.Log("[MTGUnited] Started bridge: " + psi.FileName + " " + psi.Arguments);
            }
            catch (Exception ex)
            {
                lastLine = "Could not start Java: " + ex.Message + ". Is Java 17+ installed and on PATH?";
                process = null;
            }
        }

        /// <summary>Stops a bridge this client started (before reinstalling, or from the Forge tab).</summary>
        public void Stop()
        {
            try { if (Running) { process.Kill(); process.WaitForExit(3000); } }
            catch { }
            process = null;
            KillRemembered();
            lastLine = "Bridge stopped.";
        }

        /// <summary>
        /// The bridge started last time (its id is remembered), if it's still running: after a script reload in the
        /// editor the reference to it is lost, and an orphaned bridge keeps its files locked (Update, Uninstall).
        /// </summary>
        static void KillRemembered()
        {
            int pid = PlayerPrefs.GetInt("bridgePid", 0);
            if (pid <= 0) return;
            try
            {
                var p = Process.GetProcessById(pid);
                string name = p.ProcessName.ToLowerInvariant();
                if (!p.HasExited && (name == "java" || name == "javaw")) { p.Kill(); p.WaitForExit(3000); }
            }
            catch { /* already gone */ }
            PlayerPrefs.DeleteKey("bridgePid");
        }

        static bool PortInUse(int port)
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var ar = c.BeginConnect("127.0.0.1", port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(150) && c.Connected;
                    if (ok) c.EndConnect(ar);
                    return ok;
                }
            }
            catch { return false; }
        }

        void OnLine(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            if (line.StartsWith("[bridge")) lastLine = line.Length > 120 ? line.Substring(0, 120) : line;
            ForgeSetup.Log(line);
            Debug.Log("[Forge] " + line);
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
