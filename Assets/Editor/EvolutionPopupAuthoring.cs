#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds and wires the Evolution Popup in GameScene:
//
//   Canvas
//   └── EvolutionPopup            (always active, EvolutionPopupUI)
//       └── PopupRoot             (inactive, CanvasGroup + PopupTransition)
//           ├── Overlay
//           └── Panel             (EvolutionPopupBackground)
//               ├── Background
//               ├── TitleText     "EVOLUTION"
//               ├── CloseButton   (existing Close.png X art)
//               ├── EvolutionContent
//               │   ├── Connectors (Connector1_2 ... Connector7_8)
//               │   └── Nodes      (PlanetNode1 ... PlanetNode8 — pure containers)
//               │        ├── UpcomingGlow (flat gold halo BEHIND the art; UiGlowPulse, inactive)
//               │        ├── PlanetImage  (real planet art / silhouette tint target)
//               │        └── StatusText   (TMP "UPCOMING"/"NEW" label, inactive by default)
//               └── ContinueButton (ReadyButtonBackground) → ContinueText
//
// Strictly additive and safe to re-run: every element is created only if
// missing, and layout/sprites are applied ONLY at creation time — manual
// Inspector adjustments (positions, sizes, sprites, texts) are never
// overwritten on a later run. Deleting a connector and re-running rebuilds
// it from the nodes' CURRENT authored positions. Component wiring
// (EvolutionPopupUI references) is refreshed on every run. The scene is
// marked dirty but never saved.
public static class EvolutionPopupAuthoring
{
    private const string BackgroundPath = "Assets/UI Elements/EvolutionPopupBackground.png";
    private const string CloseSpritePath = "Assets/Buttons/Close.png";
    private const string ContinueSpritePath = "Assets/UI Elements/ReadyButtonBackground.png";
    private const string ConnectorSpritePath = "Assets/UI Elements/white-dot.png";
    private const string PlanetIconFolder = "Assets/Planet Icons";
    private const int NodeCount = 8;
    private const float NodeSize = 130f;

    // Serpentine (snake) path: Tier1→2→3 left-to-right, down, Tier4→5→6
    // right-to-left, down, Tier7→8 left-to-right. Applied only when a node
    // is first created.
    private static readonly Vector2[] DefaultNodePositions =
    {
        new Vector2(-200f, 300f), new Vector2(0f, 300f), new Vector2(200f, 300f),
        new Vector2(200f, 30f), new Vector2(0f, 30f), new Vector2(-200f, 30f),
        new Vector2(-200f, -240f), new Vector2(0f, -240f),
    };

    [MenuItem("Tools/Planet Boom/UI/Create Evolution Popup")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        if (!scene.IsValid() || scene.name != "GameScene" || canvas == null)
            throw new System.InvalidOperationException("Open GameScene before creating the Evolution Popup.");

        TMP_Text template = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);
        Sprite backgroundSprite = LoadSprite(BackgroundPath);
        Sprite closeSprite = LoadSprite(CloseSpritePath);
        Sprite continueSprite = LoadSprite(ContinueSpritePath);
        Sprite connectorSprite = LoadSprite(ConnectorSpritePath);

        bool created;
        RectTransform root = EnsureRect(canvas.transform, "EvolutionPopup", out created);
        if (created) Stretch(root);
        EvolutionPopupUI controller = EnsureComponent<EvolutionPopupUI>(root.gameObject);

        RectTransform popupRoot = EnsureRect(root, "PopupRoot", out created);
        if (created) Stretch(popupRoot);
        EnsureComponent<CanvasGroup>(popupRoot.gameObject);
        EnsureComponent<PopupTransition>(popupRoot.gameObject);

        Image overlay = EnsureImage(popupRoot, "Overlay", out created);
        if (created)
        {
            Stretch(overlay.rectTransform); overlay.sprite = null;
            overlay.color = new Color(0f, 0f, 0f, .72f); overlay.raycastTarget = true;
        }

        RectTransform panel = EnsureRect(popupRoot, "Panel", out created);
        if (created)
        {
            SetRect(panel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(900f, 1200f));
            // Invisible blocker so taps inside the panel (letterboxed art
            // edges included) never fall through to gameplay.
            Image blocker = EnsureComponent<Image>(panel.gameObject);
            blocker.sprite = null; blocker.color = new Color(1f, 1f, 1f, 0f); blocker.raycastTarget = true;
        }

        Image background = EnsureImage(panel, "Background", out created);
        if (created)
        {
            Stretch(background.rectTransform);
            SetArtwork(background, backgroundSprite);
        }

        TextMeshProUGUI title = EnsureText(panel, "TitleText", template, "EVOLUTION", 64f, out created);
        if (created) SetRect(title.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -90f), new Vector2(560f, 90f));

        Image closeImage = EnsureImage(panel, "CloseButton", out created);
        if (created)
        {
            SetRect(closeImage.rectTransform, new Vector2(1f, 1f), new Vector2(-70f, -75f), new Vector2(105f, 105f));
            SetArtwork(closeImage, closeSprite); closeImage.raycastTarget = true;
        }
        Button closeButton = EnsureComponent<Button>(closeImage.gameObject);
        if (closeButton.targetGraphic == null) closeButton.targetGraphic = closeImage;
        SetButtonSound(closeImage.gameObject, UiSoundType.Back);

        RectTransform content = EnsureRect(panel, "EvolutionContent", out created);
        if (created) SetRect(content, new Vector2(.5f, .5f), new Vector2(0f, 45f), new Vector2(620f, 780f));

        // Connectors are created BEFORE nodes in the hierarchy so the path
        // segments always render underneath the planets.
        RectTransform connectorGroup = EnsureRect(content, "Connectors", out created);
        if (created) Stretch(connectorGroup);
        connectorGroup.SetAsFirstSibling();
        RectTransform nodeGroup = EnsureRect(content, "Nodes", out created);
        if (created) Stretch(nodeGroup);
        nodeGroup.SetAsLastSibling();

        Image[] nodeIcons = new Image[NodeCount];
        GameObject[] nodeGlows = new GameObject[NodeCount];
        TextMeshProUGUI[] nodeStatusTexts = new TextMeshProUGUI[NodeCount];
        for (int i = 0; i < NodeCount; i++)
        {
            Image node = EnsureImage(nodeGroup, $"PlanetNode{i + 1}", out created);
            if (created)
                SetRect(node.rectTransform, new Vector2(.5f, .5f), DefaultNodePositions[i], new Vector2(NodeSize, NodeSize));

            // The visible planet art lives on a PlanetImage CHILD so the
            // glow sibling can render behind it (UI children always draw on
            // top of their parent, so a glow behind the art is impossible
            // while the parent itself carries the sprite). Migrates older
            // popups where PlanetNode held the sprite directly: the art
            // moves to the child and the container Image is disabled.
            Image planetImage = EnsureImage(node.transform, "PlanetImage", out created);
            if (created)
            {
                Stretch(planetImage.rectTransform);
                Sprite art = node.sprite != null ? node.sprite : LoadSprite($"{PlanetIconFolder}/Planet{i + 1}.png");
                SetArtwork(planetImage, art);
            }
            node.enabled = false;
            nodeIcons[i] = planetImage;

            // Upcoming-sector glow: a FLAT soft white circle (built-in Knob)
            // tinted gold by UiGlowPulse. A UI tint multiplies the sprite's
            // own texture colors, so the old real-planet-sprite glow leaked
            // the actual artwork — a flat white shape can never reveal it.
            Image glow = EnsureImage(node.transform, "UpcomingGlow", out created);
            if (created)
            {
                SetRect(glow.rectTransform, new Vector2(.5f, .5f), Vector2.zero,
                    new Vector2(NodeSize * 1.45f, NodeSize * 1.45f));
                glow.raycastTarget = false;
                glow.gameObject.SetActive(false);
            }
            Sprite glowSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            // Never leave real planet art in the glow slot — with no soft
            // circle available a null sprite (flat quad) is still leak-free.
            if (glow.sprite != glowSprite)
                glow.sprite = glowSprite;
            glow.preserveAspect = true;
            glow.transform.SetAsFirstSibling();
            planetImage.transform.SetSiblingIndex(1);
            EnsureComponent<UiGlowPulse>(glow.gameObject);
            nodeGlows[i] = glow.gameObject;

            // Status label ("UPCOMING"/"NEW") below the planet. Created
            // empty and inactive; runtime drives text, state color and
            // visibility while position/font/size stay Inspector-authored.
            TextMeshProUGUI status = EnsureText(node.transform, "StatusText", template, "", 26f, out created);
            if (created)
            {
                SetRect(status.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, -26f), new Vector2(180f, 42f));
                status.gameObject.SetActive(false);
            }
            status.transform.SetAsLastSibling();
            nodeStatusTexts[i] = status;
        }

        // Each segment is laid out from the two nodes' CURRENT anchored
        // positions, so deleting a connector and re-running the command
        // rebuilds it to match manually repositioned planets.
        for (int i = 0; i < NodeCount - 1; i++)
        {
            Image connector = EnsureImage(connectorGroup, $"Connector{i + 1}_{i + 2}", out created);
            if (!created) continue;
            Vector2 from = nodeIcons[i].rectTransform.anchoredPosition;
            Vector2 to = nodeIcons[i + 1].rectTransform.anchoredPosition;
            Vector2 delta = to - from;
            SetRect(connector.rectTransform, new Vector2(.5f, .5f), (from + to) * .5f,
                new Vector2(Mathf.Max(40f, delta.magnitude - NodeSize * .7f), 12f));
            connector.rectTransform.localEulerAngles =
                new Vector3(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            connector.sprite = connectorSprite;
            connector.color = new Color(1f, 1f, 1f, .5f);
            connector.raycastTarget = false;
        }

        Image continueImage = EnsureImage(panel, "ContinueButton", out created);
        if (created)
        {
            SetRect(continueImage.rectTransform, new Vector2(.5f, 0f), new Vector2(0f, 85f), new Vector2(430f, 115f));
            SetArtwork(continueImage, continueSprite); continueImage.raycastTarget = true;
        }
        Button continueButton = EnsureComponent<Button>(continueImage.gameObject);
        if (continueButton.targetGraphic == null) continueButton.targetGraphic = continueImage;
        SetButtonSound(continueImage.gameObject, UiSoundType.Confirm);
        TextMeshProUGUI continueText = EnsureText(continueImage.transform, "ContinueText", template, "CONTINUE", 46f, out created);
        if (created) Stretch(continueText.rectTransform);

        Button openButton = FindHamburgerButton(scene);

        SerializedObject serialized = new SerializedObject(controller);
        serialized.FindProperty("popupRoot").objectReferenceValue = popupRoot.gameObject;
        serialized.FindProperty("openButton").objectReferenceValue = openButton;
        serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
        serialized.FindProperty("continueButton").objectReferenceValue = continueButton;
        SerializedProperty nodesProperty = serialized.FindProperty("planetNodes");
        nodesProperty.arraySize = NodeCount;
        for (int i = 0; i < NodeCount; i++)
        {
            SerializedProperty row = nodesProperty.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("tier").enumValueIndex = i;
            row.FindPropertyRelative("icon").objectReferenceValue = nodeIcons[i];
            row.FindPropertyRelative("upcomingGlow").objectReferenceValue = nodeGlows[i];
            row.FindPropertyRelative("statusText").objectReferenceValue = nodeStatusTexts[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();

        popupRoot.gameObject.SetActive(false);
        EditorUtility.SetDirty(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;

        Debug.Log("Evolution Popup ready. " +
                  $"Background={Name(backgroundSprite)}, Close={Name(closeSprite)}, Continue={Name(continueSprite)}, " +
                  (openButton != null ? $"open button={openButton.name}." : "open button NOT FOUND — assign EvolutionPopupUI.openButton manually.") +
                  " Save GameScene manually.");
    }

    private static Button FindHamburgerButton(Scene scene)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
            foreach (Button button in sceneRoot.GetComponentsInChildren<Button>(true))
                if (button.gameObject.name == "HamburgerButton")
                    return button;
        return null;
    }

    private static void SetButtonSound(GameObject go, UiSoundType type)
    {
        if (go.GetComponent<UiButtonSound>() != null) return;
        UiButtonSound sound = Undo.AddComponent<UiButtonSound>(go);
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
            Undo.RegisterCreatedObjectUndo(go, "Evolution Popup");
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

    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }

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
