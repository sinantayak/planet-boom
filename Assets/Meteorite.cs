using System.Collections;
using UnityEngine;

// Dead-rock obstacle tier, separate from PlanetTier: meteorites cap at Tier5
// (a merge there BOOMs instead of growing further), and every tier reuses
// the SAME sprite — growth and heat-up are rendered procedurally via scale
// and color, not per-tier art.
public enum MeteoriteTier
{
    Tier1,
    Tier2,
    Tier3,
    Tier4,
    Tier5
}

// Physical-only obstacle: rolls and blocks space like a planet, but is a
// completely separate merge system from Planet/PlanetMerge. Because this
// class never looks for a Planet component (and PlanetMerge never looks for
// a Meteorite one), the two systems simply can't see each other — a
// meteorite bounces off a planet like any other solid body and neither side
// attempts a merge.
//
// Direct Growth on Any Contact: unlike PlanetMerge, meteorites do NOT
// require a tier match. ANY two meteorites that touch resolve a winner (the
// higher tier, or the higher UniqueId if tied) and the winner absorbs the
// loser, growing exactly +1 tier — so a lone Tier1 shot can always chip away
// at a stranded high-tier meteorite instead of needing an exact match that
// the launcher (Tier1-only) could never produce. A winner already at Tier5
// pops instead of growing to a nonexistent Tier6 — see TriggerBigPop.
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
[RequireComponent(typeof(SpriteRenderer))]
public class Meteorite : MonoBehaviour
{
    private static int nextUniqueId = 0;

    // Tier1..Tier5, matching the enum. Kept explicit (rather than
    // System.Enum.GetValues at runtime) since the tier curve math below reads
    // it as a constant.
    private const int TierCount = 5;

    [Header("Procedural Growth")]
    // Fractional size gain per tier, applied to the spawn scale captured in
    // Awake — same "compound growth off the authored baseline" model as
    // PlanetMerge.ScaleForTier, so tuning feels consistent across both
    // systems.
    [SerializeField] private float growthPerTier = 0.22f;
    [SerializeField] private float touchDistanceMultiplier = 1.05f;

    [Header("Volcanic Color Shift")]
    // Tier1 reads as a cold, dead rock; color lerps toward moltenColor as
    // tier climbs, so a Tier5 an instant from the Big Pop visibly glows.
    [SerializeField] private Color deadColor = new Color(0.42f, 0.42f, 0.42f, 1f);
    [SerializeField] private Color moltenColor = new Color(1f, 0.32f, 0.05f, 1f);

    [Header("Tier Physics")]
    // Same shape as Planet's mass curve: heavier at higher tiers so a molten
    // Tier5 shoves the pile around more convincingly than a Tier1 pebble.
    [SerializeField] private float baseMass = 1.4f;
    [SerializeField] private float massMultiplierPerTier = 1.8f;

    [Header("Fusion Feel")]
    [SerializeField] private float fusionDuration = 0.2f;

    [Header("The Big Pop")]
    // Two Tier5s don't make a Tier6 — they detonate. Every Rigidbody2D
    // (planets AND other meteorites) within explosionRadius gets shoved away,
    // strongest at the center, clearing breathing room in the pile.
    [SerializeField] private float explosionRadius = 3.5f;
    [SerializeField] private float explosionForce = 18f;

    public MeteoriteTier CurrentTier { get; private set; } = MeteoriteTier.Tier1;
    public int UniqueId { get; private set; }

    // Loser side: being pulled into a winner (or consumed by the Big Pop);
    // physics is off and every other meteorite must ignore this one until
    // it's destroyed. Mirrors PlanetMerge.IsBeingAbsorbed exactly.
    public bool IsBeingAbsorbed { get; private set; }

    private Rigidbody2D rb;
    private CircleCollider2D circleCollider;
    private SpriteRenderer sr;
    private BlackHole blackHole;

    // Winner side: currently absorbing a losing meteorite. Public for the
    // same reason as PlanetMerge.IsAbsorbing — BlackHole's win-vortex spiral
    // leaves a still-fusing meteorite's transform alone until FuseWith
    // finishes, instead of fighting it for the same transform every frame.
    private bool isAbsorbing;
    public bool IsAbsorbing => isAbsorbing;
    private bool hasMerged;

    // The Tier1 scale this meteorite spawned with; every tier's target scale
    // is computed relative to this, same convention as PlanetMerge.tierOneScale.
    private float tierOneScale;

    void Awake()
    {
        UniqueId = nextUniqueId++;
        rb = GetComponent<Rigidbody2D>();
        circleCollider = GetComponent<CircleCollider2D>();
        sr = GetComponent<SpriteRenderer>();
        blackHole = FindFirstObjectByType<BlackHole>();

        // Meteorites never sleep for the same reason Planet doesn't: a rock
        // pinned motionless between neighbors would otherwise stop firing
        // collision callbacks and could never merge.
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        tierOneScale = transform.localScale.x > 0f ? transform.localScale.x : 1f;
        ApplyTierVisuals();
    }

    void FixedUpdate()
    {
        hasMerged = false;

        if (IsBeingAbsorbed)
            return;

        // Meteorites are full citizens of the arena: pulled toward the core
        // and orbit-braked exactly like a planet, via BlackHole's public
        // (mass-independent) acceleration API — the same one PlanetLauncher's
        // trajectory preview uses. This keeps BlackHole itself planet-only,
        // so no change was needed there.
        //
        // EXCEPT during the win vortex when it's set to include meteorites —
        // then BlackHole's own FixedUpdate takes over this Rigidbody2D
        // entirely (including the swallow check), so this ambient pull must
        // stand down. Two systems both steering one velocity every frame is
        // exactly what caused the vortex's original outward-fling bug.
        bool vortexOwnsThisBody = blackHole != null
            && blackHole.IsVortexActive
            && blackHole.VortexIncludesMeteorites;

        if (blackHole != null && rb.simulated && !vortexOwnsThisBody)
        {
            Vector2 pullAccel = blackHole.GetPullForce(rb.position);
            if (pullAccel != Vector2.zero)
            {
                rb.AddForce(pullAccel * rb.mass, ForceMode2D.Force);
            }
            rb.linearVelocity = blackHole.ApplyOrbitBrake(rb.position, rb.linearVelocity, Time.fixedDeltaTime);
        }

        if (!isAbsorbing)
        {
            CheckProximityMerge();
        }
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        // Impact SFX first (volume damped by hit speed inside AudioManager;
        // its cooldown dedupes the other body's mirrored callback), then the
        // merge check — the sound belongs to the physical hit either way.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayCollision(collision.relativeVelocity.magnitude);
        }

        TryMergeWith(collision.gameObject);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        TryMergeWith(collision.gameObject);
    }

    // Same "bypass collision events, check world distance" approach as
    // PlanetMerge.CheckProximityMerge, scoped to Meteorite vs Meteorite only.
    // No tier filter — Direct Growth resolves ANY touching pair, not just
    // matching ones.
    private void CheckProximityMerge()
    {
        float myRadius = circleCollider.radius * transform.lossyScale.x;

        foreach (Meteorite other in FindObjectsByType<Meteorite>(FindObjectsSortMode.None))
        {
            if (other == this || !other.gameObject.activeInHierarchy)
                continue;

            float otherRadius = other.circleCollider.radius * other.transform.lossyScale.x;
            float touchDistance = (myRadius + otherRadius) * touchDistanceMultiplier;
            float distance = Vector2.Distance(transform.position, other.transform.position);

            if (distance <= touchDistance && TryMergeWith(other.gameObject))
                break;
        }
    }

    private bool TryMergeWith(GameObject other)
    {
        if (hasMerged || isAbsorbing || IsBeingAbsorbed)
            return false;

        // Not a meteorite at all (a Planet, most likely) — physics alone
        // handles the bounce, no merge system engages. This is the entire
        // mechanism behind "meteorites ignore regular planet merges completely."
        if (!other.TryGetComponent(out Meteorite otherMeteorite))
            return false;

        if (otherMeteorite.IsBeingAbsorbed || otherMeteorite.isAbsorbing)
            return false;

        // Direct Growth on Any Contact: the higher tier wins outright (a
        // Tier2 absorbs a fresh Tier1 on the very first touch, no exact
        // match required); tied tiers fall back to higher UniqueId as a
        // deterministic tie-break. Only the winner's own TryMergeWith call
        // executes — the loser's mirrored OnCollisionEnter2D/Stay2D call
        // (both sides of a contact fire it) sees IsWinnerAgainst fail and
        // no-ops, so one physical contact produces exactly one merge.
        if (!IsWinnerAgainst(otherMeteorite))
            return false;

        hasMerged = true;

        if (CurrentTier >= MeteoriteTier.Tier5)
        {
            TriggerBigPop(otherMeteorite);
            return true;
        }

        isAbsorbing = true;
        otherMeteorite.BeginBeingAbsorbed();
        CurrentTier += 1;

        // Feeds the same combo chain as planet merges — a mixed flurry of
        // planet and meteorite fusions climbs one shared pitch ladder.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayMerge();
        }

        StartCoroutine(FuseWith(otherMeteorite));
        return true;
    }

    private bool IsWinnerAgainst(Meteorite other)
    {
        if (CurrentTier != other.CurrentTier)
            return CurrentTier > other.CurrentTier;
        return UniqueId > other.UniqueId;
    }

    // Radial blast: every Rigidbody2D (planet or meteorite) within
    // explosionRadius gets pushed away from the pop center, stronger the
    // closer it was — carving breathing room out of a crowded pile.
    private void TriggerBigPop(Meteorite partner)
    {
        Vector2 center = transform.position;
        Debug.Log($"Meteorite: Tier5 BIG POP at {center}.");

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayExplosion();
        }

        // Take both detonators out of the merge/physics system first, so
        // OverlapCircleAll below can't pick up their own (about-to-be-
        // destroyed) colliders.
        BeginBeingAbsorbed();
        partner.BeginBeingAbsorbed();

        Collider2D[] hits = Physics2D.OverlapCircleAll(center, explosionRadius);
        foreach (Collider2D hit in hits)
        {
            if (!hit.TryGetComponent(out Rigidbody2D otherRb))
                continue;

            Vector2 offset = otherRb.position - center;
            float distance = offset.magnitude;
            // Directly-overhead hits (distance ~0) still get shoved — pick an
            // arbitrary direction rather than dividing by zero.
            Vector2 direction = distance > 0.01f ? offset / distance : Random.insideUnitCircle.normalized;
            float falloff = 1f - Mathf.Clamp01(distance / explosionRadius);

            otherRb.AddForce(direction * (explosionForce * falloff), ForceMode2D.Impulse);
        }

        Destroy(gameObject);
        Destroy(partner.gameObject);
    }

    // External hook (GameManager's full board wipe on a hard restart —
    // meteorites otherwise persist across level transitions, see
    // GameManager.ClearBoard): takes this meteorite out of the merge system
    // ahead of a deferred Destroy, the same guarantee PlanetMerge.
    // PrepareForDespawn gives planets — no other meteorite mid-physics-step
    // can start an absorb against a corpse.
    public void PrepareForDespawn()
    {
        BeginBeingAbsorbed();
    }

    // Loser side of a fusion: freeze physics immediately, same guarantee
    // PlanetMerge.BeginBeingAbsorbed gives — no other meteorite can start a
    // second merge with a body that's already melting or popping.
    private void BeginBeingAbsorbed()
    {
        IsBeingAbsorbed = true;
        circleCollider.enabled = false;
        rb.simulated = false;
    }

    // Reverse-cytokinesis fusion, simplified from PlanetMerge.FuseWith:
    // uniform scale growth (no stretch — a single round sprite doesn't need
    // the ellipse trick) with the color animating alongside it, so a merge
    // visibly heats the rock up in real time rather than snapping.
    private IEnumerator FuseWith(Meteorite loser)
    {
        float startScale = transform.localScale.x;
        float targetScale = ScaleForTier(CurrentTier);
        Color startColor = sr.color;
        Color targetColor = ColorForTier(CurrentTier);
        Vector3 loserStartScale = loser.transform.localScale;
        float elapsed = 0f;

        while (elapsed < fusionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fusionDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            if (loser != null)
            {
                loser.transform.position = Vector3.Lerp(loser.transform.position, transform.position, eased);
                loser.transform.localScale = loserStartScale * (1f - eased);
            }

            transform.localScale = Vector3.one * Mathf.Lerp(startScale, targetScale, eased);
            sr.color = Color.Lerp(startColor, targetColor, eased);

            yield return null;
        }

        transform.localScale = Vector3.one * targetScale;
        sr.color = targetColor;
        rb.mass = MassForTier(CurrentTier);

        if (loser != null)
        {
            Destroy(loser.gameObject);
        }

        isAbsorbing = false;
    }

    private void ApplyTierVisuals()
    {
        transform.localScale = Vector3.one * ScaleForTier(CurrentTier);
        sr.color = ColorForTier(CurrentTier);
        rb.mass = MassForTier(CurrentTier);
    }

    private float ScaleForTier(MeteoriteTier tier)
    {
        return tierOneScale * Mathf.Pow(1f + growthPerTier, (int)tier);
    }

    // 0 at Tier1 (deadColor) to 1 at Tier5 (moltenColor).
    private Color ColorForTier(MeteoriteTier tier)
    {
        float t = (int)tier / (float)(TierCount - 1);
        return Color.Lerp(deadColor, moltenColor, t);
    }

    private float MassForTier(MeteoriteTier tier)
    {
        return baseMass * Mathf.Pow(massMultiplierPerTier, (int)tier);
    }
}
