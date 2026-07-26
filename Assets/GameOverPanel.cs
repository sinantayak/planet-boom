using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Presentation/controller adapter for GameManager's existing GameOver state.
// It owns no timer, board, progression, life storage or ad SDK.
public sealed class GameOverPanel : MonoBehaviour
{
    [Header("Popup")]
    [SerializeField] private GameObject popupRoot;

    [Header("Localized Text")]
    [SerializeField] private TextMeshProUGUI gameOverText;
    [SerializeField] private TextMeshProUGUI keepProgressText;
    [SerializeField] private TextMeshProUGUI orText;
    [SerializeField] private TextMeshProUGUI tryAgainText;

    [Header("Actions")]
    [SerializeField] private Button continueWithHealthButton;
    [SerializeField] private Button continueWithAdsButton;
    [SerializeField] private Button tryAgainButton;
    [SerializeField, Min(1)] private int healthContinueLifeCost = 1;

    [Header("Rewarded Ad Testing")]
    [Tooltip("Editor/Development only. Release builds still require a registered real provider.")]
    [SerializeField] private bool simulateRewardedSuccess = true;

    private bool actionPending;
    private int adRequestToken;
    private GameObject overlay;

    private void Awake()
    {
        if (popupRoot == null)
            popupRoot = gameObject;
        Transform overlayTransform = popupRoot.transform.Find("Overlay");
        if (overlayTransform != null)
            overlay = overlayTransform.gameObject;
        continueWithHealthButton?.onClick.AddListener(
            HandleContinueWithHealth);
        continueWithAdsButton?.onClick.AddListener(HandleContinueWithAds);
        tryAgainButton?.onClick.AddListener(HandleTryAgain);
    }

    private void OnEnable()
    {
        // Overlay belongs to the popup, not to the always-active hierarchy
        // container. Ensure it returns together with the panel even if it was
        // temporarily hidden while the authored layout was being adjusted.
        if (overlay != null)
        {
            overlay.SetActive(true);
            Graphic overlayGraphic = overlay.GetComponent<Graphic>();
            if (overlayGraphic != null)
                overlayGraphic.enabled = true;
        }

        Localization.LanguageChanged += Refresh;
        PlayerDataPersistenceManager.LivesChanged += HandleLivesChanged;
        actionPending = false;
        Refresh();
    }

    private void OnDisable()
    {
        Localization.LanguageChanged -= Refresh;
        PlayerDataPersistenceManager.LivesChanged -= HandleLivesChanged;
    }

    private void OnDestroy()
    {
        continueWithHealthButton?.onClick.RemoveListener(
            HandleContinueWithHealth);
        continueWithAdsButton?.onClick.RemoveListener(HandleContinueWithAds);
        tryAgainButton?.onClick.RemoveListener(HandleTryAgain);
        adRequestToken++;
    }

    private void HandleLivesChanged(int _) => Refresh();

    private void Refresh()
    {
        GameManager manager = GameManager.Instance;
        PlayerDataPersistenceManager data = PlayerDataPersistenceManager.Instance;

        if (gameOverText != null)
            gameOverText.text = Localization.Get("gameover.title");
        if (keepProgressText != null)
            keepProgressText.text =
                Localization.Get("gameover.continue_description");
        if (orText != null)
            orText.text = Localization.Get("gameover.or");
        if (tryAgainText != null)
            tryAgainText.text = Localization.Get("gameover.try_again");

        bool isGameOver = !actionPending && manager != null &&
            manager.State == GameManager.GameState.GameOver;
        bool canContinueRun = isGameOver &&
            manager.CanUseRewardedContinue;
        bool canUseHealth = canContinueRun &&
            data != null && data.IsLoaded &&
            data.Lives >= Mathf.Max(1, healthContinueLifeCost);
        bool canUseAds = canContinueRun &&
            RewardedAdGateway.IsAvailable(simulateRewardedSuccess);

        if (continueWithHealthButton != null)
            continueWithHealthButton.interactable = canUseHealth;
        if (continueWithAdsButton != null)
            continueWithAdsButton.interactable = canUseAds;
        if (tryAgainButton != null)
            tryAgainButton.interactable = isGameOver;
    }

    private void HandleContinueWithHealth()
    {
        if (actionPending)
            return;
        GameManager manager = GameManager.Instance;
        PlayerDataPersistenceManager data = PlayerDataPersistenceManager.Instance;
        int cost = Mathf.Max(1, healthContinueLifeCost);
        if (manager == null || !manager.CanUseRewardedContinue ||
            data == null || !data.TrySpendLives(cost))
        {
            UiSounds.Play(UiSoundType.Error);
            Refresh();
            return;
        }

        actionPending = true;
        Refresh();
        ResumeCurrentRun(manager);
    }

    private void HandleTryAgain()
    {
        if (actionPending)
            return;
        GameManager manager = GameManager.Instance;
        if (manager == null ||
            manager.State != GameManager.GameState.GameOver)
        {
            UiSounds.Play(UiSoundType.Error);
            Refresh();
            return;
        }

        actionPending = true;
        Refresh();
        PopupTransition.Close(popupRoot, () =>
        {
            if (manager != null)
                manager.RestartGame();
        });
    }

    private void HandleContinueWithAds()
    {
        if (actionPending)
            return;
        GameManager manager = GameManager.Instance;
        if (manager == null || !manager.CanUseRewardedContinue ||
            !RewardedAdGateway.IsAvailable(simulateRewardedSuccess))
        {
            UiSounds.Play(UiSoundType.Error);
            Refresh();
            return;
        }

        actionPending = true;
        Refresh();
        int token = ++adRequestToken;
        RewardedAdGateway.Show(simulateRewardedSuccess,
            () => HandleRewardEarned(manager, token),
            () => HandleRewardFailed(token));
    }

    private void HandleRewardEarned(GameManager manager, int token)
    {
        if (token != adRequestToken || manager == null ||
            !manager.CanUseRewardedContinue)
        {
            HandleRewardFailed(token);
            return;
        }

        ResumeCurrentRun(manager);
    }

    private void ResumeCurrentRun(GameManager manager)
    {
        float seconds = manager.ActiveRewardedContinueSeconds;
        PopupTransition.Close(popupRoot, () =>
        {
            if (!manager.TryResumeRewardedContinue(seconds))
            {
                actionPending = false;
                PopupTransition.Open(popupRoot);
                UiSounds.Play(UiSoundType.Error);
            }
        });
    }

    private void HandleRewardFailed(int token)
    {
        if (token != adRequestToken)
            return;
        actionPending = false;
        UiSounds.Play(UiSoundType.Error);
        Refresh();
    }
}
