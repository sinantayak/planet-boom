using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Single source of truth for owned skill quantities and the three quick-slot
// assignments. This component stores data only; SkillManager still owns all
// gameplay effects and SkillFlightManager still owns drop presentation.
public class SkillInventoryManager : MonoBehaviour
{
    public static SkillInventoryManager Instance { get; private set; }

    public const int QuickSlotCount = 3;

    private const string CountKeyPrefix = "SkillInventory.Count.";
    private const string QuickSlotKeyPrefix = "SkillInventory.QuickSlot.";
    private const string LegacyEmergencyBlastName = "EmergencyBlast";

    private readonly Dictionary<SkillType, int> counts = new Dictionary<SkillType, int>();
    private readonly SkillType?[] quickSlots = new SkillType?[QuickSlotCount];

    // UI can subscribe now or in Phase 3B. The new count is supplied directly
    // so listeners never need to guess whether the change was add or consume.
    public event Action<SkillType, int> InventoryCountChanged;
    public event Action<int, SkillType?> QuickSlotChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("SkillInventoryManager: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadFromPlayerPrefs();
        PlayerDataPersistenceManager.RegisterSkillInventory(this);
    }

    void OnEnable()
    {
        if (Instance == this)
        {
            SkillFlightManager.OnSkillArrivedAtChest += HandleSkillArrivedAtChest;
            UnlockManager.UnlockChanged += HandleUnlockChanged;
            PlayerDataPersistenceManager.DataLoaded += HandlePlayerDataLoaded;
            if (PlayerDataPersistenceManager.Instance != null &&
                PlayerDataPersistenceManager.Instance.IsLoaded)
                SanitizeLockedQuickSlots();
        }
    }

    void OnDisable()
    {
        SkillFlightManager.OnSkillArrivedAtChest -= HandleSkillArrivedAtChest;
        UnlockManager.UnlockChanged -= HandleUnlockChanged;
        PlayerDataPersistenceManager.DataLoaded -= HandlePlayerDataLoaded;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            PlayerDataPersistenceManager.UnregisterSkillInventory(this);
            Instance = null;
        }
    }

    public int GetCount(SkillType type)
    {
        return counts.TryGetValue(type, out int count) ? count : 0;
    }

    public void AddSkill(SkillType type, int amount = 1)
    {
        if (!IsValidSkillType(type) || amount <= 0)
            return;

        int newCount = (int)Math.Min(int.MaxValue, (long)GetCount(type) + amount);
        counts[type] = newCount;
        SaveCount(type, newCount);
        InventoryCountChanged?.Invoke(type, newCount);
        PlayerDataPersistenceManager.NotifySkillInventoryChanged(this);
    }

    // This is the only public consumption path. Execution is synchronous, so
    // the count cannot be decremented between validation and the result.
    public bool TryUseSkill(SkillType type)
    {
        int currentCount = GetCount(type);
        if (!IsSkillUnlocked(type) || currentCount <= 0 || SkillManager.Instance == null)
        {
            AudioManager.Instance?.PlaySkillUseFailed();
            return false;
        }

        if (!SkillManager.Instance.TryExecuteSkill(type))
        {
            AudioManager.Instance?.PlaySkillUseFailed();
            return false;
        }

        int newCount = currentCount - 1;
        counts[type] = newCount;
        SaveCount(type, newCount);
        InventoryCountChanged?.Invoke(type, newCount);
        PlayerDataPersistenceManager.NotifySkillInventoryChanged(this);
        AudioManager.Instance?.PlaySkillUseSucceeded(type);
        return true;
    }

    public bool TryGetQuickSlot(int slotIndex, out SkillType type)
    {
        if (IsValidSlotIndex(slotIndex) && quickSlots[slotIndex].HasValue &&
            IsSkillUnlocked(quickSlots[slotIndex].Value))
        {
            type = quickSlots[slotIndex].Value;
            return true;
        }

        type = default;
        return false;
    }

    public bool TryAssignQuickSlot(int slotIndex, SkillType type)
    {
        if (!IsValidSlotIndex(slotIndex) || !IsValidSkillType(type) || !IsSkillUnlocked(type))
            return false;

        for (int i = 0; i < QuickSlotCount; i++)
        {
            if (i != slotIndex && quickSlots[i] == type)
                return false;
        }

        if (quickSlots[slotIndex] == type)
            return true;

        quickSlots[slotIndex] = type;
        SaveQuickSlot(slotIndex);
        QuickSlotChanged?.Invoke(slotIndex, type);
        PlayerDataPersistenceManager.NotifySkillInventoryChanged(this);
        return true;
    }

    public bool ClearQuickSlot(int slotIndex)
    {
        if (!IsValidSlotIndex(slotIndex))
            return false;

        if (!quickSlots[slotIndex].HasValue)
            return true;

        quickSlots[slotIndex] = null;
        SaveQuickSlot(slotIndex);
        QuickSlotChanged?.Invoke(slotIndex, null);
        PlayerDataPersistenceManager.NotifySkillInventoryChanged(this);
        return true;
    }

    public bool TryUseQuickSlot(int slotIndex)
    {
        // Final unlock gate intentionally remains here as well as in assignment
        // and TryUseSkill. A stale/legacy slot can never activate a locked skill.
        return TryGetQuickSlot(slotIndex, out SkillType type) &&
               IsSkillUnlocked(type) && TryUseSkill(type);
    }

    private void HandleUnlockChanged(string canonicalId, bool isUnlocked)
    {
        if (!isUnlocked)
            SanitizeLockedQuickSlots();
    }

    private void HandlePlayerDataLoaded(PlayerData data)
    {
        // PlayerDataPersistenceManager applies inventory first and unlocks
        // second. DataLoaded fires after both, so this is the safe point to
        // validate legacy slot assignments without racing initial load.
        SanitizeLockedQuickSlots();
    }

    private void SanitizeLockedQuickSlots()
    {
        bool changed = false;
        for (int i = 0; i < QuickSlotCount; i++)
        {
            if (!quickSlots[i].HasValue || IsSkillUnlocked(quickSlots[i].Value))
                continue;

            quickSlots[i] = null;
            SaveQuickSlot(i);
            QuickSlotChanged?.Invoke(i, null);
            changed = true;
        }

        if (changed)
            PlayerDataPersistenceManager.NotifySkillInventoryChanged(this);
    }

    private void HandleSkillArrivedAtChest(SkillType type)
    {
        AddSkill(type);
        AudioManager.Instance?.PlaySkillCollected();
        Debug.Log($"SkillInventoryManager: {type} reached chest; count={GetCount(type)}.", this);
    }

    private void LoadFromPlayerPrefs()
    {
        counts.Clear();
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
        {
            counts[type] = Mathf.Max(0, PlayerPrefs.GetInt(CountKey(type), 0));
        }

        int legacyCount = Mathf.Max(0, PlayerPrefs.GetInt(
            CountKeyPrefix + LegacyEmergencyBlastName, 0));
        if (legacyCount > 0)
        {
            counts[SkillType.CosmicAbduction] = (int)Math.Min(int.MaxValue,
                (long)counts[SkillType.CosmicAbduction] + legacyCount);
            SaveCount(SkillType.CosmicAbduction, counts[SkillType.CosmicAbduction]);
            PlayerPrefs.DeleteKey(CountKeyPrefix + LegacyEmergencyBlastName);
            PlayerPrefs.Save();
        }

        bool sanitizedSave = false;
        var assigned = new HashSet<SkillType>();
        for (int i = 0; i < QuickSlotCount; i++)
        {
            string saved = PlayerPrefs.GetString(QuickSlotKey(i), string.Empty);
            if (!string.IsNullOrEmpty(saved) &&
                TryParsePersistedSkillType(saved, out SkillType type) &&
                IsValidSkillType(type) && assigned.Add(type))
            {
                quickSlots[i] = type;
                if (!string.Equals(saved, type.ToString(), StringComparison.Ordinal))
                    sanitizedSave = true;
            }
            else
            {
                quickSlots[i] = null;
                if (!string.IsNullOrEmpty(saved))
                    sanitizedSave = true;
            }
        }

        if (sanitizedSave)
        {
            SaveAllQuickSlots();
        }
    }

    private static bool IsValidSkillType(SkillType type)
    {
        return Enum.IsDefined(typeof(SkillType), type);
    }

    public static bool IsSkillUnlocked(SkillType type)
    {
        return IsValidSkillType(type) && UnlockManager.Instance != null &&
               UnlockManager.Instance.IsUnlocked(type);
    }

    private static bool TryParsePersistedSkillType(string saved, out SkillType type)
    {
        if (string.Equals(saved, LegacyEmergencyBlastName, StringComparison.Ordinal))
        {
            type = SkillType.CosmicAbduction;
            return true;
        }
        return Enum.TryParse(saved, out type) && IsValidSkillType(type);
    }

    private static bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < QuickSlotCount;
    }

    internal void WriteToPlayerData(PlayerData data)
    {
        data.skillInventory.Clear();
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
        {
            data.skillInventory.Add(new SkillQuantityData
            {
                skillType = type.ToString(),
                quantity = GetCount(type)
            });
        }

        data.quickSlots.Clear();
        for (int i = 0; i < QuickSlotCount; i++)
            data.quickSlots.Add(quickSlots[i]?.ToString() ?? string.Empty);
    }

    internal void ApplyPlayerData(PlayerData data)
    {
        counts.Clear();
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            counts[type] = 0;

        int migratedEmergencyBlastCount = 0;
        foreach (SkillQuantityData entry in data.skillInventory)
        {
            if (entry != null && TryParsePersistedSkillType(entry.skillType, out SkillType type) &&
                IsValidSkillType(type))
            {
                int quantity = Mathf.Max(0, entry.quantity);
                bool legacyEmergencyBlast = string.Equals(
                    entry.skillType, LegacyEmergencyBlastName, StringComparison.Ordinal);
                if (legacyEmergencyBlast)
                    migratedEmergencyBlastCount = (int)Math.Min(int.MaxValue,
                        (long)migratedEmergencyBlastCount + quantity);
                else
                    counts[type] = Mathf.Max(counts[type], quantity);
                entry.skillType = type.ToString();
            }
        }
        counts[SkillType.CosmicAbduction] = (int)Math.Min(int.MaxValue,
            (long)counts[SkillType.CosmicAbduction] + migratedEmergencyBlastCount);

        var assigned = new HashSet<SkillType>();
        for (int i = 0; i < QuickSlotCount; i++)
        {
            string saved = i < data.quickSlots.Count ? data.quickSlots[i] : string.Empty;
            if (!string.IsNullOrEmpty(saved) && TryParsePersistedSkillType(saved, out SkillType type) &&
                IsValidSkillType(type) && assigned.Add(type))
            {
                quickSlots[i] = type;
                data.quickSlots[i] = type.ToString();
            }
            else
                quickSlots[i] = null;
        }

        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
        {
            int count = GetCount(type);
            PlayerPrefs.SetInt(CountKey(type), count);
            InventoryCountChanged?.Invoke(type, count);
        }
        for (int i = 0; i < QuickSlotCount; i++)
        {
            PlayerPrefs.SetString(QuickSlotKey(i), quickSlots[i]?.ToString() ?? string.Empty);
            QuickSlotChanged?.Invoke(i, quickSlots[i]);
        }
        PlayerPrefs.Save();
    }

    private static string CountKey(SkillType type) => CountKeyPrefix + type;
    private static string QuickSlotKey(int slotIndex) => QuickSlotKeyPrefix + slotIndex;

    private static void SaveCount(SkillType type, int count)
    {
        PlayerPrefs.SetInt(CountKey(type), Mathf.Max(0, count));
        PlayerPrefs.Save();
    }

    private void SaveQuickSlot(int slotIndex)
    {
        PlayerPrefs.SetString(
            QuickSlotKey(slotIndex),
            quickSlots[slotIndex].HasValue ? quickSlots[slotIndex].Value.ToString() : string.Empty);
        PlayerPrefs.Save();
    }

    private void SaveAllQuickSlots()
    {
        for (int i = 0; i < QuickSlotCount; i++)
        {
            PlayerPrefs.SetString(
                QuickSlotKey(i),
                quickSlots[i].HasValue ? quickSlots[i].Value.ToString() : string.Empty);
        }
        PlayerPrefs.Save();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    // TEMPORARY PHASE-3A BACKEND TESTS. Remove this Update/debug block when
    // Phase 3B UI calls AddSkill/TryAssignQuickSlot/TryUseQuickSlot directly.
    void Update()
    {
        if (DebugAddAllSkillsPressed())
            DebugAddTenOfEverySkill();
    }

    private void DebugAddTenOfEverySkill()
    {
        UnlockManager unlocks = UnlockManager.Instance;
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
        {
            unlocks?.Unlock(type);
            AddSkill(type, 10);
        }
        BoosterInventoryManager boosters = BoosterInventoryManager.Instance;
        if (boosters != null)
            foreach (BoosterType type in Enum.GetValues(typeof(BoosterType)))
            {
                unlocks?.Unlock(type);
                boosters.AddBooster(type, 10);
            }
        Debug.Log("DEBUG INVENTORY: Ctrl+Shift+K unlocked and added 10 of every skill and passive booster.", this);
    }

    private void DebugAdd(SkillType type)
    {
        AddSkill(type);
        Debug.Log($"TEMP INVENTORY: added {type}; count={GetCount(type)}.", this);
    }

    [ContextMenu("DEBUG Add Planet Reroll")]
    private void DebugAddPlanetReroll() => DebugAdd(SkillType.PlanetReroll);

    [ContextMenu("DEBUG Add Cosmic Shield")]
    private void DebugAddCosmicShield() => DebugAdd(SkillType.CosmicShield);

    [ContextMenu("DEBUG Add Cosmic Abduction")]
    private void DebugAddCosmicAbduction() => DebugAdd(SkillType.CosmicAbduction);

    [ContextMenu("DEBUG Add Meteor Shower")]
    private void DebugAddMeteorShower() => DebugAdd(SkillType.MeteorShower);

    private void DebugAssignDefaultSlots()
    {
        bool a = TryAssignQuickSlot(0, SkillType.GravitySingularity);
        bool b = TryAssignQuickSlot(1, SkillType.MeteorStrike);
        bool c = TryAssignQuickSlot(2, SkillType.TimeWarp);
        Debug.Log($"TEMP INVENTORY: default quick slots assigned = {a && b && c}.", this);
    }

    private void DebugClearSlots()
    {
        for (int i = 0; i < QuickSlotCount; i++)
            ClearQuickSlot(i);
        Debug.Log("TEMP INVENTORY: all quick slots cleared.", this);
    }

    private void DebugToggleThirdSlot()
    {
        SkillType target = quickSlots[2] == SkillType.CosmicMimic
            ? SkillType.TimeWarp
            : SkillType.CosmicMimic;
        bool succeeded = TryAssignQuickSlot(2, target);
        Debug.Log($"TEMP INVENTORY: assign slot 3 to {target} = {succeeded}.", this);
    }

    private void DebugUseSlot(int slotIndex)
    {
        SkillType? assigned = quickSlots[slotIndex];
        bool succeeded = TryUseQuickSlot(slotIndex);
        Debug.Log($"TEMP INVENTORY: use slot {slotIndex + 1} ({assigned?.ToString() ?? "Empty"}) = {succeeded}.", this);
    }

    private void DebugLogSnapshot()
    {
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            Debug.Log($"TEMP INVENTORY: {type} count={GetCount(type)}.", this);
        for (int i = 0; i < QuickSlotCount; i++)
            Debug.Log($"TEMP INVENTORY: slot {i + 1}={quickSlots[i]?.ToString() ?? "Empty"}.", this);
    }

    private void DebugResetInventoryData()
    {
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
        {
            counts[type] = 0;
            PlayerPrefs.SetInt(CountKey(type), 0);
            InventoryCountChanged?.Invoke(type, 0);
        }

        for (int i = 0; i < QuickSlotCount; i++)
        {
            quickSlots[i] = null;
            PlayerPrefs.SetString(QuickSlotKey(i), string.Empty);
            QuickSlotChanged?.Invoke(i, null);
        }

        PlayerPrefs.Save();
        PlayerDataPersistenceManager.NotifySkillInventoryChanged(this);
        Debug.Log("TEMP INVENTORY: all Phase-3A inventory data reset.", this);
    }

    private static bool DebugAddAllSkillsPressed()
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.kKey.wasPressedThisFrame &&
            (keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed) &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
            return true;
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (UnityEngine.Input.GetKeyDown(KeyCode.K) &&
            (UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl)) &&
            (UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift)))
            return true;
#endif
        return false;
    }

#endif
}
