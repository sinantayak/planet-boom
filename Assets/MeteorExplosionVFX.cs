using UnityEngine;
using UnityEngine.Rendering;

// Procedural, prefab-free particle bursts — no authored VFX asset required.
// Two entry points share the same GameObject-lifecycle/material-blend
// plumbing but configure very differently:
//   Spawn:           big warm-palette BOOM/Big Pop burst
//                     (PlanetMerge.TriggerBoom, Meteorite.TriggerBigPop).
//   SpawnMergeBurst:  small, subtle sparkle for an everyday tier-up merge,
//                     tinted to the merging body's own SpriteRenderer color
//                     instead of a fixed palette.
public static class MeteorExplosionVFX
{
    private const float CleanupBuffer = 0.2f;

    private const float ExplosionMaxParticleLifetime = 0.55f;
    private const int ExplosionBurstCount = 26;

    private const float MergeMaxParticleLifetime = 0.4f;
    private const int MergeBurstCount = 14;

    // Merge sparkle particle sizing/speed in ABSOLUTE world units, NOT scaled
    // by the body's tiny localScale (planets run at ~0.15 localScale — the old
    // "multiply size by scale" math collapsed a 0.12 particle to 0.018 units,
    // effectively sub-pixel and invisible, which is why planet-merge sparkles
    // never showed). These are the real on-screen sizes regardless of tier.
    private const float MergeParticleMinSize = 0.08f;
    private const float MergeParticleMaxSize = 0.18f;
    private const float MergeParticleMinSpeed = 1.2f;
    private const float MergeParticleMaxSpeed = 2.6f;

    // Nudge the sparkle GameObject this far toward the camera (2D default:
    // camera looks down +Z from negative Z, so lower Z = nearer). Belt-and-
    // braces on top of the high sorting order below, since unlike the BOOM
    // burst the merging planets stay on screen and could otherwise occlude a
    // same-Z, same-layer effect.
    private const float MergeCameraZOffset = 1f;

    // Chunky, high-saturation arcade palette for the BOOM burst — sampled
    // randomly per particle (see BuildExplosionPaletteGradient) so one burst
    // mixes hot colors instead of a single flat tint.
    private static readonly Color HotWhite = new Color(1f, 0.95f, 0.75f);
    private static readonly Color ArcadeYellow = new Color(1f, 0.85f, 0.15f);
    private static readonly Color ArcadeOrange = new Color(1f, 0.45f, 0.08f);
    private static readonly Color ArcadeRed = new Color(0.95f, 0.15f, 0.15f);

    // Fallback for SpawnMergeBurst when the sampled color is white/near-white
    // (every planet merge) — a vibrant cyan reads clearly against both dark
    // space backgrounds and the warm BOOM palette, so it never gets confused
    // with the explosion burst.
    private static readonly Color MergeFallbackColor = new Color(0.25f, 0.85f, 1f);

    private static Material explosionMaterial;
    private static Material mergeMaterial;

    // scale lets callers size the burst to the thing that just exploded (a
    // bigger planet/meteorite reads better with a bigger pop); 1f is a solid
    // default for anything that doesn't care.
    public static void Spawn(Vector3 position, float scale = 1f)
    {
        GameObject vfxObject = CreateVfxObject("MeteorExplosionVFX", position);

        var ps = vfxObject.AddComponent<ParticleSystem>();
        ConfigureExplosionParticleSystem(ps, scale);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetExplosionMaterial();
        // Draw above planets/meteorites/background so the pop always reads.
        renderer.sortingOrder = 100;

        vfxObject.SetActive(true);
        ps.Play();

        Object.Destroy(vfxObject, ExplosionMaxParticleLifetime + CleanupBuffer);
    }

    // Subtle per-merge feedback: far fewer/shorter-lived particles than the
    // BOOM burst, tinted to whatever color the caller read off the merging
    // body's own SpriteRenderer — so a molten meteorite sparks orange-red.
    // Planet sprites are always rendered pure white (Planet.SetTier never
    // tints the pre-rendered art), so a raw white/near-white sample is
    // swapped for a vibrant fallback instead.
    //
    // bodyWorldRadius is the merging body's actual world-space radius (its
    // collider radius × lossyScale). The burst emits from a RING at that
    // radius so the sparkles appear around the planet's PERIMETER, not buried
    // dead-center behind the (large, still-on-screen) sprite — this plus the
    // absolute particle sizes and the toward-camera Z nudge are what make the
    // effect actually visible during a normal merge.
    public static void SpawnMergeBurst(Vector3 position, Color color, float bodyWorldRadius)
    {
        Color visibleColor = ResolveVisibleMergeColor(color);
        Vector3 spawnPosition = new Vector3(position.x, position.y, position.z - MergeCameraZOffset);
        GameObject vfxObject = CreateVfxObject("MergeSparkleVFX", spawnPosition);

        var ps = vfxObject.AddComponent<ParticleSystem>();
        ConfigureMergeParticleSystem(ps, visibleColor, bodyWorldRadius);

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.material = GetMergeMaterial();
        // Well above the planets/meteorites (sorting order 0 on the Default
        // layer) so the sparkles always draw in front of them.
        renderer.sortingOrder = 95;

        vfxObject.SetActive(true);
        ps.Play();

        Object.Destroy(vfxObject, MergeMaxParticleLifetime + CleanupBuffer);
    }

    // Inactive until fully configured: AddComponent<ParticleSystem> runs
    // Awake/OnEnable immediately on an active object, and the component's
    // default main.playOnAwake is true — so without this, the system would
    // start emitting with default (5s duration, default shape/color)
    // settings for a frame before the caller's Configure* call ever
    // overwrites them. Deferring activation guarantees every module is
    // fully assigned before the simulation ever ticks once.
    private static GameObject CreateVfxObject(string name, Vector3 position)
    {
        var vfxObject = new GameObject(name);
        vfxObject.SetActive(false);
        vfxObject.transform.position = position;
        return vfxObject;
    }

    private static void ConfigureExplosionParticleSystem(ParticleSystem ps, float scale)
    {
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = ExplosionMaxParticleLifetime;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, ExplosionMaxParticleLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f * scale, 6f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f * scale, 0.28f * scale);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        main.startColor = new ParticleSystem.MinMaxGradient(BuildExplosionPaletteGradient());
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        // Everything fires in one instant burst — a punchy pop rather than a
        // sputtering fountain.
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)ExplosionBurstCount) });

        // Spawn from a tight point and let startSpeed fling particles
        // outward radially — a clean radial burst.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.05f;

        // Alpha-only fade so the random start palette colors aren't tinted
        // away — particles simply melt out smoothly near the end of life.
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = BuildFadeGradient(1f, 0.55f);

        // Chunks shrink slightly as they fly out and fade — reinforces the
        // "burning debris cooling down" read.
        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));

        // Lazy tumble on each square chunk — cheap way to sell "retro pixel
        // debris" without any texture or mesh work.
        var rotationOverLifetime = ps.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-180f, 180f);

        // Outward drag so the burst decelerates instead of flying at a
        // constant speed forever — reads as an actual explosion losing energy.
        var limitVelocity = ps.limitVelocityOverLifetime;
        limitVelocity.enabled = true;
        limitVelocity.dampen = 0.35f;
        limitVelocity.limit = new ParticleSystem.MinMaxCurve(0.6f * scale);
    }

    // Same shape language as the explosion burst but quieter: fewer,
    // shorter-lived particles. Sizes and speeds are ABSOLUTE world-space
    // values (see the Merge* consts) rather than being multiplied by the
    // body's ~0.15 localScale — that multiplication is exactly what used to
    // shrink these to invisible sub-pixel dust. Only the EMISSION RING scales
    // with the body, so the sparkles ring the perimeter at any tier.
    private static void ConfigureMergeParticleSystem(ParticleSystem ps, Color color, float bodyWorldRadius)
    {
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = MergeMaxParticleLifetime;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, MergeMaxParticleLifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(MergeParticleMinSpeed, MergeParticleMaxSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(MergeParticleMinSize, MergeParticleMaxSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f * Mathf.Deg2Rad);
        // A touch of light/dark variance around the sampled color instead of
        // one flat tint, same "random point on a small gradient" trick as
        // the explosion palette.
        main.startColor = new ParticleSystem.MinMaxGradient(
            LightenTowardsWhite(color, 0.35f), DarkenTowardsBlack(color, 0.2f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)MergeBurstCount) });

        // Emit from a RING at (a little inside) the body's perimeter so the
        // sparkles appear AROUND the planet rather than at its covered center;
        // radiusThickness 0 keeps them on the ring edge, and the Circle
        // shape's outward normal flings them past the perimeter. Floored so a
        // tiny Tier1 (or a caller that couldn't compute a radius) still gets a
        // visible ring instead of a degenerate point.
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = Mathf.Max(bodyWorldRadius * 0.85f, 0.12f);
        shape.radiusThickness = 0f;

        // Starts semi-transparent (it's a subtle accent, not a flash) and
        // fades fully out.
        var colorOverLifetime = ps.colorOverLifetime;
        colorOverLifetime.enabled = true;
        colorOverLifetime.color = BuildFadeGradient(0.9f, 0.5f);

        var sizeOverLifetime = ps.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
    }

    // Random-per-particle sampling across a hot white → yellow → orange → red
    // spread; MinMaxGradient(Gradient) picks a random point along it for each
    // particle's start color, giving one burst a mix of arcade-bright hues
    // instead of a single flat tint.
    private static Gradient BuildExplosionPaletteGradient()
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(HotWhite, 0f),
                new GradientColorKey(ArcadeYellow, 0.35f),
                new GradientColorKey(ArcadeOrange, 0.7f),
                new GradientColorKey(ArcadeRed, 1f)
            },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return gradient;
    }

    // Shared alpha-fade-out curve (white RGB — colorOverLifetime multiplies
    // onto startColor, so it only ever touches alpha here) used by both
    // burst types; startAlpha/midAlpha let each caller pick how visible the
    // burst starts before it melts to 0 at the end of its life.
    private static Gradient BuildFadeGradient(float startAlpha, float midAlpha)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(startAlpha, 0f),
                new GradientAlphaKey(midAlpha, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        return gradient;
    }

    // White (Planet.SetTier's permanent tint) or anything close to it/grey
    // gets swapped for MergeFallbackColor; a real, saturated sample (a
    // meteorite's molten color) passes through untouched. Checked via how
    // far apart the color's channels are (low spread = grey/white) combined
    // with a high floor (dark greys still show up fine against space) rather
    // than an exact Color.white equality check, so near-white counts too.
    private static Color ResolveVisibleMergeColor(Color color)
    {
        float maxChannel = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        float minChannel = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        bool isNearWhiteOrGrey = minChannel > 0.85f && (maxChannel - minChannel) < 0.05f;
        return isNearWhiteOrGrey ? MergeFallbackColor : color;
    }

    private static Color LightenTowardsWhite(Color color, float amount)
    {
        Color opaque = new Color(color.r, color.g, color.b, 1f);
        return Color.Lerp(opaque, Color.white, amount);
    }

    private static Color DarkenTowardsBlack(Color color, float amount)
    {
        Color opaque = new Color(color.r, color.g, color.b, 1f);
        return Color.Lerp(opaque, Color.black, amount);
    }

    private static Material GetExplosionMaterial()
    {
        if (explosionMaterial != null)
            return explosionMaterial;

        explosionMaterial = CreateParticleMaterial("MeteorExplosionVFX (generated)");

        // Force the material's own base tint to a warm orange on every
        // property name any of the candidate shaders might expose. This is
        // what fixes a blue-spark bug seen previously: whatever produced the
        // blue (a shader ignoring per-particle vertex color, or a fallback
        // texture/material with a cool default tint), a near-zero-blue base
        // color makes a blue result impossible regardless of how it
        // combines with the per-particle gradient — worst case is flat warm
        // orange, never blue.
        SetBaseTint(explosionMaterial, new Color(1f, 0.55f, 0.12f));
        return explosionMaterial;
    }

    // Deliberately left at a neutral WHITE base tint (rather than the forced
    // warm orange above) — this material's whole point is to let the
    // dynamic per-particle color (sampled from the merging body) show
    // through untouched instead of being overridden by a fixed palette.
    private static Material GetMergeMaterial()
    {
        if (mergeMaterial != null)
            return mergeMaterial;

        mergeMaterial = CreateParticleMaterial("MergeSparkleVFX (generated)");
        SetBaseTint(mergeMaterial, Color.white);
        return mergeMaterial;
    }

    // Additive blending is what makes a small handful of untextured quads
    // read as a bright, punchy flash instead of flat colored squares. Tries
    // pipeline-appropriate shaders first (this project has URP installed)
    // and falls back down to whatever's actually available so the effect
    // never turns into a pink "missing shader" material.
    private static Material CreateParticleMaterial(string materialName)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
            Shader.Find("Particles/Standard Unlit") ??
            Shader.Find("Legacy Shaders/Particles/Additive") ??
            Shader.Find("Sprites/Default");

        var material = new Material(shader) { name = materialName };

        // Force additive transparency via the _SrcBlend/_DstBlend/_ZWrite
        // properties rather than the _Surface/_Blend enum toggles: on both
        // URP's hand-written particle shader and the legacy Particles
        // shaders, the actual `Blend [_SrcBlend] [_DstBlend]` ShaderLab
        // command reads these properties directly, so a plain SetFloat takes
        // effect immediately — no shader keyword to also flip in sync.
        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }
        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.One);
        }
        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }
        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f); // best-effort: URP Transparent
        }
        // Multiply mode (rather than Additive/Overlay/etc.) so the
        // per-particle start/over-lifetime gradient modulates on top of the
        // base tint instead of being ignored by it.
        if (material.HasProperty("_ColorMode"))
        {
            material.SetFloat("_ColorMode", 0f);
        }
        material.renderQueue = (int)RenderQueue.Transparent;

        return material;
    }

    private static void SetBaseTint(Material material, Color tint)
    {
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", tint);
        }
        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", tint);
        }
        if (material.HasProperty("_TintColor"))
        {
            material.SetColor("_TintColor", tint);
        }
    }
}
