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

    void FixedUpdate()
    {
        if (isBlackHoleFrozen || pullStrength <= 0f)
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
