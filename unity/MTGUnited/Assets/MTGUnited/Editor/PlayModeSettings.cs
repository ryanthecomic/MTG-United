using UnityEditor;

namespace MTGUnited.EditorTools
{
    /// <summary>
    /// Unity normally recompiles scripts the moment a file changes, even during Play mode. That reload wipes the
    /// client's connection to the Forge bridge mid-game. This switches the editor preference
    /// (Preferences > General > Script Changes While Playing) to "Recompile After Finished Playing".
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeSettings
    {
        const string Key = "ScriptCompilationDuringPlay";
        const int RecompileAfterFinishedPlaying = 1;

        static PlayModeSettings()
        {
            if (EditorPrefs.GetInt(Key, 0) != RecompileAfterFinishedPlaying)
            {
                EditorPrefs.SetInt(Key, RecompileAfterFinishedPlaying);
                UnityEngine.Debug.Log("[MTGUnited] Set 'Script Changes While Playing' to 'Recompile After Finished Playing' so edits don't break a running game.");
            }
        }
    }
}
