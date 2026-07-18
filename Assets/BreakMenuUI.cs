using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BreakMenuUI : MonoBehaviour
{
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private Button pauseButton;
    [SerializeField] private Button soundToggle;
    [SerializeField] private Image soundImage;
    [SerializeField] private Button musicToggle;
    [SerializeField] private Image musicImage;
    [SerializeField] private Button vibrationToggle;
    [SerializeField] private Image vibrationImage;
    [SerializeField] private Sprite onSprite;
    [SerializeField] private Sprite offSprite;
    [SerializeField] private Button homeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button continueButton;

    private void Awake()
    {
        pauseButton?.onClick.AddListener(Open);
        soundToggle?.onClick.AddListener(ToggleSound);
        musicToggle?.onClick.AddListener(ToggleMusic);
        vibrationToggle?.onClick.AddListener(ToggleVibration);
        homeButton?.onClick.AddListener(Home);
        restartButton?.onClick.AddListener(Restart);
        continueButton?.onClick.AddListener(Continue);
        if (popupRoot != null) popupRoot.SetActive(false);
        ApplyPreferences();
    }

    public void Open()
    {
        if (popupRoot == null || popupRoot.activeSelf || GameManager.Instance == null) return;
        if (!GameManager.Instance.TryPauseForBreakMenu()) return;
        popupRoot.SetActive(true);
        popupRoot.transform.SetAsLastSibling();
        RefreshToggleVisuals();
    }

    public void Continue()
    {
        if (GameManager.Instance == null || !GameManager.Instance.TryResumeFromBreakMenu()) return;
        popupRoot?.SetActive(false);
    }

    public void Restart()
    {
        popupRoot?.SetActive(false);
        GameManager.Instance?.RestartCurrentLevel();
    }

    public void Home()
    {
        popupRoot?.SetActive(false);
        GameManager.Instance?.AbandonRunToMainMenu();
    }

    private void ToggleSound() { GameSettings.SfxEnabled = !GameSettings.SfxEnabled; ApplyPreferences(); }
    private void ToggleMusic() { GameSettings.MusicEnabled = !GameSettings.MusicEnabled; ApplyPreferences(); }
    private void ToggleVibration() { GameSettings.VibrationEnabled = !GameSettings.VibrationEnabled; RefreshToggleVisuals(); }

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

    private void SetToggle(Image image, bool on)
    {
        if (image != null) image.sprite = on ? onSprite : offSprite;
    }
}
