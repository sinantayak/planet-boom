using System.Collections.Generic;
using UnityEngine;

public class PlanetLauncher : MonoBehaviour
{
    [SerializeField] private GameObject planetPrefab;
    [SerializeField] private float launchSpeed = 8f;
    [SerializeField] private float spawnPositionJitter = 0.05f;
    [SerializeField] private float maxSpinTorque = 5f;

    [Header("Aim Dots")]
    [SerializeField] private GameObject dotPrefab;
    [SerializeField] private float dotSpacing = 0.25f;
    // Number of physics steps to simulate ahead; more steps = longer preview.
    [SerializeField] private int trajectorySteps = 30;
    // Taper: dots shrink and fade from full size/alpha near the launcher down
    // to these values at the far end of the path.
    [SerializeField] private float endDotScale = 0.35f;
    [SerializeField] private float endDotAlpha = 0.2f;

    [Header("Pull Power")]
    [SerializeField] private float minPullDistance = 0.5f;
    [SerializeField] private float maxPullDistance = 5f;

    [Header("Skills")]
    // Skill hook: while true, the next launched planet is a wildcard that adopts
    // the color of whatever planet it touches first. Consumed on launch.
    public bool isRainbowActive;

    // Preview state: CurrentColor is what the next click fires; NextColor is what
    // follows it (shown in UI later). Both readable by UI code, set only here.
    public PlanetColor CurrentColor { get; private set; }
    public PlanetColor NextColor { get; private set; }

    private static readonly PlanetColor[] AllColors =
        (PlanetColor[])System.Enum.GetValues(typeof(PlanetColor));

    private BlackHole blackHole;

    // Dot pool: pre-instantiated hidden in Awake (one per trajectory step, the
    // maximum ever needed), then repositioned/activated per aim frame. Never
    // destroyed during play — no Instantiate/Destroy churn while dragging.
    private readonly List<GameObject> activeDots = new List<GameObject>();
    private readonly List<SpriteRenderer> dotRenderers = new List<SpriteRenderer>();
    private readonly List<Vector2> dotPositions = new List<Vector2>();
    private Transform dotContainer;
    private Vector3 dotBaseScale = Vector3.one;

    // Hold-to-aim state: aiming starts on press, the dots follow the pointer
    // while held, and the shot fires on release (BBTAN-style).
    private bool isAiming;
    private Vector2 lastAimDirection = Vector2.up;

    // 0..1 fraction of maxPullDistance at release; scales both the previewed
    // path length while dragging and the final launch speed (pool-cue style).
    private float lastPullRatio = 1f;

    void Awake()
    {
        CurrentColor = PickRandomColor();
        NextColor = PickRandomColor();
        blackHole = FindFirstObjectByType<BlackHole>();
        InitializeDotPool();
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
        // touch simulators can report an off-screen position on the first frame,
        // which is what fed garbage into ScreenToWorldPoint (frustum error).
        if (!TryGetAimVector(transform.position, out Vector2 toPointer))
            return;

        isAiming = true;
        lastAimDirection = toPointer.normalized;
        lastPullRatio = ComputePullRatio(toPointer.magnitude);
        UpdateAimLine();
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

        planet.SetColor(CurrentColor);

        // Every shot enters play at tier 1, even if the prefab was saved with a
        // higher Level baked in; only merging is allowed to raise it.
        planet.Level = 1;

        if (isRainbowActive)
        {
            // Skill hook: mark the spawned planet as a wildcard here once
            // Planet/PlanetMerge grow rainbow support (adopt color on first touch).
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

        // Advance the queue: the previewed color becomes live, and a fresh one is
        // drawn for the preview slot.
        CurrentColor = NextColor;
        NextColor = PickRandomColor();
    }

    private PlanetColor PickRandomColor()
    {
        return AllColors[Random.Range(0, AllColors.Length)];
    }

    private void UpdateAimLine()
    {
        // While dragging, a finger can slide off screen; keep showing the last
        // valid direction/pull instead of feeding bad coordinates to the camera.
        if (TryGetAimVector(transform.position, out Vector2 toPointer))
        {
            lastAimDirection = toPointer.normalized;
            lastPullRatio = ComputePullRatio(toPointer.magnitude);
        }

        DrawTrajectory();
    }

    // Predictive trajectory: steps the launch velocity through the same
    // per-physics-step integration the real planet will experience — pulling
    // BlackHole.GetPullForce each step (planet mass is 1, so force equals
    // acceleration) — then lays pooled dot sprites along the curve, one every
    // dotSpacing world units of travelled path, tapering toward the far end.
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
        Color liveColor = PlanetColorPalette.ToUnityColor(CurrentColor);
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
                liveColor.a = Mathf.Lerp(1f, endDotAlpha, taper);
                dotRenderer.color = liveColor;
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

    // Maps the raw pointer distance to a 0..1 power fraction: distances are
    // clamped to [minPullDistance, maxPullDistance], so a full pull gives 1
    // (max speed) and the shortest pull still gives a soft minimum tap.
    private float ComputePullRatio(float pullDistance)
    {
        float clamped = Mathf.Clamp(pullDistance, minPullDistance, maxPullDistance);
        return clamped / maxPullDistance;
    }

    // Screen-to-world conversion, guarded: fails (returning false) instead of
    // producing garbage when there is no camera or the pointer is off screen —
    // the source of the "Screen position out of view frustum" error. Outputs the
    // full (unnormalized) vector so callers get both direction and pull distance.
    private bool TryGetAimVector(Vector3 launchPosition, out Vector2 toPointer)
    {
        toPointer = Vector2.up;

        Camera cam = Camera.main;
        if (cam == null)
            return false;

        Vector3 mouseScreenPosition = Input.mousePosition;
        if (mouseScreenPosition.x < 0f || mouseScreenPosition.x > Screen.width ||
            mouseScreenPosition.y < 0f || mouseScreenPosition.y > Screen.height ||
            float.IsNaN(mouseScreenPosition.x) || float.IsNaN(mouseScreenPosition.y) ||
            float.IsInfinity(mouseScreenPosition.x) || float.IsInfinity(mouseScreenPosition.y))
            return false;

        mouseScreenPosition.z = -cam.transform.position.z;
        Vector3 mouseWorldPosition = cam.ScreenToWorldPoint(mouseScreenPosition);

        Vector2 toMouse = (Vector2)(mouseWorldPosition - launchPosition);
        if (toMouse.sqrMagnitude <= 0.0001f)
            return false;

        toPointer = toMouse;
        return true;
    }
}
