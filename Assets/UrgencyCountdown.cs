using System.Collections;
using TMPro;
using UnityEngine;

// Presentation-only low-time readout shown below the Mission HUD. It never
// owns a clock: every frame it re-reads GameManager.RemainingTime (the
// authoritative gameplay timer) and mirrors it only while the game is
// actually Playing with at most showAtOrBelowSeconds left. Time Drop /
// Time Warp pushing the clock back above the window hides it automatically,
// as does any state change away from Playing.
//
// NOTE: this is the attention feedback only — the later low-time panic
// system (looping audio, red timer pulse) is a separate feature.
public sealed class UrgencyCountdown : MonoBehaviour
{
    [Header("Scene Wiring")]
    // Defaults to a TMP on this same GameObject when left unassigned.
    [SerializeField] private TextMeshProUGUI countdownText;

    [Header("Behaviour")]
    [SerializeField, Min(1f)] private float showAtOrBelowSeconds = 10f;

    [Header("Punch Animation")]
    [SerializeField, Range(1f, 2.5f)] private float punchScale = 1.4f;
    [SerializeField, Min(0.05f)] private float punchDuration = 0.25f;

    // The authored Inspector scale is the punch's resting pose; runtime only
    // multiplies it during the pop and always restores it afterwards.
    private Vector3 authoredScale = Vector3.one;
    private int displayedSeconds = -1;
    private bool visible;
    private Coroutine punchRoutine;

    private void Awake()
    {
        if (countdownText == null)
            countdownText = GetComponent<TextMeshProUGUI>();
        if (countdownText != null)
        {
            authoredScale = countdownText.rectTransform.localScale;
            countdownText.raycastTarget = false;
            countdownText.enabled = false;
        }
    }

    private void OnDisable() => Hide();

    private void Update()
    {
        GameManager manager = GameManager.Instance;
        bool shouldShow = countdownText != null && manager != null &&
            manager.State == GameManager.GameState.Playing &&
            manager.RemainingTime > 0f &&
            manager.RemainingTime <= showAtOrBelowSeconds;

        if (!shouldShow)
        {
            Hide();
            return;
        }

        // Same Ceil convention as GameManager.UpdateTimerUI, so this readout
        // and the HUD clock always agree on the visible whole second.
        int seconds = Mathf.CeilToInt(manager.RemainingTime);
        if (visible && seconds == displayedSeconds)
            return;

        visible = true;
        displayedSeconds = seconds;
        countdownText.enabled = true;
        countdownText.text = seconds.ToString();
        if (punchRoutine != null)
            StopCoroutine(punchRoutine);
        punchRoutine = StartCoroutine(Punch());
    }

    private void Hide()
    {
        if (!visible)
            return;
        visible = false;
        displayedSeconds = -1;
        if (punchRoutine != null)
        {
            StopCoroutine(punchRoutine);
            punchRoutine = null;
        }
        if (countdownText != null)
        {
            countdownText.enabled = false;
            countdownText.rectTransform.localScale = authoredScale;
        }
    }

    // Same feel as MissionHUD's completion pop: snap out, ease back home.
    private IEnumerator Punch()
    {
        RectTransform rect = countdownText.rectTransform;
        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / punchDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            rect.localScale = authoredScale * Mathf.Lerp(punchScale, 1f, eased);
            yield return null;
        }
        rect.localScale = authoredScale;
        punchRoutine = null;
    }
}
