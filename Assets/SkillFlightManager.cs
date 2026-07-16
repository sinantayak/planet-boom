using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// World-to-UI flight for a dropped skill: spawns a UI icon at the merge's
// on-screen position, lets it hang in the air for a moment (the "dopamine
// beat" — float up, scale in, hover), then quickly flies it along a
// quadratic Bezier arc to the inventory chest, popping the chest's lid open
// just before arrival and pulsing it shut again once the icon lands. Runs
// entirely under flightContainer's Canvas — including Screen Space -
// Overlay — via the same screen/canvas-space conversion
// LevelCompletePanel.AnchoredPointForWorld uses for its vortex reveal.
//
// Scene-local singleton, same shape as AudioManager/GameManager.
public class SkillFlightManager : MonoBehaviour
{
    public static SkillFlightManager Instance { get; private set; }

    // Reward boundary for the inventory backend: fired only after the icon's
    // full animation actually reaches the chest. A flight interrupted by a
    // disable/scene unload never emits this event and therefore cannot reward.
    public static event Action<SkillType> OnSkillArrivedAtChest;

    public Sprite GetSkillIcon(SkillType type)
    {
        int index = (int)type;
        return skillIcons != null && index >= 0 && index < skillIcons.Length
            ? skillIcons[index]
            : null;
    }

    [Header("Wiring")]
    // Full-screen RectTransform the flying icons get parented under — make
    // it the LAST sibling under the root Canvas so nothing else in the UI
    // can draw over an in-flight icon, even on Screen Space - Overlay.
    [SerializeField] private RectTransform flightContainer;
    // The Inventory button's own RectTransform — the flight's destination
    // and the thing that receives the arrival pulse.
    [SerializeField] private RectTransform inventoryButtonRect;
    // Timer is resolved automatically from GameManager when this override is
    // empty. Coin points at the scene-authored Coin HUD icon RectTransform.
    [SerializeField] private RectTransform timerTargetOverride;
    [SerializeField] private RectTransform coinHudTarget;

    [Header("Icons")]
    // Indexed by SkillType (GravitySingularity, MeteorStrike, TimeWarp,
    // CosmicMimic) — same "array indexed by (int)enum" convention as
    // Planet.planetSprites.
    [SerializeField] private Sprite[] skillIcons = new Sprite[4];
    // Zero preserves the legacy shared size on older scenes/prefabs; this
    // project's GameScene is explicitly authored to its existing 150x150.
    [SerializeField] private Vector2 skillIconSize = Vector2.zero;
    [SerializeField] private Vector2 timeDropIconSize = new Vector2(64f, 64f);
    [SerializeField] private Vector2 coinDropIconSize = new Vector2(64f, 64f);
    // Receives the former shared iconSize value when an older scene/prefab is
    // loaded. Specific sizes fall back to it only if authored as zero/invalid.
    [FormerlySerializedAs("iconSize")]
    [SerializeField] [HideInInspector] private Vector2 legacyIconSize = new Vector2(80f, 80f);

    [Header("Chest Sprite Swap")]
    // The Image whose sprite gets swapped open/closed — usually the same
    // Image the InventoryButton itself displays, but left as its own
    // reference in case the chest artwork lives on a child Image instead.
    [SerializeField] private Image chestImage;
    [SerializeField] private Sprite chestClosedSprite;
    [SerializeField] private Sprite chestOpenSprite;
    // Bezier progress (0..1 along the FLIGHT phase only, not counting the
    // hangtime beat before it) at which the chest pops open — 0.85 means
    // "open the lid when the icon is 85% of the way through its flight".
    [SerializeField] [Range(0.5f, 0.99f)] private float chestOpenAtProgress = 0.85f;

    [Header("Hangtime (dopamine beat before the flight)")]
    // Phase 1: scale in and float straight up from the spawn point.
    [FormerlySerializedAs("spawnFloatDuration")]
    [SerializeField] private float floatUpDuration = 0.3f;
    // Canvas/UI units (this icon is a UI RectTransform, not a scene object),
    // but kept small to read as a gentle lift rather than a jump.
    [FormerlySerializedAs("spawnFloatDistance")]
    [SerializeField] private float floatUpDistance = 40f;
    // Phase 2: hang at that peak height so the player has time to register
    // "a skill dropped" before it shoots off toward the chest.
    [SerializeField] private float hoverDuration = 0.6f;
    // Subtle vertical bob while hovering, so it doesn't read as frozen.
    [SerializeField] private float hoverBobAmplitude = 6f;
    [SerializeField] private float hoverBobFrequency = 4f;

    [Header("Flight")]
    [SerializeField] private float flightDuration = 0.6f;
    // How far the Bezier control point bulges above the straight line from
    // the hover point to the chest — bigger = a taller, more dramatic arc.
    [SerializeField] private float arcHeight = 220f;

    [Header("Arrival Pulse")]
    [SerializeField] private float pulseScale = 1.25f;
    [SerializeField] private float pulseDuration = 0.25f;

    private Camera worldCamera;
    private Canvas canvas;
    private Coroutine pulseRoutine;

    // The chest's TRUE resting scale, captured once up front — every pulse
    // grows from and returns to this exact value, never a hardcoded
    // Vector3.one, so a chest authored at e.g. 0.8 scale doesn't drift.
    private Vector3 originalChestScale = Vector3.one;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("SkillFlightManager: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (flightContainer != null)
        {
            canvas = flightContainer.GetComponentInParent<Canvas>();
        }
        worldCamera = Camera.main;

        if (inventoryButtonRect != null)
        {
            originalChestScale = inventoryButtonRect.localScale;
        }

        SetChestOpen(false);
    }

    // Last-resort safety net: if this component (or its GameObject) is
    // disabled while a pulse is mid-flight, Unity silently kills the
    // coroutine WITHOUT necessarily running the coroutine's own finally
    // block (that guarantee only reliably holds for StopCoroutine/normal
    // completion) — so force the chest back to its resting scale here too.
    void OnDisable()
    {
        pulseRoutine = null;
        if (inventoryButtonRect != null)
        {
            inventoryButtonRect.localScale = originalChestScale;
        }
    }

    // worldPosition: the merge position in the arena. skill: which icon to
    // show, resolved from skillIcons[(int)skill].
    public void SpawnFlight(Vector3 worldPosition, SkillType skill)
    {
        if (flightContainer == null || inventoryButtonRect == null)
        {
            Debug.LogWarning("SkillFlightManager: flightContainer or inventoryButtonRect not assigned — skipping skill flight visual.", this);
            return;
        }

        if (worldCamera == null)
        {
            worldCamera = Camera.main;
        }
        if (worldCamera == null)
            return;

        Vector2 startPos = ScreenToContainerLocal(worldCamera.WorldToScreenPoint(worldPosition));
        Vector2 endPos = ScreenToContainerLocal(
            RectTransformUtility.WorldToScreenPoint(CanvasCameraOrNull(), inventoryButtonRect.position));

        var go = new GameObject($"SkillDropIcon_{skill}", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(flightContainer, false);
        rect.sizeDelta = ResolveIconSize(skillIconSize);
        rect.anchoredPosition = startPos;

        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        int iconIndex = (int)skill;
        image.sprite = (skillIcons != null && iconIndex >= 0 && iconIndex < skillIcons.Length) ? skillIcons[iconIndex] : null;
        image.preserveAspect = true;

        var settings = new SkillDropFlightIcon.FlightSettings
        {
            floatUpDuration = floatUpDuration,
            floatUpDistance = floatUpDistance,
            hoverDuration = hoverDuration,
            hoverBobAmplitude = hoverBobAmplitude,
            hoverBobFrequency = hoverBobFrequency,
            flightDuration = flightDuration,
            arcHeight = arcHeight,
            chestOpenAtProgress = chestOpenAtProgress,
        };

        var flightIcon = go.AddComponent<SkillDropFlightIcon>();
        flightIcon.Play(rect, startPos, endPos, settings, OnChestShouldOpen, () => OnFlightArrived(skill));
    }

    // Shared visual-only flight used by non-skill rewards. Reward ownership
    // remains with the caller's arrival callback, so an invalid target,
    // disabled UI, scene unload, or interrupted coroutine grants nothing.
    public bool SpawnRewardFlight(Vector3 worldPosition, RewardDropType rewardType,
        Sprite sprite, Action onArrived)
    {
        RectTransform target = ResolveRewardTarget(rewardType);
        if (flightContainer == null || target == null || sprite == null ||
            !target.gameObject.activeInHierarchy)
        {
            Debug.LogWarning($"SkillFlightManager: {rewardType} flight has no active target or sprite.", this);
            return false;
        }

        if (worldCamera == null)
            worldCamera = Camera.main;
        if (worldCamera == null)
            return false;

        Vector2 startPos = ScreenToContainerLocal(worldCamera.WorldToScreenPoint(worldPosition));
        Vector2 endPos = ScreenToContainerLocal(
            RectTransformUtility.WorldToScreenPoint(CanvasCameraOrNull(), target.position));

        var go = new GameObject($"RewardDropIcon_{rewardType}", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(flightContainer, false);
        rect.sizeDelta = ResolveRewardIconSize(rewardType);
        rect.anchoredPosition = startPos;

        var image = go.AddComponent<Image>();
        image.raycastTarget = false;
        image.sprite = sprite;
        image.preserveAspect = true;

        var settings = new SkillDropFlightIcon.FlightSettings
        {
            floatUpDuration = floatUpDuration,
            floatUpDistance = floatUpDistance,
            hoverDuration = hoverDuration,
            hoverBobAmplitude = hoverBobAmplitude,
            hoverBobFrequency = hoverBobFrequency,
            flightDuration = flightDuration,
            arcHeight = arcHeight,
            chestOpenAtProgress = 1f
        };

        var flightIcon = go.AddComponent<SkillDropFlightIcon>();
        flightIcon.Play(rect, startPos, endPos, settings, null, () =>
        {
            if (target != null && target.gameObject.activeInHierarchy)
                onArrived?.Invoke();
        });
        return true;
    }

    private RectTransform ResolveRewardTarget(RewardDropType rewardType)
    {
        if (rewardType == RewardDropType.Time)
            return timerTargetOverride != null
                ? timerTargetOverride
                : GameManager.Instance != null ? GameManager.Instance.GameplayTimerRect : null;
        if (rewardType == RewardDropType.SpaceCoin)
            return coinHudTarget;
        return inventoryButtonRect;
    }

    private Vector2 ResolveRewardIconSize(RewardDropType rewardType)
    {
        if (rewardType == RewardDropType.Time)
            return ResolveIconSize(timeDropIconSize);
        if (rewardType == RewardDropType.SpaceCoin)
            return ResolveIconSize(coinDropIconSize);
        return ResolveIconSize(skillIconSize);
    }

    private Vector2 ResolveIconSize(Vector2 configuredSize)
    {
        if (configuredSize.x > 0f && configuredSize.y > 0f)
            return configuredSize;
        if (legacyIconSize.x > 0f && legacyIconSize.y > 0f)
            return legacyIconSize;
        return new Vector2(80f, 80f);
    }

    // Fired once, partway through the flight (see chestOpenAtProgress) —
    // pops the lid open just before the icon actually arrives.
    private void OnChestShouldOpen()
    {
        SetChestOpen(true);
    }

    private void OnFlightArrived(SkillType skill)
    {
        SetChestOpen(false);
        StartPulse();
        OnSkillArrivedAtChest?.Invoke(skill);
    }

    // Stops any pulse already in flight and snaps the scale back to the
    // known-good baseline BEFORE starting a new one — so two arrivals close
    // together always start their lerp from the same resting scale instead
    // of from wherever the interrupted pulse happened to land, which is
    // what let repeated arrivals compound into a permanently enlarged chest.
    private void StartPulse()
    {
        if (inventoryButtonRect == null)
            return;

        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }
        inventoryButtonRect.localScale = originalChestScale;

        pulseRoutine = StartCoroutine(PulseInventoryButton());
    }

    private void SetChestOpen(bool open)
    {
        if (chestImage == null)
            return;

        Sprite sprite = open ? chestOpenSprite : chestClosedSprite;
        if (sprite != null)
        {
            chestImage.sprite = sprite;
        }
    }

    // Subtle scale-up/settle "success" pulse on the inventory button — no
    // external tween library, just a hand-rolled two-leg lerp that always
    // grows from and returns to originalChestScale (never a hardcoded
    // Vector3.one). Wrapped in try/finally so the chest snaps back to
    // originalChestScale on ANY exit path — normal completion, a fresh
    // arrival calling StopCoroutine on this one mid-pulse, or the whole
    // component being disabled (see OnDisable's belt-and-braces reset for
    // the one case Unity doesn't guarantee this finally actually runs).
    private IEnumerator PulseInventoryButton()
    {
        try
        {
            Vector3 enlargedScale = originalChestScale * pulseScale;
            float halfDuration = Mathf.Max(0.0001f, pulseDuration * 0.5f);

            float elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                inventoryButtonRect.localScale = Vector3.Lerp(originalChestScale, enlargedScale, t);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / halfDuration);
                inventoryButtonRect.localScale = Vector3.Lerp(enlargedScale, originalChestScale, t);
                yield return null;
            }
        }
        finally
        {
            if (inventoryButtonRect != null)
            {
                inventoryButtonRect.localScale = originalChestScale;
            }
            pulseRoutine = null;
        }
    }

    // Screen Space - Overlay canvases render with no world camera — passing
    // one to RectTransformUtility for an Overlay canvas actually produces
    // wrong coordinates, so this must stay null in that mode.
    private Camera CanvasCameraOrNull()
    {
        return canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
    }

    private Vector2 ScreenToContainerLocal(Vector2 screenPoint)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            flightContainer, screenPoint, CanvasCameraOrNull(), out Vector2 localPoint);
        return localPoint;
    }
}

// Self-contained per-icon flight: scales/floats up, hangs in the air with a
// gentle hover, then rides a quadratic Bezier arc to its destination —
// popping the chest open near the end via onChestShouldOpen — before
// destroying itself and invoking onArrived (SkillFlightManager's chest-close
// + arrival pulse). Lives entirely on the spawned icon so SkillFlightManager
// itself stays a stateless factory — mirrors ComboPopupAnimator's
// relationship to ComboTextSpawner.
public class SkillDropFlightIcon : MonoBehaviour
{
    // Plain data carrier for the tunable timings SkillFlightManager owns in
    // its Inspector — keeps Play/Animate's signature from growing a long
    // positional-parameter list every time a new knob is added.
    public struct FlightSettings
    {
        public float floatUpDuration;
        public float floatUpDistance;
        public float hoverDuration;
        public float hoverBobAmplitude;
        public float hoverBobFrequency;
        public float flightDuration;
        public float arcHeight;
        public float chestOpenAtProgress;
    }

    public void Play(RectTransform rect, Vector2 startPos, Vector2 endPos, FlightSettings settings,
        Action onChestShouldOpen, Action onArrived)
    {
        StartCoroutine(Animate(rect, startPos, endPos, settings, onChestShouldOpen, onArrived));
    }

    private IEnumerator Animate(RectTransform rect, Vector2 startPos, Vector2 endPos, FlightSettings settings,
        Action onChestShouldOpen, Action onArrived)
    {
        rect.localScale = Vector3.zero;

        // Phase 1 — float up: scale in and lift off the spawn point on an
        // elegant ease-out (fast start, gentle settle at the peak).
        float elapsed = 0f;
        Vector2 floatEnd = startPos + Vector2.up * settings.floatUpDistance;
        while (elapsed < settings.floatUpDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settings.floatUpDuration);
            float eased = EaseOutCubic(t);

            rect.localScale = Vector3.one * eased;
            rect.anchoredPosition = Vector2.Lerp(startPos, floatEnd, eased);
            yield return null;
        }
        rect.localScale = Vector3.one;
        rect.anchoredPosition = floatEnd;

        // Phase 2 — hangtime: hover in place with a subtle bob, giving the
        // player a beat to register "a skill dropped" before it takes off.
        elapsed = 0f;
        while (elapsed < settings.hoverDuration)
        {
            elapsed += Time.deltaTime;
            float bob = Mathf.Sin(elapsed * settings.hoverBobFrequency) * settings.hoverBobAmplitude;
            rect.anchoredPosition = floatEnd + Vector2.up * bob;
            yield return null;
        }
        rect.anchoredPosition = floatEnd;

        // Phase 3 — flight: quadratic Bezier from the hover point to the
        // target, bulging toward a control point above the midpoint for a
        // natural arc. Cubic ease-in-out reads as a quick launch off the
        // hover point with a soft landing into the chest.
        Vector2 flightStart = floatEnd;
        Vector2 mid = (flightStart + endPos) * 0.5f;
        Vector2 control = mid + Vector2.up * settings.arcHeight;

        bool chestOpened = false;
        elapsed = 0f;
        while (elapsed < settings.flightDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settings.flightDuration);

            if (!chestOpened && t >= settings.chestOpenAtProgress)
            {
                chestOpened = true;
                onChestShouldOpen?.Invoke();
            }

            float eased = EaseInOutCubic(t);
            Vector2 a = Vector2.Lerp(flightStart, control, eased);
            Vector2 b = Vector2.Lerp(control, endPos, eased);
            rect.anchoredPosition = Vector2.Lerp(a, b, eased);

            // Shrink slightly on approach — reads as "arriving into" the chest.
            rect.localScale = Vector3.one * Mathf.Lerp(1f, 0.6f, eased);

            yield return null;
        }

        if (!chestOpened)
        {
            onChestShouldOpen?.Invoke();
        }

        onArrived?.Invoke();
        Destroy(gameObject);
    }

    private static float EaseOutCubic(float t)
    {
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    // Quick acceleration off the hover point, gentle deceleration into the
    // chest — noticeably snappier off the start than a plain smoothstep.
    private static float EaseInOutCubic(float t)
    {
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}
