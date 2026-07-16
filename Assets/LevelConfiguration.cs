using System;
using System.Collections.Generic;
using UnityEngine;

public enum StarCriterionType { CompletionElapsedTime, RemainingTime }

[Serializable]
public sealed class StarCriteriaConfiguration
{
    public StarCriterionType criterionType = StarCriterionType.CompletionElapsedTime;
    [Min(0f)] public float threeStarThreshold = 20f;
    [Min(0f)] public float twoStarThreshold = 40f;
    [Min(0)] public long oneStarCoinReward;
    [Min(0)] public long twoStarCoinReward;
    [Min(0)] public long threeStarCoinReward;

    public int Evaluate(float elapsed, float remaining)
    {
        float value = criterionType == StarCriterionType.RemainingTime ? remaining : elapsed;
        if (criterionType == StarCriterionType.RemainingTime)
        {
            if (value >= threeStarThreshold) return 3;
            if (value >= twoStarThreshold) return 2;
        }
        else
        {
            if (value <= threeStarThreshold) return 3;
            if (value <= twoStarThreshold) return 2;
        }
        return 1;
    }

    public long CoinRewardFor(int stars) => stars >= 3 ? threeStarCoinReward : stars == 2 ? twoStarCoinReward : oneStarCoinReward;
}

[Serializable]
public sealed class LevelUnlockReward
{
    public UnlockContentType contentType;
    public string stableContentId;
    public string CanonicalId => UnlockManager.Id(contentType, stableContentId);
}

[CreateAssetMenu(menuName = "Planet Boom/Level Configuration", fileName = "Level_001")]
public sealed class LevelConfiguration : ScriptableObject
{
    [Header("Identity")]
    [Min(1)] public int levelNumber = 1;
    public string stableId = "level_001";
    public string sectorId = "sector_01";
    [Min(0)] public int sectorIndex;
    public string displayName = "Level 1";

    [Header("Gameplay")]
    [Min(1f)] public float timeLimit = 60f;
    public List<LevelObjectiveDefinition> objectives = new List<LevelObjectiveDefinition>();
    public PlanetTier maximumSpawnTier = PlanetTier.Tier2;
    public bool meteorsEnabled = true;
    [Range(0f, 1f)] public float meteorSpawnChance = 0.1f;

    [Header("Rewards")]
    [Min(0)] public long baseSpaceCoinReward;
    public StarCriteriaConfiguration starCriteria = new StarCriteriaConfiguration();
    public List<LevelUnlockReward> unlockRewards = new List<LevelUnlockReward>();

    [Header("Sector Visual IDs")]
    public string backgroundId = "default";
    public string orbitId = "default";

    public bool Validate(out string message)
    {
        var errors = new List<string>();
        if (levelNumber < 1) errors.Add("levelNumber must be >= 1");
        if (string.IsNullOrWhiteSpace(stableId)) errors.Add("stableId is required");
        if (string.IsNullOrWhiteSpace(sectorId)) errors.Add("sectorId is required");
        if (timeLimit <= 0f) errors.Add("timeLimit must be > 0");
        if (objectives == null || objectives.Count == 0) errors.Add("at least one objective is required");
        if (starCriteria == null) errors.Add("starCriteria is required");
        else if (starCriteria.criterionType == StarCriterionType.RemainingTime &&
                 starCriteria.threeStarThreshold < starCriteria.twoStarThreshold)
            errors.Add("RemainingTime three-star threshold must be >= two-star threshold");
        else if (starCriteria.criterionType == StarCriterionType.CompletionElapsedTime &&
                 starCriteria.threeStarThreshold > starCriteria.twoStarThreshold)
            errors.Add("CompletionElapsedTime three-star threshold must be <= two-star threshold");
        if (unlockRewards != null)
            for (int i = 0; i < unlockRewards.Count; i++)
                if (unlockRewards[i] == null || string.IsNullOrWhiteSpace(unlockRewards[i].stableContentId)) errors.Add($"unlockRewards[{i}] has no stable ID");
        message = string.Join("; ", errors);
        return errors.Count == 0;
    }
}

[CreateAssetMenu(menuName = "Planet Boom/Level Catalog", fileName = "LevelCatalog")]
public sealed class LevelConfigurationCatalog : ScriptableObject
{
    public List<LevelConfiguration> levels = new List<LevelConfiguration>();

    public LevelConfiguration FindByNumber(int number) => levels?.Find(item => item != null && item.levelNumber == number);
    public LevelConfiguration FindById(string id) => levels?.Find(item => item != null && string.Equals(item.stableId, id, StringComparison.Ordinal));
}
