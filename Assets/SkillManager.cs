using System;
using System.Collections;
using UnityEngine;

// Executes collected skills without owning inventory counts. A future inventory
// UI should consume an item only when TryExecuteSkill returns true.
public class SkillManager : MonoBehaviour
{
    public static SkillManager Instance { get; private set; }

    [Header("Gravity Singularity")]
    [SerializeField] private float gravitySingularityDuration = 2f;
    [SerializeField] private float gravitySingularityForceMultiplier = 4f;

    [Header("Time Warp")]
    [SerializeField] private float timeWarpBonusSeconds = 30f;

    [Header("Cosmic Shield")]
    [SerializeField] [Min(0.1f)] private float cosmicShieldDuration = 15f;

    [Header("Scene References (auto-found when empty)")]
    [SerializeField] private BlackHole blackHole;
    [SerializeField] private PlanetLauncher launcher;
    [SerializeField] private BoundaryVisualizer boundaryVisualizer;
    [SerializeField] private CosmicAbductionVisualController cosmicAbductionVisualController;

    // Future laser/VFX code can subscribe without changing execution logic.
    public static event Action<Meteorite> OnMeteorStrikeTargeted;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("SkillManager: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool TryExecuteSkill(SkillType type)
    {
        if (GameManager.Instance == null || GameManager.Instance.State != GameManager.GameState.Playing)
        {
            Debug.Log($"SkillManager: {type} rejected because gameplay is not in the Playing state.");
            return false;
        }

        ResolveReferences();

        bool succeeded;
        switch (type)
        {
            case SkillType.GravitySingularity:
                succeeded = blackHole != null && blackHole.TryBeginGravitySingularity(
                    gravitySingularityDuration, gravitySingularityForceMultiplier);
                break;
            case SkillType.MeteorStrike:
                succeeded = TryMeteorStrike();
                break;
            case SkillType.MeteorShower:
                succeeded = MeteorStrikeVisualController.Instance != null &&
                            MeteorStrikeVisualController.Instance.TryPlayMeteorShower();
                break;
            case SkillType.TimeWarp:
                succeeded = GameManager.Instance.TryAddBonusTime(timeWarpBonusSeconds);
                break;
            case SkillType.CosmicMimic:
                succeeded = launcher != null && launcher.TryQueueCosmicMimic();
                break;
            case SkillType.PlanetReroll:
                succeeded = launcher != null && launcher.TryRerollCurrentPlanet();
                break;
            case SkillType.CosmicShield:
                succeeded = launcher != null && launcher.TryBeginCosmicShield(cosmicShieldDuration);
                break;
            case SkillType.CosmicAbduction:
                succeeded = TryCosmicAbduction();
                if (!succeeded)
                    boundaryVisualizer?.PlayNoSkillTargetFeedback();
                break;
            default:
                succeeded = false;
                break;
        }

        Debug.Log($"SkillManager: {type} execution {(succeeded ? "succeeded" : "failed")}.");
        return succeeded;
    }

    private bool TryMeteorStrike()
    {
        Meteorite bestTarget = null;
        foreach (Meteorite meteorite in FindObjectsByType<Meteorite>(FindObjectsSortMode.None))
        {
            if (meteorite == null || !meteorite.gameObject.activeInHierarchy ||
                meteorite.IsBeingAbsorbed || meteorite.IsAbsorbing)
                continue;

            if (bestTarget == null || meteorite.CurrentTier > bestTarget.CurrentTier)
            {
                bestTarget = meteorite;
            }
        }

        if (bestTarget == null)
            return false;

        if (MeteorStrikeVisualController.Instance != null)
        {
            if (!MeteorStrikeVisualController.Instance.TryPlay(bestTarget))
                return false;

            OnMeteorStrikeTargeted?.Invoke(bestTarget);
            return true;
        }

        // Safe fallback for scenes that have not installed the visual
        // coordinator: preserve the original immediate behavior.
        OnMeteorStrikeTargeted?.Invoke(bestTarget);
        return bestTarget.TryDestroyBySkill();
    }

    private bool TryCosmicAbduction()
    {
        if (cosmicAbductionVisualController != null && !cosmicAbductionVisualController.CanBegin)
            return false;
        if (!GameManager.Instance.TryReserveCosmicAbductionTarget(out Planet target))
            return false;

        if (cosmicAbductionVisualController != null)
        {
            if (cosmicAbductionVisualController.TryPlay(target))
                return true;

            // Reservation already locked the planet. If presentation cannot
            // start unexpectedly, finish the gameplay action rather than
            // leaving an immortal frozen target on the board.
            AudioManager.Instance?.PlayCosmicAbductionSequence();
            GameManager.Instance.CompleteCosmicAbduction(target);
            return true;
        }

        AudioManager.Instance?.PlayCosmicAbductionSequence();
        return GameManager.Instance.CompleteCosmicAbduction(target);
    }

    private void ResolveReferences()
    {
        if (blackHole == null)
            blackHole = FindFirstObjectByType<BlackHole>();
        if (launcher == null)
            launcher = FindFirstObjectByType<PlanetLauncher>();
        if (boundaryVisualizer == null)
            boundaryVisualizer = FindFirstObjectByType<BoundaryVisualizer>();
        if (cosmicAbductionVisualController == null)
            cosmicAbductionVisualController = FindFirstObjectByType<CosmicAbductionVisualController>();
    }

}
