using System;
using UnityEngine;

// Semantic vibration requests for gameplay moments.
public enum HapticType
{
    Light,
    Medium,
    Heavy,
    Success,
    Warning,
}

// The one shared haptic entry point: gameplay code asks for a semantic type
// (HapticFeedback.Play(HapticType.Light)) and never touches platform
// vibration APIs directly. GameSettings.VibrationEnabled is the single
// gate — disabled means every call is a silent no-op, no second preference
// exists anywhere.
//
// Platform mapping (best available, always short — this is a casual game):
//   Android  — Vibrator service via JNI. API 26+ gets amplitude-shaped
//              one-shots/waveforms (VibrationEffect); older devices get the
//              closest plain vibrate() pattern. Any JNI failure marks the
//              device unavailable and later calls no-op. The final legacy
//              fallback is Handheld.Vibrate, which also keeps Unity adding
//              the android.permission.VIBRATE manifest entry.
//   iOS      — AudioToolbox system-sound haptics (1519 peek / 1520 pop /
//              1521 nope), the strongest feedback reachable without a
//              native plugin; unsupported hardware simply plays nothing.
//   Editor / other platforms — silent no-op, never throws, never logs.
//
// A short global cooldown (unscaled real time — popups run at timeScale 0)
// keeps burst events (meteor shower chains, rapid merge cascades) from
// machine-gunning the motor; single milestones are never affected.
public static class HapticFeedback
{
    private const float MinSecondsBetweenPulses = 0.08f;
    private static float lastPlayTime = -1f;

    public static void Play(HapticType type)
    {
        if (!GameSettings.VibrationEnabled)
            return;
        float now = Time.realtimeSinceStartup;
        if (lastPlayTime >= 0f && now - lastPlayTime < MinSecondsBetweenPulses)
            return;
        lastPlayTime = now;

#if UNITY_ANDROID && !UNITY_EDITOR
        PlayAndroid(type);
#elif UNITY_IOS && !UNITY_EDITOR
        PlayIos(type);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    private static AndroidJavaObject vibrator;
    private static int androidSdk;
    private static bool androidReady;
    private static bool androidUnavailable;

    private static void PlayAndroid(HapticType type)
    {
        EnsureAndroidVibrator();
        if (androidUnavailable)
            return;
        try
        {
            switch (type)
            {
                case HapticType.Light: OneShot(15, 70); break;
                case HapticType.Medium: OneShot(30, 150); break;
                case HapticType.Heavy: OneShot(45, 255); break;
                case HapticType.Success:
                    Waveform(new long[] { 0, 25, 60, 40 }, new int[] { 0, 140, 0, 255 }); break;
                case HapticType.Warning:
                    Waveform(new long[] { 0, 50, 70, 50 }, new int[] { 0, 255, 0, 255 }); break;
            }
        }
        catch (Exception)
        {
            // JNI surface changed or vendor quirk: stop trying quietly and
            // leave the player with the plainest possible fallback once.
            androidUnavailable = true;
            try { Handheld.Vibrate(); } catch (Exception) { }
        }
    }

    private static void EnsureAndroidVibrator()
    {
        if (androidReady || androidUnavailable)
            return;
        try
        {
            using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
                androidSdk = version.GetStatic<int>("SDK_INT");
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            androidReady = vibrator != null && vibrator.Call<bool>("hasVibrator");
            if (!androidReady)
                androidUnavailable = true;
        }
        catch (Exception)
        {
            androidUnavailable = true;
        }
    }

    private static void OneShot(long milliseconds, int amplitude)
    {
        if (androidSdk >= 26)
        {
            using (AndroidJavaClass effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
            using (AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>(
                "createOneShot", milliseconds, amplitude))
                vibrator.Call("vibrate", effect);
        }
        else
        {
            vibrator.Call("vibrate", milliseconds);
        }
    }

    private static void Waveform(long[] timings, int[] amplitudes)
    {
        if (androidSdk >= 26)
        {
            using (AndroidJavaClass effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
            using (AndroidJavaObject effect = effectClass.CallStatic<AndroidJavaObject>(
                "createWaveform", timings, amplitudes, -1))
                vibrator.Call("vibrate", effect);
        }
        else
        {
            vibrator.Call("vibrate", timings, -1);
        }
    }
#endif

#if UNITY_IOS && !UNITY_EDITOR
    // AudioToolbox ships in every Unity iOS build (Handheld.Vibrate uses
    // it), so the classic haptic system-sound IDs are reachable without a
    // plugin. Devices without the taptic hardware just play nothing.
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void AudioServicesPlaySystemSound(uint soundId);

    private const uint IosPeek = 1519; // light tap
    private const uint IosPop = 1520;  // strong tap
    private const uint IosNope = 1521; // triple error buzz

    private static void PlayIos(HapticType type)
    {
        try
        {
            switch (type)
            {
                case HapticType.Light: AudioServicesPlaySystemSound(IosPeek); break;
                case HapticType.Medium: AudioServicesPlaySystemSound(IosPop); break;
                case HapticType.Heavy: AudioServicesPlaySystemSound(IosPop); break;
                case HapticType.Success: AudioServicesPlaySystemSound(IosPop); break;
                case HapticType.Warning: AudioServicesPlaySystemSound(IosNope); break;
            }
        }
        catch (Exception) { }
    }
#endif
}
