#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Additive migration for the existing Game Over root. Existing children are
// retained but disabled; repeated runs preserve every authored RectTransform.
public static class GameOverPanelAuthoring
{
    private const string MenuPath =
        "Tools/Planet Boom/Gameplay/Redesign Game Over Panel";

    [MenuItem(MenuPath)]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "Game Over authoring is unavailable in Play Mode. Exit Play " +
                "Mode, run the command, then save GameScene.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
            throw new System.InvalidOperationException(
                "Open GameScene before redesigning the Game Over panel.");

        GameManager manager =
            Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        Canvas canvas =
            Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (manager == null || canvas == null)
            throw new System.InvalidOperationException(
                "GameScene is missing GameManager or Canvas.");

        Transform existing = canvas.transform.Find("GameOverPanel") ??
            canvas.transform.Find("Game Over Panel");
        RectTransform container = existing as RectTransform ??
            EnsureRect(canvas.transform, "GameOverPanel", out _);
        container.name = "GameOverPanel";

        // The old root was itself a full-screen popup and can still carry an
        // Image. After migration it is only a hierarchy container; rendering
        // that legacy Image would leave a white overlay visible while the
        // actual PopupRoot is closed.
        Graphic legacyContainerGraphic = container.GetComponent<Graphic>();
        if (legacyContainerGraphic != null)
        {
            Undo.RecordObject(
                legacyContainerGraphic, "Disable Legacy Game Over Graphic");
            legacyContainerGraphic.raycastTarget = false;
            legacyContainerGraphic.enabled = false;
            EditorUtility.SetDirty(legacyContainerGraphic);
        }

        TMP_Text template = container.GetComponentInChildren<TMP_Text>(true);
        if (template == null)
            template = Object.FindAnyObjectByType<TextMeshProUGUI>(
                FindObjectsInactive.Include);

        RectTransform popupRoot = EnsureRect(
            container, "PopupRoot", out bool popupCreated);
        if (popupCreated) Stretch(popupRoot);
        EnsureComponent<CanvasGroup>(popupRoot.gameObject);
        EnsureComponent<PopupTransition>(popupRoot.gameObject);

        RectTransform overlay = EnsureRect(
            popupRoot, "Overlay", out bool overlayCreated);
        if (overlayCreated) Stretch(overlay);
        Image overlayImage = EnsureImage(overlay);
        overlay.gameObject.SetActive(true);
        overlayImage.enabled = true;
        if (overlayCreated)
            overlayImage.color = new Color(0f, 0f, 0f, .72f);
        overlayImage.raycastTarget = true;

        RectTransform panel = EnsureRect(
            popupRoot, "Panel", out bool panelCreated);
        InitializeOnce(panel, panelCreated, Vector2.zero,
            new Vector2(940f, 1220f));

        RectTransform backgroundRect = EnsureRect(
            panel, "Background", out bool backgroundCreated);
        if (backgroundCreated) Stretch(backgroundRect);
        Image background = EnsureImage(backgroundRect);
        background.sprite = LoadSprite(
            "Assets/UI Elements/GameOverPanelBackground.png");
        background.preserveAspect = true;
        background.raycastTarget = false;
        backgroundRect.SetAsFirstSibling();

        TextMeshProUGUI title = EnsureText(
            panel, "GameOverText", template, "GAME OVER", out bool titleCreated);
        InitializeOnce(title.rectTransform, titleCreated,
            new Vector2(0f, 430f), new Vector2(780f, 150f));
        InitializeTextOnce(title, titleCreated, 94f);

        TextMeshProUGUI keepProgress = EnsureText(
            panel, "KeepProgressText", template, "KEEP YOUR PROGRESS",
            out bool keepProgressCreated);
        InitializeOnce(keepProgress.rectTransform, keepProgressCreated,
            new Vector2(0f, 275f), new Vector2(780f, 100f));
        InitializeTextOnce(keepProgress, keepProgressCreated, 46f);

        Button healthButton = EnsureButton(
            panel, "ContinueWithHealthButton",
            LoadSprite("Assets/UI Elements/ContinueWithHealt.png"),
            new Vector2(-210f, 60f), new Vector2(360f, 210f));
        ConfigureButtonSound(healthButton.gameObject);

        Button adsButton = EnsureButton(
            panel, "ContinueWithAdsButton",
            LoadSprite("Assets/UI Elements/ContinueWithADS.png"),
            new Vector2(210f, 60f), new Vector2(360f, 210f));
        ConfigureButtonSound(adsButton.gameObject);

        TextMeshProUGUI orText = EnsureText(
            panel, "OrText", template, "OR", out bool orCreated);
        InitializeOnce(orText.rectTransform, orCreated,
            new Vector2(0f, -105f), new Vector2(300f, 90f));
        InitializeTextOnce(orText, orCreated, 48f);

        Button tryAgainButton = EnsureButton(
            panel, "TryAgainButton",
            LoadSprite("Assets/UI Elements/TryAgainButton.png"),
            new Vector2(0f, -300f), new Vector2(600f, 210f));
        TextMeshProUGUI tryAgainText = EnsureText(
            panel, "TryAgainText", template, "TRY AGAIN",
            out bool tryTextCreated);
        InitializeOnce(tryAgainText.rectTransform, tryTextCreated,
            new Vector2(0f, -300f), new Vector2(520f, 150f));
        InitializeTextOnce(tryAgainText, tryTextCreated, 54f);
        tryAgainText.transform.SetAsLastSibling();
        ConfigureButtonSound(tryAgainButton.gameObject);

        DisableObsoletePanelChildren(panel);

        GameOverPanel controller =
            EnsureComponent<GameOverPanel>(popupRoot.gameObject);
        SerializedObject serialized = new SerializedObject(controller);
        Set(serialized, "popupRoot", popupRoot.gameObject);
        Set(serialized, "gameOverText", title);
        Set(serialized, "keepProgressText", keepProgress);
        Set(serialized, "orText", orText);
        Set(serialized, "tryAgainText", tryAgainText);
        Set(serialized, "continueWithHealthButton", healthButton);
        Set(serialized, "continueWithAdsButton", adsButton);
        Set(serialized, "tryAgainButton", tryAgainButton);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject managerSerialized = new SerializedObject(manager);
        managerSerialized.FindProperty("gameOverPanel").objectReferenceValue =
            popupRoot.gameObject;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();

        // Preserve the old authored objects for recovery, but they no longer
        // participate in rendering or input after migration.
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Transform child = container.GetChild(i);
            if (child != popupRoot)
                child.gameObject.SetActive(false);
        }

        container.gameObject.SetActive(true);
        popupRoot.gameObject.SetActive(false);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = popupRoot.gameObject;
        Debug.Log(
            "Existing Game Over panel migrated under GameOverPanel/PopupRoot. " +
            "Adjust authored UI values as needed, then save GameScene manually.");
    }

    [MenuItem(MenuPath, true)]
    private static bool CanApply() =>
        !EditorApplication.isPlayingOrWillChangePlaymode;

    private static RectTransform EnsureRect(
        Transform parent, string name, out bool created)
    {
        Transform found = parent.Find(name);
        created = found == null;
        if (!created)
            return found as RectTransform;
        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Redesign Game Over Panel");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static Image EnsureImage(RectTransform rect)
    {
        Image image = rect.GetComponent<Image>();
        if (image == null)
            image = Undo.AddComponent<Image>(rect.gameObject);
        return image;
    }

    private static TextMeshProUGUI EnsureText(
        Transform parent, string name, TMP_Text template, string preview,
        out bool created)
    {
        RectTransform rect = EnsureRect(parent, name, out created);
        TextMeshProUGUI text = rect.GetComponent<TextMeshProUGUI>();
        if (text == null)
            text = Undo.AddComponent<TextMeshProUGUI>(rect.gameObject);
        if (created)
        {
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
                text.color = template.color;
            }
            text.text = preview;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
        }
        return text;
    }

    private static Button EnsureButton(
        Transform parent, string name, Sprite sprite,
        Vector2 position, Vector2 size)
    {
        RectTransform rect = EnsureRect(parent, name, out bool created);
        InitializeOnce(rect, created, position, size);
        Image image = EnsureImage(rect);
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = true;
        Button button = rect.GetComponent<Button>();
        if (button == null)
            button = Undo.AddComponent<Button>(rect.gameObject);
        button.targetGraphic = image;
        return button;
    }

    private static void InitializeTextOnce(
        TMP_Text text, bool created, float fontSize)
    {
        if (!created)
            return;
        text.fontSize = fontSize;
        text.enableAutoSizing = false;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    private static void InitializeOnce(RectTransform rect, bool created,
        Vector2 position, Vector2 size)
    {
        if (!created)
            return;
        rect.anchorMin = rect.anchorMax = rect.pivot =
            new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void DisableObsoletePanelChildren(Transform panel)
    {
        for (int i = 0; i < panel.childCount; i++)
        {
            GameObject child = panel.GetChild(i).gameObject;
            bool retained = child.name == "Background" ||
                child.name == "GameOverText" ||
                child.name == "KeepProgressText" ||
                child.name == "ContinueWithHealthButton" ||
                child.name == "ContinueWithAdsButton" ||
                child.name == "OrText" ||
                child.name == "TryAgainButton" ||
                child.name == "TryAgainText";
            if (child.activeSelf != retained)
            {
                Undo.RecordObject(child, "Migrate Game Over Layout");
                child.SetActive(retained);
            }
        }
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static T EnsureComponent<T>(GameObject target)
        where T : Component =>
        target.GetComponent<T>() ?? Undo.AddComponent<T>(target);

    private static Sprite LoadSprite(params string[] paths)
    {
        foreach (string path in paths)
        {
            Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null)
                return direct;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Sprite sprite)
                    return sprite;
        }
        throw new System.InvalidOperationException(
            "Required Game Over sprite was not found: " +
            string.Join(" or ", paths));
    }

    private static void ConfigureButtonSound(GameObject target)
    {
        UiButtonSound sound = EnsureComponent<UiButtonSound>(target);
        SerializedObject serialized = new SerializedObject(sound);
        serialized.FindProperty("soundType").enumValueIndex =
            (int)UiSoundType.Confirm;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void Set(
        SerializedObject serialized, string property, Object value) =>
        serialized.FindProperty(property).objectReferenceValue = value;
}
#endif
