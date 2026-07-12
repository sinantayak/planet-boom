using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(Planet))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlanetMerge : MonoBehaviour
{
    // Two planets at this tier don't merge into the next tier — they trigger
    // the BOOM chain explosion instead (both die + every same-tier planet in
    // the scene). Tier4 caps gameplay to the first 4 tiers for now; raise it
    // (up to Tier8) once more of the sprite ladder is in play.
    // Old saves stored this as the int "maxLevel"; the value carries over
    // (3 → Tier4) because enums serialize as their int value.
    [FormerlySerializedAs("maxLevel")]
    [SerializeField] private PlanetTier maxTier = PlanetTier.Tier4;

    // Fractional size gain per tier, applied to the planet's OWN spawn scale:
    // each tier is (1 + growthPerTier) times the size of the tier below it,
    // whatever scale the prefab/instance was authored at. 0.15 → each tier is
    // a subtle 15% larger than the previous one.
    [FormerlySerializedAs("growthPerLevel")]
    [SerializeField] private float growthPerTier = 0.15f;
    [SerializeField] private float touchDistanceMultiplier = 1.05f;
    [SerializeField] private float fusionDuration = 0.25f;
    [SerializeField] private float stretchAmount = 0.35f;

    private Planet planet;
    private Rigidbody2D rb;
    private CircleCollider2D circleCollider;
    private bool hasMerged;
    private float baseColliderRadius;

    // The Tier1 scale this planet spawned with, captured before any merging.
    // All tier growth is computed relative to this, so shrinking or enlarging
    // the prefab (or the instance in the editor) never changes the growth feel.
    private float tierOneScale;

    // Winner side: currently pulling a losing planet into itself. Public so
    // BlackHole's win-vortex spiral can leave a still-fusing pair's
    // transforms alone until FuseWith's own animation finishes — otherwise
    // the vortex and the fuse coroutine fight over the same transform every
    // frame, which is exactly what made the level-completing merge (the one
    // most likely to be actively fusing the instant CompleteLevel fires)
    // look glitchy or invisible.
    private bool isAbsorbing;
    public bool IsAbsorbing => isAbsorbing;

    // Loser side: currently being pulled into a winner; physics is off and this
    // planet must be ignored by every other merge check until it is destroyed.
    public bool IsBeingAbsorbed { get; private set; }

    void Awake()
    {
        planet = GetComponent<Planet>();
        rb = GetComponent<Rigidbody2D>();
        circleCollider = GetComponent<CircleCollider2D>();
        baseColliderRadius = circleCollider.radius;

        // The authored scale is treated as the Tier1 baseline; Start() below
        // grows it if this planet actually enters play at a higher tier.
        tierOneScale = transform.localScale.x;
    }

    // Compound growth: Tier1 = spawn scale, Tier2 = +15%, Tier3 = +15%
    // on top of Tier2 (with the default growthPerTier of 0.15).
    // Public so PlanetLauncher can size its slot preview to the exact world
    // scale the fired planet will have. On a prefab asset Awake never runs
    // (tierOneScale is still 0), so the authored localScale stands in as the
    // Tier1 baseline — the same value Awake captures on a spawned instance.
    public float ScaleForTier(PlanetTier tier)
    {
        float baseline = tierOneScale > 0f ? tierOneScale : transform.localScale.x;
        return baseline * Mathf.Pow(1f + growthPerTier, (int)tier);
    }

    void Start()
    {
        // OnCollisionEnter2D/Stay2D stop firing once both Rigidbody2Ds in a contact
        // fall asleep, which happens right as two planets drift to a near-zero
        // relative velocity next to each other. Staying awake is what lets the
        // proximity check below keep running every physics step regardless of speed.
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        // The launcher may spawn planets above Tier1 (see PlanetLauncher's
        // highestSpawnTier). Start runs after the launcher's same-frame SetTier
        // call, so this snaps the spawn scale onto the tier growth curve — a
        // spawned Tier2 is exactly the size a merged-up Tier2 would be.
        if (planet.CurrentTier != PlanetTier.Tier1)
        {
            transform.localScale = Vector3.one * ScaleForTier(planet.CurrentTier);
        }
    }

    void FixedUpdate()
    {
        // Reset every step: this flag only needs to stop the *same* merge event from
        // being double-processed within one physics step (e.g. a collision callback
        // and the proximity check both catching the same pair). It must not persist
        // across steps, or a planet that already won one merge could never merge
        // again afterwards.
        hasMerged = false;

        if (IsBeingAbsorbed || isAbsorbing)
            return;

        CheckProximityMerge();
    }

    void OnCollisionEnter2D(Collision2D collision)
    {
        TryMergeWith(collision.gameObject);
    }

    void OnCollisionStay2D(Collision2D collision)
    {
        TryMergeWith(collision.gameObject);
    }

    // Bypasses Unity's collision events entirely: any same-tier planet whose
    // center is within (sum of radii * touchDistanceMultiplier) merges instantly,
    // regardless of relative velocity or whether a collision event ever fired.
    private void CheckProximityMerge()
    {
        float myRadius = circleCollider.radius * transform.lossyScale.x;

        foreach (Planet otherPlanet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (otherPlanet == planet || !otherPlanet.gameObject.activeInHierarchy)
                continue;

            if (otherPlanet.CurrentTier != planet.CurrentTier)
                continue;

            if (!otherPlanet.TryGetComponent(out CircleCollider2D otherCollider))
                continue;

            float otherRadius = otherCollider.radius * otherPlanet.transform.lossyScale.x;
            float touchDistance = (myRadius + otherRadius) * touchDistanceMultiplier;
            float distance = Vector2.Distance(transform.position, otherPlanet.transform.position);

            if (distance <= touchDistance && TryMergeWith(otherPlanet.gameObject))
                break;
        }
    }

    private bool TryMergeWith(GameObject other)
    {
        if (hasMerged || isAbsorbing || IsBeingAbsorbed)
            return false;

        if (!other.TryGetComponent(out Planet otherPlanet))
            return false;

        // 2048/Suika rule: only an exact tier match can merge.
        if (otherPlanet.CurrentTier != planet.CurrentTier)
            return false;

        // Mission ceiling: if merging this tier wouldn't serve any open
        // target (GameManager.CanMerge), the pair stays two solid colliders
        // and simply bounces — e.g. Level 5's two required Tier5s must not
        // fuse into a Tier6 and destroy the player's progress. Checked before
        // any state is touched so a blocked pair behaves as if no merge rule
        // existed at all. (This also gates the max-tier BOOM: while a level
        // caps merges below PlanetMerge.maxTier, booms can't occur — fine, as
        // booms no longer drive missions.)
        if (GameManager.Instance != null && !GameManager.Instance.CanMerge(planet.CurrentTier))
            return false;

        // A planet already melting into someone (or busy pulling one in) is spoken
        // for — starting a second fusion with it would orphan or double-count it.
        if (!other.TryGetComponent(out PlanetMerge otherMerge) ||
            otherMerge.IsBeingAbsorbed || otherMerge.isAbsorbing)
            return false;

        // Only the planet with the lower UniqueId performs the merge, so a single
        // contact between two matching planets doesn't get processed from both
        // sides (which would destroy both instead of merging them into one).
        if (planet.UniqueId > otherPlanet.UniqueId)
            return false;

        hasMerged = true;

        // Two max-tier planets don't produce a bigger planet — they detonate.
        if (planet.CurrentTier >= maxTier)
        {
            TriggerBoom(otherMerge);
            return true;
        }

        isAbsorbing = true;
        otherMerge.BeginBeingAbsorbed();

        // Tier1 + Tier1 → Tier2, and so on; SetTier also swaps in the next
        // tier's sprite and applies the tier's physics profile (heavier mass,
        // higher linear damping), so the winner both looks and *feels* like the
        // new planet the instant the fusion starts.
        planet.SetTier(planet.CurrentTier + 1);

        // Mission hook: level targets are defined as "create a planet of tier
        // X", and this is the single point in the game where a new tier comes
        // into existence. Reported after SetTier so CurrentTier is the tier
        // that was just created.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyMergeCreated(planet.CurrentTier);
        }

        // Merge SFX rides the shared combo chain (meteorite merges feed the
        // same one): quick successive merges climb in pitch.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.PlayMerge();
        }

        StartCoroutine(FuseWith(otherMerge));
        return true;
    }

    // BOOM: both max-tier planets die, and the blast chains to every other
    // active planet of the same tier anywhere in the scene. Everything caught
    // in the blast is flagged absorbed *before* the deferred Destroy lands, so
    // no other planet's collision/proximity check can start a fusion with a
    // corpse during the remainder of this physics step.
    private void TriggerBoom(PlanetMerge partner)
    {
        PlanetTier boomTier = planet.CurrentTier;
        Debug.Log($"BOOM! {boomTier} chain explosion at {transform.position}.");

        // Take the two detonators out of the merge system immediately.
        BeginBeingAbsorbed();
        partner.BeginBeingAbsorbed();

        foreach (Planet victim in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            // Same tier only — the two trigger planets are swept up by this
            // same loop, so no separate Destroy for self/partner is needed.
            if (victim.CurrentTier != boomTier || !victim.gameObject.activeInHierarchy)
                continue;

            if (victim.TryGetComponent(out PlanetMerge victimMerge))
            {
                victimMerge.BeginBeingAbsorbed();
            }

            Destroy(victim.gameObject);
        }

        // Report last, once the blast has fully claimed its victims: the
        // GameManager may react by completing the level and clearing the
        // board, and that must not interleave with the destroy loop above.
        if (GameManager.Instance != null)
        {
            GameManager.Instance.NotifyBoom(boomTier);
        }
    }

    // External hook (GameManager board clears): takes this planet out of the
    // merge system ahead of a deferred Destroy, the same guarantee TriggerBoom
    // gives its victims — no other planet can start a fusion with a corpse.
    public void PrepareForDespawn()
    {
        BeginBeingAbsorbed();
    }

    // Loser side of the fusion: freeze physics right away so the melting planet
    // can be animated by hand without shoving its neighbors around.
    private void BeginBeingAbsorbed()
    {
        IsBeingAbsorbed = true;
        circleCollider.enabled = false;
        rb.simulated = false;
    }

    // Reverse-cytokinesis fusion: the loser drifts into the winner's center while
    // shrinking to nothing, and the winner stretches toward the incoming loser
    // (an oval "bridge" along the fusion axis) before relaxing into its final,
    // larger circle.
    private IEnumerator FuseWith(PlanetMerge loser)
    {
        float startScale = transform.localScale.x;
        float targetScale = ScaleForTier(planet.CurrentTier);
        Vector3 loserStartScale = loser.transform.localScale;
        float elapsed = 0f;

        while (elapsed < fusionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fusionDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            if (loser != null)
            {
                // Chase the winner's *current* position so the melt tracks us even
                // while physics keeps nudging the winner around.
                loser.transform.position = Vector3.Lerp(
                    loser.transform.position, transform.position, eased);
                loser.transform.localScale = loserStartScale * (1f - eased);

                // Point the winner's local X axis at the incoming loser, then widen
                // X: the sprite is a uniform circle, so the rotation itself is
                // invisible — it only orients the stretch along the fusion axis.
                Vector2 toLoser = loser.transform.position - transform.position;
                if (toLoser.sqrMagnitude > 0.0001f)
                {
                    float angle = Mathf.Atan2(toLoser.y, toLoser.x) * Mathf.Rad2Deg;
                    transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }
            }

            float baseScale = Mathf.Lerp(startScale, targetScale, eased);
            float stretch = stretchAmount * Mathf.Sin(t * Mathf.PI);

            transform.localScale = new Vector3(
                baseScale * (1f + stretch),
                baseScale * (1f - stretch * 0.5f),
                1f);

            // CircleCollider2D can only ever be a circle, so the non-uniform stretch
            // above would otherwise make its effective world radius balloon with the
            // visual distortion. Compensating by 1 / (1 + stretch) keeps the physics
            // radius tracking the smooth baseScale growth curve mid-fusion.
            circleCollider.radius = baseColliderRadius / (1f + stretch);

            yield return null;
        }

        transform.localScale = Vector3.one * targetScale;
        circleCollider.radius = baseColliderRadius;

        if (loser != null)
        {
            Destroy(loser.gameObject);
        }

        isAbsorbing = false;
    }
}
