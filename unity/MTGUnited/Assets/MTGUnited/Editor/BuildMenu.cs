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

        static string ProjectDir => Directory.GetParent(Application.dataPath).FullName;           // unity/MTGUnited
        static string RepoDir => Directory.GetParent(ProjectDir).Parent.FullName;                // repo root
        static string BuildsDir => Path.Combine(RepoDir, "Builds");
        static string OutDir => Path.Combine(BuildsDir, ProductName);

        [MenuItem("MTG United/Build Windows Game", priority = 1)]
        static void BuildWindows()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureScene();
            ApplyPlayerSettings();

            if (Directory.Exists(OutDir))
            {
                try { Directory.Delete(OutDir, true); }
                catch (Exception ex) { EditorUtility.DisplayDialog("MTG United", "Couldn't clear the old build (is the game still open?)\n\n" + ex.Message, "OK"); return; }
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
                EditorUtility.DisplayDialog("MTG United", "The build failed. See the Console for the errors.", "OK");
                return;
            }

            CopySupportFiles();
            string zip = Path.Combine(BuildsDir, ProductName + " " + PlayerSettings.bundleVersion + ".zip");
            Zip(OutDir, zip);
            Debug.Log("[MTGUnited] Build ready: " + OutDir + (File.Exists(zip) ? "  ·  zip: " + zip : ""));
            EditorUtility.RevealInFinder(File.Exists(zip) ? zip : options.locationPathName);
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
            if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "0.1" || PlayerSettings.bundleVersion == "1.0")
                PlayerSettings.bundleVersion = "0.1.0";
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
@"MTG UNITED - COMO INSTALAR
==========================

1. Extraia esta pasta inteira em um lugar seu, por exemplo Documentos\MTG United
   (NAO coloque em Arquivos de Programas: o MTG United grava o Forge e as configuracoes dentro da propria pasta).
2. Abra MTG United.exe.
   Se o Windows mostrar ""O Windows protegeu o computador"", clique em ""Mais informacoes"" > ""Executar assim mesmo"".
3. Aba FORGE (icone da bigorna): o tutorial abre sozinho na primeira vez.
   - Instale o Java (botao ""Install Java"") e reabra o MTG United.
   - Ja tem o Forge? ""Use my Forge..."" e escolha a pasta dele. Nao precisa baixar de novo.
   - Nao tem? Instale o Git (botao ""Install Git""), reabra, e clique ""Install Forge"" (uns minutos).
4. Pronto: Home > Play.

KITCHEN TABLE (mesa livre, cartas customizadas, nao precisa do Forge)
- Decks > Custom Sets > Import: escolha o .xml exportado do Magic Set Editor (File > Export > Cockatrice).

JOGAR EM REDE LOCAL (LAN)
- Todos na mesma rede (mesmo Wi-Fi/roteador).
- Home > modo KITCHEN TABLE > ""PLAY ON LAN (HOST / JOIN)"".
- Quem hospeda: HOST A TABLE > OPEN TABLE. Na primeira vez o Windows pergunta sobre o firewall:
  marque ""Redes privadas"" e clique Permitir.
- Os outros: JOIN A TABLE > clique no nome do host (ou digite o endereco IP que aparece pra ele).
- Os sets customizados sao copiados automaticamente entre os jogadores.
- O host clica Start game.
";
    }
}
