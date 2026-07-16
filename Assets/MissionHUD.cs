using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Compact, data-driven objective presentation for the active level. Runtime
// objective state remains owned by GameManager; this component only mirrors
// snapshots and never changes completion logic or configuration data.
public class MissionHUD : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private TextMeshProUGUI levelTitleText;
    [SerializeField] private Image[] targetSlots = new Image[GameManager.MaxTargetsPerLevel];
    [SerializeField] private Planet planetPrefab;

    [Header("Objective Icons")]
    [Tooltip("Optional. If empty, Merge Count remains fully readable as text.")]
    [SerializeField] private Sprite mergeCountIcon;
    [Tooltip("Optional. If empty, Combo Target remains fully readable as text.")]
    [SerializeField] private Sprite comboTargetIcon;
    [Tooltip("Uses the existing meteor art in GameScene when assigned.")]
    [SerializeField] private Sprite meteorObjectiveIcon;
    [Tooltip("Optional. If empty, Survival remains fully readable as text.")]
    [SerializeField] private Sprite survivalIcon;

    [Header("Dynamic Layout")]
    [SerializeField, Min(54f)] private float entryWidth = 92f;
    [SerializeField, Min(54f)] private float entryHeight = 92f;
    [SerializeField, Min(0f)] private float entrySpacing = 6f;
    [SerializeField] private Vector2 objectiveAreaOffset = new Vector2(0f, -128f);
    [SerializeField, Range(10f, 36f)] private float progressFontSize = 27f;

    [Header("State Look")]
    [SerializeField] private Color pendingTint = Color.white;
    [SerializeField] private Color achievedTint = new Color(0.45f, 1f, 0.55f, 1f);
    [SerializeField] private Color optionalTint = new Color(1f, 1f, 1f, 0.62f);
    [SerializeField, Min(0f)] private float completionPopDuration = 0.2f;
    [SerializeField, Range(1f, 1.4f)] private float completionPopScale = 1.12f;

    private sealed class ObjectiveView
    {
        public RectTransform Root;
        public Image Icon;
        public TextMeshProUGUI Progress;
        public TextMeshProUGUI Checkmark;
        public bool Completed;
        public Coroutine PopRoutine;
    }

    private readonly Dictionary<int, ObjectiveView> views = new Dictionary<int, ObjectiveView>();
    private RectTransform objectiveArea;
    private GameManager subscribedManager;

    private void Awake()
    {
        if (TryGetComponent(out Graphic panelGraphic))
            panelGraphic.raycastTarget = false;
        if (levelTitleText != null)
            levelTitleText.raycastTarget = false;
        DisableLegacySlots();
        EnsureObjectiveArea();
    }

    private void Start()
    {
        BindToGameManager();
        RefreshFromActiveObjectives();
    }

    private void OnEnable()
    {
        BindToGameManager();
    }

    private void OnDisable()
    {
        UnbindFromGameManager();
    }

    private void LateUpdate()
    {
        // GameManager may be created or replaced after this HUD is enabled.
        if (subscribedManager != GameManager.Instance)
        {
            BindToGameManager();
            RefreshFromActiveObjectives();
        }
    }

    private void BindToGameManager()
    {
        GameManager manager = GameManager.Instance;
        if (subscribedManager == manager)
            return;

        UnbindFromGameManager();
        subscribedManager = manager;
        if (subscribedManager == null)
            return;

        subscribedManager.ObjectivesInitialized += HandleObjectivesInitialized;
        subscribedManager.ObjectiveProgressChanged += HandleObjectiveProgressChanged;
    }

    private void UnbindFromGameManager()
    {
        if (subscribedManager == null)
            return;
        subscribedManager.ObjectivesInitialized -= HandleObjectivesInitialized;
        subscribedManager.ObjectiveProgressChanged -= HandleObjectiveProgressChanged;
        subscribedManager = null;
    }

    // Kept as GameManager's existing level-reload entry point. The legacy
    // target list is intentionally ignored when runtime objectives exist.
    public void ShowLevel(int levelNumber, IReadOnlyList<PlanetTier> legacyTargets)
    {
        if (levelTitleText != null)
            levelTitleText.text = $"LEVEL {levelNumber}";

        BindToGameManager();
        RefreshFromActiveObjectives();
    }

    // Compatibility hook for the old ReachTier HUD calls. Dynamic views are
    // updated by ObjectiveProgressChanged, which carries the true objective
    // index and is authoritative for every objective type.
    public void MarkAchieved(int legacySlotIndex)
    {
        if (views.Count == 0)
            RefreshFromActiveObjectives();
    }

    private void RefreshFromActiveObjectives()
    {
        GameManager manager = GameManager.Instance;
        if (manager == null || manager.ActiveObjectives == null)
            return;

        var snapshots = new List<LevelObjectiveProgress>(manager.ActiveObjectives.Count);
        foreach (LevelObjective objective in manager.ActiveObjectives)
            if (objective != null)
                snapshots.Add(objective.Snapshot);
        HandleObjectivesInitialized(snapshots);
    }

    private void HandleObjectivesInitialized(IReadOnlyList<LevelObjectiveProgress> objectives)
    {
        ClearViews();
        EnsureObjectiveArea();
        if (objectives == null)
            return;

        foreach (LevelObjectiveProgress objective in objectives)
        {
            ObjectiveView view = CreateView(objective);
            views[objective.Index] = view;
            ApplySnapshot(view, objective, false);
        }
    }

    private void HandleObjectiveProgressChanged(LevelObjectiveProgress objective)
    {
        if (!views.TryGetValue(objective.Index, out ObjectiveView view))
        {
            // Handles debug/reload paths that replace objective data without a
            // preceding initialization event.
            RefreshFromActiveObjectives();
            views.TryGetValue(objective.Index, out view);
        }
        if (view != null)
            ApplySnapshot(view, objective, true);
    }

    private ObjectiveView CreateView(LevelObjectiveProgress objective)
    {
        GameObject rootObject = new GameObject($"Objective {objective.Index + 1} - {objective.Type}",
            typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement));
        rootObject.layer = gameObject.layer;
        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.SetParent(objectiveArea, false);
        root.sizeDelta = new Vector2(entryWidth, entryHeight);
        LayoutElement layout = rootObject.GetComponent<LayoutElement>();
        layout.preferredWidth = entryWidth;
        layout.preferredHeight = entryHeight;
        rootObject.GetComponent<CanvasGroup>().blocksRaycasts = false;
        rootObject.GetComponent<CanvasGroup>().interactable = false;

        Image icon = CreateImage("Icon", root, new Vector2(0.5f, 1f),
            new Vector2(0f, -27f), new Vector2(48f, 48f));
        icon.sprite = ResolveIcon(objective);
        icon.gameObject.SetActive(icon.sprite != null);

        TextMeshProUGUI progress = CreateText("Progress", root, new Vector2(0.5f, 0f),
            new Vector2(0f, 18f), new Vector2(entryWidth, 38f), progressFontSize);
        progress.alignment = TextAlignmentOptions.Center;
        progress.enableAutoSizing = true;
        progress.fontSizeMin = 16f;
        progress.fontSizeMax = progressFontSize;

        TextMeshProUGUI checkmark = CreateText("Completed", root, new Vector2(1f, 1f),
            new Vector2(-10f, -10f), new Vector2(28f, 28f), 24f);
        checkmark.alignment = TextAlignmentOptions.Center;
        checkmark.text = "✓";
        checkmark.color = achievedTint;
        checkmark.gameObject.SetActive(false);

        return new ObjectiveView { Root = root, Icon = icon, Progress = progress, Checkmark = checkmark };
    }

    private void ApplySnapshot(ObjectiveView view, LevelObjectiveProgress objective, bool animate)
    {
        bool justCompleted = !view.Completed && objective.IsCompleted;
        view.Completed = objective.IsCompleted;
        view.Progress.text = FormatProgress(objective);

        Color stateColor = objective.IsCompleted ? achievedTint :
            objective.IsRequired ? pendingTint : optionalTint;
        view.Progress.color = stateColor;
        view.Icon.color = stateColor;
        view.Checkmark.gameObject.SetActive(objective.IsCompleted);

        if (animate && justCompleted && completionPopDuration > 0f)
        {
            if (view.PopRoutine != null)
                StopCoroutine(view.PopRoutine);
            view.PopRoutine = StartCoroutine(PopCompleted(view));
        }
    }

    private IEnumerator PopCompleted(ObjectiveView view)
    {
        float elapsed = 0f;
        while (elapsed < completionPopDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / completionPopDuration);
            float scale = Mathf.Lerp(completionPopScale, 1f, normalized);
            view.Root.localScale = Vector3.one * scale;
            yield return null;
        }
        view.Root.localScale = Vector3.one;
        view.PopRoutine = null;
    }

    private Sprite ResolveIcon(LevelObjectiveProgress objective)
    {
        switch (objective.Type)
        {
            case LevelObjectiveType.ReachTier:
                return planetPrefab != null ? planetPrefab.GetSpriteForTier(objective.TargetTier) : null;
            case LevelObjectiveType.MergeCount:
                return mergeCountIcon;
            case LevelObjectiveType.ComboTarget:
                return comboTargetIcon;
            case LevelObjectiveType.MeteorObjective:
                return meteorObjectiveIcon;
            case LevelObjectiveType.Survival:
                return survivalIcon;
            default:
                return null;
        }
    }

    private static string FormatProgress(LevelObjectiveProgress objective)
    {
        string optional = objective.IsRequired ? string.Empty : "  OPTIONAL";
        switch (objective.Type)
        {
            case LevelObjectiveType.ReachTier:
                return objective.TargetProgress > 1f
                    ? $"T{(int)objective.TargetTier + 1}  {Whole(objective.CurrentProgress)} / {Whole(objective.TargetProgress)}{optional}"
                    : $"TIER {(int)objective.TargetTier + 1}{optional}";
            case LevelObjectiveType.MergeCount:
                return $"<size=82%>MERGE</size>\n{Whole(objective.CurrentProgress)} / {Whole(objective.TargetProgress)}{optional}";
            case LevelObjectiveType.ComboTarget:
                return $"<size=82%>COMBO x{Whole(objective.TargetProgress)}</size>\nBest: x{Whole(objective.CurrentProgress)}{optional}";
            case LevelObjectiveType.MeteorObjective:
                return $"<size=82%>METEOR</size>\n{Whole(objective.CurrentProgress)} / {Whole(objective.TargetProgress)}{optional}";
            case LevelObjectiveType.Survival:
                return $"<size=82%>SURVIVE</size>\n{Whole(objective.CurrentProgress)} / {Whole(objective.TargetProgress)}s{optional}";
            default:
                return $"{Whole(objective.CurrentProgress)} / {Whole(objective.TargetProgress)}{optional}";
        }
    }

    private static string Whole(float value) => Mathf.FloorToInt(value + 0.001f).ToString();

    private void EnsureObjectiveArea()
    {
        if (objectiveArea != null)
            return;

        GameObject areaObject = new GameObject("Dynamic Objectives", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        areaObject.layer = gameObject.layer;
        objectiveArea = areaObject.GetComponent<RectTransform>();
        objectiveArea.SetParent(transform, false);
        objectiveArea.anchorMin = new Vector2(0.5f, 1f);
        objectiveArea.anchorMax = new Vector2(0.5f, 1f);
        objectiveArea.pivot = new Vector2(0.5f, 1f);
        objectiveArea.anchoredPosition = objectiveAreaOffset;
        objectiveArea.sizeDelta = new Vector2(320f, entryHeight);

        HorizontalLayoutGroup layout = areaObject.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = entrySpacing;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(0, 0, 0, 0);
    }

    private Image CreateImage(string objectName, RectTransform parent, Vector2 anchor,
        Vector2 anchoredPosition, Vector2 size)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        child.layer = gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        Image image = child.GetComponent<Image>();
        image.preserveAspect = true;
        image.raycastTarget = false;
        return image;
    }

    private TextMeshProUGUI CreateText(string objectName, RectTransform parent, Vector2 anchor,
        Vector2 anchoredPosition, Vector2 size, float fontSize)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        child.layer = gameObject.layer;
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
        if (levelTitleText != null)
        {
            text.font = levelTitleText.font;
            text.fontSharedMaterial = levelTitleText.fontSharedMaterial;
        }
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Ellipsis;
        return text;
    }

    private void DisableLegacySlots()
    {
        if (targetSlots == null)
            return;
        foreach (Image slot in targetSlots)
        {
            if (slot == null)
                continue;
            slot.raycastTarget = false;
            slot.gameObject.SetActive(false);
        }
    }

    private void ClearViews()
    {
        foreach (ObjectiveView view in views.Values)
        {
            if (view.PopRoutine != null)
                StopCoroutine(view.PopRoutine);
            if (view.Root != null)
                Destroy(view.Root.gameObject);
        }
        views.Clear();
    }
}
