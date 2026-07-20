#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameplayHudAuthoring
{
    private const string OfferKey = "PlanetBoom.GameplayHudMigrationOffered.v1";
    private const string TopHudSyncOfferKey = "PlanetBoom.TopHudTimerSyncOffered.v1";
    private const string MissionValueLayoutOfferKey = "PlanetBoom.MissionValueLayoutOffered.v2";
    private const string MissionVisualOfferKey = "PlanetBoom.MissionObjectiveVisualsOffered.v3";

    private static void OfferMigrationWhenScriptsReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().name != "GameScene" ||
                SessionState.GetBool(OfferKey, false)) return;
            SessionState.SetBool(OfferKey, true);
            if (EditorUtility.DisplayDialog("Apply new Gameplay HUD?",
                    "This updates the currently open GameScene hierarchy in-place, preserves existing gameplay references, " +
                    "and does not save the scene automatically.", "Apply HUD", "Later"))
                Apply();
        };
    }

    private static void OfferTopHudSyncWhenScriptsReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().name != "GameScene" ||
                SessionState.GetBool(TopHudSyncOfferKey, false)) return;
            SessionState.SetBool(TopHudSyncOfferKey, true);
            if (Find(Object.FindAnyObjectByType<Canvas>()?.transform, "TopHUD") != null &&
                EditorUtility.DisplayDialog("Match Coin and Lives to Timer HUD?",
                    "TimerHUD will be used as the exact RectTransform and TMP presentation template. " +
                    "Coin/Lives data bindings remain unchanged and the scene will not be saved automatically.",
                    "Sync HUDs", "Later"))
                SyncCoinAndLivesToTimer();
        };
    }

    private static void OfferMissionValueLayoutWhenScriptsReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().name != "GameScene" ||
                SessionState.GetBool(MissionValueLayoutOfferKey, false)) return;
            SessionState.SetBool(MissionValueLayoutOfferKey, true);
            if (Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include) != null &&
                EditorUtility.DisplayDialog("Apply mission type/value text layout?",
                    "Mission headers will use 48 pt type text. The lower value text will use a 250 px area and 48 pt font. The scene is not saved automatically.",
                    "Apply Layout", "Later"))
                ApplyMissionTypeValueLayout();
        };
    }

    private static void OfferMissionVisualsWhenScriptsReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                SceneManager.GetActiveScene().name != "GameScene" ||
                SessionState.GetBool(MissionVisualOfferKey, false)) return;
            SessionState.SetBool(MissionVisualOfferKey, true);
            if (Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include) != null &&
                EditorUtility.DisplayDialog("Apply Reach + Combo mission visuals?",
                    "Reach cards will use the target planet sprite. Combo cards and in-game combo feedback will share the X artwork; missing artwork safely falls back to text.",
                    "Apply Visuals", "Later"))
                ApplyMissionObjectiveVisuals();
        };
    }

    [MenuItem("Tools/Planet Boom/Gameplay/Apply New Gameplay HUD")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
        {
            EditorUtility.DisplayDialog("GameScene required",
                "Open GameScene and save any work you want to keep, then run this command again.", "OK");
            return;
        }

        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        Transform safe = Find(canvas != null ? canvas.transform : null, "SafeAreaRoot");
        if (canvas == null || safe == null) throw new System.InvalidOperationException("Canvas/SafeAreaRoot is missing.");

        Sprite hamburger = Load("Assets/Buttons/Hamburger.png");
        Sprite coinSprite = Load("Assets/UI Elements/Coin-HUD.png");
        Sprite timeSprite = Load("Assets/UI Elements/Time-HUD.png");
        Sprite healthSprite = Load("Assets/UI Elements/Health-HUD.png");
        Sprite settings = Load("Assets/Buttons/Settings.png");
        Sprite mission = Load("Assets/UI Elements/Mission-HUD.png");
        var missing = new System.Collections.Generic.List<string>();
        if (hamburger == null) missing.Add("Assets/Buttons/Hamburger.png");
        if (coinSprite == null) missing.Add("Assets/UI Elements/Coin-HUD.png");
        if (timeSprite == null) missing.Add("Assets/UI Elements/Time-HUD.png");
        if (healthSprite == null) missing.Add("Assets/UI Elements/Health-HUD.png");
        if (settings == null) missing.Add("Assets/Buttons/Settings.png");
        if (mission == null) missing.Add("Assets/UI Elements/Mission-HUD.png");
        if (missing.Count > 0)
            throw new System.InvalidOperationException("Gameplay HUD sprite import could not be resolved: " +
                string.Join(", ", missing));

        RectTransform top = EnsureRect(safe, "TopHUD");
        SetRect(top, new Vector2(.5f, 1f), new Vector2(0f, -78f), new Vector2(1080f, 150f), new Vector2(.5f, 1f));
        HorizontalLayoutGroup topLayout = top.GetComponent<HorizontalLayoutGroup>() ?? top.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureHorizontal(topLayout, 18f);

        Image hamburgerImage = EnsureImage(top, "HamburgerButton", hamburger);
        Button hamburgerButton = hamburgerImage.GetComponent<Button>() ?? hamburgerImage.gameObject.AddComponent<Button>();
        hamburgerButton.targetGraphic = hamburgerImage;
        hamburgerButton.onClick.RemoveAllListeners();
        ConfigureArtwork(hamburgerImage, hamburger);
        PrepareLayoutItem(hamburgerImage.rectTransform, 116f, 116f);

        CoinHUD coinHud = Object.FindAnyObjectByType<CoinHUD>(FindObjectsInactive.Include);
        if (coinHud == null) throw new System.InvalidOperationException("Existing CoinHUD was not found.");
        RectTransform coinRect = (RectTransform)coinHud.transform;
        coinRect.SetParent(top, false); coinHud.gameObject.name = "CoinHUD";
        Image coinBackground = coinHud.GetComponent<Image>() ?? coinHud.gameObject.AddComponent<Image>();
        ConfigureArtwork(coinBackground, coinSprite);
        PrepareLayoutItem(coinRect, 235f, 116f);
        SerializedObject coinSerialized = new SerializedObject(coinHud);
        TMP_Text coinText = coinSerialized.FindProperty("countText").objectReferenceValue as TMP_Text;
        if (coinText != null) coinText.gameObject.name = "CoinText";
        coinSerialized.FindProperty("coinIconTarget").objectReferenceValue = coinRect;
        coinSerialized.FindProperty("pulseTarget").objectReferenceValue = coinRect;
        coinSerialized.ApplyModifiedPropertiesWithoutUndo();
        DisableRedundantImages(coinHud.transform, coinText != null ? coinText.transform : null);

        RectTransform timerHud = Find(safe, "TimerHUD") as RectTransform;
        if (timerHud == null) throw new System.InvalidOperationException("Existing TimerHUD was not found.");
        timerHud.SetParent(top, false); PrepareLayoutItem(timerHud, 235f, 116f);
        Image timerFrame = EnsureImage(timerHud, "TimerFrame", timeSprite);
        Stretch(timerFrame.rectTransform); timerFrame.transform.SetAsFirstSibling(); ConfigureArtwork(timerFrame, timeSprite);
        TMP_Text timerText = Find(timerHud, "Gameplay Timer Text")?.GetComponent<TMP_Text>() ??
                             Find(timerHud, "TimerText")?.GetComponent<TMP_Text>();
        if (timerText == null) throw new System.InvalidOperationException("Existing gameplay timer text was not found.");
        timerText.gameObject.name = "TimerText";

        RectTransform lives = EnsureRect(top, "LivesHUD");
        PrepareLayoutItem(lives, 235f, 116f);
        Image livesBackground = lives.GetComponent<Image>() ?? lives.gameObject.AddComponent<Image>();
        ConfigureArtwork(livesBackground, healthSprite);
        TMP_Text livesText = EnsureText(lives, "LivesText", timerText);
        SetRect(livesText.rectTransform, new Vector2(.5f, .5f), new Vector2(35f, 0f), new Vector2(120f, 76f));
        LivesHUD livesHud = lives.GetComponent<LivesHUD>() ?? lives.gameObject.AddComponent<LivesHUD>();
        SerializedObject livesSerialized = new SerializedObject(livesHud);
        livesSerialized.FindProperty("livesText").objectReferenceValue = livesText;
        livesSerialized.ApplyModifiedPropertiesWithoutUndo();

        Transform pauseTransform = Find(safe, "PauseButton") ?? Find(safe, "SettingsButton");
        if (pauseTransform == null) throw new System.InvalidOperationException("Existing PauseButton was not found.");
        pauseTransform.gameObject.name = "SettingsButton";
        pauseTransform.SetParent(top, false);
        Image pauseImage = pauseTransform.GetComponent<Image>() ?? pauseTransform.gameObject.AddComponent<Image>();
        ConfigureArtwork(pauseImage, settings);
        Button pauseButton = pauseTransform.GetComponent<Button>() ?? pauseTransform.gameObject.AddComponent<Button>();
        pauseButton.targetGraphic = pauseImage;
        PrepareLayoutItem((RectTransform)pauseTransform, 116f, 116f);

        SkillFlightManager flights = Object.FindAnyObjectByType<SkillFlightManager>(FindObjectsInactive.Include);
        if (flights != null)
        {
            SerializedObject flightSerialized = new SerializedObject(flights);
            flightSerialized.FindProperty("coinHudTarget").objectReferenceValue = coinRect;
            flightSerialized.FindProperty("timerTargetOverride").objectReferenceValue = timerText.rectTransform;
            flightSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        MissionHUD missionHud = Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include);
        if (missionHud == null) throw new System.InvalidOperationException("Existing MissionHUD was not found.");
        RectTransform group = (RectTransform)missionHud.transform;
        group.gameObject.name = "MissionHUDGroup";
        group.SetParent(safe, false);
        SetRect(group, new Vector2(.5f, 1f), new Vector2(0f, -250f), new Vector2(1080f, 220f), new Vector2(.5f, 1f));
        Image oldPanel = group.GetComponent<Image>(); if (oldPanel != null) oldPanel.enabled = false;
        DisableLegacyMissionChildren(group);
        HorizontalLayoutGroup missionLayout = group.GetComponent<HorizontalLayoutGroup>() ?? group.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureHorizontal(missionLayout, 18f);

        SerializedObject missionSerialized = new SerializedObject(missionHud);
        SerializedProperty cards = missionSerialized.FindProperty("missionCards"); cards.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            RectTransform card = EnsureRect(group, $"MissionCard{i + 1}");
            PrepareLayoutItem(card, 320f, 190f);
            Image bg = EnsureImage(card, "Background", mission); Stretch(bg.rectTransform); bg.transform.SetAsFirstSibling(); ConfigureArtwork(bg, mission);
            TMP_Text title = EnsureText(card, "MissionTitle", timerText);
            SetRect(title.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -28f), new Vector2(280f, 42f), new Vector2(.5f, .5f));
            TMP_Text objective = EnsureText(card, "MissionObjective", timerText);
            SetRect(objective.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -18f), new Vector2(292f, 78f));
            TMP_Text secondary = EnsureText(card, "ObjectiveSecondary", timerText);
            SetRect(secondary.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 25f), new Vector2(292f, 38f), new Vector2(.5f, 0f));
            TMP_Text completed = EnsureText(card, "Completed", timerText); completed.text = "\u2713";
            SetRect(completed.rectTransform, new Vector2(1f, 1f), new Vector2(-24f, -24f), new Vector2(42f, 42f)); completed.gameObject.SetActive(false);
            SerializedProperty item = cards.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("root").objectReferenceValue = card;
            item.FindPropertyRelative("background").objectReferenceValue = bg;
            item.FindPropertyRelative("missionTitle").objectReferenceValue = title;
            item.FindPropertyRelative("missionObjective").objectReferenceValue = objective;
            item.FindPropertyRelative("objectiveSecondary").objectReferenceValue = secondary;
            item.FindPropertyRelative("completed").objectReferenceValue = completed;
        }
        missionSerialized.FindProperty("missionCardSprite").objectReferenceValue = mission;
        missionSerialized.ApplyModifiedPropertiesWithoutUndo();

        SyncCoinAndLivesToTimerInternal(safe, coinSprite, healthSprite);

        EditorUtility.SetDirty(coinHud); EditorUtility.SetDirty(missionHud); EditorUtility.SetDirty(livesHud);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("New Gameplay HUD applied in-place. Review the current scene and save it when satisfied; no automatic scene save was performed.");
    }

    [MenuItem("Tools/Planet Boom/Gameplay/Sync Coin + Lives To Timer HUD")]
    public static void SyncCoinAndLivesToTimer()
    {
        Scene scene = SceneManager.GetActiveScene();
        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        Transform safe = Find(canvas != null ? canvas.transform : null, "SafeAreaRoot");
        if (!scene.IsValid() || scene.name != "GameScene" || safe == null)
            throw new System.InvalidOperationException("Open GameScene before syncing the Top HUD.");
        Sprite coin = Load("Assets/UI Elements/Coin-HUD.png");
        Sprite health = Load("Assets/UI Elements/Health-HUD.png");
        if (coin == null || health == null)
            throw new System.InvalidOperationException("Coin-HUD or Health-HUD sprite could not be loaded.");
        SyncCoinAndLivesToTimerInternal(safe, coin, health);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("CoinHUD and LivesHUD now match the authored TimerHUD structure, text styling and RectTransforms.");
    }

    [MenuItem("Tools/Planet Boom/Gameplay/Apply Mission Type + Value Layout")]
    public static void ApplyMissionTypeValueLayout()
    {
        Scene scene = SceneManager.GetActiveScene();
        MissionHUD hud = Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include);
        if (!scene.IsValid() || scene.name != "GameScene" || hud == null)
            throw new System.InvalidOperationException("Open GameScene before applying the Mission Card text layout.");

        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("missionTitleFontSize").floatValue = 48f;
        serialized.FindProperty("missionObjectiveFontSize").floatValue = 48f;
        SerializedProperty cards = serialized.FindProperty("missionCards");
        for (int i = 0; i < cards.arraySize; i++)
        {
            SerializedProperty card = cards.GetArrayElementAtIndex(i);
            TMP_Text title = card.FindPropertyRelative("missionTitle").objectReferenceValue as TMP_Text;
            TMP_Text objective = card.FindPropertyRelative("missionObjective").objectReferenceValue as TMP_Text;
            if (title != null)
            {
                title.rectTransform.sizeDelta = new Vector2(250f, 64f);
                title.fontSize = 48f;
                title.enableAutoSizing = false;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Overflow;
                EditorUtility.SetDirty(title);
            }
            if (objective == null) continue;
            RectTransform rect = objective.rectTransform;
            rect.sizeDelta = new Vector2(250f, 90f);
            objective.fontSize = 48f;
            objective.enableAutoSizing = false;
            objective.textWrappingMode = TextWrappingModes.NoWrap;
            objective.overflowMode = TextOverflowModes.Overflow;
            EditorUtility.SetDirty(objective);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Mission Cards updated: 48 pt type header and large value in the 250 px lower area.");
    }

    [MenuItem("Tools/Planet Boom/Gameplay/Apply Reach + Combo Mission Visuals")]
    public static void ApplyMissionObjectiveVisuals()
    {
        Scene scene = SceneManager.GetActiveScene();
        MissionHUD hud = Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include);
        if (!scene.IsValid() || scene.name != "GameScene" || hud == null)
            throw new System.InvalidOperationException("Open GameScene before applying Mission visuals.");

        GameObject planetPrefabObject = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Planet.prefab");
        Planet planet = planetPrefabObject != null ? planetPrefabObject.GetComponent<Planet>() : null;
        SerializedObject serialized = new SerializedObject(hud);
        serialized.FindProperty("planetPrefab").objectReferenceValue = planet;
        serialized.FindProperty("missionVisualSize").vector2Value = new Vector2(190f, 130f);
        serialized.FindProperty("missionTitleFontSize").floatValue = 48f;
        serialized.FindProperty("missionObjectiveFontSize").floatValue = 48f;
        SerializedProperty combo = serialized.FindProperty("comboTargetSprites");
        combo.arraySize = 5;
        for (int i = 0; i < 5; i++)
            combo.GetArrayElementAtIndex(i).objectReferenceValue =
                LoadOptional($"Assets/UI Elements/X{i + 1}.png");

        SerializedProperty cards = serialized.FindProperty("missionCards");
        for (int i = 0; i < cards.arraySize; i++)
        {
            SerializedProperty card = cards.GetArrayElementAtIndex(i);
            RectTransform root = card.FindPropertyRelative("root").objectReferenceValue as RectTransform;
            if (root == null) continue;
            Image visual = EnsureImage(root, "MissionVisual", null);
            SetRect(visual.rectTransform, new Vector2(.5f, .5f), new Vector2(0f, -14f), new Vector2(190f, 130f));
            visual.preserveAspect = true; visual.raycastTarget = false; visual.gameObject.SetActive(false);
            card.FindPropertyRelative("objectiveVisual").objectReferenceValue = visual;
            EditorUtility.SetDirty(visual);
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        ComboTextSpawner comboSpawner = Object.FindAnyObjectByType<ComboTextSpawner>(FindObjectsInactive.Include);
        if (comboSpawner != null)
        {
            SerializedObject comboSerialized = new SerializedObject(comboSpawner);
            SerializedProperty popupSprites = comboSerialized.FindProperty("comboSprites");
            popupSprites.arraySize = 5;
            for (int i = 0; i < 5; i++)
                popupSprites.GetArrayElementAtIndex(i).objectReferenceValue =
                    LoadOptional($"Assets/UI Elements/X{i + 1}.png");
            comboSerialized.FindProperty("comboSpriteScale").floatValue = .18f;
            comboSerialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(comboSpawner);
        }
        EditorUtility.SetDirty(hud);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Mission + gameplay combo visuals applied: Reach uses planet tier art; Combo uses X artwork without Best text.");
    }

    private static void SyncCoinAndLivesToTimerInternal(Transform safe, Sprite coinSprite, Sprite healthSprite)
    {
        RectTransform timer = Find(safe, "TimerHUD") as RectTransform;
        RectTransform coin = Find(safe, "CoinHUD") as RectTransform;
        RectTransform lives = Find(safe, "LivesHUD") as RectTransform;
        TMP_Text timerText = Find(timer, "TimerText")?.GetComponent<TMP_Text>();
        TMP_Text coinText = Find(coin, "CoinText")?.GetComponent<TMP_Text>();
        TMP_Text livesText = Find(lives, "LivesText")?.GetComponent<TMP_Text>();
        Image timerFrame = Find(timer, "TimerFrame")?.GetComponent<Image>();
        if (timer == null || coin == null || lives == null || timerText == null || coinText == null || livesText == null || timerFrame == null)
            throw new System.InvalidOperationException("TimerHUD, CoinHUD or LivesHUD hierarchy is incomplete.");

        CopyRect(timer, coin);
        CopyRect(timer, lives);
        CopyTextPresentation(timerText, coinText);
        CopyTextPresentation(timerText, livesText);

        Image coinFrame = EnsureImage(coin, "CoinFrame", coinSprite);
        Image livesFrame = EnsureImage(lives, "LivesFrame", healthSprite);
        CopyRect(timerFrame.rectTransform, coinFrame.rectTransform);
        CopyRect(timerFrame.rectTransform, livesFrame.rectTransform);
        ConfigureArtwork(coinFrame, coinSprite);
        ConfigureArtwork(livesFrame, healthSprite);
        coinFrame.raycastTarget = false; livesFrame.raycastTarget = false;
        coinFrame.transform.SetAsFirstSibling(); livesFrame.transform.SetAsFirstSibling();

        Image oldCoinRoot = coin.GetComponent<Image>(); if (oldCoinRoot != null) oldCoinRoot.enabled = false;
        Image oldLivesRoot = lives.GetComponent<Image>(); if (oldLivesRoot != null) oldLivesRoot.enabled = false;
        EditorUtility.SetDirty(coinText); EditorUtility.SetDirty(livesText);
        EditorUtility.SetDirty(coinFrame); EditorUtility.SetDirty(livesFrame);
    }

    private static void CopyRect(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin; target.anchorMax = source.anchorMax;
        target.pivot = source.pivot; target.anchoredPosition = source.anchoredPosition;
        target.sizeDelta = source.sizeDelta; target.localRotation = source.localRotation;
        target.localScale = source.localScale;
        LayoutElement sourceLayout = source.GetComponent<LayoutElement>();
        LayoutElement targetLayout = target.GetComponent<LayoutElement>();
        if (sourceLayout != null && targetLayout != null)
        {
            targetLayout.preferredWidth = sourceLayout.preferredWidth;
            targetLayout.preferredHeight = sourceLayout.preferredHeight;
            targetLayout.minWidth = sourceLayout.minWidth; targetLayout.minHeight = sourceLayout.minHeight;
        }
    }

    private static void CopyTextPresentation(TMP_Text source, TMP_Text target)
    {
        CopyRect(source.rectTransform, target.rectTransform);
        target.font = source.font; target.fontSharedMaterial = source.fontSharedMaterial;
        target.fontSize = source.fontSize; target.fontStyle = source.fontStyle;
        target.color = source.color; target.alignment = source.alignment;
        target.enableAutoSizing = source.enableAutoSizing;
        target.fontSizeMin = source.fontSizeMin; target.fontSizeMax = source.fontSizeMax;
        target.textWrappingMode = source.textWrappingMode; target.overflowMode = source.overflowMode;
        target.characterSpacing = source.characterSpacing; target.wordSpacing = source.wordSpacing;
        target.lineSpacing = source.lineSpacing; target.margin = source.margin;
        target.raycastTarget = false;
    }

    private static void DisableLegacyMissionChildren(Transform group)
    {
        for (int i = 0; i < group.childCount; i++)
        {
            Transform child = group.GetChild(i);
            if (!child.name.StartsWith("MissionCard", System.StringComparison.Ordinal))
                child.gameObject.SetActive(false);
        }
    }
    private static void DisableRedundantImages(Transform root, Transform keepText)
    {
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
            if (image.transform != root && image.transform != keepText) image.gameObject.SetActive(false);
    }
    private static void ConfigureHorizontal(HorizontalLayoutGroup layout, float spacing)
    { layout.spacing=spacing; layout.childAlignment=TextAnchor.MiddleCenter; layout.childControlWidth=false; layout.childControlHeight=false; layout.childForceExpandWidth=false; layout.childForceExpandHeight=false; layout.padding=new RectOffset(); }
    private static void PrepareLayoutItem(RectTransform rect,float width,float height)
    { rect.localScale=Vector3.one; rect.sizeDelta=new Vector2(width,height); LayoutElement e=rect.GetComponent<LayoutElement>()??rect.gameObject.AddComponent<LayoutElement>(); e.preferredWidth=width;e.preferredHeight=height; }
    private static void ConfigureArtwork(Image image,Sprite sprite)
    { image.sprite=sprite;image.color=Color.white;image.preserveAspect=true;image.raycastTarget=image.GetComponent<Button>()!=null; }
    private static TMP_Text EnsureText(Transform parent,string name,TMP_Text template)
    { Transform found=FindDirect(parent,name); TextMeshProUGUI text=found!=null?found.GetComponent<TextMeshProUGUI>():null; if(text==null){GameObject go=new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(parent,false);text=go.GetComponent<TextMeshProUGUI>();} if(template!=null){text.font=template.font;text.fontSharedMaterial=template.fontSharedMaterial;} text.fontSize=34;text.alignment=TextAlignmentOptions.Center;text.color=Color.white;text.enableAutoSizing=false;text.textWrappingMode=TextWrappingModes.NoWrap;text.raycastTarget=false;return text; }
    private static Image EnsureImage(Transform parent,string name,Sprite sprite)
    { Transform found=FindDirect(parent,name);GameObject go=found!=null?found.gameObject:new GameObject(name,typeof(RectTransform));if(found==null)go.transform.SetParent(parent,false);Image image=go.GetComponent<Image>()??go.AddComponent<Image>();image.sprite=sprite;return image; }
    private static RectTransform EnsureRect(Transform parent,string name)
    { Transform found=FindDirect(parent,name);if(found!=null)return (RectTransform)found;GameObject go=new GameObject(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform; }
    private static Transform Find(Transform root,string name){if(root==null)return null;if(root.name==name)return root;for(int i=0;i<root.childCount;i++){Transform found=Find(root.GetChild(i),name);if(found!=null)return found;}return null;}
    private static Transform FindDirect(Transform root,string name){if(root==null)return null;for(int i=0;i<root.childCount;i++)if(root.GetChild(i).name==name)return root.GetChild(i);return null;}
    private static Sprite Load(string path)
    {
        // Newly replaced PNG files can be newer than their existing .meta while
        // Auto Refresh is disabled. Force a synchronous import before resolving
        // either the main object or a Multiple-mode sprite representation.
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate |
                                        ImportAssetOptions.ForceSynchronousImport);
        Sprite direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (direct != null) return direct;
        Object[] assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        foreach (Object asset in assets)
            if (asset is Sprite sprite) return sprite;
        assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (Object asset in assets)
            if (asset is Sprite sprite) return sprite;

        // A replaced PNG may retain an obsolete Multiple-mode slice whose rect
        // lies outside the new texture (Coin-HUD previously had exactly that).
        // These HUD files are used as one complete piece of artwork, so Single
        // is the correct and stable import mode.
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();
            direct = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (direct != null) return direct;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Sprite sprite) return sprite;
        }
        return null;
    }
    private static Sprite LoadOptional(string path) => AssetDatabase.LoadMainAssetAtPath(path) != null
        ? Load(path) : null;
    private static void Stretch(RectTransform rect){rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;}
    private static void SetRect(RectTransform rect,Vector2 anchor,Vector2 pos,Vector2 size,Vector2? pivot=null){rect.anchorMin=rect.anchorMax=anchor;rect.pivot=pivot??new Vector2(.5f,.5f);rect.anchoredPosition=pos;rect.sizeDelta=size;rect.localScale=Vector3.one;}
}
#endif
