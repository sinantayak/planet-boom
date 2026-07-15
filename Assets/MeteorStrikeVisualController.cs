using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// One visual-only shooting star between SkillManager's automatic targeting
// and Meteorite.TryDestroyBySkill. Selection, inventory consumption and the
// real explosion/audio/cleanup remain in their existing systems.
public class MeteorStrikeVisualController : MonoBehaviour
{
    public static MeteorStrikeVisualController Instance { get; private set; }

    [Header("Shooting Star Visual")]
    [SerializeField] private Sprite shootingStarSprite;
    [SerializeField] private float shootingStarScale = 0.18f;
    // Viewport coordinates may sit outside 0..1; this default enters from just
    // beyond the upper-left edge. Change it to choose one consistent origin.
    [SerializeField] private Vector2 entryViewportPosition = new Vector2(-0.08f, 1.08f);
    // Degrees added after aiming the sprite's local +X axis along its velocity.
    // Use this when the transparent PNG was authored pointing up/down instead.
    [SerializeField] private float spriteRotationOffset;

    [Header("Flight")]
    [SerializeField] private float travelDuration = 0.25f;
    [SerializeField] private float trajectoryArc = 0.3f;
    [SerializeField] private float fallbackSpawnDistance = 9f;

    [Header("Impact")]
    [SerializeField] private float impactFlashDuration = 0.07f;
    [SerializeField] private Color impactColor = new Color(1f, 0.35f, 0.08f, 1f);
    [SerializeField] private ParticleSystem optionalImpactVfxPrefab;

    private sealed class ActiveStrike
    {
        public Meteorite target;
        public SpriteRenderer targetRenderer;
        public SpriteRenderer projectileRenderer;
        public Color originalColor;
        public GameObject projectile;
    }

    private readonly Dictionary<Meteorite, ActiveStrike> activeStrikes = new Dictionary<Meteorite, ActiveStrike>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("MeteorStrikeVisualController: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void OnDisable()
    {
        StopAllCoroutines();
        var strikes = new List<ActiveStrike>(activeStrikes.Values);
        foreach (ActiveStrike strike in strikes)
            CleanupStrike(strike);
        activeStrikes.Clear();
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // Rejecting a second sequence for the same live target prevents duplicate
    // inventory consumption and duplicate destruction.
    public bool TryPlay(Meteorite target)
    {
        if (!IsValidTarget(target) || activeStrikes.ContainsKey(target))
            return false;

        var strike = new ActiveStrike
        {
            target = target,
            targetRenderer = target.GetComponent<SpriteRenderer>()
        };
        if (strike.targetRenderer != null)
            strike.originalColor = strike.targetRenderer.color;

        strike.projectile = CreateProjectile(strike);
        activeStrikes.Add(target, strike);
        StartCoroutine(PlaySequence(strike));
        return true;
    }

    private IEnumerator PlaySequence(ActiveStrike strike)
    {
        try
        {
            Vector3 start = strike.projectile.transform.position;
            Vector2 initialDirection = ((Vector2)strike.target.transform.position - (Vector2)start).normalized;
            Vector2 perpendicular = new Vector2(-initialDirection.y, initialDirection.x);
            float duration = Mathf.Max(0.05f, travelDuration);
            float elapsed = 0f;
            Vector3 previousPosition = start;

            while (elapsed < duration && strike.projectile != null && IsValidTarget(strike.target))
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                Vector3 targetCenter = strike.target.transform.position;
                Vector3 arcOffset = (Vector3)(perpendicular * (Mathf.Sin(t * Mathf.PI) * trajectoryArc));
                Vector3 centerPathPosition = Vector3.Lerp(start, targetCenter, eased) + arcOffset;
                Vector2 velocity = centerPathPosition - previousPosition;

                if (velocity.sqrMagnitude > 0.000001f)
                {
                    float angle = Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg + spriteRotationOffset;
                    strike.projectile.transform.rotation = Quaternion.Euler(0f, 0f, angle);
                }

                Vector2 approachDirection = velocity.sqrMagnitude > 0.000001f
                    ? velocity.normalized
                    : ((Vector2)targetCenter - (Vector2)previousPosition).normalized;
                Vector3 destination = CalculateContactCenter(strike, targetCenter, approachDirection);
                Vector3 nextPosition = Vector3.Lerp(start, destination, eased) + arcOffset;

                strike.projectile.transform.position = nextPosition;
                previousPosition = nextPosition;
                yield return null;
            }

            if (!IsValidTarget(strike.target))
                yield break;

            if (strike.projectile != null)
            {
                Destroy(strike.projectile);
                strike.projectile = null;
            }

            SpawnImpact(strike.target.transform.position, strike.target.transform.lossyScale.x);

            elapsed = 0f;
            float flashDuration = Mathf.Max(0.01f, impactFlashDuration);
            while (elapsed < flashDuration && IsValidTarget(strike.target))
            {
                elapsed += Time.deltaTime;
                if (strike.targetRenderer != null)
                    strike.targetRenderer.color = Color.Lerp(Color.white, impactColor, elapsed / flashDuration);
                yield return null;
            }

            if (IsValidTarget(strike.target))
            {
                // Existing method remains the only owner of the final
                // explosion VFX, SFX, physics shutdown and destruction.
                strike.target.TryDestroyBySkill();
            }
        }
        finally
        {
            CleanupStrike(strike);
        }
    }

    private GameObject CreateProjectile(ActiveStrike strike)
    {
        var projectile = new GameObject("MeteorStrikeShootingStar");
        projectile.transform.SetParent(transform, false);
        projectile.transform.position = EntryWorldPosition(strike.target);
        projectile.transform.localScale = Vector3.one * Mathf.Max(0.001f, shootingStarScale);

        SpriteRenderer renderer = projectile.AddComponent<SpriteRenderer>();
        // A missing custom sprite remains testable by borrowing the target art;
        // assigning a transparent comet PNG produces the intended trail shape.
        renderer.sprite = shootingStarSprite != null
            ? shootingStarSprite
            : (strike.targetRenderer != null ? strike.targetRenderer.sprite : null);
        renderer.color = Color.white;
        renderer.sortingOrder = strike.targetRenderer != null ? strike.targetRenderer.sortingOrder + 50 : 100;
        strike.projectileRenderer = renderer;
        return projectile;
    }

    // Places the projectile pivot so the visible front of its rendered sprite
    // touches the target's rendered outer edge. Renderer bounds are projected
    // along the current approach direction, so sprite pivots, scale, rotation
    // and differently sized meteor tiers are all accounted for automatically.
    private static Vector3 CalculateContactCenter(ActiveStrike strike, Vector3 targetCenter, Vector2 direction)
    {
        if (direction.sqrMagnitude <= 0.000001f)
            return targetCenter;

        direction.Normalize();
        float projectileFront = GetProjectedFrontExtent(
            strike.projectileRenderer, strike.projectile.transform.position, direction);
        float targetNearEdge = GetProjectedFrontExtent(
            strike.targetRenderer, targetCenter, -direction);
        return targetCenter - (Vector3)(direction * (projectileFront + targetNearEdge));
    }

    private static float GetProjectedFrontExtent(SpriteRenderer renderer, Vector3 origin, Vector2 direction)
    {
        if (renderer == null || renderer.sprite == null)
            return 0f;

        Bounds localBounds = renderer.sprite.bounds;
        Vector3 min = localBounds.min;
        Vector3 max = localBounds.max;
        float furthest = 0f;

        for (int x = 0; x < 2; x++)
        {
            for (int y = 0; y < 2; y++)
            {
                Vector3 localCorner = new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, 0f);
                Vector3 worldCorner = renderer.transform.TransformPoint(localCorner);
                furthest = Mathf.Max(furthest, Vector2.Dot((Vector2)(worldCorner - origin), direction));
            }
        }

        return furthest;
    }

    private Vector3 EntryWorldPosition(Meteorite target)
    {
        Vector3 targetPosition = target.transform.position;
        Camera camera = Camera.main;
        if (camera != null)
        {
            float depth = Mathf.Abs(targetPosition.z - camera.transform.position.z);
            Vector3 world = camera.ViewportToWorldPoint(
                new Vector3(entryViewportPosition.x, entryViewportPosition.y, depth));
            world.z = targetPosition.z;
            return world;
        }

        return targetPosition + new Vector3(-fallbackSpawnDistance, fallbackSpawnDistance, 0f);
    }

    private void SpawnImpact(Vector3 position, float targetScale)
    {
        float radius = Mathf.Max(0.15f, targetScale * 1.5f);
        MeteorExplosionVFX.SpawnMergeBurst(position, impactColor, radius);

        if (optionalImpactVfxPrefab == null)
            return;
        ParticleSystem instance = Instantiate(optionalImpactVfxPrefab, position, Quaternion.identity);
        instance.Play();
        float lifetime = instance.main.duration + instance.main.startLifetime.constantMax + 0.2f;
        Destroy(instance.gameObject, lifetime);
    }

    private static bool IsValidTarget(Meteorite target)
    {
        return target != null && target.gameObject.activeInHierarchy &&
               !target.IsBeingAbsorbed && !target.IsAbsorbing;
    }

    private void CleanupStrike(ActiveStrike strike)
    {
        if (strike == null)
            return;

        activeStrikes.Remove(strike.target);
        if (strike.targetRenderer != null)
            strike.targetRenderer.color = strike.originalColor;
        if (strike.projectile != null)
            Destroy(strike.projectile);
        strike.projectile = null;
    }
}
