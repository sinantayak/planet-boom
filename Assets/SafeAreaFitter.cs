using UnityEngine;

// Shrinks a full-screen UI panel to the device's safe area so corner-anchored
// HUD (mission panel, timer) never sits under a notch, punch-hole camera, or
// gesture bar — the usual cause of "misplaced" UI on long Android phones.
//
// Usage: add ONE stretched panel ("SafeAreaRoot") directly under the Canvas,
// put this component on it, and parent all corner/edge HUD inside it.
// Full-bleed elements (backgrounds, dim overlays, full-screen popups) stay
// OUTSIDE it, directly under the Canvas, so they still cover the whole screen.
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Rect appliedSafeArea = new Rect(-1f, -1f, -1f, -1f);

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        Apply();
    }

    // Safe area can change at runtime (rotation, foldables, split-screen).
    void Update()
    {
        if (Screen.safeArea != appliedSafeArea)
        {
            Apply();
        }
    }

    private void Apply()
    {
        appliedSafeArea = Screen.safeArea;

        // Convert the pixel-space safe rect to normalized anchors on the
        // parent (the Canvas). Anchors do all the work: offsets stay zero, so
        // the panel is exactly the safe area at every resolution and scale.
        Vector2 anchorMin = appliedSafeArea.position;
        Vector2 anchorMax = appliedSafeArea.position + appliedSafeArea.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
    }
}
