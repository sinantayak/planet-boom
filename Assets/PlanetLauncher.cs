using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PlanetLauncher : MonoBehaviour
{
    [SerializeField] private GameObject planetPrefab;
    [SerializeField] private float launchSpeed = 8f;
    [SerializeField] private float spawnPositionJitter = 0.05f;
    [SerializeField] private float maxSpinTorque = 5f;

    [Header("Spawn Tiers")]
    // Highest tier the launcher will ever put in the queue: shots are drawn
    // uniformly from Tier1..highestSpawnTier. Kept low (Suika-style) so the
    // upper tiers can only be reached by merging; raise it to seed bigger
    // planets directly. PlanetMerge snaps spawned planets onto the tier
    // growth curve, so a spawned Tier2 matches a merged-up Tier2 in size.
    [SerializeField] private PlanetTier highestSpawnTier = PlanetTier.Tier2;

    [Header("Aim Dots")]
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private float dotSpacing = 0.25f;
    // Number of physics steps to simulate ahead; more steps = longer preview.
    [SerializeField] private int trajectorySteps = 30;
    // Taper: dots shrink and fade from full size (and startDotAlpha) near the
    // launcher down to these values at the far end of the path.
    [SerializeField] private float endDotScale = 0.35f;
    [SerializeField] private float endDotAlpha = 0.2f;
    // Alpha of the dot nearest the launcher. Dots are always pure white (the
    // loaded planet in the slot shows the tier now); lower this for a softer
    // guide line, e.g. 0.6.
    [SerializeField] private float startDotAlpha = 0.85f;

    [Header("Aim Control (relative drag)")]
    // Hard cap on how far the aim may tilt from straight up, in degrees per
    // side. This is the ONLY angle limit: 60 means a usable arc of 30°..150°
    // in polar terms — well past the old ~75°..115° feel, and past the 135°
    // target. Raise it toward 89 for near-horizontal skill shots.
    [SerializeField] [Range(0f, 89f)] private float maxAngleFromVertical = 60f;

    // Degrees of aim rotation per full screen-WIDTH of horizontal finger
    // travel. Relative control: only the drag delta matters, never where the
    // finger sits — no shot ever requires reaching the physical screen edge.
    // At 240, tilting to the 60° cap costs a quarter of the screen width.
    [SerializeField] private float angleSensitivity = 240f;

    // Classic slingshot mapping (default ON): drag RIGHT pulls the aim LEFT,
    // drag LEFT pulls it RIGHT — like drawing a rubber band backward, the
    // trajectory always leans opposite the pull. Turn off for the "direct"
    // arcade feel instead, where the trajectory leans the same way the
    // finger moves.
    [SerializeField] private bool invertHorizontalDrag = true;

    // When true, aiming only starts if the press lands within grabRadius of
    // the launcher slot (the "press the ball" feel). Off by default: with
    // relative drag the whole screen is a trackpad, which is the more
    // forgiving mobile UX — a thumb can start anywhere.
    [SerializeField] private bool requirePressOnPlanet = false;
    [SerializeField] private float grabRadius = 1.5f;

    [Header("Shot Power")]
    // Power the shot previews at the instant the finger goes down — the
    // "medium/default" straight-up guide. 1 = full launchSpeed.
    [SerializeField] [Range(0.05f, 1f)] private float defaultPowerRatio = 0.7f;

    // Change in power ratio per full screen-HEIGHT of vertical finger travel.
    // Dragging DOWN charges the shot toward full power, dragging UP softens
    // it toward minPowerRatio. Set to 0 to lock shots at defaultPowerRatio.
    [SerializeField] private float powerSensitivity = 1.2f;

    // Softest allowed shot, as a fraction of launchSpeed.
    [SerializeField] [Range(0.05f, 1f)] private float minPowerRatio = 0.3f;

    [Header("Launcher Timing")]
    // Minimum delay between shots. After a launch the slot sits empty for this
    // long — no aiming, no preview — before the next queued planet is loaded.
    // This is the anti-spam gate: rapid clicking can no longer stream planets
    // into the arena faster than one per cooldown.
    [SerializeField] private float launchCooldown = 0.6f;

    [Header("Launcher UI Preview")]
    // Canvas Image for the "Next Planet" box. Always visible: while the slot
    // itself is empty during a reload, this keeps previewing the planet that
    // will fill it when the cooldown ends (NextTier).
    [SerializeField] private Image nextPlanetUIImg;
    // World-space renderer sitting in the launcher slot, wearing the actual
    // planet sprite the next click will fire (CurrentTier). Hidden while
    // reloading so the slot reads as visibly empty. Scaled per tier in
    // RefreshPreviews — any scale set on it in the Inspector is overridden.
    [SerializeField] private SpriteRenderer loadedPlanetRenderer;

    // Universal fudge factor on the slot preview's tier-matched size: 1 shows
    // the planet at its exact in-arena world scale, < 1 shrinks every preview
    // uniformly if real sizes overflow the launcher area.
    [SerializeField] private float launcherVisualScaleModifier = 1f;

    [Header("Skills")]
    // Skill hook: while true, the next launched planet is a wildcard that adopts
    // the tier of whatever planet it touches first. Consumed on launch.
    public bool isRainbowActive;

    // Preview state: CurrentTier is what the next click fires; NextTier is what
    // follows it (shown in UI later). Both readable by UI code, set only here.
    // NOTE: during a reload (IsReloading == true) CurrentTier still holds the
    // tier that was just fired — the queue only advances once the cooldown
    // ends — so UI showing the loaded planet should hide or dim it while
    // IsReloading is true.
    public PlanetTier CurrentTier { get; private set; }
    public PlanetTier NextTier { get; private set; }

    // True from the moment a shot fires until launchCooldown has elapsed and
    // the next planet has been pulled into the slot. Input is ignored and the
    // aim preview stays hidden for the whole window; UI can read this to tint
    // or hide the launcher slot while it is locked.
    public bool IsReloading => isWaitingForCooldown;

    private bool isWaitingForCooldown;
    private Coroutine reloadRoutine;

    private BlackHole blackHole;

    // The prefab's Planet component: the previews read tier sprites off its
    // serialized array, so slot/next art always matches what actually spawns.
    private Planet prefabPlanet;

    // The prefab's PlanetMerge: source of the tier growth curve, so the slot
    // preview is sized exactly like the planet that will spawn.
    private PlanetMerge prefabMerge;

    // Dot pool: pre-instantiated hidden in Awake (one per trajectory step, the
    // maximum ever needed), then repositioned/activated per aim frame. Never
    // destroyed during play — no Instantiate/Destroy churn while dragging.
    private readonly List<GameObject> activeDots = new List<GameObject>();
    private readonly List<SpriteRenderer> dotRenderers = new List<SpriteRenderer>();
    private readonly List<Vector2> dotPositions = new List<Vector2>();
    private Transform dotContainer;
    private Vector3 dotBaseScale = Vector3.one;

    // Hold-to-aim state: aiming starts on press (guide instantly up at default
    // power), drag deltas steer it while held, the shot fires on release.
    private bool isAiming;
    private Vector2 lastAimDirection = Vector2.up;

    // Degrees the current aim is tilted from straight up (positive = right).
    // Accumulated from drag deltas, clamped to ±maxAngleFromVertical.
    private float aimAngleOffset;

    // 0..1 fraction of launchSpeed at release; scales both the previewed
    // path length while dragging and the final launch speed.
    private float lastPullRatio = 1f;

    // Last on-screen pointer position, for frame-to-frame drag deltas.
    // Off-screen/non-finite frames are skipped, so the delta stream never
    // contains garbage and the aim freezes instead of jumping.
    private Vector2 lastPointerScreenPosition;

    void Awake()
    {
        CurrentTier = PickRandomSpawnTier();
        NextTier = PickRandomSpawnTier();
        blackHole = FindFirstObjectByType<BlackHole>();

        if (planetPrefab != null)
        {
            prefabPlanet = planetPrefab.GetComponent<Planet>();
            prefabMerge = planetPrefab.GetComponent<PlanetMerge>();
        }
        if (prefabPlanet == null)
        {
            Debug.LogWarning("PlanetLauncher: planet prefab has no Planet component — sprite previews disabled.");
        }
        if (prefabMerge == null)
        {
            Debug.LogWarning("PlanetLauncher: planet prefab has no PlanetMerge component — the slot preview keeps its Inspector scale instead of tier sizing.");
        }

        if (nextPlanetUIImg != null)
        {
            // Planet PNGs aren't square-safe inside an arbitrary UI box.
            nextPlanetUIImg.preserveAspect = true;
        }

        InitializeDotPool();
        RefreshPreviews();
    }

    void Update()
    {
        // No shooting after a Game Over; also drop a drag that was in progress
        // when the state flipped, so the aim dots don't linger on screen.
        if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing)
        {
            if (isAiming)
            {
                isAiming = false;
                HideAllDots();
            }
            return;
        }

        // Locked while reloading: presses during the cooldown are swallowed
        // entirely (not queued), so the click that fired the shot can't also
        // pre-arm the next one. The player must press again once the slot
        // refills. isAiming can't be true here — aiming always ends at launch.
        if (isWaitingForCooldown)
            return;

        if (Input.GetMouseButtonDown(0))
        {
            BeginAiming();
        }

        if (isAiming && Input.GetMouseButton(0))
        {
            UpdateAimLine();
        }

        if (Input.GetMouseButtonUp(0) && isAiming)
        {
            EndAimingAndLaunch();
        }
    }

    private void InitializeDotPool()
    {
        dotContainer = new GameObject("AimDotPool").transform;

        if (dotPrefab == null)
        {
            Debug.LogWarning("PlanetLauncher: no dot prefab assigned for the aim preview.");
            return;
        }

        dotBaseScale = dotPrefab.transform.localScale;

        // At most one dot per simulated step can ever be placed, so this pool
        // size covers the worst case up front.
        for (int i = 0; i < trajectorySteps; i++)
        {
            GameObject dot = Instantiate(dotPrefab, dotContainer);
            dot.SetActive(false);
            activeDots.Add(dot);
            dotRenderers.Add(dot.GetComponentInChildren<SpriteRenderer>());
        }
    }

    private void BeginAiming()
    {
        // Only start aiming from a pointer position that is actually on screen;
        // touch simulators can report an off-screen position on the first frame.
        if (!TryGetPointerScreenPosition(out Vector2 pointer))
            return;

        if (requirePressOnPlanet && !PressIsOnPlanet(pointer))
            return;

        isAiming = true;
        lastPointerScreenPosition = pointer;

        // Spec'd press feel: the guide appears INSTANTLY on touch — straight
        // up, at the default medium power — before any dragging has happened.
        aimAngleOffset = 0f;
        lastAimDirection = Vector2.up;
        lastPullRatio = defaultPowerRatio;
        DrawTrajectory();
    }

    // Optional press gate: converts the press to world space and accepts it
    // only within grabRadius of the launcher slot.
    private bool PressIsOnPlanet(Vector2 screenPoint)
    {
        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 screenPosition = new Vector3(screenPoint.x, screenPoint.y, -cam.transform.position.z);
        Vector2 worldPoint = cam.ScreenToWorldPoint(screenPosition);
        return Vector2.Distance(worldPoint, transform.position) <= grabRadius;
    }

    private void EndAimingAndLaunch()
    {
        isAiming = false;
        HideAllDots();
        LaunchPlanet();
    }

    private void LaunchPlanet()
    {
        if (planetPrefab == null)
        {
            Debug.LogWarning("PlanetLauncher: no planet prefab assigned.");
            return;
        }

        Vector2 spawnJitter = new Vector2(
            Random.Range(-spawnPositionJitter, spawnPositionJitter),
            Random.Range(-spawnPositionJitter, spawnPositionJitter));
        Vector3 spawnPosition = transform.position + (Vector3)spawnJitter;

        GameObject planetObject = Instantiate(planetPrefab, spawnPosition, Quaternion.identity);

        if (!planetObject.TryGetComponent(out Planet planet))
        {
            planet = planetObject.AddComponent<Planet>();
        }

        // The queued tier overrides whatever tier was baked into the prefab;
        // only merging is allowed to raise it beyond this. PlanetMerge.Start
        // (which runs after this call) sizes the planet for its spawn tier.
        planet.SetTier(CurrentTier);

        if (isRainbowActive)
        {
            // Skill hook: mark the spawned planet as a wildcard here once
            // Planet/PlanetMerge grow rainbow support (adopt tier on first touch).
            isRainbowActive = false;
        }

        if (planetObject.TryGetComponent(out Rigidbody2D rb))
        {
            // Fire along the last direction the player was aiming while holding;
            // the pointer may already be gone on the release frame (touch up), so
            // the live position can't be trusted here. Speed scales with how far
            // the pointer was pulled from the launcher: full pull = full speed.
            rb.linearVelocity = lastAimDirection * (launchSpeed * lastPullRatio);
            rb.AddTorque(Random.Range(-maxSpinTorque, maxSpinTorque), ForceMode2D.Impulse);
        }
        else
        {
            Debug.LogWarning("PlanetLauncher: planet prefab has no Rigidbody2D.");
        }

        // Lock the launcher. The queue does NOT advance here: the slot stays
        // empty for the cooldown, and FinishReload pulls the next planet in.
        isWaitingForCooldown = true;
        RefreshPreviews();
        reloadRoutine = StartCoroutine(ReloadAfterCooldown());
    }

    private IEnumerator ReloadAfterCooldown()
    {
        yield return new WaitForSeconds(launchCooldown);
        FinishReload();
    }

    // Advance the queue: the previewed tier becomes live, a fresh one is drawn
    // for the preview slot, and input is accepted again.
    private void FinishReload()
    {
        reloadRoutine = null;
        isWaitingForCooldown = false;
        CurrentTier = NextTier;
        NextTier = PickRandomSpawnTier();
        RefreshPreviews();
    }

    // Pushes the current queue state into both preview visuals. Sprites come
    // straight from the Planet prefab's tier array and are shown untinted
    // (pure white) — the pre-rendered art must display exactly as authored.
    private void RefreshPreviews()
    {
        if (loadedPlanetRenderer != null)
        {
            Sprite loadedSprite = isWaitingForCooldown ? null : SpriteForTier(CurrentTier);
            loadedPlanetRenderer.sprite = loadedSprite;
            loadedPlanetRenderer.color = Color.white;
            // Reloading (or a missing sprite) leaves the slot visibly empty.
            loadedPlanetRenderer.enabled = loadedSprite != null;

            if (loadedSprite != null && prefabMerge != null)
            {
                // Size the preview to the exact world scale the fired planet
                // will spawn at (PlanetMerge.Start snaps spawns onto the same
                // curve), so a Tier3 in the slot is visibly a Tier3.
                float worldScale = prefabMerge.ScaleForTier(CurrentTier) * launcherVisualScaleModifier;

                // localScale is multiplied by every ancestor's scale before it
                // reaches the screen; divide that back out so the preview's
                // on-screen size matches the arena planet even if the launcher
                // hierarchy isn't scaled at exactly 1.
                Transform previewTransform = loadedPlanetRenderer.transform;
                float parentScale = previewTransform.parent != null
                    ? previewTransform.parent.lossyScale.x
                    : 1f;
                if (!Mathf.Approximately(parentScale, 0f))
                {
                    worldScale /= parentScale;
                }

                previewTransform.localScale = Vector3.one * worldScale;
            }
        }

        if (nextPlanetUIImg != null)
        {
            Sprite nextSprite = SpriteForTier(NextTier);
            nextPlanetUIImg.sprite = nextSprite;
            nextPlanetUIImg.color = Color.white;
            // A UI Image with a null sprite renders as a solid white square;
            // disable it outright instead if the tier has no art yet.
            nextPlanetUIImg.enabled = nextSprite != null;
        }
    }

    private Sprite SpriteForTier(PlanetTier tier)
    {
        return prefabPlanet != null ? prefabPlanet.GetSpriteForTier(tier) : null;
    }

    // Full queue reset for level transitions and restarts (called by
    // GameManager): cancels any in-flight reload cooldown and drag, redraws
    // both queue tiers from scratch, and leaves the launcher armed.
    public void ResetQueue()
    {
        if (reloadRoutine != null)
        {
            StopCoroutine(reloadRoutine);
            reloadRoutine = null;
        }
        isWaitingForCooldown = false;

        if (isAiming)
        {
            isAiming = false;
            HideAllDots();
        }

        CurrentTier = PickRandomSpawnTier();
        NextTier = PickRandomSpawnTier();
        RefreshPreviews();
    }

    // Coroutines die with the component, so a launcher disabled mid-cooldown
    // (level transition, skill takeover) would otherwise come back permanently
    // locked with a stale tier in the slot. Complete the reload immediately
    // instead — the cooldown's anti-spam job is moot while nobody can shoot.
    void OnDisable()
    {
        if (isWaitingForCooldown)
        {
            if (reloadRoutine != null)
            {
                StopCoroutine(reloadRoutine);
            }
            FinishReload();
        }
    }

    private PlanetTier PickRandomSpawnTier()
    {
        return (PlanetTier)Random.Range(0, (int)highestSpawnTier + 1);
    }

    private void UpdateAimLine()
    {
        // While dragging, a finger can slide off screen; skip the delta on
        // those frames and keep showing the last valid aim.
        if (TryGetPointerScreenPosition(out Vector2 pointer))
        {
            Vector2 delta = pointer - lastPointerScreenPosition;
            lastPointerScreenPosition = pointer;

            // Relative control: horizontal finger travel ROTATES the aim,
            // normalized by screen size so the tuning feels identical on any
            // resolution/DPI. Where the finger sits on screen is irrelevant —
            // only movement steers, so sharp angles never need edge reach.
            //
            // Classic sling mapping (invertHorizontalDrag default true): the
            // raw delta.x>0 (finger moving right) computes a RIGHT-leaning
            // angleDelta first, then the sign flips it — so the aim actually
            // bends LEFT, like pulling a rubber band back opposite the throw
            // direction. Drag left bends right the same way, symmetrically.
            float angleDelta = delta.x / Screen.width * angleSensitivity;
            if (invertHorizontalDrag)
            {
                angleDelta = -angleDelta;
            }
            aimAngleOffset = Mathf.Clamp(aimAngleOffset + angleDelta,
                -maxAngleFromVertical, maxAngleFromVertical);

            // Sling tension: dragging DOWN (delta.y negative — screen-space Y
            // runs bottom-to-top) increases lastPullRatio toward full power;
            // dragging UP softens it toward minPowerRatio. This is already
            // the "pull the band back to charge it" mapping — no inversion
            // flag needed, the sign falls out of screen-space Y directly.
            lastPullRatio = Mathf.Clamp(
                lastPullRatio - delta.y / Screen.height * powerSensitivity,
                minPowerRatio, 1f);

            // 0° = straight up; positive tilts right, negative left.
            float radians = aimAngleOffset * Mathf.Deg2Rad;
            lastAimDirection = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        DrawTrajectory();
    }

    // Predictive trajectory: steps the launch velocity through the same
    // per-physics-step integration the real planet will experience — pulling
    // BlackHole.GetPullForce each step (an acceleration, applied mass-
    // independently to every tier, so the dots are exact for any spawn tier)
    // — then lays pooled dot sprites along the curve, one every dotSpacing
    // world units of travelled path, tapering toward the far end.
    private void DrawTrajectory()
    {
        if (activeDots.Count == 0)
            return;

        Vector2 position = transform.position;
        Vector2 velocity = lastAimDirection * (launchSpeed * lastPullRatio);
        float dt = Time.fixedDeltaTime;

        // Scale the previewed distance with pull power: a soft pull simulates
        // fewer steps ahead, so the guide visibly shortens instead of showing a
        // slow planet's full-length deep curve.
        int activeSteps = Mathf.Max(3, Mathf.RoundToInt(trajectorySteps * lastPullRatio));

        // First pass: collect dot positions along the simulated path. A dot is
        // recorded only when the cumulative distance travelled along the curve
        // since the previous dot reaches dotSpacing, so gaps stay even at any
        // launch speed.
        dotPositions.Clear();
        float distanceSinceLastDot = 0f;

        for (int i = 0; i < activeSteps; i++)
        {
            if (blackHole != null)
            {
                velocity += blackHole.GetPullForce(position) * dt;
                // Same orbit braking the real planet gets in BlackHole's
                // FixedUpdate — without this the dots would overshoot the
                // curve on off-center shots.
                velocity = blackHole.ApplyOrbitBrake(position, velocity, dt);
            }

            Vector2 nextPosition = position + velocity * dt;
            distanceSinceLastDot += Vector2.Distance(position, nextPosition);
            position = nextPosition;

            if (distanceSinceLastDot >= dotSpacing)
            {
                dotPositions.Add(position);
                distanceSinceLastDot = 0f;
            }
        }

        // Second pass: activate and place pooled dots now that the total count
        // is known, so the size/alpha taper spans the whole visible path.
        // Dots render as a neutral white guide line — the real planet sprite in
        // the slot already telegraphs the tier, so per-tier tinting is gone.
        // White is reasserted on every dot every frame, so no leftover color
        // (from prefab data or anything else that touched a pooled dot's
        // renderer/material) can survive into this shot.
        int dotCount = Mathf.Min(dotPositions.Count, activeDots.Count);

        for (int i = 0; i < dotCount; i++)
        {
            GameObject dot = activeDots[i];
            dot.SetActive(true);
            dot.transform.position = dotPositions[i];

            float taper = dotCount > 1 ? (float)i / (dotCount - 1) : 0f;
            dot.transform.localScale = dotBaseScale * Mathf.Lerp(1f, endDotScale, taper);

            SpriteRenderer dotRenderer = dotRenderers[i];
            if (dotRenderer != null)
            {
                Color dotColor = Color.white;
                dotColor.a = Mathf.Lerp(startDotAlpha, endDotAlpha, taper);
                dotRenderer.color = dotColor;
            }
        }

        // Park any leftover pool dots from a previous, longer preview.
        for (int i = dotCount; i < activeDots.Count; i++)
        {
            activeDots[i].SetActive(false);
        }
    }

    private void HideAllDots()
    {
        foreach (GameObject dot in activeDots)
        {
            dot.SetActive(false);
        }
    }

    // Pointer position in screen pixels, guarded: fails (returning false)
    // instead of producing garbage when the position is off screen or
    // non-finite — the source of the old "Screen position out of view
    // frustum" error. Relative aiming only ever consumes valid deltas.
    private bool TryGetPointerScreenPosition(out Vector2 pointer)
    {
        Vector3 position = Input.mousePosition;
        pointer = position;

        if (float.IsNaN(position.x) || float.IsNaN(position.y) ||
            float.IsInfinity(position.x) || float.IsInfinity(position.y) ||
            position.x < 0f || position.x > Screen.width ||
            position.y < 0f || position.y > Screen.height)
            return false;

        return true;
    }
}
