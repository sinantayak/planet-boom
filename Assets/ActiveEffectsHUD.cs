using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Bottom-left "what is affecting this run" column, stacked directly above
// the inventory/chest bag. Purely informational (every graphic is
// non-raycast) and purely presentational: the single source of truth stays
// BoosterInventoryManager's active-run flags (via its ActiveEffectChanged
// bridge event) and PlanetLauncher's Cosmic Shield state — no state is
// duplicated here.
//
// Level-start choreography: boosters activate at READY (still
// LevelPreparing), so their slots are queued as "pending" instead of
// popping instantly; LevelStartSequence asks this HUD to reveal them after
// MissionHUD finishes. No pending boosters means the sequence yields nothing
// and adds zero delay. Effects that turn on during
// gameplay (Cosmic Shield) pop immediately without pausing anything.
//
// The Cosmic Shield slot reuses TimedEffectRadialIndicator, fed by the
// launcher's authoritative CosmicShieldRemainingSeconds — never a second
// timer. All animation runs on unscaled time; every slot's authored scale
// is captured before animating and restored exactly afterwards.
public sealed class ActiveEffectsHUD : MonoBehaviour
{
    [System.Serializable]
    public sealed class EffectSlot
    {
        public GameObject root;
        public Image icon;
        public TextMeshProUGUI introNameText;
        [System.NonSerialized] public Vector3 authoredScale;
        [System.NonSerialized] public bool authoredScaleCaptured;
        [System.NonSerialized] public Coroutine revealRoutine;
        [System.NonSerialized] public bool pendingReveal;
    }

    // Visual order bottom → top (the stack grows upward from the bag).
    [Header("Effect Slots (bottom → top)")]
    [SerializeField] private EffectSlot luckyDropSlot;
    [SerializeField] private EffectSlot doubleTimeDropSlot;
    [SerializeField] private EffectSlot starBoosterSlot;
    [SerializeField] private EffectSlot cosmicShieldSlot;

    [Header("Reveal Animation")]
    [SerializeField, Min(0.05f)] private float revealDuration = 0.28f;
    [SerializeField, Min(0f)] private float delayBetweenEffects = 0.12f;
    [SerializeField, Range(0.05f, 1f)] private float startScale = 0.4f;
    [SerializeField, Range(1f, 1.5f)] private float overshootStrength = 1.15f;
    [SerializeField] private AudioClip effectRevealClip;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;

    [Header("Level Start Effect Name")]
    [SerializeField, Min(0f)] private float effectNameHoldDuration = 0.55f;
    [SerializeField, Min(0.01f)] private float effectNameFadeDuration = 0.18f;

    [Header("Cosmic Shield Countdown")]
    // Appearance of the radial overlay TimedEffectRadialIndicator builds on
    // the shield slot; leave the sprite empty for the generated soft disc.
    [SerializeField] private Sprite countdownCircleSprite;
    [SerializeField] private Color countdownOverlayColor = new Color(0f, 0f, 0f, 0.45f);
    [SerializeField] private bool countdownShowsSeconds = true;
    [SerializeField] private Color countdownTextColor = Color.white;
    [SerializeField, Min(8f)] private float countdownTextFontSize = 34f;

    private PlanetLauncher launcher;
    private TimedEffectRadialIndicator shieldCountdown;
    private int pendingRevealCount;

    private void Awake()
    {
        Transform legacySharedName = transform.Find("IntroEffectName");
        if (legacySharedName != null)
            legacySharedName.gameObject.SetActive(false);
        foreach (EffectSlot slot in OrderedSlots())
        {
            HideSlot(slot);
            HideEffectName(slot);
        }

        if (cosmicShieldSlot != null && cosmicShieldSlot.root != null && cosmicShieldSlot.icon != null)
        {
            shieldCountdown = cosmicShieldSlot.root.GetComponent<TimedEffectRadialIndicator>();
            if (shieldCountdown == null)
                shieldCountdown = cosmicShieldSlot.root.AddComponent<TimedEffectRadialIndicator>();
            shieldCountdown.Initialize(cosmicShieldSlot.icon, countdownCircleSprite,
                countdownOverlayColor, countdownShowsSeconds, countdownTextColor, countdownTextFontSize);
        }
    }

    private void OnEnable()
    {
        BoosterInventoryManager.ActiveEffectChanged += HandleBoosterEffectChanged;

        launcher = FindFirstObjectByType<PlanetLauncher>(FindObjectsInactive.Include);
        if (launcher != null)
            launcher.CosmicShieldStateChanged += HandleShieldStateChanged;

    }

    private void OnDisable()
    {
        BoosterInventoryManager.ActiveEffectChanged -= HandleBoosterEffectChanged;
        if (launcher != null)
            launcher.CosmicShieldStateChanged -= HandleShieldStateChanged;
        foreach (EffectSlot slot in OrderedSlots())
            HideEffectName(slot);
    }

    private void Start()
    {
        // Defensive initial sync — normally everything is inactive at scene
        // load, but a mid-run recompile/re-enable must not drop icons.
        BoosterInventoryManager boosters = BoosterInventoryManager.Instance;
        if (boosters != null)
        {
            SyncBooster(luckyDropSlot, boosters.IsLuckyDropActive);
            SyncBooster(doubleTimeDropSlot, boosters.IsDoubleTimeDropActive);
            SyncBooster(starBoosterSlot, boosters.IsStarBoosterActive);
        }
        if (launcher != null && launcher.IsCosmicShieldActive)
            HandleShieldStateChanged(true);
    }

    // Safety net only: if the mission intro was interrupted and gameplay is
    // already running while reveals are still queued, show them immediately.
    // Costs a single int comparison per frame in the normal case.
    private void Update()
    {
        if (pendingRevealCount == 0)
            return;
        GameManager manager = GameManager.Instance;
        if (manager == null || manager.State != GameManager.GameState.Playing)
            return;
        foreach (EffectSlot slot in OrderedSlots())
        {
            if (slot == null || !slot.pendingReveal) continue;
            slot.pendingReveal = false;
            ShowSlot(slot, animate: true);
        }
        pendingRevealCount = 0;
    }

    private void HandleBoosterEffectChanged(ActiveEffectType type, bool active)
    {
        EffectSlot slot = SlotFor(type);
        if (slot == null || slot.root == null)
            return;

        if (!active)
        {
            ClearPending(slot);
            HideSlot(slot);
            return;
        }

        // READY activates boosters while the level is still preparing; their
        // icons wait for the post-mission-card reveal sequence.
        if (GameManager.Instance != null &&
            GameManager.Instance.State == GameManager.GameState.LevelPreparing)
        {
            if (!slot.pendingReveal)
            {
                slot.pendingReveal = true;
                pendingRevealCount++;
            }
            return;
        }
        ShowSlot(slot, animate: true);
    }

    private void HandleShieldStateChanged(bool active)
    {
        if (cosmicShieldSlot == null || cosmicShieldSlot.root == null)
            return;
        if (!active)
        {
            HideSlot(cosmicShieldSlot);
            return;
        }
        // Presentation only — gameplay is never delayed for this pop.
        ShowSlot(cosmicShieldSlot, animate: true);
        if (shieldCountdown != null && launcher != null)
            shieldCountdown.Begin(launcher.CosmicShieldActiveDurationSeconds,
                () => launcher != null ? launcher.CosmicShieldRemainingSeconds : 0f,
                () => launcher != null && launcher.IsCosmicShieldActive);
    }

    // LevelStartSequence yields this after the mission cards. Empty queue
    // means nothing is yielded and no artificial delay is introduced.
    public IEnumerator RevealPreparedEffects()
    {
        foreach (EffectSlot slot in PassiveSlots())
        {
            if (slot == null || !slot.pendingReveal) continue;
            slot.pendingReveal = false;
            pendingRevealCount = Mathf.Max(0, pendingRevealCount - 1);
            AudioManager.Instance?.PlayUiOneShot(effectRevealClip, sfxVolume);
            yield return RevealWithName(slot, LocalizationKeyFor(slot));
            if (pendingRevealCount > 0 && delayBetweenEffects > 0f)
                yield return WaitUnscaled(delayBetweenEffects);
        }
    }

    public void ResetPreparedIntro()
    {
        foreach (EffectSlot slot in OrderedSlots())
            HideEffectName(slot);
        foreach (EffectSlot slot in OrderedSlots())
        {
            if (slot?.revealRoutine == null) continue;
            StopCoroutine(slot.revealRoutine);
            slot.revealRoutine = null;
        }
    }

    private IEnumerator RevealWithName(EffectSlot slot, string key)
    {
        TextMeshProUGUI label = slot?.introNameText;
        CanvasGroup nameGroup = null;
        if (label != null)
        {
            label.text = Localization.Get(key);
            label.gameObject.SetActive(true);
            nameGroup = GetOrAddCanvasGroup(label.gameObject);
            nameGroup.alpha = 1f;
        }

        // Icon and its own child label enter on the same frame.
        yield return RevealRoutine(slot);
        if (effectNameHoldDuration > 0f)
            yield return WaitUnscaled(effectNameHoldDuration);
        float elapsed = 0f;
        while (nameGroup != null && elapsed < effectNameFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            nameGroup.alpha = 1f - Mathf.Clamp01(elapsed / effectNameFadeDuration);
            yield return null;
        }
        HideEffectName(slot);
    }

    private static void HideEffectName(EffectSlot slot)
    {
        if (slot?.introNameText == null) return;
        GetOrAddCanvasGroup(slot.introNameText.gameObject).alpha = 0f;
        slot.introNameText.gameObject.SetActive(false);
    }

    private static CanvasGroup GetOrAddCanvasGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        return group != null ? group : target.AddComponent<CanvasGroup>();
    }

    private string LocalizationKeyFor(EffectSlot slot)
    {
        if (slot == luckyDropSlot) return "level_start.effect.lucky_drop";
        if (slot == doubleTimeDropSlot) return "level_start.effect.double_time_drop";
        return "level_start.effect.star_booster";
    }

    private void SyncBooster(EffectSlot slot, bool active)
    {
        if (slot == null || slot.root == null || slot.pendingReveal) return;
        if (active && !slot.root.activeSelf) ShowSlot(slot, animate: false);
        else if (!active && slot.root.activeSelf) HideSlot(slot);
    }

    private void ShowSlot(EffectSlot slot, bool animate)
    {
        if (slot == null || slot.root == null)
            return;
        CaptureAuthoredScale(slot);
        if (slot.revealRoutine != null)
        {
            StopCoroutine(slot.revealRoutine);
            slot.revealRoutine = null;
        }
        slot.root.SetActive(true);
        if (animate && isActiveAndEnabled)
            slot.revealRoutine = StartCoroutine(RevealRoutine(slot));
        else
            RestoreAuthoredLook(slot);
    }

    private void HideSlot(EffectSlot slot)
    {
        if (slot == null || slot.root == null)
            return;
        if (slot.revealRoutine != null)
        {
            StopCoroutine(slot.revealRoutine);
            slot.revealRoutine = null;
        }
        RestoreAuthoredLook(slot);
        if (slot.root.activeSelf)
            slot.root.SetActive(false); // shield radial self-clears via its OnDisable
    }

    private void ClearPending(EffectSlot slot)
    {
        if (slot != null && slot.pendingReveal)
        {
            slot.pendingReveal = false;
            pendingRevealCount = Mathf.Max(0, pendingRevealCount - 1);
        }
    }

    // Small pop on unscaled time: grow from startScale, overshoot slightly,
    // settle on the EXACT authored scale (captured before the first frame of
    // animation and restored verbatim — manual Inspector scales survive).
    // Self-contained on purpose: it captures the authored scale and
    // activates the slot itself, because the mission-intro path yields this
    // routine directly without going through ShowSlot.
    private IEnumerator RevealRoutine(EffectSlot slot)
    {
        CaptureAuthoredScale(slot);
        if (!slot.root.activeSelf)
            slot.root.SetActive(true);
        CanvasGroup group = slot.root.GetComponent<CanvasGroup>();
        Vector3 authored = slot.authoredScale;
        float elapsed = 0f;
        while (elapsed < revealDuration && slot.root != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / revealDuration);
            float curve = t < 0.7f
                ? Mathf.Lerp(startScale, overshootStrength, SmoothStep01(t / 0.7f))
                : Mathf.Lerp(overshootStrength, 1f, SmoothStep01((t - 0.7f) / 0.3f));
            slot.root.transform.localScale = authored * curve;
            if (group != null)
                group.alpha = Mathf.Clamp01(t / 0.5f);
            yield return null;
        }
        RestoreAuthoredLook(slot);
        slot.revealRoutine = null;
    }

    private static void CaptureAuthoredScale(EffectSlot slot)
    {
        if (slot.authoredScaleCaptured)
            return;
        slot.authoredScale = slot.root.transform.localScale;
        slot.authoredScaleCaptured = true;
    }

    private void RestoreAuthoredLook(EffectSlot slot)
    {
        if (slot.root == null)
            return;
        if (slot.authoredScaleCaptured)
            slot.root.transform.localScale = slot.authoredScale;
        CanvasGroup group = slot.root.GetComponent<CanvasGroup>();
        if (group != null)
            group.alpha = 1f;
    }

    private EffectSlot SlotFor(ActiveEffectType type)
    {
        switch (type)
        {
            case ActiveEffectType.LuckyDrop: return luckyDropSlot;
            case ActiveEffectType.DoubleTimeDrop: return doubleTimeDropSlot;
            case ActiveEffectType.StarBooster: return starBoosterSlot;
            default: return null;
        }
    }

    private IEnumerable<EffectSlot> OrderedSlots()
    {
        yield return luckyDropSlot;
        yield return doubleTimeDropSlot;
        yield return starBoosterSlot;
        yield return cosmicShieldSlot;
    }

    private IEnumerable<EffectSlot> PassiveSlots()
    {
        yield return luckyDropSlot;
        yield return doubleTimeDropSlot;
        yield return starBoosterSlot;
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

    private static float SmoothStep01(float t) => t * t * (3f - 2f * t);
}
