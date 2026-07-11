using UnityEngine;

// App-level frame rate unlock. Android ignores QualitySettings.vSyncCount and
// instead paces the game by Application.targetFrameRate — whose mobile default
// is 30 FPS. That default is the "locked at a low refresh rate" feel on
// device. This runs before the first scene loads (no scene object, no wiring,
// survives every scene/restart) and pins the target to the display's native
// refresh rate: 60, 90 or 120 Hz depending on the phone.
public static class PerformanceBootstrap
{
    private static bool focusHookInstalled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        ApplyFrameRateSettings();

        // Re-apply on focus regained: Android may hand the app a different
        // display mode after backgrounding (battery saver, per-app refresh
        // overrides, external display), which would silently re-cap us. The
        // guard keeps editor "Enter Play Mode without domain reload" from
        // stacking duplicate handlers across play sessions.
        if (!focusHookInstalled)
        {
            focusHookInstalled = true;
            Application.focusChanged += focused =>
            {
                if (focused)
                    ApplyFrameRateSettings();
            };
        }
    }

    private static void ApplyFrameRateSettings()
    {
        // targetFrameRate is ignored while vSync is on (Editor/desktop), so
        // vSync goes first. On an Android device this line is a no-op — the
        // OS always syncs to the display — but it makes the Editor Game view
        // pace itself the same way the phone does.
        QualitySettings.vSyncCount = 0;

        // Native refresh rate of the ACTIVE display mode. refreshRateRatio is
        // the Unity 6 API (the old int refreshRate is deprecated). Floor at
        // 60 so a bogus 0/low reading from an odd device can't re-create the
        // very cap this script exists to remove.
        int nativeHz = Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value);
        Application.targetFrameRate = Mathf.Max(60, nativeHz);

        Debug.Log($"PerformanceBootstrap: vSync off, targetFrameRate={Application.targetFrameRate} " +
                  $"(display reports {nativeHz}Hz).");
    }
}
