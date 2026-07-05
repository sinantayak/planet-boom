using UnityEngine;

public enum BubbleColor
{
    Red,
    Blue,
    Green,
    Yellow
}

public static class BubbleColorPalette
{
    public static Color ToUnityColor(BubbleColor color)
    {
        switch (color)
        {
            case BubbleColor.Red: return Color.red;
            case BubbleColor.Blue: return Color.blue;
            case BubbleColor.Green: return Color.green;
            case BubbleColor.Yellow: return Color.yellow;
            default: return Color.white;
        }
    }
}

// Marker + state component so JellyCore and BubbleMerge can identify and react to bubbles.
public class Bubble : MonoBehaviour
{
    private static int nextUniqueId = 0;

    public BubbleColor CurrentColor = BubbleColor.Red;

    // Merge tier, 2048/Suika style: every launched bubble starts at 1 and only
    // ever climbs by merging with a same-color, same-level bubble. BubbleMerge
    // owns the progression (and the max-level BOOM).
    public int Level = 1;
    public int UniqueId { get; private set; }

    void Awake()
    {
        UniqueId = nextUniqueId++;

        // Bubbles settling into a resting cluster (pulled by JellyCore but pinned by
        // neighbors) fall below Unity's sleep velocity threshold and go to sleep.
        // Once both bodies in a contact are asleep, OnCollisionStay2D stops firing for
        // that pair, so same-color bubbles could sit touching forever without merging.
        if (TryGetComponent(out Rigidbody2D rb))
        {
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }
    }

    public void SetColor(BubbleColor color)
    {
        CurrentColor = color;
        if (TryGetComponent(out SpriteRenderer sr))
        {
            sr.color = BubbleColorPalette.ToUnityColor(color);
        }
    }
}
