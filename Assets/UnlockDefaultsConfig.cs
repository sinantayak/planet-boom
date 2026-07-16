using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Planet Boom/Unlock Defaults", fileName = "UnlockDefaults")]
public sealed class UnlockDefaultsConfig : ScriptableObject
{
    [Tooltip("Canonical IDs such as skill:MeteorStrike or planet:Tier1.")]
    public List<string> defaultUnlockedIds = new List<string>();
}
