using TMPro;
using UnityEngine;

// Display-only view over the existing persisted PlayerData.lives value.
public sealed class LivesHUD : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI livesText;

    private void OnEnable()
    {
        PlayerDataPersistenceManager.LivesChanged += Refresh;
        PlayerDataPersistenceManager.DataLoaded += HandleDataLoaded;
        Refresh(PlayerDataPersistenceManager.Instance != null
            ? PlayerDataPersistenceManager.Instance.Lives : new PlayerData().lives);
    }

    private void OnDisable()
    {
        PlayerDataPersistenceManager.LivesChanged -= Refresh;
        PlayerDataPersistenceManager.DataLoaded -= HandleDataLoaded;
    }

    private void HandleDataLoaded(PlayerData data) => Refresh(data != null ? data.lives : 0);

    private void Refresh(int lives)
    {
        if (livesText != null) livesText.text = Mathf.Max(0, lives).ToString();
    }
}
