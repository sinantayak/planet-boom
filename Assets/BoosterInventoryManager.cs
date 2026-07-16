using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum BoosterType
{
    LuckyDrop,
    DoubleTimeDrop,
    StarBooster
}

public enum ActiveEffectType
{
    LuckyDrop,
    DoubleTimeDrop,
    StarBooster
}

// Permanent booster ownership is intentionally independent from SkillType and
// quick slots. ActiveEffectChanged is the reusable bridge for a future HUD.
public sealed class BoosterInventoryManager : MonoBehaviour
{
    public static BoosterInventoryManager Instance { get; private set; }
    public static event Action<ActiveEffectType, bool> ActiveEffectChanged;
    public event Action<BoosterType, int> InventoryCountChanged;

    private const string CountKeyPrefix = "BoosterInventory.Count.";
    private readonly Dictionary<BoosterType, int> counts = new Dictionary<BoosterType, int>();
    private bool luckyDropActive;
    private bool doubleTimeDropActive;
    private bool starBoosterActive;

    public bool IsLuckyDropActive => luckyDropActive;
    public bool IsDoubleTimeDropActive => doubleTimeDropActive;
    public bool IsStarBoosterActive => starBoosterActive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
            return;
        var owner = new GameObject("BoosterInventoryManager");
        DontDestroyOnLoad(owner);
        owner.AddComponent<BoosterInventoryManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        foreach (BoosterType type in Enum.GetValues(typeof(BoosterType)))
            counts[type] = Mathf.Max(0, PlayerPrefs.GetInt(CountKeyPrefix + type, 0));
        PlayerDataPersistenceManager.RegisterBoosterInventory(this);
    }

    private void OnEnable()
    {
        SkillDropManager.ModifyDropChance += ModifyDropChance;
        GameManager.ModifyStarRating += ModifyStarRating;
        SceneManager.activeSceneChanged += HandleActiveSceneChanged;
    }

    private void OnDisable()
    {
        SkillDropManager.ModifyDropChance -= ModifyDropChance;
        GameManager.ModifyStarRating -= ModifyStarRating;
        SceneManager.activeSceneChanged -= HandleActiveSceneChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            PlayerDataPersistenceManager.UnregisterBoosterInventory(this);
            Instance = null;
        }
    }

    public int GetCount(BoosterType type) => counts.TryGetValue(type, out int value) ? value : 0;

    public bool AddBooster(BoosterType type, int amount = 1)
    {
        if (!Enum.IsDefined(typeof(BoosterType), type) || amount <= 0)
            return false;
        int next = (int)Math.Min(int.MaxValue, (long)GetCount(type) + amount);
        counts[type] = next;
        SaveCount(type, next);
        InventoryCountChanged?.Invoke(type, next);
        PlayerDataPersistenceManager.NotifyBoosterInventoryChanged(this);
        return true;
    }

    // Future pre-level UI should call this method. Ownership alone never
    // activates or consumes the booster.
    public bool TryActivateForCurrentRun(BoosterType type, bool bypassUnlock = false)
    {
        if (!Enum.IsDefined(typeof(BoosterType), type) || IsActive(type) || GetCount(type) <= 0 ||
            (!bypassUnlock && (UnlockManager.Instance == null || !UnlockManager.Instance.IsUnlocked(type))) ||
            GameManager.Instance == null || GameManager.Instance.State != GameManager.GameState.Playing)
            return false;

        int next = GetCount(type) - 1;
        counts[type] = next;
        SaveCount(type, next);
        InventoryCountChanged?.Invoke(type, next);
        PlayerDataPersistenceManager.NotifyBoosterInventoryChanged(this);
        SetActive(type, true);
        return true;
    }

    public void EndCurrentRun()
    {
        if (luckyDropActive)
            SetActive(BoosterType.LuckyDrop, false);
        if (doubleTimeDropActive)
            SetActive(BoosterType.DoubleTimeDrop, false);
        if (starBoosterActive)
            SetActive(BoosterType.StarBooster, false);
    }

    public bool IsActive(BoosterType type)
    {
        switch (type)
        {
            case BoosterType.LuckyDrop: return luckyDropActive;
            case BoosterType.DoubleTimeDrop: return doubleTimeDropActive;
            case BoosterType.StarBooster: return starBoosterActive;
            default: return false;
        }
    }

    public float GetTimeDropRewardMultiplier() => doubleTimeDropActive ? 2f : 1f;

    private void SetActive(BoosterType type, bool active)
    {
        switch (type)
        {
            case BoosterType.LuckyDrop:
                luckyDropActive = active;
                ActiveEffectChanged?.Invoke(ActiveEffectType.LuckyDrop, active);
                break;
            case BoosterType.DoubleTimeDrop:
                doubleTimeDropActive = active;
                ActiveEffectChanged?.Invoke(ActiveEffectType.DoubleTimeDrop, active);
                break;
            case BoosterType.StarBooster:
                starBoosterActive = active;
                ActiveEffectChanged?.Invoke(ActiveEffectType.StarBooster, active);
                break;
        }
    }

    private void HandleActiveSceneChanged(Scene previous, Scene next) => EndCurrentRun();

    private float ModifyDropChance(RewardDropType type, float chance)
    {
        if (!luckyDropActive || SkillDropManager.Instance == null)
            return chance;
        return chance * SkillDropManager.Instance.GetLuckyDropMultiplier(type);
    }

    private int ModifyStarRating(StarRatingEvaluationContext context, int currentRating)
    {
        if (!starBoosterActive || GameManager.Instance == null)
            return currentRating;
        int modified = currentRating + GameManager.Instance.StarBoosterRatingAdvantage;
        Debug.Log($"Star Booster: level {context.LevelNumber}, base={context.BaseRating}, " +
                  $"incoming={currentRating}, advantage=+{GameManager.Instance.StarBoosterRatingAdvantage}, " +
                  $"requested={modified}.", this);
        return modified;
    }

    internal void WriteToPlayerData(PlayerData data)
    {
        data.boosterInventory.Clear();
        foreach (BoosterType type in Enum.GetValues(typeof(BoosterType)))
            data.boosterInventory.Add(new BoosterQuantityData { boosterType = type.ToString(), quantity = GetCount(type) });
    }

    internal void ApplyPlayerData(PlayerData data)
    {
        foreach (BoosterType type in Enum.GetValues(typeof(BoosterType)))
            counts[type] = 0;
        if (data?.boosterInventory != null)
        {
            foreach (BoosterQuantityData row in data.boosterInventory)
                if (row != null && Enum.TryParse(row.boosterType, true, out BoosterType type))
                    counts[type] = Mathf.Max(0, row.quantity);
        }
        foreach (BoosterType type in Enum.GetValues(typeof(BoosterType)))
        {
            SaveCount(type, GetCount(type));
            InventoryCountChanged?.Invoke(type, GetCount(type));
        }
    }

    private static void SaveCount(BoosterType type, int count)
    {
        PlayerPrefs.SetInt(CountKeyPrefix + type, Mathf.Max(0, count));
        PlayerPrefs.Save();
    }
}
