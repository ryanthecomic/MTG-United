using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MTGUnited.EditorTools
{
    /// <summary>
    /// Menu "MTG United → Build Windows Game": makes a ready-to-share build in one click.
    ///  1. makes sure there's a scene in the build (the game builds its whole UI from code, so an empty scene is enough);
    ///  2. sets the player settings (name, windowed fullscreen, keeps running in the background for LAN games);
    ///  3. builds Builds/MTG United/MTG United.exe;
    ///  4. copies what the Forge tab needs next to the exe (scripts/ and the bridge source) plus a short guide;
    ///  5. zips it all into Builds/MTG United.zip and opens the folder.
    /// Send people the zip: they extract it anywhere (not Program Files) and run MTG United.exe.
    /// </summary>
    static class BuildMenu
    {
        const string ScenePath = "Assets/MTGUnited/Main.unity";
        const string ProductName = "MTG United";
        /// <summary>The release being built (shown on the main menu, in the zip's name and in the guide).</summary>
        public const string Version = "0.1.1";

        static string ProjectDir => Directory.GetParent(Application.dataPath).FullName;           // unity/MTGUnited
        static string RepoDir => Directory.GetParent(ProjectDir).Parent.FullName;                // repo root
        static string BuildsDir => Path.Combine(RepoDir, "Builds");
        static string OutDir => Path.Combine(BuildsDir, ProductName);

        [MenuItem("MTG United/Build Windows Game", priority = 1)]
        static void BuildWindows()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string zip = Build(out string error);
            if (zip == null) { EditorUtility.DisplayDialog("MTG United", error, "OK"); return; }
            EditorUtility.RevealInFinder(File.Exists(zip) ? zip : OutDir);
        }

        /// <summary>
        /// Command line (scripts\build-release.bat):
        /// Unity.exe -batchmode -quit -projectPath unity\MTGUnited -executeMethod MTGUnited.EditorTools.BuildMenu.BuildFromCommandLine
        /// </summary>
        public static void BuildFromCommandLine()
        {
            string zip = Build(out string error);
            if (zip == null) { Debug.LogError("[MTGUnited] " + error); EditorApplication.Exit(1); return; }
            Debug.Log("[MTGUnited] Release ready: " + zip);
            EditorApplication.Exit(0);
        }

        /// <summary>Builds, copies the support files and zips. Returns the zip path, or null with an error.</summary>
        static string Build(out string error)
        {
            error = null;
            EnsureScene();
            ApplyPlayerSettings();

            if (Directory.Exists(OutDir))
            {
                try { Directory.Delete(OutDir, true); }
                catch (Exception ex) { error = "Couldn't clear the old build (is the game still open?)\n\n" + ex.Message; return null; }
            }
            Directory.CreateDirectory(OutDir);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = Path.Combine(OutDir, ProductName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                error = "The build failed. See the Console for the errors.";
                return null;
            }

            CopySupportFiles();
            string zip = Path.Combine(BuildsDir, ProductName + " " + Version + ".zip");
            Zip(OutDir, zip);
            Debug.Log("[MTGUnited] Build ready: " + OutDir + (File.Exists(zip) ? "  ·  zip: " + zip : ""));
            return zip;
        }

        [MenuItem("MTG United/Open Builds Folder", priority = 2)]
        static void OpenBuilds()
        {
            Directory.CreateDirectory(BuildsDir);
            EditorUtility.RevealInFinder(BuildsDir + Path.DirectorySeparatorChar);
        }

        [MenuItem("MTG United/Set Up Build Settings Only", priority = 20)]
        static void SetupOnly()
        {
            EnsureScene();
            ApplyPlayerSettings();
            Debug.Log("[MTGUnited] Scene and player settings are ready. You can also build from File → Build Profiles.");
        }

        /// <summary>An empty scene in the build list. The game starts itself (GameController boots after any scene loads).</summary>
        static void EnsureScene()
        {
            if (!File.Exists(Path.Combine(ProjectDir, ScenePath)))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
                AssetDatabase.Refresh();
            }
            var list = EditorBuildSettings.scenes;
            if (list.Length == 0 || list[0].path != ScenePath || !list[0].enabled)
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = ProductName;
            PlayerSettings.productName = ProductName;
            PlayerSettings.bundleVersion = Version;
            PlayerSettings.runInBackground = true;   // LAN games and the bridge keep going when the window loses focus
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.visibleInBackground = true;
        }

        /// <summary>The Forge tab runs scripts/setup.ps1 and builds the bridge from bridge/: they live next to the exe.</summary>
        static void CopySupportFiles()
        {
            CopyDir(Path.Combine(RepoDir, "scripts"), Path.Combine(OutDir, "scripts"));
            CopyDir(Path.Combine(RepoDir, "bridge", "src"), Path.Combine(OutDir, "bridge", "src"));
            CopyFile(Path.Combine(RepoDir, "bridge", "pom.xml"), Path.Combine(OutDir, "bridge", "pom.xml"));
            CopyFile(Path.Combine(RepoDir, "README.md"), Path.Combine(OutDir, "README.md"));
            CopyFile(Path.Combine(RepoDir, "CHANGELOG.md"), Path.Combine(OutDir, "CHANGELOG.md"));
            File.WriteAllText(Path.Combine(OutDir, "LEIA-ME - COMO INSTALAR.txt"), Guide);
        }

        static void CopyDir(string from, string to)
        {
            if (!Directory.Exists(from)) { Debug.LogWarning("[MTGUnited] Missing " + from); return; }
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(from, to));
            Directory.CreateDirectory(to);
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, file.Replace(from, to), true);
        }

        static void CopyFile(string from, string to)
        {
            if (!File.Exists(from)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(to));
            File.Copy(from, to, true);
        }

        static void Zip(string folder, string zip)
        {
            try
            {
                if (File.Exists(zip)) File.Delete(zip);
                EditorUtility.DisplayProgressBar("MTG United", "Zipping the build...", 0.9f);
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-NoProfile -Command \"Compress-Archive -Path '" + folder + "' -DestinationPath '" + zip + "' -Force\"",
                    UseShellExecute = false, CreateNoWindow = true,
                };
                using (var p = Process.Start(psi)) p.WaitForExit(10 * 60 * 1000);
            }
            catch (Exception ex) { Debug.LogWarning("[MTGUnited] Couldn't zip the build: " + ex.Message); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        const string Guide =
@"MTG UNITED " + Version + @" - COMO INSTALAR
=================================

1. Extraia esta pasta inteira em um lugar seu, por exemplo Documentos\MTG United
   (NAO coloque em Arquivos de Programas: o MTG United grava o Forge e as configuracoes dentro da propria pasta).
2. Abra MTG United.exe.
   Se o Windows mostrar ""O Windows protegeu o computador"", clique em ""Mais informacoes"" > ""Executar assim mesmo"".
3. Aba FORGE (icone da bigorna): o tutorial abre sozinho na primeira vez.
   - Instale o Java (botao ""Install Java"") e reabra o MTG United.
   - Ja tem o Forge? ""Use my Forge..."" e escolha a pasta dele. Nao precisa baixar de novo.
   - Nao tem? Instale o Git (botao ""Install Git""), reabra, e clique ""Install Forge"" (uns minutos).
4. Pronto: Home > Play.

JOGAR CONTRA A IA (FORGE)
- Home > modo FORGE RULES. Escolha o formato: CMDR (Commander), 60 ou 40 cartas.
- Numero de oponentes, decks da IA (PRECONS / RANDOM) e forca da IA no mesmo quadro.

KITCHEN TABLE (mesa livre, cartas customizadas, nao precisa do Forge)
- Aba TABLE (garfo): explica tudo. Sets do Magic Set Editor: File > Export > Cockatrice, depois ""Import a set"".
- Atalhos: Options > Shortcuts > Kitchen Table.

JOGAR EM REDE LOCAL (LAN)
- Todos na mesma rede (mesmo Wi-Fi/roteador). Na primeira vez o Windows pergunta sobre o firewall:
  marque ""Redes privadas"" e clique Permitir (para o MTG United e, no host do Forge, tambem para o Java).
- Home > PLAY ON LAN.
  - Modo KITCHEN TABLE: HOST A TABLE > OPEN TABLE; os outros: JOIN A TABLE. Sets customizados sao copiados sozinhos.
  - Modo FORGE RULES: o host precisa do Forge rodando; quem entra NAO precisa. O host escolhe o formato e quantos bots.
- Se a mesa nao aparecer na lista: REFRESH, ou digite o endereco que aparece na tela do host.
";
    }
}
