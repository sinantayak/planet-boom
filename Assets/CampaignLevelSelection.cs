// Carries a map selection across the LevelMap -> GameScene scene transition.
// This is transient run intent, not progression or save data.
public static class CampaignLevelSelection
{
    public static LevelConfiguration Selected { get; private set; }

    public static void Select(LevelConfiguration configuration) => Selected = configuration;

    public static LevelConfiguration Consume()
    {
        LevelConfiguration result = Selected;
        Selected = null;
        return result;
    }
}
