using UnityEngine;

public class BlackHole : MonoBehaviour
{
    [SerializeField] private float pullStrength = 15f;
    [SerializeField] private float maxPullDistance = 20f;

    [Header("Orbit Control")]
    // Cruise cap for sideways (orbital) speed around the core, in world
    // units/sec. Only the tangential velocity component is capped — radial
    // motion (falling inward, bouncing outward) is untouched, so the pull
    // itself is unaffected. Keep this low for slow, aimable drifting.
    [SerializeField] private float maxOrbitSpeed = 1.2f;

    // How quickly tangential speed above the cap is shed, in units/sec².
    // Low values let a fresh off-center shot glide for a moment before
    // settling into the slow cruise; high values snap it down instantly.
    [SerializeField] private float orbitBrakeStrength = 4f;

    [Header("Heavy Settling")]
    // Extra settling acceleration per tier step above Tier1, so heavier
    // planets migrate toward the BOTTOM of the cluster over time (pushing the
    // light ones up and out of the easy straight-shot lane). Tier1 gets none;
    // with 0.35, a Tier5 in the upper hemisphere feels ~1.4 u/s² of drift.
    [SerializeField] private float settlingAccelPerTier = 0.35f;
    // Ceiling on the drift so Tier7/Tier8 don't plow violently through the
    // cluster — settling should read as slow, inevitable sinking.
    [SerializeField] private float maxSettlingAccel = 2f;
    // Mix of the drift direction: most of it slides tangentially around the
    // cluster surface (so neighbors can roll aside) with a touch of straight
    // down. 1 = pure straight-down (tends to press into the pile and stall),
    // 0 = pure orbital slide.
    [Range(0f, 1f)]
    [SerializeField] private float settlingDownwardMix = 0.25f;

    [Header("Skills")]
    // Skill hook: while true, the black hole's pull is fully suspended (effective
    // strength multiplied by zero) and planets float freely.
    public bool isBlackHoleFrozen;

    [Header("Level Complete Vortex")]
    // The win cinematic (GameManager drives it via BeginVortex/EndVortex):
    // the hole spins up visually and drags every body on the board into the
    // core, where it is swallowed.
    //
    // DELIBERATELY NOT physics-driven at all. Two force/velocity-based
    // attempts both fought the Rigidbody2D simulation in different ways (an
    // uncapped swirl force out-muscling a constant inward pull → flung
    // outward; then a velocity-target approach that some other system kept
    // re-zeroing every step → frozen in place). Neither is worth chasing
    // further: this is a scripted cinematic, not gameplay physics, so it
    // gets the guarantee a script can give and physics can't. BeginVortex
    // sets rb.simulated = false on every affected body — the Rigidbody2D
    // stops mattering entirely — and a plain Update() loop below moves
    // transform.position directly along a spiral that is mathematically
    // incapable of doing anything but shrink toward the core.
    //
    // Pacing: earlier tuning used an inverse-distance term ("pull increases
    // near the core") that blows up at THIS game's actual scale — the play
    // area sits within a couple of units of the core (maxBoundaryRadius is
    // ~2.4), so bodies typically start the vortex already close to it. An
    // inverse-distance term at that range produced near-infinite speed and
    // swallowed the whole board within a couple of frames — the "ghost
    // vortex" (board empty almost before the cinematic could be seen).
    //
    // Two-phase fix, tuned so a body starting at maxBoundaryRadius gets ~2
    // full swirl loops before it's swallowed (simulated at 60fps: ~3.0s,
    // ~2.0 loops from 2.38 units out — see AdvanceSpiral):
    //   outer phase (distance > vortexSinkholeRadius): inward speed is
    //     proportional to remaining distance, same shape as before but with
    //     a much gentler divisor, so the cruise is slow enough to actually
    //     see the whirlpool — never blows up regardless of starting
    //     distance, same as before.
    //   sinkhole phase (distance <= vortexSinkholeRadius): an additional
    //     speed boost ramps in QUADRATICALLY as distance drops toward 0 —
    //     negligible for most of that inner radius, then shooting up right
    //     at the end — because it's an absolute add-on rather than
    //     distance-proportional, it still bites even as distance itself
    //     shrinks toward zero, giving the "late acceleration" drain feel
    //     instead of fizzling out.
    // GameManager.vortexDuration must stay >= the farthest body's arrival
    // time for this to read as a swallow rather than a cutoff — it was
    // bumped alongside this tuning.
    [SerializeField] private float vortexOuterCloseTimeConstant = 3.6f;
    // Minimum inward speed regardless of distance, so a body doesn't
    // asymptotically crawl the last few units forever.
    [SerializeField] private float vortexCoreSpeedFloor = 0.2f;
    // Below this distance from the core, the sinkhole speed boost starts
    // ramping in (see AdvanceSpiral). Kept well inside maxBoundaryRadius so
    // the outer cruise — and the swirl loops — dominate the animation.
    [SerializeField] private float vortexSinkholeRadius = 0.9f;
    // Peak extra inward speed added once a body is right at the core
    // (distance -> 0 within vortexSinkholeRadius); this is what makes the
    // final drag into the drain read as a sharp grab rather than a slow
    // fade.
    [SerializeField] private float vortexSinkholeAccel = 6f;
    // Whirlpool spin, degrees/second, applied to the body's angle around the
    // core (NOT its radius — rotating the direction vector and then
    // rescaling it to the shrunk radius every step means the spin can never
    // add outward drift; it only changes which way "inward" is facing).
    // Constant angular speed still reads as a tightening spiral because the
    // LINEAR sideways speed (radius × angularSpeed) shrinks on its own as
    // the radius does — no inverse-distance term needed here either.
    [SerializeField] private float vortexSwirlDegreesPerSecond = 240f;
    // Visual spin of the black hole transform itself, degrees/second.
    [SerializeField] private float vortexSpinSpeed = 540f;
    // A body whose shrinking radius reaches this is swallowed: taken out of
    // its merge system (PrepareForDespawn) and destroyed immediately. Because
    // the radius is an interpolated scalar that can only decrease, every
    // vortex-affected body is mathematically guaranteed to cross this
    // threshold before its radius could ever reach exactly 0 — there is no
    // tunneling case to guard against like the old velocity-integration
    // version had. Kept small (tight around the core) so bodies spin close
    // in before vanishing rather than popping out early.
    [SerializeField] private float vortexSwallowRadius = 0.3f;

    private bool isVortexActive;
    private bool vortexIncludesMeteorites;

    public bool IsVortexActive => isVortexActive;
    // Meteorite reads this (together with VortexIncludesMeteorites) so its
    // own ambient self-gravity can stand down while this script is driving
    // its transform directly — belt-and-braces on top of rb.simulated being
    // false, since AddForce/velocity writes on a non-simulated body are
    // no-ops anyway, but this keeps Meteorite's FixedUpdate from bothering.
    public bool VortexIncludesMeteorites => vortexIncludesMeteorites;

    // GameManager's level-complete cinematic. includeMeteorites mirrors its
    // persistence rule: when meteorites survive level transitions, the vortex
    // must leave them alone too — no physics disable, no pull, no swallow;
    // their own ambient gravity keeps driving them unchanged throughout.
    public void BeginVortex(bool includeMeteorites)
    {
        isVortexActive = true;
        vortexIncludesMeteorites = includeMeteorites;

        // Instantly take every affected body out of the physics simulation.
        // This is the actual fix for both prior bugs at once: with
        // rb.simulated false, nothing — not gravity, not collisions, not
        // damping, not another script's FixedUpdate — can touch this body's
        // motion again except our own transform writes below.
        DisablePhysics(FindObjectsByType<Planet>(FindObjectsSortMode.None));
        if (includeMeteorites)
        {
            DisablePhysics(FindObjectsByType<Meteorite>(FindObjectsSortMode.None));
        }
    }

    private void DisablePhysics<T>(T[] bodies) where T : Component
    {
        foreach (T body in bodies)
        {
            if (body != null && body.TryGetComponent(out Rigidbody2D rb))
            {
                rb.linearVelocity = Vector2.zero;
                rb.angularVelocity = 0f;
                rb.simulated = false;
            }
        }
    }

    public void EndVortex()
    {
        isVortexActive = false;

        // Defensive only: the normal flow always follows EndVortex with a
        // ClearBoard sweep that destroys every remaining body regardless, so
        // nothing should actually still be simulated=false afterward. This
        // just guarantees a body can never get stranded non-simulated if
        // that assumption ever changes (e.g. a future early-exit path).
        RestorePhysics(FindObjectsByType<Planet>(FindObjectsSortMode.None));
        RestorePhysics(FindObjectsByType<Meteorite>(FindObjectsSortMode.None));
    }

    private void RestorePhysics<T>(T[] bodies) where T : Component
    {
        foreach (T body in bodies)
        {
            if (body != null && body.TryGetComponent(out Rigidbody2D rb) && !rb.simulated)
            {
                rb.simulated = true;
            }
        }
    }

    void Update()
    {
        if (!isVortexActive)
            return;

        // Purely visual — sells the cinematic even though it isn't what
        // moves the infalling bodies.
        transform.Rotate(0f, 0f, vortexSpinSpeed * Time.deltaTime);

        StepVortexTransform(FindObjectsByType<Planet>(FindObjectsSortMode.None));
        if (vortexIncludesMeteorites)
        {
            StepVortexTransform(FindObjectsByType<Meteorite>(FindObjectsSortMode.None));
        }
    }

    private void StepVortexTransform<T>(T[] bodies) where T : Component
    {
        foreach (T body in bodies)
        {
            if (body == null || !body.gameObject.activeInHierarchy)
                continue;

            if (IsMidFusion(body))
                continue;

            if (AdvanceSpiral(body.transform))
            {
                Swallow(body);
            }
        }
    }

    // True while this body's own fusion animation (PlanetMerge.FuseWith or
    // Meteorite.FuseWith) is actively writing its transform.position/scale —
    // as either the winner mid-absorb or the loser mid-melt. The vortex must
    // leave these completely alone until the fusion finishes: it and the
    // fuse coroutine both write transform.position every frame, and without
    // this guard they fight over it. This matters most for exactly the merge
    // that COMPLETES the level, since BeginVortex fires synchronously from
    // inside that merge's own TryMergeWith — its fuse coroutine is always
    // still running the instant the vortex begins.
    private static bool IsMidFusion(Component body)
    {
        if (body is Meteorite meteorite)
        {
            return meteorite.IsBeingAbsorbed || meteorite.IsAbsorbing;
        }

        if (body.TryGetComponent(out PlanetMerge merge))
        {
            return merge.IsBeingAbsorbed || merge.IsAbsorbing;
        }

        return false;
    }

    // The guaranteed spiral: reduce the scalar distance-to-core via
    // MoveTowards(distance, 0, ...) — a value that interpolates toward a
    // target can never overshoot past it or grow, full stop, regardless of
    // how large inwardSpeed is — then rotate the direction vector for the
    // swirl and rebuild the position at exactly that shrunk radius. Radius
    // and angle are fully decoupled: nothing the angular term does can ever
    // feed back into the radius, so there is no mechanism left by which this
    // could push a body outward. Returns true once the shrunk radius has
    // reached vortexSwallowRadius.
    private bool AdvanceSpiral(Transform bodyTransform)
    {
        Vector2 core = transform.position;
        Vector2 offset = (Vector2)bodyTransform.position - core;
        float distance = offset.magnitude;

        if (distance <= vortexSwallowRadius)
            return true;

        // Outer cruise: proportional-to-remaining-distance, plus a floor —
        // this is what makes the timing scale-independent and slow enough to
        // show off the swirl. Sinkhole boost: zero outside vortexSinkholeRadius,
        // then ramps in quadratically as distance -> 0, so the drag stays
        // gentle until the body is genuinely close to the core and only
        // spikes right at the very end (the "late acceleration" drain).
        float outerSpeed = vortexCoreSpeedFloor + distance / vortexOuterCloseTimeConstant;
        float sinkholeT = Mathf.Clamp01(distance / vortexSinkholeRadius);
        float sinkholeBoost = (1f - sinkholeT) * (1f - sinkholeT) * vortexSinkholeAccel;
        float inwardSpeed = outerSpeed + sinkholeBoost;
        float newDistance = Mathf.MoveTowards(distance, 0f, inwardSpeed * Time.deltaTime);

        Vector2 direction = distance > 0.0001f ? offset / distance : Vector2.up;
        Vector2 rotatedDirection = RotateDegrees(direction, vortexSwirlDegreesPerSecond * Time.deltaTime);

        bodyTransform.position = core + rotatedDirection * newDistance;

        return newDistance <= vortexSwallowRadius;
    }

    private static Vector2 RotateDegrees(Vector2 v, float degrees)
    {
        float rad = degrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(rad);
        float sin = Mathf.Sin(rad);
        return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
    }

    // Despawns a body that has reached the core: out of its merge system
    // first (the same PrepareForDespawn guarantee GameManager's board clear
    // uses), then the deferred Destroy.
    private void Swallow(Component victim)
    {
        if (victim.TryGetComponent(out PlanetMerge merge))
        {
            merge.PrepareForDespawn();
        }
        if (victim.TryGetComponent(out Meteorite meteorite))
        {
            meteorite.PrepareForDespawn();
        }
        Destroy(victim.gameObject);
    }

    void FixedUpdate()
    {
        if (isBlackHoleFrozen || pullStrength <= 0f)
            return;

        // Update()'s transform spiral fully owns every vortex-affected body
        // (their Rigidbody2Ds are simulated = false, so this loop would only
        // ever be pushing on inert bodies anyway) — skip the ambient physics
        // pass entirely while the cinematic is running.
        if (isVortexActive)
            return;

        Planet[] planets = FindObjectsByType<Planet>(FindObjectsSortMode.None);

        foreach (Planet planet in planets)
        {
            if (planet == null || !planet.gameObject.activeInHierarchy)
                continue;

            if (!planet.TryGetComponent(out Rigidbody2D rb))
                continue;

            // GetPullForce is really an ACCELERATION (the trajectory preview
            // has always integrated it that way); multiplying by mass here
            // makes the pull mass-independent, like real gravity. Without
            // this, ForceMode2D.Force divides by mass and heavy planets get a
            // fraction of a Tier1's pull — which is exactly how big planets
            // ended up parked at the top of the cluster.
            Vector2 pullAccel = GetPullForce(rb.position);
            if (pullAccel != Vector2.zero)
            {
                rb.AddForce(pullAccel * rb.mass, ForceMode2D.Force);
            }

            // Heavy-tier settling: a gentle, tier-scaled drift that walks
            // high-mass planets around the cluster to its lowest point, so
            // the big merge target ends up at the BOTTOM and straight shots
            // from below can't reach it.
            Vector2 settlingAccel = GetSettlingAccel(planet, rb.position);
            if (settlingAccel != Vector2.zero)
            {
                rb.AddForce(settlingAccel * rb.mass, ForceMode2D.Force);
            }

            rb.linearVelocity = ApplyOrbitBrake(rb.position, rb.linearVelocity, Time.fixedDeltaTime);
        }
    }

    // The settling drift for one planet, as an acceleration. Zero for Tier1
    // and for anything already in the lower hemisphere; above the equator it
    // fades in with height (no on/off pop at the horizontal) and scales with
    // tier, then descends mostly ALONG the cluster surface — a tangential
    // slide on whichever side the planet already sits, so the pile slowly
    // rotates until the heavy mass rests at the lowest potential point,
    // instead of the heavy planet pressing straight into the pile and
    // stalling. Deliberately NOT part of the aim preview: freshly launched
    // Tier1/Tier2 planets get at most a trace of it, so the dots stay honest.
    private Vector2 GetSettlingAccel(Planet planet, Vector2 bodyPosition)
    {
        int tierSteps = (int)planet.CurrentTier;
        if (tierSteps <= 0)
            return Vector2.zero;

        Vector2 offset = bodyPosition - (Vector2)transform.position;
        float distance = offset.magnitude;
        if (distance < 0.01f || distance > maxPullDistance)
            return Vector2.zero;

        // 0 at the equator, 1 directly overhead; negative (below) means done.
        float upFactor = offset.y / distance;
        if (upFactor <= 0f)
            return Vector2.zero;

        float accel = Mathf.Min(settlingAccelPerTier * tierSteps, maxSettlingAccel) * upFactor;

        // Tangent pointing "downhill" along the circle on this planet's side
        // of the core; directly overhead (x == 0) it breaks the tie to the
        // right, which just picks a consistent rotation direction.
        float side = offset.x >= 0f ? 1f : -1f;
        Vector2 tangentDown = new Vector2(offset.y, -offset.x).normalized * side;
        Vector2 direction = Vector2.Lerp(tangentDown, Vector2.down, settlingDownwardMix).normalized;

        return direction * accel;
    }

    // Brakes the sideways component of a velocity toward maxOrbitSpeed and
    // returns the result; the radial component passes through unchanged.
    // Public for the same reason as GetPullForce: PlanetLauncher's trajectory
    // preview steps through this too, so the dots keep matching real flight.
    public Vector2 ApplyOrbitBrake(Vector2 bodyPosition, Vector2 velocity, float deltaTime)
    {
        if (isBlackHoleFrozen)
            return velocity;

        Vector2 toCore = (Vector2)transform.position - bodyPosition;
        float distance = toCore.magnitude;
        if (distance < 0.01f)
            return velocity;

        Vector2 radialDir = toCore / distance;
        Vector2 radialVel = radialDir * Vector2.Dot(velocity, radialDir);
        Vector2 tangentialVel = velocity - radialVel;

        float tangentialSpeed = tangentialVel.magnitude;
        if (tangentialSpeed <= maxOrbitSpeed || tangentialSpeed < 0.0001f)
            return velocity;

        float brakedSpeed = Mathf.MoveTowards(
            tangentialSpeed, maxOrbitSpeed, orbitBrakeStrength * deltaTime);

        return radialVel + tangentialVel * (brakedSpeed / tangentialSpeed);
    }

    // The single source of truth for the black hole's gravity: the ACCELERATION
    // a body at the given position receives this physics step, independent of
    // its mass (FixedUpdate multiplies by rb.mass before AddForce). Shared by
    // the real pull and by PlanetLauncher's predictive trajectory line, so the
    // preview can never drift from the actual physics at any tier.
    public Vector2 GetPullForce(Vector2 bodyPosition)
    {
        if (isBlackHoleFrozen)
            return Vector2.zero;

        Vector2 direction = (Vector2)transform.position - bodyPosition;
        float distance = direction.magnitude;

        if (distance < 0.01f || distance > maxPullDistance)
            return Vector2.zero;

        return direction.normalized * pullStrength;
    }
}
