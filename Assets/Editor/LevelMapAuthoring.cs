#if UNITY_EDITOR
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LevelMapAuthoring
{
    private const string ScenePath = "Assets/Scenes/LevelMap.unity";
    private static readonly Vector2[] Positions =
    {
        new(-205, -590), new(165, -420), new(-155, -235), new(190, -35),
        new(-180, 180), new(145, 390), new(-80, 610)
    };
    private static readonly Vector2[] Sizes =
    {
        new(230,145), new(270,170), new(225,140), new(315,195),
        new(235,148), new(275,172), new(330,205)
    };

    [MenuItem("Tools/Planet Boom/Author Level Map")]
    public static void Author()
    {
        AssetDatabase.Refresh();
        LevelConfigurationCatalog catalog = AssetDatabase.LoadAssetAtPath<LevelConfigurationCatalog>(
            "Assets/Progression/Sector01/Sector01_LevelCatalog.asset");
        if (catalog == null) throw new System.InvalidOperationException("Sector 1 Level Catalog is missing.");

        LevelMapRewardIconLibrary icons = CreateRewardLibrary();
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
        {
            Scene existingScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            UpgradeExistingScene(existingScene, catalog, icons);
            ValidateDefaultSelection(catalog);
            if (!EditorSceneManager.SaveScene(existingScene)) throw new System.InvalidOperationException("LevelMap scene save failed.");
            AssetDatabase.SaveAssets();
            Debug.Log("Level Map upgraded in place: PathNodes drive the orbit; existing editable objects were preserved.");
            return;
        }

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        CreateCamera();
        CreateEventSystem();
        GameObject canvasObject = CreateUI("Canvas", null);
        Canvas canvas = canvasObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
        canvasObject.AddComponent<GraphicRaycaster>();

        GameObject screenObject = CreateUI("LevelMapScreen", canvasObject.transform);
        Stretch(screenObject.GetComponent<RectTransform>());
        LevelMapScreen screen = screenObject.AddComponent<LevelMapScreen>();

        GameObject viewportObject = CreateUI("BackgroundViewport", screenObject.transform);
        Stretch(viewportObject.GetComponent<RectTransform>());
        viewportObject.AddComponent<RectMask2D>();
        Image background = CreateImage("SectorBackground", viewportObject.transform, null, Color.white);
        background.preserveAspect = true;
        SetRect(background.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(1080,1920));
        GameObject pathObject = CreateUI("OrbitPath", screenObject.transform); Stretch(pathObject.GetComponent<RectTransform>());
        LevelMapOrbitPath path = pathObject.AddComponent<LevelMapOrbitPath>(); path.raycastTarget = false;
        GameObject nodeRoot = CreateUI("LevelNodes", screenObject.transform); Stretch(nodeRoot.GetComponent<RectTransform>());

        Sprite lockSprite = LoadSprite("Assets/UI Elements/PadLock.png");
        Sprite filledStar = LoadSprite("Assets/Stars/Yellow-Star.png");
        Sprite emptyStar = LoadSprite("Assets/Stars/Gray-Star.png");
        var nodes = new List<LevelMapNodeUI>();
        var points = new List<RectTransform>();
        for (int i = 0; i < 7; i++)
        {
            LevelMapNodeUI node = CreateNode(nodeRoot.transform, i, lockSprite, filledStar, emptyStar);
            RectTransform rect = (RectTransform)node.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = Positions[i]; rect.sizeDelta = new Vector2(560, Sizes[i].y + 150);
            rect.localRotation = Quaternion.identity;
            nodes.Add(node); points.Add((RectTransform)rect.Find("PathNode"));
        }

        Image header = CreateImage("SectorHeader", screenObject.transform, null, new Color(.04f,.06f,.12f,.72f));
        SetRect(header.rectTransform, new Vector2(.5f,1), new Vector2(0,-65), new Vector2(700,130));
        TMP_Text title = CreateText("SectorTitle", header.transform, "SECTOR 1", 54, TextAlignmentOptions.Center);
        Stretch(title.rectTransform);
        header.gameObject.SetActive(false);
        TMP_Text selection = CreateText("SelectedLevelText", screenObject.transform, "Bir seviye seç", 32, TextAlignmentOptions.Center);
        SetRect(selection.rectTransform, new Vector2(.5f,0), new Vector2(0,155), new Vector2(520,70));
        Button previous = CreateTextButton("PreviousSectorButton", screenObject.transform, "‹", new Vector2(.08f,.5f), new Vector2(120,180));
        Button next = CreateTextButton("NextSectorButton", screenObject.transform, "›", new Vector2(.92f,.5f), new Vector2(120,180));
        Button play = CreateTextButton("TemporaryPlayButton", screenObject.transform, "PLAY", new Vector2(.5f,0), new Vector2(360,105));
        play.GetComponent<RectTransform>().anchoredPosition = new Vector2(0,55); play.gameObject.SetActive(false);

        SetObject(screen, "levelCatalog", catalog); SetObject(screen, "rewardIcons", icons);
        SetObject(screen, "sectorBackground", background); SetObject(screen, "sectorTitle", title);
        SetObject(screen, "selectionText", selection); SetObject(screen, "previousSectorButton", previous);
        SetObject(screen, "nextSectorButton", next); SetObject(screen, "playButton", play); SetObject(screen, "orbitPath", path);
        SetList(screen, "nodes", nodes);
        EnsureBottomNavigation(screen);
        SerializedObject pathSerialized = new(path); SetReferenceList(pathSerialized.FindProperty("controlPoints"), points); pathSerialized.ApplyModifiedPropertiesWithoutUndo();
        AssignSectors(screen);
        EnsureSectorLayouts(screen);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new System.InvalidOperationException("LevelMap scene save failed.");
        AddSceneToBuild();
        PointMainMenuAtMap();
        AssetDatabase.SaveAssets();
        Debug.Log("Level Map authored: persistent 7-node hierarchy, sectors 1-9 art, safe empty sector 10.");
    }

    [MenuItem("Tools/Planet Boom/Level Map/Recover Sector 1 From Scene + Validate")]
    public static void RecoverSectorOneFromScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        LevelMapScreen screen = null;
        foreach (GameObject root in scene.GetRootGameObjects())
            if (screen == null) screen = root.GetComponentInChildren<LevelMapScreen>(true);
        if (screen == null) throw new System.InvalidOperationException("LevelMapScreen is missing.");
        LevelMapSectorVisual sector = screen.EditorFindSector(1);
        if (sector?.layout == null) throw new System.InvalidOperationException("Sector 1 layout asset is not assigned.");
        if (!LevelMapLayoutEditorUtility.RecoverAndValidateSectorOne(screen, sector.layout, out string report))
            throw new System.InvalidOperationException("Sector 1 round-trip failed: " + report);
        Debug.Log("Sector 1 recovered from the current Edit Mode scene. " + report, sector.layout);
    }

    private static LevelMapNodeUI CreateNode(Transform parent, int index, Sprite lockSprite, Sprite filled, Sprite empty)
    {
        GameObject root = CreateUI($"LevelNode{index + 1:00}", parent); root.AddComponent<CanvasGroup>();
        Image circle = CreateImage("PathNode", root.transform,
            LoadSprite("Assets/Buttons/SkillSlot.png") ?? AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"), Color.white);
        circle.preserveAspect = true;
        circle.raycastTarget = true; SetRect(circle.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(92,92));
        Button button = root.AddComponent<Button>(); button.targetGraphic = circle;
        TMP_Text number = CreateText("LevelNumber", circle.transform, (index + 1).ToString(), 42, TextAlignmentOptions.Center);
        Stretch(number.rectTransform);
        GameObject lockOverlay = CreateUI("LockOverlay", circle.transform); Stretch(lockOverlay.GetComponent<RectTransform>());
        Image lockImage = lockOverlay.AddComponent<Image>(); lockImage.sprite = lockSprite; lockImage.preserveAspect = true; lockImage.color = Color.white;
        Image dim = CreateImage("LockedDim", lockOverlay.transform, null, new Color(0,0,0,.36f)); Stretch(dim.rectTransform); dim.transform.SetAsFirstSibling();
        Image island = CreateImage("IslandImage", root.transform, null, Color.white); island.preserveAspect = true; island.raycastTarget = true;
        Button islandButton = island.gameObject.AddComponent<Button>(); islandButton.targetGraphic = island;
        SetRect(island.rectTransform, new Vector2(.5f,.5f), new Vector2(index % 2 == 0 ? -205 : 205, 0), Sizes[index]);
        GameObject starsRoot = CreateUI("Stars", root.transform);
        SetRect(starsRoot.GetComponent<RectTransform>(), new Vector2(.5f,.5f), new Vector2(index % 2 == 0 ? -205 : 205, Sizes[index].y * .5f + 28), new Vector2(150,42));
        HorizontalLayoutGroup layout = starsRoot.AddComponent<HorizontalLayoutGroup>(); layout.spacing = 5; layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = false;
        var stars = new List<Image>();
        for (int s = 0; s < 3; s++) { Image star = CreateImage($"Star{s+1}", starsRoot.transform, empty, Color.white); star.rectTransform.sizeDelta = new Vector2(42,42); star.preserveAspect = true; stars.Add(star); }
        Sprite unclaimedFrame = LoadSprite("Assets/UI Elements/EmptySquareFrame.png");
        Sprite claimedFrame = LoadSprite("Assets/UI Elements/EmptySquareFrameWithCheck.png");
        Image reward = CreateImage("RewardBadge", root.transform, unclaimedFrame, Color.white);
        reward.preserveAspect = true; reward.raycastTarget = false;
        SetRect(reward.rectTransform, new Vector2(.5f,.5f), new Vector2(index % 2 == 0 ? -205 : 205, -Sizes[index].y * .5f - 38), new Vector2(90,90));
        Image rewardIcon = CreateImage("RewardIcon", reward.transform, null, Color.white);
        rewardIcon.preserveAspect = true; rewardIcon.raycastTarget = false;
        SetRect(rewardIcon.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(58,58));
        TMP_Text check = CreateText("ClaimedCheck", reward.transform, "✓", 28, TextAlignmentOptions.BottomRight); Stretch(check.rectTransform);

        LevelMapNodeUI node = root.AddComponent<LevelMapNodeUI>(); SerializedObject serialized = new(node);
        serialized.FindProperty("button").objectReferenceValue = button; serialized.FindProperty("islandImage").objectReferenceValue = island;
        serialized.FindProperty("islandButton").objectReferenceValue = islandButton;
        serialized.FindProperty("levelText").objectReferenceValue = number; serialized.FindProperty("lockOverlay").objectReferenceValue = lockOverlay;
        serialized.FindProperty("filledStar").objectReferenceValue = filled; serialized.FindProperty("emptyStar").objectReferenceValue = empty;
        serialized.FindProperty("rewardContainer").objectReferenceValue = reward.rectTransform;
        serialized.FindProperty("rewardFrame").objectReferenceValue = reward;
        serialized.FindProperty("rewardIcon").objectReferenceValue = rewardIcon;
        serialized.FindProperty("rewardUnclaimedFrame").objectReferenceValue = unclaimedFrame;
        serialized.FindProperty("rewardClaimedFrame").objectReferenceValue = claimedFrame;
        serialized.FindProperty("rewardCompleteMark").objectReferenceValue = null;
        serialized.FindProperty("canvasGroup").objectReferenceValue = root.GetComponent<CanvasGroup>();
        SetReferenceList(serialized.FindProperty("stars"), stars); serialized.ApplyModifiedPropertiesWithoutUndo();
        EnsureSelectedHighlight(root.transform, circle);
        return node;
    }

    private static void UpgradeExistingScene(Scene scene, LevelConfigurationCatalog catalog,
        LevelMapRewardIconLibrary icons)
    {
        LevelMapScreen screen = null;
        LevelMapOrbitPath path = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (screen == null) screen = root.GetComponentInChildren<LevelMapScreen>(true);
            if (path == null) path = root.GetComponentInChildren<LevelMapOrbitPath>(true);
        }
        if (screen == null || path == null) throw new System.InvalidOperationException("Existing LevelMap scene is incomplete.");

        EnsureBackgroundViewport(screen);
        EnsureBottomNavigation(screen);

        Transform nodeRoot = screen.transform.Find("LevelNodes");
        if (nodeRoot == null) throw new System.InvalidOperationException("LevelNodes root is missing.");
        Sprite circleSprite = LoadSprite("Assets/Buttons/SkillSlot.png") ??
            AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var pathPoints = new List<RectTransform>();
        for (int i = 0; i < 7; i++)
        {
            Transform node = nodeRoot.Find($"LevelNode{i + 1:00}");
            if (node == null) throw new System.InvalidOperationException($"LevelNode{i + 1:00} is missing.");
            RectTransform nodeRect = (RectTransform)node;
            Vector2 islandSize = nodeRect.sizeDelta;

            Transform pathNode = node.Find("PathNode");
            bool migratingLegacyNode = pathNode == null;
            Image circle;
            if (migratingLegacyNode)
            {
                circle = CreateImage("PathNode", node, circleSprite, new Color(.08f,.12f,.2f,.95f));
                pathNode = circle.transform;
            }
            else
            {
                circle = pathNode.GetComponent<Image>() ?? pathNode.gameObject.AddComponent<Image>();
                circle.sprite = circleSprite; circle.color = Color.white; circle.preserveAspect = true;
            }
            circle.raycastTarget = true;
            EnsureSelectedHighlight(node, circle);
            pathPoints.Add((RectTransform)pathNode);
            if (migratingLegacyNode)
            {
                nodeRect.sizeDelta = new Vector2(560, Mathf.Max(240, islandSize.y + 150));
                nodeRect.localRotation = Quaternion.identity;
                SetRect((RectTransform)pathNode, new Vector2(.5f,.5f), Vector2.zero, new Vector2(92,92));
                pathNode.SetAsFirstSibling();
                Transform number = node.Find("LevelNumber");
                if (number != null) { number.SetParent(pathNode, false); Stretch((RectTransform)number); }
                Transform lockOverlay = node.Find("LockOverlay");
                if (lockOverlay != null) { lockOverlay.SetParent(pathNode, false); Stretch((RectTransform)lockOverlay); lockOverlay.SetAsLastSibling(); }
                Transform island = node.Find("IslandImage");
                float side = i % 2 == 0 ? -1f : 1f;
                if (island != null)
                {
                    RectTransform islandRect = (RectTransform)island;
                    SetRect(islandRect, new Vector2(.5f,.5f), new Vector2(side * 205f, 0), islandSize);
                    islandRect.localScale = Vector3.one;
                }
                Transform stars = node.Find("Stars");
                if (stars != null) SetRect((RectTransform)stars, new Vector2(.5f,.5f),
                    new Vector2(side * 205f, islandSize.y * .5f + 28f), new Vector2(150,42));
                Transform reward = node.Find("RewardBadge");
                if (reward != null) SetRect((RectTransform)reward, new Vector2(.5f,.5f),
                    new Vector2(side * 205f, -islandSize.y * .5f - 38f), new Vector2(90,90));
            }

            Button button = node.GetComponent<Button>();
            if (button != null) button.targetGraphic = circle;
            Transform numberTransform = pathNode.Find("LevelNumber");
            if (numberTransform != null && numberTransform.TryGetComponent(out TMP_Text numberTextComponent))
                numberTextComponent.color = Color.white;
            Transform islandTransform = node.Find("IslandImage");
            if (islandTransform != null)
            {
                Image islandImage = islandTransform.GetComponent<Image>();
                if (islandImage != null) islandImage.raycastTarget = true;
                Button islandButton = islandTransform.GetComponent<Button>() ?? islandTransform.gameObject.AddComponent<Button>();
                islandButton.targetGraphic = islandImage;
                LevelMapNodeUI nodeUI = node.GetComponent<LevelMapNodeUI>();
                if (nodeUI != null)
                {
                    SerializedObject nodeSerialized = new(nodeUI);
                    nodeSerialized.FindProperty("islandButton").objectReferenceValue = islandButton;
                    nodeSerialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            EnsureRewardBadgeVisuals(node);
            EditorUtility.SetDirty(node.gameObject);
        }

        SerializedObject pathSerialized = new(path);
        SetReferenceList(pathSerialized.FindProperty("controlPoints"), pathPoints);
        pathSerialized.FindProperty("m_RaycastTarget").boolValue = false;
        pathSerialized.ApplyModifiedPropertiesWithoutUndo();

        Transform header = screen.transform.Find("SectorHeader");
        if (header != null) header.gameObject.SetActive(false);
        SerializedObject screenSerialized = new(screen);
        screenSerialized.FindProperty("levelCatalog").objectReferenceValue = catalog;
        screenSerialized.FindProperty("rewardIcons").objectReferenceValue = icons;
        screenSerialized.ApplyModifiedPropertiesWithoutUndo();
        EnsureSectorLayouts(screen);
        EditorUtility.SetDirty(screen);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    private static void EnsureRewardBadgeVisuals(Transform node)
    {
        Transform rewardTransform = node.Find("RewardBadge");
        if (rewardTransform == null) return;

        Sprite unclaimedFrame = LoadSprite("Assets/UI Elements/EmptySquareFrame.png");
        Sprite claimedFrame = LoadSprite("Assets/UI Elements/EmptySquareFrameWithCheck.png");
        Image frame = rewardTransform.GetComponent<Image>() ?? rewardTransform.gameObject.AddComponent<Image>();
        frame.sprite = unclaimedFrame;
        frame.color = Color.white;
        frame.preserveAspect = true;
        frame.raycastTarget = false;

        RectTransform rewardRect = (RectTransform)rewardTransform;
        if (Vector2.SqrMagnitude(rewardRect.sizeDelta - new Vector2(62f, 62f)) < .001f)
            rewardRect.sizeDelta = new Vector2(90f, 90f);

        Transform iconTransform = rewardTransform.Find("RewardIcon");
        Image icon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
        if (icon != null)
        {
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            RectTransform iconRect = icon.rectTransform;
            bool legacyStretch = iconRect.anchorMin == Vector2.zero && iconRect.anchorMax == Vector2.one;
            if (legacyStretch)
                SetRect(iconRect, new Vector2(.5f, .5f), Vector2.zero, new Vector2(58f, 58f));
        }

        Transform claimedCheck = rewardTransform.Find("ClaimedCheck");
        if (claimedCheck != null) claimedCheck.gameObject.SetActive(false);

        LevelMapNodeUI nodeUI = node.GetComponent<LevelMapNodeUI>();
        if (nodeUI == null) return;
        SerializedObject serialized = new(nodeUI);
        serialized.FindProperty("rewardContainer").objectReferenceValue = rewardRect;
        serialized.FindProperty("rewardFrame").objectReferenceValue = frame;
        serialized.FindProperty("rewardIcon").objectReferenceValue = icon;
        serialized.FindProperty("rewardUnclaimedFrame").objectReferenceValue = unclaimedFrame;
        serialized.FindProperty("rewardClaimedFrame").objectReferenceValue = claimedFrame;
        serialized.FindProperty("rewardCompleteMark").objectReferenceValue = null;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureSelectedHighlight(Transform node, Image circle)
    {
        Sprite selectedSprite = LoadSprite("Assets/Buttons/SelectedSkillShot.png");
        Transform highlightTransform = circle.transform.Find("SelectedHighlight");
        Image highlight;
        if (highlightTransform == null)
        {
            highlight = CreateImage("SelectedHighlight", circle.transform,
                selectedSprite, new Color(1f, 1f, 1f, 0f));
            SetRect(highlight.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(126,126));
            highlight.transform.SetAsFirstSibling();
        }
        else highlight = highlightTransform.GetComponent<Image>() ?? highlightTransform.gameObject.AddComponent<Image>();
        if (selectedSprite != null) highlight.sprite = selectedSprite;
        highlight.preserveAspect = true;
        highlight.raycastTarget = false;
        highlight.gameObject.SetActive(false);

        LevelMapNodeUI nodeUI = node.GetComponent<LevelMapNodeUI>();
        if (nodeUI != null)
        {
            SerializedObject serialized = new(nodeUI);
            serialized.FindProperty("selectedHighlight").objectReferenceValue = highlight;
            serialized.FindProperty("selectedHighlightSprite").objectReferenceValue = selectedSprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(nodeUI);
        }
    }

    private static void EnsureBottomNavigation(LevelMapScreen screen)
    {
        Transform bottom = screen.transform.Find("BottomNavigation");
        if (bottom == null)
        {
            bottom = CreateUI("BottomNavigation", screen.transform).transform;
            Stretch((RectTransform)bottom);
        }
        if (bottom.GetComponent<SafeAreaFitter>() == null) bottom.gameObject.AddComponent<SafeAreaFitter>();

        SerializedObject screenSerialized = new(screen);
        Button previous = screenSerialized.FindProperty("previousSectorButton").objectReferenceValue as Button;
        Button next = screenSerialized.FindProperty("nextSectorButton").objectReferenceValue as Button;
        TMP_Text selection = screenSerialized.FindProperty("selectionText").objectReferenceValue as TMP_Text;
        if (previous == null || next == null)
            throw new System.InvalidOperationException("Bottom navigation references are incomplete.");

        previous.transform.SetParent(bottom, false);
        next.transform.SetParent(bottom, false);
        SetRect(previous.GetComponent<RectTransform>(), new Vector2(0f,0f), new Vector2(105,105), new Vector2(150,120));
        SetRect(next.GetComponent<RectTransform>(), new Vector2(1f,0f), new Vector2(-105,105), new Vector2(150,120));

        Transform center = bottom.Find("StartButton") ?? bottom.Find("CenterStatus");
        Image frame;
        if (center == null)
        {
            frame = CreateImage("StartButton", bottom, LoadSprite("Assets/UI Elements/Green-Buttonpng.png"), Color.white);
            center = frame.transform;
            SetRect(frame.rectTransform, new Vector2(.5f,0f), new Vector2(0,105), new Vector2(560,120));
        }
        else frame = center.GetComponent<Image>() ?? center.gameObject.AddComponent<Image>();
        center.name = "StartButton";
        Sprite startSprite = LoadSprite("Assets/UI Elements/Green-Buttonpng.png");
        if (startSprite != null) frame.sprite = startSprite;
        frame.color = Color.white; frame.preserveAspect = true; frame.raycastTarget = true;
        Button startButton = center.GetComponent<Button>() ?? center.gameObject.AddComponent<Button>();
        startButton.targetGraphic = frame;

        TMP_Text startText = center.Find("StartText")?.GetComponent<TMP_Text>();
        if (startText == null)
        {
            startText = selection != null ? selection : CreateText("StartText", center, "START", 42, TextAlignmentOptions.Center);
            startText.name = "StartText";
        }
        startText.transform.SetParent(center, false);
        startText.text = "START"; startText.color = Color.white;
        StretchInset(startText.rectTransform, 18f);

        Button oldPlay = screenSerialized.FindProperty("playButton").objectReferenceValue as Button;
        if (oldPlay != null && oldPlay != startButton) Object.DestroyImmediate(oldPlay.gameObject);

        Image previousImage = ConfigureRootNavigationButton(previous,
            screenSerialized.FindProperty("backButtonSprite").objectReferenceValue as Sprite);
        Image nextImage = ConfigureRootNavigationButton(next,
            screenSerialized.FindProperty("nextButtonSprite").objectReferenceValue as Sprite);
        screenSerialized.FindProperty("backButtonImage").objectReferenceValue = previousImage;
        screenSerialized.FindProperty("nextButtonImage").objectReferenceValue = nextImage;
        screenSerialized.FindProperty("centerStatusBackground").objectReferenceValue = frame;
        screenSerialized.FindProperty("selectionText").objectReferenceValue = null;
        screenSerialized.FindProperty("playButton").objectReferenceValue = startButton;
        screenSerialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(screen);
    }

    private static Image ConfigureRootNavigationButton(Button button, Sprite assignedSprite)
    {
        Image rootImage = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        Transform oldArrow = button.transform.Find("ArrowImage");
        if (assignedSprite == null && oldArrow != null && oldArrow.TryGetComponent(out Image oldArrowImage))
            assignedSprite = oldArrowImage.sprite;
        rootImage.sprite = assignedSprite;
        rootImage.preserveAspect = true;
        rootImage.raycastTarget = true;
        rootImage.color = assignedSprite != null ? Color.white : new Color(1f,1f,1f,0f);
        button.targetGraphic = rootImage;
        if (oldArrow != null) Object.DestroyImmediate(oldArrow.gameObject);
        Transform oldLabel = button.transform.Find("Label");
        if (oldLabel != null) Object.DestroyImmediate(oldLabel.gameObject);
        for (int i = button.transform.childCount - 1; i >= 0; i--)
        {
            Transform child = button.transform.GetChild(i);
            if (child.name == "ArrowImage" || child.name == "Label") Object.DestroyImmediate(child.gameObject);
        }
        return rootImage;
    }

    private static void EnsureSectorLayouts(LevelMapScreen screen)
    {
        const string folder = "Assets/Progression/MapLayouts";
        EnsureFolder(folder);
        SerializedObject serialized = new(screen);
        SerializedProperty sectors = serialized.FindProperty("sectors");
        for (int i = 0; i < Mathf.Min(9, sectors.arraySize); i++)
        {
            int sectorNumber = i + 1;
            string path = $"{folder}/Sector{sectorNumber:00}_MapLayout.asset";
            SectorMapLayout layout = AssetDatabase.LoadAssetAtPath<SectorMapLayout>(path);
            if (layout == null)
            {
                layout = ScriptableObject.CreateInstance<SectorMapLayout>();
                layout.sectorNumber = sectorNumber;
                AssetDatabase.CreateAsset(layout, path);
            }
            SerializedProperty layoutProperty = sectors.GetArrayElementAtIndex(i).FindPropertyRelative("layout");
            layoutProperty.objectReferenceValue = layout;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            if (!layout.HasCompleteNodeLayout)
            {
                if (sectorNumber == 1)
                    Debug.LogWarning("Sector 1 layout is incomplete. It is intentionally NOT auto-seeded; restore the scene and use 'Recapture Sector 1 From Current Scene'.", layout);
                else
                {
                    LevelMapLayoutEditorUtility.Capture(screen, layout);
                    layout.sectorNumber = sectorNumber;
                    EditorUtility.SetDirty(layout);
                }
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ValidateDefaultSelection(LevelConfigurationCatalog catalog)
    {
        LevelConfiguration fresh = LevelMapDefaultSelection.Find(catalog, 1, 7, level => level == 1, _ => 0);
        LevelConfiguration partial = LevelMapDefaultSelection.Find(catalog, 1, 7, level => level <= 4,
            level => level <= 3 ? 1 : 0);
        LevelConfiguration complete = LevelMapDefaultSelection.Find(catalog, 1, 7, level => level <= 7, _ => 1);
        LevelConfiguration unfinished = LevelMapDefaultSelection.Find(catalog, 8, 7, _ => true, _ => 0);
        if (fresh?.levelNumber != 1 || partial?.levelNumber != 4 || complete?.levelNumber != 7 || unfinished != null)
            throw new System.InvalidOperationException("Level Map automatic-selection validation failed.");
        Debug.Log("Level Map automatic-selection validation passed: fresh=L1, partial=L4, complete=L7, unfinished sector=none.");
    }

    private static void EnsureBackgroundViewport(LevelMapScreen screen)
    {
        SerializedObject screenSerialized = new(screen);
        Image background = screenSerialized.FindProperty("sectorBackground").objectReferenceValue as Image;
        if (background == null) throw new System.InvalidOperationException("SectorBackground reference is missing.");

        AspectRatioFitter fitter = background.GetComponent<AspectRatioFitter>();
        if (fitter != null) Object.DestroyImmediate(fitter);

        Transform viewport = screen.transform.Find("BackgroundViewport");
        bool migrating = viewport == null;
        if (migrating)
        {
            GameObject viewportObject = CreateUI("BackgroundViewport", screen.transform);
            viewport = viewportObject.transform;
            Stretch((RectTransform)viewport);
            viewportObject.AddComponent<RectMask2D>();
            viewport.SetAsFirstSibling();
            background.transform.SetParent(viewport, false);
        }
        else
        {
            if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            Stretch((RectTransform)viewport);
            viewport.SetAsFirstSibling();
            if (background.transform.parent != viewport) background.transform.SetParent(viewport, true);
        }

        bool stillUsingStretchAnchors = background.rectTransform.anchorMin != background.rectTransform.anchorMax;
        if (migrating || stillUsingStretchAnchors)
        {
            background.rectTransform.anchorMin = background.rectTransform.anchorMax = new Vector2(.5f,.5f);
            background.rectTransform.pivot = new Vector2(.5f,.5f);
            background.rectTransform.anchoredPosition = Vector2.zero;
            background.rectTransform.localScale = Vector3.one;
            if (background.sprite == null) background.sprite = LoadSprite("Assets/Sectors/Sector1.png");
            if (background.sprite != null) background.SetNativeSize();
        }
        background.preserveAspect = true;
        background.raycastTarget = false;
        EditorUtility.SetDirty(background);
        EditorUtility.SetDirty(viewport.gameObject);
    }

    private static void AssignSectors(LevelMapScreen screen)
    {
        SerializedObject serialized = new(screen); SerializedProperty list = serialized.FindProperty("sectors"); list.arraySize = 10;
        for (int i = 0; i < 10; i++)
        {
            SerializedProperty item = list.GetArrayElementAtIndex(i); item.FindPropertyRelative("sectorNumber").intValue = i + 1;
            item.FindPropertyRelative("title").stringValue = i == 9 ? "SECTOR 10 — YAKINDA" : $"SECTOR {i + 1}";
            if (i < 9)
            {
                item.FindPropertyRelative("background").objectReferenceValue = LoadSprite($"Assets/Sectors/Sector{i+1}.png");
                item.FindPropertyRelative("island").objectReferenceValue = LoadSprite($"Assets/Sectors/Sector{i+1}-Land.png");
            }
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static LevelMapRewardIconLibrary CreateRewardLibrary()
    {
        const string path = "Assets/Progression/LevelMapRewardIcons.asset";
        LevelMapRewardIconLibrary library = AssetDatabase.LoadAssetAtPath<LevelMapRewardIconLibrary>(path);
        if (library == null) { library = ScriptableObject.CreateInstance<LevelMapRewardIconLibrary>(); AssetDatabase.CreateAsset(library, path); }
        SerializedObject serialized = new(library); SerializedProperty entries = serialized.FindProperty("entries"); entries.arraySize = 2;
        SetReward(entries.GetArrayElementAtIndex(0), UnlockContentType.Planet, "Tier5", LoadSprite("Assets/Planet Icons/Planet5.png"));
        SetReward(entries.GetArrayElementAtIndex(1), UnlockContentType.Background, "background_sector_02", LoadSprite("Assets/Sectors/Sector2.png"));
        serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(library); return library;
    }

    private static void SetReward(SerializedProperty item, UnlockContentType type, string id, Sprite sprite)
    { item.FindPropertyRelative("contentType").enumValueIndex = (int)type; item.FindPropertyRelative("stableContentId").stringValue = id; item.FindPropertyRelative("icon").objectReferenceValue = sprite; }
    private static Sprite LoadSprite(string path) => AssetDatabase.LoadAssetAtPath<Sprite>(path);
    private static GameObject CreateUI(string name, Transform parent) { GameObject go = new(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go; }
    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color) { GameObject go = CreateUI(name, parent); Image image = go.AddComponent<Image>(); image.sprite = sprite; image.color = color; image.raycastTarget = false; return image; }
    private static TMP_Text CreateText(string name, Transform parent, string value, float size, TextAlignmentOptions alignment) { GameObject go = CreateUI(name, parent); TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size; text.alignment = alignment; text.color = Color.white; text.raycastTarget = false; return text; }
    private static Button CreateTextButton(string name, Transform parent, string label, Vector2 anchor, Vector2 size) { Image image = CreateImage(name, parent, null, new Color(.08f,.12f,.2f,.88f)); image.raycastTarget = true; SetRect(image.rectTransform, anchor, Vector2.zero, size); Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; TMP_Text text = CreateText("Label", image.transform, label, 52, TextAlignmentOptions.Center); Stretch(text.rectTransform); return button; }
    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size) { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f,.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static void Stretch(RectTransform rect) { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    private static void StretchInset(RectTransform rect, float inset) { Stretch(rect); rect.offsetMin = Vector2.one * inset; rect.offsetMax = Vector2.one * -inset; }
    private static void SetObject(Object target, string property, Object value) { SerializedObject serialized = new(target); serialized.FindProperty(property).objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetList<T>(Object target, string property, List<T> values) where T : Object { SerializedObject serialized = new(target); SetReferenceList(serialized.FindProperty(property), values); serialized.ApplyModifiedPropertiesWithoutUndo(); }
    private static void SetReferenceList<T>(SerializedProperty property, List<T> values) where T : Object { property.arraySize = values.Count; for (int i = 0; i < values.Count; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i]; }
    private static void CreateCamera() { GameObject go = new("Main Camera"); Camera camera = go.AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black; camera.orthographic = true; go.tag = "MainCamera"; }
    private static void CreateEventSystem() { GameObject go = new("EventSystem"); go.AddComponent<EventSystem>(); go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(); }
    private static void AddSceneToBuild() { var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes); if (!scenes.Exists(item => item.path == ScenePath)) scenes.Insert(1, new EditorBuildSettingsScene(ScenePath, true)); EditorBuildSettings.scenes = scenes.ToArray(); }
    private static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; int slash = path.LastIndexOf('/'); string parent = path.Substring(0, slash); if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent); AssetDatabase.CreateFolder(parent, path.Substring(slash + 1)); }
    private static void PointMainMenuAtMap() { Scene menu = EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity", OpenSceneMode.Additive); MainMenuController controller = Object.FindFirstObjectByType<MainMenuController>(); SerializedObject serialized = new(controller); serialized.FindProperty("gameplaySceneName").stringValue = "LevelMap"; serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(controller); EditorSceneManager.SaveScene(menu); EditorSceneManager.CloseScene(menu, true); }
}
#endif
