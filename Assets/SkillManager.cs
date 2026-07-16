using System;
using System.Collections;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // TEMPORARY PHASE-2 TEST CONTROLS: remove this Update method after the
    // inventory UI calls TryExecuteSkill. Compiled out of non-development builds.
    // The project currently enables BOTH input backends. Prefer the new Input
    // System when a keyboard is available, then fall back to the legacy Input
    // Manager so this remains usable if Player Settings changes later.
    private bool hasLoggedDebugUpdate;

    void OnEnable()
    {
        Debug.Log($"TEMP DEBUG SKILLS: SkillManager enabled " +
                  $"(activeInHierarchy={gameObject.activeInHierarchy}, enabled={enabled}).", this);
    }

    void Update()
    {
        if (!hasLoggedDebugUpdate)
        {
            hasLoggedDebugUpdate = true;
            Debug.Log("TEMP DEBUG SKILLS: SkillManager.Update is running.", this);
        }

        if (DebugNumberRowPressed(1)) ExecuteDebugSkill(SkillType.GravitySingularity);
        if (DebugNumberRowPressed(2)) ExecuteDebugSkill(SkillType.MeteorStrike);
        if (DebugNumberRowPressed(3)) ExecuteDebugSkill(SkillType.TimeWarp);
        if (DebugNumberRowPressed(4)) ExecuteDebugSkill(SkillType.CosmicMimic);
        if (DebugNumberRowPressed(5)) ExecuteDebugSkill(SkillType.PlanetReroll);
    }

    private void ExecuteDebugSkill(SkillType type)
    {
        Debug.Log($"DEBUG SKILL KEY: {type}", this);
        bool succeeded = TryExecuteSkill(type);
        Debug.Log($"DEBUG SKILL RESULT: {type} = {succeeded}", this);
    }

    [ContextMenu("DEBUG Execute Cosmic Abduction")]
    private void DebugExecuteCosmicAbduction() => ExecuteDebugSkill(SkillType.CosmicAbduction);

    [ContextMenu("DEBUG Execute Meteor Shower")]
    private void DebugExecuteMeteorShower() => ExecuteDebugSkill(SkillType.MeteorShower);

    private static bool DebugNumberRowPressed(int number)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            switch (number)
            {
                case 1: if (keyboard.digit1Key.wasPressedThisFrame) return true; break;
                case 2: if (keyboard.digit2Key.wasPressedThisFrame) return true; break;
                case 3: if (keyboard.digit3Key.wasPressedThisFrame) return true; break;
                case 4: if (keyboard.digit4Key.wasPressedThisFrame) return true; break;
                case 5: if (keyboard.digit5Key.wasPressedThisFrame) return true; break;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        switch (number)
        {
            case 1: return UnityEngine.Input.GetKeyDown(KeyCode.Alpha1);
            case 2: return UnityEngine.Input.GetKeyDown(KeyCode.Alpha2);
            case 3: return UnityEngine.Input.GetKeyDown(KeyCode.Alpha3);
            case 4: return UnityEngine.Input.GetKeyDown(KeyCode.Alpha4);
            case 5: return UnityEngine.Input.GetKeyDown(KeyCode.Alpha5);
        }
#endif

        return false;
    }
#endif
}
