using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Planet Boom/Level Catalog", fileName = "LevelCatalog")]
public sealed class LevelConfigurationCatalog : ScriptableObject
{
    public List<LevelConfiguration> levels = new List<LevelConfiguration>();
    public LevelConfiguration FindByNumber(int number) => levels?.Find(item => item != null && item.levelNumber == number);
    public LevelConfiguration FindById(string id) => levels?.Find(item => item != null && string.Equals(item.stableId, id, StringComparison.Ordinal));
}
