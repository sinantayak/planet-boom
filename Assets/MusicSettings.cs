using UnityEngine;

// Central Inspector configuration for background music: one asset at
// Resources/MusicSettings holds the default looping track. Kept separate
// from AudioManager on purpose — the manager is a scene-local singleton
// (absent in MainMenu/LevelMap, rebuilt on every GameScene load) while the
// music must outlive every scene. Future selectable tracks extend this
// asset; MusicPlayer.SetTrack already accepts any clip.
[CreateAssetMenu(fileName = "MusicSettings", menuName = "Planet Boom/Music Settings")]
public sealed class MusicSettings : ScriptableObject
{
    [Header("Background Music")]
    // The one looping menu/gameplay track for now. Optional: when empty the
    // game simply runs without music, no errors.
    [SerializeField] private AudioClip defaultTrack;

    public AudioClip DefaultTrack => defaultTrack;

    private static MusicSettings active;
    private static bool searched;

    public static MusicSettings Active
    {
        get
        {
            if (active == null && !searched)
            {
                searched = true;
                active = Resources.Load<MusicSettings>("MusicSettings");
            }
            return active;
        }
    }
}
