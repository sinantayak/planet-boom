using UnityEngine;

public enum PlanetColor
{
    Red,
    Blue,
    Green,
    Yellow
}

public static class PlanetColorPalette
{
    public static Color ToUnityColor(PlanetColor color)
    {
        switch (color)
        {
            case PlanetColor.Red: return Color.red;
            case PlanetColor.Blue: return Color.blue;
            case PlanetColor.Green: return Color.green;
            case PlanetColor.Yellow: return Color.yellow;
            default: return Color.white;
        }
    }
}

// Marker + state component so BlackHole and PlanetMerge can identify and react to planets.
public class Planet : MonoBehaviour
{
    private static int nextUniqueId = 0;

    public PlanetColor CurrentColor = PlanetColor.Red;

    // Merge tier, 2048/Suika style: every launched planet starts at 1 and only
    // ever climbs by merging with a same-color, same-level planet. PlanetMerge
    // owns the progression (and the max-level BOOM).
    public int Level = 1;
    public int UniqueId { get; private set; }

    void Awake()
    {
        UniqueId = nextUniqueId++;

        // Planets settling into a resting cluster (pulled by BlackHole but pinned by
        // neighbors) fall below Unity's sleep velocity threshold and go to sleep.
        // Once both bodies in a contact are asleep, OnCollisionStay2D stops firing for
        // that pair, so same-color planets could sit touching forever without merging.
        if (TryGetComponent(out Rigidbody2D rb))
        {
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }
    }

    public void SetColor(PlanetColor color)
    {
        CurrentColor = color;
        if (TryGetComponent(out SpriteRenderer sr))
        {
            sr.color = PlanetColorPalette.ToUnityColor(color);
        }
    }
}
