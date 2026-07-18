using UnityEngine;

public static class GameSettings
{
    private const string SfxKey = "Settings.SfxEnabled";
    private const string MusicKey = "Settings.MusicEnabled";
    private const string VibrationKey = "Settings.VibrationEnabled";

    public static bool SfxEnabled { get => Get(SfxKey, "MuteState"); set => Set(SfxKey, value); }
    public static bool MusicEnabled { get => Get(MusicKey); set => Set(MusicKey, value); }
    public static bool VibrationEnabled { get => Get(VibrationKey, "VibeState"); set => Set(VibrationKey, value); }

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
