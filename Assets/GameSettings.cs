using UnityEngine;

public static class GameSettings
{
    private const string SfxKey = "Settings.SfxEnabled";
    private const string MusicKey = "Settings.MusicEnabled";
    private const string VibrationKey = "Settings.VibrationEnabled";
    private const string MusicVolumeKey = "Settings.MusicVolume";
    private const float DefaultMusicVolume = 0.8f;

    public static bool SfxEnabled { get => Get(SfxKey, "MuteState"); set => Set(SfxKey, value); }
    // Setting either music value pushes it into the persistent MusicPlayer
    // immediately, so every current toggle and any future settings UI takes
    // effect on the same frame with no extra wiring.
    public static bool MusicEnabled
    {
        get => Get(MusicKey);
        set { Set(MusicKey, value); MusicPlayer.ApplySavedPreferences(); }
    }
    public static bool VibrationEnabled { get => Get(VibrationKey, "VibeState"); set => Set(VibrationKey, value); }

    // Normalized 0–1 background-music volume; a future settings slider just
    // assigns this property. Independent of every SFX setting.
    public static float MusicVolume
    {
        get => Mathf.Clamp01(PlayerPrefs.GetFloat(MusicVolumeKey, DefaultMusicVolume));
        set
        {
            PlayerPrefs.SetFloat(MusicVolumeKey, Mathf.Clamp01(value));
            PlayerPrefs.Save();
            MusicPlayer.ApplySavedPreferences();
        }
    }

    private static bool Get(string key, string legacyKey = null)
    {
        if (PlayerPrefs.HasKey(key)) return PlayerPrefs.GetInt(key, 1) != 0;
        return string.IsNullOrEmpty(legacyKey) || PlayerPrefs.GetInt(legacyKey, 1) != 0;
    }
    private static void Set(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
    }
}

public static class Haptics
{
    public static bool Enabled => GameSettings.VibrationEnabled;
    public static void Vibrate()
    {
        if (Enabled) Handheld.Vibrate();
    }
}
