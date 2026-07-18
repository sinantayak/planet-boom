using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

public readonly struct StarRatingEvaluationContext
{
    public int LevelNumber { get; }
    public int BaseRating { get; }
    public int MaximumRating { get; }

    public StarRatingEvaluationContext(int levelNumber, int baseRating, int maximumRating)
    {
        LevelNumber = levelNumber;
        BaseRating = baseRating;
        MaximumRating = maximumRating;
    }
}

// One authored level: the planets the player must CREATE (via merging) to
// clear it. Order is cosmetic — it drives the left-to-right slot layout on
// the MissionHUD. Duplicates are legal and rendered as separate slots (two
// Tier5 entries = two Tier5 sprites side by side, never a "2x" label). Hard
// cap: 3 targets per level, because the MissionHUD has exactly 3 slots;
// extras are ignored with a warning at load.
[System.Serializable]
public class LevelDefinition
{
    public List<PlanetTier> targetTiers = new List<PlanetTier>();

    // Optional reusable objective configuration. Empty keeps existing levels
    // fully compatible by converting targetTiers into ReachTier objectives.
    public List<LevelObjectiveDefinition> objectives = new List<LevelObjectiveDefinition>();

    // Seconds on this level's clock; the level fails when it runs out.
    public float timeLimit = 60f;

    // Star brackets scale off this playtested baseline: clear within this
    // many ELAPSED seconds → 3 stars, within twice it → 2 stars, any slower
    // finish → 1 star (a completed level never shows zero).
    public float threeStarThreshold = 20f;
}

// Owns the mission/level state and the Suika-style lose condition. Merges
// reach it through NotifyMergeCreated (called by PlanetMerge); everything<
// else is self-driven.
public class GameManager : MonoBehaviour
{
    // Criteria calculate a base result first. Run effects may improve that
    // result through this hook, but CalculateStarRating clamps every response
    // so no modifier can reduce an earned rating or exceed the rating scale.
    public static event System.Func<StarRatingEvaluationContext, int, int> ModifyStarRating;
    // CinematicVortex: the moment between "last target fulfilled" and the win
    // popup — the black hole spins up and swallows the board while all input
    // and lose checks are frozen (every gameplay system already gates on
    // State == Playing, so the new state disables them for free).
    public enum GameState { Playing, LevelComplete, GameOver, CinematicVortex, InventoryPaused }

    public static GameManager Instance { get; private set; }

    public GameState State { get; private set; } = GameState.Playing;
    public int CurrentLevelNumber { get; private set; } = 1;

    // Seconds left on the current level's clock. Public for the timer text
    // that will be added to the HUD later; already drives the star rating.<<
    public float RemainingTime { get; private set; }
    // Coins collected from drop flights belong to this level run until a
    // future Level Complete reward flow explicitly commits the final reward.
    public long LevelEarnedCoins { get; private set; }
    public bool IsLevelRewardCommitted { get; private set; }
    public event System.Action<long> LevelEarnedCoinsChanged;
    public event System.Action<IReadOnlyList<LevelObjectiveProgress>> ObjectivesInitialized;
    public event System.Action<LevelObjectiveProgress> ObjectiveProgressChanged;
    public IReadOnlyList<LevelObjective> ActiveObjectives => activeObjectives;

    // The MissionHUD has exactly 3 target slots; levels can't ask for more.
    public const int MaxTargetsPerLevel = 3;

    [Header("Levels")]
    // Authored missions, in order. Leave empty to use the built-in 5-level
    // configuration (BuildDefaultLevels). Levels past the end of this list
    // replay the last authored one so the game never runs out.
    [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();
    [Header("Data-Driven Campaign")]
    [SerializeField] private LevelConfigurationCatalog levelCatalog;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private LevelConfiguration debugLevelConfiguration;
    private LevelConfiguration debugLevelOverride;
#endif
    public LevelConfiguration ActiveLevelConfiguration { get; private set; }
    public string ActiveBackgroundId => ActiveLevelConfiguration != null ? ActiveLevelConfiguration.backgroundId : "default";
    public string ActiveOrbitId => ActiveLevelConfiguration != null ? ActiveLevelConfiguration.orbitId : "default";
    public long ActiveBaseSpaceCoinReward => ActiveLevelConfiguration != null ? ActiveLevelConfiguration.baseSpaceCoinReward : 0;

    [Header("Star Rating Booster Integration")]
    [SerializeField, Range(0, 2)] private int starBoosterRatingAdvantage = 1;
    public int StarBoosterRatingAdvantage => Mathf.Clamp(starBoosterRatingAdvantage, 0, 2);

    [Header("Objective Debug (Editor / Development)")]
    [SerializeField] private bool useDebugObjectiveOverride;
    [SerializeField] private List<LevelObjectiveDefinition> debugObjectives = new List<LevelObjectiveDefinition>();
    [SerializeField] private PlanetTier debugReachTier = PlanetTier.Tier5;
    [SerializeField] [Min(1)] private int debugComboValue = 4;
    [SerializeField] [Min(0.1f)] private float debugSurvivalSeconds = 10f;

    [Header("Boundary / Lose Condition")]
    // Defaults to the BlackHole's transform when left unassigned.
    [SerializeField] private Transform blackHoleCenter;
    [SerializeField] private float maxBoundaryRadius = 6f;
    [SerializeField] private float outsideTimeLimit = 2f;

    [Header("Boundary Projectile Classification")]
    [SerializeField] [Min(0f)] private float boundaryRecentContactMemory = 1f;

    [Header("Level Complete Vortex")]
    // Seconds the CinematicVortex phase runs before the win popup appears —
    // long enough for the boosted pull to visibly swallow the whole board.
    // Must stay >= the farthest planet's actual travel time in BlackHole's
    // spiral (AdvanceSpiral) or the cleanup sweep below cuts the animation
    // short and yanks stragglers out instead of letting them get swallowed;
    // a body starting at maxBoundaryRadius (~2.4) takes ~3s with the current
    // slow-cruise/late-acceleration tuning, so this needs headroom above that.
    [SerializeField] private float vortexDuration = 3.5f;
    // Whether the vortex also drags in and eats meteorites. ON matches the
    // "board fully swallowed" cinematic; turn OFF to restore full meteorite
    // persistence across level wins (they'll sit untouched through the show).
    [SerializeField] private bool vortexSwallowsMeteorites = true;

    [Header("UI")]
    // Countdown readout ("CRITICAL: 1.4s!") shown only for genuine boundary
    // danger; a normal fast incoming launcher planet is intentionally hidden.
    public TextMeshProUGUI countdownText;

    // Top-right live level clock, rendered MM:SS ("08:20"). Optional — when
    // left unassigned the timer float still runs, it just isn't displayed.
    [SerializeField] private TextMeshProUGUI gameplayTimerText;

    [Header("Time Warp Feedback")]
    [SerializeField] private Vector2 timeWarpPopupOffset = new Vector2(0f, -55f);
    [SerializeField] private float timeWarpPopupDuration = 0.65f;
    [SerializeField] private float timeWarpPopupRiseDistance = 35f;
    [SerializeField] private float timeWarpPopupStartScale = 0.65f;
    [SerializeField] private float timeWarpPopupPeakScale = 1.15f;
    [SerializeField] private float timeWarpTimerPulseScale = 1.16f;
    [SerializeField] private float timeWarpTimerPulseDuration = 0.28f;

    // Panel switched on when the state flips to GameOver. Wire its Restart
    // button's OnClick to GameManager.RestartGame.
    public GameObject gameOverPanel;

    // Permanent top-left HUD: "LEVEL N" title plus up to 3 target planet
    // icons that dim live as targets are met. Never hidden during play.
    [FormerlySerializedAs("missionPanel")]
    public MissionHUD missionHUD;

    // Centered win popup, hidden during normal play. Its NEXT button wires
    // itself to AdvanceToNextLevel in code; nothing to hook up beyond the
    // reference here.
    public LevelCompletePanel levelCompletePanel;

    [Header("Level Complete Screen")]
    // Wraps Overlay_BG + the Level Complete Popup under one parent so both
    // switch on/off together and nothing else can render between them;
    // disabled by default in the scene. LevelCompletePanel's own Show/Hide
    // only toggles the popup itself — this group is what actually makes it
    // (and the dimming overlay) visible, since a child under an inactive
    // parent stays invisible regardless of its own active state.
    [SerializeField] private GameObject levelCompleteGroup;

    [Header("Gameplay HUD")]
    // Hidden while the level-complete screen is up (score/timer/next-planet
    // HUD would otherwise show through behind the popup); re-enabled the
    // instant play resumes (next level, replay, or restart).
    [SerializeField] private GameObject safeAreaRoot;
    [SerializeField] private GameObject nextPlanetContainer;

    [Header("Arena Visuals")]
    // World-space visuals hidden alongside the HUD above so the board reads
    // as fully cleared behind the win screen — no orbit guides still
    // spinning, no boundary ring, no stray ball waiting in the launcher.
    [SerializeField] private GameObject boundaryRingVisual;
    [SerializeField] private GameObject orbitLinesVisual;
    [SerializeField] private GameObject launcherVisual;

    // Read by BoundaryVisualizer so the drawn circle can never drift from the
    // radius the lose check actually enforces.
    public Transform BlackHoleCenter => blackHoleCenter;
    public float MaxBoundaryRadius => maxBoundaryRadius;
    public RectTransform GameplayTimerRect => gameplayTimerText != null
        ? gameplayTimerText.rectTransform
        : null;

    // True while at least one outside planet is classified as genuine danger,
    // excluding normal fast incoming launcher bodies. BoundaryVisualizer polls
    // this to flip the ring color. Game Over still uses the unfiltered timers.
    public bool IsAnyPlanetBeyondBoundary { get; private set; }

    // Runtime copy of the current level's targets plus per-target achieved
    // flags. The two lists are index-aligned with each other AND with the
    // MissionHUD's slots, so "slot i achieved" is unambiguous even when a
    // level contains duplicate tiers.
    private readonly List<PlanetTier> activeTargets = new List<PlanetTier>();
    private readonly List<bool> achievedTargets = new List<bool>();
    private readonly List<LevelObjective> activeObjectives = new List<LevelObjective>();
    private readonly List<LevelObjective> missionReachObjectives = new List<LevelObjective>();

    // For resetting the shot queue on level transitions and restarts.
    private PlanetLauncher launcher;

    // The scene's black hole component (blackHoleCenter is just its
    // transform): drives the win vortex via BeginVortex/EndVortex.
    private BlackHole blackHole;

    // Live while the CinematicVortex phase runs; RestartGame must be able to
    // cancel it (and shut the vortex down) if called mid-cinematic.
    private Coroutine vortexRoutine;

    // Last whole second written to gameplayTimerText: the clock only needs a
    // TMP re-layout once per second, not once per frame.
    private int lastDisplayedTimerSeconds = -1;

    // The loaded level's timing, copied out of its LevelDefinition by
    // LoadLevel so ticking and star math never index back into the list.
    private float currentTimeLimit;
    private float currentThreeStarThreshold;
    private int lastEarnedStars;
    private float timeScaleBeforeInventoryPause = 1f;
    private Vector3 gameplayTimerBaseScale = Vector3.one;
    private Coroutine timeWarpFeedbackRoutine;
    private TextMeshProUGUI activeTimeWarpPopup;

    // Seconds each planet has spent continuously outside the boundary. Entries
    // are dropped the moment a planet comes back inside, so the timer measures
    // an unbroken stretch outside, not a lifetime total.
    private readonly Dictionary<Planet, float> outsideTimers = new Dictionary<Planet, float>();
    private readonly List<Planet> staleTimerKeys = new List<Planet>();

    // Selection and final removal are deliberately separate. A future UFO
    // controller can reserve the target, animate it, then call Complete.
    public bool TryReserveCosmicAbductionTarget(out Planet reservedPlanet)
    {
        reservedPlanet = null;
        if (State != GameState.Playing || blackHoleCenter == null)
            return false;

        float bestDistance = float.NegativeInfinity;
        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (!IsEligibleCosmicAbductionTarget(planet, out float distance))
                continue;

            if (distance > bestDistance)
            {
                reservedPlanet = planet;
                bestDistance = distance;
            }
        }

        if (reservedPlanet == null)
            return false;

        outsideTimers.Remove(reservedPlanet);
        if (reservedPlanet.TryGetComponent(out PlanetMerge merge))
            merge.PrepareForDespawn();
        IsAnyPlanetBeyondBoundary = HasGenuineBoundaryDanger();
        Debug.Log($"Cosmic Abduction reserved {reservedPlanet.CurrentTier} at distance {bestDistance:F2}.", this);
        return true;
    }

    public bool CompleteCosmicAbduction(Planet reservedPlanet)
    {
        if (reservedPlanet == null)
            return false;
        Destroy(reservedPlanet.gameObject);
        return true;
    }

    private bool IsEligibleCosmicAbductionTarget(Planet planet, out float distance)
    {
        distance = 0f;
        if (planet == null || !planet.gameObject.activeInHierarchy)
            return false;

        if (planet.TryGetComponent(out PlanetMerge merge) &&
            (merge.IsBeingAbsorbed || merge.IsAbsorbing))
            return false;

        Rigidbody2D body = planet.GetComponent<Rigidbody2D>();
        if (body == null || !body.simulated)
            return false;

        // Share the same incoming-projectile classification as the boundary
        // warning so targeting and danger feedback cannot disagree.
        if (IsNormalIncomingPlanet(planet, body))
            return false;

        distance = Vector2.Distance(planet.transform.position, blackHoleCenter.position);
        return true;
    }

    private bool HasGenuineBoundaryDanger()
    {
        foreach (KeyValuePair<Planet, float> pair in outsideTimers)
        {
            Planet planet = pair.Key;
            if (planet == null || !planet.gameObject.activeInHierarchy)
                continue;
            Rigidbody2D body = planet.GetComponent<Rigidbody2D>();
            if (IsGenuineBoundaryDanger(planet, body, pair.Value))
                return true;
        }
        return false;
    }

    // Central launcher-flight classification shared by CRITICAL feedback and
    // boundary-related targeting. It covers both the initial inward leg and an
    // opposite-side ballistic arc and its near-zero-speed apex: a freely
    // travelling, unsettled body with no current/recent board contact remains
    // normal projectile motion. Low speed alone is not congestion evidence.
    private bool IsNormalIncomingPlanet(Planet planet, Rigidbody2D body)
    {
        if (planet == null || planet.IsSettled || body == null || !body.simulated ||
            blackHoleCenter == null)
            return false;
        if (planet.HasCurrentBoardBodyContact ||
            planet.HadBoardBodyContactRecently(boundaryRecentContactMemory))
            return false;

        return true;
    }

    private bool IsGenuineBoundaryDanger(Planet planet, Rigidbody2D body,
        float continuousOutsideTime)
    {
        if (planet == null)
            return false;
        if (planet.IsSettled)
            return true;

        // Suppress single-frame flashes for every fresh crossing. Once this
        // short grace passes, settled or contact-marked bodies warn. A free
        // projectile does not become dangerous merely because it reaches the
        // low-speed apex of its ballistic path.
        float flashGrace = Mathf.Min(0.25f, Mathf.Max(0f, outsideTimeLimit) * 0.25f);
        if (continuousOutsideTime < flashGrace)
            return false;

        // Active support/blocking outside the ring is congestion danger even
        // if the body still has residual speed from the collision.
        if (planet.HasCurrentBoardBodyContact)
            return true;

        if (!IsNormalIncomingPlanet(planet, body))
            return true;

        // Do not hide even a contact-free projectile all the way to Game Over:
        // consuming most of the authoritative outside allowance is the final
        // visual safety fallback.
        return continuousOutsideTime >= Mathf.Max(flashGrace, outsideTimeLimit * 0.75f);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Cosmic Abduction/Arrange Fresh Incoming Planet")]
    private void DebugArrangeFreshIncomingAbductionTest()
    {
        Planet[] planets = FindObjectsByType<Planet>(FindObjectsSortMode.None);
        Vector2 outward = Vector2.up;
        bool arranged = planets.Length > 0 && DebugPlaceAbductionPlanet(
            planets[0], outward, maxBoundaryRadius + 1f, -outward * 7f);
        outsideTimers.Clear();
        Debug.Log($"Cosmic Abduction DEBUG: fresh fast incoming planet arranged={arranged}; " +
                  "it must not be selected while moving inward.", this);
    }

    [ContextMenu("DEBUG Cosmic Abduction/Arrange Multiple Valid Planets")]
    private void DebugArrangeMultipleAbductionTargets()
    {
        Planet[] planets = FindObjectsByType<Planet>(FindObjectsSortMode.None);
        int count = 0;
        if (planets.Length > 0 && DebugPlaceAbductionPlanet(
                planets[0], Vector2.right, maxBoundaryRadius * 0.45f, Vector2.zero)) count++;
        if (planets.Length > 1 && DebugPlaceAbductionPlanet(
                planets[1], Vector2.up, maxBoundaryRadius * 0.8f, Vector2.zero)) count++;
        outsideTimers.Clear();
        Debug.Log($"Cosmic Abduction DEBUG: arranged {count} valid planets; the outermost must be selected.", this);
    }

    [ContextMenu("DEBUG Cosmic Abduction/Arrange No Valid Planets")]
    private void DebugArrangeNoAbductionTargets()
    {
        Planet[] planets = FindObjectsByType<Planet>(FindObjectsSortMode.None);
        float radius = Mathf.Max(0.5f, maxBoundaryRadius * 0.45f);
        for (int i = 0; i < planets.Length; i++)
        {
            float angle = planets.Length > 0 ? 2f * Mathf.PI * i / planets.Length : 0f;
            DebugPlaceAbductionPlanet(planets[i], new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)),
                radius, Vector2.right * 7f);
        }
        outsideTimers.Clear();
        IsAnyPlanetBeyondBoundary = false;
        UpdateCountdownUI(-1f);
        Debug.Log("Cosmic Abduction DEBUG: all normal planets are fast/ineligible.", this);
    }

    [ContextMenu("DEBUG Cosmic Abduction/Log Meteor Exclusion")]
    private void DebugLogCosmicAbductionMeteorExclusion()
    {
        int meteorCount = FindObjectsByType<Meteorite>(FindObjectsSortMode.None).Length;
        Debug.Log($"Cosmic Abduction DEBUG: {meteorCount} meteor(s) present; target scan only enumerates Planet components.", this);
    }

    private bool DebugPlaceAbductionPlanet(Planet planet, Vector2 outward, float distance, Vector2 velocity)
    {
        if (planet == null || blackHoleCenter == null ||
            !planet.TryGetComponent(out Rigidbody2D body))
            return false;

        outward = outward.sqrMagnitude > 0f ? outward.normalized : Vector2.right;
        planet.transform.position = (Vector2)blackHoleCenter.position + outward * distance;
        body.simulated = true;
        body.linearVelocity = velocity;
        body.angularVelocity = 0f;
        return true;
    }
#endif

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("GameManager: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        blackHole = FindFirstObjectByType<BlackHole>();
        if (blackHoleCenter == null)
        {
            if (blackHole != null)
            {
                blackHoleCenter = blackHole.transform;
            }
            else
            {
                Debug.LogWarning("GameManager: no BlackHoleCenter assigned and no BlackHole found — boundary check disabled until one exists.");
            }
        }

        // Drop unusable entries first: this also catches scenes saved under the
        // old BoomTarget schema, whose serialized data doesn't survive the
        // switch to flat tier lists and would deserialize as empty levels — an
        // empty target list would otherwise count as instantly complete.
        levels.RemoveAll(level => level == null ||
            ((level.targetTiers == null || level.targetTiers.Count == 0) &&
             (level.objectives == null || level.objectives.Count == 0)));
        if (levels.Count == 0)
        {
            BuildDefaultLevels();
        }

        launcher = FindFirstObjectByType<PlanetLauncher>();

        if (gameplayTimerText != null)
            gameplayTimerBaseScale = gameplayTimerText.rectTransform.localScale;

        // Start hidden; FixedUpdate re-shows it whenever a planet is outside.
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }

        if (levelCompletePanel != null)
        {
            levelCompletePanel.Hide();
        }

        // Defensive start state regardless of how the scene was left
        // authored: gameplay HUD visible, the level-complete group hidden.
        HideLevelCompleteScreen();

        LevelConfiguration mapSelection = CampaignLevelSelection.Consume();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (mapSelection != null)
            debugLevelOverride = mapSelection;
#endif
        LoadLevel(mapSelection != null ? mapSelection.levelNumber : 1);
    }

    void OnEnable()
    {
        AudioManager.MergeRegistered += HandleMergeRegistered;
    }

    void OnDisable()
    {
        AudioManager.MergeRegistered -= HandleMergeRegistered;
    }

    void OnDestroy()
    {
        ResetTimeWarpFeedback();
        if (State == GameState.InventoryPaused)
        {
            Time.timeScale = timeScaleBeforeInventoryPause;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        // Survival advances only during live gameplay. Resolve it before the
        // fail clock so a 90-second objective can complete at 90s exactly.
        if (State == GameState.Playing)
            ApplyObjectiveSignal(LevelObjectiveType.Survival, Time.deltaTime);

        // The level clock only runs while actually playing — frozen during the
        // win popup and after a game over. Running out fails the level.
        if (State == GameState.Playing && RemainingTime > 0f)
        {
            RemainingTime = Mathf.Max(0f, RemainingTime - Time.deltaTime);
            if (RemainingTime <= 0f)
            {
                TriggerGameOver($"time limit reached ({currentTimeLimit:F0}s) with mission unfinished: {DescribeTargets()}.");
            }
        }

        UpdateTimerUI();
    }

    // Raw whole-seconds readout of RemainingTime ("45", "44", ...). Ceil,
    // not floor: the display holds the full limit through the first second and
    // only reads "0" once the clock is truly exhausted. Skipped gracefully
    // when no text is wired.
    private void UpdateTimerUI()
    {
        if (gameplayTimerText == null)
            return;

        int totalSeconds = Mathf.CeilToInt(RemainingTime);
        if (totalSeconds == lastDisplayedTimerSeconds)
            return;
        lastDisplayedTimerSeconds = totalSeconds;

        gameplayTimerText.text = totalSeconds.ToString();
    }

    public bool TryPauseForInventory()
    {
        if (State != GameState.Playing || Time.timeScale <= 0f)
            return false;

        timeScaleBeforeInventoryPause = Time.timeScale;
        State = GameState.InventoryPaused;
        Time.timeScale = 0f;
        return true;
    }

    public bool TryResumeFromInventory()
    {
        if (State != GameState.InventoryPaused)
            return false;

        Time.timeScale = timeScaleBeforeInventoryPause;
        State = GameState.Playing;
        return true;
    }

    public bool TryAddBonusTime(float seconds)
    {
        if (State != GameState.Playing || seconds <= 0f)
            return false;

        // Keep elapsed-time/star calculations stable: currentTimeLimit is the
        // reference total used by CalculateStarRating, so both sides grow by
        // the same bonus and already-spent time does not become negative.
        RemainingTime += seconds;
        currentTimeLimit += seconds;
        lastDisplayedTimerSeconds = -1;
        UpdateTimerUI();
        PlayTimeWarpFeedback(seconds);
        return true;
    }

    public bool TryCollectLevelCoins(long amount)
    {
        // A flight launched during play can legitimately arrive after the
        // final merge has moved us into CinematicVortex/LevelComplete. It can
        // also finish while inventory is paused or after Game Over, where a
        // future Continue/Life flow must retain the same run. Fresh-run and
        // abandon paths reset explicitly through LoadLevel/Discard instead.
        if (amount <= 0 || IsLevelRewardCommitted)
            return false;

        long newAmount = SaturatingAdd(LevelEarnedCoins, amount);
        if (newAmount == LevelEarnedCoins)
            return false;
        LevelEarnedCoins = newAmount;
        LevelEarnedCoinsChanged?.Invoke(LevelEarnedCoins);
        return true;
    }

    // Future Level Complete UI supplies its calculated base and star rewards;
    // this method adds the run's dropped coins and performs exactly one
    // permanent PlayerData balance mutation. The guard makes repeated button
    // callbacks/idempotency mistakes unable to grant the reward twice.
    public bool TryCommitLevelReward(long baseLevelReward, long starReward,
        out long committedTotal)
    {
        committedTotal = 0;
        if (State != GameState.LevelComplete || IsLevelRewardCommitted ||
            baseLevelReward < 0 || starReward < 0)
            return false;

        long total = SaturatingAdd(baseLevelReward, starReward);
        total = SaturatingAdd(total, LevelEarnedCoins);
        if (total <= 0 || PlayerDataPersistenceManager.Instance == null ||
            !PlayerDataPersistenceManager.Instance.AddSpaceCoin(total))
            return false;

        IsLevelRewardCommitted = true;
        committedTotal = total;
        Debug.Log($"GameManager: committed level reward {total} Space Coin " +
                  $"(base={baseLevelReward}, stars={starReward}, drops={LevelEarnedCoins}).", this);
        return true;
    }

    // Abandoning/restarting starts a fresh run and discards uncommitted coins.
    // A future Continue/Life flow should resume without calling this method,
    // preserving the current session earnings after Game Over.
    public void DiscardLevelEarnedCoins()
    {
        bool changed = LevelEarnedCoins != 0;
        LevelEarnedCoins = 0;
        IsLevelRewardCommitted = false;
        if (changed)
            LevelEarnedCoinsChanged?.Invoke(0);
    }

    private static long SaturatingAdd(long current, long amount)
    {
        if (amount <= 0)
            return current;
        return current > long.MaxValue - amount ? long.MaxValue : current + amount;
    }

    private void PlayTimeWarpFeedback(float bonusSeconds)
    {
        if (gameplayTimerText == null)
            return;

        ResetTimeWarpFeedback();

        RectTransform timerRect = gameplayTimerText.rectTransform;
        GameObject popupObject = new GameObject("TimeWarpBonusPopup", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI), typeof(CanvasGroup));
        RectTransform popupRect = popupObject.GetComponent<RectTransform>();
        popupRect.SetParent(timerRect.parent, false);
        popupRect.anchorMin = timerRect.anchorMin;
        popupRect.anchorMax = timerRect.anchorMax;
        popupRect.pivot = timerRect.pivot;
        popupRect.sizeDelta = timerRect.sizeDelta;
        popupRect.anchoredPosition = timerRect.anchoredPosition + timeWarpPopupOffset;
        popupRect.localScale = Vector3.one * Mathf.Max(0.01f, timeWarpPopupStartScale);
        popupRect.SetSiblingIndex(timerRect.GetSiblingIndex() + 1);

        activeTimeWarpPopup = popupObject.GetComponent<TextMeshProUGUI>();
        activeTimeWarpPopup.font = gameplayTimerText.font;
        activeTimeWarpPopup.fontSharedMaterial = gameplayTimerText.fontSharedMaterial;
        activeTimeWarpPopup.fontSize = gameplayTimerText.fontSize;
        activeTimeWarpPopup.fontStyle = FontStyles.Bold;
        activeTimeWarpPopup.alignment = TextAlignmentOptions.Center;
        activeTimeWarpPopup.color = gameplayTimerText.color;
        activeTimeWarpPopup.raycastTarget = false;
        activeTimeWarpPopup.text = FormatBonusTime(bonusSeconds);

        timeWarpFeedbackRoutine = StartCoroutine(AnimateTimeWarpFeedback(
            timerRect, popupRect, popupObject.GetComponent<CanvasGroup>()));
    }

    private IEnumerator AnimateTimeWarpFeedback(
        RectTransform timerRect, RectTransform popupRect, CanvasGroup popupCanvasGroup)
    {
        Vector2 popupStartPosition = popupRect.anchoredPosition;
        float popupDuration = Mathf.Max(0.05f, timeWarpPopupDuration);
        float pulseDuration = Mathf.Clamp(timeWarpTimerPulseDuration, 0.05f, popupDuration);
        float elapsed = 0f;

        while (elapsed < popupDuration && popupRect != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float popupT = Mathf.Clamp01(elapsed / popupDuration);
            float popT = Mathf.Clamp01(popupT / 0.25f);
            float popupScale = popupT < 0.25f
                ? Mathf.Lerp(timeWarpPopupStartScale, timeWarpPopupPeakScale, SmoothStep01(popT))
                : Mathf.Lerp(timeWarpPopupPeakScale, 1f, SmoothStep01((popupT - 0.25f) / 0.75f));
            popupRect.localScale = Vector3.one * Mathf.Max(0.01f, popupScale);
            popupRect.anchoredPosition = popupStartPosition + Vector2.up * (timeWarpPopupRiseDistance * popupT);
            popupCanvasGroup.alpha = 1f - SmoothStep01(Mathf.InverseLerp(0.45f, 1f, popupT));

            if (timerRect != null)
            {
                float pulseT = Mathf.Clamp01(elapsed / pulseDuration);
                float pulse = Mathf.Sin(pulseT * Mathf.PI);
                timerRect.localScale = gameplayTimerBaseScale * Mathf.Lerp(1f, timeWarpTimerPulseScale, pulse);
            }

            yield return null;
        }

        timeWarpFeedbackRoutine = null;
        if (timerRect != null)
            timerRect.localScale = gameplayTimerBaseScale;
        if (activeTimeWarpPopup != null)
            Destroy(activeTimeWarpPopup.gameObject);
        activeTimeWarpPopup = null;
    }

    private void ResetTimeWarpFeedback()
    {
        if (timeWarpFeedbackRoutine != null)
        {
            StopCoroutine(timeWarpFeedbackRoutine);
            timeWarpFeedbackRoutine = null;
        }

        if (gameplayTimerText != null)
            gameplayTimerText.rectTransform.localScale = gameplayTimerBaseScale;

        if (activeTimeWarpPopup != null)
            Destroy(activeTimeWarpPopup.gameObject);
        activeTimeWarpPopup = null;
    }

    private static float SmoothStep01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static string FormatBonusTime(float seconds)
    {
        float rounded = Mathf.Round(seconds);
        return Mathf.Approximately(seconds, rounded)
            ? $"+{rounded:0}s"
            : $"+{seconds:0.#}s";
    }

    void FixedUpdate()
    {
        if (State != GameState.Playing || blackHoleCenter == null)
        {
            IsAnyPlanetBeyondBoundary = false;
            return;
        }

        // Destroyed planets leave fake-null keys behind; sweep them first so
        // the dictionary can't grow forever.
        staleTimerKeys.Clear();
        foreach (Planet key in outsideTimers.Keys)
        {
            if (key == null)
                staleTimerKeys.Add(key);
        }
        foreach (Planet key in staleTimerKeys)
        {
            outsideTimers.Remove(key);
        }

        // The countdown UI shows the single most endangered planet: the one
        // with the longest unbroken stretch outside, i.e. the least time left.
        float worstTimer = -1f;
        bool anyGenuineBoundaryDanger = false;

        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (!planet.gameObject.activeInHierarchy)
                continue;

            // A planet melting into a merge winner is already on its way out;
            // it must not be able to lose the game from beyond the line.
            if (planet.TryGetComponent(out PlanetMerge merge) && merge.IsBeingAbsorbed)
            {
                outsideTimers.Remove(planet);
                continue;
            }

            float distance = Vector2.Distance(planet.transform.position, blackHoleCenter.position);
            if (distance <= maxBoundaryRadius)
            {
                outsideTimers.Remove(planet);
                continue;
            }

            outsideTimers.TryGetValue(planet, out float timer);
            timer += Time.fixedDeltaTime;
            outsideTimers[planet] = timer;

            Rigidbody2D body = planet.GetComponent<Rigidbody2D>();
            if (IsGenuineBoundaryDanger(planet, body, timer))
            {
                anyGenuineBoundaryDanger = true;
                if (timer > worstTimer)
                    worstTimer = timer;
            }

            if (timer >= outsideTimeLimit)
            {
                TriggerGameOver($"a {planet.CurrentTier} planet stayed {outsideTimeLimit:F1}s outside " +
                                $"the boundary (distance {distance:F2} > radius {maxBoundaryRadius:F2}).");
                return;
            }
        }

        IsAnyPlanetBeyondBoundary = anyGenuineBoundaryDanger;
        UpdateCountdownUI(worstTimer);
    }

    // worstTimer < 0 means no outside planet is genuine danger this step.
    private void UpdateCountdownUI(float worstTimer)
    {
        if (countdownText == null)
            return;

        if (worstTimer < 0f)
        {
            countdownText.gameObject.SetActive(false);
            return;
        }

        float remaining = Mathf.Max(0f, outsideTimeLimit - worstTimer);
        countdownText.gameObject.SetActive(true);
        countdownText.text = $"CRITICAL: {remaining:F1}s!";
    }

    // Swaps the whole screen from "playing" to "level complete" in one call:
    // the group holding Overlay_BG + the popup comes on, and everything else
    // (HUD, orbit/boundary visuals, the launcher, any surviving meteorite)
    // goes off so nothing shows through behind the dim overlay.
    private void ShowLevelCompleteScreen()
    {
        if (levelCompleteGroup != null)
        {
            levelCompleteGroup.SetActive(true);
        }
        SetGameplayElementsVisible(false);
        SetSurvivingMeteoritesVisible(false);
    }

    // Reverse of ShowLevelCompleteScreen — called by every path that returns
    // to actual play (next level, replay, or a restart that interrupts the
    // win screen).
    private void HideLevelCompleteScreen()
    {
        if (levelCompleteGroup != null)
        {
            levelCompleteGroup.SetActive(false);
        }
        SetGameplayElementsVisible(true);
        SetSurvivingMeteoritesVisible(true);
    }

    private void SetGameplayElementsVisible(bool visible)
    {
        if (safeAreaRoot != null)
        {
            safeAreaRoot.SetActive(visible);
        }
        if (nextPlanetContainer != null)
        {
            nextPlanetContainer.SetActive(visible);
        }
        if (boundaryRingVisual != null)
        {
            boundaryRingVisual.SetActive(visible);
        }
        if (orbitLinesVisual != null)
        {
            orbitLinesVisual.SetActive(visible);
        }
        if (launcherVisual != null)
        {
            launcherVisual.SetActive(visible);
        }
    }

    // Meteorites are the one gameplay body that survives a level win BY
    // DESIGN (ClearBoard only ever destroys them on a hard RestartGame — see
    // its clearMeteorites param). When vortexSwallowsMeteorites is off (as
    // configured in this scene), a meteorite that was never pulled into the
    // vortex just sits there fully active — visible and still physically
    // simulated — floating behind the level-complete popup. There's no
    // single tracked "carry-over" reference to hide; every live Meteorite
    // in the scene at this moment is, by definition, the one carrying over.
    //
    // FindObjectsInactive.Include (not the Exclude default used elsewhere in
    // this file) is required on BOTH ends: without it, the restore call
    // would search only for ACTIVE meteorites and find none, since Show just
    // deactivated all of them — the round trip would never re-enable them.
    //
    // Disabling the GameObject also freezes its Rigidbody2D/coroutines for
    // the duration of the screen (no physics step, no FixedUpdate) and
    // leaves its position untouched, so re-enabling drops it back exactly
    // where the player left it.
    private void SetSurvivingMeteoritesVisible(bool visible)
    {
        foreach (Meteorite meteorite in FindObjectsByType<Meteorite>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (meteorite != null)
            {
                meteorite.gameObject.SetActive(visible);
            }
        }
    }

    // Called by PlanetMerge after a max-tier BOOM has claimed its victims.
    // Booms no longer advance the mission (targets are about CREATING planets,
    // and a boom only destroys); kept as the hook for future scoring/VFX.
    public void NotifyBoom(PlanetTier tier)
    {
        Debug.Log($"GameManager: {tier} chain boom on level {CurrentLevelNumber}.");
    }

    // Called by PlanetMerge the moment a merge produces its upgraded planet.
    // Fulfils at most ONE unachieved matching target per merge — Level 4's
    // [Tier5, Tier5] needs two separate Tier5 merges, not one counted twice.
    //
    // Dependency rule: a created planet is only allowed to consume a target
    // when NO higher-tier target is still open. Merging upward eats the very
    // planets it is built from, so without this guard a level like
    // [Tier4, Tier3] is trivialized: the first Tier3 on the way to the Tier4
    // would claim the Tier3 slot, then vanish into the Tier4 merge that
    // finishes the level. With the guard, targets resolve strictly from the
    // highest tier down — the Tier4 must exist first, and only a Tier3
    // created AFTER that (a genuine, separate Tier3) can claim its slot.
    public void NotifyMergeCreated(PlanetTier createdTier)
    {
        if (State != GameState.Playing)
            return;

        // Guaranteed Time Rush time is awarded only from this normal planet
        // merge completion hook. Time Drops and Time Warp keep their own data
        // and triggers while sharing the authoritative RemainingTime state.
        if (ActiveLevelConfiguration != null &&
            ActiveLevelConfiguration.TryGetMergeTimeBonus(createdTier, out float mergeBonus))
        {
            if (TryAddBonusTime(mergeBonus))
                Debug.Log($"Time Rush: {createdTier} merge +{mergeBonus:0.#}s.", ActiveLevelConfiguration);
        }

        foreach (LevelObjective objective in activeObjectives)
        {
            if (objective.Type == LevelObjectiveType.ReachTier &&
                !objective.IsCompleted && objective.TargetTier > createdTier)
            {
                Debug.Log($"GameManager: {createdTier} created but not counted; " +
                          $"{objective.TargetTier} must be achieved first.");
                return;
            }
        }

        // The created tier matches an open target — but defer it while any
        // higher-tier target remains unfulfilled, because this planet is
        // (presumptively) a building block that will be consumed on the way
        // to that bigger goal.
        foreach (LevelObjective objective in activeObjectives)
        {
            if (objective.Type != LevelObjectiveType.ReachTier ||
                objective.IsCompleted || objective.TargetTier != createdTier)
                continue;

            if (objective.Apply(LevelObjectiveType.ReachTier, 1f, 0, createdTier))
                PublishObjectiveProgress(objective);
            return;
        }
    }

    // Max Target Tier Collision Guard, asked by PlanetMerge before combining
    // two 'tier' planets (which would create a tier+1). Allowed only while an
    // UNFULFILLED target sits strictly above 'tier' — i.e. the merge is still
    // a step toward an open goal. Otherwise the pair must bounce like plain
    // physics objects: without this, Level 5's two required Tier5s would
    // touch and fuse into a useless Tier6, eating the player's progress.
    // Side effects of the rule, both intentional:
    //  - planets AT the highest open target tier never merge upward, so an
    //    achieved-or-pending target planet can't be destroyed by accident;
    //  - once every target is achieved the level ends anyway, and while the
    //    win popup is up (state != Playing) nothing combines behind it.
    public bool CanMerge(PlanetTier tier)
    {
        if (State != GameState.Playing)
            return false;

        if (activeObjectives.Count == 0)
            return true;

        bool hasIncompleteReachTier = false;
        foreach (LevelObjective objective in activeObjectives)
        {
            if (objective.Type != LevelObjectiveType.ReachTier || objective.IsCompleted)
                continue;
            hasIncompleteReachTier = true;
            if (objective.TargetTier > tier)
                return true;
        }

        return !hasIncompleteReachTier && !AreAllRequiredObjectivesCompleted();
    }

    public bool CanCreatePlanetTier(PlanetTier resultTier)
    {
        if (!System.Enum.IsDefined(typeof(PlanetTier), resultTier))
            return false;
        if (ActiveLevelConfiguration != null)
        {
            if (!System.Enum.IsDefined(typeof(PlanetTier), ActiveLevelConfiguration.maximumAllowedMergeTier) ||
                resultTier > ActiveLevelConfiguration.maximumAllowedMergeTier)
                return false;
        }
        return UnlockManager.Instance == null || UnlockManager.Instance.IsUnlocked(resultTier);
    }

    private void HandleMergeRegistered(int combo)
    {
        if (State != GameState.Playing)
            return;
        ApplyObjectiveSignal(LevelObjectiveType.MergeCount, 1f);
        if (State == GameState.Playing)
            ApplyObjectiveSignal(LevelObjectiveType.ComboTarget, 0f, combo);
    }

    public void NotifyMeteorDestroyed(int amount)
    {
        if (State == GameState.Playing && amount > 0)
            ApplyObjectiveSignal(LevelObjectiveType.MeteorObjective, amount);
    }

    private void ApplyObjectiveSignal(LevelObjectiveType type, float amount,
        int combo = 0, PlanetTier createdTier = default)
    {
        if (State != GameState.Playing)
            return;

        // Snapshot iteration is unnecessary: publishing never mutates the
        // active list, and completion changes state only after the loop.
        bool changed = false;
        foreach (LevelObjective objective in activeObjectives)
        {
            if (!objective.IsCompleted &&
                objective.Apply(type, amount, combo, createdTier))
            {
                ObjectiveProgressChanged?.Invoke(objective.Snapshot);
                changed = true;
            }
        }

        if (changed)
            TryCompleteObjectives();
    }

    private void PublishObjectiveProgress(LevelObjective objective)
    {
        ObjectiveProgressChanged?.Invoke(objective.Snapshot);

        int missionSlot = missionReachObjectives.IndexOf(objective);
        if (missionSlot >= 0 && objective.IsCompleted)
        {
            achievedTargets[missionSlot] = true;
            missionHUD?.MarkAchieved(missionSlot);
        }

        TryCompleteObjectives();
    }

    private bool AreAllRequiredObjectivesCompleted()
    {
        bool hasRequired = false;
        foreach (LevelObjective objective in activeObjectives)
        {
            if (!objective.IsRequired)
                continue;
            hasRequired = true;
            if (!objective.IsCompleted)
                return false;
        }
        return hasRequired;
    }

    private void TryCompleteObjectives()
    {
        if (State == GameState.Playing && AreAllRequiredObjectivesCompleted())
            CompleteLevel();
    }

    // The win no longer snaps the popup open: it enters the CinematicVortex
    // phase first — input frozen via the state flag (the launcher ignores
    // everything while State != Playing, so the player watches, not shoots),
    // the black hole spins up and swallows the board, and only then does the
    // popup bloom out of the core. The old "board stays behind the popup"
    // behavior is gone by design: the vortex IS the board clear now.
    private void CompleteLevel()
    {
        State = GameState.CinematicVortex;

        // Rate before anything else can touch the clock: Update stops ticking
        // the moment State leaves Playing, so this reads the true finish time
        // no matter how long the cinematic runs.
        int starsEarned = CalculateStarRating();
        lastEarnedStars = starsEarned;
        GrantActiveLevelUnlockRewards();
        BoosterInventoryManager.Instance?.EndCurrentRun();
        PlayerDataPersistenceManager.Instance?.RecordLevelCompleted(CurrentLevelNumber, starsEarned);
        Debug.Log($"GameManager: LEVEL {CurrentLevelNumber} COMPLETE! " +
                  $"Cleared in {currentTimeLimit - RemainingTime:F1}s of {currentTimeLimit:F0}s " +
                  $"(3-star pace: {currentThreeStarThreshold:F0}s) — {starsEarned} star(s).");

        // A planet may have been mid-countdown outside the boundary; the
        // readout is meaningless in the frozen state.
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        vortexRoutine = StartCoroutine(RunLevelCompleteVortex(starsEarned));
    }

    // The cinematic itself: vortex on → wait until the board is actually
    // empty (or the safety cap expires) → vortex off → sweep whatever the
    // pull didn't physically reach in time → popup out of the core.
    private IEnumerator RunLevelCompleteVortex(int starsEarned)
    {
        if (blackHole != null)
        {
            blackHole.BeginVortex(vortexSwallowsMeteorites);
        }

        // Dynamic reveal: end the wait the instant every body has actually
        // been swallowed by the core instead of always sitting out the full
        // vortexDuration — that turned an already-empty screen into dead
        // time before the popup appeared. vortexDuration still bounds the
        // wait as a safety cap in case a straggler never arrives (e.g. stuck
        // mid-fusion), so the cinematic can't hang indefinitely.
        float elapsed = 0f;
        while (elapsed < vortexDuration && blackHole != null && blackHole.HasRemainingVortexBodies())
        {
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (blackHole != null)
        {
            blackHole.EndVortex();
        }

        // The vortex swallows bodies that reach the core, but a straggler
        // parked at the rim might not arrive within vortexDuration — the
        // sweep guarantees the next level starts clean regardless.
        ClearBoard(clearMeteorites: vortexSwallowsMeteorites);

        vortexRoutine = null;
        State = GameState.LevelComplete;
        ShowLevelCompleteScreen();

        if (levelCompletePanel != null)
        {
            Vector3 core = blackHoleCenter != null ? blackHoleCenter.position : Vector3.zero;
            levelCompletePanel.ShowFromWorldPoint(starsEarned, core);
        }
    }

    // Pace-based rating against the level's own playtested baseline: elapsed
    // time within threeStarThreshold → 3 stars, within twice it → 2 stars,
    // anything slower → 1 star. The floor is always 1 — a completed level
    // can't show zero.
    private int CalculateStarRating()
    {
        float timePassed = currentTimeLimit - RemainingTime;
        int baseRating = ActiveLevelConfiguration != null && ActiveLevelConfiguration.starCriteria != null
            ? ActiveLevelConfiguration.starCriteria.Evaluate(timePassed, RemainingTime)
            : timePassed <= currentThreeStarThreshold ? 3 : timePassed <= currentThreeStarThreshold * 2f ? 2 : 1;

        int rating = baseRating;
        var context = new StarRatingEvaluationContext(CurrentLevelNumber, baseRating, 3);
        if (ModifyStarRating != null)
        {
            foreach (System.Func<StarRatingEvaluationContext, int, int> modifier in ModifyStarRating.GetInvocationList())
                rating = Mathf.Clamp(modifier(context, rating), rating, context.MaximumRating);
        }
        return rating;
    }

    private void GrantActiveLevelUnlockRewards()
    {
        if (ActiveLevelConfiguration?.unlockRewards == null || UnlockManager.Instance == null) return;
        foreach (LevelUnlockReward reward in ActiveLevelConfiguration.unlockRewards)
            if (reward != null && !string.IsNullOrWhiteSpace(reward.stableContentId)) UnlockManager.Instance.Unlock(reward.CanonicalId);
    }

    public bool TryCommitConfiguredLevelReward(out long committedTotal)
    {
        long starReward = ActiveLevelConfiguration?.starCriteria?.CoinRewardFor(lastEarnedStars) ?? 0;
        return TryCommitLevelReward(ActiveBaseSpaceCoinReward, starReward, out committedTotal);
    }

    // Wired to the Level Completed popup's NEXT button. Public and state-
    // guarded so a double-click or stray call outside the popup can't skip
    // levels or wipe a live board.
    public void AdvanceToNextLevel()
    {
        if (State != GameState.LevelComplete)
            return;

        if (levelCompletePanel != null)
        {
            levelCompletePanel.Hide();
        }
        HideLevelCompleteScreen();

        // The win vortex already swallowed the board before this popup ever
        // appeared; this sweep is a belt-and-braces no-op in the normal flow.
        // clearMeteorites stays false so that when vortexSwallowsMeteorites
        // is turned OFF, surviving meteorites still persist into the next
        // level as designed.
        ClearBoard(clearMeteorites: false);

        if (launcher != null)
        {
            launcher.ResetQueue();
        }

        // State first, then LoadLevel: same ordering rule as RestartGame, so
        // the first frame of the new level is fully playable. LoadLevel then
        // pushes the new level number and targets into the MissionHUD.
        State = GameState.Playing;
        LoadLevel(CurrentLevelNumber + 1);
    }

    // Wired to the win popup's BACK button: return to the level just before
    // the one that was completed. Same teardown as AdvanceToNextLevel, but
    // the level index moves down instead of up — clamped to 1 so it can
    // never step below the first level. State-guarded so it only fires from
    // the win popup.
    public void ReturnToPreviousLevel()
    {
        if (State != GameState.LevelComplete)
            return;

        if (levelCompletePanel != null)
        {
            levelCompletePanel.Hide();
        }
        HideLevelCompleteScreen();

        // Same belt-and-braces sweep as AdvanceToNextLevel; meteorites persist
        // into the previous level exactly as they would going forward.
        ClearBoard(clearMeteorites: false);

        if (launcher != null)
        {
            launcher.ResetQueue();
        }

        State = GameState.Playing;
        LoadLevel(Mathf.Max(1, CurrentLevelNumber - 1));
    }

    // Wired to the win popup's RESTART button: replay the level just
    // completed (unlike RestartGame, which is the Game Over path back to
    // level 1). Same teardown as AdvanceToNextLevel — clear board, fresh
    // launcher queue, fresh level clock via LoadLevel — but the level index
    // stays put. State-guarded so it only fires from the win popup.
    public void ReplayCurrentLevel()
    {
        if (State != GameState.LevelComplete)
            return;

        Debug.Log($"GameManager: replaying level {CurrentLevelNumber}.");

        if (levelCompletePanel != null)
        {
            levelCompletePanel.Hide();
        }
        HideLevelCompleteScreen();

        // Meteorites persist across a replay too — they were part of the
        // board state the player actually won with.
        ClearBoard(clearMeteorites: false);

        if (launcher != null)
        {
            launcher.ResetQueue();
        }

        State = GameState.Playing;
        LoadLevel(CurrentLevelNumber);
    }

    private void LoadLevel(int levelNumber)
    {
        // LoadLevel always represents a fresh run (initial load, next/back,
        // replay, or hard restart). Game Over itself deliberately does not
        // reset this value so a future Continue can keep the same run alive.
        BoosterInventoryManager.Instance?.EndCurrentRun();
        DiscardLevelEarnedCoins();
        CurrentLevelNumber = levelNumber;
        activeTargets.Clear();
        achievedTargets.Clear();
        activeObjectives.Clear();
        missionReachObjectives.Clear();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ActiveLevelConfiguration = debugLevelOverride != null ? debugLevelOverride :
            (levelCatalog != null ? levelCatalog.FindByNumber(levelNumber) : null);
        debugLevelOverride = null;
#else
        ActiveLevelConfiguration = levelCatalog != null ? levelCatalog.FindByNumber(levelNumber) : null;
#endif
        if (ActiveLevelConfiguration == null && levels.Count == 0)
        {
            Debug.LogWarning("GameManager: no levels defined — mission progression disabled.");
            return;
        }

        // Past the authored list, replay the last level.
        LevelDefinition source = ActiveLevelConfiguration != null
            ? new LevelDefinition { timeLimit = ActiveLevelConfiguration.timeLimit, objectives = ActiveLevelConfiguration.objectives }
            : levels[Mathf.Min(levelNumber - 1, levels.Count - 1)];

        // Per-level timing, floored defensively so a mis-authored 0 can't
        // produce an instant game over or a degenerate star bracket.
        float configuredStartingTime = ActiveLevelConfiguration != null &&
            ActiveLevelConfiguration.timeMode == LevelTimeMode.MergeTimeRush
                ? ActiveLevelConfiguration.timeRushStartingTime
                : source.timeLimit;
        currentTimeLimit = Mathf.Max(1f, configuredStartingTime);
        currentThreeStarThreshold = Mathf.Clamp(source.threeStarThreshold, 1f, currentTimeLimit);
        if (ActiveLevelConfiguration != null && launcher != null)
        {
            System.Func<PlanetTier, bool> isUnlocked = UnlockManager.Instance != null
                ? UnlockManager.Instance.IsUnlocked
                : null;
            if (!ActiveLevelConfiguration.ValidateSpawnPool(isUnlocked, out string spawnPoolMessage))
                Debug.LogWarning($"LevelConfig {ActiveLevelConfiguration.stableId} spawn pool: {spawnPoolMessage}. Invalid entries are ignored; Tier1 is the final fallback.", ActiveLevelConfiguration);
            launcher.ApplyLevelSpawnConfiguration(ActiveLevelConfiguration.launcherSpawnPool,
                ActiveLevelConfiguration.maximumAllowedMergeTier,
                ActiveLevelConfiguration.meteorsEnabled, ActiveLevelConfiguration.meteorSpawnChance,
                ActiveLevelConfiguration.guaranteeMeteorWithinLaunches,
                ActiveLevelConfiguration.meteorGuaranteeLaunchCount);
        }

        // Fresh clock for every level entry — this is the single reset point,
        // and both AdvanceToNextLevel and RestartGame funnel through here.
        // The readout snaps to the level's full time ("45", "90", ...)
        // immediately rather than waiting for the next Update tick.
        RemainingTime = currentTimeLimit;
        UpdateTimerUI();

        BuildObjectives(source);

        if (missionHUD != null)
        {
            missionHUD.ShowLevel(levelNumber, activeTargets);
        }

        var initialProgress = new List<LevelObjectiveProgress>(activeObjectives.Count);
        foreach (LevelObjective objective in activeObjectives)
            initialProgress.Add(objective.Snapshot);
        ObjectivesInitialized?.Invoke(initialProgress);

        Debug.Log($"GameManager: Level {levelNumber} started. Mission: {DescribeTargets()}");
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Level Config/Load Assigned Configuration")]
    private void DebugLoadAssignedConfiguration()
    {
        if (debugLevelConfiguration == null) { Debug.LogWarning("Assign Debug Level Configuration first.", this); return; }
        debugLevelOverride = debugLevelConfiguration;
        LoadLevel(debugLevelConfiguration.levelNumber);
    }

    [ContextMenu("DEBUG Level Config/Log Active")]
    private void DebugLogActiveConfiguration()
    {
        if (ActiveLevelConfiguration == null) { Debug.Log("No active data-driven level configuration; legacy fallback is active.", this); return; }
        Debug.Log($"LevelConfig {ActiveLevelConfiguration.stableId}: level={ActiveLevelConfiguration.levelNumber}, sector={ActiveLevelConfiguration.sectorId}, " +
                  $"time={ActiveLevelConfiguration.timeLimit}, maxTier={ActiveLevelConfiguration.maximumSpawnTier}, meteors={ActiveLevelConfiguration.meteorsEnabled}/{ActiveLevelConfiguration.meteorSpawnChance:P0}, " +
                  $"baseCoin={ActiveLevelConfiguration.baseSpaceCoinReward}, bg={ActiveLevelConfiguration.backgroundId}, orbit={ActiveLevelConfiguration.orbitId}", this);
    }

    [ContextMenu("DEBUG Level Config/Validate Catalog")]
    private void DebugValidateCatalog()
    {
        if (levelCatalog == null) { Debug.LogWarning("No Level Catalog assigned.", this); return; }
        var ids = new HashSet<string>(); var numbers = new HashSet<int>(); int errors = 0;
        System.Func<PlanetTier, bool> isUnlocked = UnlockManager.Instance != null
            ? UnlockManager.Instance.IsUnlocked
            : null;
        foreach (LevelConfiguration config in levelCatalog.levels)
        {
            string message = "configuration reference is null";
            if (config == null || !config.Validate(out message)) { Debug.LogError($"Invalid level config {(config != null ? config.name : "null")}: {message}", config); errors++; continue; }
            if (!config.ValidateSpawnPool(isUnlocked, out message)) { Debug.LogError($"Invalid runtime spawn pool {config.name}: {message}", config); errors++; }
            if (!ids.Add(config.stableId) || !numbers.Add(config.levelNumber)) { Debug.LogError($"Duplicate level ID/number: {config.stableId}/{config.levelNumber}", config); errors++; }
        }
        Debug.Log($"Level Catalog validation finished: {levelCatalog.levels.Count} entries, {errors} error(s).", this);
    }
#endif

    private void BuildObjectives(LevelDefinition source)
    {
        IReadOnlyList<LevelObjectiveDefinition> configured =
            useDebugObjectiveOverride && debugObjectives != null && debugObjectives.Count > 0
                ? debugObjectives
                : source.objectives;

        if (configured != null && configured.Count > 0)
        {
            for (int i = 0; i < configured.Count; i++)
            {
                if (configured[i] != null)
                    activeObjectives.Add(LevelObjective.Create(activeObjectives.Count, configured[i]));
            }
        }
        else
        {
            foreach (PlanetTier tier in source.targetTiers)
            {
                var legacy = new LevelObjectiveDefinition
                {
                    type = LevelObjectiveType.ReachTier,
                    targetTier = tier,
                    targetProgress = 1f,
                    required = true
                };
                activeObjectives.Add(LevelObjective.Create(activeObjectives.Count, legacy));
            }
        }

        // The current MissionHUD remains the compact tier-icon view. It shows
        // the first three ReachTier objectives; all objective types are still
        // exposed through the runtime list/events for the future objective UI.
        foreach (LevelObjective objective in activeObjectives)
        {
            if (objective.Type != LevelObjectiveType.ReachTier ||
                activeTargets.Count >= MaxTargetsPerLevel)
                continue;
            activeTargets.Add(objective.TargetTier);
            achievedTargets.Add(false);
            missionReachObjectives.Add(objective);
        }
    }

    // Meteorites are persistent obstacles by design (see Meteorite.cs's
    // Direct Growth rule): clearing them on every level win would erase the
    // exact escalating hazard the mechanic is built around. clearMeteorites
    // is true only for RestartGame's hard reset back to level 1, where a
    // fully blank board is the expected "start over" behavior.
    private void ClearBoard(bool clearMeteorites)
    {
        int cleared = 0;
        foreach (Planet planet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            // Same guarantee TriggerBoom gives its victims: out of the merge
            // system first, then the deferred Destroy.
            if (planet.TryGetComponent(out PlanetMerge merge))
            {
                merge.PrepareForDespawn();
            }
            Destroy(planet.gameObject);
            cleared++;
        }

        int meteoritesCleared = 0;
        if (clearMeteorites)
        {
            foreach (Meteorite meteorite in FindObjectsByType<Meteorite>(FindObjectsSortMode.None))
            {
                meteorite.PrepareForDespawn();
                Destroy(meteorite.gameObject);
                meteoritesCleared++;
            }
        }

        outsideTimers.Clear();
        Debug.Log($"GameManager: board cleared ({cleared} planets removed, " +
                  $"{(clearMeteorites ? meteoritesCleared.ToString() : "0 (persisted)")} meteorites removed).");
    }

    // Shared fail path for both lose conditions (boundary breach and the
    // level clock running out); the reason string only feeds the log.
    private void TriggerGameOver(string reason)
    {
        State = GameState.GameOver;

        // The countdown has done its job — don't leave a stale "0.0s" on screen.
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        Debug.Log($"GameManager: GAME OVER on level {CurrentLevelNumber} — {reason}");
    }

    // Full reset back to level 1. Public so a Game Over panel button can call
    // it straight from an OnClick event; safe to call in any state.
    public void RestartGame()
    {
        Debug.Log("GameManager: restarting game.");

        // RestartGame is intentionally callable from any state. If a future
        // menu invokes it while the inventory popup owns the pause, release
        // that ownership first so the restarted level cannot inherit scale 0.
        if (State == GameState.InventoryPaused)
        {
            Time.timeScale = timeScaleBeforeInventoryPause;
        }

        // RestartGame is public and unguarded by design; if it fires while
        // the win cinematic is mid-swallow, shut the vortex down cleanly so
        // the next run doesn't start inside a super-gravity black hole.
        if (vortexRoutine != null)
        {
            StopCoroutine(vortexRoutine);
            vortexRoutine = null;
            if (blackHole != null)
            {
                blackHole.EndVortex();
            }
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
        if (levelCompletePanel != null)
        {
            levelCompletePanel.Hide();
        }
        HideLevelCompleteScreen();
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        // Full restart wipes meteorites too — a hard reset back to level 1
        // should hand back a genuinely blank board, not a hazard the player
        // accumulated in a run that's over.
        ClearBoard(clearMeteorites: true);

        if (launcher != null)
        {
            launcher.ResetQueue();
        }

        // Order matters: LoadLevel refreshes the mission UI, and the launcher
        // only re-arms once State is Playing again — set state first so the
        // first frame after restart is fully playable.
        State = GameState.Playing;
        LoadLevel(1);
    }

    // The designed 15-level ramp, Tier5 debut through the Tier8 ultimate
    // challenge (PlanetMerge.maxTier is configured at Tier8 — two Tier8s BOOM
    // instead of merging further, so Tier8 is the true ceiling). Duplicate
    // entries are intentional: two Tier6 targets render as two identical
    // sprites side by side on the panel.
    //
    // Timing model: building a TierN from scratch costs 2^(N-1) Tier1-
    // equivalents of merging (Tier4=8, Tier5=16, Tier6=32, Tier7=64,
    // Tier8=128). Every level prices out at ~6s per cost unit with a 3-star
    // pace of roughly a third of the limit — the same formula as before,
    // just carried one tier higher and with every level's targets bumped up
    // the ladder so the early game no longer clears on autopilot. Remember
    // the progression rules: targets resolve highest-tier-first, and the
    // collision guard stops merges above the highest open target — so each
    // mission is exactly the grind its cost says it is, and a bigger target
    // means a bigger, heavier pile the player has to keep inside the
    // boundary the whole time it's being built.
    private void BuildDefaultLevels()
    {
        // ---- Act 1: openers (cost 16-56) — was Tier3/4, now Tier4/5/6 ----
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier5 },
            timeLimit = 95f, threeStarThreshold = 30f
        });
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier5, PlanetTier.Tier4 },
            timeLimit = 145f, threeStarThreshold = 50f
        });
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier6, PlanetTier.Tier4 },
            timeLimit = 240f, threeStarThreshold = 80f
        });
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier6, PlanetTier.Tier5 },
            timeLimit = 290f, threeStarThreshold = 95f
        });
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier6, PlanetTier.Tier5, PlanetTier.Tier4 },
            timeLimit = 335f, threeStarThreshold = 110f
        });

        // ---- Act 2: the Tier7 era (cost 64-112) — was Tier6, now Tier7 ----
        // L6 — Tier7 debut, a single clean goal to learn the longer chain.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier7 },
            timeLimit = 385f, threeStarThreshold = 130f
        });
        // L7 — Tier7 plus a Tier5 chaser afterwards.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier7, PlanetTier.Tier5 },
            timeLimit = 480f, threeStarThreshold = 160f
        });
        // L8 — Tier7 then a Tier6: the follow-up is half a Tier7 by itself.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier7, PlanetTier.Tier6 },
            timeLimit = 575f, threeStarThreshold = 190f
        });
        // L9 — full three-slot spread; high-tier chasers crowd the board.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier7, PlanetTier.Tier6, PlanetTier.Tier5 },
            timeLimit = 670f, threeStarThreshold = 225f
        });
        // L10 — twin Tier7s, the Act 2 finale (same cost as one Tier8).
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier7, PlanetTier.Tier7 },
            timeLimit = 770f, threeStarThreshold = 255f
        });

        // ---- Act 3: the Tier8 era (cost 128-224) — was Tier7, now Tier8 ----
        // L11 — Tier8 debut: one goal, the longest single chain in the game.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier8 },
            timeLimit = 770f, threeStarThreshold = 255f
        });
        // L12 — Tier8 with a Tier5 epilogue.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier8, PlanetTier.Tier5 },
            timeLimit = 865f, threeStarThreshold = 290f
        });
        // L13 — Tier8 then Tier6.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier8, PlanetTier.Tier6 },
            timeLimit = 960f, threeStarThreshold = 320f
        });
        // L14 — Tier8 then Tier7: two monster chains back to back.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier8, PlanetTier.Tier7 },
            timeLimit = 1150f, threeStarThreshold = 385f
        });
        // L15 — the ultimate: Tier8, Tier7, Tier6 in strict descending order.
        levels.Add(new LevelDefinition
        {
            targetTiers = { PlanetTier.Tier8, PlanetTier.Tier7, PlanetTier.Tier6 },
            timeLimit = 1345f, threeStarThreshold = 450f
        });

        Debug.Log("GameManager: no levels authored in the Inspector — using the built-in 15-level configuration.");
    }

    private string DescribeTargets()
    {
        if (activeObjectives.Count == 0)
            return "(none)";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < activeObjectives.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            LevelObjective objective = activeObjectives[i];
            sb.Append(objective.Type);
            if (objective.Type == LevelObjectiveType.ReachTier)
                sb.Append($" {objective.TargetTier}");
            sb.Append($" {objective.CurrentProgress:0.#}/{objective.TargetProgress:0.#}");
            if (objective.IsCompleted)
                sb.Append(" (done)");
        }
        return sb.ToString();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Objectives/Load All-Type Test Set")]
    private void DebugLoadAllTypeTestSet()
    {
        if (State != GameState.Playing)
            return;

        debugObjectives = new List<LevelObjectiveDefinition>
        {
            new LevelObjectiveDefinition { type = LevelObjectiveType.ReachTier,
                targetTier = debugReachTier, targetProgress = 1f, required = true },
            new LevelObjectiveDefinition { type = LevelObjectiveType.MergeCount,
                targetProgress = 5f, required = true },
            new LevelObjectiveDefinition { type = LevelObjectiveType.ComboTarget,
                targetProgress = Mathf.Max(1, debugComboValue), required = true },
            new LevelObjectiveDefinition { type = LevelObjectiveType.MeteorObjective,
                targetProgress = 3f, required = true },
            new LevelObjectiveDefinition { type = LevelObjectiveType.Survival,
                targetProgress = Mathf.Max(1f, debugSurvivalSeconds), required = true }
        };
        useDebugObjectiveOverride = true;

        activeObjectives.Clear();
        activeTargets.Clear();
        achievedTargets.Clear();
        missionReachObjectives.Clear();
        BuildObjectives(new LevelDefinition());
        missionHUD?.ShowLevel(CurrentLevelNumber, activeTargets);

        var progress = new List<LevelObjectiveProgress>(activeObjectives.Count);
        foreach (LevelObjective objective in activeObjectives)
            progress.Add(objective.Snapshot);
        ObjectivesInitialized?.Invoke(progress);
        Debug.Log("GameManager: loaded temporary all-type objective test set.", this);
    }

    [ContextMenu("DEBUG Objectives/Simulate Reach Tier")]
    private void DebugSimulateReachTier() => NotifyMergeCreated(debugReachTier);

    [ContextMenu("DEBUG Objectives/Simulate Merge + Combo")]
    private void DebugSimulateMergeAndCombo() => HandleMergeRegistered(Mathf.Max(1, debugComboValue));

    [ContextMenu("DEBUG Objectives/Simulate Meteor Destroyed")]
    private void DebugSimulateMeteorDestroyed() => NotifyMeteorDestroyed(1);

    [ContextMenu("DEBUG Objectives/Add Survival Seconds")]
    private void DebugAddSurvivalSeconds() =>
        ApplyObjectiveSignal(LevelObjectiveType.Survival, Mathf.Max(0.1f, debugSurvivalSeconds));

    [ContextMenu("DEBUG Objectives/Complete All Objectives")]
    private void DebugCompleteAllObjectives()
    {
        if (State != GameState.Playing)
            return;
        foreach (LevelObjective objective in activeObjectives)
        {
            if (objective.ForceComplete())
            {
                ObjectiveProgressChanged?.Invoke(objective.Snapshot);
                int slot = missionReachObjectives.IndexOf(objective);
                if (slot >= 0)
                {
                    achievedTargets[slot] = true;
                    missionHUD?.MarkAchieved(slot);
                }
            }
        }
        TryCompleteObjectives();
    }
#endif

    void OnDrawGizmosSelected()
    {
        Transform center = blackHoleCenter != null ? blackHoleCenter : transform;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center.position, maxBoundaryRadius);
    }
}
