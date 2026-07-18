using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class LevelMapRewardIconEntry
{
    public UnlockContentType contentType;
    public string stableContentId;
    public Sprite icon;
}

[CreateAssetMenu(menuName = "Planet Boom/Level Map/Reward Icon Library", fileName = "LevelMapRewardIcons")]
public sealed class LevelMapRewardIconLibrary : ScriptableObject
{
    [SerializeField] private List<LevelMapRewardIconEntry> entries = new();

    public Sprite Resolve(LevelUnlockReward reward)
    {
        if (reward == null || entries == null) return null;
        LevelMapRewardIconEntry match = entries.Find(item => item != null &&
            item.contentType == reward.contentType &&
            string.Equals(item.stableContentId, reward.stableContentId, StringComparison.Ordinal));
        return match != null ? match.icon : null;
    }
}
