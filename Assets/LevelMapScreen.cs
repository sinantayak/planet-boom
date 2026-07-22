using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;
using UnityEngine.UI;

[Serializable]
public sealed class LevelMapSectorVisual
{
    [Range(1, 10)] public int sectorNumber = 1;
    public string title = "SECTOR 1";
    public Sprite background;
    public Sprite island;
    public SectorMapLayout layout;
}

public static class LevelMapDefaultSelection
{
    public static LevelConfiguration Find(LevelConfigurationCatalog catalog, int firstLevel, int levelCount,
        Func<int, bool> isUnlocked, Func<int, int> getBestStars)
    {
        LevelConfiguration lastPlayable = null;
        for (int i = 0; i < levelCount; i++)
        {
            LevelConfiguration config = catalog != null ? catalog.FindByNumber(firstLevel + i) : null;
            if (config == null || !isUnlocked(config.levelNumber)) continue;
            lastPlayable = config;
            if (getBestStars(config.levelNumber) <= 0) return config;
        }
        return lastPlayable;
    }
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
    [Header("Bottom Navigation Artwork")]
    [FormerlySerializedAs("previousSectorArrowImage")]
    [SerializeField] private Image backButtonImage;
    [FormerlySerializedAs("previousSectorArrowSprite")]
    [SerializeField] private Sprite backButtonSprite;
    [FormerlySerializedAs("nextSectorArrowImage")]
    [SerializeField] private Image nextButtonImage;
    [FormerlySerializedAs("nextSectorArrowSprite")]
    [SerializeField] private Sprite nextButtonSprite;
    [SerializeField] private Image centerStatusBackground;
    [SerializeField] private Sprite centerStatusBackgroundSprite;
    [SerializeField] private List<LevelMapNodeUI> nodes = new();
    [SerializeField] private LevelMapOrbitPath orbitPath;
    [SerializeField] private string gameplaySceneName = "GameScene";
    [SerializeField, Range(1, 10)] private int initialSector = 1;
#if UNITY_EDITOR
    [SerializeField, Range(1, 10)] private int editorPreviewSector = 1;
#endif

    private int currentSector;
    private LevelConfiguration selected;
    private bool autoSelectRequested;

    private void OnEnable()
    {
        PlayerDataPersistenceManager.ProgressionChanged += HandleProgressionChanged;
        UnlockManager.UnlockChanged += HandleUnlockChanged;
    }

    private void OnDisable()
    {
        PlayerDataPersistenceManager.ProgressionChanged -= HandleProgressionChanged;
        UnlockManager.UnlockChanged -= HandleUnlockChanged;
    }

    private void Start()
    {
        ApplyBottomNavigationArtwork();
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
        if (playButton != null) playButton.interactable = true;
        foreach (LevelMapNodeUI node in nodes) node?.SetSelected(false);
        int localIndex = (config.levelNumber - 1) % 7;
        if (localIndex >= 0 && localIndex < nodes.Count) nodes[localIndex]?.SetSelected(true);
    }

    public void ShowSector(int sectorNumber)
    {
        currentSector = Mathf.Clamp(sectorNumber, 1, 10);
        selected = null;
        autoSelectRequested = true;
        foreach (LevelMapNodeUI node in nodes) node?.SetSelected(false);
        if (playButton != null) { playButton.gameObject.SetActive(true); playButton.interactable = false; }
        if (selectionText != null) selectionText.text = "Bir seviye seç";
        Refresh();
    }

    public void PreviousSector() => ShowSector(currentSector - 1);
    public void NextSector() => ShowSector(currentSector + 1);

    public void PlaySelected()
    {
        if (selected == null || !IsLevelUnlocked(selected.levelNumber)) return;
        CampaignLevelSelection.Select(selected);
        SceneTransition.LoadScene(gameplaySceneName);
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
        ApplyLayout(visual?.layout);
        BindSectorIslandArtwork(visual?.island);
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
        BindSectorIslandArtwork(visual?.island);
        orbitPath?.SetCompletedSegments(completedSegments);
        if (autoSelectRequested)
        {
            autoSelectRequested = false;
            AutoSelectCurrentProgressionLevel(firstLevel);
        }
    }

    private void AutoSelectCurrentProgressionLevel(int firstLevel)
    {
        PlayerDataPersistenceManager save = PlayerDataPersistenceManager.Instance;
        if (save != null && !save.IsLoaded)
        {
            autoSelectRequested = true;
            if (playButton != null) playButton.interactable = false;
            return;
        }
        LevelConfiguration target = LevelMapDefaultSelection.Find(levelCatalog, firstLevel, nodes.Count,
            IsLevelUnlocked, level => save != null ? save.GetBestStars(level) : 0);
        if (target != null) SelectLevel(target);
        else if (playButton != null) playButton.interactable = false;
    }

    private static bool IsLevelUnlocked(int levelNumber)
    {
        PlayerDataPersistenceManager save = PlayerDataPersistenceManager.Instance;
        return save == null ? levelNumber == 1 : save.IsLevelUnlocked(levelNumber);
    }

    private void HandleUnlockChanged(string _, bool __) => Refresh();

    private void HandleProgressionChanged()
    {
        // Re-read only the real PlayerData state. This also advances the default
        // selection after returning from a completed level or after a debug reset.
        selected = null;
        autoSelectRequested = true;
        if (playButton != null) playButton.interactable = false;
        Refresh();
    }

    private void ApplyLayout(SectorMapLayout layout, bool forceValidationApply = false)
    {
        if (layout == null) return;
        if (!forceValidationApply && !layout.completeHierarchyCaptured)
        {
            Debug.LogWarning($"SectorMapLayout '{layout.name}' has not passed complete-hierarchy round-trip validation. " +
                "The current scene transforms are preserved to prevent an Edit Mode -> Play Mode layout jump.", layout);
            return;
        }
        int count = Mathf.Min(nodes.Count, layout.nodes != null ? layout.nodes.Count : 0);
        for (int i = 0; i < count; i++)
        {
            SectorMapNodeLayout nodeLayout = layout.nodes[i];
            if (nodeLayout == null) continue;
            Vector2 position = nodeLayout.normalizedPathPosition;
            if (position.x < 0f || position.x > 1f || position.y < 0f || position.y > 1f)
                Debug.LogWarning($"SectorMapLayout '{layout.name}' node {i + 1} has out-of-range normalized position {position}. Runtime clamps it to 0..1; recapture the layout.", layout);
            Vector2 rootPosition = nodeLayout.normalizedRootCenter;
            if (rootPosition.x < 0f || rootPosition.x > 1f || rootPosition.y < 0f || rootPosition.y > 1f)
                Debug.LogWarning($"SectorMapLayout '{layout.name}' node {i + 1} has out-of-range normalized root center {rootPosition}. Runtime clamps it to 0..1; recapture the layout.", layout);
            nodes[i]?.ApplyVisualLayout(nodeLayout);
        }
        if (sectorBackground != null)
        {
            RectTransform rect = sectorBackground.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = layout.backgroundPosition;
            rect.sizeDelta = layout.backgroundSize;
            rect.localScale = layout.backgroundScale;
        }
        orbitPath?.SetVerticesDirty();
    }

    private void BindSectorIslandArtwork(Sprite island)
    {
        if (island == null) return;
        for (int i = 0; i < nodes.Count; i++) nodes[i]?.BindIslandSprite(island);
    }

    private void ApplyBottomNavigationArtwork()
    {
        ApplyOptionalSprite(backButtonImage, backButtonSprite);
        ApplyOptionalSprite(nextButtonImage, nextButtonSprite);
        ApplyOptionalSprite(centerStatusBackground, centerStatusBackgroundSprite);
    }

    private static void ApplyOptionalSprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null) return;
        image.sprite = sprite;
        image.enabled = true;
        image.preserveAspect = true;
        Color color = image.color;
        color.a = 1f;
        image.color = color;
        Transform fallback = image.transform.parent != null ? image.transform.parent.Find("Label") : null;
        if (fallback != null) fallback.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    public int EditorPreviewSector => editorPreviewSector;
    public IReadOnlyList<LevelMapNodeUI> EditorNodes => nodes;
    public Image EditorSectorBackground => sectorBackground;
    public RectTransform EditorLayoutRoot => nodes != null && nodes.Count > 0 && nodes[0] != null
        ? nodes[0].RootRect.parent as RectTransform : null;
    public LevelMapSectorVisual EditorFindSector(int sectorNumber) =>
        sectors?.Find(item => item != null && item.sectorNumber == sectorNumber);
    public void EditorApplyPreviewSector()
    {
        currentSector = Mathf.Clamp(editorPreviewSector, 1, 10);
        LevelMapSectorVisual visual = EditorFindSector(currentSector);
        if (sectorBackground != null) { sectorBackground.sprite = visual?.background; sectorBackground.enabled = visual?.background != null; }
        BindSectorIslandArtwork(visual?.island);
        ApplyLayout(visual?.layout);
    }
    public void EditorApplyLayoutForValidation(SectorMapLayout layout) => ApplyLayout(layout, true);
    public void EditorApplySectorForValidation(int sectorNumber)
    {
        LevelMapSectorVisual visual = EditorFindSector(sectorNumber);
        ApplyLayout(visual?.layout, true);
        BindSectorIslandArtwork(visual?.island);
    }
#endif

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
