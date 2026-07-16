#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class Sector01CampaignAuthoring
{
    private const string Root = "Assets/Progression/Sector01";
    private const string ScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("Tools/Planet Boom/Author Sector 1 Campaign")]
    public static void Author()
    {
        EnsureFolder("Assets/Progression"); EnsureFolder(Root); EnsureFolder("Assets/Resources");
        var configs = new List<LevelConfiguration>
        {
            CreateLevel(1, 45, PlanetTier.Tier3, Pool(100), false, 0, 20, 15, 30,
                Obj(LevelObjectiveType.ReachTier, 1, PlanetTier.Tier3)),
            CreateLevel(2, 75, PlanetTier.Tier4, Pool(80, 20), false, 0, 25, 35, 55,
                Obj(LevelObjectiveType.MergeCount, 15)),
            CreateLevel(3, 75, PlanetTier.Tier4, Pool(70, 30), false, 0, 30, 30, 50,
                Obj(LevelObjectiveType.ComboTarget, 3)),
            CreateLevel(4, 10, PlanetTier.Tier4, Pool(70, 25, 5), false, 0, 40, 8, 4,
                Obj(LevelObjectiveType.ReachTier, 1, PlanetTier.Tier4)),
            CreateLevel(5, 110, PlanetTier.Tier5, Pool(60, 30, 10), false, 0, 50, 55, 80,
                Obj(LevelObjectiveType.ReachTier, 1, PlanetTier.Tier5)),
            CreateLevel(6, 90, PlanetTier.Tier5, Pool(60, 30, 10), true, 0.11f, 60, 45, 70,
                Obj(LevelObjectiveType.MeteorObjective, 1), Obj(LevelObjectiveType.MergeCount, 15)),
            CreateLevel(7, 130, PlanetTier.Tier5, Pool(55, 30, 15), true, 0.10f, 100, 75, 105,
                Obj(LevelObjectiveType.ReachTier, 1, PlanetTier.Tier5), Obj(LevelObjectiveType.MergeCount, 20), Obj(LevelObjectiveType.ComboTarget, 4))
        };
        configs[3].timeMode = LevelTimeMode.MergeTimeRush;
        configs[3].timeRushStartingTime = 10f;
        configs[3].mergeTimeRewards = new List<MergeTimeRewardEntry>
        {
            new MergeTimeRewardEntry(PlanetTier.Tier2, 1f),
            new MergeTimeRewardEntry(PlanetTier.Tier3, 2f),
            new MergeTimeRewardEntry(PlanetTier.Tier4, 3f)
        };
        configs[3].starCriteria.criterionType = StarCriterionType.RemainingTime;
        configs[5].guaranteeMeteorWithinLaunches = true;
        configs[5].meteorGuaranteeLaunchCount = 5;
        configs[3].unlockRewards = new List<LevelUnlockReward> { Reward(UnlockContentType.Planet, "Tier5") };
        configs[6].unlockRewards = new List<LevelUnlockReward> { Reward(UnlockContentType.Background, "background_sector_02") };
        for (int i = 0; i < configs.Count; i++) { EditorUtility.SetDirty(configs[i]); AssetDatabase.SaveAssets(); }

        var catalog = LoadOrCreate<LevelConfigurationCatalog>($"{Root}/Sector01_LevelCatalog.asset");
        catalog.levels = configs; EditorUtility.SetDirty(catalog);

        var defaults = LoadOrCreate<UnlockDefaultsConfig>("Assets/Resources/UnlockDefaults.asset");
        defaults.defaultUnlockedIds = new List<string>
        {
            "planet:Tier1", "planet:Tier2", "planet:Tier3", "planet:Tier4",
            "skill:TimeWarp", "skill:GravitySingularity", "skill:PlanetReroll",
            "background:background_sector_01", "orbit:orbit_default"
        };
        EditorUtility.SetDirty(defaults); AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameManager manager = Object.FindFirstObjectByType<GameManager>();
        if (manager == null) throw new System.InvalidOperationException("GameManager not found in GameScene.");
        var serialized = new SerializedObject(manager);
        SerializedProperty catalogProperty = serialized.FindProperty("levelCatalog");
        if (catalogProperty == null) throw new System.InvalidOperationException("GameManager.levelCatalog serialized property not found.");
        catalogProperty.objectReferenceValue = catalog;
        if (!serialized.ApplyModifiedPropertiesWithoutUndo())
            Debug.LogWarning("Catalog assignment reported no serialized change; verifying object reference anyway.");
        serialized.Update();
        if (serialized.FindProperty("levelCatalog").objectReferenceValue != catalog)
            throw new System.InvalidOperationException("Level Catalog reference did not stick on GameManager.");
        EditorUtility.SetDirty(manager); EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("GameScene save failed.");
        EditorSceneManager.SaveOpenScenes();

        int invalid = 0; var ids = new HashSet<string>(); var numbers = new HashSet<int>();
        foreach (LevelConfiguration config in configs)
            if (!config.Validate(out string message) || !ids.Add(config.stableId) || !numbers.Add(config.levelNumber)) { Debug.LogError($"Invalid {config.name}: {message}", config); invalid++; }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Debug.Log($"Sector 1 authoring complete: {configs.Count} levels, validation errors={invalid}, catalog assigned to GameScene.");
    }

    private static LevelConfiguration CreateLevel(int number, float time, PlanetTier maxMergeTier,
        List<WeightedPlanetTierEntry> spawnPool, bool meteors, float chance,
        long coins, float three, float two, params LevelObjectiveDefinition[] objectives)
    {
        var config = LoadOrCreate<LevelConfiguration>($"{Root}/Sector01_Level{number:00}.asset");
        config.levelNumber = number; config.stableId = $"sector01_level{number:00}"; config.sectorId = "sector_01";
        config.sectorIndex = 0; config.displayName = $"Level {number}"; config.timeLimit = time;
        config.maximumAllowedMergeTier = maxMergeTier;
        config.objectives = new List<LevelObjectiveDefinition>(objectives); config.launcherSpawnPool = spawnPool;
        config.maximumSpawnTier = spawnPool[spawnPool.Count - 1].tier;
        config.meteorsEnabled = meteors; config.meteorSpawnChance = chance; config.baseSpaceCoinReward = coins;
        config.timeMode = LevelTimeMode.Normal; config.timeRushStartingTime = 10f;
        config.mergeTimeRewards = new List<MergeTimeRewardEntry>();
        config.guaranteeMeteorWithinLaunches = false; config.meteorGuaranteeLaunchCount = 6;
        config.starCriteria = new StarCriteriaConfiguration { criterionType = StarCriterionType.CompletionElapsedTime,
            threeStarThreshold = three, twoStarThreshold = two };
        config.backgroundId = "background_sector_01"; config.orbitId = "orbit_default";
        config.unlockRewards = new List<LevelUnlockReward>(); return config;
    }

    private static LevelObjectiveDefinition Obj(LevelObjectiveType type, float progress, PlanetTier tier = PlanetTier.Tier1) =>
        new LevelObjectiveDefinition { type = type, targetProgress = progress, targetTier = tier, required = true };
    private static List<WeightedPlanetTierEntry> Pool(float tier1, float tier2 = 0f, float tier3 = 0f)
    {
        var pool = new List<WeightedPlanetTierEntry> { new WeightedPlanetTierEntry(PlanetTier.Tier1, tier1) };
        if (tier2 > 0f) pool.Add(new WeightedPlanetTierEntry(PlanetTier.Tier2, tier2));
        if (tier3 > 0f) pool.Add(new WeightedPlanetTierEntry(PlanetTier.Tier3, tier3));
        return pool;
    }
    private static LevelUnlockReward Reward(UnlockContentType type, string id) => new LevelUnlockReward { contentType = type, stableContentId = id };
    private static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path); if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>(); AssetDatabase.CreateAsset(asset, path); return asset;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
}
#endif
