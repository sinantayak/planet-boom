using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.CloudSave.Models;
using Unity.Services.CloudSave.Models.Data.Player;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[Serializable]
public sealed class SkillQuantityData
{
    public string skillType;
    public int quantity;
}

[Serializable]
public sealed class BoosterQuantityData
{
    public string boosterType;
    public int quantity;
}

[Serializable]
public sealed class LevelStarsData
{
    public int level;
    public int bestStars;
}

[Serializable]
public sealed class PlayerData
{
    public int schemaVersion = 1;
    public long revision;
    public long modifiedUtcTicks;
    public bool migratedFromLegacyPlayerPrefs;
    public List<SkillQuantityData> skillInventory = new List<SkillQuantityData>();
    public List<BoosterQuantityData> boosterInventory = new List<BoosterQuantityData>();
    public List<string> quickSlots = new List<string>();
    public int highestUnlockedLevel = 1;
    public List<LevelStarsData> bestStarsByLevel = new List<LevelStarsData>();
    public List<string> unlockedContentIds = new List<string>();

    // Reserved for upcoming phases. They are part of the versioned document
    // now, but no gameplay system reads or mutates them yet.
    public long spaceCoin;
    public int lives = 5;
}

// Cloud Save is the long-term source, with an immediate per-player PlayerPrefs
// JSON cache. One versioned document keeps future migrations atomic and avoids
// scattering gameplay state across unrelated cloud keys.
public sealed class PlayerDataPersistenceManager : MonoBehaviour
{
    public enum PlayerDataState
    {
        NotStarted,
        WaitingForAuthentication,
        LoadingCloud,
        ReadyCloud,
        ReadyLocalFallback,
        Failed
    }

    private const string CloudKey = "player_data_v1";
    private const string LocalCachePrefix = "PlayerData.Cache.v1.";
    private const string LocalDirtyPrefix = "PlayerData.Dirty.v1.";
    private const float CloudSaveDebounceSeconds = 1.25f;

    public static PlayerDataPersistenceManager Instance { get; private set; }
    public static event Action<PlayerDataState> StateChanged;
    public static event Action<PlayerData> DataLoaded;
    public static event Action<Exception> CloudOperationFailed;
    // Level Map UI can subscribe without depending on the save format.
    public static event Action ProgressionChanged;
    public static event Action<int, int> LevelBestStarsChanged;
    public static event Action<int> HighestUnlockedLevelChanged;
    public static event Action<long> SpaceCoinChanged;
    public static event Action<int> LivesChanged;

    [Header("Runtime Status (Play Mode)")]
    [SerializeField] private PlayerDataState currentState = PlayerDataState.NotStarted;
    [SerializeField] private bool isSaving;
    [SerializeField] private long currentRevision;
    [SerializeField] private long lastConfirmedCloudRevision = -1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [Header("Temporary Progression Debug")]
    [SerializeField, Min(1)] private int debugLevelNumber = 1;
    [SerializeField, Range(1, 3)] private int debugStars = 3;
#endif

    public PlayerDataState State => currentState;
    public bool IsLoaded => State == PlayerDataState.ReadyCloud || State == PlayerDataState.ReadyLocalFallback;
    public bool IsCloudBacked => State == PlayerDataState.ReadyCloud;
    public bool IsSaving => isSaving;
    public PlayerData CurrentData => currentData;
    public int HighestUnlockedLevel => currentData != null ? currentData.highestUnlockedLevel : 1;
    public long SpaceCoin => currentData != null ? currentData.spaceCoin : 0;
    public int Lives => currentData != null ? currentData.lives : new PlayerData().lives;
    public Task ReadyTask => readyTaskSource.Task;

    private static SkillInventoryManager registeredSkillInventory;
    private static BoosterInventoryManager registeredBoosterInventory;
    private static UnlockManager registeredUnlockManager;
    private PlayerData currentData;
    private string playerId = string.Empty;
    private string cloudWriteLock;
    private bool localDirty;
    private bool inventoryChangedBeforeLoad;
    private bool boosterInventoryChangedBeforeLoad;
    private bool unlockDataChangedBeforeLoad;
    private long pendingSpaceCoin;
    private readonly List<LevelStarsData> pendingLevelCompletions = new List<LevelStarsData>();
    private readonly List<string> unlocksSinceLastCompletion = new List<string>();
    private Coroutine debouncedSaveRoutine;
    private TaskCompletionSource<bool> readyTaskSource = NewReadyTaskSource();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void BootstrapBeforeFirstScene()
    {
        if (Instance != null)
            return;
        var serviceObject = new GameObject("PlayerDataPersistenceManager");
        DontDestroyOnLoad(serviceObject);
        serviceObject.AddComponent<PlayerDataPersistenceManager>();
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
        UnityGamingServicesManager.AuthenticationReady += HandleAuthenticationReady;
        _ = BeginLoadingAsync();
    }

    private void OnDestroy()
    {
        UnityGamingServicesManager.AuthenticationReady -= HandleAuthenticationReady;
        if (Instance == this)
            Instance = null;
    }

    private async Task BeginLoadingAsync()
    {
        SetState(PlayerDataState.WaitingForAuthentication);
        while (UnityGamingServicesManager.Instance == null)
            await Task.Yield();

        try
        {
            await UnityGamingServicesManager.Instance.ReadyTask;
            await LoadForAuthenticatedPlayerAsync(UnityGamingServicesManager.Instance.PlayerId);
        }
        catch (Exception exception)
        {
            LoadLocalFallback(string.Empty, exception);
        }
    }

    private async void HandleAuthenticationReady(string authenticatedPlayerId)
    {
        if (IsLoaded && playerId == authenticatedPlayerId)
            return;
        try
        {
            await LoadForAuthenticatedPlayerAsync(authenticatedPlayerId);
        }
        catch (Exception exception)
        {
            LoadLocalFallback(authenticatedPlayerId, exception);
        }
    }

    private async Task LoadForAuthenticatedPlayerAsync(string authenticatedPlayerId)
    {
        if (string.IsNullOrEmpty(authenticatedPlayerId))
            throw new InvalidOperationException("Cannot load PlayerData without an authenticated Player ID.");

        playerId = authenticatedPlayerId;
        SetState(PlayerDataState.LoadingCloud);

        PlayerData local = LoadLocalCache(playerId);
        bool dirty = PlayerPrefs.GetInt(LocalDirtyKey(playerId), 0) == 1;

        Dictionary<string, Item> loadedItems;
        try
        {
            loadedItems = await UnityGamingServicesManager.Instance.CloudSave.Data.Player.LoadAsync(
                new HashSet<string> { CloudKey }, new LoadOptions());
        }
        catch (Exception exception)
        {
            LoadLocalFallback(playerId, exception);
            return;
        }

        PlayerData selected;
        bool needsCloudWrite = false;
        if (loadedItems.TryGetValue(CloudKey, out Item cloudItem))
        {
            cloudWriteLock = cloudItem.WriteLock;
            string cloudJson = cloudItem.Value.GetAs<string>();
            PlayerData cloud = DeserializeAndNormalize(cloudJson);
            if (cloud == null)
                throw new InvalidOperationException("Cloud PlayerData exists but could not be parsed safely.");
            lastConfirmedCloudRevision = cloud.revision;

            // Safe/simple stage-one conflict rule: an explicitly dirty local
            // document wins only when its revision is strictly newer. Cloud
            // wins ties and all clean-cache cases.
            if (dirty && local != null && local.revision > cloud.revision)
            {
                selected = local;
                needsCloudWrite = true;
                Debug.Log($"PlayerData: newer dirty local revision {local.revision} selected over " +
                          $"cloud revision {cloud.revision}.", this);
            }
            else
            {
                selected = cloud;
                dirty = false;
                Debug.Log($"PlayerData: loaded cloud revision {cloud.revision}.", this);
            }
        }
        else
        {
            cloudWriteLock = null;
            selected = local ?? CreateLegacySnapshot();
            selected.migratedFromLegacyPlayerPrefs = true;
            selected.revision = Math.Max(1, selected.revision);
            selected.modifiedUtcTicks = DateTime.UtcNow.Ticks;
            needsCloudWrite = true;
            Debug.Log("PlayerData: no cloud document; migrating existing local PlayerPrefs/cache.", this);
        }

        currentData = Normalize(selected);
        localDirty = dirty || needsCloudWrite;
        ApplyChangesThatOccurredWhileLoading();
        SaveLocalCache(localDirty);

        if (needsCloudWrite || localDirty)
        {
            bool saved = await SaveCloudSnapshotAsync();
            SetState(saved ? PlayerDataState.ReadyCloud : PlayerDataState.ReadyLocalFallback);
        }
        else
        {
            SetState(PlayerDataState.ReadyCloud);
        }

        FinishLoad();
    }

    private void LoadLocalFallback(string authenticatedPlayerId, Exception exception)
    {
        if (!string.IsNullOrEmpty(authenticatedPlayerId))
            playerId = authenticatedPlayerId;

        currentData = LoadLocalCache(playerId) ?? CreateLegacySnapshot();
        currentData = Normalize(currentData);
        localDirty = true;
        ApplyChangesThatOccurredWhileLoading();
        SaveLocalCache(true);
        SetState(PlayerDataState.ReadyLocalFallback);
        FinishLoad();

        Debug.LogWarning($"PlayerData: Cloud unavailable; using local cache. " +
                         $"{exception.GetType().Name}: {exception.Message}", this);
        CloudOperationFailed?.Invoke(exception);
    }

    private void FinishLoad()
    {
        currentRevision = currentData.revision;
        registeredSkillInventory?.ApplyPlayerData(currentData);
        registeredBoosterInventory?.ApplyPlayerData(currentData);
        registeredUnlockManager?.ApplyPlayerData(currentData);
        readyTaskSource.TrySetResult(true);
        DataLoaded?.Invoke(currentData);
        ProgressionChanged?.Invoke();
        SpaceCoinChanged?.Invoke(SpaceCoin);
        LivesChanged?.Invoke(Lives);
    }

    public bool AddSpaceCoin(long amount)
    {
        if (amount <= 0)
            return false;

        if (!IsLoaded || currentData == null)
        {
            pendingSpaceCoin = SaturatingAdd(pendingSpaceCoin, amount);
            return true;
        }

        long newAmount = SaturatingAdd(currentData.spaceCoin, amount);
        if (newAmount == currentData.spaceCoin)
            return false;
        currentData.spaceCoin = newAmount;
        MarkChangedAndScheduleSave();
        SpaceCoinChanged?.Invoke(newAmount);
        return true;
    }

    public void RecordLevelCompleted(int levelNumber, int starsEarned)
    {
        levelNumber = Mathf.Max(1, levelNumber);
        starsEarned = Mathf.Clamp(starsEarned, 1, 3);
        if (!IsLoaded || currentData == null)
        {
            pendingLevelCompletions.Add(new LevelStarsData { level = levelNumber, bestStars = starsEarned });
            return;
        }

        int previousBest = GetBestStars(levelNumber);
        int previousHighest = HighestUnlockedLevel;
        if (ApplyLevelCompletion(currentData, levelNumber, starsEarned))
        {
            MarkChangedAndScheduleSave();
            NotifyProgressionChanged(levelNumber, previousBest, previousHighest);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            string newUnlocks = unlocksSinceLastCompletion.Count > 0
                ? string.Join(", ", unlocksSinceLastCompletion) : "none";
            Debug.Log($"[Progression]\nCompleted Level: {levelNumber}\nStars Earned: {starsEarned}\n" +
                      $"Best Stars: {GetBestStars(levelNumber)}\nHighest Unlocked Level: {HighestUnlockedLevel}\n" +
                      $"New Unlocks: {newUnlocks}\nSave Dirty: {localDirty}", this);
#endif
        }
        unlocksSinceLastCompletion.Clear();
    }

    public bool IsLevelUnlocked(int levelNumber)
    {
        return levelNumber >= 1 && levelNumber <= HighestUnlockedLevel;
    }

    public int GetBestStars(int levelNumber)
    {
        if (currentData == null || levelNumber < 1)
            return 0;
        LevelStarsData entry = currentData.bestStarsByLevel.Find(item => item.level == levelNumber);
        return entry != null ? Mathf.Clamp(entry.bestStars, 0, 3) : 0;
    }

    private void NotifyProgressionChanged(int levelNumber, int previousBest, int previousHighest)
    {
        int newBest = GetBestStars(levelNumber);
        if (newBest != previousBest)
            LevelBestStarsChanged?.Invoke(levelNumber, newBest);
        if (HighestUnlockedLevel != previousHighest)
            HighestUnlockedLevelChanged?.Invoke(HighestUnlockedLevel);
        ProgressionChanged?.Invoke();
    }

    public Task SaveNowAsync()
    {
        if (!IsLoaded || !localDirty || UnityGamingServicesManager.Instance == null ||
            !UnityGamingServicesManager.Instance.IsCloudSaveAvailable)
            return Task.CompletedTask;
        return SaveCloudSnapshotAsync();
    }

    internal static void RegisterSkillInventory(SkillInventoryManager manager)
    {
        registeredSkillInventory = manager;
        if (Instance != null && Instance.IsLoaded && Instance.currentData != null)
            manager.ApplyPlayerData(Instance.currentData);
    }

    internal static void RegisterBoosterInventory(BoosterInventoryManager manager)
    {
        registeredBoosterInventory = manager;
        if (Instance != null && Instance.IsLoaded && Instance.currentData != null)
            manager.ApplyPlayerData(Instance.currentData);
    }

    internal static void RegisterUnlockManager(UnlockManager manager)
    {
        registeredUnlockManager = manager;
        if (Instance != null && Instance.IsLoaded && Instance.currentData != null) manager.ApplyPlayerData(Instance.currentData);
    }
    internal static void UnregisterUnlockManager(UnlockManager manager)
    {
        if (registeredUnlockManager == manager) registeredUnlockManager = null;
    }
    internal static void NotifyUnlockDataChanged(UnlockManager manager)
    {
        if (Instance == null) return;
        if (!Instance.IsLoaded || Instance.currentData == null) { Instance.unlockDataChangedBeforeLoad = true; return; }
        var previous = new HashSet<string>(Instance.currentData.unlockedContentIds, StringComparer.Ordinal);
        manager.WriteToPlayerData(Instance.currentData);
        foreach (string id in Instance.currentData.unlockedContentIds)
            if (!previous.Contains(id) && !Instance.unlocksSinceLastCompletion.Contains(id))
                Instance.unlocksSinceLastCompletion.Add(id);
        Instance.MarkChangedAndScheduleSave();
    }

    internal static void UnregisterBoosterInventory(BoosterInventoryManager manager)
    {
        if (registeredBoosterInventory == manager)
            registeredBoosterInventory = null;
    }

    internal static void NotifyBoosterInventoryChanged(BoosterInventoryManager manager)
    {
        if (Instance == null)
            return;
        if (!Instance.IsLoaded || Instance.currentData == null)
        {
            Instance.boosterInventoryChangedBeforeLoad = true;
            return;
        }
        manager.WriteToPlayerData(Instance.currentData);
        Instance.MarkChangedAndScheduleSave();
    }

    internal static void UnregisterSkillInventory(SkillInventoryManager manager)
    {
        if (registeredSkillInventory == manager)
            registeredSkillInventory = null;
    }

    internal static void NotifySkillInventoryChanged(SkillInventoryManager manager)
    {
        if (Instance == null)
            return;
        if (!Instance.IsLoaded || Instance.currentData == null)
        {
            Instance.inventoryChangedBeforeLoad = true;
            return;
        }

        manager.WriteToPlayerData(Instance.currentData);
        Instance.MarkChangedAndScheduleSave();
    }

    private void MarkChangedAndScheduleSave()
    {
        currentData.revision++;
        currentData.modifiedUtcTicks = DateTime.UtcNow.Ticks;
        currentRevision = currentData.revision;
        localDirty = true;
        SaveLocalCache(true);

        if (debouncedSaveRoutine != null)
            StopCoroutine(debouncedSaveRoutine);
        debouncedSaveRoutine = StartCoroutine(DebouncedCloudSave());
    }

    private IEnumerator DebouncedCloudSave()
    {
        yield return new WaitForSecondsRealtime(CloudSaveDebounceSeconds);
        debouncedSaveRoutine = null;
        _ = SaveCloudSnapshotAsync();
    }

    private async Task<bool> SaveCloudSnapshotAsync()
    {
        if (currentData == null || UnityGamingServicesManager.Instance == null ||
            !UnityGamingServicesManager.Instance.IsCloudSaveAvailable || isSaving)
            return false;

        isSaving = true;
        long savedRevision = currentData.revision;
        string json = JsonUtility.ToJson(currentData);
        try
        {
            var payload = new Dictionary<string, SaveItem>
            {
                { CloudKey, new SaveItem(json, cloudWriteLock) }
            };
            Dictionary<string, string> locks = await UnityGamingServicesManager.Instance.CloudSave.Data.Player.SaveAsync(
                payload, new SaveOptions());
            if (locks.TryGetValue(CloudKey, out string newLock))
                cloudWriteLock = newLock;

            if (currentData.revision == savedRevision)
            {
                localDirty = false;
                SaveLocalCache(false);
            }
            Debug.Log($"PlayerData: cloud save succeeded at revision {savedRevision}.", this);
            lastConfirmedCloudRevision = savedRevision;
            return true;
        }
        catch (Exception exception)
        {
            localDirty = true;
            SaveLocalCache(true);
            Debug.LogWarning($"PlayerData: cloud save failed; local cache retained. " +
                             $"{exception.GetType().Name}: {exception.Message}", this);
            CloudOperationFailed?.Invoke(exception);
            return false;
        }
        finally
        {
            isSaving = false;
            if (localDirty && currentData != null && currentData.revision > savedRevision &&
                UnityGamingServicesManager.Instance != null &&
                UnityGamingServicesManager.Instance.IsCloudSaveAvailable)
            {
                if (debouncedSaveRoutine != null)
                    StopCoroutine(debouncedSaveRoutine);
                debouncedSaveRoutine = StartCoroutine(DebouncedCloudSave());
            }
        }
    }

    private void ApplyChangesThatOccurredWhileLoading()
    {
        if (inventoryChangedBeforeLoad && registeredSkillInventory != null)
        {
            registeredSkillInventory.WriteToPlayerData(currentData);
            currentData.revision++;
            currentData.modifiedUtcTicks = DateTime.UtcNow.Ticks;
            localDirty = true;
            inventoryChangedBeforeLoad = false;
        }

        if (boosterInventoryChangedBeforeLoad && registeredBoosterInventory != null)
        {
            registeredBoosterInventory.WriteToPlayerData(currentData);
            currentData.revision++;
            currentData.modifiedUtcTicks = DateTime.UtcNow.Ticks;
            localDirty = true;
            boosterInventoryChangedBeforeLoad = false;
        }
        if (unlockDataChangedBeforeLoad && registeredUnlockManager != null)
        {
            registeredUnlockManager.WriteToPlayerData(currentData);
            currentData.revision++; currentData.modifiedUtcTicks = DateTime.UtcNow.Ticks;
            localDirty = true; unlockDataChangedBeforeLoad = false;
        }

        foreach (LevelStarsData completion in pendingLevelCompletions)
        {
            if (ApplyLevelCompletion(currentData, completion.level, completion.bestStars))
            {
                currentData.revision++;
                currentData.modifiedUtcTicks = DateTime.UtcNow.Ticks;
                localDirty = true;
            }
        }
        pendingLevelCompletions.Clear();

        if (pendingSpaceCoin > 0)
        {
            currentData.spaceCoin = SaturatingAdd(currentData.spaceCoin, pendingSpaceCoin);
            currentData.revision++;
            currentData.modifiedUtcTicks = DateTime.UtcNow.Ticks;
            localDirty = true;
            pendingSpaceCoin = 0;
        }
    }

    private static long SaturatingAdd(long current, long amount)
    {
        if (amount <= 0)
            return current;
        return current > long.MaxValue - amount ? long.MaxValue : current + amount;
    }

    private static bool ApplyLevelCompletion(PlayerData data, int levelNumber, int starsEarned)
    {
        bool changed = false;
        int unlocked = levelNumber + 1;
        if (unlocked > data.highestUnlockedLevel)
        {
            data.highestUnlockedLevel = unlocked;
            changed = true;
        }

        LevelStarsData entry = data.bestStarsByLevel.Find(item => item.level == levelNumber);
        if (entry == null)
        {
            data.bestStarsByLevel.Add(new LevelStarsData { level = levelNumber, bestStars = starsEarned });
            changed = true;
        }
        else if (starsEarned > entry.bestStars)
        {
            entry.bestStars = starsEarned;
            changed = true;
        }
        return changed;
    }

    private void SaveLocalCache(bool dirty)
    {
        if (currentData == null)
            return;
        string id = string.IsNullOrEmpty(playerId) ? "offline" : playerId;
        PlayerPrefs.SetString(LocalCacheKey(id), JsonUtility.ToJson(currentData));
        PlayerPrefs.SetInt(LocalDirtyKey(id), dirty ? 1 : 0);
        PlayerPrefs.Save();
    }

    private static PlayerData LoadLocalCache(string id)
    {
        id = string.IsNullOrEmpty(id) ? "offline" : id;
        string json = PlayerPrefs.GetString(LocalCacheKey(id), string.Empty);
        return DeserializeAndNormalize(json);
    }

    private static PlayerData CreateLegacySnapshot()
    {
        var data = new PlayerData
        {
            migratedFromLegacyPlayerPrefs = true,
            revision = 1,
            modifiedUtcTicks = DateTime.UtcNow.Ticks,
            highestUnlockedLevel = 1
        };

        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
        {
            int quantity = Mathf.Max(0, PlayerPrefs.GetInt("SkillInventory.Count." + type, 0));
            if (type == SkillType.CosmicAbduction)
            {
                quantity = (int)Math.Min(int.MaxValue, (long)quantity +
                    Mathf.Max(0, PlayerPrefs.GetInt("SkillInventory.Count.EmergencyBlast", 0)));
            }
            data.skillInventory.Add(new SkillQuantityData
            {
                skillType = type.ToString(),
                quantity = quantity
            });
        }
        for (int i = 0; i < SkillInventoryManager.QuickSlotCount; i++)
            data.quickSlots.Add(PlayerPrefs.GetString("SkillInventory.QuickSlot." + i, string.Empty));
        return Normalize(data);
    }

    private static PlayerData CreateFreshPlayerData(long revision)
    {
        var data = new PlayerData
        {
            schemaVersion = 1,
            revision = Math.Max(1, revision),
            modifiedUtcTicks = DateTime.UtcNow.Ticks,
            migratedFromLegacyPlayerPrefs = false
        };
        UnlockDefaultsConfig defaults = Resources.Load<UnlockDefaultsConfig>("UnlockDefaults");
        if (defaults == null)
            throw new InvalidOperationException("Resources/UnlockDefaults.asset is required for a safe progression reset.");
        if (defaults.defaultUnlockedIds != null)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in defaults.defaultUnlockedIds)
                if (!string.IsNullOrWhiteSpace(id) && unique.Add(id.Trim()))
                    data.unlockedContentIds.Add(id.Trim());
        }
        return Normalize(data);
    }

    private static PlayerData DeserializeAndNormalize(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return Normalize(JsonUtility.FromJson<PlayerData>(json));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static PlayerData Normalize(PlayerData data)
    {
        if (data == null)
            return null;
        data.schemaVersion = Math.Max(1, data.schemaVersion);
        data.revision = Math.Max(0, data.revision);
        data.highestUnlockedLevel = Mathf.Max(1, data.highestUnlockedLevel);
        data.lives = Mathf.Max(0, data.lives);
        data.spaceCoin = Math.Max(0, data.spaceCoin);
        data.skillInventory ??= new List<SkillQuantityData>();
        data.boosterInventory ??= new List<BoosterQuantityData>();
        data.quickSlots ??= new List<string>();
        data.bestStarsByLevel ??= new List<LevelStarsData>();
        data.unlockedContentIds ??= new List<string>();
        NormalizeProgression(data);
        while (data.quickSlots.Count < SkillInventoryManager.QuickSlotCount)
            data.quickSlots.Add(string.Empty);
        if (data.quickSlots.Count > SkillInventoryManager.QuickSlotCount)
            data.quickSlots.RemoveRange(SkillInventoryManager.QuickSlotCount,
                data.quickSlots.Count - SkillInventoryManager.QuickSlotCount);
        return data;
    }

    private static void NormalizeProgression(PlayerData data)
    {
        // Older or manually edited documents may contain duplicate rows,
        // invalid stars, or stars without a matching highestUnlockedLevel.
        // Consolidating with max-stars makes loading lossless and idempotent.
        var bestByLevel = new Dictionary<int, int>();
        foreach (LevelStarsData entry in data.bestStarsByLevel)
        {
            if (entry == null || entry.level < 1)
                continue;
            int stars = Mathf.Clamp(entry.bestStars, 0, 3);
            if (!bestByLevel.TryGetValue(entry.level, out int existing) || stars > existing)
                bestByLevel[entry.level] = stars;
        }

        data.bestStarsByLevel.Clear();
        foreach (KeyValuePair<int, int> pair in bestByLevel)
        {
            if (pair.Value > 0)
                data.bestStarsByLevel.Add(new LevelStarsData { level = pair.Key, bestStars = pair.Value });
            if (pair.Value > 0)
                data.highestUnlockedLevel = Mathf.Max(data.highestUnlockedLevel, pair.Key + 1);
        }
        data.bestStarsByLevel.Sort((left, right) => left.level.CompareTo(right.level));
    }

    private void SetState(PlayerDataState newState)
    {
        if (currentState == newState)
            return;
        currentState = newState;
        StateChanged?.Invoke(newState);
    }

    private static string LocalCacheKey(string id) => LocalCachePrefix + id;
    private static string LocalDirtyKey(string id) => LocalDirtyPrefix + id;
    private static TaskCompletionSource<bool> NewReadyTaskSource() =>
        new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Log PlayerData Snapshot")]
    private void DebugLogSnapshot()
    {
        if (currentData == null)
        {
            Debug.Log($"[PlayerData Snapshot] state={State}; no data loaded.", this);
            return;
        }

        var stars = new List<string>();
        foreach (LevelStarsData row in currentData.bestStarsByLevel)
            if (row != null) stars.Add($"L{row.level}:{row.bestStars}");
        var skills = new List<string>();
        foreach (SkillQuantityData row in currentData.skillInventory)
            if (row != null) skills.Add($"{row.skillType}:{row.quantity}");
        var boosters = new List<string>();
        foreach (BoosterQuantityData row in currentData.boosterInventory)
            if (row != null) boosters.Add($"{row.boosterType}:{row.quantity}");

        string authenticatedId = !string.IsNullOrEmpty(playerId) ? playerId :
            UnityGamingServicesManager.Instance != null ? UnityGamingServicesManager.Instance.PlayerId : "offline";
        Debug.Log($"[PlayerData Snapshot]\nPlayerId: {authenticatedId}\nState: {State}\n" +
                  $"HighestUnlockedLevel: {HighestUnlockedLevel}\nBestStarsByLevel: [{string.Join(", ", stars)}]\n" +
                  $"SpaceCoin: {currentData.spaceCoin}\nLives: {currentData.lives}\n" +
                  $"Skill Inventory: [{string.Join(", ", skills)}]\nBooster Inventory: [{string.Join(", ", boosters)}]\n" +
                  $"Quick Slots: [{string.Join(", ", currentData.quickSlots)}]\n" +
                  $"UnlockedContentIds: [{string.Join(", ", currentData.unlockedContentIds)}]\n" +
                  $"Dirty: {localDirty}; IsSaving: {isSaving}; Local Revision: {currentData.revision}; " +
                  $"Confirmed Cloud Revision: {lastConfirmedCloudRevision}; WriteLock: {!string.IsNullOrEmpty(cloudWriteLock)}", this);
    }

#if UNITY_EDITOR
    [ContextMenu("DEBUG Reset All Player Progress")]
    private void DebugResetAllPlayerProgress()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("PlayerData DEBUG reset must be run in Play Mode on the live persistence manager.", this);
            return;
        }
        if (!EditorUtility.DisplayDialog("Reset All Player Progress?",
                "This overwrites the current player's local cache and Cloud Save with first-time defaults. " +
                "The Authentication PlayerId is preserved.", "RESET ALL PROGRESS", "Cancel"))
            return;
        _ = DebugResetAllPlayerProgressAsync();
    }

    private async Task DebugResetAllPlayerProgressAsync()
    {
        if (!IsLoaded || currentData == null)
        {
            Debug.LogWarning($"PlayerData DEBUG reset aborted: data is not ready (state={State}).", this);
            return;
        }

        if (debouncedSaveRoutine != null)
        {
            StopCoroutine(debouncedSaveRoutine);
            debouncedSaveRoutine = null;
        }
        while (isSaving)
            await Task.Yield();

        long freshRevision = Math.Max(currentData.revision, lastConfirmedCloudRevision) + 1;
        currentData = CreateFreshPlayerData(freshRevision);
        currentRevision = currentData.revision;
        localDirty = true;
        inventoryChangedBeforeLoad = false;
        boosterInventoryChangedBeforeLoad = false;
        unlockDataChangedBeforeLoad = false;
        pendingSpaceCoin = 0;
        pendingLevelCompletions.Clear();
        unlocksSinceLastCompletion.Clear();

        PlayerPrefs.DeleteKey("SkillInventory.Count.EmergencyBlast");
        registeredSkillInventory?.ApplyPlayerData(currentData);
        registeredBoosterInventory?.EndCurrentRun();
        registeredBoosterInventory?.ApplyPlayerData(currentData);
        registeredUnlockManager?.ApplyPlayerData(currentData);
        GameManager.Instance?.DiscardLevelEarnedCoins();
        SaveLocalCache(true);

        DataLoaded?.Invoke(currentData);
        ProgressionChanged?.Invoke();
        HighestUnlockedLevelChanged?.Invoke(HighestUnlockedLevel);
        SpaceCoinChanged?.Invoke(SpaceCoin);
        LivesChanged?.Invoke(Lives);

        bool cloudAvailable = UnityGamingServicesManager.Instance != null &&
                              UnityGamingServicesManager.Instance.IsCloudSaveAvailable;
        bool cloudSaved = cloudAvailable && await SaveCloudSnapshotAsync();
        if (cloudSaved) SetState(PlayerDataState.ReadyCloud);
        else if (!cloudAvailable)
            Debug.LogWarning("[PlayerData RESET] Cloud Save is unavailable. The fresh local cache is dirty and will be uploaded when Cloud Save becomes available.", this);
        Debug.Log($"[PlayerData RESET] Fresh state applied for PlayerId=" +
                  $"{(string.IsNullOrEmpty(playerId) ? "offline" : playerId)}; " +
                  $"localRevision={currentData.revision}; localDirty={localDirty}; " +
                  $"cloudAvailable={cloudAvailable}; cloudConfirmed={cloudSaved}; " +
                  $"confirmedCloudRevision={lastConfirmedCloudRevision}.", this);
        DebugLogSnapshot();
    }
#endif

    [ContextMenu("DEBUG Force Cloud Save")]
    private void DebugForceCloudSave()
    {
        _ = SaveNowAsync();
    }

    [ContextMenu("DEBUG Complete Configured Level")]
    private void DebugCompleteConfiguredLevel()
    {
        if (!IsLoaded)
        {
            Debug.LogWarning("PlayerData DEBUG: progression is not loaded yet.", this);
            return;
        }
        RecordLevelCompleted(debugLevelNumber, Mathf.Clamp(debugStars, 1, 3));
        Debug.Log($"PlayerData DEBUG: completed level {debugLevelNumber} with " +
                  $"{Mathf.Clamp(debugStars, 1, 3)} star(s).", this);
    }

    [ContextMenu("DEBUG Log Level Progression")]
    private void DebugLogLevelProgression()
    {
        if (currentData == null)
        {
            Debug.Log($"PlayerData DEBUG: progression unavailable; state={State}.", this);
            return;
        }

        var summary = new List<string>();
        foreach (LevelStarsData entry in currentData.bestStarsByLevel)
            summary.Add($"L{entry.level}:{entry.bestStars}");
        Debug.Log($"PlayerData DEBUG: highest unlocked={HighestUnlockedLevel}; " +
                  $"best stars=[{string.Join(", ", summary)}].", this);
    }
#endif
}
