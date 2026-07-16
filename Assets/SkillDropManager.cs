using System;
using UnityEngine;

public enum RewardDropType
{
    Skill,
    Time,
    SpaceCoin
}

// Rolls whether a merge drops a collectible skill, and if so, which of the
// SkillType values. Purely probabilistic bookkeeping — actual
// OnSkillDropped remains an immediate analytics/presentation hook. Inventory
// intentionally does NOT subscribe here: it rewards only from
// SkillFlightManager.OnSkillArrivedAtChest after the visual completes.
//
// Scene-local singleton, same shape as AudioManager/GameManager.
public class SkillDropManager : MonoBehaviour
{
    public static SkillDropManager Instance { get; private set; }

    // Fired the instant a drop roll succeeds. This is not a reward event;
    // interrupted/misconfigured flights must not add inventory.
    public static event Action<SkillType> OnSkillDropped;
    public static event Action<float> OnTimeDropped;
    public static event Action<long> OnSpaceCoinDropped;

    // Future Lucky Drop boosters can subscribe and return a modified chance.
    // Modifiers are applied in subscription order and clamped to 0..1.
    public static event Func<RewardDropType, float, float> ModifyDropChance;

    [Header("Drop Chance by Combo")]
    // Combo 1 or 2 never reaches this table at all — TryDropOnMerge returns
    // 0% for them unconditionally, see DropChanceForCombo below.
    [SerializeField] [Range(0f, 1f)] private float comboX3DropChance = 0.10f;
    [SerializeField] [Range(0f, 1f)] private float comboX4DropChance = 0.25f;
    // Combo 5 and every combo above it shares this same chance.
    [SerializeField] [Range(0f, 1f)] private float comboX5PlusDropChance = 0.50f;

    [Header("Time Drop")]
    [SerializeField] [Range(0f, 1f)] private float timeDropChancePerMerge = 0.08f;
    [SerializeField] [Min(0.1f)] private float timeBonusSeconds = 3f;
    [SerializeField] private Sprite timeDropSprite;

    [Header("Space Coin Drop")]
    [SerializeField] [Range(0f, 1f)] private float coinDropChancePerMerge = 0.03f;
    [SerializeField] [Min(1)] private int coinAmount = 1;
    [SerializeField] private Sprite coinDropSprite;

    [Header("Passive Booster Icons")]
    [SerializeField] private Sprite luckyDropIcon;
    [SerializeField] private Sprite doubleTimeDropIcon;
    [SerializeField] private Sprite starBoosterIcon;

    [Header("Lucky Drop Booster")]
    [SerializeField] [Min(0f)] private float luckyDropSkillChanceMultiplier = 1.5f;
    [SerializeField] [Min(0f)] private float luckyDropTimeChanceMultiplier = 1.5f;
    [SerializeField] [Min(0f)] private float luckyDropCoinChanceMultiplier = 1.5f;

    public Sprite LuckyDropIcon => luckyDropIcon;
    public Sprite DoubleTimeDropIcon => doubleTimeDropIcon;
    public Sprite StarBoosterIcon => starBoosterIcon;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("SkillDropManager: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // Called once per merge (PlanetMerge and Meteorite both feed this, same
    // shared combo chain AudioManager tracks). Rolls the drop chance for the
    // given combo and, on success, rolls a random SkillType and starts its
    // flight visual from worldPosition to the inventory button.
    public void TryDropOnMerge(int combo, Vector3 worldPosition)
    {
        TryDropSkill(combo, worldPosition);
        TryDropTime(worldPosition);
        TryDropCoin(worldPosition);
    }

    private void TryDropSkill(int combo, Vector3 worldPosition)
    {
        float chance = CalculateFinalChance(RewardDropType.Skill, DropChanceForCombo(combo));
        if (!Roll(chance)) return;
        if (!TryRollRandomSkillType(false, out SkillType skill)) return;
        Debug.Log($"SkillDropManager: combo x{combo} dropped {skill}.");
        SkillFlightManager.Instance?.SpawnFlight(worldPosition, skill);
        OnSkillDropped?.Invoke(skill);
    }

    private void TryDropTime(Vector3 worldPosition)
    {
        if (!Roll(CalculateFinalChance(RewardDropType.Time, timeDropChancePerMerge))) return;
        if (SpawnTimeDrop(worldPosition))
            OnTimeDropped?.Invoke(timeBonusSeconds);
    }

    private void TryDropCoin(Vector3 worldPosition)
    {
        if (!Roll(CalculateFinalChance(RewardDropType.SpaceCoin, coinDropChancePerMerge))) return;
        if (SpawnCoinDrop(worldPosition))
            OnSpaceCoinDropped?.Invoke(coinAmount);
    }

    private bool SpawnTimeDrop(Vector3 worldPosition)
    {
        if (SkillFlightManager.Instance == null || timeDropSprite == null)
            return false;
        return SkillFlightManager.Instance.SpawnRewardFlight(worldPosition, RewardDropType.Time,
            timeDropSprite, () =>
            {
                float multiplier = BoosterInventoryManager.Instance != null
                    ? BoosterInventoryManager.Instance.GetTimeDropRewardMultiplier()
                    : 1f;
                float grantedSeconds = timeBonusSeconds * multiplier;
                bool granted = GameManager.Instance != null &&
                               GameManager.Instance.TryAddBonusTime(grantedSeconds);
                if (granted)
                    AudioManager.Instance?.PlayTimeDropCollected();
                Debug.Log($"RewardDrop: Time +{grantedSeconds:0.#}s arrival " +
                          $"(base {timeBonusSeconds:0.#} x{multiplier:0.#}), granted={granted}.", this);
            });
    }

    private bool SpawnCoinDrop(Vector3 worldPosition)
    {
        if (SkillFlightManager.Instance == null || coinDropSprite == null)
            return false;
        return SkillFlightManager.Instance.SpawnRewardFlight(worldPosition, RewardDropType.SpaceCoin,
            coinDropSprite, () =>
            {
                bool collected = GameManager.Instance != null &&
                                 GameManager.Instance.TryCollectLevelCoins(coinAmount);
                if (collected)
                    AudioManager.Instance?.PlayCoinDropCollected();
                Debug.Log($"RewardDrop: temporary level coin +{coinAmount} arrival, " +
                          $"collected={collected}.", this);
            });
    }

    private static bool Roll(float chance) => chance > 0f && UnityEngine.Random.value < chance;

    public static float CalculateFinalChance(RewardDropType type, float baseChance)
    {
        float chance = Mathf.Clamp01(baseChance);
        if (ModifyDropChance == null)
            return chance;
        foreach (Func<RewardDropType, float, float> modifier in ModifyDropChance.GetInvocationList())
            chance = Mathf.Clamp01(modifier(type, chance));
        return chance;
    }

    public float GetLuckyDropMultiplier(RewardDropType type)
    {
        switch (type)
        {
            case RewardDropType.Skill: return Mathf.Max(0f, luckyDropSkillChanceMultiplier);
            case RewardDropType.Time: return Mathf.Max(0f, luckyDropTimeChanceMultiplier);
            case RewardDropType.SpaceCoin: return Mathf.Max(0f, luckyDropCoinChanceMultiplier);
            default: return 1f;
        }
    }

    private float DropChanceForCombo(int combo)
    {
        if (combo <= 2)
            return 0f;
        if (combo == 3)
            return comboX3DropChance;
        if (combo == 4)
            return comboX4DropChance;
        return comboX5PlusDropChance;
    }

    private static bool TryRollRandomSkillType(bool bypassUnlock, out SkillType result)
    {
        var eligible = new System.Collections.Generic.List<SkillType>();
        foreach (SkillType value in Enum.GetValues(typeof(SkillType)))
            if (bypassUnlock || (UnlockManager.Instance != null && UnlockManager.Instance.IsUnlocked(value))) eligible.Add(value);
        if (eligible.Count == 0) { result = default; return false; }
        result = eligible[UnityEngine.Random.Range(0, eligible.Count)]; return true;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Star Booster/Add To Booster Inventory")]
    private void DebugAddStarBooster()
    {
        bool added = BoosterInventoryManager.Instance != null &&
                     BoosterInventoryManager.Instance.AddBooster(BoosterType.StarBooster);
        Debug.Log($"Star Booster DEBUG: added={added}, owned={BoosterInventoryManager.Instance?.GetCount(BoosterType.StarBooster) ?? 0}.", this);
    }

    [ContextMenu("DEBUG Star Booster/Activate For Current Run")]
    private void DebugActivateStarBooster()
    {
        bool activated = BoosterInventoryManager.Instance != null &&
                         BoosterInventoryManager.Instance.TryActivateForCurrentRun(BoosterType.StarBooster, true);
        Debug.Log($"Star Booster DEBUG: activated={activated}, active={BoosterInventoryManager.Instance?.IsStarBoosterActive ?? false}.", this);
    }

    [ContextMenu("DEBUG Boosters/Log All Active States")]
    private void DebugLogAllBoosterStates()
    {
        BoosterInventoryManager manager = BoosterInventoryManager.Instance;
        Debug.Log($"Booster DEBUG active: LuckyDrop={manager?.IsLuckyDropActive ?? false}, " +
                  $"DoubleTimeDrop={manager?.IsDoubleTimeDropActive ?? false}, " +
                  $"StarBooster={manager?.IsStarBoosterActive ?? false}.", this);
    }

    [ContextMenu("DEBUG 2x Time Drop/Add To Booster Inventory")]
    private void DebugAddDoubleTimeDrop()
    {
        bool added = BoosterInventoryManager.Instance != null &&
                     BoosterInventoryManager.Instance.AddBooster(BoosterType.DoubleTimeDrop);
        Debug.Log($"2x Time Drop DEBUG: added={added}, owned={BoosterInventoryManager.Instance?.GetCount(BoosterType.DoubleTimeDrop) ?? 0}.", this);
    }

    [ContextMenu("DEBUG 2x Time Drop/Activate For Current Run")]
    private void DebugActivateDoubleTimeDrop()
    {
        bool activated = BoosterInventoryManager.Instance != null &&
                         BoosterInventoryManager.Instance.TryActivateForCurrentRun(BoosterType.DoubleTimeDrop, true);
        Debug.Log($"2x Time Drop DEBUG: activated={activated}, active={BoosterInventoryManager.Instance?.IsDoubleTimeDropActive ?? false}, " +
                  $"LuckyDropActive={BoosterInventoryManager.Instance?.IsLuckyDropActive ?? false}.", this);
    }

    [ContextMenu("DEBUG 2x Time Drop/Log Reward")]
    private void DebugLogDoubleTimeDropReward()
    {
        float multiplier = BoosterInventoryManager.Instance?.GetTimeDropRewardMultiplier() ?? 1f;
        Debug.Log($"2x Time Drop DEBUG: next collected Time Drop grants {timeBonusSeconds * multiplier:0.#}s " +
                  $"(base {timeBonusSeconds:0.#} x{multiplier:0.#}). Time Warp is not routed through this calculation.", this);
    }

    [ContextMenu("DEBUG Lucky Drop/Add To Booster Inventory")]
    private void DebugAddLuckyDrop()
    {
        bool added = BoosterInventoryManager.Instance != null &&
                     BoosterInventoryManager.Instance.AddBooster(BoosterType.LuckyDrop);
        Debug.Log($"Lucky Drop DEBUG: added={added}, owned={BoosterInventoryManager.Instance?.GetCount(BoosterType.LuckyDrop) ?? 0}.", this);
    }

    [ContextMenu("DEBUG Lucky Drop/Activate For Current Run")]
    private void DebugActivateLuckyDrop()
    {
        bool activated = BoosterInventoryManager.Instance != null &&
                         BoosterInventoryManager.Instance.TryActivateForCurrentRun(BoosterType.LuckyDrop, true);
        Debug.Log($"Lucky Drop DEBUG: activated={activated}, active={BoosterInventoryManager.Instance?.IsLuckyDropActive ?? false}.", this);
    }

    [ContextMenu("DEBUG Lucky Drop/Log Effective Chances")]
    private void DebugLogLuckyDropChances()
    {
        Debug.Log($"Lucky Drop DEBUG effective chances: Skill[x3={CalculateFinalChance(RewardDropType.Skill, comboX3DropChance):P1}, " +
                  $"x4={CalculateFinalChance(RewardDropType.Skill, comboX4DropChance):P1}, x5+={CalculateFinalChance(RewardDropType.Skill, comboX5PlusDropChance):P1}], " +
                  $"Time={CalculateFinalChance(RewardDropType.Time, timeDropChancePerMerge):P1}, Coin={CalculateFinalChance(RewardDropType.SpaceCoin, coinDropChancePerMerge):P1}.", this);
    }

    [ContextMenu("DEBUG Spawn Random Skill Drop")]
    private void DebugSpawnSkillDrop()
    {
        if (!TryRollRandomSkillType(true, out SkillType skill)) return;
        bool spawned = SkillFlightManager.Instance != null;
        SkillFlightManager.Instance?.SpawnFlight(transform.position, skill);
        Debug.Log($"RewardDrop DEBUG: Skill {skill} spawned={spawned}.", this);
    }

    [ContextMenu("DEBUG Unlocks/Test Normal Skill Drop Filter")]
    private void DebugTestSkillDropFilter()
    {
        bool found = TryRollRandomSkillType(false, out SkillType skill);
        Debug.Log($"Unlock filter DEBUG: eligible={found}, rolled={(found ? skill.ToString() : "none")}", this);
    }

    [ContextMenu("DEBUG Spawn Time Drop")]
    private void DebugSpawnTimeDrop() => Debug.Log($"RewardDrop DEBUG: Time spawned={SpawnTimeDrop(transform.position)}.", this);

    [ContextMenu("DEBUG Spawn Coin Drop")]
    private void DebugSpawnCoinDrop() => Debug.Log($"RewardDrop DEBUG: Coin spawned={SpawnCoinDrop(transform.position)}.", this);
#endif
}
