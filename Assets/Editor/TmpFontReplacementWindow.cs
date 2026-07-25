#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class TmpFontReplacementWindow : EditorWindow
{
    private const string MenuPath =
        "Tools/Planet Boom/UI/Replace TMP Fonts Project-Wide";
    private const string EnglishCharacters =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const string TurkishCharacters = "ÇĞİÖŞÜçğışü";

    [Serializable]
    private sealed class AssetResult
    {
        public string kind;
        public string path;
        public int textComponents;
        public int serializedReferences;
        public readonly List<string> details = new List<string>();
        public int Total => textComponents + serializedReferences;
    }

    private TMP_FontAsset oldFont;
    private TMP_FontAsset newFont;
    private readonly List<AssetResult> previewResults = new List<AssetResult>();
    private readonly List<string> scanErrors = new List<string>();
    private Vector2 scroll;
    private TMP_FontAsset previewOldFont;
    private TMP_FontAsset previewNewFont;
    private bool previewReady;
    private int previewTextCount;
    private int previewReferenceCount;

    [MenuItem(MenuPath)]
    private static void Open()
    {
        var window = GetWindow<TmpFontReplacementWindow>(
            "Replace TMP Fonts");
        window.minSize = new Vector2(650f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        if (oldFont == null)
            oldFont = TMP_Settings.defaultFontAsset;
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Project-Wide TMP Font Replacement",
            EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Only references to the selected Old Font are replaced. This keeps " +
            "separate icon fonts and TMP Sprite Assets untouched. Preview is " +
            "required before Apply.", MessageType.Info);

        EditorGUI.BeginChangeCheck();
        oldFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
            new GUIContent("Old Font", "Defaults to TMP Settings' current default font."),
            oldFont, typeof(TMP_FontAsset), false);
        newFont = (TMP_FontAsset)EditorGUILayout.ObjectField(
            new GUIContent("New TMP Font Asset"), newFont,
            typeof(TMP_FontAsset), false);
        if (EditorGUI.EndChangeCheck())
            InvalidatePreview();

        DrawGlyphStatus();
        EditorGUILayout.Space(6f);

        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode ||
                   oldFont == null || newFont == null || oldFont == newFont))
        {
            if (GUILayout.Button("Preview Project-Wide Replacement",
                    GUILayout.Height(30f)))
                Preview();
        }

        bool matchingPreview = previewReady && previewOldFont == oldFont &&
            previewNewFont == newFont;
        using (new EditorGUI.DisabledScope(
                   EditorApplication.isPlayingOrWillChangePlaymode ||
                   !matchingPreview))
        {
            if (GUILayout.Button("Apply Previewed Replacement",
                    GUILayout.Height(34f)))
                ConfirmAndApply();
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode)
            EditorGUILayout.HelpBox(
                "Exit Play Mode before previewing or applying. The tool never " +
                "opens or edits scenes during Play Mode.", MessageType.Warning);

        EditorGUILayout.Space(8f);
        DrawPreview();
    }

    private void DrawGlyphStatus()
    {
        if (newFont == null)
            return;
        string missingEnglish = GetMissingGlyphs(newFont, EnglishCharacters);
        string missingTurkish = GetMissingGlyphs(newFont, TurkishCharacters);
        if (missingEnglish.Length == 0 && missingTurkish.Length == 0)
            EditorGUILayout.HelpBox(
                "English/Turkish glyph check passed. Turkish characters: " +
                TurkishCharacters,
                MessageType.Info);
        else
            EditorGUILayout.HelpBox(
                (missingEnglish.Length > 0
                    ? "Missing English glyphs: " + missingEnglish + "\n"
                    : string.Empty) +
                (missingTurkish.Length > 0
                    ? "Missing Turkish glyphs: " + missingTurkish + "\n"
                    : string.Empty) +
                "The tool will still allow preview, but Apply will " +
                "ask for an additional confirmation.", MessageType.Warning);
    }

    private void DrawPreview()
    {
        if (!previewReady)
        {
            EditorGUILayout.LabelField("No current preview.");
            return;
        }

        EditorGUILayout.LabelField(
            $"Preview: {previewTextCount} TMP component(s), " +
            $"{previewReferenceCount} serialized script reference(s), " +
            $"{previewResults.Count} changed asset(s).",
            EditorStyles.boldLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (AssetResult result in previewResults)
        {
            EditorGUILayout.LabelField(
                $"{result.kind}: {result.path}  —  TMP {result.textComponents}, " +
                $"serialized {result.serializedReferences}");
            foreach (string detail in result.details)
                EditorGUILayout.LabelField("    " + detail,
                    EditorStyles.miniLabel);
        }
        foreach (string error in scanErrors)
            EditorGUILayout.HelpBox(error, MessageType.Error);
        EditorGUILayout.EndScrollView();
    }

    private void Preview()
    {
        if (!ValidateReady())
            return;

        previewResults.Clear();
        scanErrors.Clear();
        previewTextCount = 0;
        previewReferenceCount = 0;
        try
        {
            ScanScenes(false, false, previewResults);
            ScanPrefabs(false, previewResults);
            ScanScriptableAssets(false, previewResults);
            previewOldFont = oldFont;
            previewNewFont = newFont;
            previewReady = true;
            Recount(previewResults);
            Debug.Log(BuildReport("TMP font replacement preview", previewResults));
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void ConfirmAndApply()
    {
        if (!ValidateReady() || !previewReady ||
            previewOldFont != oldFont || previewNewFont != newFont)
            return;

        string missing = GetMissingGlyphs(
            newFont, EnglishCharacters + TurkishCharacters);
        if (missing.Length > 0 && !EditorUtility.DisplayDialog(
                "Missing Turkish Glyphs",
                "The selected font is missing: " + missing +
                "\n\nApply anyway?", "Continue", "Cancel"))
            return;

        int choice = EditorUtility.DisplayDialogComplex(
            "Apply TMP Font Replacement",
            $"Replace {previewTextCount + previewReferenceCount} previewed " +
            "reference(s)?\n\nPrefab and ScriptableObject assets will be saved. " +
            "Choose whether changed scenes should be left open and dirty or " +
            "saved now. Saving an already-open scene also saves its other " +
            "unsaved changes.",
            "Apply, Keep Scenes Dirty", "Cancel", "Apply & Save Scenes");
        if (choice == 1)
            return;
        bool saveScenes = choice == 2;

        var applied = new List<AssetResult>();
        scanErrors.Clear();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace TMP Fonts Project-Wide");
        try
        {
            ScanScenes(true, saveScenes, applied);
            ScanPrefabs(true, applied);
            ScanScriptableAssets(true, applied);
            SetTmpDefaultFont();
            AssetDatabase.SaveAssets();
            Undo.CollapseUndoOperations(undoGroup);
            Recount(applied);
            string report = BuildReport("TMP font replacement applied", applied);
            Debug.Log(report);
            EditorUtility.DisplayDialog(
                "TMP Font Replacement Complete",
                $"Changed {previewTextCount} TMP component(s) and " +
                $"{previewReferenceCount} serialized reference(s) across " +
                $"{applied.Count} asset(s).\n\n" +
                (saveScenes
                    ? "Changed scenes were saved with your confirmation."
                    : "Changed scenes were left open and marked dirty. Save them manually.") +
                (scanErrors.Count > 0
                    ? $"\n\n{scanErrors.Count} item(s) could not be processed; see Console."
                    : string.Empty),
                "OK");
            previewReady = false;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void ScanScenes(bool apply, bool saveScenes,
        List<AssetResult> results)
    {
        Scene previouslyActive = SceneManager.GetActiveScene();
        string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            EditorUtility.DisplayProgressBar(
                apply ? "Applying TMP font to scenes" : "Scanning scenes",
                path, guids.Length == 0 ? 0f : (float)i / guids.Length);
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedByTool = !scene.IsValid() || !scene.isLoaded;
            bool previewScene = openedByTool && !apply;
            try
            {
                if (openedByTool)
                    scene = previewScene
                        ? EditorSceneManager.OpenPreviewScene(path)
                        : EditorSceneManager.OpenScene(
                            path, OpenSceneMode.Additive);
                AssetResult result = ProcessScene(scene, path, apply);
                if (result.Total > 0)
                {
                    results.Add(result);
                    if (apply)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (saveScenes)
                            EditorSceneManager.SaveScene(scene);
                    }
                }

                // Preview never retains temporary scenes. Applied dirty scenes
                // stay open unless the user explicitly approved saving.
                if (openedByTool && (!apply || saveScenes || result.Total == 0))
                {
                    if (previewScene)
                        EditorSceneManager.ClosePreviewScene(scene);
                    else
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
            catch (Exception exception)
            {
                AddScanError("Scene", path, exception);
                if (openedByTool && scene.IsValid() && scene.isLoaded &&
                    (!apply || !scene.isDirty))
                {
                    if (previewScene)
                        EditorSceneManager.ClosePreviewScene(scene);
                    else
                        EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
        if (previouslyActive.IsValid() && previouslyActive.isLoaded)
            SceneManager.SetActiveScene(previouslyActive);
    }

    private AssetResult ProcessScene(Scene scene, string path, bool apply)
    {
        var result = NewResult("Scene", path);
        foreach (GameObject root in scene.GetRootGameObjects())
            ProcessHierarchy(root, apply, true, result);
        return result;
    }

    private void ScanPrefabs(bool apply, List<AssetResult> results)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                continue;
            EditorUtility.DisplayProgressBar(
                apply ? "Applying TMP font to prefabs" : "Scanning prefabs",
                path, guids.Length == 0 ? 0f : (float)i / guids.Length);
            GameObject root = null;
            try
            {
                root = PrefabUtility.LoadPrefabContents(path);
                AssetResult result = NewResult("Prefab", path);
                ProcessHierarchy(root, apply, false, result);
                if (result.Total > 0)
                {
                    results.Add(result);
                    if (apply)
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }
            catch (Exception exception)
            {
                AddScanError("Prefab", path, exception);
            }
            finally
            {
                if (root != null)
                    PrefabUtility.UnloadPrefabContents(root);
            }
        }
    }

    private void ProcessHierarchy(GameObject root, bool apply,
        bool supportUndo, AssetResult result)
    {
        TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text text in texts)
        {
            if (text == null || text.font != oldFont ||
                (!supportUndo && IsNestedPrefabContent(root, text.gameObject)))
                continue;
            result.textComponents++;
            result.details.Add("TMP: " + GetTransformPath(text.transform));
            if (!apply)
                continue;
            if (supportUndo)
                Undo.RecordObject(text, "Replace TMP Font");
            Material previousMaterial = text.fontSharedMaterial;
            text.font = newFont;
            if (!IsMaterialCompatible(previousMaterial, newFont))
                text.fontSharedMaterial = newFont.material;
            EditorUtility.SetDirty(text);
        }

        Component[] components = root.GetComponentsInChildren<Component>(true);
        foreach (Component component in components)
        {
            if (component == null || component is TMP_Text ||
                (!supportUndo &&
                 IsNestedPrefabContent(root, component.gameObject)))
                continue;
            result.serializedReferences += ProcessSerializedObject(
                component, apply, supportUndo, result.details);
        }
    }

    private void ScanScriptableAssets(bool apply, List<AssetResult> results)
    {
        string[] guids = AssetDatabase.FindAssets(
            "t:ScriptableObject", new[] { "Assets" });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            ScriptableObject asset =
                AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
            if (asset == null || asset is TMP_FontAsset ||
                asset is TMP_SpriteAsset || asset is TMP_Settings)
                continue;
            EditorUtility.DisplayProgressBar(
                apply ? "Applying serialized font references" :
                    "Scanning serialized font references",
                path, guids.Length == 0 ? 0f : (float)i / guids.Length);
            try
            {
                var details = new List<string>();
                int count = ProcessSerializedObject(
                    asset, apply, true, details);
                if (count > 0)
                {
                    AssetResult result = NewResult("Asset", path);
                    result.serializedReferences = count;
                    result.details.AddRange(details);
                    results.Add(result);
                }
            }
            catch (Exception exception)
            {
                AddScanError("Asset", path, exception);
            }
        }
    }

    private int ProcessSerializedObject(UnityEngine.Object target,
        bool apply, bool supportUndo, List<string> details)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.GetIterator();
        int count = 0;
        bool undoRecorded = false;
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference ||
                property.objectReferenceValue != oldFont)
                continue;
            count++;
            details?.Add(
                $"Serialized: {target.GetType().Name} \"{target.name}\" → " +
                property.propertyPath);
            if (apply)
            {
                if (supportUndo && !undoRecorded)
                {
                    Undo.RecordObject(target,
                        "Replace serialized TMP Font reference");
                    undoRecorded = true;
                }
                property.objectReferenceValue = newFont;
            }
        }

        if (apply && count > 0)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
        return count;
    }

    private void SetTmpDefaultFont()
    {
        TMP_Settings settings = TMP_Settings.instance;
        if (settings == null || TMP_Settings.defaultFontAsset == newFont)
            return;
        Undo.RecordObject(settings, "Set TMP Default Font");
        TMP_Settings.defaultFontAsset = newFont;
        EditorUtility.SetDirty(settings);
    }

    private static bool IsMaterialCompatible(Material material,
        TMP_FontAsset font)
    {
        if (material == null || font == null || font.atlasTexture == null)
            return false;
        return material.mainTexture == font.atlasTexture;
    }

    private static bool IsNestedPrefabContent(GameObject loadedRoot,
        GameObject candidate)
    {
        if (candidate == loadedRoot ||
            !PrefabUtility.IsPartOfPrefabInstance(candidate))
            return false;
        GameObject nearest = PrefabUtility.GetNearestPrefabInstanceRoot(candidate);
        return nearest != null && nearest != loadedRoot;
    }

    private bool ValidateReady()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Unavailable in Play Mode",
                "Exit Play Mode before using the TMP font replacement tool.", "OK");
            return false;
        }
        if (oldFont == null || newFont == null || oldFont == newFont)
        {
            EditorUtility.DisplayDialog("Select Fonts",
                "Select different Old Font and New TMP Font Asset values.", "OK");
            return false;
        }
        return true;
    }

    private static string GetMissingGlyphs(TMP_FontAsset font,
        string characters)
    {
        if (font == null)
            return characters;
        var missing = new StringBuilder();
        foreach (char character in characters)
            if (!font.HasCharacter(character, false, false))
                missing.Append(character).Append(' ');
        return missing.ToString().TrimEnd();
    }

    private void Recount(List<AssetResult> results)
    {
        previewTextCount = 0;
        previewReferenceCount = 0;
        foreach (AssetResult result in results)
        {
            previewTextCount += result.textComponents;
            previewReferenceCount += result.serializedReferences;
        }
    }

    private void AddScanError(string kind, string path, Exception exception)
    {
        string message = $"{kind} scan failed: {path}\n" +
            $"{exception.GetType().Name}: {exception.Message}";
        scanErrors.Add(message);
        Debug.LogError(message);
    }

    private string BuildReport(string heading, List<AssetResult> results)
    {
        var report = new StringBuilder();
        report.AppendLine($"[{heading}]");
        report.AppendLine($"Old: {oldFont?.name}");
        report.AppendLine($"New: {newFont?.name}");
        string missingTurkish = GetMissingGlyphs(newFont, TurkishCharacters);
        report.AppendLine(missingTurkish.Length == 0
            ? "Turkish glyphs: complete"
            : "Missing Turkish glyphs: " + missingTurkish);
        foreach (AssetResult result in results)
        {
            report.AppendLine(
                $"- {result.kind}: {result.path} " +
                $"(TMP={result.textComponents}, serialized={result.serializedReferences})");
            foreach (string detail in result.details)
                report.AppendLine("    " + detail);
        }
        if (scanErrors.Count > 0)
            report.AppendLine($"Errors: {scanErrors.Count}");
        return report.ToString();
    }

    private static AssetResult NewResult(string kind, string path) =>
        new AssetResult { kind = kind, path = path };

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<missing>";
        var names = new Stack<string>();
        for (Transform current = transform; current != null;
             current = current.parent)
            names.Push(current.name);
        return string.Join("/", names);
    }

    private void InvalidatePreview()
    {
        previewReady = false;
        previewResults.Clear();
        scanErrors.Clear();
    }
}
#endif
