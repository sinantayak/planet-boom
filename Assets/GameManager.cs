using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

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

    // Seconds on this level's clock; the level fails when it runs out.
    public float timeLimit = 60f;

    // Star brackets scale off this playtested baseline: clear within this
    // many ELAPSED seconds → 3 stars, within twice it → 2 stars, any slower
    // finish → 1 star (a completed level never shows zero).
    public float threeStarThreshold = 20f;
}

// Owns the mission/level state and the Suika-style lose condition. Merges
// reach it through NotifyMergeCreated (called by PlanetMerge); everything
// else is self-driven.
public class GameManager : MonoBehaviour
{
    // CinematicVortex: the moment between "last target fulfilled" and the win
    // popup — the black hole spins up and swallows the board while all input
    // and lose checks are frozen (every gameplay system already gates on
    // State == Playing, so the new state disables them for free).
    public enum GameState { Playing, LevelComplete, GameOver, CinematicVortex }

    public static GameManager Instance { get; private set; }

    public GameState State { get; private set; } = GameState.Playing;
    public int CurrentLevelNumber { get; private set; } = 1;

    // Seconds left on the current level's clock. Public for the timer text
    // that will be added to the HUD later; already drives the star rating.
    public float RemainingTime { get; private set; }

    // The MissionHUD has exactly 3 target slots; levels can't ask for more.
    public const int MaxTargetsPerLevel = 3;

    [Header("Levels")]
    // Authored missions, in order. Leave empty to use the built-in 5-level
    // configuration (BuildDefaultLevels). Levels past the end of this list
    // replay the last authored one so the game never runs out.
    [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();

    [Header("Boundary / Lose Condition")]
    // Defaults to the BlackHole's transform when left unassigned.
    [SerializeField] private Transform blackHoleCenter;
    [SerializeField] private float maxBoundaryRadius = 6f;
    [SerializeField] private float outsideTimeLimit = 2f;

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
    // Countdown readout ("CRITICAL: 1.4s!") shown while at least one planet is
    // outside the boundary; hidden the rest of the time.
    public TextMeshProUGUI countdownText;

    // Top-right live level clock, rendered MM:SS ("08:20"). Optional — when
    // left unassigned the timer float still runs, it just isn't displayed.
    [SerializeField] private TextMeshProUGUI gameplayTimerText;

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

    // Read by BoundaryVisualizer so the drawn circle can never drift from the
    // radius the lose check actually enforces.
    public Transform BlackHoleCenter => blackHoleCenter;
    public float MaxBoundaryRadius => maxBoundaryRadius;

    // Runtime copy of the current level's targets plus per-target achieved
    // flags. The two lists are index-aligned with each other AND with the
    // MissionHUD's slots, so "slot i achieved" is unambiguous even when a
    // level contains duplicate tiers.
    private readonly List<PlanetTier> activeTargets = new List<PlanetTier>();
    private readonly List<bool> achievedTargets = new List<bool>();

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

    // Seconds each planet has spent continuously outside the boundary. Entries
    // are dropped the moment a planet comes back inside, so the timer measures
    // an unbroken stretch outside, not a lifetime total.
    private readonly Dictionary<Planet, float> outsideTimers = new Dictionary<Planet, float>();
    private readonly List<Planet> staleTimerKeys = new List<Planet>();

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
        levels.RemoveAll(level => level == null || level.targetTiers == null || level.targetTiers.Count == 0);
        if (levels.Count == 0)
        {
            BuildDefaultLevels();
        }

        launcher = FindFirstObjectByType<PlanetLauncher>();

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

        LoadLevel(1);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
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

    void FixedUpdate()
    {
        if (State != GameState.Playing || blackHoleCenter == null)
            return;

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

            if (timer > worstTimer)
            {
                worstTimer = timer;
            }

            if (timer >= outsideTimeLimit)
            {
                TriggerGameOver($"a {planet.CurrentTier} planet stayed {outsideTimeLimit:F1}s outside " +
                                $"the boundary (distance {distance:F2} > radius {maxBoundaryRadius:F2}).");
                return;
            }
        }

        UpdateCountdownUI(worstTimer);
    }

    // worstTimer < 0 means no planet is outside this step → hide the readout.
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

        int slot = -1;
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (!achievedTargets[i] && activeTargets[i] == createdTier)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
            return;

        // The created tier matches an open target — but defer it while any
        // higher-tier target remains unfulfilled, because this planet is
        // (presumptively) a building block that will be consumed on the way
        // to that bigger goal.
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (!achievedTargets[i] && activeTargets[i] > createdTier)
            {
                Debug.Log($"GameManager: {createdTier} created but not counted — " +
                          $"{activeTargets[i]} must be achieved first (higher targets before lower).");
                return;
            }
        }

        achievedTargets[slot] = true;
        Debug.Log($"GameManager: target {slot + 1} ({createdTier}) achieved! Mission: {DescribeTargets()}");

        if (missionHUD != null)
        {
            missionHUD.MarkAchieved(slot);
        }

        if (!achievedTargets.Contains(false))
        {
            CompleteLevel();
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

        // No mission authored (sandbox): no ceiling to enforce.
        if (activeTargets.Count == 0)
            return true;

        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (!achievedTargets[i] && activeTargets[i] > tier)
                return true;
        }
        return false;
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

    // The cinematic itself: vortex on → wait → vortex off → sweep whatever
    // the pull didn't physically reach in time → popup out of the core.
    private IEnumerator RunLevelCompleteVortex(int starsEarned)
    {
        if (blackHole != null)
        {
            blackHole.BeginVortex(vortexSwallowsMeteorites);
        }

        yield return new WaitForSeconds(vortexDuration);

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
        if (timePassed <= currentThreeStarThreshold)
            return 3;
        if (timePassed <= currentThreeStarThreshold * 2f)
            return 2;
        return 1;
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
        CurrentLevelNumber = levelNumber;
        activeTargets.Clear();
        achievedTargets.Clear();

        if (levels.Count == 0)
        {
            Debug.LogWarning("GameManager: no levels defined — mission progression disabled.");
            return;
        }

        // Past the authored list, replay the last level.
        LevelDefinition source = levels[Mathf.Min(levelNumber - 1, levels.Count - 1)];

        // Per-level timing, floored defensively so a mis-authored 0 can't
        // produce an instant game over or a degenerate star bracket.
        currentTimeLimit = Mathf.Max(1f, source.timeLimit);
        currentThreeStarThreshold = Mathf.Clamp(source.threeStarThreshold, 1f, currentTimeLimit);

        // Fresh clock for every level entry — this is the single reset point,
        // and both AdvanceToNextLevel and RestartGame funnel through here.
        // The readout snaps to the level's full time ("45", "90", ...)
        // immediately rather than waiting for the next Update tick.
        RemainingTime = currentTimeLimit;
        UpdateTimerUI();

        foreach (PlanetTier tier in source.targetTiers)
        {
            if (activeTargets.Count >= MaxTargetsPerLevel)
            {
                Debug.LogWarning($"GameManager: level {levelNumber} defines more than {MaxTargetsPerLevel} targets — extras ignored (the MissionHUD has {MaxTargetsPerLevel} slots).");
                break;
            }
            activeTargets.Add(tier);
            achievedTargets.Add(false);
        }

        if (missionHUD != null)
        {
            missionHUD.ShowLevel(levelNumber, activeTargets);
        }

        Debug.Log($"GameManager: Level {levelNumber} started. Mission: {DescribeTargets()}");
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
        if (activeTargets.Count == 0)
            return "(none)";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < activeTargets.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(activeTargets[i]);
            if (achievedTargets[i])
                sb.Append(" (done)");
        }
        return sb.ToString();
    }

    void OnDrawGizmosSelected()
    {
        Transform center = blackHoleCenter != null ? blackHoleCenter : transform;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center.position, maxBoundaryRadius);
    }
}
