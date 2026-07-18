using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LevelMapNodeUI : MonoBehaviour
{
    [SerializeField] private Button button;
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

    private LevelConfiguration configuration;
    private LevelMapScreen owner;

    public void Bind(LevelMapScreen screen, LevelConfiguration config, Sprite island, bool unlocked,
        int bestStars, LevelMapRewardIconLibrary icons)
    {
        owner = screen; configuration = config;
        if (islandImage != null) islandImage.sprite = island;
        if (levelText != null) levelText.text = config != null ? config.levelNumber.ToString() : "—";
        if (button != null)
        {
            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
            button.interactable = unlocked && config != null;
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
    }

    public void SetSelected(bool selected)
    {
        if (canvasGroup != null && configuration != null) canvasGroup.alpha = selected ? .78f : 1f;
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
        if (configuration != null) owner?.SelectLevel(configuration);
    }
}
