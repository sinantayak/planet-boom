using System.Collections.Generic;
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

// Per-tier colors matching each planet's actual sprite art — consumed by
// PlanetMerge's merge-sparkle VFX (which can't read a real color off the
// sprite itself, see below) and available for HUD accents. Planet sprites
// themselves are pre-rendered and must NEVER be tinted with these —
// Planet.SetTier forces sr.color to pure white for exactly that reason;
// these colors exist for effects/UI that sit alongside the sprite, not on
// top of it. Three colors per tier, straight from the art palette:
// Main (base sparkle tint), Secondary (shadow/accent/crystal/ring/base
// depending on the tier's art — the sparkle's dark gradient endpoint), and
// Glow (the sparkle's bright gradient endpoint).
//
// NOTE: a Tier9 (Cosmic Dark Blue: Main #3C4A8F, Base #1C162E,
// Glow #7B90FF) sprite is sitting in Assets/Planet Icons/Planet9.png (now
// imported — it has a .meta) but isn't wired into the tier ladder: it's not
// in Planet.prefab's planetSprites list, and PlanetTier has no Tier9
// member. Wiring it for real also touches PlanetMerge.maxTier and the
// GameManager level ramp, so it's left out here pending a deliberate
// "add Tier9" pass rather than folded silently into a color update.
public static class PlanetTierPalette
{
    private static readonly Color[] MainColors =
    {
        new Color32(0x8A, 0x8E, 0x8F, 255), // Tier1 — Gray Moon
        new Color32(0x2A, 0x75, 0xD3, 255), // Tier2 — Earth Blue/Green
        new Color32(0x75, 0xBD, 0xF2, 255), // Tier3 — Ice/Light Blue
        new Color32(0xC6, 0x2D, 0x99, 255), // Tier4 — Magenta/Purple
        new Color32(0x20, 0xBF, 0xA8, 255), // Tier5 — Turquoise/Purple Crystals
        new Color32(0xE6, 0x3B, 0x72, 255), // Tier6 — Pink/Orange Gas
        new Color32(0xE5, 0xA9, 0x6A, 255), // Tier7 — Caramel Desert
        new Color32(0x6B, 0xE8, 0x3D, 255), // Tier8 — Neon Green Ring
    };

    private static readonly Color[] SecondaryColors =
    {
        new Color32(0x5A, 0x5D, 0x5E, 255), // Tier1 — Shadow
        new Color32(0x44, 0xB7, 0x46, 255), // Tier2 — Accent
        new Color32(0x33, 0x6B, 0x9E, 255), // Tier3 — Shadow
        new Color32(0x75, 0x1E, 0x6E, 255), // Tier4 — Shadow
        new Color32(0x9B, 0x3C, 0xE3, 255), // Tier5 — Crystal
        new Color32(0xFF, 0xB8, 0x5C, 255), // Tier6 — Ring
        new Color32(0x8E, 0x56, 0x33, 255), // Tier7 — Shadow
        new Color32(0x29, 0x7B, 0x15, 255), // Tier8 — Ring
    };

    private static readonly Color[] GlowColors =
    {
        new Color32(0xD1, 0xD5, 0xD6, 255), // Tier1
        new Color32(0x7C, 0xE8, 0x95, 255), // Tier2
        new Color32(0xC3, 0xE6, 0xFC, 255), // Tier3
        new Color32(0xFF, 0x63, 0xD8, 255), // Tier4
        new Color32(0x5E, 0xF2, 0xDF, 255), // Tier5
        new Color32(0xFF, 0xA3, 0x75, 255), // Tier6
        new Color32(0xF7, 0xD2, 0xA3, 255), // Tier7
        new Color32(0xA2, 0xFF, 0x75, 255), // Tier8
    };

    // Asteroid/base-meteor lava red-orange — fallback for a tier index
    // outside the array (shouldn't happen on the current 8-tier ladder, but
    // keeps this defensive rather than silently going white/invisible).
    private static readonly Color DefaultColor = new Color(1.0f, 0.35f, 0.0f);

    public static Color GetAccentColor(PlanetTier tier) => Lookup(MainColors, tier);
    public static Color GetSecondaryColor(PlanetTier tier) => Lookup(SecondaryColors, tier);
    public static Color GetGlowColor(PlanetTier tier) => Lookup(GlowColors, tier);

    private static Color Lookup(Color[] colors, PlanetTier tier)
    {
        int index = (int)tier;
        return (index >= 0 && index < colors.Length) ? colors[index] : DefaultColor;
    }
}

// Marker + state component so BlackHole and PlanetMerge can identify and react to planets.
public class Planet : MonoBehaviour
{
    private static int nextUniqueId = 0;

    [Header("Cosmic Visuals")]
    // Pre-colored planet sprites, indexed by tier: Tier1 = 0, Tier2 = 1, ... Tier8 = 7.
    [SerializeField] private Sprite[] planetSprites;
    // Dedicated wildcard art shared by live planets and launcher previews.
    // Future aura/VFX code can use CosmicMimicStateChanged independently.
    [SerializeField] private Sprite cosmicMimicSprite;

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

    // Swapped onto the CircleCollider2D the instant isSettled flips, so a
    // flying planet keeps some slide (friction ~0.8, no bounce — see
    // PlanetSurface.physicsMaterial2D) while a settled one grips hard enough
    // that fresh shots can't wedge underneath it (friction 1, no bounce —
    // see PlanetSettled.physicsMaterial2D). Leave either slot empty to keep
    // whatever material the collider already has for that state.
    [Header("Settling Materials")]
    [SerializeField] private PhysicsMaterial2D flightMaterial;
    [SerializeField] private PhysicsMaterial2D settledMaterial;

    // How long BlackHole keeps applying a fading fraction of its pull after
    // this planet settles, instead of cutting it instantly. 0 (default) means
    // gravity disables the instant isSettled becomes true — the surest way to
    // kill the jitter entirely. Raise toward ~1.5s only if the hard cutoff
    // reads as too abrupt visually; the tradeoff is a residual pull that can
    // keep nudging the planet against its neighbors for that whole window.
    [Header("Gravity Falloff")]
    [SerializeField] private float settledGravityFalloff = 0f;

    // How long the settle transition takes to bleed residual velocity to
    // zero, instead of snapping it instantly — an instant stop reads as
    // hitting an invisible wall. See ApplySettleVelocityRamp.
    [Header("Settle Transition")]
    [SerializeField] private float settleVelocityDampDuration = 0.3f;

    // Old scenes/prefabs stored this as "CurrentColor"; the enum's int values
    // carry over (Red→Tier1, Blue→Tier2, Green→Tier3, Yellow→Tier4).
    [FormerlySerializedAs("CurrentColor")]
    public PlanetTier CurrentTier = PlanetTier.Tier1;

    public int UniqueId { get; private set; }

    private Rigidbody2D rb;
    private CircleCollider2D circleCollider;
    private BlackHole blackHole;

    // The tier-curve damping this planet flies with while NOT settled;
    // recomputed by ApplyTierPhysics on every tier change.
    private float flightLinearDamping;
    private float flightAngularDamping;
    private bool isSettled;
    private float settledSinceTime;

    // Gate on the Flying->Settled transition: colliders CURRENTLY touching
    // this planet that belong to an already-Settled planet/meteorite (see
    // UpdatePileContact/OnCollisionExit2D below — added on contact, removed
    // the instant contact ends or the neighbor stops being Settled). Combined
    // live with BlackHole.IsTouchingCore (polled every step, since the core
    // has no collider to fire a contact event) to decide "touching the pile
    // right now" in FixedUpdate. Deliberately LIVE rather than a one-shot
    // latch: without this, a shot's velocity naturally passes through zero at
    // the apex of its arc — out in open space, nowhere near the pile — and
    // the old speed-only check would freeze it there mid-air; a latch that
    // never resets would only trade that bug for a subtler one (settling in
    // open space after merely having touched something earlier, then
    // drifting away before actually coming to rest).
    private readonly HashSet<Collider2D> pileContacts = new HashSet<Collider2D>();

    // Read by BlackHole so it knows whether — and how much — of its pull to
    // apply this step. See settledGravityFalloff above for the decay option.
    public bool IsSettled => isSettled;

    private float WorldRadius => circleCollider != null ? circleCollider.radius * transform.lossyScale.x : 0f;

    public float GravityMultiplier
    {
        get
        {
            if (!isSettled)
                return 1f;
            if (settledGravityFalloff <= 0f)
                return 0f;
            float elapsed = Time.time - settledSinceTime;
            return 1f - Mathf.Clamp01(elapsed / settledGravityFalloff);
        }
    }

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
        TryGetComponent(out circleCollider);
        ApplyMaterial();

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

        // Polled every step rather than event-driven: the core has no
        // Collider2D, so this is the only way contact with it is ever
        // detected (see BlackHole.IsTouchingCore).
        bool touchingCoreNow = blackHole != null && blackHole.IsTouchingCore(rb.position, WorldRadius);
        bool touchingPileNow = touchingCoreNow || pileContacts.Count > 0;

        float speed = rb.linearVelocity.magnitude;
        bool inOrbitArea = blackHole == null ||
            Vector2.Distance(rb.position, blackHole.transform.position) <= settleRadius;

        bool wasSettled = isSettled;

        if (isSettled)
        {
            if (!inOrbitArea || speed > settleSpeedThreshold * settleExitSpeedMultiplier)
            {
                isSettled = false;
            }
        }
        else if (inOrbitArea && speed <= settleSpeedThreshold && touchingPileNow)
        {
            isSettled = true;
        }

        if (isSettled != wasSettled)
        {
            OnSettleStateChanged();
        }

        if (isSettled && (blackHole == null || !blackHole.IsGravitySingularityActive))
        {
            ApplySettleVelocityRamp();
        }

        ApplyDamping();
    }

    // Visual systems can subscribe to this state change later to swap a
    // rainbow sprite/material/VFX without coupling art to merge logic.
    public bool IsCosmicMimic { get; private set; }
    public event System.Action<bool> CosmicMimicStateChanged;

    public void SetCosmicMimic(bool active)
    {
        if (IsCosmicMimic == active)
            return;

        IsCosmicMimic = active;
        ApplyCurrentVisual();
        CosmicMimicStateChanged?.Invoke(active);
    }

    // Fires exactly once on each Flying<->Settled edge. BlackHole separately
    // stops re-applying its pull once IsSettled is true (see
    // GravityMultiplier), so the only remaining driver of jitter was the
    // instant velocity snap this used to do — that's now handled gradually
    // by ApplySettleVelocityRamp instead.
    private void OnSettleStateChanged()
    {
        if (isSettled)
        {
            settledSinceTime = Time.time;
        }

        ApplyMaterial();
    }

    // Bleeds off residual velocity smoothly instead of snapping it to zero,
    // which read as the planet hitting an invisible wall the instant it
    // settled. Blends the CURRENT velocity toward zero each step — not a
    // snapshot taken at settle time — so a genuine bump partway through the
    // ramp (one too soft to trip the unsettle threshold above) still shows up
    // before being damped away, instead of being silently overwritten. The
    // per-step factor is sized to the time remaining in the window, so the
    // ramp always lands on an exact zero right as settleVelocityDampDuration
    // elapses rather than merely approaching it asymptotically forever.
    private void ApplySettleVelocityRamp()
    {
        float elapsed = Time.time - settledSinceTime;
        if (elapsed >= settleVelocityDampDuration)
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            return;
        }

        float remaining = settleVelocityDampDuration - elapsed;
        float t = Mathf.Clamp01(Time.fixedDeltaTime / remaining);
        rb.linearVelocity = Vector2.Lerp(rb.linearVelocity, Vector2.zero, t);
        rb.angularVelocity = Mathf.Lerp(rb.angularVelocity, 0f, t);
    }

    private void ApplyMaterial()
    {
        if (circleCollider == null)
            return;

        PhysicsMaterial2D material = isSettled ? settledMaterial : flightMaterial;
        if (material != null)
        {
            circleCollider.sharedMaterial = material;
        }
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

        UpdatePileContact(collision);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        UpdatePileContact(collision);
    }

    // Contact ending is exactly as informative as contact starting for a live
    // "touching right now" gate — separating from a neighbor must clear it
    // out of pileContacts even if this planet never itself settled.
    void OnCollisionExit2D(Collision2D collision)
    {
        pileContacts.Remove(collision.collider);
    }

    // Part of the live settle-contact gate (see pileContacts): touching a
    // neighbor only counts while that neighbor is itself currently Settled —
    // two still-Flying planets bouncing off each other mid-air isn't a
    // foundation to rest on. Meteorites count too: they're solid, physically
    // blocking members of the same pile even though they're a separate merge
    // system (see Meteorite.cs). Re-evaluated on every Stay callback, not
    // just Enter, so a neighbor that settles (or wakes back up) mid-contact
    // is reflected without waiting for a fresh collision.
    private void UpdatePileContact(Collision2D collision)
    {
        if (IsSettledPileMember(collision.gameObject))
        {
            pileContacts.Add(collision.collider);
        }
        else
        {
            pileContacts.Remove(collision.collider);
        }
    }

    private static bool IsSettledPileMember(GameObject other)
    {
        if (other.TryGetComponent(out Planet otherPlanet))
            return otherPlanet.IsSettled;
        if (other.TryGetComponent(out Meteorite otherMeteorite))
            return otherMeteorite.IsSettled;
        return false;
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

    public Sprite GetCosmicMimicSprite()
    {
        return cosmicMimicSprite;
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

            Sprite visualSprite = IsCosmicMimic && cosmicMimicSprite != null
                ? cosmicMimicSprite
                : GetSpriteForTier(tier);
            if (visualSprite != null)
            {
                sr.sprite = visualSprite;
            }
            else
            {
                Debug.LogWarning($"Planet: no sprite assigned for {tier} (index {(int)tier}) — keeping the current sprite.", this);
            }
        }
    }

    private void ApplyCurrentVisual()
    {
        // SetTier is already the single visual/physics application path. Reuse
        // it so leaving Mimic state always restores the exact current tier art.
        SetTier(CurrentTier);
    }
}
