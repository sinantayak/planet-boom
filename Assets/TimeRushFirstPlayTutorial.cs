using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation-only phase attached to LevelStartSequence's existing
// AfterTimeRushModeIntro seam. It never creates board objects or changes time.
public sealed class TimeRushFirstPlayTutorial : MonoBehaviour
{
    private const string TutorialId = "tutorial:merge_time_rush";

    [Header("Existing Systems")]
    [SerializeField] private LevelStartSequence levelStartSequence;
    [SerializeField] private Planet planetSpriteSource;

    [Header("Authored Tutorial UI")]
    [SerializeField] private RectTransform tutorialRoot;
    [SerializeField] private CanvasGroup tutorialGroup;
    [SerializeField] private Image planetLeft;
    [SerializeField] private Image planetRight;
    [SerializeField] private Image resultPlanet;
    // Retained only to safely hide the previously-authored flash object in
    // scenes that already ran the older authoring command.
    [SerializeField, HideInInspector] private Image mergeFlash;
    [SerializeField] private TextMeshProUGUI timeBonusText;
    // Retained only to hide the old presentation-only "10 → 11" object in
    // scenes authored before that part of the tutorial was removed.
    [SerializeField, HideInInspector] private TextMeshProUGUI demoTimerText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Button tapCatcher;
    [SerializeField] private TextMeshProUGUI tapToContinueText;

    [Header("Animation")]
    [SerializeField, Min(0f)] private float introDuration = .18f;
    [SerializeField, Min(0f)] private float mergeApproachDuration = .55f;
    [SerializeField, Min(0f)] private float mergeDistance = 250f;
    [SerializeField, Min(0f)] private float fusionDuration = .32f;
    [Tooltip("Gameplay merge gibi sonuç gezegenini birleşme ekseninde kısa süre genişletir.")]
    [SerializeField, Range(1f, 1.8f)] private float fusionStretch = 1.28f;
    [SerializeField, Range(.4f, 1f)] private float fusionSquash = .82f;
    [SerializeField, Min(0f)] private float timeGainDuration = .28f;
    [SerializeField, Min(0f)] private float loopHoldDuration = .85f;
    [SerializeField, Min(0f)] private float loopRestartDelay = .2f;
    [SerializeField, Min(0f)] private float outroDuration = .2f;

    [Header("Optional Feedback")]
    [SerializeField] private AudioClip tutorialMergeClip;
    [SerializeField] private AudioClip tutorialTimeGainClip;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = .9f;
    [SerializeField] private bool lightHapticOnMerge = true;

    private Vector2 leftAuthoredPosition;
    private Vector2 rightAuthoredPosition;
    private Vector3 leftAuthoredScale;
    private Vector3 rightAuthoredScale;
    private Vector3 resultAuthoredScale;
    private bool subscribed;
    private GameManager activeRun;
    private int activeSequenceToken = -1;
    private bool continueRequested;

    private void Awake()
    {
        CaptureAuthoredState();
        ConfigureNonInteractiveUi();
        if (tapCatcher != null)
            tapCatcher.onClick.AddListener(RequestContinue);
        ResetVisuals();
    }

    private void OnEnable() => Subscribe();
    private void Start() => Subscribe();

    private void LateUpdate()
    {
        if (!subscribed)
            Subscribe();
        if (tutorialRoot != null && tutorialRoot.gameObject.activeSelf &&
            (activeRun == null || activeRun != GameManager.Instance ||
             activeRun.State != GameManager.GameState.LevelPreparing))
            ResetVisuals();
    }

    private void OnDisable()
    {
        Unsubscribe();
        ResetVisuals();
    }

    private void OnDestroy()
    {
        if (tapCatcher != null)
            tapCatcher.onClick.RemoveListener(RequestContinue);
    }

    private void Subscribe()
    {
        if (subscribed || levelStartSequence == null)
            return;
        levelStartSequence.AfterTimeRushModeIntro += PlayIfNeeded;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || levelStartSequence == null)
            return;
        levelStartSequence.AfterTimeRushModeIntro -= PlayIfNeeded;
        subscribed = false;
    }

    private IEnumerator PlayIfNeeded(GameManager run)
    {
        LevelConfiguration config = run != null ? run.ActiveLevelConfiguration : null;
        PlayerDataPersistenceManager persistence = PlayerDataPersistenceManager.Instance;
        activeSequenceToken = levelStartSequence != null
            ? levelStartSequence.PreparationToken : -1;
        if (config == null || config.timeMode != LevelTimeMode.MergeTimeRush ||
            persistence == null || !HasRequiredUi() || !StillPreparing(run))
            yield break;

        while (!persistence.IsLoaded &&
               persistence.State != PlayerDataPersistenceManager.PlayerDataState.Failed &&
               StillPreparing(run))
            yield return null;
        if (!StillPreparing(run) || !persistence.IsLoaded ||
            persistence.IsTutorialCompleted(TutorialId))
            yield break;

        activeRun = run;
        ConfigurePresentation(config, out float bonus);
        ShowInitialState();

        yield return Fade(0f, 1f, introDuration, run);
        if (!StillPreparing(run)) yield break;

        int cycle = 0;
        while (!continueRequested && StillPreparing(run))
        {
            PrepareDemoCycle();
            yield return PlayDemoCycle(run, cycle++ == 0);
            if (!StillPreparing(run) || continueRequested)
                break;
            yield return WaitForLoop(loopHoldDuration, run);
            if (!StillPreparing(run) || continueRequested)
                break;
            yield return WaitForLoop(loopRestartDelay, run);
        }
        if (!StillPreparing(run)) yield break;
        yield return Fade(1f, 0f, outroDuration, run);
        if (!StillPreparing(run)) yield break;

        // Persist only after the complete presentation and while this prepared
        // run is still valid. The existing manager handles cache/cloud saving.
        persistence = PlayerDataPersistenceManager.Instance;
        if (persistence != null)
            persistence.CompleteTutorial(TutorialId);
        ResetVisuals();
    }

    private void ConfigurePresentation(LevelConfiguration config, out float bonus)
    {
        bonus = 1f;
        PlanetTier resultTier = PlanetTier.Tier2;
        if (config.mergeTimeRewards != null)
            for (int i = 0; i < config.mergeTimeRewards.Count; i++)
            {
                MergeTimeRewardEntry entry = config.mergeTimeRewards[i];
                if (entry == null || entry.bonusSeconds <= 0f ||
                    float.IsNaN(entry.bonusSeconds) || float.IsInfinity(entry.bonusSeconds))
                    continue;
                resultTier = entry.resultTier;
                bonus = entry.bonusSeconds;
                break;
            }

        int inputIndex = Mathf.Max(0, (int)resultTier - 1);
        Sprite inputSprite = planetSpriteSource != null
            ? planetSpriteSource.GetSpriteForTier((PlanetTier)inputIndex) : null;
        Sprite resultSprite = planetSpriteSource != null
            ? planetSpriteSource.GetSpriteForTier(resultTier) : null;
        planetLeft.sprite = inputSprite;
        planetRight.sprite = inputSprite;
        resultPlanet.sprite = resultSprite != null ? resultSprite : inputSprite;

        string formattedBonus = Mathf.Approximately(bonus, Mathf.Round(bonus))
            ? Mathf.RoundToInt(bonus).ToString() : bonus.ToString("0.#");
        timeBonusText.text = Localization.Get("tutorial.time_bonus", formattedBonus);
        descriptionText.richText = true;
        descriptionText.text = Localization.Get("tutorial.time_rush.description");
        tapToContinueText.text = Localization.Get("tutorial.tap_to_continue");
    }

    private void ShowInitialState()
    {
        RestoreChildTransforms();
        tutorialRoot.gameObject.SetActive(true);
        tutorialGroup.alpha = 0f;
        tutorialGroup.interactable = false;
        tutorialGroup.blocksRaycasts = false;
        continueRequested = false;
        if (tapCatcher != null)
        {
            tapCatcher.gameObject.SetActive(true);
            tapCatcher.interactable = true;
        }
        tapToContinueText.gameObject.SetActive(true);
        PrepareDemoCycle();
        descriptionText.gameObject.SetActive(true);
    }

    private void PrepareDemoCycle()
    {
        planetLeft.gameObject.SetActive(true);
        planetRight.gameObject.SetActive(true);
        resultPlanet.gameObject.SetActive(false);
        if (mergeFlash != null)
            mergeFlash.gameObject.SetActive(false);
        timeBonusText.gameObject.SetActive(false);
        if (demoTimerText != null)
            demoTimerText.gameObject.SetActive(false);
        RestoreChildTransforms();
    }

    private IEnumerator PlayDemoCycle(GameManager run, bool allowHaptic)
    {
        Vector2 leftTarget = Vector2.MoveTowards(leftAuthoredPosition,
            rightAuthoredPosition, mergeDistance);
        Vector2 rightTarget = Vector2.MoveTowards(rightAuthoredPosition,
            leftAuthoredPosition, mergeDistance);
        for (float elapsed = 0f;
            elapsed < mergeApproachDuration && ContinueLoop(run);
            elapsed += Time.unscaledDeltaTime)
        {
            float t = Smooth(elapsed / Mathf.Max(.001f, mergeApproachDuration));
            planetLeft.rectTransform.anchoredPosition =
                Vector2.LerpUnclamped(leftAuthoredPosition, leftTarget, t);
            planetRight.rectTransform.anchoredPosition =
                Vector2.LerpUnclamped(rightAuthoredPosition, rightTarget, t);
            yield return null;
        }
        if (!ContinueLoop(run)) yield break;

        resultPlanet.gameObject.SetActive(true);
        AudioManager.Instance?.PlayUiOneShot(tutorialMergeClip, sfxVolume);
        if (allowHaptic && lightHapticOnMerge)
            HapticFeedback.Play(HapticType.Light);

        for (float elapsed = 0f; elapsed < fusionDuration && ContinueLoop(run);
            elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.001f, fusionDuration));
            float eased = Smooth(t);
            planetLeft.rectTransform.localScale =
                leftAuthoredScale * Mathf.Lerp(1f, 0f, eased);
            planetRight.rectTransform.localScale =
                rightAuthoredScale * Mathf.Lerp(1f, 0f, eased);
            resultPlanet.rectTransform.localScale = Vector3.Scale(resultAuthoredScale,
                new Vector3(
                    Mathf.Lerp(fusionStretch, 1f, eased),
                    Mathf.Lerp(fusionSquash, 1f, eased),
                    1f));
            yield return null;
        }
        if (!ContinueLoop(run)) yield break;

        planetLeft.gameObject.SetActive(false);
        planetRight.gameObject.SetActive(false);
        resultPlanet.rectTransform.localScale = resultAuthoredScale;
        timeBonusText.gameObject.SetActive(true);
        AudioManager.Instance?.PlayUiOneShot(tutorialTimeGainClip, sfxVolume);
        yield return PopTimeFeedback(run);
    }

    private IEnumerator PopTimeFeedback(GameManager run)
    {
        RectTransform bonusRect = timeBonusText.rectTransform;
        Vector3 bonusScale = bonusRect.localScale;
        for (float elapsed = 0f; elapsed < timeGainDuration && ContinueLoop(run);
            elapsed += Time.unscaledDeltaTime)
        {
            float t = Mathf.Clamp01(elapsed / Mathf.Max(.001f, timeGainDuration));
            float pop = t < .55f
                ? Mathf.Lerp(.55f, 1.18f, EaseOut(t / .55f))
                : Mathf.Lerp(1.18f, 1f, Smooth((t - .55f) / .45f));
            bonusRect.localScale = bonusScale * pop;
            yield return null;
        }
        bonusRect.localScale = bonusScale;
    }

    private IEnumerator Fade(float from, float to, float duration, GameManager run)
    {
        for (float elapsed = 0f; elapsed < duration && StillPreparing(run);
            elapsed += Time.unscaledDeltaTime)
        {
            tutorialGroup.alpha = Mathf.Lerp(from, to,
                Smooth(elapsed / Mathf.Max(.001f, duration)));
            yield return null;
        }
        if (StillPreparing(run))
            tutorialGroup.alpha = to;
    }

    private IEnumerator Wait(float duration, GameManager run)
    {
        for (float elapsed = 0f; elapsed < duration && StillPreparing(run);
            elapsed += Time.unscaledDeltaTime)
            yield return null;
    }

    private IEnumerator WaitForLoop(float duration, GameManager run)
    {
        for (float elapsed = 0f; elapsed < duration && ContinueLoop(run);
            elapsed += Time.unscaledDeltaTime)
            yield return null;
    }

    private bool ContinueLoop(GameManager run) =>
        !continueRequested && StillPreparing(run);

    private void RequestContinue()
    {
        if (activeRun == null || !StillPreparing(activeRun))
            return;
        continueRequested = true;
        if (tapCatcher != null)
            tapCatcher.interactable = false;
    }

    private bool StillPreparing(GameManager run) =>
        isActiveAndEnabled && run != null && run == GameManager.Instance &&
        run.State == GameManager.GameState.LevelPreparing &&
        levelStartSequence != null &&
        levelStartSequence.PreparationToken == activeSequenceToken;

    private bool HasRequiredUi() =>
        tutorialRoot != null && tutorialGroup != null && planetLeft != null &&
        planetRight != null && resultPlanet != null &&
        timeBonusText != null && descriptionText != null &&
        tapCatcher != null && tapToContinueText != null;

    private void CaptureAuthoredState()
    {
        if (planetLeft != null)
        {
            leftAuthoredPosition = planetLeft.rectTransform.anchoredPosition;
            leftAuthoredScale = planetLeft.rectTransform.localScale;
        }
        if (planetRight != null)
        {
            rightAuthoredPosition = planetRight.rectTransform.anchoredPosition;
            rightAuthoredScale = planetRight.rectTransform.localScale;
        }
        if (resultPlanet != null)
            resultAuthoredScale = resultPlanet.rectTransform.localScale;
    }

    private void RestoreChildTransforms()
    {
        if (planetLeft != null)
        {
            planetLeft.rectTransform.anchoredPosition = leftAuthoredPosition;
            planetLeft.rectTransform.localScale = leftAuthoredScale;
        }
        if (planetRight != null)
        {
            planetRight.rectTransform.anchoredPosition = rightAuthoredPosition;
            planetRight.rectTransform.localScale = rightAuthoredScale;
        }
        if (resultPlanet != null)
            resultPlanet.rectTransform.localScale = resultAuthoredScale;
    }

    private void ConfigureNonInteractiveUi()
    {
        foreach (Image image in new[] { planetLeft, planetRight, resultPlanet })
            if (image != null)
                image.raycastTarget = false;
        if (mergeFlash != null)
        {
            mergeFlash.raycastTarget = false;
            mergeFlash.gameObject.SetActive(false);
        }
        foreach (TextMeshProUGUI text in new[]
                 { timeBonusText, demoTimerText, descriptionText, tapToContinueText })
            if (text != null)
                text.raycastTarget = false;
    }

    private void ResetVisuals()
    {
        activeRun = null;
        activeSequenceToken = -1;
        continueRequested = false;
        if (tapCatcher != null)
        {
            tapCatcher.interactable = false;
            tapCatcher.gameObject.SetActive(false);
        }
        RestoreChildTransforms();
        if (tutorialGroup != null)
        {
            tutorialGroup.alpha = 0f;
            tutorialGroup.interactable = false;
            tutorialGroup.blocksRaycasts = false;
        }
        if (tutorialRoot != null)
            tutorialRoot.gameObject.SetActive(false);
        if (mergeFlash != null)
            mergeFlash.gameObject.SetActive(false);
    }

    private static float Smooth(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static float EaseOut(float value)
    {
        value = Mathf.Clamp01(value);
        return 1f - (1f - value) * (1f - value);
    }
}
