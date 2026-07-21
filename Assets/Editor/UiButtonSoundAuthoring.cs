#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Additive-only authoring for UI interaction sounds. Run it once per scene
// (MainMenu, LevelMap, GameScene — whichever is open): it ensures the shared
// Resources/UiSoundLibrary asset exists, then adds a UiButtonSound to every
// scene Button that doesn't have one, guessing the category from the
// button's name. Nothing is moved, restyled, or rebuilt; re-running skips
// every button that already carries the component (including ones whose
// category you changed by hand), so it is safe to run repeatedly.
//
// Deliberately skipped:
// - Buttons under QuickSlotBar (skill use already has its own success/fail
//   audio via SkillInventoryManager — a click sound would double up).
// - Buttons whose Inspector OnClick already targets an AudioSource or the
//   AudioManager (existing custom click audio; flagged in the log instead).
public static class UiButtonSoundAuthoring
{
    private const string LibraryAssetPath = "Assets/Resources/UiSoundLibrary.asset";

    [MenuItem("Tools/Planet Boom/UI/Add UI Button Sounds To Open Scene")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            throw new System.InvalidOperationException("Open a scene before adding UI button sounds.");

        EnsureLibraryAsset();

        StringBuilder report = new StringBuilder($"UI button sound authoring ({scene.name}):");
        int added = 0;
        Button[] buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include);
        foreach (Button button in buttons)
        {
            if (button.GetComponent<UiButtonSound>() != null)
                continue;
            if (IsUnder(button.transform, "QuickSlotBar"))
            {
                report.Append($"\n- {Path(button.transform)}: skipped (quick slot — has its own skill audio).");
                continue;
            }
            if (HasCustomClickAudio(button))
            {
                report.Append($"\n- {Path(button.transform)}: skipped (OnClick already plays custom audio).");
                continue;
            }

            UiSoundType type = GuessCategory(button.name);
            UiButtonSound sound = Undo.AddComponent<UiButtonSound>(button.gameObject);
            SerializedObject serialized = new SerializedObject(sound);
            serialized.FindProperty("soundType").enumValueIndex = (int)type;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(button.gameObject);
            added++;
            report.Append($"\n- {Path(button.transform)} → {type}");
        }

        if (added > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        report.Append(added > 0
            ? $"\n{added} UiButtonSound component(s) added. Review categories in the Inspector if a guess is off, then save {scene.name} manually."
            : "\nNo new components needed; every eligible button is already covered.");
        Debug.Log(report.ToString());
    }

    private static void EnsureLibraryAsset()
    {
        if (Resources.Load<UiSoundLibrary>("UiSoundLibrary") != null)
            return;
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        UiSoundLibrary library = ScriptableObject.CreateInstance<UiSoundLibrary>();
        AssetDatabase.CreateAsset(library, LibraryAssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"Created {LibraryAssetPath} — assign the five shared category clips there once; every scene reads the same asset.", library);
    }

    // Name-based first guess only; the Inspector value on each button is the
    // real source of truth afterwards (re-runs never overwrite it).
    private static UiSoundType GuessCategory(string buttonName)
    {
        string name = buttonName.ToLowerInvariant();
        if (name.Contains("sector"))
            return UiSoundType.Default; // sector arrows are plain navigation
        if (Matches(name, "sound", "music", "sfx", "vibr", "vibe", "audio", "mute", "toggle"))
            return UiSoundType.Toggle;
        if (Matches(name, "close", "back", "exit", "quit", "home", "return") || name == "x")
            return UiSoundType.Back;
        if (Matches(name, "ready", "start", "play", "continue", "restart", "retry", "tryagain", "try again", "next", "confirm", "yes", "ok"))
            return UiSoundType.Confirm;
        return UiSoundType.Default;
    }

    private static bool Matches(string name, params string[] keys)
    {
        foreach (string key in keys)
            if (name.Contains(key))
                return true;
        return false;
    }

    private static bool IsUnder(Transform transform, string ancestorName)
    {
        for (Transform current = transform; current != null; current = current.parent)
            if (current.name == ancestorName)
                return true;
        return false;
    }

    private static bool HasCustomClickAudio(Button button)
    {
        int count = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < count; i++)
        {
            Object target = button.onClick.GetPersistentTarget(i);
            if (target is AudioSource || target is AudioManager)
                return true;
        }
        return false;
    }

    private static string Path(Transform transform)
    {
        StringBuilder path = new StringBuilder(transform.name);
        for (Transform current = transform.parent; current != null; current = current.parent)
            path.Insert(0, current.name + "/");
        return path.ToString();
    }
}
#endif
