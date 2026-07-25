using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation-only READY -> Playing coordinator. Existing systems retain
// ownership of gameplay state, the clock, mission cards and effect icons.
public sealed class LevelStartSequence : MonoBehaviour
{
    [Header("Existing Systems")]
    [SerializeField] private MissionHUD missionHud;
    [SerializeField] private ActiveEffectsHUD activeEffectsHud;
    [SerializeField] private RectTransform timerHudTarget;
    [SerializeField] private CanvasGroup timerHudGroup;
    [SerializeField] private CanvasGroup timerHudTextGroup;

    [Header("Authored Cinematic UI")]
    [SerializeField] private TextMeshProUGUI levelIntroText;
    [SerializeField] private RectTransform timeRushModeRoot;
    [SerializeField] private CanvasGroup timeRushModeGroup;
    [SerializeField] private TextMeshProUGUI timeRushTitleText;
    [SerializeField] private TextMeshProUGUI timeRushDescriptionText;
    [SerializeField] private GameObject timeRushEdgeEffect;
    [SerializeField] private CanvasGroup timeRushEdgeGroup;
    [SerializeField] private Image[] timeRushEdgeImages;
    [SerializeField] private TextMeshProUGUI timeIntroText;
    [SerializeField] private TextMeshProUGUI countdownText;

    [Header("LEVEL")]
    [SerializeField, Min(.01f)] private float levelIntroDuration = .22f;
    [SerializeField, Min(0f)] private float levelHoldDuration = .55f;
    [SerializeField, Min(.01f)] private float levelOutroDuration = .18f;
    [SerializeField, Range(.05f, 1f)] private float levelStartScale = .65f;
    [SerializeField, Range(1f, 1.5f)] private float levelPunchScale = 1.12f;

    [Header("TIME RUSH MODE INTRO")]
    [SerializeField, Min(.01f)] private float timeRushIntroDuration = .25f;
    [SerializeField, Min(0f)] private float timeRushHoldDuration = .85f;
    [SerializeField, Min(.01f)] private float timeRushOutroDuration = .25f;
    [SerializeField, Range(.05f, 1f)] private float timeRushStartScale = .72f;
    [SerializeField, Range(1f, 1.5f)] private float timeRushPunchScale = 1.08f;
    [SerializeField] private Color timeRushEdgeColor = new Color(.2f, .75f, 1f, 1f);
    [SerializeField, Range(0f, 1f)] private float edgeMinimumAlpha = .08f;
    [SerializeField, Range(0f, 1f)] private float edgeMaximumAlpha = .28f;
    [SerializeField, Min(0f)] private float edgePulseSpeed = 2.2f;
    [Tooltip("Pushes the four edge strips outward so they sit under the phone bezel instead of visibly inside the play area.")]
    [SerializeField, Min(0f)] private float edgeOutwardOffset = 10f;
    [SerializeField, Min(.01f)] private float edgeFadeInDuration = .2f;
    [SerializeField, Min(.01f)] private float edgeFadeOutDuration = .25f;

    [Header("TIME -> HUD")]
    [SerializeField, Min(.01f)] private float timeIntroDuration = .22f;
    [SerializeField, Min(0f)] private float timeHoldDuration = .65f;
    [SerializeField, Min(.01f)] private float timeTravelDuration = .5f;
    [SerializeField] private AnimationCurve timeTravelEasing =
        AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField, Range(.1f, 1.5f)] private float timeTravelEndScale = .55f;
    [SerializeField, Range(0f, 1f)] private float timerHudFadeStart = .55f;

    [Header("3 / 2 / 1 / GO")]
    [SerializeField, Min(.01f)] private float countdownItemDuration = .48f;
    [SerializeField, Min(.01f)] private float goDuration = .58f;
    [SerializeField, Range(.05f, 1f)] private float countdownStartScale = .55f;
    [SerializeField, Range(1f, 1.6f)] private float countdownPunchScale = 1.2f;

    [Header("Cinematic SFX")]
    [SerializeField] private AudioClip introRevealClip;
    [SerializeField] private AudioClip timeRushRevealClip;
    [SerializeField] private AudioClip countdownTickClip;
    [SerializeField] private AudioClip countdownGoClip;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = .9f;

    private Coroutine routine;
    private GameManager manager;
    private int generation;
    private bool completed;
    private GameManager.GameState lastState;
    private Vector2 authoredTimePosition;
    private Vector3 authoredTimeScale;
    private bool rushEdgeRunning;
    private float rushEdgeElapsed;

    // Extension seam for a future first-time demonstration. No tutorial
    // state, objects or persistence are introduced by this implementation.
    public event System.Func<GameManager, IEnumerator> AfterTimeRushModeIntro;

    private void Awake()
    {
        EnsureRuntimeGroups();
        ApplyEdgeOutwardOffset();
        CaptureAuthoredTimeTransform();
        HideTexts();
        SetTimerHudVisible(false);
    }

    private void OnEnable() => Bind();
    private void Start() => Bind();

    private void EnsureRuntimeGroups()
    {
        if (timerHudGroup == null && timerHudTarget != null &&
            timerHudTarget.parent != null)
            timerHudGroup = GetOrAddGroup(timerHudTarget.parent.gameObject);
        if (timerHudTextGroup == null && timerHudTarget != null)
            timerHudTextGroup = GetOrAddGroup(timerHudTarget.gameObject);
        // The authored frame/background/icon remain visible throughout the
        // cinematic; only the numeric TimerText participates in the handoff.
        if (timerHudGroup != null)
            timerHudGroup.alpha = 1f;
        if (timeRushModeGroup == null && timeRushModeRoot != null)
            timeRushModeGroup = GetOrAddGroup(timeRushModeRoot.gameObject);
        if (timeRushEdgeGroup == null && timeRushEdgeEffect != null)
            timeRushEdgeGroup = GetOrAddGroup(timeRushEdgeEffect);
    }

    private void ApplyEdgeOutwardOffset()
    {
        if (timeRushEdgeImages == null || timeRushEdgeImages.Length < 4)
            return;
        Vector2[] directions = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        for (int i = 0; i < 4; i++)
        {
            Image edge = timeRushEdgeImages[i];
            if (edge == null) continue;
            RectTransform rect = edge.rectTransform;
            float thickness = i < 2 ? rect.rect.width : rect.rect.height;
            // Never push a thin authored strip completely outside the screen.
            float safeOffset = Mathf.Min(edgeOutwardOffset, thickness * .45f);
            rect.anchoredPosition += directions[i] * safeOffset;
        }
    }

    private void LateUpdate()
    {
        if (manager != GameManager.Instance) Bind();
        if (manager != null && manager.State == GameManager.GameState.LevelPreparing &&
            lastState != GameManager.GameState.LevelPreparing)
        {
            completed = false;
            HideTimeRushEdge();
            SetTimerHudVisible(false);
        }
        if (manager != null && manager.State == GameManager.GameState.LevelPreparing &&
            routine == null && !completed)
            SetTimerHudVisible(false);
        if (routine != null && (manager == null ||
            manager.State != GameManager.GameState.LevelPreparing))
            Cancel();
        UpdateRushEdge();
        if (manager != null) lastState = manager.State;
    }

    private void OnDisable()
    {
        Unbind();
        Cancel();
    }

    private void Bind()
    {
        if (manager == GameManager.Instance) return;
        Unbind();
        manager = GameManager.Instance;
        if (manager != null)
        {
            manager.PreparedLevelStarting += Begin;
            lastState = manager.State;
        }
    }

    private void Unbind()
    {
        if (manager != null) manager.PreparedLevelStarting -= Begin;
        manager = null;
    }

    private bool Begin()
    {
        if (manager == null || manager.State != GameManager.GameState.LevelPreparing ||
            routine != null)
            return false;
        completed = false;
        int token = ++generation;
        routine = StartCoroutine(Play(manager, token));
        return true;
    }

    private IEnumerator Play(GameManager run, int token)
    {
        SetTimerHudVisible(false);
        int number = run.ActiveLevelConfiguration != null
            ? run.ActiveLevelConfiguration.levelNumber : run.CurrentLevelNumber;
        if (levelIntroText != null)
            levelIntroText.text = Localization.Get("prelevel.level", number);
        AudioManager.Instance?.PlayUiOneShot(introRevealClip, sfxVolume);
        yield return Present(levelIntroText, levelIntroDuration, levelHoldDuration,
            levelOutroDuration, levelStartScale, levelPunchScale, true, run, token);
        if (!Current(run, token)) yield break;

        bool rush = run.ActiveLevelConfiguration != null &&
            run.ActiveLevelConfiguration.timeMode == LevelTimeMode.MergeTimeRush;
        if (rush)
        {
            yield return PresentTimeRushMode(run, token);
            if (!Current(run, token)) yield break;
            if (AfterTimeRushModeIntro != null)
                foreach (System.Func<GameManager, IEnumerator> extension in
                    AfterTimeRushModeIntro.GetInvocationList())
                {
                    IEnumerator phase = extension(run);
                    if (phase != null) yield return phase;
                    if (!Current(run, token)) yield break;
                }
        }
        if (timeIntroText != null)
            timeIntroText.text = Localization.Get("prelevel.time",
                Mathf.CeilToInt(run.RemainingTime));
        AudioManager.Instance?.PlayUiOneShot(introRevealClip, sfxVolume);
        yield return Present(timeIntroText, timeIntroDuration, timeHoldDuration,
            0f, levelStartScale, levelPunchScale, false, run, token);
        if (!Current(run, token)) yield break;
        yield return HandoffTime(run, token);
        if (!Current(run, token)) yield break;

        if (missionHud != null) yield return missionHud.PlayPreparedIntro(run);
        if (!Current(run, token)) yield break;
        if (activeEffectsHud != null) yield return activeEffectsHud.RevealPreparedEffects();
        if (!Current(run, token)) yield break;

        foreach (string value in new[] { "3", "2", "1" })
        {
            AudioManager.Instance?.PlayUiOneShot(countdownTickClip, sfxVolume);
            yield return Countdown(value, countdownItemDuration, run, token);
            if (!Current(run, token)) yield break;
        }
        AudioManager.Instance?.PlayUiOneShot(countdownGoClip, sfxVolume);
        yield return Countdown(Localization.Get("level_start.go"), goDuration, run, token);
        if (!Current(run, token)) yield break;

        completed = true;
        routine = null;
        run.CompletePreparedLevelStart();
    }

    private IEnumerator PresentTimeRushMode(GameManager run, int token)
    {
        if (timeRushModeRoot == null || timeRushModeGroup == null)
            yield break;

        if (timeRushTitleText != null)
            timeRushTitleText.text = Localization.Get("mode.time_rush.title");
        if (timeRushDescriptionText != null)
            timeRushDescriptionText.text = Localization.Get("mode.time_rush.description");
        foreach (Image edge in timeRushEdgeImages)
            if (edge != null)
            {
                Color color = timeRushEdgeColor;
                color.a = 1f;
                edge.color = color;
                edge.raycastTarget = false;
            }

        Vector3 authoredScale = timeRushModeRoot.localScale;
        timeRushModeRoot.gameObject.SetActive(true);
        StartTimeRushEdge();
        timeRushModeGroup.alpha = 0f;
        AudioManager.Instance?.PlayUiOneShot(timeRushRevealClip, sfxVolume);

        float total = timeRushIntroDuration + timeRushHoldDuration + timeRushOutroDuration;
        float elapsed = 0f;
        while (elapsed < total && Current(run, token))
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < timeRushIntroDuration)
            {
                float t = Mathf.Clamp01(elapsed / timeRushIntroDuration);
                timeRushModeGroup.alpha = EaseOut(t);
                float scale = t < .7f
                    ? Mathf.Lerp(timeRushStartScale, timeRushPunchScale, EaseOut(t / .7f))
                    : Mathf.Lerp(timeRushPunchScale, 1f, Smooth((t - .7f) / .3f));
                timeRushModeRoot.localScale = authoredScale * scale;
            }
            else if (elapsed > total - timeRushOutroDuration)
            {
                float t = Mathf.Clamp01((elapsed - (total - timeRushOutroDuration)) /
                    timeRushOutroDuration);
                timeRushModeGroup.alpha = 1f - Smooth(t);
                timeRushModeRoot.localScale = authoredScale * Mathf.Lerp(1f, .94f, Smooth(t));
            }
            else
            {
                timeRushModeGroup.alpha = 1f;
                timeRushModeRoot.localScale = authoredScale;
            }

            yield return null;
        }

        timeRushModeRoot.localScale = authoredScale;
        timeRushModeGroup.alpha = 0f;
        timeRushModeRoot.gameObject.SetActive(false);
    }

    private IEnumerator Present(TextMeshProUGUI label, float intro, float hold,
        float outro, float startScale, float punch, bool hide, GameManager run, int token)
    {
        if (label == null) yield break;
        RectTransform rect = label.rectTransform;
        Vector3 authored = rect.localScale;
        CanvasGroup group = Group(label);
        label.gameObject.SetActive(true);
        for (float elapsed = 0f; elapsed < intro && Current(run, token);
            elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / intro);
            group.alpha = EaseOut(t);
            float scale = t < .7f ? Mathf.Lerp(startScale, punch, EaseOut(t / .7f))
                : Mathf.Lerp(punch, 1f, Smooth((t - .7f) / .3f));
            rect.localScale = authored * scale;
            yield return null;
        }
        group.alpha = 1f;
        rect.localScale = authored;
        yield return Wait(hold, run, token);
        for (float elapsed = 0f; elapsed < outro && Current(run, token);
            elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / outro);
            group.alpha = 1f - Smooth(t);
            rect.localScale = authored * Mathf.Lerp(1f, .85f, Smooth(t));
            yield return null;
        }
        rect.localScale = authored;
        if (hide) label.gameObject.SetActive(false);
    }

    private IEnumerator HandoffTime(GameManager run, int token)
    {
        if (timeIntroText == null || timerHudTarget == null)
        {
            SetTimerHudVisible(true);
            if (timeIntroText != null) timeIntroText.gameObject.SetActive(false);
            yield break;
        }
        RectTransform source = timeIntroText.rectTransform;
        RectTransform parent = source.parent as RectTransform;
        Vector2 destination = source.anchoredPosition;
        if (parent != null)
        {
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, timerHudTarget.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, null, out destination);
        }
        CanvasGroup sourceGroup = Group(timeIntroText);
        Vector2 start = source.anchoredPosition;
        Vector3 scale = source.localScale;
        for (float elapsed = 0f; elapsed < timeTravelDuration && Current(run, token);
            elapsed += Time.unscaledDeltaTime)
        {
            float raw = Mathf.Clamp01(elapsed / timeTravelDuration);
            float t = timeTravelEasing != null ? timeTravelEasing.Evaluate(raw) : Smooth(raw);
            source.anchoredPosition = Vector2.LerpUnclamped(start, destination, t);
            source.localScale = scale * Mathf.Lerp(1f, timeTravelEndScale, t);
            float crossFade = Mathf.Clamp01((raw - timerHudFadeStart) /
                Mathf.Max(.001f, 1f - timerHudFadeStart));
            sourceGroup.alpha = 1f - crossFade;
            if (timerHudTextGroup != null) timerHudTextGroup.alpha = crossFade;
            yield return null;
        }
        RestoreTimeTransform();
        timeIntroText.gameObject.SetActive(false);
        SetTimerHudVisible(true);
    }

    private IEnumerator Countdown(string value, float duration, GameManager run, int token)
    {
        if (countdownText == null) yield break;
        countdownText.text = value;
        countdownText.gameObject.SetActive(true);
        RectTransform rect = countdownText.rectTransform;
        Vector3 authored = rect.localScale;
        CanvasGroup group = Group(countdownText);
        for (float elapsed = 0f; elapsed < duration && Current(run, token);
            elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            rect.localScale = authored * (t < .3f
                ? Mathf.Lerp(countdownStartScale, countdownPunchScale, EaseOut(t / .3f))
                : Mathf.Lerp(countdownPunchScale, 1f, Smooth((t - .3f) / .7f)));
            group.alpha = t < .72f ? 1f : 1f - Smooth((t - .72f) / .28f);
            yield return null;
        }
        rect.localScale = authored;
        countdownText.gameObject.SetActive(false);
    }

    private IEnumerator Wait(float duration, GameManager run, int token)
    {
        for (float elapsed = 0f; elapsed < duration && Current(run, token);
            elapsed += Time.unscaledDeltaTime)
            yield return null;
    }

    private bool Current(GameManager run, int token) =>
        isActiveAndEnabled && token == generation && run == GameManager.Instance &&
        run.State == GameManager.GameState.LevelPreparing;

    private void Cancel()
    {
        generation++;
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        missionHud?.ResetPreparedIntro();
        activeEffectsHud?.ResetPreparedIntro();
        RestoreTimeTransform();
        HideTexts();
        completed = false;
    }

    private void CaptureAuthoredTimeTransform()
    {
        if (timeIntroText == null) return;
        authoredTimePosition = timeIntroText.rectTransform.anchoredPosition;
        authoredTimeScale = timeIntroText.rectTransform.localScale;
    }

    private void RestoreTimeTransform()
    {
        if (timeIntroText == null) return;
        timeIntroText.rectTransform.anchoredPosition = authoredTimePosition;
        timeIntroText.rectTransform.localScale = authoredTimeScale;
        Group(timeIntroText).alpha = 1f;
    }

    private void HideTexts()
    {
        if (levelIntroText != null) levelIntroText.gameObject.SetActive(false);
        if (timeIntroText != null) timeIntroText.gameObject.SetActive(false);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (timeRushModeRoot != null) timeRushModeRoot.gameObject.SetActive(false);
        HideTimeRushEdge();
    }

    private void HideTimeRushEdge()
    {
        rushEdgeRunning = false;
        rushEdgeElapsed = 0f;
        if (timeRushEdgeGroup != null) timeRushEdgeGroup.alpha = 0f;
        if (timeRushEdgeEffect != null) timeRushEdgeEffect.SetActive(false);
    }

    private void StartTimeRushEdge()
    {
        rushEdgeRunning = true;
        rushEdgeElapsed = 0f;
        if (timeRushEdgeGroup != null) timeRushEdgeGroup.alpha = 0f;
        if (timeRushEdgeEffect != null) timeRushEdgeEffect.SetActive(true);
    }

    private void UpdateRushEdge()
    {
        if (!rushEdgeRunning || timeRushEdgeGroup == null)
            return;

        bool runStillActive = manager != null &&
            (manager.State == GameManager.GameState.LevelPreparing ||
             manager.State == GameManager.GameState.Playing ||
             manager.State == GameManager.GameState.InventoryPaused ||
             manager.State == GameManager.GameState.GamePaused);
        if (!runStillActive)
        {
            timeRushEdgeGroup.alpha = Mathf.MoveTowards(timeRushEdgeGroup.alpha, 0f,
                Time.unscaledDeltaTime / Mathf.Max(.01f, edgeFadeOutDuration));
            if (timeRushEdgeGroup.alpha <= .001f)
                HideTimeRushEdge();
            return;
        }

        rushEdgeElapsed += Time.unscaledDeltaTime;
        float fadeIn = Mathf.Clamp01(rushEdgeElapsed / Mathf.Max(.01f, edgeFadeInDuration));
        float pulse = .5f + .5f * Mathf.Sin(
            rushEdgeElapsed * edgePulseSpeed * Mathf.PI * 2f);
        float alpha = Mathf.Lerp(edgeMinimumAlpha, edgeMaximumAlpha, pulse);
        timeRushEdgeGroup.alpha = alpha * Smooth(fadeIn);
    }

    private void SetTimerHudVisible(bool visible)
    {
        if (timerHudGroup != null)
        {
            timerHudGroup.alpha = 1f;
            timerHudGroup.interactable = false;
            timerHudGroup.blocksRaycasts = false;
        }
        if (timerHudTextGroup == null) return;
        timerHudTextGroup.alpha = visible ? 1f : 0f;
        timerHudTextGroup.interactable = false;
        timerHudTextGroup.blocksRaycasts = false;
    }

    private static CanvasGroup Group(TMP_Text text)
    {
        return GetOrAddGroup(text.gameObject);
    }

    private static CanvasGroup GetOrAddGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }

    private static float EaseOut(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
