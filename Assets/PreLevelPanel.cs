using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PreLevelPanel : MonoBehaviour
{
    [Serializable]
    private sealed class ObjectivePreview
    {
        public GameObject root;
        public TextMeshProUGUI label;
    }

    [Serializable]
    private sealed class BoosterEntry
    {
        public BoosterType type;
        public GameObject root;
        public Image icon;
        public Image quantityBadgeBackground;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI quantityText;
        public Button selectButton;
        public Image selectGraphic;
        public TextMeshProUGUI selectText;
        public GameObject selectedIndicator;
        public GameObject lockedIndicator;
    }

    [Header("Scene Wiring")]
    [SerializeField] private TextMeshProUGUI levelTitle;
    // Presentation-only starting-time readout. Runtime writes its string;
    // position/font/size/color stay whatever was authored in the Inspector.
    [SerializeField] private TextMeshProUGUI levelTimeText;
    [SerializeField] private RectTransform objectivesSection;
    [SerializeField] private ObjectivePreview[] objectivePreviews = new ObjectivePreview[3];
    [SerializeField] private BoosterEntry[] boosterEntries = new BoosterEntry[3];
    [SerializeField] private Button readyButton;
    [SerializeField] private Button closeButton;

    [Header("Booster Button Artwork")]
    [SerializeField] private Sprite useSprite;
    [SerializeField] private Sprite usedSprite;

    private readonly HashSet<BoosterType> pendingBoosters = new HashSet<BoosterType>();
    private GameManager preparedManager;
    private bool readyTransitionStarted;
    // Optional presentation layer on this same root; every path falls back
    // to the original instant SetActive behavior when it is absent.
    private PopupTransition transition;

    private void Awake()
    {
        transition = GetComponent<PopupTransition>();
        RevealAssignedArtwork();
        foreach (BoosterEntry entry in boosterEntries)
        {
            if (entry?.selectButton == null) continue;
            BoosterType captured = entry.type;
            entry.selectButton.onClick.AddListener(() => ToggleBooster(captured));
        }
        if (readyButton != null)
            readyButton.onClick.AddListener(OnReadyClicked);
        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

#if UNITY_EDITOR
    private void OnValidate() => RevealAssignedArtwork();
#endif

    private void RevealAssignedArtwork()
    {
        RevealImageAt("Panel/MainBackground");
        RevealImageAt("Panel/LevelHeader/Background");
        RevealImageAt("Panel/ReadyButton");
    }

    private void RevealImageAt(string relativePath)
    {
        Transform target = transform.Find(relativePath);
        if (target == null || !target.TryGetComponent(out Image image) || image.sprite == null)
            return;
        image.enabled = true;
        Color color = image.color;
        color.a = 1f;
        image.color = color;
        image.preserveAspect = true;
    }

    private void OnEnable()
    {
        PlayerDataPersistenceManager.DataLoaded += HandlePlayerDataLoaded;
        UnlockManager.UnlockChanged += HandleUnlockChanged;
        if (BoosterInventoryManager.Instance != null)
            BoosterInventoryManager.Instance.InventoryCountChanged += HandleInventoryChanged;
    }

    private void Start() => RefreshAllBoosters();

    private void OnDisable()
    {
        PlayerDataPersistenceManager.DataLoaded -= HandlePlayerDataLoaded;
        UnlockManager.UnlockChanged -= HandleUnlockChanged;
        if (BoosterInventoryManager.Instance != null)
            BoosterInventoryManager.Instance.InventoryCountChanged -= HandleInventoryChanged;
    }

    public void ShowPreparedLevel(GameManager manager)
    {
        preparedManager = manager;
        readyTransitionStarted = false;
        pendingBoosters.Clear();
        gameObject.SetActive(true);
        if (transition != null)
            transition.OpenAnimated();

        if (levelTitle != null)
        {
            int number = manager != null && manager.ActiveLevelConfiguration != null
                ? manager.ActiveLevelConfiguration.levelNumber
                : manager != null ? manager.CurrentLevelNumber : 1;
            levelTitle.text = $"LEVEL {number}";
        }

        if (levelTimeText != null)
            levelTimeText.text = FormatLevelTime(manager);

        BindObjectivePreviews(manager);
        RefreshAllBoosters();
        if (readyButton != null) readyButton.interactable = true;
        LogPending("Selected Boosters");
    }

    private void BindObjectivePreviews(GameManager manager)
    {
        int count = manager?.ActiveObjectives != null
            ? Mathf.Min(manager.ActiveObjectives.Count, objectivePreviews.Length)
            : 0;
        for (int i = 0; i < objectivePreviews.Length; i++)
        {
            ObjectivePreview preview = objectivePreviews[i];
            if (preview?.root == null) continue;
            bool visible = i < count && manager.ActiveObjectives[i] != null;
            preview.root.SetActive(visible);
            if (visible && preview.label != null)
                preview.label.text = FormatPreview(manager.ActiveObjectives[i].Snapshot);
        }
        if (objectivesSection != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(objectivesSection);
    }

    private static string FormatPreview(LevelObjectiveProgress objective)
    {
        switch (objective.Type)
        {
            case LevelObjectiveType.ReachTier:
                return $"REACH: TIER {(int)objective.TargetTier + 1}";
            case LevelObjectiveType.MergeCount:
                return $"MERGE: 0/{Whole(objective.TargetProgress)}";
            case LevelObjectiveType.ComboTarget:
                return $"COMBO: X{Whole(objective.TargetProgress)}";
            case LevelObjectiveType.MeteorObjective:
                return $"DESTROY: {Whole(objective.TargetProgress)} METEOR";
            case LevelObjectiveType.Survival:
                return $"SURVIVE: {Whole(objective.TargetProgress)} SEC";
            default:
                return $"MISSION {Whole(objective.TargetProgress)}";
        }
    }

    private static string Whole(float value) => Mathf.FloorToInt(value + .001f).ToString();

    // The displayed value is the same authored starting time LoadLevel feeds
    // the authoritative clock — no second timer, just its configured origin.
    private static string FormatLevelTime(GameManager manager)
    {
        LevelConfiguration config = manager != null ? manager.ActiveLevelConfiguration : null;
        if (config != null && config.timeMode == LevelTimeMode.MergeTimeRush)
            return $"TIME RUSH: {Mathf.CeilToInt(config.timeRushStartingTime)} SEC";
        if (config != null)
            return $"TIME: {Mathf.CeilToInt(config.timeLimit)} SEC";

        // Legacy list-driven levels carry no LevelConfiguration; RemainingTime
        // was already reset to their full clock by LoadLevel before this panel
        // is shown, so it still reads the true starting value here.
        return $"TIME: {Mathf.CeilToInt(manager != null ? manager.RemainingTime : 0f)} SEC";
    }

    private void ToggleBooster(BoosterType type)
    {
        if (readyTransitionStarted || !IsAvailable(type)) return;
        if (!pendingBoosters.Add(type)) pendingBoosters.Remove(type);
        RefreshAllBoosters();
        LogPending("Selected Boosters");
    }

    private bool IsAvailable(BoosterType type)
    {
        return BoosterInventoryManager.Instance != null &&
            BoosterInventoryManager.Instance.GetCount(type) > 0 &&
            UnlockManager.Instance != null && UnlockManager.Instance.IsUnlocked(type);
    }

    private void RefreshAllBoosters()
    {
        foreach (BoosterEntry entry in boosterEntries)
        {
            if (entry == null) continue;
            int count = BoosterInventoryManager.Instance?.GetCount(entry.type) ?? 0;
            bool unlocked = UnlockManager.Instance != null && UnlockManager.Instance.IsUnlocked(entry.type);
            bool eligible = unlocked && count > 0;
            bool available = eligible && !readyTransitionStarted;
            bool selected = pendingBoosters.Contains(entry.type) &&
                (eligible || readyTransitionStarted);
            if (!eligible && !readyTransitionStarted) pendingBoosters.Remove(entry.type);

            if (entry.root != null) entry.root.SetActive(true);
            if (entry.nameText != null) entry.nameText.text = DisplayName(entry.type);
            // Selection is still pending: preview the quantity that will
            // remain after READY without consuming persistent inventory yet.
            int displayedCount = selected ? Mathf.Max(0, count - 1) : count;
            if (entry.quantityText != null) entry.quantityText.text = displayedCount.ToString();
            if (entry.selectButton != null) entry.selectButton.interactable = available;
            Sprite stateSprite = selected ? usedSprite : useSprite;
            if (entry.selectGraphic != null && stateSprite != null)
            {
                entry.selectGraphic.sprite = stateSprite;
                entry.selectGraphic.enabled = true;
            }
            if (entry.selectText != null)
            {
                entry.selectText.text = selected ? "USED" : "USE";
                entry.selectText.gameObject.SetActive(stateSprite == null);
            }
            if (entry.selectedIndicator != null) entry.selectedIndicator.SetActive(false);
            if (entry.lockedIndicator != null) entry.lockedIndicator.SetActive(!unlocked);
            if (entry.icon != null)
            {
                Sprite resolvedIcon = ResolveIcon(entry.type);
                if (resolvedIcon != null)
                    entry.icon.sprite = resolvedIcon;
                entry.icon.enabled = entry.icon.sprite != null;
                entry.icon.preserveAspect = true;
            }
        }
    }

    private static string DisplayName(BoosterType type)
    {
        switch (type)
        {
            case BoosterType.LuckyDrop: return "Lucky Drop";
            case BoosterType.DoubleTimeDrop: return "2X Time Drop";
            case BoosterType.StarBooster: return "Star Booster";
            default: return type.ToString().ToUpperInvariant();
        }
    }

    private static Sprite ResolveIcon(BoosterType type)
    {
        SkillDropManager drops = SkillDropManager.Instance;
        if (drops == null) return null;
        switch (type)
        {
            case BoosterType.LuckyDrop: return drops.LuckyDropIcon;
            case BoosterType.DoubleTimeDrop: return drops.DoubleTimeDropIcon;
            case BoosterType.StarBooster: return drops.StarBoosterIcon;
            default: return null;
        }
    }

    private void HandleInventoryChanged(BoosterType type, int count) => RefreshAllBoosters();
    private void HandlePlayerDataLoaded(PlayerData data) => RefreshAllBoosters();
    private void HandleUnlockChanged(string canonicalId, bool unlocked) => RefreshAllBoosters();

    private void OnReadyClicked()
    {
        if (readyTransitionStarted || preparedManager == null ||
            preparedManager.State != GameManager.GameState.LevelPreparing)
            return;

        foreach (BoosterType type in new List<BoosterType>(pendingBoosters))
        {
            if (!IsAvailable(type))
            {
                pendingBoosters.Remove(type);
                RefreshAllBoosters();
                UiSounds.PlayError();
                Debug.LogWarning($"[PreLevel] {type} is no longer available; selection removed.", this);
                return;
            }
        }

        readyTransitionStarted = true;
        if (readyButton != null) readyButton.interactable = false;
        BoosterInventoryManager inventory = BoosterInventoryManager.Instance;
        if (pendingBoosters.Count > 0 &&
            (inventory == null || !inventory.TryActivatePreparedRun(pendingBoosters)))
        {
            readyTransitionStarted = false;
            RefreshAllBoosters();
            if (readyButton != null) readyButton.interactable = true;
            UiSounds.PlayError();
            Debug.LogWarning("[PreLevel] Booster activation was rejected; run remains prepared.", this);
            return;
        }

        LogActivated(inventory);
        // The mission-card intro must not start under a still-visible panel:
        // BeginPreparedLevel only runs once the close transition has finished
        // and the panel is deactivated.
        if (transition != null)
            transition.CloseAnimated(HandOffToPreparedLevel);
        else
        {
            gameObject.SetActive(false);
            HandOffToPreparedLevel();
        }
    }

    private void HandOffToPreparedLevel()
    {
        if (preparedManager == null)
            return;
        if (!preparedManager.BeginPreparedLevel())
        {
            if (transition != null)
                transition.OpenAnimated();
            else
                gameObject.SetActive(true);
            readyTransitionStarted = false;
            RefreshAllBoosters();
        }
    }

    private void OnCloseClicked()
    {
        if (readyTransitionStarted || preparedManager == null ||
            preparedManager.State != GameManager.GameState.LevelPreparing)
            return;
        pendingBoosters.Clear();
        if (transition != null)
            transition.CloseAnimated(() => preparedManager?.AbandonPreparedRunToLevelMap());
        else
            preparedManager.AbandonPreparedRunToLevelMap();
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogPending(string heading)
    {
        StringBuilder text = new StringBuilder($"[PreLevel]\n{heading}:");
        if (pendingBoosters.Count == 0) text.Append("\n- None");
        foreach (BoosterType type in pendingBoosters) text.Append("\n- ").Append(type);
        Debug.Log(text.ToString(), this);
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void LogActivated(BoosterInventoryManager inventory)
    {
        StringBuilder text = new StringBuilder("[PreLevel]\nActivated Boosters:");
        if (pendingBoosters.Count == 0) text.Append("\n- None");
        foreach (BoosterType type in pendingBoosters)
            text.Append("\n- ").Append(type).Append(" (remaining: ")
                .Append(inventory?.GetCount(type) ?? 0).Append(')');
        Debug.Log(text.ToString(), this);
    }
}
