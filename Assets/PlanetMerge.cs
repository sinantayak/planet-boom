using System.Collections;
using UnityEngine;

[RequireComponent(typeof(Planet))]
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(CircleCollider2D))]
public class PlanetMerge : MonoBehaviour
{
    // Two planets at this level don't level up — they trigger the BOOM chain
    // explosion instead (both die + every same-color planet in the scene).
    [SerializeField] private int maxLevel = 3;

    // Fractional size gain per tier, applied to the planet's OWN spawn scale:
    // each level is (1 + growthPerLevel) times the size of the level below it,
    // whatever scale the prefab/instance was authored at. 0.15 → each tier is
    // a subtle 15% larger than the previous one.
    [SerializeField] private float growthPerLevel = 0.15f;
    [SerializeField] private float touchDistanceMultiplier = 1.05f;
    [SerializeField] private float fusionDuration = 0.25f;
    [SerializeField] private float stretchAmount = 0.35f;

    private Planet planet;
    private Rigidbody2D rb;
    private CircleCollider2D circleCollider;
    private bool hasMerged;
    private float baseColliderRadius;

    // The tier-1 scale this planet spawned with, captured before any merging.
    // All level growth is computed relative to this, so shrinking or enlarging
    // the prefab (or the instance in the editor) never changes the growth feel.
    private float tierOneScale;

    // Winner side: currently pulling a losing planet into itself.
    private bool isAbsorbing;

    // Loser side: currently being pulled into a winner; physics is off and this
    // planet must be ignored by every other merge check until it is destroyed.
    public bool IsBeingAbsorbed { get; private set; }

    void Awake()
    {
        planet = GetComponent<Planet>();
        rb = GetComponent<Rigidbody2D>();
        circleCollider = GetComponent<CircleCollider2D>();
        baseColliderRadius = circleCollider.radius;
        tierOneScale = transform.localScale.x;
    }

    // Compound growth: level 1 = spawn scale, level 2 = +15%, level 3 = +15%
    // on top of level 2 (with the default growthPerLevel of 0.15).
    private float ScaleForLevel(int level)
    {
        return tierOneScale * Mathf.Pow(1f + growthPerLevel, level - 1);
    }

    void Start()
    {
        // OnCollisionEnter2D/Stay2D stop firing once both Rigidbody2Ds in a contact
        // fall asleep, which happens right as two planets drift to a near-zero
        // relative velocity next to each other. Staying awake is what lets the
        // proximity check below keep running every physics step regardless of speed.
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
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

    // Bypasses Unity's collision events entirely: any same-color planet whose
    // center is within (sum of radii * touchDistanceMultiplier) merges instantly,
    // regardless of relative velocity or whether a collision event ever fired.
    private void CheckProximityMerge()
    {
        float myRadius = circleCollider.radius * transform.lossyScale.x;

        foreach (Planet otherPlanet in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            if (otherPlanet == planet || !otherPlanet.gameObject.activeInHierarchy)
                continue;

            if (otherPlanet.CurrentColor != planet.CurrentColor ||
                otherPlanet.Level != planet.Level)
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

        // 2048/Suika rule: only an exact color AND level match can merge.
        if (otherPlanet.CurrentColor != planet.CurrentColor ||
            otherPlanet.Level != planet.Level)
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

        // Two max-level planets don't produce a bigger planet — they detonate.
        if (planet.Level >= maxLevel)
        {
            TriggerBoom(otherMerge);
            return true;
        }

        isAbsorbing = true;
        otherMerge.BeginBeingAbsorbed();

        planet.Level += 1;
        StartCoroutine(FuseWith(otherMerge));
        return true;
    }

    // BOOM: both max-level planets die, and the blast chains to every other
    // active planet of the same color anywhere in the scene. Everything caught
    // in the blast is flagged absorbed *before* the deferred Destroy lands, so
    // no other planet's collision/proximity check can start a fusion with a
    // corpse during the remainder of this physics step.
    private void TriggerBoom(PlanetMerge partner)
    {
        PlanetColor boomColor = planet.CurrentColor;
        Debug.Log($"BOOM! {boomColor} chain explosion at {transform.position}.");

        // Take the two detonators out of the merge system immediately.
        BeginBeingAbsorbed();
        partner.BeginBeingAbsorbed();

        foreach (Planet victim in FindObjectsByType<Planet>(FindObjectsSortMode.None))
        {
            // Same color only — the two trigger planets are swept up by this
            // same loop, so no separate Destroy for self/partner is needed.
            if (victim.CurrentColor != boomColor || !victim.gameObject.activeInHierarchy)
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
            GameManager.Instance.NotifyBoom(boomColor);
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
        float targetScale = ScaleForLevel(planet.Level);
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
