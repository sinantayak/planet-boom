using UnityEngine;

// Category a UI element picks in the Inspector for its interaction sound.
// Default is first so a freshly added UiButtonSound needs no setup for the
// common case; None opts a button out while keeping the component in place.
public enum UiSoundType
{
    Default,
    Confirm,
    Back,
    Toggle,
    Error,
    None
}

// Central shared configuration for UI interaction sounds: one asset at
// Resources/UiSoundLibrary holds the five category clips and the shared
// volume, so individual buttons only pick a category and never need their
// own AudioClip (an optional per-button override lives on UiButtonSound).
// Playback itself is routed elsewhere (UiSounds → AudioManager); this asset
// is data only.
[CreateAssetMenu(fileName = "UiSoundLibrary", menuName = "Planet Boom/UI Sound Library")]
public sealed class UiSoundLibrary : ScriptableObject
{
    [Header("Shared Category Clips (all optional)")]
    [SerializeField] private AudioClip defaultClickClip;
    [SerializeField] private AudioClip confirmClip;
    [SerializeField] private AudioClip backClip;
    [SerializeField] private AudioClip toggleClip;
    [SerializeField] private AudioClip errorClip;

    [Header("Shared Volume")]
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;

    public float Volume => volume;

    public AudioClip ClipFor(UiSoundType type)
    {
        switch (type)
        {
            case UiSoundType.Default: return defaultClickClip;
            case UiSoundType.Confirm: return confirmClip;
            case UiSoundType.Back: return backClip;
            case UiSoundType.Toggle: return toggleClip;
            case UiSoundType.Error: return errorClip;
            default: return null;
        }
    }

    private static UiSoundLibrary active;
    private static bool searched;

    // Loaded once from Resources so every scene shares the same asset with
    // zero per-scene or per-button wiring. Null (asset missing) is a valid
    // state — callers treat it as "no clips assigned yet" and stay silent.
    public static UiSoundLibrary Active
    {
        get
        {
            if (active == null && !searched)
            {
                searched = true;
                active = Resources.Load<UiSoundLibrary>("UiSoundLibrary");
            }
            return active;
        }
    }
}
