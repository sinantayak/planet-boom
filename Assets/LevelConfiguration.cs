using System;
using System.Collections.Generic;
using UnityEngine;

public enum StarCriterionType { CompletionElapsedTime, RemainingTime }
public enum LevelTimeMode { Normal, MergeTimeRush }

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

[Serializable]
public sealed class WeightedPlanetTierEntry
{
    public PlanetTier tier = PlanetTier.Tier1;
    [Min(0f)] public float weight = 1f;

    public WeightedPlanetTierEntry() { }
    public WeightedPlanetTierEntry(PlanetTier tier, float weight)
    {
        this.tier = tier;
        this.weight = weight;
    }
}

[Serializable]
public sealed class MergeTimeRewardEntry
{
    public PlanetTier resultTier = PlanetTier.Tier2;
    [Min(0f)] public float bonusSeconds = 1f;

    public MergeTimeRewardEntry() { }
    public MergeTimeRewardEntry(PlanetTier resultTier, float bonusSeconds)
    {
        this.resultTier = resultTier;
        this.bonusSeconds = bonusSeconds;
    }
}

// The single weighted-selection path used by every configured level. It does
// not infer candidates from unlock state: the authored pool defines what may
// spawn, while the predicate only rejects accidentally configured locked tiers.
public static class WeightedPlanetTierSelector
{
    public static bool TrySelect(IReadOnlyList<WeightedPlanetTierEntry> entries,
        Func<PlanetTier, bool> isUnlocked, out PlanetTier selected)
    {
        selected = PlanetTier.Tier1;
        if (entries == null || entries.Count == 0)
            return false;

        var seen = new HashSet<PlanetTier>();
        float total = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            WeightedPlanetTierEntry entry = entries[i];
            if (!IsUsable(entry, isUnlocked) || !seen.Add(entry.tier))
                continue;
            total += entry.weight;
        }
        if (!(total > 0f) || float.IsInfinity(total) || float.IsNaN(total))
            return false;

        float roll = UnityEngine.Random.value * total;
        seen.Clear();
        PlanetTier lastValid = PlanetTier.Tier1;
        for (int i = 0; i < entries.Count; i++)
        {
            WeightedPlanetTierEntry entry = entries[i];
            if (!IsUsable(entry, isUnlocked) || !seen.Add(entry.tier))
                continue;
            lastValid = entry.tier;
            roll -= entry.weight;
            if (roll <= 0f)
            {
                selected = entry.tier;
                return true;
            }
        }

        // Floating-point edge at the end of the interval.
        selected = lastValid;
        return true;
    }

    private static bool IsUsable(WeightedPlanetTierEntry entry,
        Func<PlanetTier, bool> isUnlocked)
    {
        return entry != null && Enum.IsDefined(typeof(PlanetTier), entry.tier) &&
               entry.weight > 0f && !float.IsNaN(entry.weight) &&
               !float.IsInfinity(entry.weight) &&
               (isUnlocked == null || isUnlocked(entry.tier));
    }
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
    [Tooltip("Highest planet tier this level may create, even if higher tiers are permanently unlocked.")]
    public PlanetTier maximumAllowedMergeTier = PlanetTier.Tier4;
    public List<LevelObjectiveDefinition> objectives = new List<LevelObjectiveDefinition>();
    [Tooltip("Relative launcher probabilities. Only listed tiers can spawn directly; weights do not need to total 100.")]
    public List<WeightedPlanetTierEntry> launcherSpawnPool = new List<WeightedPlanetTierEntry>
    {
        new WeightedPlanetTierEntry(PlanetTier.Tier1, 1f)
    };
    [HideInInspector] // Serialized only for compatibility with already-authored assets.
    public PlanetTier maximumSpawnTier = PlanetTier.Tier2;
    public bool meteorsEnabled = true;
    [Range(0f, 1f)] public float meteorSpawnChance = 0.1f;

    [Header("Timer Mode")]
    public LevelTimeMode timeMode = LevelTimeMode.Normal;
    [Min(0.1f)] public float timeRushStartingTime = 10f;
    public List<MergeTimeRewardEntry> mergeTimeRewards = new List<MergeTimeRewardEntry>();

    [Header("Meteor Guarantee")]
    [Tooltip("When enabled, the queue forces a meteor no later than this launch number unless one appeared naturally first.")]
    public bool guaranteeMeteorWithinLaunches;
    [Min(1)] public int meteorGuaranteeLaunchCount = 6;

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
        if (!Enum.IsDefined(typeof(PlanetTier), maximumAllowedMergeTier))
            errors.Add("maximumAllowedMergeTier is outside supported PlanetTier values");
        if (objectives == null || objectives.Count == 0) errors.Add("at least one objective is required");
        ValidateSpawnPool(null, errors);
        ValidateTimeMode(errors);
        ValidateMeteorGuarantee(errors);
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

    public bool TryGetMergeTimeBonus(PlanetTier resultTier, out float bonusSeconds)
    {
        bonusSeconds = 0f;
        if (timeMode != LevelTimeMode.MergeTimeRush || mergeTimeRewards == null)
            return false;
        for (int i = 0; i < mergeTimeRewards.Count; i++)
        {
            MergeTimeRewardEntry entry = mergeTimeRewards[i];
            if (entry != null && entry.resultTier == resultTier && entry.bonusSeconds > 0f &&
                !float.IsNaN(entry.bonusSeconds) && !float.IsInfinity(entry.bonusSeconds))
            {
                bonusSeconds = entry.bonusSeconds;
                return true;
            }
        }
        return false;
    }

    public bool ValidateSpawnPool(Func<PlanetTier, bool> isUnlocked, out string message)
    {
        var errors = new List<string>();
        ValidateSpawnPool(isUnlocked, errors);
        message = string.Join("; ", errors);
        return errors.Count == 0;
    }

    private void ValidateSpawnPool(Func<PlanetTier, bool> isUnlocked, List<string> errors)
    {
        if (launcherSpawnPool == null || launcherSpawnPool.Count == 0)
        {
            errors.Add("launcherSpawnPool must contain at least one entry");
            return;
        }

        var tiers = new HashSet<PlanetTier>();
        bool hasPositiveEntry = false;
        float totalWeight = 0f;
        for (int i = 0; i < launcherSpawnPool.Count; i++)
        {
            WeightedPlanetTierEntry entry = launcherSpawnPool[i];
            if (entry == null)
            {
                errors.Add($"launcherSpawnPool[{i}] is null");
                continue;
            }
            if (!Enum.IsDefined(typeof(PlanetTier), entry.tier))
                errors.Add($"launcherSpawnPool[{i}] tier {(int)entry.tier} is outside supported PlanetTier values");
            else
            {
                if (!tiers.Add(entry.tier))
                    errors.Add($"launcherSpawnPool contains duplicate tier {entry.tier}");
                if (entry.tier > maximumAllowedMergeTier)
                    errors.Add($"launcherSpawnPool tier {entry.tier} exceeds maximumAllowedMergeTier {maximumAllowedMergeTier}");
                if (isUnlocked != null && !isUnlocked(entry.tier))
                    errors.Add($"launcherSpawnPool tier {entry.tier} is currently locked");
            }
            if (!(entry.weight > 0f) || float.IsNaN(entry.weight) || float.IsInfinity(entry.weight))
                errors.Add($"launcherSpawnPool[{i}] weight must be finite and > 0");
            else
            {
                hasPositiveEntry = true;
                totalWeight += entry.weight;
            }
        }
        if (!hasPositiveEntry)
            errors.Add("launcherSpawnPool has no positive usable weight");
        else if (float.IsNaN(totalWeight) || float.IsInfinity(totalWeight))
            errors.Add("launcherSpawnPool total weight must be finite");
    }

    private void ValidateTimeMode(List<string> errors)
    {
        if (!Enum.IsDefined(typeof(LevelTimeMode), timeMode))
        {
            errors.Add("timeMode is invalid");
            return;
        }
        if (timeMode != LevelTimeMode.MergeTimeRush)
            return;
        if (!(timeRushStartingTime > 0f) || float.IsNaN(timeRushStartingTime) || float.IsInfinity(timeRushStartingTime))
            errors.Add("MergeTimeRush timeRushStartingTime must be finite and > 0");
        if (mergeTimeRewards == null || mergeTimeRewards.Count == 0)
        {
            errors.Add("MergeTimeRush requires a non-empty mergeTimeRewards list");
            return;
        }

        var tiers = new HashSet<PlanetTier>();
        bool meaningful = false;
        for (int i = 0; i < mergeTimeRewards.Count; i++)
        {
            MergeTimeRewardEntry entry = mergeTimeRewards[i];
            if (entry == null)
            {
                errors.Add($"mergeTimeRewards[{i}] is null");
                continue;
            }
            if (!Enum.IsDefined(typeof(PlanetTier), entry.resultTier))
                errors.Add($"mergeTimeRewards[{i}] result tier is unsupported");
            else
            {
                if (!tiers.Add(entry.resultTier))
                    errors.Add($"mergeTimeRewards contains duplicate tier {entry.resultTier}");
                if (entry.resultTier <= PlanetTier.Tier1)
                    errors.Add($"mergeTimeRewards[{i}] must target a merge result of Tier2 or higher");
                if (entry.resultTier > maximumAllowedMergeTier)
                    errors.Add($"mergeTimeRewards tier {entry.resultTier} exceeds maximumAllowedMergeTier");
            }
            if (!(entry.bonusSeconds > 0f) || float.IsNaN(entry.bonusSeconds) || float.IsInfinity(entry.bonusSeconds))
                errors.Add($"mergeTimeRewards[{i}] bonusSeconds must be finite and > 0");
            else
                meaningful = true;
        }
        if (!meaningful)
            errors.Add("MergeTimeRush has no meaningful positive merge time reward");
    }

    private void ValidateMeteorGuarantee(List<string> errors)
    {
        if (!guaranteeMeteorWithinLaunches)
            return;
        if (!meteorsEnabled)
            errors.Add("meteor guarantee requires meteorsEnabled");
        if (meteorGuaranteeLaunchCount < 1)
            errors.Add("meteorGuaranteeLaunchCount must be >= 1");
        bool hasMeteorObjective = objectives != null &&
            objectives.Exists(objective => objective != null && objective.type == LevelObjectiveType.MeteorObjective);
        if (!hasMeteorObjective)
            errors.Add("meteor guarantee should only be enabled for a level containing MeteorObjective");
    }
}
