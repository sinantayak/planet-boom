using UnityEngine;
using UnityEngine.Serialization;

// Generic merge tier, Suika/2048 style. The game currently plays within
// Tier1..Tier4 (see PlanetMerge.maxTier), but the type — and the sprite
// array on Planet — already supports all 8 so the ladder can be extended
// from the Inspector without touching code.
public enum PlanetTier
{
    Tier1,
    Tier2,
    Tier3,
    Tier4,
    Tier5,
    Tier6,
    Tier7,
    Tier8
}

// Per-tier accent colors matching each planet's actual sprite art (lava,
// earth, sand, ...) — consumed by PlanetMerge's merge-sparkle VFX (which
// can't read a real color off the sprite itself, see below) and available
// for HUD accents. Planet sprites themselves are pre-rendered and must
// NEVER be tinted with these — Planet.SetTier forces sr.color to pure white
// for exactly that reason; these colors exist for effects/UI that sit
// alongside the sprite, not on top of it.
public static class PlanetTierPalette
{
    private static readonly Color[] Accents =
    {
        new Color(1.0f, 0.6f, 0.0f),    // Tier1 — Lava: fiery yellow/orange
        new Color(0.2f, 0.65f, 1.0f),   // Tier2 — Earth: soft ocean blue/green
        new Color(0.9f, 0.75f, 0.55f),  // Tier3 — Sand: beige/sandy brown
        new Color(1.0f, 0.0f, 0.8f),    // Tier4 — Pink/Magenta: vibrant magenta
        new Color(0.35f, 0.2f, 0.9f),   // Tier5 — Dark Blue: deep violet/indigo
        new Color(0.75f, 0.75f, 0.75f), // Tier6 — Moon/Grey: ash grey
        new Color(0.4f, 1.0f, 0.1f),    // Tier7 — Lime Green: acid lime green
        new Color(0.6f, 0.85f, 1.0f),   // Tier8 — Ice Blue: cool frost ice blue
    };

    // Asteroid/base-meteor lava red-orange — fallback for a tier index
    // outside the array (shouldn't happen on the current 8-tier ladder, but
    // keeps this defensive rather than silently going white/invisible).
    private static readonly Color DefaultColor = new Color(1.0f, 0.35f, 0.0f);

    public static Color GetAccentColor(PlanetTier tier)
    {
        int index = (int)tier;
        return (index >= 0 && index < Accents.Length) ? Accents[index] : DefaultColor;
    }
}

// Marker + state component so BlackHole and PlanetMerge can identify and react to planets.
public class Planet : MonoBehaviour
{
    private static int nextUniqueId = 0;

    [Header("Cosmic Visuals")]
    // Pre-colored planet sprites, indexed by tier: Tier1 = 0, Tier2 = 1, ... Tier8 = 7.
    [SerializeField] private Sprite[] planetSprites;

    [Header("Tier Physics")]
    // Mass grows exponentially: mass = baseMass * massMultiplierPerTier^(tier - 1).
    // Defaults give Tier1 = 1, Tier2 ≈ 2.7, Tier4 ≈ 20, Tier5 ≈ 53 — a Tier1
    // planet bouncing off a high-tier planet barely moves it, so spam-launched
    // small planets can't wedge under the big ones, and BlackHole's heavy-
    // settling bias (scaled by tier) has real weight differences to work with.
    [SerializeField] private float baseMass = 1f;
    [SerializeField] private float massMultiplierPerTier = 2.7f;

    // Linear damping grows linearly with tier so heavy planets bleed off the
    // small shoves they do receive instead of skating around like ice cubes:
    // damping = baseLinearDamping + linearDampingPerTier * (tier - 1).
    [SerializeField] private float baseLinearDamping = 0f;
    [SerializeField] private float linearDampingPerTier = 0.4f;

    // Angular damping on the same tier curve. Friction alone can't stop two
    // touching circles from rolling around each other (Box2D has no rolling
    // resistance), so this is what actually kills the endless spin-and-slide
    // that let planets ladder past each other into unearned chain merges.
    [SerializeField] private float baseAngularDamping = 1.5f;
    [SerializeField] private float angularDampingPerTier = 0.5f;

    [Header("Settling Stabilization")]
    // A planet counts as SETTLED once it sits inside this radius around the
    // black hole AND is moving slower than settleSpeedThreshold. Settled
    // planets swap to the much higher damping pair below, so they park in
    // place, resist the constant central pull, and can physically block each
    // other — the Suika-style clutter the balance needs. Size this to the
    // cluster zone around the core, not the whole arena.
    [SerializeField] private float settleRadius = 4f;
    [SerializeField] private float settleSpeedThreshold = 0.8f;
    // Hysteresis: a settled planet only wakes back to flight damping once
    // something shoves it faster than threshold * this multiplier, so grazing
    // hits from new shots rock it without re-liquefying the whole pile.
    [SerializeField] private float settleExitSpeedMultiplier = 2f;
    [SerializeField] private float settledLinearDamping = 4f;
    [SerializeField] private float settledAngularDamping = 8f;

    // Old scenes/prefabs stored this as "CurrentColor"; the enum's int values
    // carry over (Red→Tier1, Blue→Tier2, Green→Tier3, Yellow→Tier4).
    [FormerlySerializedAs("CurrentColor")]
    public PlanetTier CurrentTier = PlanetTier.Tier1;

    public int UniqueId { get; private set; }

    private Rigidbody2D rb;
    private BlackHole blackHole;

    // The tier-curve damping this planet flies with while NOT settled;
    // recomputed by ApplyTierPhysics on every tier change.
    private float flightLinearDamping;
    private float flightAngularDamping;
    private bool isSettled;

    void Awake()
    {
        UniqueId = nextUniqueId++;
        blackHole = FindFirstObjectByType<BlackHole>();

        // Planets settling into a resting cluster (pulled by BlackHole but pinned by
        // neighbors) fall below Unity's sleep velocity threshold and go to sleep.
        // Once both bodies in a contact are asleep, OnCollisionStay2D stops firing for
        // that pair, so same-tier planets could sit touching forever without merging.
        if (TryGetComponent(out rb))
        {
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }

        // Scene-placed planets may never receive a SetTier call (the launcher and
        // merges always issue one), so make sure the serialized tier's physics
        // profile is applied at least once.
        ApplyTierPhysics(CurrentTier);
    }

    // Settled-state bookkeeping: swap between the flight and settled damping
    // profiles based on where the planet is and how fast it moves. Runs even
    // while the merge system owns the body (rb.simulated false) — writing
    // damping to a frozen body is harmless and the state stays current.
    void FixedUpdate()
    {
        if (rb == null)
            return;

        float speed = rb.linearVelocity.magnitude;
        bool inOrbitArea = blackHole == null ||
            Vector2.Distance(rb.position, blackHole.transform.position) <= settleRadius;

        if (isSettled)
        {
            if (!inOrbitArea || speed > settleSpeedThreshold * settleExitSpeedMultiplier)
            {
                isSettled = false;
            }
        }
        else if (inOrbitArea && speed <= settleSpeedThreshold)
        {
            isSettled = true;
        }

        ApplyDamping();
    }

    // Impact SFX for any contact this planet is part of (planet-vs-planet,
    // planet-vs-meteorite): AudioManager damps the volume by this impact
    // speed and its internal cooldown swallows the mirrored callback the
    // other body fires for the same contact.
    void OnCollisionEnter2D(Collision2D collision)
    {
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayCollision(collision.relativeVelocity.magnitude);
        }
    }

    // Mass and damping for the given tier, per the curves configured above.
    // Called from SetTier, so a merge upgrade instantly makes the planet heavier
    // and more grounded in the same frame its sprite changes.
    private void ApplyTierPhysics(PlanetTier tier)
    {
        if (rb == null && !TryGetComponent(out rb))
            return;

        int tierSteps = (int)tier; // Tier1 = 0 steps above base.
        rb.mass = baseMass * Mathf.Pow(massMultiplierPerTier, tierSteps);
        flightLinearDamping = baseLinearDamping + linearDampingPerTier * tierSteps;
        flightAngularDamping = baseAngularDamping + angularDampingPerTier * tierSteps;
        ApplyDamping();
    }

    private void ApplyDamping()
    {
        rb.linearDamping = isSettled ? settledLinearDamping : flightLinearDamping;
        rb.angularDamping = isSettled ? settledAngularDamping : flightAngularDamping;
    }

    // Single source of truth for tier art: SetTier uses it on live planets, and
    // PlanetLauncher reads it off the prefab's Planet component for the slot and
    // "next planet" previews, so previews always show the exact sprite the
    // spawned planet will wear. Returns null when no sprite is assigned.
    public Sprite GetSpriteForTier(PlanetTier tier)
    {
        int spriteIndex = (int)tier;
        if (planetSprites != null && spriteIndex >= 0 && spriteIndex < planetSprites.Length)
        {
            return planetSprites[spriteIndex];
        }
        return null;
    }

    public void SetTier(PlanetTier tier)
    {
        CurrentTier = tier;
        ApplyTierPhysics(tier);
        if (TryGetComponent(out SpriteRenderer sr))
        {
            // The assets are pre-rendered with their own gradients and glow;
            // pure white means "no tint" so they display exactly as authored.
            sr.color = Color.white;

            Sprite tierSprite = GetSpriteForTier(tier);
            if (tierSprite != null)
            {
                sr.sprite = tierSprite;
            }
            else
            {
                Debug.LogWarning($"Planet: no sprite assigned for {tier} (index {(int)tier}) — keeping the current sprite.", this);
            }
        }
    }
}
