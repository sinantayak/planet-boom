#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the Active Effects HUD in GameScene, directly above the existing
// bottom-left Inventory/Chest bag (which is never moved or touched):
//
//   SafeAreaRoot
//   ├── InventoryButton              (existing — untouched)
//   └── ActiveEffectsHUD             (ActiveEffectsHUD component)
//       └── EffectStack              (VerticalLayoutGroup, grows upward)
//           ├── LuckyDropSlot        (CanvasGroup) → Icon
//           ├── DoubleTimeDropSlot   (CanvasGroup) → Icon
//           ├── StarBoosterSlot      (CanvasGroup) → Icon
//           └── CosmicShieldSlot     (CanvasGroup) → Icon
//
// Child order = visual order bottom → top (the layout group is authored
// with LowerLeft alignment + reverseArrangement), so Lucky Drop hugs the
// bag and Cosmic Shield pops in on top of the column.
//
// Additive and re-run safe: layout, sprites and layout-group settings are
// applied only when an element is first created — manual Inspector edits
// (HUD position, icon sizes, spacing, per-slot scale) are never overwritten
// on a later run. Component wiring refreshes every run. Slots are left
// active in the editor for comfortable authoring; ActiveEffectsHUD.Awake
// hides them at runtime until their effect is actually active. The scene is
// marked dirty but never saved.
public static class ActiveEffectsHudAuthoring
{
    private const string IconFolder = "Assets/UI Elements/Skills";
    private const float SlotSize = 130f;

    [MenuItem("Tools/Planet Boom/UI/Create Active Effects HUD")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
            throw new System.InvalidOperationException("Open GameScene before creating the Active Effects HUD.");

        RectTransform bag = FindRect(scene, "InventoryButton");
        RectTransform parent = bag != null ? bag.parent as RectTransform : FindRect(scene, "SafeAreaRoot");
        if (parent == null)
            throw new System.InvalidOperationException("Neither InventoryButton nor SafeAreaRoot found in GameScene.");

        bool created;
        RectTransform root = EnsureRect(parent, "ActiveEffectsHUD", out created);
        if (created)
        {
            // Default resting place: a hair inset from the bag's left edge,
            // starting just above its visual top — freely movable afterwards.
            Vector2 position = new Vector2(60f, 205f);
            if (bag != null && bag.anchorMin == Vector2.zero && bag.anchorMax == Vector2.zero)
            {
                float bagTop = bag.anchoredPosition.y +
                    bag.sizeDelta.y * bag.localScale.y * (1f - bag.pivot.y);
                position = new Vector2(bag.anchoredPosition.x + 10f, bagTop + 15f);
            }
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.anchoredPosition = position;
            root.sizeDelta = new Vector2(150f, 620f);
            root.localScale = Vector3.one;
        }
        ActiveEffectsHUD controller = EnsureComponent<ActiveEffectsHUD>(root.gameObject);
        Transform legacySharedName = root.Find("IntroEffectName");
        if (legacySharedName != null)
            legacySharedName.gameObject.SetActive(false);

        RectTransform stack = EnsureRect(root, "EffectStack", out created);
        if (created)
        {
            stack.anchorMin = Vector2.zero; stack.anchorMax = Vector2.one;
            stack.offsetMin = stack.offsetMax = Vector2.zero;
            stack.localScale = Vector3.one;
        }
        VerticalLayoutGroup layout = stack.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            // Configured once; every knob (spacing, alignment, direction)
            // stays editable on the component afterwards.
            layout = Undo.AddComponent<VerticalLayoutGroup>(stack.gameObject);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.LowerLeft;
            layout.reverseArrangement = true; // first child sits at the bottom
            layout.childControlWidth = false; layout.childControlHeight = false;
            layout.childForceExpandWidth = false; layout.childForceExpandHeight = false;
        }

        Image lucky = EnsureSlot(stack, "LuckyDropSlot", $"{IconFolder}/LuckyDrop.png", "LUCKY DROP");
        Image doubleTime = EnsureSlot(stack, "DoubleTimeDropSlot", $"{IconFolder}/DoubleTimeDrop.png", "DOUBLE TIME DROP");
        Image star = EnsureSlot(stack, "StarBoosterSlot", $"{IconFolder}/StarBooster.png", "STAR BOOSTER");
        Image shield = EnsureSlot(stack, "CosmicShieldSlot", $"{IconFolder}/CosmicShield.png", "COSMIC SHIELD");

        SerializedObject serialized = new SerializedObject(controller);
        WireSlot(serialized, "luckyDropSlot", lucky);
        WireSlot(serialized, "doubleTimeDropSlot", doubleTime);
        WireSlot(serialized, "starBoosterSlot", star);
        WireSlot(serialized, "cosmicShieldSlot", shield);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;
        Debug.Log("Active Effects HUD ready above " +
                  (bag != null ? "InventoryButton" : "SafeAreaRoot origin") +
                  ". Assign the optional Effect Reveal Clip on the ActiveEffectsHUD component. " +
                  "Save GameScene manually.");
    }

    // Slot container (CanvasGroup for the reveal fade) + stretched icon.
    // Icon sprite/appearance is set only at creation; a manually swapped
    // sprite or resized slot survives every re-run.
    private static Image EnsureSlot(RectTransform stack, string name, string spritePath,
        string previewName)
    {
        bool created;
        RectTransform slot = EnsureRect(stack, name, out created);
        if (created)
        {
            slot.sizeDelta = new Vector2(SlotSize, SlotSize);
            slot.localScale = Vector3.one;
        }
        EnsureComponent<CanvasGroup>(slot.gameObject);

        Image icon = EnsureImage(slot, "Icon", out created);
        if (created)
        {
            icon.rectTransform.anchorMin = Vector2.zero;
            icon.rectTransform.anchorMax = Vector2.one;
            icon.rectTransform.offsetMin = icon.rectTransform.offsetMax = Vector2.zero;
            icon.rectTransform.localScale = Vector3.one;
            Sprite sprite = LoadSprite(spritePath);
            icon.sprite = sprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false; // informational — never blocks gameplay input
            icon.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        }

        RectTransform nameRect = EnsureRect(slot, "IntroNameText", out bool createdName);
        TextMeshProUGUI nameText = EnsureComponent<TextMeshProUGUI>(nameRect.gameObject);
        if (createdName)
        {
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(1f, .5f);
            nameRect.pivot = new Vector2(0f, .5f);
            nameRect.anchoredPosition = new Vector2(12f, 0f);
            nameRect.sizeDelta = new Vector2(520f, 90f);
            nameRect.localScale = Vector3.one;
            nameText.text = previewName;
            nameText.fontSize = 42f;
            nameText.fontStyle = FontStyles.Bold;
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.textWrappingMode = TextWrappingModes.NoWrap;
            nameText.overflowMode = TextOverflowModes.Overflow;
            nameText.raycastTarget = false;
        }
        EnsureComponent<CanvasGroup>(nameRect.gameObject);
        return icon;
    }

    private static void WireSlot(SerializedObject serialized, string field, Image icon)
    {
        SerializedProperty slot = serialized.FindProperty(field);
        slot.FindPropertyRelative("root").objectReferenceValue =
            icon != null ? icon.transform.parent.gameObject : null;
        slot.FindPropertyRelative("icon").objectReferenceValue = icon;
        slot.FindPropertyRelative("introNameText").objectReferenceValue =
            icon != null ? icon.transform.parent.Find("IntroNameText")?.GetComponent<TextMeshProUGUI>() : null;
    }

    private static RectTransform FindRect(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (RectTransform rect in root.GetComponentsInChildren<RectTransform>(true))
                if (rect.gameObject.name == name)
                    return rect;
        return null;
    }

    private static RectTransform EnsureRect(Transform parent, string name, out bool created)
    {
        Transform child = parent.Find(name);
        created = child == null;
        if (created)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, "Active Effects HUD");
            go.transform.SetParent(parent, false);
            child = go.transform;
        }
        return (RectTransform)child;
    }

    private static Image EnsureImage(Transform parent, string name, out bool created)
    {
        RectTransform rect = EnsureRect(parent, name, out created);
        return EnsureComponent<Image>(rect.gameObject);
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T value = go.GetComponent<T>();
        return value != null ? value : Undo.AddComponent<T>(go);
    }

    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite subSprite) return subSprite;
        return null;
    }
}
#endif
