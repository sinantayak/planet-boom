#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-time migration checkpoint requested after the HUD authoring passes.
// It saves the currently open authored state exactly as-is; it never reapplies
// or regenerates any HUD. The persistent EditorPrefs guard prevents this from
// becoming another startup action.
public static class PersistCurrentUnityStateOnce
{
    private const string CompletedKey = "PlanetBoom.PersistCurrentHudState.Completed.v1";
    private static bool waitingForEditMode;

    private static void Schedule()
    {
        if (EditorPrefs.GetBool(CompletedKey, false))
            return;
        EditorApplication.delayCall += TrySave;
    }

    private static void TrySave()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += TrySave;
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            if (!waitingForEditMode)
            {
                waitingForEditMode = true;
                EditorApplication.playModeStateChanged += HandlePlayModeChanged;
            }
            Debug.Log("HUD persistence checkpoint is waiting for Edit Mode before saving open scenes.");
            return;
        }

        SaveNow();
    }

    private static void HandlePlayModeChanged(PlayModeStateChange change)
    {
        if (change != PlayModeStateChange.EnteredEditMode)
            return;
        EditorApplication.playModeStateChanged -= HandlePlayModeChanged;
        waitingForEditMode = false;
        EditorApplication.delayCall += TrySave;
    }

    private static void SaveNow()
    {
        AssetDatabase.SaveAssets();
        bool scenesSaved = EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        EditorPrefs.SetBool(CompletedKey, true);
        Debug.Log(scenesSaved
            ? "Current Unity HUD state saved permanently. Startup authoring prompts are disabled."
            : "Asset changes were saved, but one or more open scenes could not be saved. Check their scene paths.");
    }
}
#endif
