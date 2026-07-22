using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// The one shared scene-change flow: block input → fade to black → optional
// whoosh → load the target scene → fade back in → restore input. Navigation
// controllers just call SceneTransition.LoadScene("LevelMap") and own no
// fade logic themselves; WHICH scene an action targets never changes here,
// only how the change is presented.
//
// The overlay is a single self-created DontDestroyOnLoad object (top-most
// sorting order, full-screen raycast-blocking Image), so it survives the
// load, can never duplicate, and can never strand a black screen — the same
// coroutine that covered the screen always uncovers it, and an invalid scene
// name fades back to a usable state. Everything runs on unscaled time
// because several paths leave scenes that sit at timeScale 0. Popups
// (PopupTransition) are untouched: this canvas renders above them and only
// exists during a scene change or the startup fade.
public sealed class SceneTransition : MonoBehaviour
{
    private const float DefaultFadeOut = 0.22f;
    private const float DefaultHold = 0.05f;
    private const float DefaultFadeIn = 0.28f;

    private static SceneTransition instance;

    public static bool IsTransitioning { get; private set; }

    private Image fadeImage;
    private AudioSource sfxSource;

    // Rapid double taps and simultaneous requests are absorbed here: while a
    // transition is running every further request is ignored, so exactly one
    // scene load can be in flight.
    public static void LoadScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName) || IsTransitioning)
            return;
        IsTransitioning = true;
        SceneTransition runner = EnsureInstance();
        runner.StartCoroutine(runner.RunTransition(sceneName));
    }

    // Initial launch: start behind black and reveal the first scene (MainMenu
    // in a real build). Runs once per play session, never on later loads.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void PlayStartupFadeIn()
    {
        SceneTransitionSettings settings = SceneTransitionSettings.Active;
        if (settings != null && !settings.startupFadeIn)
            return;
        if (IsTransitioning)
            return;
        IsTransitioning = true;
        SceneTransition runner = EnsureInstance();
        runner.StartCoroutine(runner.RunStartupFadeIn());
    }

    private static SceneTransition EnsureInstance()
    {
        if (instance != null)
            return instance;

        GameObject host = new GameObject("SceneTransitionOverlay");
        DontDestroyOnLoad(host);
        Canvas canvas = host.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue; // above every scene canvas and popup
        host.AddComponent<GraphicRaycaster>(); // required for the Image to swallow input

        instance = host.AddComponent<SceneTransition>();
        instance.fadeImage = host.AddComponent<Image>();
        instance.fadeImage.color = new Color(0f, 0f, 0f, 0f);
        instance.fadeImage.enabled = false;
        instance.fadeImage.raycastTarget = false;

        // Own persistent source: a clip played here survives the scene load,
        // which would cut anything routed through the scene-local AudioManager.
        instance.sfxSource = host.AddComponent<AudioSource>();
        instance.sfxSource.playOnAwake = false;
        return instance;
    }

    private IEnumerator RunTransition(string sceneName)
    {
        SceneTransitionSettings settings = SceneTransitionSettings.Active;
        float fadeOut = settings != null ? settings.fadeOutDuration : DefaultFadeOut;
        float hold = settings != null ? settings.holdDuration : DefaultHold;
        float fadeIn = settings != null ? settings.fadeInDuration : DefaultFadeIn;

        // Same saved SFX preference the AudioManager honours; missing clip is
        // simply silent.
        if (settings != null && settings.transitionClip != null && GameSettings.SfxEnabled)
            sfxSource.PlayOneShot(settings.transitionClip, Mathf.Clamp01(settings.sfxVolume));

        yield return Fade(0f, 1f, fadeOut);
        if (hold > 0f)
            yield return new WaitForSecondsRealtime(hold);

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
        if (load == null)
        {
            // Unknown/unbuilt scene: uncover the current scene instead of
            // stranding the player behind a black screen.
            Debug.LogError($"SceneTransition: scene '{sceneName}' failed to load; restoring input.");
            yield return Fade(1f, 0f, fadeIn);
            FinishTransition();
            yield break;
        }
        while (!load.isDone)
            yield return null;

        yield return Fade(1f, 0f, fadeIn);
        FinishTransition();
    }

    private IEnumerator RunStartupFadeIn()
    {
        SceneTransitionSettings settings = SceneTransitionSettings.Active;
        float fadeIn = settings != null ? settings.fadeInDuration : DefaultFadeIn;
        SetAlpha(1f);
        yield return Fade(1f, 0f, fadeIn);
        FinishTransition();
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        SetAlpha(from);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration)));
            yield return null;
        }
        SetAlpha(to);
    }

    // The overlay blocks input the entire time it is visible — from the very
    // first fade-out frame until the fade-in completes.
    private void SetAlpha(float alpha)
    {
        fadeImage.enabled = true;
        fadeImage.raycastTarget = true;
        Color color = fadeImage.color;
        color.a = alpha;
        fadeImage.color = color;
    }

    private void FinishTransition()
    {
        fadeImage.enabled = false;
        fadeImage.raycastTarget = false;
        IsTransitioning = false;
    }
}
