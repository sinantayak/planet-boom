using UnityEngine;
using UnityEngine.UI;

// Attach next to a Button (or a Button used as a toggle) and pick a category
// in the Inspector — the shared UiSoundLibrary supplies the actual clip, so
// no per-button AudioClip is ever required (Override Clip exists for the
// rare special case). Presentation only: it listens to the Button's own
// onClick, so it can never change behavior, never delays the action, and
// never fires for a non-interactable button (onClick simply doesn't invoke).
[DisallowMultipleComponent]
public sealed class UiButtonSound : MonoBehaviour
{
    [SerializeField] private UiSoundType soundType = UiSoundType.Default;
    // Optional per-button clip; when null the shared library clip for the
    // chosen category plays instead.
    [SerializeField] private AudioClip overrideClip;

    private Button button;
    private int lastPlayedFrame = -1;

    private void Awake()
    {
        button = GetComponent<Button>();
        if (button != null)
            button.onClick.AddListener(HandleClick);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        if (soundType == UiSoundType.None)
            return;
        // One sound per frame per button: even if a click event is somehow
        // dispatched twice in the same frame, playback can't stack.
        if (Time.frameCount == lastPlayedFrame)
            return;
        lastPlayedFrame = Time.frameCount;
        UiSounds.Play(soundType, overrideClip);
    }
}
