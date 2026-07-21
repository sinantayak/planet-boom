using System.Collections;
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

    // NEXT advances to the following level, BACK returns to the one before
    // it, RESTART replays the level the player just completed (full reset
    // back to level 1 belongs to the Game Over panel, not this popup). All
    // three are wired in code in Awake (to GameManager.AdvanceToNextLevel,
    // GameManager.ReturnToPreviousLevel, and GameManager.ReplayCurrentLevel
    // respectively), so no manual OnClick setup is needed — extra OnClick
    // entries added in the Inspector (e.g. a click sound) are harmless
    // alongside the code wiring.
    [SerializeField] private Button nextButton;
    [SerializeField] private Button backButton;
    [SerializeField] private Button restartButton;

    [Header("Star Rating")]
    // The 3 header star Images, left to right. Show() fills the first
    // starsEarned of them and grays out the rest.
    [SerializeField] private Image[] starSlots = new Image[3];
    [SerializeField] private Sprite starFilledSprite;
    [SerializeField] private Sprite starEmptySprite;

    [Header("Space Coin Reward")]
    // Placeholder economy texts, hardcoded in Show() until the scoring and
    // currency systems exist — they're here so the panel layout and font can
    // be art-reviewed with realistic values on screen.
    [SerializeField] private TextMeshProUGUI coinRewardText;

    [Header("Space Coin Balance Count")]
    [SerializeField] private AudioClip coinCountClip;
    [SerializeField, Range(0f, 1f)] private float coinCountVolume = 0.8f;
    [SerializeField, Min(0.005f)] private float secondsPerCoin = 0.04f;
    [SerializeField, Min(0.1f)] private float maximumCountDuration = 2.5f;
    [SerializeField, Min(1)] private int soundEveryCoins = 1;

    // Banner wording; swap to "SUCCESS" here or in the Inspector if preferred.
    [SerializeField] private string titleMessage = "LEVEL COMPLETED";

    [Header("Vortex Reveal")]
    // Seconds for the popup to fly/scale out of the black hole core when
    // shown via ShowFromWorldPoint (the win-vortex path). Plain Show() stays
    // instant for any other caller.
    [SerializeField] private float popDuration = 0.45f;

    private RectTransform rectTransform;

    // The panel's authored resting place, captured once on first activation;
    // every reveal animation ends exactly here regardless of where the black
    // hole was.
    private Vector2 homeAnchoredPosition;
    private bool homeCaptured;
    private Coroutine coinCountRoutine;
    // Optional presentation layer on this same root; absent, the panel keeps
    // its original instant show/hide behavior.
    private PopupTransition transition;

    void Awake()
    {
        transition = GetComponent<PopupTransition>();
        rectTransform = transform as RectTransform;
        if (rectTransform != null && !homeCaptured)
        {
            homeAnchoredPosition = rectTransform.anchoredPosition;
            homeCaptured = true;
        }

        if (nextButton != null)
        {
            nextButton.onClick.AddListener(OnNextClicked);
        }
        else
        {
            Debug.LogWarning("LevelCompletePanel: no NEXT button assigned — the popup can't advance the game.", this);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(OnBackClicked);
        }
        else
        {
            Debug.LogWarning("LevelCompletePanel: no BACK button assigned.", this);
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
    public void Show(int starsEarned) => Show(starsEarned, true);

    // animateTransition false = the caller owns the entrance (the win-vortex
    // bloom); the popup is still marked open so its close stays animated.
    private void Show(int starsEarned, bool animateTransition)
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

        long startingBalance = PlayerDataPersistenceManager.Instance != null
            ? PlayerDataPersistenceManager.Instance.SpaceCoin
            : 0;
        long endingBalance = startingBalance;
        GameManager manager = GameManager.Instance;
        if (manager != null)
        {
            if (manager.IsLevelRewardCommitted)
            {
                endingBalance = PlayerDataPersistenceManager.Instance != null
                    ? PlayerDataPersistenceManager.Instance.SpaceCoin
                    : startingBalance;
                startingBalance = System.Math.Max(0L, endingBalance - manager.LastCommittedLevelReward);
            }
            else if (!manager.TryCommitConfiguredLevelReward(out long committedReward))
                Debug.LogError("LevelCompletePanel: Space Coin reward could not be committed.", this);
            else
                endingBalance = PlayerDataPersistenceManager.Instance.SpaceCoin;
        }

        if (coinRewardText != null)
            coinRewardText.text = startingBalance.ToString();

        gameObject.SetActive(true);

        // A previous reveal may have been cut short by Hide() (disabling the
        // object kills its coroutines mid-flight); snap back to the resting
        // pose so no show can inherit a half-animated scale or position.
        // Runs after SetActive so Awake (which captures the pose on the very
        // first activation) has definitely executed.
        if (homeCaptured && rectTransform != null)
        {
            rectTransform.anchoredPosition = homeAnchoredPosition;
            rectTransform.localScale = Vector3.one;
        }

        if (transition != null)
        {
            if (animateTransition)
                transition.OpenAnimated();
            else
                transition.SetOpenInstant();
        }

        if (coinCountRoutine != null)
            StopCoroutine(coinCountRoutine);
        coinCountRoutine = StartCoroutine(CountBalance(startingBalance, endingBalance));
    }

    private IEnumerator CountBalance(long from, long to)
    {
        if (coinRewardText == null || to <= from)
        {
            if (coinRewardText != null) coinRewardText.text = to.ToString();
            coinCountRoutine = null;
            yield break;
        }

        long difference = to - from;
        float interval = Mathf.Min(secondsPerCoin,
            maximumCountDuration / Mathf.Max(1f, difference));
        int soundStep = Mathf.Max(1, soundEveryCoins);
        long value = from;
        while (value < to)
        {
            value++;
            coinRewardText.text = value.ToString();
            if ((value - from) % soundStep == 0 && AudioManager.Instance != null)
                AudioManager.Instance.PlayUiOneShot(coinCountClip, coinCountVolume);
            yield return new WaitForSecondsRealtime(interval);
        }

        coinRewardText.text = to.ToString();
        coinCountRoutine = null;
    }

    // The win-vortex reveal: same content as Show, but the panel blooms out
    // of the given world point (the black hole core) — starting at zero
    // scale on top of it and easing up/over to its authored spot.
    public void ShowFromWorldPoint(int starsEarned, Vector3 worldPoint)
    {
        Show(starsEarned, false);

        if (rectTransform == null)
            return;

        // StartCoroutine is legal here because Show just activated us.
        StartCoroutine(PopFromPoint(AnchoredPointForWorld(worldPoint)));
    }

    private IEnumerator PopFromPoint(Vector2 startPoint)
    {
        rectTransform.anchoredPosition = startPoint;
        rectTransform.localScale = Vector3.zero;

        float elapsed = 0f;
        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popDuration);
            float eased = t * t * (3f - 2f * t); // smoothstep

            rectTransform.anchoredPosition = Vector2.Lerp(startPoint, homeAnchoredPosition, eased);
            rectTransform.localScale = Vector3.one * eased;
            yield return null;
        }

        rectTransform.anchoredPosition = homeAnchoredPosition;
        rectTransform.localScale = Vector3.one;
    }

    // Converts a world-space point (the black hole core) into this panel's
    // parent-relative anchored space, handling both Screen Space - Overlay
    // and camera-driven canvases. Falls back to the resting position if
    // anything needed for the conversion is missing.
    private Vector2 AnchoredPointForWorld(Vector3 worldPoint)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        Camera worldCamera = Camera.main;
        RectTransform parentRect = rectTransform.parent as RectTransform;

        if (canvas == null || worldCamera == null || parentRect == null)
            return homeAnchoredPosition;

        Vector2 screenPoint = worldCamera.WorldToScreenPoint(worldPoint);
        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : canvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentRect, screenPoint, uiCamera, out Vector2 localPoint)
            ? localPoint
            : homeAnchoredPosition;
    }

    public void Hide()
    {
        // Animated only when the panel was actually shown this session;
        // defensive startup hides still snap instantly (PopupTransition
        // tracks that itself), and a missing component falls back to the
        // original SetActive(false).
        PopupTransition.Close(gameObject);
    }

    private void OnDisable()
    {
        coinCountRoutine = null;
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

    private void OnBackClicked()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.ReturnToPreviousLevel();
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
