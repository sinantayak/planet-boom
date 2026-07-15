using System.Collections;
using System.Collections.Generic;
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
    // Same growth rate as Planet.massMultiplierPerTier (2.7) rather than a
    // gentler curve — dead rock reads as dense, so a high-tier meteorite
    // should feel at least as immovable as a same-tier planet, not lighter.
    // baseMass starts above Planet's Tier1 (1f) so even a fresh meteorite
    // reads as "heavier" than a fresh planet.
    [SerializeField] private float baseMass = 1.4f;
    [SerializeField] private float massMultiplierPerTier = 2.7f;

    // Sluggishness that grows with tier, mirroring Planet's damping curve.
    // Mass alone barely changes how "loose" a body feels once it's already
    // moving — a heavy Rigidbody2D still skates and spins freely without
    // damping. This is what actually makes a high-tier meteorite resist
    // being shoved and stop rolling instead of bouncing around forever.
    [SerializeField] private float baseLinearDamping = 0f;
    [SerializeField] private float linearDampingPerTier = 0.4f;
    [SerializeField] private float baseAngularDamping = 1.5f;
    [SerializeField] private float angularDampingPerTier = 0.5f;

    [Header("Fusion Feel")]
    [SerializeField] private float fusionDuration = 0.2f;

    [Header("Settling Stabilization")]
    // Mirrors Planet's settle mechanism exactly (see Planet.cs) — without it,
    // a meteorite resting in the pile would keep receiving BlackHole's full
    // pull forever and jitter in place just like planets used to.
    [SerializeField] private float settleRadius = 4f;
    [SerializeField] private float settleSpeedThreshold = 0.8f;
    [SerializeField] private float settleExitSpeedMultiplier = 2f;
    [SerializeField] private float settledLinearDamping = 4f;
    [SerializeField] private float settledAngularDamping = 8f;
    [SerializeField] private PhysicsMaterial2D flightMaterial;
    [SerializeField] private PhysicsMaterial2D settledMaterial;
    // How long the settle transition takes to bleed residual velocity to
    // zero, instead of snapping it instantly. Mirrors
    // Planet.settleVelocityDampDuration exactly — see ApplySettleVelocityRamp.
    [SerializeField] private float settleVelocityDampDuration = 0.3f;

    private bool isSettled;
    public bool IsSettled => isSettled;
    private float settledSinceTime;

    // Mirrors Planet.pileContacts exactly: gates the Flying->Settled
    // transition on LIVE contact with the core or an already-Settled
    // neighbor, so a meteorite can't freeze mid-air at the apex of its arc
    // just because its velocity happens to pass through zero there.
    private readonly HashSet<Collider2D> pileContacts = new HashSet<Collider2D>();

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

    // The tier-curve damping this meteorite rolls with while NOT settled;
    // recomputed by ApplyTierPhysics on every tier change, same split as
    // Planet's flightLinearDamping/flightAngularDamping.
    private float flightLinearDamping;
    private float flightAngularDamping;

    private float WorldRadius => circleCollider != null ? circleCollider.radius * transform.lossyScale.x : 0f;

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
        ApplyMaterial();
    }

    void FixedUpdate()
    {
        hasMerged = false;

        if (IsBeingAbsorbed)
            return;

        UpdateSettleState();

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
        //
        // Settled meteorites stop receiving this pull entirely — same fix as
        // Planet.GravityMultiplier: a constant pull opposed only by damping
        // never reaches true zero velocity, which is what caused the
        // infinite jitter in a resting pile.
        bool vortexOwnsThisBody = blackHole != null
            && blackHole.IsVortexActive
            && blackHole.VortexIncludesMeteorites;

        bool singularityActive = blackHole != null && blackHole.IsGravitySingularityActive;
        if (blackHole != null && rb.simulated && !vortexOwnsThisBody && (!isSettled || singularityActive))
        {
            Vector2 pullAccel = blackHole.GetPullForce(rb.position) * blackHole.GravitySingularityMultiplier;
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

    // Mirrors Planet's settle bookkeeping exactly: swap damping/material
    // profiles based on proximity to the core and current speed, with
    // hysteresis on the wake-up threshold so grazing hits don't re-liquefy
    // a resting rock. Residual velocity is bled off smoothly rather than
    // snapped to zero — see ApplySettleVelocityRamp below.
    private void UpdateSettleState()
    {
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
            if (isSettled)
            {
                settledSinceTime = Time.time;
            }
            ApplyMaterial();
        }

        if (isSettled && (blackHole == null || !blackHole.IsGravitySingularityActive))
        {
            ApplySettleVelocityRamp();
        }

        rb.linearDamping = isSettled ? settledLinearDamping : flightLinearDamping;
        rb.angularDamping = isSettled ? settledAngularDamping : flightAngularDamping;
    }

    // Mirrors Planet.ApplySettleVelocityRamp exactly: blends the CURRENT
    // velocity toward zero each step (not a snapshot taken at settle time),
    // landing on an exact zero right as settleVelocityDampDuration elapses
    // instead of just approaching it asymptotically forever.
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

    void OnCollisionEnter2D(Collision2D collision)
    {
        // Impact SFX first (volume damped by hit speed inside AudioManager;
        // its cooldown dedupes the other body's mirrored callback), then the
        // merge check — the sound belongs to the physical hit either way.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayCollision(collision.relativeVelocity.magnitude);
        }

        UpdatePileContact(collision);
        TryMergeWith(collision.gameObject);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        UpdatePileContact(collision);
        TryMergeWith(collision.gameObject);
    }

    void OnCollisionExit2D(Collision2D collision)
    {
        pileContacts.Remove(collision.collider);
    }

    // Mirrors Planet.UpdatePileContact: only touching an already-Settled
    // neighbor (planet or meteorite) counts toward the live settle-contact
    // gate — two still-Flying bodies bouncing off each other isn't a
    // foundation, and a neighbor that stops being Settled mid-contact drops
    // back out on the next Stay callback.
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
        if (other.TryGetComponent(out Meteorite otherMeteorite))
            return otherMeteorite.IsSettled;
        if (other.TryGetComponent(out Planet otherPlanet))
            return otherPlanet.IsSettled;
        return false;
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

            // Combo popup + skill-drop roll, both keyed off the same shared
            // combo count AudioManager just advanced above.
            int combo = AudioManager.Instance.CurrentCombo;
            if (ComboTextSpawner.Instance != null)
            {
                ComboTextSpawner.Instance.Spawn(transform.position, combo);
            }
            if (SkillDropManager.Instance != null)
            {
                SkillDropManager.Instance.TryDropOnMerge(combo, transform.position);
            }
        }

        // Subtle sparkle around the meteorite's perimeter, distinct from the
        // Big Pop burst — tinted to this meteorite's own current color (dead
        // grey through molten orange/red per ColorForTier), so hotter rocks
        // spark hotter. Pass the real WORLD radius (collider × lossyScale), not
        // the tiny localScale, so the ring lands on the actual on-screen edge.
        float worldRadius = circleCollider.radius * transform.lossyScale.x;
        MeteorExplosionVFX.SpawnMergeBurst(transform.position, sr.color, worldRadius);

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

        MeteorExplosionVFX.Spawn(center, transform.localScale.x);

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

    public bool TryDestroyBySkill()
    {
        if (IsBeingAbsorbed || isAbsorbing || !gameObject.activeInHierarchy)
            return false;

        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayExplosion();
        }

        MeteorExplosionVFX.Spawn(transform.position, transform.localScale.x);
        PrepareForDespawn();
        Destroy(gameObject);
        return true;
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
        ApplyTierPhysics(CurrentTier);

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
        ApplyTierPhysics(CurrentTier);
    }

    // Mass and damping for the given tier, mirroring Planet.ApplyTierPhysics
    // exactly — a merge that bumps CurrentTier makes the winner both heavier
    // AND more sluggish in the same step, instead of mass alone (which does
    // little to a body that's already moving/rolling without damping to
    // actually bleed that motion off).
    private void ApplyTierPhysics(MeteoriteTier tier)
    {
        int tierSteps = (int)tier;
        rb.mass = MassForTier(tier);
        flightLinearDamping = baseLinearDamping + linearDampingPerTier * tierSteps;
        flightAngularDamping = baseAngularDamping + angularDampingPerTier * tierSteps;
        rb.linearDamping = isSettled ? settledLinearDamping : flightLinearDamping;
        rb.angularDamping = isSettled ? settledAngularDamping : flightAngularDamping;
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
