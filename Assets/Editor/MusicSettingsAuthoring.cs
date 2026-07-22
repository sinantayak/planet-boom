#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

// Creates the shared Resources/MusicSettings asset and, if its track slot is
// still empty, assigns the project's background music clip automatically.
// Safe to run repeatedly: an existing asset is never replaced and a manually
// assigned track is never overwritten. No scene is touched.
public static class MusicSettingsAuthoring
{
    private const string AssetPath = "Assets/Resources/MusicSettings.asset";
    private const string PreferredClipPath = "Assets/Sounds/SFX_BackgroundMusic-1.mp3";

    [MenuItem("Tools/Planet Boom/UI/Create Music Settings Asset")]
    public static void Apply()
    {
        MusicSettings settings = Resources.Load<MusicSettings>("MusicSettings");
        if (settings == null)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");
            settings = ScriptableObject.CreateInstance<MusicSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
        }

        SerializedObject serialized = new SerializedObject(settings);
        SerializedProperty track = serialized.FindProperty("defaultTrack");
        if (track.objectReferenceValue == null)
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PreferredClipPath);
            if (clip == null)
            {
                // Fall back to a name search in case the file moves or its
                // extension changes.
                foreach (string guid in AssetDatabase.FindAssets("SFX_BackgroundMusic t:AudioClip"))
                {
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                    if (clip != null)
                        break;
                }
            }
            if (clip != null)
            {
                track.objectReferenceValue = clip;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }
        AssetDatabase.SaveAssets();

        Debug.Log(track.objectReferenceValue != null
            ? $"MusicSettings ready — track: {track.objectReferenceValue.name}. Swap it any time on {AssetPath}."
            : $"MusicSettings created at {AssetPath}, but no background clip was found — assign Default Track manually.",
            settings);
        Selection.activeObject = settings;
    }
}
#endif
