// The four collectible skills a high-combo merge can drop. Extend this
// ladder (and every Sprite[]/lookup array indexed by it, e.g.
// SkillFlightManager.skillIcons) together when a new skill is designed —
// they all follow the same "array indexed by (int)enum" convention as
// PlanetTier/Planet.planetSprites.
public enum SkillType
{
    GravitySingularity, // Kozmik Cekim
    MeteorStrike,        // Meteor Imha Lazeri
    TimeWarp,             // Zaman Bukucu
    CosmicMimic           // Joker Gezegen
}
