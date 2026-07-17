using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class SkillInventoryLockUIAuthoring
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("Tools/Planet Boom/Author Skill Inventory Lock UI")]
    public static void Author()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        Transform popup = FindTransform(scene, "SkillInventoryPopup");
        Transform content = popup != null
            ? popup.Find("Panel/SkillGridViewport/Content")
            : null;
        if (content == null)
            throw new InvalidOperationException(
                "Skill Inventory Content was not found in GameScene.");

        Transform template = content.Find(nameof(SkillType.CosmicMimic));
        if (template == null)
            throw new InvalidOperationException(
                "CosmicMimic entry is required as the persistent entry template.");

        int siblingIndex = 0;
        foreach (SkillType type in SkillInventoryUI.DisplayOrder)
        {
            Transform entry = content.Find(type.ToString());
            if (entry == null)
            {
                GameObject clone = UnityEngine.Object.Instantiate(template.gameObject, content);
                clone.name = type.ToString();
                entry = clone.transform;
            }

            EnsureLockOverlay(entry);
            entry.SetSiblingIndex(siblingIndex++);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Skill Inventory Lock UI authored in GameScene: all skill entries are persistent and contain LockOverlay children.");
    }

    private static void EnsureLockOverlay(Transform entry)
    {
        Transform existing = entry.Find("LockOverlay");
        GameObject overlayObject;
        if (existing != null)
        {
            overlayObject = existing.gameObject;
        }
        else
        {
            overlayObject = new GameObject(
                "LockOverlay", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            overlayObject.layer = entry.gameObject.layer;
            overlayObject.transform.SetParent(entry, false);
        }

        RectTransform rect = overlayObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(72f, 72f);
        rect.SetAsLastSibling();

        Image image = overlayObject.GetComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.sprite = null;
        overlayObject.SetActive(false);
    }

    private static Transform FindTransform(Scene scene, string objectName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Transform found = FindRecursive(root.transform, objectName);
            if (found != null)
                return found;
        }
        return null;
    }

    private static Transform FindRecursive(Transform current, string objectName)
    {
        if (current.name == objectName)
            return current;
        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindRecursive(current.GetChild(i), objectName);
            if (found != null)
                return found;
        }
        return null;
    }
}
