using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

// The Settings popup (historically the "break menu"), opened by the
// top-right Settings button in GameScene. This is the game's ONE
// settings/pause popup: pausing goes through the shared
// GameManager.TryPauseForBreakMenu gate — the same gate that keeps the
// Evolution Popup, Pre-Level and the skill inventory from ever stacking —
// and gameplay resumes only in the close animation's completion callback.
public sealed class BreakMenuUI : MonoBehaviour
{
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private Button pauseButton;

    [Header("Preference Toggles")]
    [SerializeField] private Button soundToggle;
    [SerializeField] private Image soundImage;
    [SerializeField] private Button musicToggle;
    [SerializeField] private Image musicImage;
    [SerializeField] private Button vibrationToggle;
    [SerializeField] private Image vibrationImage;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;

    [Header("Action Buttons")]
    // Abandons the current run (no rewards committed) back to the LevelMap.
    [FormerlySerializedAs("homeButton")]
    [SerializeField] private Button exitButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button languageButton;
    // The language button's own label — always shows the CURRENT selection.
    [SerializeField] private TMP_Text languageLabel;
    [SerializeField] private string englishLabel = "ENGLISH";
    [SerializeField] private string turkishLabel = "TURKISH";
    [SerializeField] private Button supportButton;
    // Future support destination (https:// or mailto:). While empty the
    // button is a safe no-op placeholder — nothing opens, nothing throws.
    [SerializeField] private string supportUrl = "";

    [Header("Remove Ads (placeholder)")]
    // Presentation only until IAP integration lands: the click just logs.
    [SerializeField] private Button removeAdsButton;

    [SerializeField] private Button continueButton;

    private void Awake()
    {
        pauseButton?.onClick.AddListener(Open);
        soundToggle?.onClick.AddListener(ToggleSound);
        musicToggle?.onClick.AddListener(ToggleMusic);
        vibrationToggle?.onClick.AddListener(ToggleVibration);
        languageButton?.onClick.AddListener(ToggleLanguage);
        supportButton?.onClick.AddListener(OpenSupport);
        exitButton?.onClick.AddListener(Exit);
        restartButton?.onClick.AddListener(Restart);
        removeAdsButton?.onClick.AddListener(RequestRemoveAds);
        continueButton?.onClick.AddListener(Continue);
        if (popupRoot != null) popupRoot.SetActive(false);
        ApplyPreferences();
        RefreshLanguageLabel();
    }

    public void Open()
    {
        if (popupRoot == null || popupRoot.activeSelf || GameManager.Instance == null) return;
        if (!GameManager.Instance.TryPauseForBreakMenu()) return;
        PopupTransition.Open(popupRoot);
        popupRoot.transform.SetAsLastSibling();
        RefreshToggleVisuals();
        RefreshLanguageLabel();
    }

    // The game stays GamePaused (timeScale 0) for the whole unscaled close
    // animation; gameplay only resumes once the popup has visually finished
    // closing. Repeated taps are safe: PopupTransition ignores a second close
    // and the popup's buttons are non-interactable while it transitions.
    public void Continue()
    {
        if (GameManager.Instance == null ||
            GameManager.Instance.State != GameManager.GameState.GamePaused) return;
        PopupTransition.Close(popupRoot,
            () => GameManager.Instance?.TryResumeFromBreakMenu());
    }

    // Restarts the SAME active level through the shared reload path, so the
    // normal prepared flow (Pre-Level → boosters → READY → intro) runs again.
    public void Restart()
    {
        PopupTransition.Close(popupRoot,
            () => GameManager.Instance?.RestartCurrentLevel());
    }

    // Abandon the run: no rewards committed, no progression granted, back to
    // the LevelMap through the shared SceneTransition fade.
    public void Exit()
    {
        PopupTransition.Close(popupRoot,
            () => GameManager.Instance?.AbandonRunToLevelMap());
    }

    private void ToggleSound() { GameSettings.SfxEnabled = !GameSettings.SfxEnabled; ApplyPreferences(); }
    private void ToggleMusic() { GameSettings.MusicEnabled = !GameSettings.MusicEnabled; ApplyPreferences(); }
    private void ToggleVibration() { GameSettings.VibrationEnabled = !GameSettings.VibrationEnabled; RefreshToggleVisuals(); }

    // Only the persisted preference and this button's label change — actual
    // translation is the future localization system's job.
    private void ToggleLanguage()
    {
        GameSettings.Language = GameSettings.Language == GameLanguage.English
            ? GameLanguage.Turkish
            : GameLanguage.English;
        RefreshLanguageLabel();
    }

    private void OpenSupport()
    {
        if (string.IsNullOrWhiteSpace(supportUrl))
        {
            Debug.Log("BreakMenuUI: support destination is not configured yet.", this);
            return;
        }
        Application.OpenURL(supportUrl.Trim());
    }

    private void RequestRemoveAds()
    {
        Debug.Log("BreakMenuUI: Remove Ads purchasing is not implemented yet (presentation only).", this);
    }

    private void ApplyPreferences()
    {
        AudioManager.Instance?.ApplySavedPreferences();
        RefreshToggleVisuals();
    }

    private void RefreshToggleVisuals()
    {
        SetToggle(soundImage, GameSettings.SfxEnabled);
        SetToggle(musicImage, GameSettings.MusicEnabled);
        SetToggle(vibrationImage, GameSettings.VibrationEnabled);
    }

    private void RefreshLanguageLabel()
    {
        if (languageLabel != null)
            languageLabel.text = GameSettings.Language == GameLanguage.Turkish ? turkishLabel : englishLabel;
    }

    private void SetToggle(Image image, bool on)
    {
        if (image != null) image.sprite = on ? onSprite : offSprite;
    }
}
