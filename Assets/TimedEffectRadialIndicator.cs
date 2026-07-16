using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Reusable quick-slot/HUD countdown view. The owner supplies the authoritative
// remaining-time function; this component never advances a second timer.
public sealed class TimedEffectRadialIndicator : MonoBehaviour
{
    private static Sprite runtimeCircleSprite;
    private Image sourceIcon;
    private Image radialOverlay;
    private TextMeshProUGUI remainingText;
    private Func<float> remainingProvider;
    private Func<bool> activeProvider;
    private float totalDuration;

    public void Initialize(Image icon, Sprite circleSprite, Color overlayColor, bool showSeconds,
        Color textColor, float textFontSize)
    {
        sourceIcon = icon;
        if (sourceIcon == null)
            return;

        RectTransform slotRect = transform as RectTransform;
        if (slotRect == null)
            return;

        Transform oldOverlay = slotRect.Find("TimedEffectRadialOverlay");
        if (oldOverlay != null)
            Destroy(oldOverlay.gameObject);
        Transform oldText = slotRect.Find("RemainingSeconds");
        if (oldText != null)
            Destroy(oldText.gameObject);

        GameObject overlayObject = new GameObject("TimedEffectRadialOverlay",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.SetParent(slotRect, false);
        MatchIconRect(overlayRect, sourceIcon.rectTransform);
        radialOverlay = overlayObject.GetComponent<Image>();
        radialOverlay.raycastTarget = false;
        radialOverlay.type = Image.Type.Filled;
        radialOverlay.fillMethod = Image.FillMethod.Radial360;
        radialOverlay.fillOrigin = (int)Image.Origin360.Top;
        radialOverlay.fillClockwise = true;
        radialOverlay.color = overlayColor;
        radialOverlay.sprite = circleSprite != null ? circleSprite : GetRuntimeCircleSprite();
        radialOverlay.preserveAspect = true;
        overlayRect.SetSiblingIndex(Mathf.Min(sourceIcon.transform.GetSiblingIndex() + 1,
            slotRect.childCount - 1));

        if (showSeconds)
        {
            GameObject textObject = new GameObject("RemainingSeconds",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(slotRect, false);
            MatchIconRect(textRect, sourceIcon.rectTransform);
            remainingText = textObject.GetComponent<TextMeshProUGUI>();
            remainingText.raycastTarget = false;
            remainingText.alignment = TextAlignmentOptions.Center;
            remainingText.fontStyle = FontStyles.Bold;
            remainingText.fontSize = Mathf.Max(8f, textFontSize);
            remainingText.color = textColor;
            textRect.SetSiblingIndex(Mathf.Min(overlayRect.GetSiblingIndex() + 1,
                slotRect.childCount - 1));
        }

        Clear();
    }

    public void Begin(float authoritativeTotalDuration, Func<float> getRemaining,
        Func<bool> isActive)
    {
        if (radialOverlay == null || sourceIcon == null || authoritativeTotalDuration <= 0f)
            return;

        totalDuration = authoritativeTotalDuration;
        remainingProvider = getRemaining;
        activeProvider = isActive;
        radialOverlay.gameObject.SetActive(true);
        EnsureVisualOrderAndGeometry();
        if (remainingText != null)
            remainingText.gameObject.SetActive(true);
        Refresh();
    }

    public void Clear()
    {
        remainingProvider = null;
        activeProvider = null;
        totalDuration = 0f;
        if (radialOverlay != null)
        {
            radialOverlay.fillAmount = 0f;
            radialOverlay.gameObject.SetActive(false);
        }
        if (remainingText != null)
            remainingText.gameObject.SetActive(false);
    }

    void Update()
    {
        if (remainingProvider != null)
            Refresh();
    }

    void OnDisable() => Clear();

    private void Refresh()
    {
        if (remainingProvider == null || totalDuration <= 0f ||
            (activeProvider != null && !activeProvider()))
        {
            Clear();
            return;
        }

        float remaining = Mathf.Max(0f, remainingProvider());
        radialOverlay.fillAmount = Mathf.Clamp01(remaining / totalDuration);
        if (remainingText != null)
            remainingText.text = Mathf.CeilToInt(remaining).ToString();
        if (remaining <= 0f)
            Clear();
    }

    private void EnsureVisualOrderAndGeometry()
    {
        if (sourceIcon == null || radialOverlay == null)
            return;
        RectTransform slotRect = transform as RectTransform;
        RectTransform overlayRect = radialOverlay.rectTransform;
        MatchIconRect(overlayRect, sourceIcon.rectTransform);
        overlayRect.SetSiblingIndex(Mathf.Min(sourceIcon.transform.GetSiblingIndex() + 1,
            slotRect.childCount - 1));
        if (remainingText != null)
        {
            MatchIconRect(remainingText.rectTransform, sourceIcon.rectTransform);
            remainingText.rectTransform.SetSiblingIndex(Mathf.Min(overlayRect.GetSiblingIndex() + 1,
                slotRect.childCount - 1));
        }
    }

    private static void MatchIconRect(RectTransform rect, RectTransform iconRect)
    {
        rect.anchorMin = iconRect.anchorMin;
        rect.anchorMax = iconRect.anchorMax;
        rect.pivot = iconRect.pivot;
        rect.anchoredPosition = iconRect.anchoredPosition;
        rect.sizeDelta = iconRect.sizeDelta;
        rect.localRotation = iconRect.localRotation;
        rect.localScale = iconRect.localScale;
    }

    // Filled Images need a sprite to produce a dependable radial mesh. This
    // generated white disc is tinted by the Inspector color and avoids both
    // external artwork and blending back into the detailed skill icon.
    private static Sprite GetRuntimeCircleSprite()
    {
        if (runtimeCircleSprite != null)
            return runtimeCircleSprite;

        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "TimedEffectRadialCircle (Runtime)",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };
        var pixels = new Color32[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float radius = size * 0.48f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float alpha = 1f - Mathf.SmoothStep(radius - 1.5f, radius, distance);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        runtimeCircleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), size);
        runtimeCircleSprite.name = "TimedEffectRadialCircle (Runtime)";
        runtimeCircleSprite.hideFlags = HideFlags.HideAndDontSave;
        return runtimeCircleSprite;
    }
}
