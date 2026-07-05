using UnityEngine;

// Draws the lose-boundary as a smooth LineRenderer circle around the core.
// Center and radius are pulled from the GameManager every frame, so the drawn
// line is always exactly the circle the lose check enforces — tweaking
// maxBoundaryRadius in the Inspector moves the visual automatically.
[RequireComponent(typeof(LineRenderer))]
public class BoundaryVisualizer : MonoBehaviour
{
    // Master switch for the debug line. The boundary is now drawn with custom
    // UI sprites during real gameplay, so this stays off by default and the
    // LineRenderer only serves as a dev/tuning aid.
    [SerializeField] private bool showGizmoLine = false;

    [SerializeField] private int segments = 96;
    [SerializeField] private float lineWidth = 0.06f;
    [SerializeField] private Color lineColor = new Color(1f, 0.35f, 0.35f, 0.8f);

    [Header("Custom Art Sync")]
    // The hand-made ring art (Visual_Boundary_Ring). The physics radius stays
    // the single source of truth; this transform is repositioned and rescaled
    // every frame so its visual radius mirrors maxBoundaryRadius exactly —
    // runtime radius tweaks and moving cores included.
    [SerializeField] private Transform visualBoundaryTransform;

    // One-time fine-tune for 1:1 alignment, multiplied into the computed
    // scale. Needed when the painted ring doesn't reach the exact edge of its
    // sprite bounds (padding, glow, soft edges): nudge until the art sits on
    // the gizmo line, then leave it.
    [SerializeField] private float spriteScaleMultiplier = 1f;

    // Inner orbit-lines art: synced with the same physics-leads logic as the
    // main ring, with its own calibration knob. Values below 1 draw it inside
    // the boundary at a fixed proportion of maxBoundaryRadius, so both rings
    // grow and shrink together on any device or runtime radius tweak.
    [SerializeField] private Transform innerOrbitTransform;
    [SerializeField] private float innerOrbitScaleMultiplier = 1f;

    // Used only when no GameManager is available (e.g. a test scene).
    [Header("Fallback (no GameManager)")]
    [SerializeField] private Transform fallbackCenter;
    [SerializeField] private float fallbackRadius = 6f;

    private LineRenderer lineRenderer;

    // World radius of each art piece at localScale 1, derived from its
    // sprite's own bounds (pixels ÷ pixels-per-unit). Captured once in Awake.
    private float visualSpriteBaseRadius = 1f;
    private float innerOrbitBaseRadius = 1f;

    // Last-built circle, so the points are only recomputed when something moved.
    private Vector3 builtCenter;
    private float builtRadius = -1f;

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
        visualSpriteBaseRadius = ResolveSpriteBaseRadius(visualBoundaryTransform, "visualBoundaryTransform");
        innerOrbitBaseRadius = ResolveSpriteBaseRadius(innerOrbitTransform, "innerOrbitTransform");
    }

    // Reads an art piece's intrinsic size so the scale math can be exact: a
    // sprite's local bounds are its pixel size divided by pixels-per-unit,
    // i.e. its world size at localScale 1. Falls back to 1 world unit if there
    // is no SpriteRenderer to inspect — the per-ring multiplier covers the rest.
    private float ResolveSpriteBaseRadius(Transform art, string fieldName)
    {
        if (art == null)
            return 1f;

        SpriteRenderer sr = art.GetComponentInChildren<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            return Mathf.Max(0.0001f, sr.sprite.bounds.extents.x);
        }

        Debug.LogWarning($"BoundaryVisualizer: {fieldName} has no SpriteRenderer/sprite — " +
                         "assuming 1 world-unit radius at scale 1; use its scale multiplier to fine-tune.");
        return 1f;
    }

    void LateUpdate()
    {
        bool hasBoundary = TryGetBoundary(out Vector3 center, out float radius);

        // The art follows the physics boundary whether or not the debug line
        // is showing — the sprite IS the gameplay visual.
        if (hasBoundary)
        {
            SyncVisualBoundary(center, radius);
        }

        if (!showGizmoLine || !hasBoundary)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;

        // Re-applied every visible frame so lineWidth/lineColor edits in the
        // Inspector take effect live in Play mode — the whole point of this
        // debug line is tuning it against the custom UI sprite.
        ApplyLineStyle();

        if (center == builtCenter && Mathf.Approximately(radius, builtRadius))
            return;

        RebuildCircle(center, radius);
    }

    // Physics leads, art follows: both ring sprites are centered on the core
    // and scaled off the same maxBoundaryRadius every frame, so Inspector
    // tweaks mid-play propagate instantly and the rings stay proportional.
    private void SyncVisualBoundary(Vector3 center, float radius)
    {
        SyncRingArt(visualBoundaryTransform, visualSpriteBaseRadius, spriteScaleMultiplier, center, radius);
        SyncRingArt(innerOrbitTransform, innerOrbitBaseRadius, innerOrbitScaleMultiplier, center, radius);
    }

    private void SyncRingArt(Transform art, float baseRadius, float multiplier, Vector3 center, float radius)
    {
        if (art == null)
            return;

        // Keep the art's own Z so its sorting/depth setup is respected.
        art.position = new Vector3(center.x, center.y, art.position.z);

        float scale = radius / baseRadius * multiplier;
        art.localScale = new Vector3(scale, scale, 1f);
    }

    private void ApplyLineStyle()
    {
        lineRenderer.startWidth = lineWidth;
        lineRenderer.endWidth = lineWidth;
        lineRenderer.startColor = lineColor;
        lineRenderer.endColor = lineColor;
    }

    private void ConfigureLineRenderer()
    {
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = Mathf.Max(8, segments);
        ApplyLineStyle();

        // A LineRenderer with no material renders magenta; give it a plain
        // vertex-color material as a clean default. A material assigned in the
        // Inspector (styling pass later) always wins over this.
        if (lineRenderer.sharedMaterial == null)
        {
            lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
        }
    }

    private bool TryGetBoundary(out Vector3 center, out float radius)
    {
        GameManager manager = GameManager.Instance;
        if (manager != null && manager.CoreCenter != null)
        {
            center = manager.CoreCenter.position;
            radius = manager.MaxBoundaryRadius;
            return true;
        }

        if (fallbackCenter != null)
        {
            center = fallbackCenter.position;
            radius = fallbackRadius;
            return true;
        }

        center = Vector3.zero;
        radius = 0f;
        return false;
    }

    private void RebuildCircle(Vector3 center, float radius)
    {
        int count = Mathf.Max(8, segments);
        if (lineRenderer.positionCount != count)
        {
            lineRenderer.positionCount = count;
        }

        // loop = true closes the last segment back to the first point, so the
        // angle step divides the full circle by count without repeating 0/2π.
        float angleStep = 2f * Mathf.PI / count;
        for (int i = 0; i < count; i++)
        {
            float angle = i * angleStep;
            lineRenderer.SetPosition(i, new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                center.y + Mathf.Sin(angle) * radius,
                center.z));
        }

        builtCenter = center;
        builtRadius = radius;
    }
}
