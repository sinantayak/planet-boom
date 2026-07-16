using System;
using UnityEngine;

public enum LevelObjectiveType
{
    ReachTier,
    MergeCount,
    ComboTarget,
    MeteorObjective,
    Survival
}

[Serializable]
public sealed class LevelObjectiveDefinition
{
    public LevelObjectiveType type = LevelObjectiveType.ReachTier;
    public PlanetTier targetTier = PlanetTier.Tier5;
    [Min(1f)] public float targetProgress = 1f;
    public bool required = true;
}

public readonly struct LevelObjectiveProgress
{
    public readonly int Index;
    public readonly LevelObjectiveType Type;
    public readonly PlanetTier TargetTier;
    public readonly float CurrentProgress;
    public readonly float TargetProgress;
    public readonly bool IsCompleted;
    public readonly bool IsRequired;

    public LevelObjectiveProgress(int index, LevelObjectiveType type,
        PlanetTier targetTier, float currentProgress, float targetProgress,
        bool isCompleted, bool isRequired)
    {
        Index = index;
        Type = type;
        TargetTier = targetTier;
        CurrentProgress = currentProgress;
        TargetProgress = targetProgress;
        IsCompleted = isCompleted;
        IsRequired = isRequired;
    }
}

// Runtime state is deliberately separate from the serializable definition:
// level configuration stays data-only while every fresh run gets clean state.
public abstract class LevelObjective
{
    public int Index { get; }
    public LevelObjectiveType Type { get; }
    public PlanetTier TargetTier { get; }
    public float CurrentProgress { get; private set; }
    public float TargetProgress { get; }
    public bool IsCompleted => CurrentProgress >= TargetProgress;
    public bool IsRequired { get; }

    protected LevelObjective(int index, LevelObjectiveDefinition definition)
    {
        Index = index;
        Type = definition.type;
        TargetTier = definition.targetTier;
        TargetProgress = Mathf.Max(1f, definition.targetProgress);
        IsRequired = definition.required;
    }

    public LevelObjectiveProgress Snapshot => new LevelObjectiveProgress(
        Index, Type, TargetTier, CurrentProgress, TargetProgress,
        IsCompleted, IsRequired);

    public abstract bool Apply(LevelObjectiveType signalType, float amount,
        int combo, PlanetTier createdTier);

    public bool ForceComplete() => SetProgress(TargetProgress);

    protected bool SetProgress(float value)
    {
        float next = Mathf.Clamp(value, 0f, TargetProgress);
        if (Mathf.Approximately(next, CurrentProgress))
            return false;
        CurrentProgress = next;
        return true;
    }

    protected bool AddProgress(float amount)
    {
        return amount > 0f && SetProgress(CurrentProgress + amount);
    }

    public static LevelObjective Create(int index, LevelObjectiveDefinition definition)
    {
        switch (definition.type)
        {
            case LevelObjectiveType.ReachTier:
                return new ReachTierLevelObjective(index, definition);
            case LevelObjectiveType.ComboTarget:
                return new ComboTargetLevelObjective(index, definition);
            default:
                return new CounterLevelObjective(index, definition);
        }
    }
}

internal sealed class ReachTierLevelObjective : LevelObjective
{
    public ReachTierLevelObjective(int index, LevelObjectiveDefinition definition)
        : base(index, definition) { }

    public override bool Apply(LevelObjectiveType signalType, float amount,
        int combo, PlanetTier createdTier)
    {
        return signalType == LevelObjectiveType.ReachTier && createdTier == TargetTier
            && AddProgress(1f);
    }
}

internal sealed class ComboTargetLevelObjective : LevelObjective
{
    public ComboTargetLevelObjective(int index, LevelObjectiveDefinition definition)
        : base(index, definition) { }

    public override bool Apply(LevelObjectiveType signalType, float amount,
        int combo, PlanetTier createdTier)
    {
        return signalType == LevelObjectiveType.ComboTarget && combo > 0
            && SetProgress(Mathf.Max(CurrentProgress, combo));
    }
}

internal sealed class CounterLevelObjective : LevelObjective
{
    public CounterLevelObjective(int index, LevelObjectiveDefinition definition)
        : base(index, definition) { }

    public override bool Apply(LevelObjectiveType signalType, float amount,
        int combo, PlanetTier createdTier)
    {
        return signalType == Type && AddProgress(amount);
    }
}
