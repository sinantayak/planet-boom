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

            Vector2 pullForce = GetPullForce(rb.position);
            if (pullForce != Vector2.zero)
            {
                rb.AddForce(pullForce, ForceMode2D.Force);
            }

            rb.linearVelocity = ApplyOrbitBrake(rb.position, rb.linearVelocity, Time.fixedDeltaTime);
        }
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

    // The single source of truth for the black hole's gravity: the force applied to a
    // body at the given position this physics step. Shared by the real pull in
    // FixedUpdate and by PlanetLauncher's predictive trajectory line, so the
    // preview can never drift from the actual physics.
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
