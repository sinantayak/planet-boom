#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Creates the optional shared Resources/SceneTransitionSettings asset so the
// fade durations and transition clip are Inspector-editable. Safe to run any
// number of times — an existing asset is only selected, never replaced. No
// scene is touched (transitions need zero scene wiring).
public static class SceneTransitionSettingsAuthoring
{
    private const string AssetPath = "Assets/Resources/SceneTransitionSettings.asset";

    [MenuItem("Tools/Planet Boom/UI/Create Scene Transition Settings Asset")]
    public static void Apply()
    {
        SceneTransitionSettings settings = Resources.Load<SceneTransitionSettings>("SceneTransitionSettings");
        if (settings == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            settings = ScriptableObject.CreateInstance<SceneTransitionSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log($"Created {AssetPath} — tune fade durations and assign the optional transition clip there.", settings);
        }
        else
        {
            Debug.Log("SceneTransitionSettings already exists; selecting it.", settings);
        }
        Selection.activeObject = settings;
    }
}
#endif
