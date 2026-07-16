using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum UnlockContentType { Planet, Skill, Booster, Background, Orbit }

// Central, string-ID based progression gate. Inventory ownership and active
// run state remain separate concerns.
public sealed class UnlockManager : MonoBehaviour
{
    public static UnlockManager Instance { get; private set; }
    public static event Action<string, bool> UnlockChanged;

    private readonly HashSet<string> unlocked = new HashSet<string>(StringComparer.Ordinal);
    private UnlockDefaultsConfig defaults;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private UnlockContentType debugContentType = UnlockContentType.Skill;
    [SerializeField] private string debugContentId = "MeteorStrike";
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null) return;
        var owner = new GameObject("UnlockManager");
        DontDestroyOnLoad(owner);
        owner.AddComponent<UnlockManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        defaults = Resources.Load<UnlockDefaultsConfig>("UnlockDefaults");
        PlayerDataPersistenceManager.RegisterUnlockManager(this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            PlayerDataPersistenceManager.UnregisterUnlockManager(this);
            Instance = null;
        }
    }

    public static string Id(UnlockContentType type, string contentId) =>
        $"{type.ToString().ToLowerInvariant()}:{contentId?.Trim()}";
    public static string Id(SkillType value) => Id(UnlockContentType.Skill, value.ToString());
    public static string Id(BoosterType value) => Id(UnlockContentType.Booster, value.ToString());
    public static string Id(PlanetTier value) => Id(UnlockContentType.Planet, value.ToString());

    public bool IsUnlocked(string canonicalId) => !string.IsNullOrWhiteSpace(canonicalId) && unlocked.Contains(canonicalId);
    public bool IsUnlocked(SkillType value) => IsUnlocked(Id(value));
    public bool IsUnlocked(BoosterType value) => IsUnlocked(Id(value));
    public bool IsUnlocked(PlanetTier value) => IsUnlocked(Id(value));
    public bool IsBackgroundUnlocked(string stableId) => IsUnlocked(Id(UnlockContentType.Background, stableId));
    public bool IsOrbitUnlocked(string stableId) => IsUnlocked(Id(UnlockContentType.Orbit, stableId));

    public bool Unlock(string canonicalId)
    {
        if (string.IsNullOrWhiteSpace(canonicalId) || !unlocked.Add(canonicalId)) return false;
        PlayerDataPersistenceManager.NotifyUnlockDataChanged(this);
        UnlockChanged?.Invoke(canonicalId, true);
        return true;
    }
    public bool Unlock(UnlockContentType type, string contentId) => Unlock(Id(type, contentId));
    public bool Unlock(SkillType value) => Unlock(Id(value));
    public bool Unlock(BoosterType value) => Unlock(Id(value));
    public bool Unlock(PlanetTier value) => Unlock(Id(value));

    public IReadOnlyList<string> GetUnlockedContent(UnlockContentType type)
    {
        string prefix = type.ToString().ToLowerInvariant() + ":";
        return unlocked.Where(id => id.StartsWith(prefix, StringComparison.Ordinal)).OrderBy(id => id).ToArray();
    }

    public PlanetTier GetHighestUnlockedPlanetTier()
    {
        PlanetTier highest = PlanetTier.Tier1;
        foreach (PlanetTier tier in Enum.GetValues(typeof(PlanetTier)))
            if (IsUnlocked(tier) && tier > highest) highest = tier;
        return highest;
    }

    internal void WriteToPlayerData(PlayerData data)
    {
        data.unlockedContentIds.Clear();
        data.unlockedContentIds.AddRange(unlocked.OrderBy(id => id));
    }

    internal void ApplyPlayerData(PlayerData data)
    {
        unlocked.Clear();
        if (data?.unlockedContentIds != null)
            foreach (string id in data.unlockedContentIds)
                if (!string.IsNullOrWhiteSpace(id)) unlocked.Add(id);
        if (unlocked.Count == 0)
        {
            ApplyFirstTimeDefaults();
            WriteToPlayerData(data);
            PlayerDataPersistenceManager.NotifyUnlockDataChanged(this);
        }
    }

    private void ApplyFirstTimeDefaults()
    {
        if (defaults != null && defaults.defaultUnlockedIds.Count > 0)
        {
            foreach (string id in defaults.defaultUnlockedIds)
                if (!string.IsNullOrWhiteSpace(id)) unlocked.Add(id.Trim());
            return;
        }
        // Compatibility fallback until campaign defaults are authored.
        foreach (SkillType value in Enum.GetValues(typeof(SkillType))) unlocked.Add(Id(value));
        foreach (BoosterType value in Enum.GetValues(typeof(BoosterType))) unlocked.Add(Id(value));
        foreach (PlanetTier value in Enum.GetValues(typeof(PlanetTier))) unlocked.Add(Id(value));
        unlocked.Add(Id(UnlockContentType.Background, "default"));
        unlocked.Add(Id(UnlockContentType.Orbit, "default"));
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Unlocks/Log All")]
    private void DebugLogAll() => Debug.Log("Unlocks:\n" + string.Join("\n", unlocked.OrderBy(id => id)), this);
    [ContextMenu("DEBUG Unlocks/Unlock Configured Item")]
    private void DebugUnlockConfigured() => Debug.Log($"Unlock {Id(debugContentType, debugContentId)} changed={Unlock(debugContentType, debugContentId)}", this);
    [ContextMenu("DEBUG Unlocks/Lock Configured Item (Test Only)")]
    private void DebugLockConfigured()
    {
        string id = Id(debugContentType, debugContentId);
        bool changed = unlocked.Remove(id);
        if (changed)
        {
            PlayerDataPersistenceManager.NotifyUnlockDataChanged(this);
            UnlockChanged?.Invoke(id, false);
        }
        Debug.Log($"Test-lock {id} changed={changed}", this);
    }
    [ContextMenu("DEBUG Unlocks/Reset To First-Time Defaults")]
    private void DebugResetDefaults()
    {
        foreach (string id in unlocked.ToArray()) UnlockChanged?.Invoke(id, false);
        unlocked.Clear(); ApplyFirstTimeDefaults();
        PlayerDataPersistenceManager.NotifyUnlockDataChanged(this);
        foreach (string id in unlocked) UnlockChanged?.Invoke(id, true);
        DebugLogAll();
    }
#endif
}
