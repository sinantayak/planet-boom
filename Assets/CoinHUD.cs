using System.Collections;
using TMPro;
using UnityEngine;

// Scene-authored HUD for coins earned during the current level run. This
// deliberately reads GameManager's temporary counter, never PlayerData.
public sealed class CoinHUD : MonoBehaviour
{
    [Header("Scene Wiring")]
    [SerializeField] private RectTransform coinIconTarget;
    [SerializeField] private TextMeshProUGUI countText;
    [SerializeField] private RectTransform pulseTarget;

    [Header("Arrival Pulse")]
    [SerializeField] [Min(1f)] private float pulseScale = 1.22f;
    [SerializeField] [Min(0.05f)] private float pulseDuration = 0.24f;

    public RectTransform CoinIconTarget => coinIconTarget;

    private GameManager boundGameManager;
    private Coroutine bindRoutine;
    private Coroutine pulseRoutine;
    private Vector3 restingScale = Vector3.one;
    private long displayedCoins;

    private void Awake()
    {
        if (pulseTarget == null)
            pulseTarget = transform as RectTransform;
        if (pulseTarget != null)
            restingScale = pulseTarget.localScale;
    }

    private void OnEnable()
    {
        TryBind();
        if (boundGameManager == null)
            bindRoutine = StartCoroutine(BindWhenGameManagerIsReady());
    }

    private void OnDisable()
    {
        if (bindRoutine != null)
        {
            StopCoroutine(bindRoutine);
            bindRoutine = null;
        }
        Unbind();
        StopPulse();
    }

    private IEnumerator BindWhenGameManagerIsReady()
    {
        while (boundGameManager == null)
        {
            TryBind();
            if (boundGameManager == null)
                yield return null;
        }
        bindRoutine = null;
    }

    private void TryBind()
    {
        GameManager manager = GameManager.Instance;
        if (manager == null || manager == boundGameManager)
            return;

        Unbind();
        boundGameManager = manager;
        boundGameManager.LevelEarnedCoinsChanged += HandleLevelEarnedCoinsChanged;
        SetDisplayedCoins(boundGameManager.LevelEarnedCoins, false);
    }

    private void Unbind()
    {
        if (boundGameManager != null)
            boundGameManager.LevelEarnedCoinsChanged -= HandleLevelEarnedCoinsChanged;
        boundGameManager = null;
    }

    private void HandleLevelEarnedCoinsChanged(long amount)
    {
        SetDisplayedCoins(amount, amount > displayedCoins);
    }

    private void SetDisplayedCoins(long amount, bool pulse)
    {
        displayedCoins = amount;
        if (countText != null)
            countText.text = amount.ToString();
        if (pulse)
            PlayPulse();
    }

    private void PlayPulse()
    {
        StopPulse();
        if (pulseTarget != null)
            pulseRoutine = StartCoroutine(PulseRoutine());
    }

    private void StopPulse()
    {
        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }
        if (pulseTarget != null)
            pulseTarget.localScale = restingScale;
    }

    private IEnumerator PulseRoutine()
    {
        float duration = Mathf.Max(0.05f, pulseDuration);
        float peakScale = Mathf.Max(1f, pulseScale);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float triangle = 1f - Mathf.Abs(t * 2f - 1f);
            float eased = triangle * triangle * (3f - 2f * triangle);
            pulseTarget.localScale = restingScale * Mathf.Lerp(1f, peakScale, eased);
            yield return null;
        }

        pulseTarget.localScale = restingScale;
        pulseRoutine = null;
    }
}
