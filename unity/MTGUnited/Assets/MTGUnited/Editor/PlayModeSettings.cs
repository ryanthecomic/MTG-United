using UnityEditor;

namespace MTGUnited.EditorTools
{
    /// <summary>
    /// Unity normally recompiles scripts the moment a file changes, even during Play mode. That reload wipes the
    /// client's connection to the Forge bridge mid-game. This switches the editor preference
    /// (Preferences > General > Script Changes While Playing) to "Recompile After Finished Playing".
    ///
    /// It also holds that reload back for a moment when Play stops: reloading while Unity is still in the middle of
    /// stopping is what logs "Mismatched state in DefaultScenario, was Stopping but expected Idle". The reload now
    /// waits until the editor is fully back in Edit mode.
    /// </summary>
    [InitializeOnLoad]
    static class PlayModeSettings
    {
        const string Key = "ScriptCompilationDuringPlay";
        const int RecompileAfterFinishedPlaying = 1;
        const string LockKey = "MTGUnited.ReloadLockedForStop";

        static PlayModeSettings()
        {
            if (EditorPrefs.GetInt(Key, 0) != RecompileAfterFinishedPlaying)
            {
                EditorPrefs.SetInt(Key, RecompileAfterFinishedPlaying);
                UnityEngine.Debug.Log("[MTGUnited] Set 'Script Changes While Playing' to 'Recompile After Finished Playing' so edits don't break a running game.");
            }
            // A lock left over from before a reload (the lock itself doesn't survive one): just forget it.
            SessionState.SetBool(LockKey, false);
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.playModeStateChanged += OnPlayMode;
        }

        static void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && !SessionState.GetBool(LockKey, false))
            {
                SessionState.SetBool(LockKey, true);
                lockedAt = EditorApplication.timeSinceStartup;
                EditorApplication.LockReloadAssemblies();
                EditorApplication.update -= Watchdog;
                EditorApplication.update += Watchdog;
            }
            else if (change == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(LockKey, false))
            {
                // Two editor ticks after Edit mode is back, let the queued recompile reload.
                EditorApplication.delayCall += () => EditorApplication.delayCall += Unlock;
            }
        }

        static double lockedAt;

        /// <summary>Safety net: never keep scripts from reloading for more than a few seconds after Play stops.</summary>
        static void Watchdog()
        {
            if (!SessionState.GetBool(LockKey, false)) { EditorApplication.update -= Watchdog; return; }
            if (!EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.timeSinceStartup - lockedAt > 3.0) Unlock();
        }

        static void Unlock()
        {
            EditorApplication.update -= Watchdog;
            if (!SessionState.GetBool(LockKey, false)) return;
            SessionState.SetBool(LockKey, false);
            EditorApplication.UnlockReloadAssemblies();
        }
    }
}
