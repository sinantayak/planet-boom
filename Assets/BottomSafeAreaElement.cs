using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public sealed class BottomSafeAreaElement : MonoBehaviour
{
    [Tooltip("Offset in Canvas UI units from the SafeAreaRoot bottom-center edge.")]
    [SerializeField] private Vector2 safeBottomOffset = new Vector2(0f, 50f);

    private RectTransform rectTransform;
    private bool applying;

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        Apply();
    }

    private void OnEnable()
    {
        if (rectTransform == null) rectTransform = (RectTransform)transform;
        Apply();
    }

    private void OnRectTransformDimensionsChange()
    {
        if (Application.isPlaying) Apply();
    }

    private void Apply()
    {
        if (applying || rectTransform == null) return;
        applying = true;
        rectTransform.anchorMin = rectTransform.anchorMax = new Vector2(.5f, 0f);
        rectTransform.pivot = new Vector2(.5f, 0f);
        rectTransform.anchoredPosition = safeBottomOffset;
        applying = false;
    }
}
