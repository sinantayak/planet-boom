using UnityEngine;

// The one persistent background-music player. A single self-created
// DontDestroyOnLoad AudioSource starts with the game, loops the configured
// track, and simply keeps playing through every scene change — nothing
// restarts it, so MainMenu → LevelMap → GameScene (and back) is seamless.
//
// This is deliberately NOT a second AudioManager: every SFX still lives in
// the scene-local AudioManager, which cannot host persistent music because
// it is rebuilt on each GameScene load and absent from the menu scenes.
//
// Preferences: GameSettings.MusicEnabled mutes/unmutes (playback position is
// preserved, so re-enabling resumes cleanly mid-track), and the normalized
// GameSettings.MusicVolume drives the source volume — both setters call
// ApplySavedPreferences, so any current toggle or future settings slider
// takes effect on the same frame. SFX preferences never touch this source.
// Time.timeScale is irrelevant to AudioSources, and ignoreListenerPause
// guards against any future AudioListener.pause usage.
public sealed class MusicPlayer : MonoBehaviour
{
    private static MusicPlayer instance;
    private AudioSource source;

    // Runs once per play session, after the first scene (MainMenu in a real
    // build) has loaded — music is already playing by the first menu frame.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        MusicSettings settings = MusicSettings.Active;
        if (settings != null && settings.DefaultTrack != null)
            SetTrack(settings.DefaultTrack);
    }

    private static MusicPlayer EnsureInstance()
    {
        if (instance != null)
            return instance;
        GameObject host = new GameObject("MusicPlayer");
        DontDestroyOnLoad(host);
        instance = host.AddComponent<MusicPlayer>();
        instance.source = host.AddComponent<AudioSource>();
        instance.source.playOnAwake = false;
        instance.source.loop = true;
        instance.source.ignoreListenerPause = true;
        return instance;
    }

    // Future track selection swaps clips through here; the current single-
    // track setup is just SetTrack(settings.DefaultTrack) at bootstrap.
    // Setting the clip that is already playing is a no-op, so this can never
    // restart the music mid-session.
    public static void SetTrack(AudioClip track)
    {
        MusicPlayer player = EnsureInstance();
        if (player.source.clip == track)
        {
            ApplySavedPreferences();
            return;
        }
        player.source.clip = track;
        ApplySavedPreferences();
        if (track != null)
            player.source.Play();
        else
            player.source.Stop();
    }

    // Re-reads the saved music preference and volume. Muting (not stopping)
    // keeps the track's position alive while Music is OFF; SFX settings are
    // never consulted here.
    public static void ApplySavedPreferences()
    {
        if (instance == null || instance.source == null)
            return;
        instance.source.mute = !GameSettings.MusicEnabled;
        instance.source.volume = GameSettings.MusicVolume;
    }
}
