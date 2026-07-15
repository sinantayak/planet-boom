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
    }

    void OnEnable()
    {
        if (Instance == this)
        {
            SkillFlightManager.OnSkillArrivedAtChest += HandleSkillArrivedAtChest;
        }
    }

    void OnDisable()
    {
        SkillFlightManager.OnSkillArrivedAtChest -= HandleSkillArrivedAtChest;
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
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
    }

    // This is the only public consumption path. Execution is synchronous, so
    // the count cannot be decremented between validation and the result.
    public bool TryUseSkill(SkillType type)
    {
        int currentCount = GetCount(type);
        if (currentCount <= 0 || SkillManager.Instance == null)
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
        AudioManager.Instance?.PlaySkillUseSucceeded(type);
        return true;
    }

    public bool TryGetQuickSlot(int slotIndex, out SkillType type)
    {
        if (IsValidSlotIndex(slotIndex) && quickSlots[slotIndex].HasValue)
        {
            type = quickSlots[slotIndex].Value;
            return true;
        }

        type = default;
        return false;
    }

    public bool TryAssignQuickSlot(int slotIndex, SkillType type)
    {
        if (!IsValidSlotIndex(slotIndex) || !IsValidSkillType(type))
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
        return true;
    }

    public bool TryUseQuickSlot(int slotIndex)
    {
        return TryGetQuickSlot(slotIndex, out SkillType type) && TryUseSkill(type);
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

        bool sanitizedSave = false;
        var assigned = new HashSet<SkillType>();
        for (int i = 0; i < QuickSlotCount; i++)
        {
            string saved = PlayerPrefs.GetString(QuickSlotKey(i), string.Empty);
            if (!string.IsNullOrEmpty(saved) &&
                Enum.TryParse(saved, out SkillType type) &&
                IsValidSkillType(type) && assigned.Add(type))
            {
                quickSlots[i] = type;
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

    private static bool IsValidSlotIndex(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < QuickSlotCount;
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
        if (DebugFunctionKeyPressed(1)) DebugAdd(SkillType.GravitySingularity);
        if (DebugFunctionKeyPressed(2)) DebugAdd(SkillType.MeteorStrike);
        if (DebugFunctionKeyPressed(3)) DebugAdd(SkillType.TimeWarp);
        if (DebugFunctionKeyPressed(4)) DebugAdd(SkillType.CosmicMimic);

        if (DebugFunctionKeyPressed(5)) DebugAssignDefaultSlots();
        if (DebugFunctionKeyPressed(6)) DebugClearSlots();
        if (DebugFunctionKeyPressed(7)) DebugToggleThirdSlot();
        if (DebugFunctionKeyPressed(8)) DebugLogSnapshot();
        if (DebugFunctionKeyPressed(9)) DebugResetInventoryData();

        if (DebugNumberRowPressed(5)) DebugUseSlot(0);
        if (DebugNumberRowPressed(6)) DebugUseSlot(1);
        if (DebugNumberRowPressed(7)) DebugUseSlot(2);
    }

    private void DebugAdd(SkillType type)
    {
        AddSkill(type);
        Debug.Log($"TEMP INVENTORY: added {type}; count={GetCount(type)}.", this);
    }

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
        Debug.Log("TEMP INVENTORY: all Phase-3A inventory data reset.", this);
    }

    private static bool DebugNumberRowPressed(int number)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (number == 5 && keyboard.digit5Key.wasPressedThisFrame) return true;
            if (number == 6 && keyboard.digit6Key.wasPressedThisFrame) return true;
            if (number == 7 && keyboard.digit7Key.wasPressedThisFrame) return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (number == 5 && UnityEngine.Input.GetKeyDown(KeyCode.Alpha5)) return true;
        if (number == 6 && UnityEngine.Input.GetKeyDown(KeyCode.Alpha6)) return true;
        if (number == 7 && UnityEngine.Input.GetKeyDown(KeyCode.Alpha7)) return true;
#endif
        return false;
    }

    private static bool DebugFunctionKeyPressed(int number)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (number == 1 && keyboard.f1Key.wasPressedThisFrame) return true;
            if (number == 2 && keyboard.f2Key.wasPressedThisFrame) return true;
            if (number == 3 && keyboard.f3Key.wasPressedThisFrame) return true;
            if (number == 4 && keyboard.f4Key.wasPressedThisFrame) return true;
            if (number == 5 && keyboard.f5Key.wasPressedThisFrame) return true;
            if (number == 6 && keyboard.f6Key.wasPressedThisFrame) return true;
            if (number == 7 && keyboard.f7Key.wasPressedThisFrame) return true;
            if (number == 8 && keyboard.f8Key.wasPressedThisFrame) return true;
            if (number == 9 && keyboard.f9Key.wasPressedThisFrame) return true;
        }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        if (number == 1 && UnityEngine.Input.GetKeyDown(KeyCode.F1)) return true;
        if (number == 2 && UnityEngine.Input.GetKeyDown(KeyCode.F2)) return true;
        if (number == 3 && UnityEngine.Input.GetKeyDown(KeyCode.F3)) return true;
        if (number == 4 && UnityEngine.Input.GetKeyDown(KeyCode.F4)) return true;
        if (number == 5 && UnityEngine.Input.GetKeyDown(KeyCode.F5)) return true;
        if (number == 6 && UnityEngine.Input.GetKeyDown(KeyCode.F6)) return true;
        if (number == 7 && UnityEngine.Input.GetKeyDown(KeyCode.F7)) return true;
        if (number == 8 && UnityEngine.Input.GetKeyDown(KeyCode.F8)) return true;
        if (number == 9 && UnityEngine.Input.GetKeyDown(KeyCode.F9)) return true;
#endif
        return false;
    }
#endif
}
