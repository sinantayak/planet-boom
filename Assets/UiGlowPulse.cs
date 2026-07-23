using UnityEngine;
using UnityEngine.UI;

// Lightweight reusable "something is waiting here" pulse for UI highlights
// (Evolution Popup upcoming-planet glow). Animates ONLY its own object — a
// soft alpha shimmer on the target Image plus a subtle scale breath relative
// to the authored scale, captured on enable and restored on disable so
// manual Inspector layout is never permanently overwritten. Runs on
// unscaled time (popups live under timeScale 0) with no tween package.
// Stopping is just deactivating the GameObject.
public sealed class UiGlowPulse : MonoBehaviour
{
    // Defaults to the Image on this GameObject when left empty.
    [SerializeField] private Image glowImage;
    [SerializeField] private Color glowColor = new Color(1f, 0.85f, 0.4f, 1f);
    // Full pulse cycles per second.
    [Min(0.01f)] [SerializeField] private float pulseSpeed = 1.1f;
    // Extra relative scale at the peak of the pulse (0.08 = +8%).
    [Range(0f, 0.5f)] [SerializeField] private float pulseScale = 0.08f;
    [Range(0f, 1f)] [SerializeField] private float minAlpha = 0.15f;
    [Range(0f, 1f)] [SerializeField] private float maxAlpha = 0.45f;

    private Vector3 authoredScale = Vector3.one;
    private bool hasAuthoredScale;

    private void Awake()
    {
        if (glowImage == null)
            glowImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        authoredScale = transform.localScale;
        hasAuthoredScale = true;
        Apply(0f);
    }

    private void OnDisable()
    {
        if (hasAuthoredScale)
            transform.localScale = authoredScale;
    }

    private void Update()
    {
        float wave = (Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f;
        Apply(wave);
    }

    private void Apply(float wave)
    {
        transform.localScale = authoredScale * (1f + pulseScale * wave);
        if (glowImage != null)
        {
            Color color = glowColor;
            color.a = Mathf.Lerp(minAlpha, maxAlpha, wave);
            glowImage.color = color;
        }
    }
}
