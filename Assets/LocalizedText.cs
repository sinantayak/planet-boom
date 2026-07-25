using TMPro;
using UnityEngine;

// Attach next to a static TMP text and set its localization key: the label
// shows the current language's string on enable and refreshes itself the
// moment GameSettings.Language changes. ONLY the displayed string is ever
// written — font, size, color, alignment and the RectTransform stay exactly
// as authored. Texts composed at runtime (mission progress, timers, counts)
// must NOT carry this component; their owning scripts call
// Localization.Get themselves.
[DisallowMultipleComponent]
public sealed class LocalizedText : MonoBehaviour
{
    [SerializeField] private string key;
    // Defaults to the TMP text on this same GameObject when left empty.
    [SerializeField] private TMP_Text target;

    private void Awake()
    {
        if (target == null)
            target = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        Localization.LanguageChanged += Apply;
        Apply();
    }

    private void OnDisable()
    {
        Localization.LanguageChanged -= Apply;
    }

    private void Apply()
    {
        if (target != null && !string.IsNullOrEmpty(key))
            target.text = Localization.Get(key);
    }
}
