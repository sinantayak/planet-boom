using System.Collections.Generic;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LevelMapNodeUI : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private Button islandButton;
    [SerializeField] private Image islandImage;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private GameObject lockOverlay;
    [SerializeField] private List<Image> stars = new();
    [SerializeField] private Sprite filledStar;
    [SerializeField] private Sprite emptyStar;
    [SerializeField] private RectTransform rewardContainer;
    [SerializeField] private Image rewardIcon;
    [SerializeField] private GameObject rewardCompleteMark;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Selected State")]
    [SerializeField] private Image selectedHighlight;
    [SerializeField] private Sprite selectedHighlightSprite;
    [SerializeField, Min(1f)] private float selectedScale = 1.12f;
    [SerializeField, Range(0f, 1f)] private float selectedHighlightOpacity = 0.72f;
    [SerializeField, Min(0f)] private float selectionAnimationDuration = 0.14f;
    [SerializeField] private Color selectedPathColor = new(0.25f, 0.78f, 1f, 1f);
    [SerializeField, Min(1f)] private float selectedIslandScale = 1.03f;

    private LevelConfiguration configuration;
    private LevelMapScreen owner;
    private bool unlocked;
    private bool isSelected;
    private Coroutine selectionRoutine;
    private Vector3 pathBaseScale = Vector3.one;
    private Vector3 islandBaseScale = Vector3.one;
    private Color pathBaseColor = Color.white;

    public RectTransform RootRect => (RectTransform)transform;
    public RectTransform PathNodeRect => levelText != null ? levelText.rectTransform.parent as RectTransform : null;
    public RectTransform IslandRect => islandImage != null ? islandImage.rectTransform : null;
    public RectTransform StarsRect => stars != null && stars.Count > 0 && stars[0] != null
        ? stars[0].rectTransform.parent as RectTransform : null;
    public RectTransform RewardBadgeRect => rewardContainer;

    public void ApplyVisualLayout(SectorMapNodeLayout layout)
    {
        if (layout == null) return;
        RectTransform root = RootRect;
        RectTransform common = root.parent as RectTransform;
        if (common == null) return;
        // Legacy layouts did not store the LevelNode root. Never manufacture/reset a root
        // transform from their default values; only a complete hierarchy capture may move it.
        if (layout.rootRect != null)
        {
            Vector2 normalized = new(
                Mathf.Clamp01(layout.normalizedRootCenter.x),
                Mathf.Clamp01(layout.normalizedRootCenter.y));
            Vector2 targetCenter = new(
                Mathf.Lerp(common.rect.xMin, common.rect.xMax, normalized.x),
                Mathf.Lerp(common.rect.yMin, common.rect.yMax, normalized.y));
            ApplyRootSnapshotAtCenter(root, common, layout.rootRect, targetCenter);
        }
        RectTransform path = PathNodeRect;
        if (layout.pathRect != null) ApplySnapshot(path, layout.pathRect);
        else if (path != null) { path.sizeDelta = layout.pathNodeSize; }
        if (layout.islandRect != null) ApplySnapshot(IslandRect, layout.islandRect);
        else ApplyRectByCenter(IslandRect, layout.islandPosition, layout.islandSize);
        if (IslandRect != null)
        {
            if (layout.islandRect == null)
            {
                IslandRect.localRotation = Quaternion.Euler(0f, 0f, layout.islandRotation);
                IslandRect.localScale = layout.islandScale;
            }
        }
        if (layout.starsRect != null) ApplySnapshot(StarsRect, layout.starsRect);
        else ApplyCenter(StarsRect, layout.starsPosition);
        if (layout.rewardBadgeRect != null) ApplySnapshot(RewardBadgeRect, layout.rewardBadgeRect);
        else ApplyRectByCenter(RewardBadgeRect, layout.rewardBadgePosition, layout.rewardBadgeSize);
        CacheSelectionBaseline();
    }

    public void BindIslandSprite(Sprite sprite)
    {
        if (islandImage == null || sprite == null) return;
        islandImage.sprite = sprite;
        islandImage.enabled = true;
        Color color = islandImage.color;
        color.a = 1f;
        islandImage.color = color;
    }

    private static void ApplyCenter(RectTransform rect, Vector2 center)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.anchoredPosition = center - (Vector2)rect.rect.center;
    }

    private static void ApplyRectByCenter(RectTransform rect, Vector2 center, Vector2 size)
    {
        if (rect == null) return;
        rect.sizeDelta = size;
        ApplyCenter(rect, center);
    }

    private static void ApplySnapshot(RectTransform rect, RectTransformLayout snapshot)
    {
        if (rect == null || snapshot == null) return;
        rect.anchorMin = snapshot.anchorMin;
        rect.anchorMax = snapshot.anchorMax;
        rect.pivot = snapshot.pivot;
        rect.anchoredPosition = snapshot.anchoredPosition;
        rect.sizeDelta = snapshot.sizeDelta;
        rect.localRotation = Quaternion.Euler(0f, 0f, snapshot.rotation);
        rect.localScale = snapshot.scale;
    }

    private static void ApplyRootSnapshotAtCenter(RectTransform root, RectTransform parent,
        RectTransformLayout snapshot, Vector2 targetCenter)
    {
        if (root == null) return;
        if (snapshot == null)
        {
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = targetCenter - parent.rect.center;
            return;
        }
        root.anchorMin = snapshot.anchorMin;
        root.anchorMax = snapshot.anchorMax;
        root.pivot = snapshot.pivot;
        root.sizeDelta = snapshot.sizeDelta;
        root.localRotation = Quaternion.Euler(0f, 0f, snapshot.rotation);
        root.localScale = snapshot.scale;
        Vector2 anchorMinPoint = new(
            Mathf.Lerp(parent.rect.xMin, parent.rect.xMax, snapshot.anchorMin.x),
            Mathf.Lerp(parent.rect.yMin, parent.rect.yMax, snapshot.anchorMin.y));
        Vector2 anchorMaxPoint = new(
            Mathf.Lerp(parent.rect.xMin, parent.rect.xMax, snapshot.anchorMax.x),
            Mathf.Lerp(parent.rect.yMin, parent.rect.yMax, snapshot.anchorMax.y));
        Vector2 anchorReference = new(
            Mathf.Lerp(anchorMinPoint.x, anchorMaxPoint.x, snapshot.pivot.x),
            Mathf.Lerp(anchorMinPoint.y, anchorMaxPoint.y, snapshot.pivot.y));
        Vector2 scaledCenterOffset = Vector2.Scale(root.rect.center, snapshot.scale);
        Vector2 rotatedCenterOffset = Quaternion.Euler(0f, 0f, snapshot.rotation) * scaledCenterOffset;
        root.anchoredPosition = targetCenter - rotatedCenterOffset - anchorReference;
    }

    public void Bind(LevelMapScreen screen, LevelConfiguration config, Sprite island, bool unlocked,
        int bestStars, LevelMapRewardIconLibrary icons)
    {
        owner = screen; configuration = config; this.unlocked = unlocked && config != null;
        BindIslandSprite(island);
        if (!isSelected) CacheSelectionBaseline();
        if (levelText != null) levelText.text = config != null ? config.levelNumber.ToString() : "—";
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
            button.interactable = unlocked && config != null;
        }
        if (islandButton != null)
        {
            islandButton.onClick.RemoveListener(HandleClick);
            islandButton.onClick.AddListener(HandleClick);
            islandButton.interactable = this.unlocked;
        }
        if (lockOverlay != null) lockOverlay.SetActive(!unlocked || config == null);
        if (canvasGroup != null) canvasGroup.alpha = unlocked && config != null ? 1f : .48f;
        for (int i = 0; i < stars.Count; i++)
        {
            if (stars[i] == null) continue;
            stars[i].sprite = i < bestStars ? filledStar : emptyStar;
            stars[i].color = config != null ? Color.white : new Color(1f, 1f, 1f, .3f);
        }
        BindReward(config, icons);
        if (!this.unlocked) SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        selected = selected && unlocked && configuration != null;
        isSelected = selected;
        if (selectionRoutine != null) StopCoroutine(selectionRoutine);
        if (!isActiveAndEnabled || selectionAnimationDuration <= 0f)
        {
            ApplySelectionVisual(selected ? 1f : 0f);
            return;
        }
        selectionRoutine = StartCoroutine(AnimateSelection(selected));
    }

    private void CacheSelectionBaseline()
    {
        RectTransform path = PathNodeRect;
        if (path != null) pathBaseScale = path.localScale;
        if (IslandRect != null) islandBaseScale = IslandRect.localScale;
        Image pathImage = path != null ? path.GetComponent<Image>() : null;
        if (pathImage != null) pathBaseColor = pathImage.color;
    }

    private IEnumerator AnimateSelection(bool selected)
    {
        float start = CurrentSelectionAmount();
        float target = selected ? 1f : 0f;
        float elapsed = 0f;
        while (elapsed < selectionAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            ApplySelectionVisual(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, elapsed / selectionAnimationDuration)));
            yield return null;
        }
        ApplySelectionVisual(target);
        selectionRoutine = null;
    }

    private float CurrentSelectionAmount()
    {
        if (selectedHighlight != null) return selectedHighlight.gameObject.activeSelf ? selectedHighlight.color.a / Mathf.Max(.001f, selectedHighlightOpacity) : 0f;
        return isSelected ? 1f : 0f;
    }

    private void ApplySelectionVisual(float amount)
    {
        RectTransform path = PathNodeRect;
        if (path != null) path.localScale = pathBaseScale * Mathf.Lerp(1f, selectedScale, amount);
        if (IslandRect != null) IslandRect.localScale = islandBaseScale * Mathf.Lerp(1f, selectedIslandScale, amount);
        Image pathImage = path != null ? path.GetComponent<Image>() : null;
        if (pathImage != null) pathImage.color = Color.Lerp(pathBaseColor, selectedPathColor, amount);
        if (selectedHighlight != null)
        {
            if (selectedHighlightSprite != null) selectedHighlight.sprite = selectedHighlightSprite;
            selectedHighlight.raycastTarget = false;
            Color color = selectedHighlight.color;
            color.a = selectedHighlightOpacity * amount;
            selectedHighlight.color = color;
            selectedHighlight.gameObject.SetActive(amount > .001f);
        }
    }

    private void BindReward(LevelConfiguration config, LevelMapRewardIconLibrary icons)
    {
        LevelUnlockReward reward = config?.unlockRewards != null && config.unlockRewards.Count > 0
            ? config.unlockRewards[0] : null;
        if (rewardContainer != null) rewardContainer.gameObject.SetActive(reward != null);
        if (reward == null) return;
        Sprite icon = icons != null ? icons.Resolve(reward) : null;
        if (rewardIcon != null)
        {
            rewardIcon.sprite = icon;
            rewardIcon.enabled = icon != null;
        }
        bool claimed = UnlockManager.Instance != null && UnlockManager.Instance.IsUnlocked(reward.CanonicalId);
        if (rewardCompleteMark != null) rewardCompleteMark.SetActive(claimed);
    }

    private void HandleClick()
    {
        if (unlocked && configuration != null) owner?.SelectLevel(configuration);
    }
}
