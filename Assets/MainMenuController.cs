using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Owns every OnClick() handler on the Main Menu scene's buttons: scene
// transitions (Play/Quit), submenu stubs (Shop/Options), and the two
// persistent Audio/Vibe toggles. Toggle state lives in PlayerPrefs so it
// survives between sessions; both toggles snap to their saved state in
// Start() so the button sprites are correct the instant the menu loads, not
// just after the player clicks them.
public class MainMenuController : MonoBehaviour
{
    // Shared with AudioManager.Awake, which re-applies this same key every
    // time a new scene (and therefore a new AudioManager instance) loads —
    // keep the two literals in sync if this ever changes.
    private const string MuteStateKey = "MuteState"; // 1 = audio ON, 0 = OFF
    private const string VibeStateKey = "VibeState"; // 1 = vibration ON, 0 = OFF

    [Header("Scene Transition")]
    // Must match a scene name added to Build Settings. Kept as a plain
    // string (not a build index) so re-ordering scenes in Build Settings
    // can't silently repoint this at the wrong scene.
    [SerializeField] private string gameplaySceneName = "LevelMap";

    [Header("Audio Toggle")]
    [SerializeField] private Image audioButtonImage;
    [SerializeField] private Sprite audioOnSprite;
    [SerializeField] private Sprite audioOffSprite;

    [Header("Vibration Toggle (future prep)")]
    // No haptics call exists yet — this only persists the preference and
    // updates the button art so the feature can be wired in later without
    // touching this script again.
    [SerializeField] private Image vibeButtonImage;
    [SerializeField] private Sprite vibeOnSprite;
    [SerializeField] private Sprite vibeOffSprite;

    private bool audioOn;
    private bool vibeOn;

    void Start()
    {
        // Default to ON when no key is saved yet (first-ever launch);
        // GetInt's default only applies while the key is absent.
        audioOn = GameSettings.SfxEnabled;
        vibeOn = GameSettings.VibrationEnabled;

        // save:false — just sync visuals/AudioManager to the existing saved
        // value, don't re-write PlayerPrefs on every menu visit.
        ApplyAudioState(audioOn, save: false);
        RefreshVibeVisual();
    }

    // ---- Scene Transitions ----

    // Wired to the Start button. Loads the gameplay scene by name; swap
    // gameplaySceneName in the Inspector if a dedicated level-map scene
    // replaces SampleScene later.
    public void PlayGame()
    {
        SceneTransition.LoadScene(gameplaySceneName);
    }

    // Wired to the Exit button. Application.Quit() is a no-op in the Editor,
    // so playmode is stopped directly there instead.
    public void QuitGame()
    {
        Debug.Log("MainMenuController: quitting application.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---- Submenus (stubs — wire to real popups later) ----

    public void OpenShop()
    {
        Debug.Log("MainMenuController: OpenShop() called — wire up the Shop panel here.");
    }

    public void OpenOptions()
    {
        Debug.Log("MainMenuController: OpenOptions() called — wire up the Options panel here.");
    }

    // ---- Audio Toggle ----

    // Wired to the Audio toggle button.
    public void ToggleAudio()
    {
        ApplyAudioState(!audioOn, save: true);
    }

    private void ApplyAudioState(bool on, bool save)
    {
        audioOn = on;

        if (save)
        {
            GameSettings.SfxEnabled = audioOn;
        }

        if (audioButtonImage != null)
        {
            Sprite sprite = audioOn ? audioOnSprite : audioOffSprite;
            if (sprite != null)
            {
                audioButtonImage.sprite = sprite;
            }
        }

        // Instant, not just persisted: AudioManager.SetMuted flips
        // AudioListener.volume immediately so the player hears the change
        // on the same click, without waiting for a scene reload.
        if (AudioManager.Instance != null)
        {
            AudioManager.Instance.SetMuted(!audioOn);
        }
    }

    // ---- Vibration Toggle (future prep) ----

    // Wired to the Vibe toggle button.
    public void ToggleVibe()
    {
        vibeOn = !vibeOn;
        GameSettings.VibrationEnabled = vibeOn;
        RefreshVibeVisual();
    }

    private void RefreshVibeVisual()
    {
        if (vibeButtonImage == null)
            return;

        Sprite sprite = vibeOn ? vibeOnSprite : vibeOffSprite;
        if (sprite != null)
        {
            vibeButtonImage.sprite = sprite;
        }
    }
}
