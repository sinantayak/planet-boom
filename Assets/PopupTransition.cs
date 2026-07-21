using System;
using System.Collections;
using UnityEngine;

// Reusable open/close presentation for popup roots. Attach it to the popup's
// root GameObject: OpenAnimated pops/fades the popup in, CloseAnimated pops/
// fades it out and only deactivates the GameObject once the transition has
// visually finished (then fires the caller's callback). Owners keep every
// gameplay decision — this component never touches game state, pause state,
// or navigation; it is presentation only.
//
// All animation runs on unscaled time because most popups live under
// Time.timeScale = 0 (LevelPreparing, pause, inventory, game over). The
// authored Inspector scale and CanvasGroup values are captured once and
// always restored: the transition animates relative to them and never
// permanently overwrites manually authored layout.
[DisallowMultipleComponent]
public sealed class PopupTransition : MonoBehaviour
{
    [Header("Open")]
    [SerializeField, Min(0.05f)] private float openDuration = 0.2f;
    [SerializeField, Range(0.5f, 1f)] private float startScaleMultiplier = 0.88f;
    // 0 = plain ease-out; higher values overshoot past the authored scale
    // before settling (1.5 ≈ a subtle mobile-style pop).
    [SerializeField, Range(0f, 3f)] private float overshootStrength = 1.5f;

    [Header("Close")]
    [SerializeField, Min(0.05f)] private float closeDuration = 0.13f;
    [SerializeField, Range(0.5f, 1f)] private float closeEndScaleMultiplier = 0.9f;

    [Header("Fade")]
    [SerializeField] private bool fadeEnabled = true;

    [Header("SFX (optional)")]
    [SerializeField] private AudioClip openClip;
    [SerializeField] private AudioClip closeClip;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;

    private CanvasGroup canvasGroup;
    private Vector3 authoredScale = Vector3.one;
    private float authoredAlpha = 1f;
    private bool authoredInteractable = true;
    private bool authoredBlocksRaycasts = true;
    private bool initialized;
    private bool hasOpened;
    private bool opening;
    private bool closing;
    private Coroutine transitionRoutine;
    private Action pendingCloseCallback;

    public bool IsTransitioning => opening || closing;

    // Owner-side helpers: popups that carry the component animate, anything
    // else falls back to the original instant SetActive behavior, so callers
    // never need to branch (and future popups opt in just by adding the
    // component to their root).
    public static void Open(GameObject root)
    {
        if (root == null)
            return;
        PopupTransition transition = root.GetComponent<PopupTransition>();
        if (transition != null)
            transition.OpenAnimated();
        else
            root.SetActive(true);
    }

    public static void Close(GameObject root, Action onClosed = null)
    {
        if (root == null)
        {
            onClosed?.Invoke();
            return;
        }
        PopupTransition transition = root.GetComponent<PopupTransition>();
        if (transition != null)
        {
            transition.CloseAnimated(onClosed);
        }
        else
        {
            root.SetActive(false);
            onClosed?.Invoke();
        }
    }

    public void OpenAnimated()
    {
        if (opening)
            return;
        EnsureInitialized();
        CancelTransition();
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);
        hasOpened = true;
        if (!gameObject.activeInHierarchy)
        {
            // A disabled ancestor means no coroutine can run; present the
            // popup at rest so it is correct whenever the parent activates.
            RestoreAuthoredVisuals();
            return;
        }

        opening = true;
        AudioManager.Instance?.PlayUiOneShot(openClip, sfxVolume);
        transitionRoutine = StartCoroutine(OpenRoutine());
    }

    // Marks the popup as presented without playing the standard open
    // animation, so a caller with its own custom reveal (e.g. the win-vortex
    // bloom) still gets the animated close later.
    public void SetOpenInstant()
    {
        EnsureInitialized();
        CancelTransition();
        RestoreAuthoredVisuals();
        hasOpened = true;
    }

    public void CloseAnimated(Action onClosed = null)
    {
        if (closing)
            return; // one close at a time; the first caller's handoff wins
        EnsureInitialized();

        // Defensive startup hides, already-hidden popups, and popups under a
        // disabled ancestor snap instantly — the contract (deactivate, then
        // notify) still holds, just without a visible animation.
        if (!hasOpened || !gameObject.activeInHierarchy)
        {
            CancelTransition();
            RestoreAuthoredVisuals();
            gameObject.SetActive(false);
            onClosed?.Invoke();
            return;
        }

        CancelTransition();
        closing = true;
        pendingCloseCallback = onClosed;
        AudioManager.Instance?.PlayUiOneShot(closeClip, sfxVolume);
        transitionRoutine = StartCoroutine(CloseRoutine());
    }

    private void Awake() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (initialized)
            return;
        initialized = true;
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        authoredScale = transform.localScale;
        authoredAlpha = canvasGroup.alpha;
        authoredInteractable = canvasGroup.interactable;
        authoredBlocksRaycasts = canvasGroup.blocksRaycasts;
    }

    private void CancelTransition()
    {
        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }
        opening = false;
        closing = false;
    }

    private void RestoreAuthoredVisuals()
    {
        transform.localScale = authoredScale;
        if (canvasGroup != null)
        {
            canvasGroup.alpha = authoredAlpha;
            canvasGroup.interactable = authoredInteractable;
            canvasGroup.blocksRaycasts = authoredBlocksRaycasts;
        }
    }

    private IEnumerator OpenRoutine()
    {
        // Blocking from the very first frame: nothing behind the popup can
        // be tapped, and its own buttons only arm once the pop settles.
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = false;
        transform.localScale = authoredScale * startScaleMultiplier;
        canvasGroup.alpha = fadeEnabled ? 0f : authoredAlpha;

        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / openDuration);
            transform.localScale = authoredScale *
                Mathf.LerpUnclamped(startScaleMultiplier, 1f, BackOut(t, overshootStrength));
            if (fadeEnabled)
                canvasGroup.alpha = authoredAlpha * Mathf.Clamp01(t / 0.6f);
            yield return null;
        }

        opening = false;
        transitionRoutine = null;
        RestoreAuthoredVisuals();
    }

    private IEnumerator CloseRoutine()
    {
        // Buttons die instantly; the still-visible popup keeps absorbing
        // raycasts until it deactivates, so taps can't leak through it while
        // it fades.
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = true;

        float elapsed = 0f;
        while (elapsed < closeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / closeDuration);
            float eased = t * t;
            transform.localScale = authoredScale *
                Mathf.Lerp(1f, closeEndScaleMultiplier, eased);
            if (fadeEnabled)
                canvasGroup.alpha = authoredAlpha * (1f - eased);
            yield return null;
        }

        closing = false;
        transitionRoutine = null;
        Action callback = pendingCloseCallback;
        pendingCloseCallback = null;
        RestoreAuthoredVisuals();
        // Deactivation precedes the callback so owners resume gameplay or
        // hand off to the next presentation with the popup genuinely gone.
        gameObject.SetActive(false);
        callback?.Invoke();
    }

    // If something external deactivates the popup mid-transition, finish the
    // close contract anyway so the owner's completion logic is never stranded.
    private void OnDisable()
    {
        bool wasClosing = closing;
        CancelTransition();
        Action callback = pendingCloseCallback;
        pendingCloseCallback = null;
        RestoreAuthoredVisuals();
        if (wasClosing)
            callback?.Invoke();
    }

    // Standard back-out ease: overshoots the target proportionally to
    // strength and settles smoothly — one curve, no keyframe passes.
    private static float BackOut(float t, float strength)
    {
        t -= 1f;
        return 1f + (strength + 1f) * t * t * t + strength * t * t;
    }
}
