#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Redesigns the existing Settings popup (BreakMenuPopup) in GameScene with
// the new UI art — a MIGRATION of the live BreakMenuUI hierarchy, never a
// second settings system:
//
//   BreakMenu (BreakMenuUI — same component, extended fields)
//   └── BreakMenuPopup           (existing popupRoot: dim overlay + CanvasGroup + PopupTransition)
//       └── Panel                (existing 760×1180 panel → EmptySettingsBackground)
//           ├── ToggleRow        (existing Sound/Music/Vibration toggles + new SOUND/MUSIC/HAPTIC labels)
//           ├── ActionButtons
//           │   ├── LanguageButton (new      — EmptyHUD + Language-Icon + "ENGLISH")
//           │   ├── SupportButton  (new      — EmptyHUD + Support-Icon + "SUPPORT")
//           │   ├── ExitButton     (migrated HomeButton — EmptyHUD + Exit-Icon + "EXIT")
//           │   └── RestartButton  (migrated — EmptyHUD + Restart-Icon + "RESTART")
//           ├── RemoveAdsButton  (new — EmptyYellowButton + ADS-Block-Icon + "REMOVE ADS" + "$2.99")
//           └── ContinueButton   (existing — reskinned to ReadyButtonBackground)
//
// One-time migration, then additive: the heavy restyle (reparenting the old
// Home/Restart buttons, swapping sprites, applying the new layout) runs only
// while the ActionButtons group does not exist yet. Every later run only
// re-creates deleted children at their defaults and refreshes BreakMenuUI's
// wiring — manual Inspector adjustments are never overwritten. The scene is
// marked dirty but never saved.
public static class SettingsPopupAuthoring
{
    private const string UndoLabel = "Redesign Settings Popup";
    private const string PanelSpritePath = "Assets/UI Elements/EmptySettingsBackground.png";
    private const string ActionSpritePath = "Assets/UI Elements/EmptyHUD.png";
    private const string RemoveAdsSpritePath = "Assets/Buttons/EmptyYellowButton.png";
    private const string ContinueSpritePath = "Assets/UI Elements/ReadyButtonBackground.png";
    private const string LanguageIconPath = "Assets/UI Elements/Language-Icon.png";
    private const string SupportIconPath = "Assets/UI Elements/Support-Icon.png";
    private const string ExitIconPath = "Assets/UI Elements/Exit-Icon.png";
    private const string RestartIconPath = "Assets/UI Elements/Restart-Icon.png";
    private const string AdsIconPath = "Assets/UI Elements/ADS-Block-Icon.png";

    // Dark navy for text sitting on the yellow Remove Ads background.
    private static readonly Color DarkText = new Color(0.10f, 0.14f, 0.32f, 1f);

    [MenuItem("Tools/Planet Boom/UI/Redesign Settings Popup")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
            throw new System.InvalidOperationException("Open GameScene before redesigning the Settings popup.");
        BreakMenuUI controller = Object.FindAnyObjectByType<BreakMenuUI>(FindObjectsInactive.Include);
        if (controller == null)
            throw new System.InvalidOperationException("BreakMenuUI not found in GameScene.");

        foreach (string path in new[]
        {
            PanelSpritePath, ActionSpritePath, RemoveAdsSpritePath, LanguageIconPath,
            SupportIconPath, ExitIconPath, RestartIconPath, AdsIconPath,
        })
            EnsureSpriteImport(path);

        Sprite panelSprite = LoadSprite(PanelSpritePath);
        Sprite actionSprite = LoadSprite(ActionSpritePath);
        Sprite removeAdsSprite = LoadSprite(RemoveAdsSpritePath);
        Sprite continueSprite = LoadSprite(ContinueSpritePath);
        Sprite languageIcon = LoadSprite(LanguageIconPath);
        Sprite supportIcon = LoadSprite(SupportIconPath);
        Sprite exitIcon = LoadSprite(ExitIconPath);
        Sprite restartIcon = LoadSprite(RestartIconPath);
        Sprite adsIcon = LoadSprite(AdsIconPath);

        SerializedObject serialized = new SerializedObject(controller);
        Button soundToggle = serialized.FindProperty("soundToggle").objectReferenceValue as Button;
        Button musicToggle = serialized.FindProperty("musicToggle").objectReferenceValue as Button;
        Button vibrationToggle = serialized.FindProperty("vibrationToggle").objectReferenceValue as Button;
        Button exitButton = serialized.FindProperty("exitButton").objectReferenceValue as Button;
        Button restartButton = serialized.FindProperty("restartButton").objectReferenceValue as Button;
        Button continueButton = serialized.FindProperty("continueButton").objectReferenceValue as Button;
        if (continueButton == null)
            throw new System.InvalidOperationException(
                "BreakMenuUI.continueButton is not wired — cannot locate the popup panel.");

        RectTransform panel = continueButton.transform.parent as RectTransform;
        if (panel == null)
            throw new System.InvalidOperationException("Settings popup panel not found above the Continue button.");
        TMP_Text template = continueButton.GetComponentInChildren<TMP_Text>(true);
        if (template == null)
            template = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);

        // The heavy one-time restyle runs only while the redesign has not
        // been applied yet; the ActionButtons group is its marker.
        bool firstRun = panel.Find("ActionButtons") == null;
        bool created;

        Image panelImage = panel.GetComponent<Image>();
        if (firstRun && panelImage != null && panelSprite != null)
        {
            panelImage.sprite = panelSprite;
            panelImage.type = Image.Type.Simple;
            panelImage.preserveAspect = false;
            panelImage.color = Color.white;
        }

        // TOP — existing toggles keep their objects, wiring and on/off
        // sprites; the row moves up once and each gets a label underneath.
        if (firstRun && soundToggle != null && soundToggle.transform.parent is RectTransform row && row != panel)
            row.anchoredPosition = new Vector2(0f, 440f);
        EnsureToggleLabel(soundToggle, "SOUND", template);
        EnsureToggleLabel(musicToggle, "MUSIC", template);
        EnsureToggleLabel(vibrationToggle, "HAPTIC", template);
        if (soundToggle != null) SetButtonSound(soundToggle.gameObject, UiSoundType.Toggle, false);
        if (musicToggle != null) SetButtonSound(musicToggle.gameObject, UiSoundType.Toggle, false);
        if (vibrationToggle != null) SetButtonSound(vibrationToggle.gameObject, UiSoundType.Toggle, false);

        // SECOND SECTION — the four blue action buttons. Language/Support are
        // new; Exit/Restart migrate the old Home/Restart buttons so their
        // Button components (and scene wiring) survive.
        RectTransform actionGroup = EnsureRect(panel, "ActionButtons", out created);
        if (created) SetRect(actionGroup, new Vector2(.5f, .5f), new Vector2(0f, 170f), new Vector2(700f, 300f));

        Button languageButton = EnsureActionButton(actionGroup, null, "LanguageButton", new Vector2(-175f, 75f),
            actionSprite, languageIcon, "ENGLISH", UiSoundType.Toggle, firstRun, template,
            out TextMeshProUGUI languageLabel);
        Button supportButton = EnsureActionButton(actionGroup, null, "SupportButton", new Vector2(175f, 75f),
            actionSprite, supportIcon, "SUPPORT", UiSoundType.Default, firstRun, template, out _);
        exitButton = EnsureActionButton(actionGroup, exitButton, "ExitButton", new Vector2(-175f, -75f),
            actionSprite, exitIcon, "EXIT", UiSoundType.Back, firstRun, template, out _);
        restartButton = EnsureActionButton(actionGroup, restartButton, "RestartButton", new Vector2(175f, -75f),
            actionSprite, restartIcon, "RESTART", UiSoundType.Confirm, firstRun, template, out _);

        // REMOVE ADS — presentation only; BreakMenuUI's click handler is a
        // safe log until real IAP integration.
        Image removeAdsImage = EnsureImage(panel, "RemoveAdsButton", out created);
        if (created)
        {
            SetRect(removeAdsImage.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -105f), new Vector2(620f, 170f));
            removeAdsImage.sprite = removeAdsSprite;
            removeAdsImage.type = Image.Type.Simple;
            removeAdsImage.preserveAspect = false;
            removeAdsImage.color = Color.white;
            removeAdsImage.raycastTarget = true;
        }
        Button removeAdsButton = EnsureComponent<Button>(removeAdsImage.gameObject);
        if (removeAdsButton.targetGraphic == null) removeAdsButton.targetGraphic = removeAdsImage;
        SetButtonSound(removeAdsImage.gameObject, UiSoundType.Confirm, created);
        Image adsIconImage = EnsureImage(removeAdsImage.transform, "Icon", out created);
        if (created)
        {
            SetRect(adsIconImage.rectTransform, new Vector2(.5f, .5f), new Vector2(-225f, 0f), new Vector2(96f, 96f));
            SetArtwork(adsIconImage, adsIcon);
        }
        TextMeshProUGUI removeAdsText = EnsureText(removeAdsImage.transform, "RemoveAdsText", template, "REMOVE ADS", 34f, out created);
        if (created)
        {
            SetRect(removeAdsText.rectTransform, new Vector2(.5f, .5f), new Vector2(45f, 28f), new Vector2(400f, 60f));
            removeAdsText.color = DarkText;
        }
        TextMeshProUGUI priceText = EnsureText(removeAdsImage.transform, "PriceText", template, "$2.99", 28f, out created);
        if (created)
        {
            SetRect(priceText.rectTransform, new Vector2(.5f, .5f), new Vector2(45f, -34f), new Vector2(400f, 52f));
            priceText.color = DarkText;
        }

        // BOTTOM — the existing Continue button, reskinned once to the same
        // green art the Pre-Level "I AM READY" button uses.
        if (firstRun)
        {
            RectTransform continueRect = (RectTransform)continueButton.transform;
            continueRect.anchoredPosition = new Vector2(0f, -330f);
            continueRect.SetAsLastSibling();
            Image continueImage = continueButton.GetComponent<Image>();
            if (continueImage != null && continueSprite != null)
            {
                continueImage.sprite = continueSprite;
                continueImage.type = Image.Type.Simple;
                continueImage.preserveAspect = false;
                continueImage.color = Color.white;
            }
            TMP_Text continueLabel = continueButton.GetComponentInChildren<TMP_Text>(true);
            if (continueLabel != null) continueLabel.text = "CONTINUE";
        }
        SetButtonSound(continueButton.gameObject, UiSoundType.Confirm, firstRun);

        // Wiring is refreshed on every run; toggles, pause button and on/off
        // sprites keep their existing references untouched.
        serialized.FindProperty("exitButton").objectReferenceValue = exitButton;
        serialized.FindProperty("restartButton").objectReferenceValue = restartButton;
        serialized.FindProperty("languageButton").objectReferenceValue = languageButton;
        serialized.FindProperty("languageLabel").objectReferenceValue = languageLabel;
        serialized.FindProperty("supportButton").objectReferenceValue = supportButton;
        serialized.FindProperty("removeAdsButton").objectReferenceValue = removeAdsButton;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = panel.gameObject;
        Debug.Log("Settings popup redesigned (" + (firstRun ? "migration applied" : "re-run, wiring refreshed") + "). " +
                  $"Panel={Name(panelSprite)}, Action={Name(actionSprite)}, RemoveAds={Name(removeAdsSprite)}, " +
                  $"Continue={Name(continueSprite)}. Save GameScene manually.");
    }

    // Existing = the old break-menu button being migrated into the new
    // design (reparent + restyle happen only on the migration run); when
    // null the button is created under the group by name.
    private static Button EnsureActionButton(RectTransform group, Button existing, string name,
        Vector2 position, Sprite background, Sprite icon, string label, UiSoundType sound,
        bool firstRun, TMP_Text template, out TextMeshProUGUI labelText)
    {
        bool created = false;
        GameObject go = existing != null ? existing.gameObject : EnsureRect(group, name, out created).gameObject;
        bool restyle = created || (existing != null && firstRun);

        if (existing != null && restyle)
        {
            Undo.SetTransformParent(go.transform, group, UndoLabel);
            go.name = name;
        }
        Image image = EnsureComponent<Image>(go);
        if (restyle)
        {
            SetRect((RectTransform)go.transform, new Vector2(.5f, .5f), position, new Vector2(330f, 130f));
            image.sprite = background;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;
            image.raycastTarget = true;
            image.enabled = true;
        }
        Button button = EnsureComponent<Button>(go);
        if (button.targetGraphic == null) button.targetGraphic = image;
        SetButtonSound(go, sound, restyle);

        Image iconImage = EnsureImage(go.transform, "Icon", out bool iconCreated);
        if (iconCreated)
        {
            SetRect(iconImage.rectTransform, new Vector2(.5f, .5f), new Vector2(-100f, 0f), new Vector2(72f, 72f));
            SetArtwork(iconImage, icon);
        }

        labelText = EnsureText(go.transform, "Label", template, label, 30f, out bool labelCreated);
        if (labelCreated || restyle)
        {
            SetRect(labelText.rectTransform, new Vector2(.5f, .5f), new Vector2(35f, 0f), new Vector2(210f, 64f));
            labelText.text = label;
            labelText.fontSize = 30f;
        }
        labelText.transform.SetAsLastSibling();
        return button;
    }

    private static void EnsureToggleLabel(Button toggle, string label, TMP_Text template)
    {
        if (toggle == null) return;
        TextMeshProUGUI text = EnsureText(toggle.transform, "Label", template, label, 26f, out bool created);
        if (created)
            SetRect(text.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, -30f), new Vector2(170f, 44f));
    }

    private static void EnsureSpriteImport(string path)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
            importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }
    }

    private static void SetButtonSound(GameObject go, UiSoundType type, bool force)
    {
        UiButtonSound sound = go.GetComponent<UiButtonSound>();
        bool added = sound == null;
        if (added) sound = Undo.AddComponent<UiButtonSound>(go);
        if (!added && !force) return;
        SerializedObject serialized = new SerializedObject(sound);
        serialized.FindProperty("soundType").enumValueIndex = (int)type;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static RectTransform EnsureRect(Transform parent, string name, out bool created)
    {
        Transform child = parent.Find(name);
        created = child == null;
        if (created)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(go, UndoLabel);
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

    private static TextMeshProUGUI EnsureText(Transform parent, string name, TMP_Text template, string value, float size, out bool created)
    {
        RectTransform rect = EnsureRect(parent, name, out created);
        TextMeshProUGUI text = EnsureComponent<TextMeshProUGUI>(rect.gameObject);
        if (created)
        {
            if (template != null) { text.font = template.font; text.fontSharedMaterial = template.fontSharedMaterial; }
            text.fontSize = size; text.enableAutoSizing = false; text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap; text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false; text.text = value;
        }
        return text;
    }

    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        T value = go.GetComponent<T>();
        return value != null ? value : Undo.AddComponent<T>(go);
    }

    private static void SetArtwork(Image image, Sprite sprite)
    {
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = sprite != null ? Color.white : new Color(1f, 1f, 1f, 0f);
        image.enabled = true;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; rect.localScale = Vector3.one; }

    private static Sprite LoadSprite(string path)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite != null) return sprite;
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
            if (asset is Sprite subSprite) return subSprite;
        return null;
    }

    private static string Name(Sprite sprite) => sprite != null ? sprite.name : "MISSING";
}
#endif
