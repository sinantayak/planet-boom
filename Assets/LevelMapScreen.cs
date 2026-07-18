using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[Serializable]
public sealed class LevelMapSectorVisual
{
    [Range(1, 10)] public int sectorNumber = 1;
    public string title = "SECTOR 1";
    public Sprite background;
    public Sprite island;
}

public sealed class LevelMapScreen : MonoBehaviour
{
    [SerializeField] private LevelConfigurationCatalog levelCatalog;
    [SerializeField] private List<LevelMapSectorVisual> sectors = new();
    [SerializeField] private LevelMapRewardIconLibrary rewardIcons;
    [SerializeField] private Image sectorBackground;
    [SerializeField] private TMP_Text sectorTitle;
    [SerializeField] private TMP_Text selectionText;
    [SerializeField] private Button previousSectorButton;
    [SerializeField] private Button nextSectorButton;
    [SerializeField] private Button playButton;
    [SerializeField] private List<LevelMapNodeUI> nodes = new();
    [SerializeField] private LevelMapOrbitPath orbitPath;
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField, Range(1, 10)] private int initialSector = 1;

    private int currentSector;
    private LevelConfiguration selected;

    private void OnEnable()
    {
        PlayerDataPersistenceManager.ProgressionChanged += Refresh;
        UnlockManager.UnlockChanged += HandleUnlockChanged;
    }

    private void OnDisable()
    {
        PlayerDataPersistenceManager.ProgressionChanged -= Refresh;
        UnlockManager.UnlockChanged -= HandleUnlockChanged;
    }

    private void Start()
    {
        previousSectorButton?.onClick.AddListener(PreviousSector);
        nextSectorButton?.onClick.AddListener(NextSector);
        playButton?.onClick.AddListener(PlaySelected);
        ShowSector(initialSector);
    }

    public void SelectLevel(LevelConfiguration config)
    {
        if (config == null || !IsLevelUnlocked(config.levelNumber)) return;
        selected = config;
        CampaignLevelSelection.Select(config);
        if (selectionText != null) selectionText.text = config.displayName;
        if (playButton != null) playButton.gameObject.SetActive(true);
        foreach (LevelMapNodeUI node in nodes) node?.SetSelected(false);
        int localIndex = (config.levelNumber - 1) % 7;
        if (localIndex >= 0 && localIndex < nodes.Count) nodes[localIndex]?.SetSelected(true);
    }

    public void ShowSector(int sectorNumber)
    {
        currentSector = Mathf.Clamp(sectorNumber, 1, 10);
        selected = null;
        if (playButton != null) playButton.gameObject.SetActive(false);
        if (selectionText != null) selectionText.text = "Bir seviye seç";
        Refresh();
    }

    public void PreviousSector() => ShowSector(currentSector - 1);
    public void NextSector() => ShowSector(currentSector + 1);

    public void PlaySelected()
    {
        if (selected == null || !IsLevelUnlocked(selected.levelNumber)) return;
        CampaignLevelSelection.Select(selected);
        SceneManager.LoadScene(gameplaySceneName);
    }

    private void Refresh()
    {
        LevelMapSectorVisual visual = sectors?.Find(item => item != null && item.sectorNumber == currentSector);
        if (sectorTitle != null) sectorTitle.text = visual != null ? visual.title : $"SECTOR {currentSector}";
        if (sectorBackground != null)
        {
            sectorBackground.sprite = visual?.background;
            sectorBackground.enabled = visual?.background != null;
        }
        previousSectorButton.interactable = currentSector > 1;
        nextSectorButton.interactable = currentSector < 10;

        int firstLevel = (currentSector - 1) * 7 + 1;
        int completedSegments = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            LevelConfiguration config = levelCatalog != null ? levelCatalog.FindByNumber(firstLevel + i) : null;
            bool unlocked = config != null && IsLevelUnlocked(config.levelNumber);
            int stars = config != null && PlayerDataPersistenceManager.Instance != null
                ? PlayerDataPersistenceManager.Instance.GetBestStars(config.levelNumber) : 0;
            nodes[i]?.Bind(this, config, visual?.island, unlocked, stars, rewardIcons);
            if (i < nodes.Count - 1 && stars > 0) completedSegments++;
        }
        orbitPath?.SetCompletedSegments(completedSegments);
    }

    private static bool IsLevelUnlocked(int levelNumber)
    {
        PlayerDataPersistenceManager save = PlayerDataPersistenceManager.Instance;
        return save == null ? levelNumber == 1 : save.IsLevelUnlocked(levelNumber);
    }

    private void HandleUnlockChanged(string _, bool __) => Refresh();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("DEBUG Map/Fresh Player Preview")]
    private void DebugFresh() => Debug.Log("Map debug: fresh = L1 available; clear/reset is intentionally not performed.", this);
    [ContextMenu("DEBUG Map/Log Current State")]
    private void DebugLog() => Debug.Log($"Map debug: sector={currentSector}, highest=" +
        (PlayerDataPersistenceManager.Instance != null ? PlayerDataPersistenceManager.Instance.HighestUnlockedLevel : 1), this);
    [ContextMenu("DEBUG Map/Partial Sector 1 (L1-L3)")]
    private void DebugPartial()
    {
        PlayerDataPersistenceManager save = PlayerDataPersistenceManager.Instance;
        if (save == null || !save.IsLoaded) { Debug.LogWarning("Save data is not ready.", this); return; }
        save.RecordLevelCompleted(1, 3); save.RecordLevelCompleted(2, 2); save.RecordLevelCompleted(3, 1);
    }
    [ContextMenu("DEBUG Map/Complete Sector 1")]
    private void DebugCompleteSector()
    {
        PlayerDataPersistenceManager save = PlayerDataPersistenceManager.Instance;
        if (save == null || !save.IsLoaded) { Debug.LogWarning("Save data is not ready.", this); return; }
        for (int level = 1; level <= 7; level++) save.RecordLevelCompleted(level, (level % 3) + 1);
    }
#endif
}
