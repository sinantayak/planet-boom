using UnityEngine;

// Single playback entry point for UI interaction sounds. Resolution order:
// the shared UiSoundLibrary supplies the clip (unless the caller overrides),
// then AudioManager.PlayUiOneShot plays it — the existing mute-respecting,
// null-safe SFX path. MainMenu and LevelMap carry no AudioManager, so a tiny
// hidden fallback AudioSource covers those scenes while honouring the same
// saved GameSettings.SfxEnabled preference; it is NOT a second audio system,
// just a speaker of last resort for menu scenes.
public static class UiSounds
{
    private static AudioSource fallbackSource;

    public static void Play(UiSoundType type, AudioClip overrideClip = null)
    {
        if (type == UiSoundType.None)
            return;

        UiSoundLibrary library = UiSoundLibrary.Active;
        AudioClip clip = overrideClip != null
            ? overrideClip
            : library != null ? library.ClipFor(type) : null;
        if (clip == null)
            return; // unassigned categories are a silent no-op, never an error
        float volume = library != null ? library.Volume : 1f;

        AudioManager manager = AudioManager.Instance;
        if (manager != null)
        {
            manager.PlayUiOneShot(clip, volume);
            return;
        }

        if (!GameSettings.SfxEnabled)
            return;
        if (fallbackSource == null)
        {
            GameObject host = new GameObject("UiSoundsFallbackSource");
            // Survives scene loads so a menu click that triggers a scene
            // change isn't clipped mid-sound.
            Object.DontDestroyOnLoad(host);
            fallbackSource = host.AddComponent<AudioSource>();
            fallbackSource.playOnAwake = false;
        }
        fallbackSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    // For owners whose action genuinely failed (locked content, rejected
    // activation, insufficient resources). Never wired automatically —
    // callers invoke it exactly where their existing code reports failure.
    public static void PlayError() => Play(UiSoundType.Error);
}
