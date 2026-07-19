using System.Collections;
using TMPro;
using UnityEngine;

// Combo-based dopamine popup ("GREAT! COMBO x3", etc). Spawned once per
// merge whose AudioManager.CurrentCombo is 2 or higher — a lone merge with
// no chain (combo 1) stays quiet, nothing to celebrate yet. Renders as a
// plain 3D-world TextMeshPro object (NOT TextMeshProUGUI — no Canvas
// involved), so it sits directly at the merge position in the arena with
// zero extra scene wiring beyond this singleton.
//
// Scene-local singleton, same shape as AudioManager/GameManager: one
// instance, Awake wires it up, callers reach it via Instance.
public class ComboTextSpawner : MonoBehaviour
{
    public static ComboTextSpawner Instance { get; private set; }

    [Header("Font")]
    // Left null to fall back to TMP_Settings.defaultFontAsset (the project's
    // configured default font asset), so this works with zero Inspector setup.
    [SerializeField] private TMP_FontAsset fontAsset;

    [Header("Combo Artwork")]
    [Tooltip("X1..X5. Gameplay feedback starts at combo 2; values above 5 reuse X5.")]
    [SerializeField] private Sprite[] comboSprites = new Sprite[5];
    [SerializeField, Range(.05f, 1f)] private float comboSpriteScale = .18f;

    [System.Serializable]
    private struct ComboTier
    {
        public string label;
        public float scale;
        public Color color;
        // Cosmic-tier drama: color pulses toward white instead of staying flat.
        public bool flashing;
    }

    [Header("Tiers")]
    // Index-aligned with (combo - 2): combo 2 -> index 0 ("GOOD!"), combo 3 ->
    // index 1 ("GREAT!"), and so on. Combo counts at or beyond the array's
    // length all reuse the LAST entry, so every combo 6, 7, 8... still reads
    // as the final "COSMIC!" tier instead of throwing.
    [SerializeField]
    private ComboTier[] tiers =
    {
        new ComboTier { label = "GOOD!", scale = 1.0f, color = new Color(0.80f, 1f, 0.85f) },
        new ComboTier { label = "GREAT!", scale = 1.3f, color = new Color(1f, 0.92f, 0.25f) },
        new ComboTier { label = "EXCELLENT!", scale = 1.65f, color = new Color(1f, 0.5f, 0.12f) },
        new ComboTier { label = "UNBELIEVABLE!", scale = 2.0f, color = new Color(1f, 0.2f, 0.85f), flashing = true },
        new ComboTier { label = "COSMIC!", scale = 2.3f, color = new Color(0.55f, 0.65f, 1f), flashing = true },
    };

    [Header("Motion")]
    // Instant punchy scale-in: overshoots targetScale before settling, the
    // same sin(t*PI) idiom PlanetMerge's fusion stretch uses for a quick bounce.
    [SerializeField] private float punchDuration = 0.15f;
    [SerializeField] private float punchOvershoot = 0.25f;
    // World units the text drifts upward over its lifetime.
    [SerializeField] private float driftDistance = 1.2f;
    [SerializeField] private float minLifetime = 0.8f;
    [SerializeField] private float maxLifetime = 1.2f;
    // World-space TMP font size baseline before the per-tier scale multiplier;
    // tune this first if popups render too large/small for this scene's scale.
    [SerializeField] private float baseFontSize = 3.5f;
    // Above MeteorExplosionVFX's merge sparkle (95) and BOOM burst (100) so
    // the popup always reads on top of both.
    [SerializeField] private int sortingOrder = 110;
    // Extra gap between the tier word and the "COMBO xN" line beneath it, in
    // TMP's line-spacing units (roughly % of font size). The two lines use
    // different <size> tags, which shrinks TMP's own auto leading and reads
    // as cramped/overlapping at 0 — positive values push them apart; go
    // negative instead if a different font asset ever reads too loose.
    [SerializeField] private float lineSpacing = 25f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("ComboTextSpawner: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (fontAsset == null)
        {
            fontAsset = TMP_Settings.defaultFontAsset;
        }
    }

    // Combo 0/1 never actually reaches this from PlanetMerge/Meteorite, but
    // the guard keeps a stray call harmless rather than indexing negative.
    public void Spawn(Vector3 worldPosition, int combo)
    {
        if (combo < 2 || tiers.Length == 0)
            return;

        ComboTier tier = tiers[Mathf.Min(combo - 2, tiers.Length - 1)];

        var go = new GameObject("ComboPopup");
        go.transform.position = worldPosition;

        Sprite sprite = ResolveComboSprite(combo);
        if (sprite != null)
        {
            SpriteRenderer renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = Color.white;
            renderer.sortingOrder = sortingOrder;
            float spriteLifetime = Random.Range(minLifetime, maxLifetime);
            var spriteAnimator = go.AddComponent<ComboPopupAnimator>();
            spriteAnimator.Play(renderer, tier.scale * comboSpriteScale, punchDuration,
                punchOvershoot, driftDistance, spriteLifetime, tier.flashing);
            return;
        }

        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = $"<size=115%>{tier.label}</size>\n<size=65%>COMBO x{combo}</size>";
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = baseFontSize;
        tmp.lineSpacing = lineSpacing;
        tmp.color = tier.color;
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color(0f, 0f, 0f, 0.6f);
        if (fontAsset != null)
        {
            tmp.font = fontAsset;
        }

        if (tmp.TryGetComponent(out MeshRenderer meshRenderer))
        {
            meshRenderer.sortingOrder = sortingOrder;
        }

        float lifetime = Random.Range(minLifetime, maxLifetime);
        var animator = go.AddComponent<ComboPopupAnimator>();
        animator.Play(tmp, tier.scale, punchDuration, punchOvershoot, driftDistance, lifetime, tier.flashing);
    }

    private Sprite ResolveComboSprite(int combo)
    {
        int index = Mathf.Clamp(combo, 1, 5) - 1;
        return comboSprites != null && index < comboSprites.Length ? comboSprites[index] : null;
    }
}

// Self-contained per-popup animation: punch-scale in, then drift upward
// while fading out, then destroy its own GameObject. Lives entirely on the
// spawned popup so ComboTextSpawner itself stays a stateless factory.
public class ComboPopupAnimator : MonoBehaviour
{
    public void Play(TextMeshPro tmp, float targetScale, float punchDuration, float punchOvershoot,
        float driftDistance, float lifetime, bool flashing)
    {
        StartCoroutine(Animate(tmp, targetScale, punchDuration, punchOvershoot, driftDistance, lifetime, flashing));
    }

    public void Play(SpriteRenderer renderer, float targetScale, float punchDuration,
        float punchOvershoot, float driftDistance, float lifetime, bool flashing)
    {
        StartCoroutine(AnimateSprite(renderer, targetScale, punchDuration,
            punchOvershoot, driftDistance, lifetime, flashing));
    }

    private IEnumerator Animate(TextMeshPro tmp, float targetScale, float punchDuration, float punchOvershoot,
        float driftDistance, float lifetime, bool flashing)
    {
        Color baseColor = tmp.color;

        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / punchDuration);
            float overshoot = Mathf.Sin(t * Mathf.PI) * punchOvershoot;
            transform.localScale = Vector3.one * (targetScale * (t + overshoot));
            yield return null;
        }
        transform.localScale = Vector3.one * targetScale;

        elapsed = 0f;
        Vector3 startPos = transform.position;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);

            transform.position = startPos + Vector3.up * (driftDistance * t);

            Color c = flashing
                ? Color.Lerp(baseColor, Color.white, Mathf.PingPong(elapsed * 6f, 1f))
                : baseColor;
            c.a = 1f - t;
            tmp.color = c;

            yield return null;
        }

        Destroy(gameObject);
    }

    private IEnumerator AnimateSprite(SpriteRenderer renderer, float targetScale,
        float punchDuration, float punchOvershoot, float driftDistance, float lifetime, bool flashing)
    {
        Color baseColor = renderer.color;
        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / punchDuration);
            float overshoot = Mathf.Sin(t * Mathf.PI) * punchOvershoot;
            transform.localScale = Vector3.one * (targetScale * (t + overshoot));
            yield return null;
        }
        transform.localScale = Vector3.one * targetScale;
        elapsed = 0f;
        Vector3 startPos = transform.position;
        while (elapsed < lifetime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / lifetime);
            transform.position = startPos + Vector3.up * (driftDistance * t);
            Color color = flashing
                ? Color.Lerp(baseColor, Color.white, Mathf.PingPong(elapsed * 6f, 1f))
                : baseColor;
            color.a = 1f - t;
            renderer.color = color;
            yield return null;
        }
        Destroy(gameObject);
    }
}
