using UnityEngine;

// Central SFX brain, scene-singleton. Three core sounds — launch, collision,
// merge — each with its own dynamics:
//   Launch:    plain one-shot, fired by PlanetLauncher on every shot.
//   Collision: volume scales with impact speed (relativeVelocity magnitude),
//              plus a short cooldown so the two mirrored OnCollisionEnter2D
//              callbacks of one contact (and pile-settling chatter) can't
//              stack into ear rape.
//   Merge:     combo-pitched. Every merge (planet OR meteorite — they share
//              one chain) bumps a combo counter; pitch climbs with the chain
//              and snaps back to 1.0 after comboResetWindow seconds without
//              a merge.
//
// All clip slots are optional: a missing clip just skips that sound, so the
// game runs silently until audio assets are dropped in.
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("SFX Clips")]
    [SerializeField] private AudioClip launchClip;
    [SerializeField] private AudioClip collisionClip;
    [SerializeField] private AudioClip mergeClip;
    // BOOM/Big Pop only (PlanetMerge.TriggerBoom, Meteorite.TriggerBigPop) —
    // regular tier-up merges still ride PlayMerge's combo chain above.
    public AudioClip SFX_Explosion;

    [Header("Skill Feedback Clips")]
    [SerializeField] private AudioClip skillCollectedClip;
    [SerializeField] private AudioClip skillUseSuccessClip;
    [SerializeField] private AudioClip skillUseFailedClip;
    [SerializeField] private AudioClip gravitySingularityClip;
    [SerializeField] private AudioClip meteorStrikeImpactClip;
    [SerializeField] private AudioClip timeWarpClip;
    [SerializeField] private AudioClip cosmicMimicClip;

    [Header("Skill Feedback Mix")]
    [SerializeField] [Range(0f, 1f)] private float skillCollectedVolume = 0.9f;
    [SerializeField] [Range(0f, 1f)] private float skillFeedbackVolume = 0.9f;
    [SerializeField] [Range(0f, 1f)] private float specificSkillVolume = 1f;
    [SerializeField] private bool playGenericSuccessWithSpecific = true;

    [Header("Launch")]
    [SerializeField] [Range(0f, 1f)] private float launchVolume = 0.9f;

    [Header("Collision (impact-damped)")]
    // Impacts slower than this are inaudible pile chatter — no sound at all.
    [SerializeField] private float minImpactSpeed = 0.6f;
    // Impact speed at (and above) which the collision plays at full
    // maxCollisionVolume; volume ramps linearly between min and this.
    [SerializeField] private float fullVolumeImpactSpeed = 8f;
    [SerializeField] [Range(0f, 1f)] private float maxCollisionVolume = 0.8f;
    // Minimum seconds between collision sounds. Also the dedupe for a single
    // contact: both bodies fire OnCollisionEnter2D the same frame, and the
    // second call lands inside this window and is dropped.
    [SerializeField] private float collisionSoundCooldown = 0.07f;

    [Header("Explosion (BOOM / Big Pop)")]
    [SerializeField] [Range(0f, 1f)] private float explosionVolume = 1f;

    [Header("Merge Combo (pitch shifting)")]
    // Seconds without a merge before the combo chain resets to zero.
    [SerializeField] private float comboResetWindow = 1.5f;
    // Pitch gain per combo step: combo 1 = 1.0, combo 2 = 1.2, combo 3 = 1.4...
    [SerializeField] private float pitchStepPerCombo = 0.2f;
    // Healthy ceiling so long chains excite without turning into a chipmunk.
    [SerializeField] private float maxComboPitch = 2f;
    [SerializeField] [Range(0f, 1f)] private float mergeVolume = 1f;

    // Live combo length, readable by future UI ("COMBO x4!"). 0 = no chain.
    public int CurrentCombo { get; private set; }

    // Readable by UI (e.g. MainMenuController) that wants to reflect the
    // current state without keeping its own copy.
    public bool IsMuted { get; private set; }

    // Two dedicated sources so the merge combo's pitch rides never bend the
    // launch/collision sounds: sfxSource stays at pitch 1 forever, only
    // mergeSource gets retuned.
    private AudioSource sfxSource;
    private AudioSource mergeSource;

    private float lastCollisionSoundTime = float.NegativeInfinity;
    private float lastMergeTime = float.NegativeInfinity;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("AudioManager: duplicate instance destroyed.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;

        mergeSource = gameObject.AddComponent<AudioSource>();
        mergeSource.playOnAwake = false;

        // AudioManager is a scene-local singleton (re-created on every scene
        // load, not DontDestroyOnLoad), so the mute preference set on the
        // Main Menu wouldn't otherwise survive into the gameplay scene —
        // re-apply it here every time a fresh instance wakes up. Key/values
        // must stay in sync with MainMenuController.MuteStateKey.
        SetMuted(PlayerPrefs.GetInt("MuteState", 1) == 0);
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Global instant mute: flips AudioListener.volume rather than gating
    // each PlayXxx call, so it silences everything (including anything
    // already playing) on the same frame it's called, with no per-source
    // bookkeeping needed.
    public void SetMuted(bool muted)
    {
        IsMuted = muted;
        AudioListener.volume = muted ? 0f : 1f;
    }

    public void PlayLaunch()
    {
        if (launchClip == null)
            return;

        sfxSource.PlayOneShot(launchClip, launchVolume);
    }

    // impactSpeed: collision.relativeVelocity.magnitude from the caller's
    // OnCollisionEnter2D. Soft touches stay silent; the harder the hit, the
    // louder the clip, capped at maxCollisionVolume.
    public void PlayCollision(float impactSpeed)
    {
        if (collisionClip == null || impactSpeed < minImpactSpeed)
            return;

        if (Time.time - lastCollisionSoundTime < collisionSoundCooldown)
            return;
        lastCollisionSoundTime = Time.time;

        float volume = Mathf.InverseLerp(minImpactSpeed, fullVolumeImpactSpeed, impactSpeed)
                       * maxCollisionVolume;
        sfxSource.PlayOneShot(collisionClip, volume);
    }

    // PlayOneShot (not a dedicated source) so a rapid double-BOOM can overlap
    // instead of the second explosion cutting the first one off.
    public void PlayExplosion()
    {
        if (SFX_Explosion == null)
            return;

        sfxSource.PlayOneShot(SFX_Explosion, explosionVolume);
    }

    public void PlaySkillCollected()
    {
        PlayOptionalOneShot(skillCollectedClip, skillCollectedVolume);
    }

    public void PlaySkillUseFailed()
    {
        PlayOptionalOneShot(skillUseFailedClip, skillFeedbackVolume);
    }

    public void PlaySkillUseSucceeded(SkillType type)
    {
        AudioClip specificClip = null;
        switch (type)
        {
            case SkillType.GravitySingularity:
                specificClip = gravitySingularityClip;
                break;
            case SkillType.TimeWarp:
                specificClip = timeWarpClip;
                break;
            case SkillType.CosmicMimic:
                specificClip = cosmicMimicClip;
                break;
            // Meteor Strike's themed sound belongs to the later visual impact.
            case SkillType.MeteorStrike:
                break;
        }

        if (playGenericSuccessWithSpecific || specificClip == null)
            PlayOptionalOneShot(skillUseSuccessClip, skillFeedbackVolume);
        PlayOptionalOneShot(specificClip, specificSkillVolume);
    }

    public void PlayMeteorStrikeSequence()
    {
        PlayOptionalOneShot(meteorStrikeImpactClip, specificSkillVolume);
    }

    private void PlayOptionalOneShot(AudioClip clip, float volume)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    // One call per successful merge (PlanetMerge and Meteorite both report
    // here, feeding a single shared chain). Combo bookkeeping is lazy — the
    // reset is checked on the next merge rather than in Update, which is
    // equivalent for anything driven off PlayMerge/CurrentCombo.
    public void PlayMerge()
    {
        if (Time.time - lastMergeTime > comboResetWindow)
        {
            CurrentCombo = 0;
        }
        lastMergeTime = Time.time;
        CurrentCombo++;

        if (mergeClip == null)
            return;

        mergeSource.pitch = Mathf.Min(
            1f + pitchStepPerCombo * (CurrentCombo - 1), maxComboPitch);
        mergeSource.PlayOneShot(mergeClip, mergeVolume);
    }
}
