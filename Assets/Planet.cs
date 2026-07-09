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

// UI accent colors only (HUD hints — the aim dots went neutral white once the
// launcher slot started showing the real planet sprite; currently no code
// consumes this). Planet sprites themselves are pre-rendered and must NEVER
// be tinted — Planet.SetTier forces sr.color to pure white for exactly that
// reason.
public static class PlanetTierPalette
{
    private static readonly Color[] Accents =
    {
        new Color(0.96f, 0.35f, 0.35f), // Tier1
        new Color(0.35f, 0.55f, 0.96f), // Tier2
        new Color(0.38f, 0.85f, 0.45f), // Tier3
        new Color(0.98f, 0.85f, 0.30f), // Tier4
        new Color(0.75f, 0.45f, 0.95f), // Tier5
        new Color(0.35f, 0.90f, 0.90f), // Tier6
        new Color(0.98f, 0.55f, 0.25f), // Tier7
        new Color(0.95f, 0.95f, 0.95f), // Tier8
    };

    public static Color GetAccentColor(PlanetTier tier)
    {
        int index = (int)tier;
        return (index >= 0 && index < Accents.Length) ? Accents[index] : Color.white;
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

    // Old scenes/prefabs stored this as "CurrentColor"; the enum's int values
    // carry over (Red→Tier1, Blue→Tier2, Green→Tier3, Yellow→Tier4).
    [FormerlySerializedAs("CurrentColor")]
    public PlanetTier CurrentTier = PlanetTier.Tier1;

    public int UniqueId { get; private set; }

    void Awake()
    {
        UniqueId = nextUniqueId++;

        // Planets settling into a resting cluster (pulled by BlackHole but pinned by
        // neighbors) fall below Unity's sleep velocity threshold and go to sleep.
        // Once both bodies in a contact are asleep, OnCollisionStay2D stops firing for
        // that pair, so same-tier planets could sit touching forever without merging.
        if (TryGetComponent(out Rigidbody2D rb))
        {
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
        }

        // Scene-placed planets may never receive a SetTier call (the launcher and
        // merges always issue one), so make sure the serialized tier's physics
        // profile is applied at least once.
        ApplyTierPhysics(CurrentTier);
    }

    // Mass and damping for the given tier, per the curves configured above.
    // Called from SetTier, so a merge upgrade instantly makes the planet heavier
    // and more grounded in the same frame its sprite changes.
    private void ApplyTierPhysics(PlanetTier tier)
    {
        if (!TryGetComponent(out Rigidbody2D rb))
            return;

        int tierSteps = (int)tier; // Tier1 = 0 steps above base.
        rb.mass = baseMass * Mathf.Pow(massMultiplierPerTier, tierSteps);
        rb.linearDamping = baseLinearDamping + linearDampingPerTier * tierSteps;
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
