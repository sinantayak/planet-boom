using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Planet evolution roadmap popup, opened from the top-left Evolution
// (hamburger) button in GameScene. Follows the BreakMenuUI pattern exactly:
// this component lives on an always-active root while the animated popup
// visuals sit on an initially-inactive child (popupRoot) driven by
// PopupTransition.
//
// Pause rules are shared with the break menu rather than duplicated:
// TryPauseForBreakMenu only succeeds from an unpaused Playing state, so this
// popup can never stack on top of Pre-Level, the skill inventory or the
// break menu — and none of them can open on top of it either. Gameplay
// resumes only in the close animation's completion callback.
//
// Per-node presentation states, in priority order — all DERIVED live from
// the existing campaign data (LevelConfiguration.unlockRewards + the
// player's progression frontier), so no "seen/acknowledged" persistence is
// needed and none is written:
//   1. Unlocked + reward of the CURRENT sector → full color + "NEW" label
//      (stays for the remainder of the sector, however often the popup is
//      opened; vanishes by itself once the player progresses to the next
//      sector, because the current sector changes)
//   2. Unlocked otherwise (earlier sector / default unlock) → full color
//   3. Locked + reward of the CURRENT sector → shining silhouette +
//      "UPCOMING" label
//   4. Locked otherwise (future sectors) → plain dark silhouette
// Real artwork is revealed only once a planet is actually unlocked.
public sealed class EvolutionPopupUI : MonoBehaviour
{
    [Serializable]
    public sealed class PlanetNode
    {
        public PlanetTier tier = PlanetTier.Tier1;
        public Image icon;
        // Optional per-node presentation children; runtime only toggles
        // their active state (and the label's text/state color), never
        // their authored RectTransforms.
        public GameObject upcomingGlow;
        // Legacy sprite badge slot from the earlier NEW-badge iteration.
        // No longer driven by any state — runtime only makes sure a stale
        // active badge is switched off.
        public GameObject newBadge;
        public TMP_Text statusText;
    }

    [SerializeField] private GameObject popupRoot;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button continueButton;

    [Header("Evolution Nodes")]
    // One entry per roadmap planet; layout (positions/sizes/connectors) is
    // authored in the scene and never touched at runtime.
    [SerializeField] private PlanetNode[] planetNodes;
    // Tint for tiers the player has not unlocked yet: dark enough that the
    // pre-rendered art reads as a pure silhouette without revealing its
    // colors or details.
    [SerializeField] private Color silhouetteColor = new Color(0.07f, 0.09f, 0.16f, 1f);

    [Header("Status Labels")]
    // Central wording/state colors for every node's StatusText. Everything
    // else about the labels (font, size, position, scale) is authored on the
    // StatusText objects themselves and never overwritten.
    [SerializeField] private string upcomingLabel = "UPCOMING";
    [SerializeField] private string newLabel = "NEW";
    [SerializeField] private Color upcomingLabelColor = new Color(0.62f, 0.82f, 1f, 1f);
    [SerializeField] private Color newLabelColor = new Color(1f, 0.84f, 0.25f, 1f);

    private void Awake()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (continueButton != null) continueButton.onClick.AddListener(Close);
        if (popupRoot != null) popupRoot.SetActive(false);
    }

    public void Open()
    {
        if (popupRoot == null || popupRoot.activeSelf || GameManager.Instance == null) return;
        if (!GameManager.Instance.TryPauseForBreakMenu()) return;
        RefreshNodes();
        PopupTransition.Open(popupRoot);
        popupRoot.transform.SetAsLastSibling();
    }

    // Shared by the X and CONTINUE buttons. The game stays GamePaused
    // (timeScale 0) for the whole unscaled close animation; repeated taps are
    // absorbed by PopupTransition's closing guard. Closing has no effect on
    // status labels — NEW persists for the rest of the sector.
    public void Close()
    {
        if (GameManager.Instance == null ||
            GameManager.Instance.State != GameManager.GameState.GamePaused) return;
        PopupTransition.Close(popupRoot,
            () => GameManager.Instance?.TryResumeFromBreakMenu());
    }

    private void RefreshNodes()
    {
        if (planetNodes == null) return;

        UnlockManager unlocks = UnlockManager.Instance;
        HashSet<PlanetTier> sectorRewardTiers = GetCurrentSectorRewardTiers();

        foreach (PlanetNode node in planetNodes)
        {
            if (node == null || node.icon == null) continue;

            bool unlocked = unlocks != null && unlocks.IsUnlocked(node.tier);
            bool sectorReward = sectorRewardTiers.Contains(node.tier);
            bool upcoming = !unlocked && sectorReward;
            bool newlyUnlocked = unlocked && sectorReward;

            // The sprites are pre-rendered and must never be color-graded:
            // pure white means "shown exactly as authored".
            node.icon.color = unlocked ? Color.white : silhouetteColor;
            if (node.upcomingGlow != null && node.upcomingGlow.activeSelf != upcoming)
                node.upcomingGlow.SetActive(upcoming);
            if (node.newBadge != null && node.newBadge.activeSelf)
                node.newBadge.SetActive(false);

            if (newlyUnlocked) ShowStatus(node, newLabel, newLabelColor);
            else if (upcoming) ShowStatus(node, upcomingLabel, upcomingLabelColor);
            else HideStatus(node);
        }
    }

    private static void HideStatus(PlanetNode node)
    {
        if (node.statusText != null && node.statusText.gameObject.activeSelf)
            node.statusText.gameObject.SetActive(false);
    }

    private void ShowStatus(PlanetNode node, string label, Color color)
    {
        if (node.statusText == null) return;
        node.statusText.text = label;
        node.statusText.color = color;
        if (!node.statusText.gameObject.activeSelf)
            node.statusText.gameObject.SetActive(true);
    }

    // Planet-tier unlock rewards configured on the levels of the player's
    // CURRENT sector. Purely data-driven: any number of planet rewards per
    // sector is supported and nothing (sector, level, tier) is hardcoded.
    private HashSet<PlanetTier> GetCurrentSectorRewardTiers()
    {
        var result = new HashSet<PlanetTier>();
        GameManager manager = GameManager.Instance;
        LevelConfigurationCatalog catalog = manager != null ? manager.LevelCatalog : null;
        string sectorId = ResolveCurrentSectorId(catalog);
        if (catalog == null || catalog.levels == null || string.IsNullOrEmpty(sectorId))
            return result;

        foreach (LevelConfiguration level in catalog.levels)
        {
            if (level == null || level.unlockRewards == null ||
                !string.Equals(level.sectorId, sectorId, StringComparison.Ordinal))
                continue;
            foreach (LevelUnlockReward reward in level.unlockRewards)
                if (TryGetPlanetRewardTier(reward, out PlanetTier tier))
                    result.Add(tier);
        }
        return result;
    }

    // The player's CURRENT sector is defined by their progression frontier —
    // the sector of the highest unlocked (not yet completed) campaign level.
    // Completing a sector's final level unlocks the next sector's first
    // level, which moves the frontier and thereby retires that sector's NEW
    // labels automatically. Replaying an old level does not bring them back.
    private static string ResolveCurrentSectorId(LevelConfigurationCatalog catalog)
    {
        if (catalog == null || catalog.levels == null)
            return null;

        PlayerDataPersistenceManager persistence = PlayerDataPersistenceManager.Instance;
        if (persistence != null && persistence.IsLoaded)
        {
            int frontierNumber = persistence.HighestUnlockedLevel;
            LevelConfiguration frontier = catalog.FindByNumber(frontierNumber);
            if (frontier != null)
                return frontier.sectorId;

            // Frontier past the authored campaign: every sector is finished,
            // so nothing is "current" — no NEW / UPCOMING labels at all.
            int highestAuthored = 0;
            foreach (LevelConfiguration level in catalog.levels)
                if (level != null && level.levelNumber > highestAuthored)
                    highestAuthored = level.levelNumber;
            if (frontierNumber > highestAuthored)
                return null;
        }

        // Progression not loaded yet, or an authoring gap in level numbers:
        // fall back to the sector of the level currently being played.
        LevelConfiguration active = GameManager.Instance != null
            ? GameManager.Instance.ActiveLevelConfiguration : null;
        return active != null ? active.sectorId : null;
    }

    private static bool TryGetPlanetRewardTier(LevelUnlockReward reward, out PlanetTier tier)
    {
        tier = PlanetTier.Tier1;
        return reward != null && reward.contentType == UnlockContentType.Planet &&
               !string.IsNullOrWhiteSpace(reward.stableContentId) &&
               Enum.TryParse(reward.stableContentId.Trim(), true, out tier) &&
               Enum.IsDefined(typeof(PlanetTier), tier);
    }
}
