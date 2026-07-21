#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Additive-only authoring: ensures a PopupTransition (+ CanvasGroup) on every
// existing popup root in GameScene. Nothing is repositioned, restyled, or
// rebuilt — re-running on an already-equipped scene is a no-op, so manually
// tuned Inspector values always survive.
public static class PopupTransitionAuthoring
{
    [MenuItem("Tools/Planet Boom/UI/Add Popup Transitions To Existing Popups")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
            throw new System.InvalidOperationException("Open GameScene before adding popup transitions.");

        StringBuilder report = new StringBuilder("Popup transition authoring:");
        int added = 0;

        PreLevelPanel preLevel = Object.FindAnyObjectByType<PreLevelPanel>(FindObjectsInactive.Include);
        added += Ensure(preLevel != null ? preLevel.gameObject : null, "Pre-Level Panel", report);

        LevelCompletePanel levelComplete = Object.FindAnyObjectByType<LevelCompletePanel>(FindObjectsInactive.Include);
        added += Ensure(levelComplete != null ? levelComplete.gameObject : null, "Level Complete Panel", report);

        BreakMenuUI breakMenu = Object.FindAnyObjectByType<BreakMenuUI>(FindObjectsInactive.Include);
        added += Ensure(ReadObjectField(breakMenu, "popupRoot") as GameObject, "Break Menu popup", report);

        GameManager manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        added += Ensure(manager != null ? manager.gameOverPanel : null, "Game Over panel", report);

        SkillInventoryUI inventoryUi = Object.FindAnyObjectByType<SkillInventoryUI>(FindObjectsInactive.Include);
        RectTransform canvasRoot = ReadObjectField(inventoryUi, "canvasRoot") as RectTransform;
        Transform inventoryPopup = canvasRoot != null ? canvasRoot.Find("SkillInventoryPopup") : null;
        added += Ensure(inventoryPopup != null ? inventoryPopup.gameObject : null, "Skill Inventory popup", report);

        if (added > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        report.Append(added > 0
            ? $"\n{added} component(s) added. Tune Open/Close Duration, scales, overshoot and the optional Open/Close clips on each PopupTransition, then save GameScene manually."
            : "\nAll popups already carry PopupTransition; nothing changed.");
        Debug.Log(report.ToString());
    }

    private static Object ReadObjectField(Object owner, string propertyName)
    {
        if (owner == null)
            return null;
        SerializedProperty property = new SerializedObject(owner).FindProperty(propertyName);
        return property != null ? property.objectReferenceValue : null;
    }

    private static int Ensure(GameObject root, string label, StringBuilder report)
    {
        if (root == null)
        {
            report.Append($"\n- {label}: NOT FOUND in scene, skipped.");
            return 0;
        }

        int added = 0;
        if (root.GetComponent<CanvasGroup>() == null)
        {
            Undo.AddComponent<CanvasGroup>(root);
            added++;
        }
        if (root.GetComponent<PopupTransition>() == null)
        {
            Undo.AddComponent<PopupTransition>(root);
            added++;
            report.Append($"\n- {label} ({root.name}): PopupTransition added.");
        }
        else
        {
            report.Append($"\n- {label} ({root.name}): already equipped.");
        }
        if (added > 0)
            EditorUtility.SetDirty(root);
        return added;
    }
}
#endif
