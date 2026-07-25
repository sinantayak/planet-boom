using TMPro;
using UnityEngine;

// Presentation-only companion to the authoritative GameManager clock.
// UrgencyCountdown keeps owning the large 10..1 readout; this component owns
// only the persistent Timer HUD color/pulse and once-per-whole-second audio.
public sealed class LowTimePanic : MonoBehaviour
{
    [Header("Scene Wiring")]
    [SerializeField] private TextMeshProUGUI timerText;
    [Tooltip("Leave empty to pulse the centered TimerText itself.")]
    [SerializeField] private RectTransform centeredPulseTarget;

    [Header("Thresholds")]
    [SerializeField, Min(.01f)] private float warningThreshold = 10f;
    [SerializeField, Min(.01f)] private float criticalThreshold = 5f;

    [Header("Warning (10–6)")]
    [SerializeField] private Color warningColor = new Color(1f, .28f, .22f, 1f);
    [SerializeField, Range(1f, 1.5f)] private float warningPulseScale = 1.06f;
    [SerializeField, Min(.01f)] private float warningPulseSpeed = 1.5f;

    [Header("Critical (5–1)")]
    [SerializeField] private Color criticalColor = new Color(1f, .08f, .04f, 1f);
    [SerializeField, Range(1f, 1.75f)] private float criticalPulseScale = 1.13f;
    [SerializeField, Min(.01f)] private float criticalPulseSpeed = 2.5f;

    [Header("Audio")]
    [SerializeField] private AudioClip lowTimeTickClip;
    [SerializeField] private AudioClip criticalTimeTickClip;
    [SerializeField, Range(0f, 1f)] private float tickVolume = .9f;

    [Header("Haptic")]
    [SerializeField] private bool warningHapticOnEntry = true;

    private Color authoredColor = Color.white;
    private Vector3 authoredScale = Vector3.one;
    private bool presentationActive;
    private bool lowTimePeriodActive;
    private int lastTickSecond = -1;
    private float pulsePhase;

    private void Awake()
    {
        if (timerText == null)
            timerText = GetComponentInChildren<TextMeshProUGUI>(true);
        if (centeredPulseTarget == null && timerText != null)
            centeredPulseTarget = timerText.rectTransform;
        CaptureAuthoredLook();
        RestoreVisuals(true);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        warningThreshold = Mathf.Max(.01f, warningThreshold);
        criticalThreshold = Mathf.Clamp(criticalThreshold, .01f, warningThreshold);
    }
#endif

    private void OnDisable()
    {
        RestoreVisuals(true);
        ClearLowTimePeriod();
    }

    private void Update()
    {
        if (timerText == null || centeredPulseTarget == null)
            return;
        GameManager manager = GameManager.Instance;
        if (manager == null)
        {
            RestoreVisuals();
            ClearLowTimePeriod();
            return;
        }

        if (manager.State != GameManager.GameState.Playing)
        {
            RestoreVisuals();
            // Pause does not create a new low-time period. The frozen whole
            // second will not replay its tick or entry haptic on resume.
            if (manager.State != GameManager.GameState.GamePaused &&
                manager.State != GameManager.GameState.InventoryPaused)
                ClearLowTimePeriod();
            return;
        }

        float remaining = manager.RemainingTime;
        if (remaining <= 0f || remaining > warningThreshold)
        {
            RestoreVisuals();
            ClearLowTimePeriod();
            return;
        }

        if (!lowTimePeriodActive)
        {
            lowTimePeriodActive = true;
            lastTickSecond = -1;
            if (warningHapticOnEntry)
                HapticFeedback.Play(HapticType.Warning);
        }

        if (!presentationActive)
        {
            presentationActive = true;
            pulsePhase = 0f;
        }

        bool critical = remaining <= criticalThreshold;
        int wholeSecond = Mathf.CeilToInt(remaining);
        if (wholeSecond != lastTickSecond)
        {
            lastTickSecond = wholeSecond;
            AudioClip clip = critical
                ? (criticalTimeTickClip != null ? criticalTimeTickClip : lowTimeTickClip)
                : lowTimeTickClip;
            AudioManager.Instance?.PlayUiOneShot(clip, tickVolume);
        }

        timerText.color = critical ? criticalColor : warningColor;
        float speed = critical ? criticalPulseSpeed : warningPulseSpeed;
        float peakScale = critical ? criticalPulseScale : warningPulseScale;
        pulsePhase = Mathf.Repeat(pulsePhase + Time.unscaledDeltaTime * speed, 1f);
        float pulse = HeartbeatPulse(pulsePhase);
        centeredPulseTarget.localScale =
            authoredScale * Mathf.Lerp(1f, peakScale, pulse);
    }

    private void CaptureAuthoredLook()
    {
        if (timerText != null)
            authoredColor = timerText.color;
        if (centeredPulseTarget != null)
            authoredScale = centeredPulseTarget.localScale;
    }

    private void RestoreVisuals(bool force = false)
    {
        if (!force && !presentationActive)
            return;
        if (timerText != null)
            timerText.color = authoredColor;
        if (centeredPulseTarget != null)
            centeredPulseTarget.localScale = authoredScale;
        presentationActive = false;
        pulsePhase = 0f;
    }

    private void ClearLowTimePeriod()
    {
        lowTimePeriodActive = false;
        lastTickSecond = -1;
    }

    // Two compact beats followed by a rest: "dum-dum ...". Unlike a sine
    // wave this returns fully to the authored scale between heartbeats.
    private static float HeartbeatPulse(float phase)
    {
        float firstBeat = PulseWindow(phase, 0f, .16f);
        float secondBeat = PulseWindow(phase, .21f, .36f) * .72f;
        return Mathf.Max(firstBeat, secondBeat);
    }

    private static float PulseWindow(float value, float start, float end)
    {
        if (value < start || value > end)
            return 0f;
        float t = Mathf.InverseLerp(start, end, value);
        return Mathf.Sin(t * Mathf.PI);
    }
}
