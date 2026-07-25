#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Additive and idempotent: creates only the cinematic layer, keeps all
// existing HUD layouts and every subsequently hand-authored value intact.
public static class LevelStartSequenceAuthoring
{
    [MenuItem("Tools/Planet Boom/Gameplay/Add Level Start Cinematic")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "Level Start Cinematic authoring is unavailable in Play Mode. " +
                "Exit Play Mode, run the command, then save GameScene.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
            throw new System.InvalidOperationException("Open GameScene before adding the level-start cinematic.");

        Canvas canvas = Object.FindAnyObjectByType<Canvas>(FindObjectsInactive.Include);
        MissionHUD missions = Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include);
        ActiveEffectsHUD effects = Object.FindAnyObjectByType<ActiveEffectsHUD>(FindObjectsInactive.Include);
        GameManager manager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
        if (canvas == null || missions == null || effects == null || manager == null)
            throw new System.InvalidOperationException("GameScene is missing Canvas, GameManager, MissionHUD or ActiveEffectsHUD.");

        RectTransform timerText = manager.GameplayTimerRect;
        if (timerText == null)
            throw new System.InvalidOperationException("GameManager gameplay timer reference is missing.");
        RectTransform timerHud = timerText.parent as RectTransform;
        if (timerHud == null)
            throw new System.InvalidOperationException("TimerHUD was not found above the gameplay timer.");
        CanvasGroup timerGroup = EnsureCanvasGroup(timerHud.gameObject);
        CanvasGroup timerTextGroup = EnsureCanvasGroup(timerText.gameObject);

        RectTransform root = EnsureRect(canvas.transform, "LevelStartCinematic");
        Stretch(root);
        LevelStartSequence sequence = root.GetComponent<LevelStartSequence>() ??
            Undo.AddComponent<LevelStartSequence>(root.gameObject);

        TMP_Text template = timerText.GetComponent<TMP_Text>();
        TextMeshProUGUI level = EnsureText(root, "LevelIntroText", template, "LEVEL 5");
        RectTransform modeRoot = EnsureRect(root, "TimeRushModeRoot");
        InitializeRectOnce(modeRoot, Vector2.zero, new Vector2(1050f, 330f));
        CanvasGroup modeGroup = EnsureCanvasGroup(modeRoot.gameObject);
        TextMeshProUGUI modeTitle = EnsureText(modeRoot, "ModeTitleText", template, "TIME RUSH");
        TextMeshProUGUI modeDescription = EnsureText(modeRoot, "ModeDescriptionText", template,
            "MERGE TO GAIN TIME");
        InitializeRectOnce(modeTitle.rectTransform, new Vector2(0f, 55f), new Vector2(1000f, 150f));
        InitializeRectOnce(modeDescription.rectTransform, new Vector2(0f, -70f), new Vector2(1000f, 90f));
        modeTitle.fontSize = Mathf.Max(modeTitle.fontSize, 104f);
        modeDescription.fontSize = Mathf.Max(modeDescription.fontSize, 48f);

        RectTransform edgeRoot = EnsureRect(root, "TimeRushEdgeEffect");
        Stretch(edgeRoot);
        CanvasGroup edgeGroup = EnsureCanvasGroup(edgeRoot.gameObject);
        Image edgeLeft = EnsureEdge(edgeRoot, "EdgeLeft", Edge.Left);
        Image edgeRight = EnsureEdge(edgeRoot, "EdgeRight", Edge.Right);
        Image edgeTop = EnsureEdge(edgeRoot, "EdgeTop", Edge.Top);
        Image edgeBottom = EnsureEdge(edgeRoot, "EdgeBottom", Edge.Bottom);
        edgeRoot.SetAsFirstSibling();

        TextMeshProUGUI time = EnsureText(root, "TimeIntroText", template, "TIME: 45 SEC");
        TextMeshProUGUI countdown = EnsureText(root, "CountdownText", template, "3");
        InitializeRectOnce(level.rectTransform, new Vector2(0f, 120f), new Vector2(900f, 180f));
        InitializeRectOnce(time.rectTransform, Vector2.zero, new Vector2(1000f, 180f));
        InitializeRectOnce(countdown.rectTransform, Vector2.zero, new Vector2(600f, 260f));
        level.fontSize = Mathf.Max(level.fontSize, 100f);
        time.fontSize = Mathf.Max(time.fontSize, 86f);
        countdown.fontSize = Mathf.Max(countdown.fontSize, 160f);

        SerializedObject serialized = new SerializedObject(sequence);
        Set(serialized, "missionHud", missions);
        Set(serialized, "activeEffectsHud", effects);
        Set(serialized, "timerHudTarget", timerText);
        Set(serialized, "timerHudGroup", timerGroup);
        Set(serialized, "timerHudTextGroup", timerTextGroup);
        Set(serialized, "levelIntroText", level);
        Set(serialized, "timeRushModeRoot", modeRoot);
        Set(serialized, "timeRushModeGroup", modeGroup);
        Set(serialized, "timeRushTitleText", modeTitle);
        Set(serialized, "timeRushDescriptionText", modeDescription);
        Set(serialized, "timeRushEdgeEffect", edgeRoot.gameObject);
        Set(serialized, "timeRushEdgeGroup", edgeGroup);
        SerializedProperty edgeImages = serialized.FindProperty("timeRushEdgeImages");
        edgeImages.arraySize = 4;
        edgeImages.GetArrayElementAtIndex(0).objectReferenceValue = edgeLeft;
        edgeImages.GetArrayElementAtIndex(1).objectReferenceValue = edgeRight;
        edgeImages.GetArrayElementAtIndex(2).objectReferenceValue = edgeTop;
        edgeImages.GetArrayElementAtIndex(3).objectReferenceValue = edgeBottom;
        Set(serialized, "timeIntroText", time);
        Set(serialized, "countdownText", countdown);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PreLevelPanel preLevel = Object.FindAnyObjectByType<PreLevelPanel>(FindObjectsInactive.Include);
        Transform oldTime = preLevel != null
            ? preLevel.transform.Find("Panel/LevelTimeText") : null;
        if (oldTime != null) oldTime.gameObject.SetActive(false);

        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(effects);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;
        Debug.Log("Level-start cinematic created/wired. Adjust authored TMP RectTransforms and LevelStartSequence timing fields, then save GameScene.");
    }

    [MenuItem("Tools/Planet Boom/Gameplay/Add Level Start Cinematic", true)]
    private static bool CanApply() => !EditorApplication.isPlayingOrWillChangePlaymode;

    private static RectTransform EnsureRect(Transform parent, string name)
    {
        Transform found = parent.Find(name);
        if (found != null) return (RectTransform)found;
        GameObject go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Level Start Cinematic");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static TextMeshProUGUI EnsureText(Transform parent, string name,
        TMP_Text template, string preview)
    {
        Transform found = parent.Find(name);
        TextMeshProUGUI text;
        if (found == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(go, "Level Start Cinematic");
            go.transform.SetParent(parent, false);
            text = go.GetComponent<TextMeshProUGUI>();
            if (template != null)
            {
                text.font = template.font;
                text.fontSharedMaterial = template.fontSharedMaterial;
                text.color = template.color;
            }
            text.alignment = TextAlignmentOptions.Center;
            text.fontStyle = FontStyles.Bold;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.text = preview;
        }
        else text = found.GetComponent<TextMeshProUGUI>() ??
            Undo.AddComponent<TextMeshProUGUI>(found.gameObject);
        return text;
    }

    private enum Edge { Left, Right, Top, Bottom }

    private static Image EnsureEdge(RectTransform parent, string name, Edge edge)
    {
        Transform found = parent.Find(name);
        bool created = found == null;
        RectTransform rect = created ? EnsureRect(parent, name) : (RectTransform)found;
        Image image = rect.GetComponent<Image>() ?? Undo.AddComponent<Image>(rect.gameObject);
        if (created)
        {
            const float thickness = 90f;
            if (edge == Edge.Left || edge == Edge.Right)
            {
                float x = edge == Edge.Left ? 0f : 1f;
                rect.anchorMin = new Vector2(x, 0f);
                rect.anchorMax = new Vector2(x, 1f);
                rect.pivot = new Vector2(x, .5f);
                rect.sizeDelta = new Vector2(thickness, 0f);
                rect.anchoredPosition = Vector2.zero;
            }
            else
            {
                float y = edge == Edge.Bottom ? 0f : 1f;
                rect.anchorMin = new Vector2(0f, y);
                rect.anchorMax = new Vector2(1f, y);
                rect.pivot = new Vector2(.5f, y);
                rect.sizeDelta = new Vector2(0f, thickness);
                rect.anchoredPosition = Vector2.zero;
            }
            image.color = Color.white;
        }
        image.raycastTarget = false;
        return image;
    }

    private static void InitializeRectOnce(RectTransform rect, Vector2 position, Vector2 size)
    {
        // A newly-created Unity UI RectTransform starts at 100x100, while a
        // new TMP RectTransform starts at 200x50. Repair those untouched
        // defaults on re-run, but preserve every manually authored layout.
        bool untouchedDefault = rect.sizeDelta == Vector2.zero ||
            rect.sizeDelta == new Vector2(100f, 100f) ||
            rect.sizeDelta == new Vector2(200f, 50f);
        if (!untouchedDefault) return;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private static void Set(SerializedObject serialized, string name, Object value) =>
        serialized.FindProperty(name).objectReferenceValue = value;

    private static CanvasGroup EnsureCanvasGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
            group = Undo.AddComponent<CanvasGroup>(target);
        EditorUtility.SetDirty(target);
        return group;
    }
}
#endif
