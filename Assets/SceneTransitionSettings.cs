using UnityEngine;

// Central Inspector configuration for the shared scene-transition flow: one
// asset at Resources/SceneTransitionSettings covers every scene and every
// future navigation path. The asset is optional — if it is missing,
// SceneTransition falls back to the same defaults coded here and transitions
// still work (just without a custom clip).
[CreateAssetMenu(fileName = "SceneTransitionSettings", menuName = "Planet Boom/Scene Transition Settings")]
public sealed class SceneTransitionSettings : ScriptableObject
{
    [Header("Timing (seconds, unscaled)")]
    [Min(0.05f)] public float fadeOutDuration = 0.22f;
    [Min(0f)] public float holdDuration = 0.05f;
    [Min(0.05f)] public float fadeInDuration = 0.28f;

    [Header("Startup")]
    // Subtle fade-in from black when the game first launches.
    public bool startupFadeIn = true;

    [Header("SFX (optional)")]
    public AudioClip transitionClip;
    [Range(0f, 1f)] public float sfxVolume = 0.9f;

    private static SceneTransitionSettings active;
    private static bool searched;

    public static SceneTransitionSettings Active
    {
        get
        {
            if (active == null && !searched)
            {
                searched = true;
                active = Resources.Load<SceneTransitionSettings>("SceneTransitionSettings");
            }
            return active;
        }
    }
}
