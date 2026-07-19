using System.Collections;
using TMPro;
using UnityEngine;

// Displays the spendable Space Coin balance plus uncommitted coins collected
// during the current run. The preview becomes persistent only on victory.
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
        PlayerDataPersistenceManager.DataLoaded += HandlePlayerDataLoaded;
        PlayerDataPersistenceManager.SpaceCoinChanged += HandleSpaceCoinChanged;
        TryBind();
        if (boundGameManager == null)
            bindRoutine = StartCoroutine(BindWhenGameManagerIsReady());
        RefreshDisplayedCoins(false);
    }

    private void OnDisable()
    {
        PlayerDataPersistenceManager.DataLoaded -= HandlePlayerDataLoaded;
        PlayerDataPersistenceManager.SpaceCoinChanged -= HandleSpaceCoinChanged;
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
        RefreshDisplayedCoins(false);
    }

    private void Unbind()
    {
        if (boundGameManager != null)
            boundGameManager.LevelEarnedCoinsChanged -= HandleLevelEarnedCoinsChanged;
        boundGameManager = null;
    }

    private void HandleLevelEarnedCoinsChanged(long amount)
    {
        RefreshDisplayedCoins(true);
    }

    private void HandlePlayerDataLoaded(PlayerData data) => RefreshDisplayedCoins(false);

    private void HandleSpaceCoinChanged(long amount) => RefreshDisplayedCoins(false);

    private void RefreshDisplayedCoins(bool pulseIfIncreased)
    {
        long persisted = PlayerDataPersistenceManager.Instance != null
            ? PlayerDataPersistenceManager.Instance.SpaceCoin
            : 0;
        long runCoins = boundGameManager != null && !boundGameManager.IsLevelRewardCommitted
            ? boundGameManager.LevelEarnedCoins
            : 0;
        long total = persisted > long.MaxValue - runCoins ? long.MaxValue : persisted + runCoins;
        SetDisplayedCoins(total, pulseIfIncreased && total > displayedCoins);
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
