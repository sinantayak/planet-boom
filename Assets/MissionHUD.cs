using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Permanent top-left HUD showing the current mission: a "LEVEL N" title and
// up to 3 target planet icons side by side. Stays active and visible for the
// whole session — it is NOT the win popup (that's LevelCompletePanel). Driven
// entirely by GameManager: ShowLevel on level start, MarkAchieved the instant
// a merge fulfils a target, so progress reads live while playing. Holds no
// game state of its own.
public class MissionHUD : MonoBehaviour
{
    [Header("Wiring")]
    // The HUD title TextMeshPro; becomes "LEVEL 1", "LEVEL 2", ...
    [SerializeField] private TextMeshProUGUI levelTitleText;

    // Exactly GameManager.MaxTargetsPerLevel slots, ordered left to right.
    // Slots beyond the current level's target count are deactivated, so a
    // 1-target level shows one icon, not one icon and two empty frames.
    [SerializeField] private Image[] targetSlots = new Image[GameManager.MaxTargetsPerLevel];

    // Sprite source: the shared planet prefab's Planet component. Reading art
    // through GetSpriteForTier keeps the HUD icons pixel-identical to the
    // planets that actually spawn in the arena.
    [SerializeField] private Planet planetPrefab;

    [Header("Achieved Look")]
    // Achieved icons stay visible but dim to this tint, so the player can see
    // both what is done and what remains at a glance mid-game. (The never-tint
    // rule protects the in-world planet renderers; this is deliberate UI state
    // feedback.)
    [SerializeField] private Color achievedTint = new Color(0.45f, 0.45f, 0.45f, 0.9f);

    void Awake()
    {
        if (planetPrefab == null)
        {
            Debug.LogWarning("MissionHUD: no planet prefab assigned — target slots can't resolve sprites and will hide.", this);
        }
    }

    // Rebuilds the HUD for a level: title text, one icon per target (with
    // duplicates rendered as separate identical sprites — two Tier5 targets
    // are two Tier5 icons, never a "2x" label), all tints reset to pending,
    // and leftover slots hidden.
    public void ShowLevel(int levelNumber, IReadOnlyList<PlanetTier> targets)
    {
        if (levelTitleText != null)
        {
            levelTitleText.text = $"LEVEL {levelNumber}";
        }

        for (int i = 0; i < targetSlots.Length; i++)
        {
            Image slot = targetSlots[i];
            if (slot == null)
                continue;

            Sprite sprite = null;
            if (targets != null && i < targets.Count && planetPrefab != null)
            {
                sprite = planetPrefab.GetSpriteForTier(targets[i]);
            }

            // No target for this slot (or no art for the tier yet): hide the
            // whole slot object rather than showing an empty/white frame.
            if (sprite == null)
            {
                slot.gameObject.SetActive(false);
                continue;
            }

            slot.sprite = sprite;
            slot.preserveAspect = true;
            slot.color = Color.white;
            slot.gameObject.SetActive(true);
        }
    }

    // Dims the given icon (index-aligned with GameManager's target list) the
    // moment its target is met in the arena. ShowLevel resets the tint on the
    // next level.
    public void MarkAchieved(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= targetSlots.Length)
            return;

        Image slot = targetSlots[slotIndex];
        if (slot != null)
        {
            slot.color = achievedTint;
        }
    }
}
