using System;
using UnityEngine;

public enum RewardDropType
{
    Skill,
    Time,
    SpaceCoin
}

// Rolls whether a merge drops a collectible skill, and if so, which of the
// four SkillType values. Purely probabilistic bookkeeping — actual
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
        SkillType skill = RollRandomSkillType();
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
                bool granted = GameManager.Instance != null &&
                               GameManager.Instance.TryAddBonusTime(timeBonusSeconds);
                if (granted)
                    AudioManager.Instance?.PlayTimeDropCollected();
                Debug.Log($"RewardDrop: Time +{timeBonusSeconds:0.#}s arrival, granted={granted}.", this);
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

    private static float CalculateFinalChance(RewardDropType type, float baseChance)
    {
        float chance = Mathf.Clamp01(baseChance);
        if (ModifyDropChance == null)
            return chance;
        foreach (Func<RewardDropType, float, float> modifier in ModifyDropChance.GetInvocationList())
            chance = Mathf.Clamp01(modifier(type, chance));
        return chance;
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

    private static SkillType RollRandomSkillType()
    {
        Array values = Enum.GetValues(typeof(SkillType));
        return (SkillType)values.GetValue(UnityEngine.Random.Range(0, values.Length));
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Spawn Random Skill Drop")]
    private void DebugSpawnSkillDrop()
    {
        SkillType skill = RollRandomSkillType();
        bool spawned = SkillFlightManager.Instance != null;
        SkillFlightManager.Instance?.SpawnFlight(transform.position, skill);
        Debug.Log($"RewardDrop DEBUG: Skill {skill} spawned={spawned}.", this);
    }

    [ContextMenu("DEBUG Spawn Time Drop")]
    private void DebugSpawnTimeDrop() => Debug.Log($"RewardDrop DEBUG: Time spawned={SpawnTimeDrop(transform.position)}.", this);

    [ContextMenu("DEBUG Spawn Coin Drop")]
    private void DebugSpawnCoinDrop() => Debug.Log($"RewardDrop DEBUG: Coin spawned={SpawnCoinDrop(transform.position)}.", this);
#endif
}
