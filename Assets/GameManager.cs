using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;

// One mission entry: "cause <count> BOOMs of <tier>". Counts tick down in
// place on the runtime copy, never on the authored level data. Note that
// BOOMs only ever fire at PlanetMerge's maxTier (two max-tier planets
// colliding), so authored targets should use that tier.
[System.Serializable]
public class BoomTarget
{
    // Old scenes stored this as "color"; the enum's int values carry over
    // (Red→Tier1, Blue→Tier2, Green→Tier3, Yellow→Tier4).
    [FormerlySerializedAs("color")]
    public PlanetTier tier;
    public int count = 1;
}

[System.Serializable]
public class LevelDefinition
{
    public List<BoomTarget> targets = new List<BoomTarget>();
}

// Owns the mission/level state and the Suika-style lose condition. Booms reach
// it through NotifyBoom (called by PlanetMerge); everything else is self-driven.
public class GameManager : MonoBehaviour
{
    public enum GameState { Playing, GameOver }

    public static GameManager Instance { get; private set; }

    public GameState State { get; private set; } = GameState.Playing;
    public int CurrentLevelNumber { get; private set; } = 1;

    [Header("Levels")]
    // Authored missions, in order. Levels past the end of this list reuse the
    // last authored one with every target count raised by 1 per extra level,
    // so the game keeps escalating without more authoring.
    [SerializeField] private List<LevelDefinition> levels = new List<LevelDefinition>();

    [Header("Boundary / Lose Condition")]
    // Defaults to the BlackHole's transform when left unassigned.
    [SerializeField] private Transform blackHoleCenter;
    [SerializeField] private float maxBoundaryRadius = 6f;
    [SerializeField] private float outsideTimeLimit = 2f;

    [Header("UI")]
    // Countdown readout ("CRITICAL: 1.4s!") shown while at least one planet is
    // outside the boundary; hidden the rest of the time.
    public TextMeshProUGUI countdownText;

    // Always-visible mission readout: current level plus the boom targets
    // still to hit. Refreshed on level start and every counted boom.
    public TextMeshProUGUI missionText;

    // Panel switched on when the state flips to GameOver. Wire its Restart
    // button's OnClick to GameManager.RestartGame.
    public GameObject gameOverPanel;

    // Read by BoundaryVisualizer so the drawn circle can never drift from the
    // radius the lose check actually enforces.
    public Transform BlackHoleCenter => blackHoleCenter;
    public float MaxBoundaryRadius => maxBoundaryRadius;

    // Runtime copy of the current level's targets (see BoomTarget note).
    private readonly List<BoomTarget> remainingTargets = new List<BoomTarget>();

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

        if (blackHoleCenter == null)
        {
            BlackHole blackHole = FindFirstObjectByType<BlackHole>();
            if (blackHole != null)
            {
                blackHoleCenter = blackHole.transform;
            }
            else
            {
                Debug.LogWarning("GameManager: no BlackHoleCenter assigned and no BlackHole found — boundary check disabled until one exists.");
            }
        }

        if (levels.Count == 0)
        {
            BuildDefaultLevels();
        }

        // Start hidden; FixedUpdate re-shows it whenever a planet is outside.
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
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
                TriggerGameOver(planet, distance);
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
    public void NotifyBoom(PlanetTier tier)
    {
        if (State != GameState.Playing)
            return;

        BoomTarget target = remainingTargets.Find(t => t.tier == tier && t.count > 0);
        if (target == null)
        {
            Debug.Log($"GameManager: {tier} boom — not a mission target on level {CurrentLevelNumber}.");
            return;
        }

        target.count--;
        Debug.Log($"GameManager: {tier} boom counted! Remaining mission: {DescribeTargets()}");
        UpdateMissionUI();

        if (remainingTargets.TrueForAll(t => t.count <= 0))
        {
            CompleteLevel();
        }
    }

    private void CompleteLevel()
    {
        Debug.Log($"GameManager: LEVEL {CurrentLevelNumber} COMPLETE!");
        ClearBoard();
        LoadLevel(CurrentLevelNumber + 1);
    }

    private void LoadLevel(int levelNumber)
    {
        CurrentLevelNumber = levelNumber;
        remainingTargets.Clear();

        if (levels.Count == 0)
        {
            Debug.LogWarning("GameManager: no levels defined — mission progression disabled.");
            UpdateMissionUI();
            return;
        }

        LevelDefinition source = levels[Mathf.Min(levelNumber - 1, levels.Count - 1)];
        int difficultyBonus = Mathf.Max(0, levelNumber - levels.Count);

        foreach (BoomTarget target in source.targets)
        {
            remainingTargets.Add(new BoomTarget
            {
                tier = target.tier,
                count = target.count + difficultyBonus
            });
        }

        Debug.Log($"GameManager: Level {levelNumber} started. Mission: {DescribeTargets()}");
        UpdateMissionUI();
    }

    // Mission readout, e.g. "Level 2 — Tier4 x2". Shows "Mission complete!"
    // for the brief window where every target is at 0 (all-zero states
    // otherwise only exist mid-transition).
    private void UpdateMissionUI()
    {
        if (missionText == null)
            return;

        bool allDone = remainingTargets.Count > 0 && remainingTargets.TrueForAll(t => t.count <= 0);
        missionText.text = allDone
            ? $"Level {CurrentLevelNumber} — Mission complete!"
            : $"Level {CurrentLevelNumber} — Booms needed: {DescribeTargets()}";
    }

    private void ClearBoard()
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

        outsideTimers.Clear();
        Debug.Log($"GameManager: board cleared ({cleared} planets removed).");
    }

    private void TriggerGameOver(Planet offender, float distance)
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

        Debug.Log($"GameManager: GAME OVER on level {CurrentLevelNumber} — a {offender.CurrentTier} " +
                  $"planet stayed {outsideTimeLimit:F1}s outside the boundary " +
                  $"(distance {distance:F2} > radius {maxBoundaryRadius:F2}).");
    }

    // Full reset back to level 1. Public so a Game Over panel button can call
    // it straight from an OnClick event; safe to call in any state.
    public void RestartGame()
    {
        Debug.Log("GameManager: restarting game.");

        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }

        ClearBoard();

        // Order matters: LoadLevel refreshes the mission UI, and the launcher
        // only re-arms once State is Playing again — set state first so the
        // first frame after restart is fully playable.
        State = GameState.Playing;
        LoadLevel(1);
    }

    private void BuildDefaultLevels()
    {
        // Under the unified tier rules a BOOM needs two Tier4 planets, and each
        // Tier4 is itself the product of a merge chain — so per-level counts
        // are lower than the old color-based defaults were.
        levels.Add(new LevelDefinition
        {
            targets = { new BoomTarget { tier = PlanetTier.Tier4, count = 1 } }
        });
        levels.Add(new LevelDefinition
        {
            targets = { new BoomTarget { tier = PlanetTier.Tier4, count = 2 } }
        });
        Debug.Log("GameManager: no levels authored in the Inspector — using built-in defaults.");
    }

    private string DescribeTargets()
    {
        if (remainingTargets.Count == 0)
            return "(none)";

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < remainingTargets.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(remainingTargets[i].tier).Append(" x").Append(remainingTargets[i].count);
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
