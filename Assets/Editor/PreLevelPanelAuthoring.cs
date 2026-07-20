#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class PreLevelPanelAuthoring
{
    [MenuItem("Tools/Planet Boom/Gameplay/Apply Custom Pre-Level Panel Visuals")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        GameManager manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (!scene.IsValid() || scene.name != "GameScene" || canvas == null || manager == null)
            throw new System.InvalidOperationException("Open GameScene before applying the custom Pre-Level panel.");

        TMP_Text template = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        Sprite mainBackgroundSprite = FindSprite("PreLevel Background", "PreLevelBackground");
        Sprite levelHeaderSprite = FindSprite("emptybackground2");
        Sprite badgeSprite = LoadSprite("Assets/UI Elements/BadgeBackground.png") ??
            FindSprite("BadgeBackground", "RedGradientBadge", "QuantityBadge", "RedBadge");
        Sprite useSprite = FindSprite("Use", "UseButton");
        Sprite usedSprite = FindSprite("Used", "UsedButton");
        Sprite readySprite = FindSprite("IAmReady", "ReadyButton", "I Am Ready");
        Sprite closeSprite = LoadSprite("Assets/Buttons/Close.png");
        Sprite luckyIcon = LoadSprite("Assets/UI Elements/LuckyDrop.png");
        Sprite doubleIcon = LoadSprite("Assets/UI Elements/DoubleTimeDrop.png");
        Sprite starIcon = LoadSprite("Assets/UI Elements/StarBooster.png");

        RectTransform root = EnsureRect(canvas.transform, "PreLevelPanel");
        Stretch(root);
        PreLevelPanel controller = EnsureComponent<PreLevelPanel>(root.gameObject);

        Image overlay = EnsureImage(root, "Overlay");
        Stretch(overlay.rectTransform); overlay.sprite = null;
        overlay.color = new Color(0f, 0f, 0f, .72f); overlay.raycastTarget = true;

        RectTransform panel = EnsureRect(root, "Panel");
        SetRect(panel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(900f, 1350f));
        Image panelBlocker = EnsureComponent<Image>(panel.gameObject);
        panelBlocker.sprite = null; panelBlocker.color = new Color(1f, 1f, 1f, 0f); panelBlocker.raycastTarget = true;
        ClearChildren(panel);

        Image mainBackground = EnsureImage(panel, "MainBackground");
        Stretch(mainBackground.rectTransform); SetArtwork(mainBackground, mainBackgroundSprite);

        RectTransform levelHeader = EnsureRect(panel, "LevelHeader");
        SetRect(levelHeader, new Vector2(.5f, 1f), new Vector2(0f, -145f), new Vector2(520f, 125f));
        Image levelHeaderBackground = EnsureImage(levelHeader, "Background");
        Stretch(levelHeaderBackground.rectTransform); SetArtwork(levelHeaderBackground, levelHeaderSprite);
        TextMeshProUGUI levelText = EnsureText(levelHeader, "LevelText", template, "LEVEL 1", 62f);
        Stretch(levelText.rectTransform);

        Image closeImage = EnsureImage(panel, "CloseButton");
        SetRect(closeImage.rectTransform, new Vector2(1f, 1f), new Vector2(-55f, -55f), new Vector2(105f, 105f));
        SetArtwork(closeImage, closeSprite); closeImage.raycastTarget = true;
        Button closeButton = EnsureComponent<Button>(closeImage.gameObject); closeButton.targetGraphic = closeImage;

        RectTransform objectives = EnsureRect(panel, "ObjectivesArea");
        SetRect(objectives, new Vector2(.5f, 1f), new Vector2(0f, -370f), new Vector2(690f, 260f));
        VerticalLayoutGroup objectiveLayout = EnsureComponent<VerticalLayoutGroup>(objectives.gameObject);
        objectiveLayout.spacing = 8f; objectiveLayout.childAlignment = TextAnchor.MiddleCenter;
        objectiveLayout.childControlWidth = true; objectiveLayout.childControlHeight = true;
        objectiveLayout.childForceExpandWidth = true; objectiveLayout.childForceExpandHeight = true;

        GameObject[] objectiveRoots = new GameObject[3];
        TextMeshProUGUI[] objectiveTexts = new TextMeshProUGUI[3];
        for (int i = 0; i < 3; i++)
        {
            RectTransform row = EnsureRect(objectives, $"ObjectiveText{i + 1}");
            objectiveRoots[i] = row.gameObject;
            objectiveTexts[i] = EnsureComponent<TextMeshProUGUI>(row.gameObject);
            CopyTextStyle(objectiveTexts[i], template, 48f);
            objectiveTexts[i].text = "OBJECTIVE";
        }

        RectTransform boosters = EnsureRect(panel, "BoosterSection");
        SetRect(boosters, new Vector2(.5f, 0f), new Vector2(0f, 340f), new Vector2(780f, 390f));
        BoosterType[] types = { BoosterType.LuckyDrop, BoosterType.DoubleTimeDrop, BoosterType.StarBooster };
        string[] names = { "Lucky Drop", "2X Time Drop", "Star Booster" };
        Sprite[] icons = { luckyIcon, doubleIcon, starIcon };
        GameObject[] entryRoots = new GameObject[3]; Image[] iconImages = new Image[3];
        Image[] badgeBackgrounds = new Image[3]; TextMeshProUGUI[] nameTexts = new TextMeshProUGUI[3];
        TextMeshProUGUI[] quantityTexts = new TextMeshProUGUI[3]; Button[] useButtons = new Button[3];
        Image[] useGraphics = new Image[3]; TextMeshProUGUI[] useTexts = new TextMeshProUGUI[3];
        GameObject[] lockedIndicators = new GameObject[3];
        float[] xPositions = { -255f, 0f, 255f };
        for (int i = 0; i < 3; i++)
        {
            RectTransform entry = EnsureRect(boosters, types[i].ToString());
            SetRect(entry, new Vector2(.5f, .5f), new Vector2(xPositions[i], 0f), new Vector2(235f, 380f));
            entryRoots[i] = entry.gameObject;
            nameTexts[i] = EnsureText(entry, "Name", template, names[i], 29f);
            SetRect(nameTexts[i].rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -35f), new Vector2(230f, 60f));

            RectTransform iconContainer = EnsureRect(entry, "BoosterIconContainer");
            SetRect(iconContainer, new Vector2(.5f, .5f), new Vector2(0f, 15f), new Vector2(190f, 190f));
            iconImages[i] = EnsureImage(iconContainer, "BoosterIcon");
            Stretch(iconImages[i].rectTransform); SetArtwork(iconImages[i], icons[i]);

            RectTransform badge = EnsureRect(iconContainer, "QuantityBadge");
            SetRect(badge, new Vector2(1f, 0f), new Vector2(-12f, 12f), new Vector2(62f, 62f));
            badgeBackgrounds[i] = EnsureImage(badge, "Background");
            Stretch(badgeBackgrounds[i].rectTransform); SetArtwork(badgeBackgrounds[i], badgeSprite);
            quantityTexts[i] = EnsureText(badge, "QuantityText", template, "0", 34f);
            Stretch(quantityTexts[i].rectTransform);

            useGraphics[i] = EnsureImage(entry, "UseButton");
            SetRect(useGraphics[i].rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 45f), new Vector2(170f, 78f));
            SetArtwork(useGraphics[i], useSprite); useGraphics[i].raycastTarget = true;
            if (useSprite == null) { useGraphics[i].enabled = true; useGraphics[i].color = new Color(1f, 1f, 1f, 0f); }
            useButtons[i] = EnsureComponent<Button>(useGraphics[i].gameObject);
            useButtons[i].targetGraphic = useGraphics[i];
            useTexts[i] = EnsureText(useGraphics[i].transform, "UseText", template, "USE", 30f);
            Stretch(useTexts[i].rectTransform); useTexts[i].gameObject.SetActive(useSprite == null);

            TextMeshProUGUI locked = EnsureText(entry, "LockedIndicator", template, "LOCKED", 32f);
            SetRect(locked.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(210f, 80f));
            lockedIndicators[i] = locked.gameObject;
        }

        Image readyImage = EnsureImage(panel, "ReadyButton");
        SetRect(readyImage.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 65f), new Vector2(520f, 135f));
        SetArtwork(readyImage, readySprite); readyImage.raycastTarget = true;
        if (readySprite == null) { readyImage.enabled = true; readyImage.color = new Color(1f, 1f, 1f, 0f); }
        Button readyButton = EnsureComponent<Button>(readyImage.gameObject); readyButton.targetGraphic = readyImage;
        TextMeshProUGUI readyText = EnsureText(readyImage.transform, "ReadyText", template, "I AM READY", 50f);
        Stretch(readyText.rectTransform);

        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("levelTitle").objectReferenceValue = levelText;
        serialized.FindProperty("objectivesSection").objectReferenceValue = objectives;
        serialized.FindProperty("readyButton").objectReferenceValue = readyButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("useSprite").objectReferenceValue = useSprite;
        serialized.FindProperty("usedSprite").objectReferenceValue = usedSprite;
        SerializedProperty objectiveArray = serialized.FindProperty("objectivePreviews"); objectiveArray.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            SerializedProperty row = objectiveArray.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("root").objectReferenceValue = objectiveRoots[i];
            row.FindPropertyRelative("label").objectReferenceValue = objectiveTexts[i];
        }
        SerializedProperty boosterArray = serialized.FindProperty("boosterEntries"); boosterArray.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            SerializedProperty row = boosterArray.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("type").enumValueIndex = (int)types[i];
            row.FindPropertyRelative("root").objectReferenceValue = entryRoots[i];
            row.FindPropertyRelative("icon").objectReferenceValue = iconImages[i];
            row.FindPropertyRelative("quantityBadgeBackground").objectReferenceValue = badgeBackgrounds[i];
            row.FindPropertyRelative("nameText").objectReferenceValue = nameTexts[i];
            row.FindPropertyRelative("quantityText").objectReferenceValue = quantityTexts[i];
            row.FindPropertyRelative("selectButton").objectReferenceValue = useButtons[i];
            row.FindPropertyRelative("selectGraphic").objectReferenceValue = useGraphics[i];
            row.FindPropertyRelative("selectText").objectReferenceValue = useTexts[i];
            row.FindPropertyRelative("selectedIndicator").objectReferenceValue = null;
            row.FindPropertyRelative("lockedIndicator").objectReferenceValue = lockedIndicators[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject managerSerialized = new SerializedObject(manager);
        managerSerialized.FindProperty("preLevelPanel").objectReferenceValue = controller;
        managerSerialized.ApplyModifiedPropertiesWithoutUndo();

        SkillDropManager dropManager = Object.FindAnyObjectByType<SkillDropManager>(FindObjectsInactive.Include);
        if (dropManager != null)
        {
            SerializedObject drops = new SerializedObject(dropManager);
            drops.FindProperty("luckyDropIcon").objectReferenceValue = luckyIcon;
            drops.FindProperty("doubleTimeDropIcon").objectReferenceValue = doubleIcon;
            drops.FindProperty("starBoosterIcon").objectReferenceValue = starIcon;
            drops.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(dropManager);
        }
        root.gameObject.SetActive(false);
        EditorUtility.SetDirty(controller); EditorUtility.SetDirty(manager);
        EditorSceneManager.MarkSceneDirty(scene); Selection.activeGameObject = root.gameObject;

        Debug.Log($"Custom Pre-Level visuals applied. Main={Name(mainBackgroundSprite)}, Header={Name(levelHeaderSprite)}, " +
                  $"Badge={Name(badgeSprite)}, Use={Name(useSprite)}, Used={Name(usedSprite)}, Ready={Name(readySprite)}. Save GameScene manually.");
    }

    private static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
    }
    private static RectTransform EnsureRect(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null) { GameObject go = new GameObject(name, typeof(RectTransform)); Undo.RegisterCreatedObjectUndo(go, "Pre-Level UI"); go.transform.SetParent(parent, false); child = go.transform; }
        return (RectTransform)child;
    }
    private static Image EnsureImage(Transform parent, string name) => EnsureComponent<Image>(EnsureRect(parent, name).gameObject);
    private static TextMeshProUGUI EnsureText(Transform parent, string name, TMP_Text template, string value, float size)
    { TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(EnsureRect(parent, name).gameObject); CopyTextStyle(text, template, size); text.text = value; return text; }
    private static void CopyTextStyle(TextMeshProUGUI text, TMP_Text template, float size)
    {
        if (template != null) { text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; }
        text.fontSize = size; text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow; text.raycastTarget = false;
    }
    private static T EnsureComponent<T>(GameObject go) where T : Component
    { T value = go.GetComponent<T>(); return value != null ? value : Undo.AddComponent<T>(go); }
    private static void SetArtwork(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        image.enabled = true;
    }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one; }
    private static string Name(Sprite sprite) => sprite != null ? sprite.name : "MANUAL";
    private static Sprite LoadSprite(string path)
    { return AssetDatabase.LoadAssetAtPath<Sprite>(path) ?? FirstSpriteAtPath(path); }
    private static Sprite FindSprite(params string[] candidateNames)
    {
        string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string normalized = Normalize(Path.GetFileNameWithoutExtension(path));
            foreach (string candidate in candidateNames)
                if (normalized == Normalize(candidate)) return LoadSprite(path);
        }
        return null;
    }
    private static Sprite FirstSpriteAtPath(string path)
    { foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path)) if (asset is Sprite sprite) return sprite; return null; }
    private static string Normalize(string value)
    { return value.ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("-", ""); }
}
#endif
