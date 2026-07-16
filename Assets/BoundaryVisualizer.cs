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

    [Header("Source Art Standard")]
    // The project's standard source-texture size for ring/orbit art going
    // forward: every new Orbit/Ring PNG gets authored at this square
    // resolution. The scale math below never actually reads this value — it
    // already derives world size from sprite.bounds (real imported pixels ÷
    // spritePixelsToUnits), so any texture size works without stretching.
    // This is only a sanity check: it flags in the console if someone drops
    // in art that doesn't match the agreed standard, before it becomes a
    // subtle mis-scale bug.
    [SerializeField] private int standardSourceTexturePixels = 2048;

    [Header("Boundary Alert Color")]
    // Outer ring's tint in normal play — instantly swapped for
    // boundaryAlertColor the moment GameManager reports any planet beyond
    // maxBoundaryRadius, and swapped back the instant none are. A hard switch
    // (not a lerp) so the warning reads unmistakably the moment it happens.
    [SerializeField] private Color boundaryNormalColor = new Color(1f, 1f, 1f, 0.4f);
    [SerializeField] private Color boundaryAlertColor = new Color(1f, 0f, 0f, 0.4f);

    [Header("Skill No-Target Feedback")]
    [SerializeField] [Min(0.05f)] private float noTargetFlashDuration = 0.3f;
    [SerializeField] private Color noTargetFlashColor = new Color(1f, 0.05f, 0.05f, 0.9f);

    [Header("Vortex Ring Spin")]
    // Same rate the swirling planets use (BlackHole.VortexSwirlDegreesPerSecond)
    // times this multiplier, so the rings visibly wind up together with the
    // vortex instead of carrying a second, independently-tuned speed that can
    // drift out of sync.
    [SerializeField] private float vortexRingSpinMultiplier = 1.5f;
    // Seconds to ramp from a standstill to full spin speed once the vortex
    // begins, so the rings visibly pick up speed rather than snapping
    // straight to a constant rate.
    [SerializeField] private float vortexRingSpinRampUpTime = 1.2f;

    [Header("Gravity Singularity Inner Orbit")]
    [SerializeField] private float singularityOrbitRotationSpeed = 75f;
    [Range(0.1f, 1f)]
    [SerializeField] private float singularityOrbitMinimumScale = 0.58f;

    private LineRenderer lineRenderer;
    private BlackHole blackHole;

    // The outer ring's SpriteRenderer, cached once so the per-frame alert
    // color check doesn't pay for a GetComponentInChildren every LateUpdate.
    private SpriteRenderer visualBoundarySpriteRenderer;

    // World radius of each art piece at localScale 1, derived from its
    // sprite's own bounds (pixels ÷ pixels-per-unit). Captured once in Awake.
    private float visualSpriteBaseRadius = 1f;
    private float innerOrbitBaseRadius = 1f;

    // Last-built circle, so the points are only recomputed when something moved.
    private Vector3 builtCenter;
    private float builtRadius = -1f;

    // How long the current vortex spin-up has been running; also doubles as
    // "was the vortex active last frame" (>0 once spinning, reset to 0 the
    // instant it stops) so LateUpdate can detect the active->inactive edge.
    private float vortexSpinRampElapsed = 0f;
    private Quaternion visualBoundaryInitialRotation;
    private Quaternion innerOrbitInitialRotation;
    private float singularityOrbitRotation;
    private float noTargetFlashEndsAt;

    public void PlayNoSkillTargetFeedback()
    {
        noTargetFlashEndsAt = Time.unscaledTime + noTargetFlashDuration;
    }

    void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();
        ConfigureLineRenderer();
        blackHole = FindFirstObjectByType<BlackHole>();
        visualSpriteBaseRadius = ResolveSpriteBaseRadius(visualBoundaryTransform, "visualBoundaryTransform");
        innerOrbitBaseRadius = ResolveSpriteBaseRadius(innerOrbitTransform, "innerOrbitTransform");
        visualBoundarySpriteRenderer = visualBoundaryTransform != null
            ? visualBoundaryTransform.GetComponentInChildren<SpriteRenderer>()
            : null;
        visualBoundaryInitialRotation = visualBoundaryTransform != null
            ? visualBoundaryTransform.localRotation
            : Quaternion.identity;
        innerOrbitInitialRotation = innerOrbitTransform != null
            ? innerOrbitTransform.localRotation
            : Quaternion.identity;
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
            WarnIfNotStandardSize(sr, fieldName);
            return Mathf.Max(0.0001f, sr.sprite.bounds.extents.x);
        }

        Debug.LogWarning($"BoundaryVisualizer: {fieldName} has no SpriteRenderer/sprite — " +
                         "assuming 1 world-unit radius at scale 1; use its scale multiplier to fine-tune.");
        return 1f;
    }

    // Sanity check only, per standardSourceTexturePixels above — the scale
    // math itself is resolution-independent, so a mismatch here won't break
    // anything on its own, but it's the earliest signal that a dropped-in
    // asset doesn't match the agreed 2048x2048 template.
    private void WarnIfNotStandardSize(SpriteRenderer sr, string fieldName)
    {
        Texture texture = sr.sprite.texture;
        if (texture == null)
            return;

        if (texture.width != standardSourceTexturePixels || texture.height != standardSourceTexturePixels)
        {
            Debug.LogWarning($"BoundaryVisualizer: {fieldName}'s source texture '{texture.name}' is " +
                             $"{texture.width}x{texture.height}, not the standard " +
                             $"{standardSourceTexturePixels}x{standardSourceTexturePixels} — still scales " +
                             "correctly, but re-export to the standard size to keep future ring/orbit art consistent.");
        }
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

        UpdateVortexRingSpin();
        UpdateBoundaryAlertColor();

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
        float singularityIntensity = blackHole != null ? blackHole.GravitySingularityIntensity : 0f;
        float singularityScale = Mathf.Lerp(1f, singularityOrbitMinimumScale, singularityIntensity);
        SyncRingArt(innerOrbitTransform, innerOrbitBaseRadius,
            innerOrbitScaleMultiplier * singularityScale, center, radius);
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

    // The Level Complete cinematic (BlackHole.BeginVortex/EndVortex) spins
    // the ring/orbit art along with the planets it's swallowing — this is
    // purely a rotation layered on top of SyncRingArt's position/scale, so
    // the two never fight over the same field.
    private void UpdateVortexRingSpin()
    {
        bool vortexActive = blackHole != null && blackHole.IsVortexActive;

        if (!vortexActive)
        {
            if (vortexSpinRampElapsed > 0f)
            {
                // Cinematic just ended: snap back to the authored upright
                // orientation rather than leaving the art wherever the spin
                // happened to stop, so the next level starts clean.
                vortexSpinRampElapsed = 0f;
                RestoreRingRotation(visualBoundaryTransform, visualBoundaryInitialRotation);
                RestoreRingRotation(innerOrbitTransform, innerOrbitInitialRotation);
            }

            UpdateSingularityOrbitVisual();
            return;
        }

        singularityOrbitRotation = 0f;
        if (vortexSpinRampElapsed <= 0f)
        {
            RestoreRingRotation(visualBoundaryTransform, visualBoundaryInitialRotation);
            RestoreRingRotation(innerOrbitTransform, innerOrbitInitialRotation);
        }

        vortexSpinRampElapsed += Time.deltaTime;
        float rampT = vortexRingSpinRampUpTime > 0f
            ? Mathf.Clamp01(vortexSpinRampElapsed / vortexRingSpinRampUpTime)
            : 1f;

        float spinSpeed = blackHole.VortexSwirlDegreesPerSecond * vortexRingSpinMultiplier * rampT;
        float delta = spinSpeed * Time.deltaTime;

        RotateRing(visualBoundaryTransform, delta);
        RotateRing(innerOrbitTransform, delta);
    }

    private void UpdateSingularityOrbitVisual()
    {
        if (innerOrbitTransform == null)
            return;

        float intensity = blackHole != null ? blackHole.GravitySingularityIntensity : 0f;
        if (intensity <= 0f)
        {
            if (!Mathf.Approximately(singularityOrbitRotation, 0f))
            {
                singularityOrbitRotation = 0f;
                innerOrbitTransform.localRotation = innerOrbitInitialRotation;
            }
            return;
        }

        singularityOrbitRotation += singularityOrbitRotationSpeed * Time.deltaTime;
        float displayedRotation = singularityOrbitRotation * intensity;
        innerOrbitTransform.localRotation = innerOrbitInitialRotation *
            Quaternion.Euler(0f, 0f, displayedRotation);
    }

    // Hard-switches the outer ring's tint the instant GameManager reports any
    // planet beyond maxBoundaryRadius — a snap rather than a lerp, so the
    // warning is unmistakable at the moment the line is crossed.
    private void UpdateBoundaryAlertColor()
    {
        if (visualBoundarySpriteRenderer == null)
            return;

        GameManager manager = GameManager.Instance;
        bool exceeded = manager != null && manager.IsAnyPlanetBeyondBoundary;
        if (Time.unscaledTime < noTargetFlashEndsAt)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 45f);
            visualBoundarySpriteRenderer.color = Color.Lerp(boundaryAlertColor, noTargetFlashColor, pulse);
        }
        else
        {
            visualBoundarySpriteRenderer.color = exceeded ? boundaryAlertColor : boundaryNormalColor;
        }
    }

    private static void RotateRing(Transform art, float degrees)
    {
        if (art != null)
        {
            art.Rotate(0f, 0f, degrees);
        }
    }

    private static void RestoreRingRotation(Transform art, Quaternion rotation)
    {
        if (art != null)
        {
            art.localRotation = rotation;
        }
    }

    void OnDisable()
    {
        noTargetFlashEndsAt = 0f;
        vortexSpinRampElapsed = 0f;
        singularityOrbitRotation = 0f;
        RestoreRingRotation(visualBoundaryTransform, visualBoundaryInitialRotation);
        RestoreRingRotation(innerOrbitTransform, innerOrbitInitialRotation);
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
        if (manager != null && manager.BlackHoleCenter != null)
        {
            center = manager.BlackHoleCenter.position;
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
