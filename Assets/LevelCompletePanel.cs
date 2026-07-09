using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The centered win popup (stone-themed panel asset). Hidden during normal
// play; GameManager shows it — with the earned star count — when every
// mission target is fulfilled, and hides it again when the player advances
// or restarts. Completely independent of MissionHUD: the HUD narrates live
// progress, this panel only celebrates the finish.
//
// Put this component on the panel's ROOT object: Show/Hide toggle that same
// GameObject, so everything under it (banner, stars, buttons, frame art)
// follows.
public class LevelCompletePanel : MonoBehaviour
{
    [Header("Wiring")]
    // The banner TextMeshPro on the stone panel; stamped with titleMessage
    // every time the popup opens.
    [SerializeField] private TextMeshProUGUI titleText;

    // NEXT advances to the following level, RESTART replays the level the
    // player just completed (full reset back to level 1 belongs to the Game
    // Over panel, not this popup). Both are wired in code in Awake (to
    // GameManager.AdvanceToNextLevel and GameManager.ReplayCurrentLevel
    // respectively), so no manual OnClick setup is needed — extra OnClick
    // entries added in the Inspector (e.g. a click sound) are harmless
    // alongside the code wiring.
    [SerializeField] private Button nextButton;
    [SerializeField] private Button restartButton;

    [Header("Star Rating")]
    // The 3 header star Images, left to right. Show() fills the first
    // starsEarned of them and grays out the rest.
    [SerializeField] private Image[] starSlots = new Image[3];
    [SerializeField] private Sprite starFilledSprite;
    [SerializeField] private Sprite starEmptySprite;

    [Header("Reward Readouts (mock)")]
    // Placeholder economy texts, hardcoded in Show() until the scoring and
    // currency systems exist — they're here so the panel layout and font can
    // be art-reviewed with realistic values on screen.
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI gemText;

    // Banner wording; swap to "SUCCESS" here or in the Inspector if preferred.
    [SerializeField] private string titleMessage = "LEVEL COMPLETED";

    void Awake()
    {
        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextClicked);
        }
        else
        {
            Debug.LogWarning("LevelCompletePanel: no NEXT button assigned — the popup can't advance the game.", this);
        }

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(OnRestartClicked);
        }
        else
        {
            Debug.LogWarning("LevelCompletePanel: no RESTART button assigned.", this);
        }
    }

    // Opens the popup showing starsEarned (1..3) filled stars. Star sprites
    // are assigned every time, so a 3-star win after a 1-star win can't
    // inherit stale gray sprites from the previous showing.
    public void Show(int starsEarned)
    {
        if (titleText != null)
        {
            titleText.text = titleMessage;
        }

        for (int i = 0; i < starSlots.Length; i++)
        {
            Image star = starSlots[i];
            if (star == null)
                continue;

            star.sprite = i < starsEarned ? starFilledSprite : starEmptySprite;
        }

        // Mock values until real scoring/currency arrive.
        if (scoreText != null)
        {
            scoreText.text = "SCORE: 3500";
        }
        if (goldText != null)
        {
            goldText.text = "500";
        }
        if (gemText != null)
        {
            gemText.text = "350";
        }

        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    // Both handlers just forward to GameManager, which owns every progression
    // side effect (board clear, launcher queue, level index, HUD refresh,
    // timer reset, unfreeze). Both targets are state-guarded to
    // LevelComplete, so double-clicks or a click racing the other button
    // can't fire twice.
    private void OnNextClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.AdvanceToNextLevel();
        }
    }

    private void OnRestartClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReplayCurrentLevel();
        }
    }
}
