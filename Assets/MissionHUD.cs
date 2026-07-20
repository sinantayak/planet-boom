using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation-only adapter for GameManager's existing objective snapshots.
public sealed class MissionHUD : MonoBehaviour
{
    [System.Serializable]
    private sealed class MissionCard
    {
        public RectTransform root;
        public Image background;
        public TextMeshProUGUI missionTitle;
        public TextMeshProUGUI missionObjective;
        public TextMeshProUGUI objectiveSecondary;
        public Image objectiveVisual;
        public TextMeshProUGUI completed;
        [System.NonSerialized] public bool isCompleted;
        [System.NonSerialized] public Coroutine popRoutine;
        [System.NonSerialized] public Vector2 introFinalPosition;
        [System.NonSerialized] public Vector3 introFinalScale;
        [System.NonSerialized] public bool introTransformCaptured;
    }

    [Header("Persistent Mission Cards")]
    [SerializeField] private List<MissionCard> missionCards = new List<MissionCard>(3);
    [SerializeField] private Sprite missionCardSprite;
    [SerializeField] private Planet planetPrefab;
    [SerializeField] private Sprite[] comboTargetSprites = new Sprite[5];

    [Header("Mission Card Presentation")]
    [SerializeField] private Vector2 missionCardSize = new Vector2(320f, 190f);
    [SerializeField, Min(0f)] private float missionCardSpacing = 18f;
    [SerializeField, Range(20f, 64f)] private float missionTitleFontSize = 48f;
    [SerializeField, Range(28f, 72f)] private float missionObjectiveFontSize = 48f;
    [SerializeField, Range(16f, 36f)] private float missionSecondaryFontSize = 22f;
    [SerializeField] private Vector2 missionVisualSize = new Vector2(190f, 130f);

    [Header("Completed Look")]
    [SerializeField] private Color pendingTint = Color.white;
    [SerializeField] private Color achievedTint = new Color(.45f, 1f, .55f, 1f);
    [SerializeField] private Color optionalTint = new Color(1f, 1f, 1f, .62f);
    [SerializeField, Min(0f)] private float completionPopDuration = .2f;
    [SerializeField, Range(1f, 1.4f)] private float completionPopScale = 1.12f;

    [Header("Pre-Level Mission Intro")]
    [SerializeField, Min(0.05f)] private float missionIntroCardDuration = 0.22f;
    [SerializeField, Min(0f)] private float missionIntroDelayBetweenCards = 0.08f;
    [SerializeField, Range(0.05f, 1f)] private float missionIntroStartScale = 0.55f;
    [SerializeField, Range(1f, 1.5f)] private float missionIntroOvershootScale = 1.1f;
    [SerializeField] private Vector2 missionIntroStartOffset = new Vector2(0f, -45f);
    [SerializeField, Min(0f)] private float missionIntroFinalDelay = 0.16f;
    [SerializeField] private AudioClip missionCardRevealClip;
    [SerializeField, Range(0f, 1f)] private float missionCardRevealVolume = 0.9f;

    // Future Active Effects HUD can append its own unscaled sequence here.
    public event System.Func<IEnumerator> AfterMissionIntroSequence;

    private readonly Dictionary<int, MissionCard> activeCards = new Dictionary<int, MissionCard>();
    private GameManager subscribedManager;
    private bool runPresentationVisible = true;
    private Coroutine missionIntroRoutine;
    private HorizontalLayoutGroup introLayout;

    private void Awake()
    {
        EnsureObjectiveVisuals();
        ApplyPresentationOnce();
        HideAllCards();
    }

    // Scene authoring normally supplies these persistent Images. Keep a safe
    // runtime fallback so missing/stale scene references never silently turn
    // Reach and Combo missions back into plain text.
    private void EnsureObjectiveVisuals()
    {
        foreach (MissionCard card in missionCards)
        {
            if (card?.root == null || card.objectiveVisual != null)
                continue;

            Transform existing = card.root.Find("MissionVisual");
            Image visual = existing != null ? existing.GetComponent<Image>() : null;
            if (visual == null)
            {
                GameObject visualObject = new GameObject("MissionVisual", typeof(RectTransform),
                    typeof(CanvasRenderer), typeof(Image));
                visualObject.transform.SetParent(card.root, false);
                visual = visualObject.GetComponent<Image>();
            }

            RectTransform rect = visual.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = new Vector2(0f, -14f);
            rect.sizeDelta = missionVisualSize;
            visual.preserveAspect = true;
            visual.raycastTarget = false;
            visual.gameObject.SetActive(false);
            card.objectiveVisual = visual;
        }
    }

    private void Start()
    {
        BindToGameManager();
        RefreshFromActiveObjectives();
    }

    private void OnEnable() => BindToGameManager();
    private void OnDisable()
    {
        StopMissionIntro(true);
        UnbindFromGameManager();
    }

    private void LateUpdate()
    {
        if (subscribedManager == GameManager.Instance) return;
        BindToGameManager();
        RefreshFromActiveObjectives();
    }

    public void ShowLevel(int levelNumber, IReadOnlyList<PlanetTier> legacyTargets)
    {
        BindToGameManager();
        RefreshFromActiveObjectives();
    }

    public void MarkAchieved(int legacySlotIndex)
    {
        if (activeCards.Count == 0) RefreshFromActiveObjectives();
    }

    private void BindToGameManager()
    {
        if (subscribedManager == GameManager.Instance) return;
        UnbindFromGameManager();
        subscribedManager = GameManager.Instance;
        if (subscribedManager == null) return;
        subscribedManager.ObjectivesInitialized += HandleObjectivesInitialized;
        subscribedManager.ObjectiveProgressChanged += HandleObjectiveProgressChanged;
        subscribedManager.PreparedLevelStarting += HandlePreparedLevelStarting;
    }

    private void UnbindFromGameManager()
    {
        if (subscribedManager == null) return;
        subscribedManager.ObjectivesInitialized -= HandleObjectivesInitialized;
        subscribedManager.ObjectiveProgressChanged -= HandleObjectiveProgressChanged;
        subscribedManager.PreparedLevelStarting -= HandlePreparedLevelStarting;
        subscribedManager = null;
    }

    private void RefreshFromActiveObjectives()
    {
        GameManager manager = GameManager.Instance;
        if (manager?.ActiveObjectives == null) return;
        var snapshots = new List<LevelObjectiveProgress>();
        foreach (LevelObjective objective in manager.ActiveObjectives)
            if (objective != null) snapshots.Add(objective.Snapshot);
        HandleObjectivesInitialized(snapshots);
    }

    private void HandleObjectivesInitialized(IReadOnlyList<LevelObjectiveProgress> objectives)
    {
        HideAllCards();
        activeCards.Clear();
        if (!runPresentationVisible)
            return;
        int count = Mathf.Min(objectives?.Count ?? 0, missionCards.Count);
        for (int i = 0; i < count; i++)
        {
            MissionCard card = missionCards[i];
            if (card?.root == null) continue;
            card.root.gameObject.SetActive(true);
            activeCards[objectives[i].Index] = card;
            ApplySnapshot(card, objectives[i], false);
        }
    }

    public void SetRunPresentationVisible(bool visible)
    {
        if (!visible)
            StopMissionIntro(true);
        runPresentationVisible = visible;
        if (visible)
            RefreshFromActiveObjectives();
        else
        {
            HideAllCards();
            activeCards.Clear();
        }
    }

    private bool HandlePreparedLevelStarting()
    {
        if (subscribedManager == null ||
            subscribedManager.State != GameManager.GameState.LevelPreparing ||
            missionIntroRoutine != null)
            return false;

        missionIntroRoutine = StartCoroutine(PlayMissionIntro(subscribedManager));
        return true;
    }

    private IEnumerator PlayMissionIntro(GameManager manager)
    {
        runPresentationVisible = true;
        RefreshFromActiveObjectives();
        Canvas.ForceUpdateCanvases();

        var cards = new List<MissionCard>();
        var finalPositions = new List<Vector2>();
        var finalScales = new List<Vector3>();
        var groups = new List<CanvasGroup>();
        foreach (MissionCard card in missionCards)
        {
            if (card?.root == null || !card.root.gameObject.activeSelf) continue;
            cards.Add(card);
            finalPositions.Add(card.root.anchoredPosition);
            finalScales.Add(card.root.localScale);
            card.introFinalPosition = card.root.anchoredPosition;
            card.introFinalScale = card.root.localScale;
            card.introTransformCaptured = true;
            CanvasGroup group = card.root.GetComponent<CanvasGroup>();
            if (group == null) group = card.root.gameObject.AddComponent<CanvasGroup>();
            groups.Add(group);
        }

        introLayout = GetComponent<HorizontalLayoutGroup>();
        if (introLayout != null) introLayout.enabled = false;
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].root.anchoredPosition = finalPositions[i] + missionIntroStartOffset;
            cards[i].root.localScale = finalScales[i] * missionIntroStartScale;
            groups[i].alpha = 0f;
        }

        DebugIntro($"Mission cards: {cards.Count}");
        for (int i = 0; i < cards.Count; i++)
        {
            float elapsed = 0f;
            while (elapsed < missionIntroCardDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / missionIntroCardDuration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                float scaleFactor = t < .72f
                    ? Mathf.Lerp(missionIntroStartScale, missionIntroOvershootScale, t / .72f)
                    : Mathf.Lerp(missionIntroOvershootScale, 1f, (t - .72f) / .28f);
                cards[i].root.anchoredPosition = Vector2.Lerp(
                    finalPositions[i] + missionIntroStartOffset, finalPositions[i], eased);
                cards[i].root.localScale = finalScales[i] * scaleFactor;
                groups[i].alpha = eased;
                yield return null;
            }

            cards[i].root.anchoredPosition = finalPositions[i];
            cards[i].root.localScale = finalScales[i];
            groups[i].alpha = 1f;
            AudioManager.Instance?.PlayUiOneShot(missionCardRevealClip, missionCardRevealVolume);
            DebugIntro($"Card {i + 1} revealed");
            if (i < cards.Count - 1 && missionIntroDelayBetweenCards > 0f)
                yield return WaitUnscaled(missionIntroDelayBetweenCards);
        }

        if (missionIntroFinalDelay > 0f)
            yield return WaitUnscaled(missionIntroFinalDelay);

        if (AfterMissionIntroSequence != null)
            foreach (System.Func<IEnumerator> extension in AfterMissionIntroSequence.GetInvocationList())
            {
                IEnumerator sequence = extension();
                if (sequence != null) yield return sequence;
            }

        RestoreIntroLayout(cards, finalPositions, finalScales, groups);
        missionIntroRoutine = null;
        DebugIntro("Intro complete → Playing");
        if (manager != null && manager.State == GameManager.GameState.LevelPreparing)
            manager.CompletePreparedLevelStart();
    }

    private static IEnumerator WaitUnscaled(float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    private void StopMissionIntro(bool restoreCards)
    {
        if (missionIntroRoutine != null)
        {
            StopCoroutine(missionIntroRoutine);
            missionIntroRoutine = null;
        }
        if (introLayout != null)
        {
            introLayout.enabled = true;
            introLayout = null;
        }
        if (!restoreCards) return;
        foreach (MissionCard card in missionCards)
        {
            if (card?.root == null) continue;
            if (card.introTransformCaptured)
            {
                card.root.anchoredPosition = card.introFinalPosition;
                card.root.localScale = card.introFinalScale;
                card.introTransformCaptured = false;
            }
            CanvasGroup group = card.root.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = 1f;
        }
    }

    private void RestoreIntroLayout(List<MissionCard> cards, List<Vector2> positions,
        List<Vector3> scales, List<CanvasGroup> groups)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            cards[i].root.anchoredPosition = positions[i];
            cards[i].root.localScale = scales[i];
            cards[i].introTransformCaptured = false;
            groups[i].alpha = 1f;
        }
        if (introLayout != null)
        {
            introLayout.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(transform as RectTransform);
            introLayout = null;
        }
    }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
    private void DebugIntro(string message) => Debug.Log($"[PreLevel Intro]\n{message}", this);

    private void HandleObjectiveProgressChanged(LevelObjectiveProgress objective)
    {
        if (!activeCards.TryGetValue(objective.Index, out MissionCard card))
        {
            RefreshFromActiveObjectives();
            activeCards.TryGetValue(objective.Index, out card);
        }
        if (card != null) ApplySnapshot(card, objective, true);
    }

    private void ApplySnapshot(MissionCard card, LevelObjectiveProgress objective, bool animate)
    {
        bool justCompleted = !card.isCompleted && objective.IsCompleted;
        card.isCompleted = objective.IsCompleted;
        FormatObjective(objective, out string title, out string main, out string secondary);
        Sprite visual = ResolveObjectiveVisual(objective);
        if (card.missionTitle != null) card.missionTitle.text = title;
        if (card.missionObjective != null)
        {
            card.missionObjective.text = main;
            card.missionObjective.color = StateColor(objective);
            card.missionObjective.gameObject.SetActive(visual == null);
        }
        if (card.objectiveVisual != null)
        {
            card.objectiveVisual.sprite = visual;
            card.objectiveVisual.gameObject.SetActive(visual != null);
            card.objectiveVisual.color = Color.white;
        }
        if (card.objectiveSecondary != null)
        {
            card.objectiveSecondary.text = secondary;
            card.objectiveSecondary.gameObject.SetActive(!string.IsNullOrEmpty(secondary));
            card.objectiveSecondary.color = StateColor(objective);
        }
        if (card.completed != null)
        {
            card.completed.color = achievedTint;
            card.completed.gameObject.SetActive(objective.IsCompleted);
        }
        if (animate && justCompleted && completionPopDuration > 0f)
        {
            if (card.popRoutine != null) StopCoroutine(card.popRoutine);
            card.popRoutine = StartCoroutine(Pop(card));
        }
    }

    private Color StateColor(LevelObjectiveProgress objective) => objective.IsCompleted
        ? achievedTint : objective.IsRequired ? pendingTint : optionalTint;

    private IEnumerator Pop(MissionCard card)
    {
        float elapsed = 0f;
        while (elapsed < completionPopDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / completionPopDuration);
            card.root.localScale = Vector3.one * Mathf.Lerp(completionPopScale, 1f, t);
            yield return null;
        }
        card.root.localScale = Vector3.one;
        card.popRoutine = null;
    }

    private void ApplyPresentationOnce()
    {
        HorizontalLayoutGroup layout = GetComponent<HorizontalLayoutGroup>();
        if (layout != null) layout.spacing = missionCardSpacing;
        foreach (MissionCard card in missionCards)
        {
            if (card?.root != null) card.root.sizeDelta = missionCardSize;
            if (card?.background != null)
            {
                if (missionCardSprite != null) card.background.sprite = missionCardSprite;
                card.background.preserveAspect = true;
                card.background.raycastTarget = false;
            }
            ConfigureText(card?.missionTitle, missionTitleFontSize);
            ConfigureText(card?.missionObjective, missionObjectiveFontSize);
            ConfigureText(card?.objectiveSecondary, missionSecondaryFontSize);
            if (card?.objectiveVisual != null)
            {
                card.objectiveVisual.rectTransform.sizeDelta = missionVisualSize;
                card.objectiveVisual.preserveAspect = true;
                card.objectiveVisual.raycastTarget = false;
            }
        }
    }

    private static void ConfigureText(TextMeshProUGUI text, float size)
    {
        if (text == null) return;
        text.fontSize = size;
        text.enableAutoSizing = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
    }

    private void HideAllCards()
    {
        foreach (MissionCard card in missionCards)
        {
            if (card?.popRoutine != null) StopCoroutine(card.popRoutine);
            if (card?.root != null)
            {
                card.root.localScale = Vector3.one;
                card.root.gameObject.SetActive(false);
            }
            if (card != null) { card.isCompleted = false; card.popRoutine = null; }
        }
    }

    private static void FormatObjective(LevelObjectiveProgress objective, out string title,
        out string main, out string secondary)
    {
        secondary = string.Empty;
        switch (objective.Type)
        {
            case LevelObjectiveType.ReachTier:
                title = "REACH"; main = $"TIER {(int)objective.TargetTier + 1}"; return;
            case LevelObjectiveType.MergeCount:
                title = "MERGE"; main = $"{Whole(objective.CurrentProgress)}/{Whole(objective.TargetProgress)}"; return;
            case LevelObjectiveType.ComboTarget:
                title = "COMBO"; main = $"x{Whole(objective.TargetProgress)}"; return;
            case LevelObjectiveType.MeteorObjective:
                title = "METEOR"; main = $"{Whole(objective.CurrentProgress)}/{Whole(objective.TargetProgress)}"; return;
            case LevelObjectiveType.Survival:
                title = "SURVIVE"; main = $"{Whole(objective.CurrentProgress)}/{Whole(objective.TargetProgress)}s"; return;
            default:
                title = "MISSION"; main = $"{Whole(objective.CurrentProgress)}/{Whole(objective.TargetProgress)}"; return;
        }
    }

    private Sprite ResolveObjectiveVisual(LevelObjectiveProgress objective)
    {
        if (objective.Type == LevelObjectiveType.ReachTier)
            return planetPrefab != null ? planetPrefab.GetSpriteForTier(objective.TargetTier) : null;
        if (objective.Type == LevelObjectiveType.ComboTarget)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(objective.TargetProgress), 1, 5) - 1;
            return comboTargetSprites != null && index < comboTargetSprites.Length
                ? comboTargetSprites[index] : null;
        }
        return null;
    }

    private static string Whole(float value) => Mathf.FloorToInt(value + .001f).ToString();
}
