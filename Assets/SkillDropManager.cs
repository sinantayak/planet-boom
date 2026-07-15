using System;
using UnityEngine;

// Rolls whether a merge drops a collectible skill, and if so, which of the
// four SkillType values. Purely probabilistic bookkeeping — actual
// pickup/inventory storage is a later phase; for now a successful roll fires
// OnSkillDropped (so a future inventory system can subscribe without this
// manager needing to know it exists yet) and hands the drop straight to
// SkillFlightManager for the world-to-UI flight visual.
//
// Scene-local singleton, same shape as AudioManager/GameManager.
public class SkillDropManager : MonoBehaviour
{
    public static SkillDropManager Instance { get; private set; }

    // Fired the instant a drop roll succeeds, after the flight visual has
    // already been kicked off.
    public static event Action<SkillType> OnSkillDropped;

    [Header("Drop Chance by Combo")]
    // Combo 1 or 2 never reaches this table at all — TryDropOnMerge returns
    // 0% for them unconditionally, see DropChanceForCombo below.
    [SerializeField] [Range(0f, 1f)] private float comboX3DropChance = 0.10f;
    [SerializeField] [Range(0f, 1f)] private float comboX4DropChance = 0.25f;
    // Combo 5 and every combo above it shares this same chance.
    [SerializeField] [Range(0f, 1f)] private float comboX5PlusDropChance = 0.50f;

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
        float chance = DropChanceForCombo(combo);
        if (chance <= 0f || UnityEngine.Random.value >= chance)
            return;

        SkillType skill = RollRandomSkillType();
        Debug.Log($"SkillDropManager: combo x{combo} dropped {skill}.");

        if (SkillFlightManager.Instance != null)
        {
            SkillFlightManager.Instance.SpawnFlight(worldPosition, skill);
        }

        OnSkillDropped?.Invoke(skill);
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
}
