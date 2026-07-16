using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Phase 3B presentation controller. Every visual object is authored in
// GameScene; this component only binds behavior and reads counts/assignments
// from SkillInventoryManager. No UI hierarchy or inventory data is created here.
public class SkillInventoryUI : MonoBehaviour
{
    [Header("Existing Scene Wiring")]
    [SerializeField] private RectTransform canvasRoot;
    [SerializeField] private RectTransform safeAreaRoot;
    [SerializeField] private Button chestButton;

    [Header("Scene UI Content")]
    [SerializeField] private Sprite selectedSlotHighlight;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Sprite[] skillIcons = Array.Empty<Sprite>();

    [SerializeField] private Color unavailableIconColor = new Color(0.35f, 0.35f, 0.35f, 0.75f);

    [Header("Selected Popup Slot Feedback")]
    [SerializeField] private float selectedSlotScale = 1.08f;
    [SerializeField] private Color selectedSlotTint = new Color(0.55f, 0.95f, 1f, 1f);

    [Header("HUD Skill Use Feedback")]
    [SerializeField] private float successPulseDuration = 0.22f;
    [SerializeField] private float successPulseScale = 1.14f;
    [SerializeField] private Color successFlashColor = new Color(0.55f, 1f, 0.8f, 1f);
    [SerializeField] private float failureShakeDuration = 0.28f;
    [SerializeField] private float failureShakeDistance = 10f;
    [SerializeField] private float failureShakeCycles = 4f;
    [SerializeField] private Color failureFlashColor = new Color(1f, 0.3f, 0.3f, 1f);

    [Header("Timed Effect Radial Indicator")]
    [SerializeField] private Sprite timedEffectRadialSprite;
    [SerializeField] private Color timedEffectOverlayColor = new Color(0.025f, 0.045f, 0.12f, 0.72f);
    [SerializeField] private bool showTimedEffectSeconds = true;
    [SerializeField] private Color timedEffectTextColor = Color.white;
    [SerializeField] [Min(8f)] private float timedEffectTextFontSize = 28f;

    // Hooks for Phase 3C feedback (shake/toast/audio). UI does not invent a
    // failure animation yet, but callers receive the exact failed operation.
    public event Action<int, SkillType?> QuickSlotUseFailed;
    public event Action<int, SkillType> QuickSlotUseSucceeded;
    public event Action<int, SkillType?, QuickSlotFailureReason> QuickSlotUseFailureDetailed;
    public event Action<int, SkillType> QuickSlotAssignmentFailed;

    public enum QuickSlotFailureReason
    {
        EmptySlot,
        NoInventoryCount,
        ExecutionRejected,
        InventoryUnavailable
    }

    private sealed class SlotView
    {
        public Button button;
        public RectTransform root;
        public Image frame;
        public Image icon;
        public GameObject badgeRoot;
        public TextMeshProUGUI countText;
        public Image selectedHighlight;
        public Vector3 baseScale;
        public Vector2 basePosition;
        public Color baseFrameColor;
        public Coroutine feedbackRoutine;
        public TimedEffectRadialIndicator timedEffectIndicator;
    }

    private sealed class EntryView
    {
        public Image icon;
        public TextMeshProUGUI countText;
    }

    private sealed class ButtonBinding
    {
        public Button button;
        public UnityAction action;
    }

    private readonly List<SlotView> hudSlots = new List<SlotView>();
    private readonly List<SlotView> popupSlots = new List<SlotView>();
    private readonly Dictionary<SkillType, EntryView> entries = new Dictionary<SkillType, EntryView>();
    private readonly List<ButtonBinding> buttonBindings = new List<ButtonBinding>();

    private SkillInventoryManager inventory;
    private PlanetLauncher shieldLauncher;
    private GameObject popupRoot;
    private TextMeshProUGUI chestCountText;
    private Button popupCloseButton;
    private Button clearSlotButton;
    private int selectedSlotIndex;
    private bool popupOpen;

    void Awake()
    {
        ResolveSceneReferences();
        ResolveAuthoredUI();
        WireButtons();
    }

    void Start()
    {
        BindInventory();
        BindShieldLauncher();
    }

    void OnEnable()
    {
        BindInventory();
        BindShieldLauncher();
    }

    void OnDisable()
    {
        ResetAllHudFeedback();
        UnbindShieldLauncher();
        UnbindInventory();
        ClosePopup();
    }

    void OnDestroy()
    {
        UnwireButtons();
    }

    public void OpenPopup()
    {
        if (popupOpen || popupRoot == null || GameManager.Instance == null)
            return;

        if (!GameManager.Instance.TryPauseForInventory())
            return;

        popupOpen = true;
        selectedSlotIndex = Mathf.Clamp(selectedSlotIndex, 0, SkillInventoryManager.QuickSlotCount - 1);
        popupRoot.SetActive(true);
        popupRoot.transform.SetAsLastSibling();
        RefreshAll();
    }

    public void ClosePopup()
    {
        if (!popupOpen)
            return;

        popupOpen = false;
        if (popupRoot != null)
            popupRoot.SetActive(false);
        if (GameManager.Instance != null)
            GameManager.Instance.TryResumeFromInventory();
    }

    private void BindInventory()
    {
        SkillInventoryManager current = SkillInventoryManager.Instance;
        if (inventory == current)
        {
            if (inventory != null) RefreshAll();
            return;
        }

        UnbindInventory();
        inventory = current;
        if (inventory == null)
            return;

        inventory.InventoryCountChanged += HandleInventoryCountChanged;
        inventory.QuickSlotChanged += HandleQuickSlotChanged;
        RefreshAll();
    }

    private void UnbindInventory()
    {
        if (inventory == null)
            return;
        inventory.InventoryCountChanged -= HandleInventoryCountChanged;
        inventory.QuickSlotChanged -= HandleQuickSlotChanged;
        inventory = null;
    }

    private void HandleInventoryCountChanged(SkillType type, int count)
    {
        RefreshChestBadge();
        RefreshEntry(type);
        RefreshAllSlots();
    }

    private void HandleQuickSlotChanged(int slotIndex, SkillType? assignment)
    {
        RefreshSlot(hudSlots, slotIndex, false);
        RefreshSlot(popupSlots, slotIndex, true);
    }

    private void UseQuickSlot(int slotIndex)
    {
        if (inventory == null)
            BindInventory();

        SkillType? assignment = inventory != null && inventory.TryGetQuickSlot(slotIndex, out SkillType type)
            ? type
            : (SkillType?)null;
        int countBeforeUse = assignment.HasValue && inventory != null
            ? inventory.GetCount(assignment.Value)
            : 0;
        bool succeeded = inventory != null && inventory.TryUseQuickSlot(slotIndex);
        if (succeeded && assignment.HasValue)
        {
            PlayHudSlotFeedback(slotIndex, true);
            if (assignment.Value == SkillType.CosmicShield)
                StartCosmicShieldIndicator(slotIndex);
            QuickSlotUseSucceeded?.Invoke(slotIndex, assignment.Value);
        }
        else
        {
            PlayHudSlotFeedback(slotIndex, false);
            QuickSlotUseFailed?.Invoke(slotIndex, assignment);
            QuickSlotFailureReason reason = inventory == null
                ? QuickSlotFailureReason.InventoryUnavailable
                : !assignment.HasValue
                    ? QuickSlotFailureReason.EmptySlot
                    : countBeforeUse <= 0
                        ? QuickSlotFailureReason.NoInventoryCount
                        : QuickSlotFailureReason.ExecutionRejected;
            QuickSlotUseFailureDetailed?.Invoke(slotIndex, assignment, reason);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"QUICK SLOT UI: slot {slotIndex + 1} use failed " +
                      $"({assignment?.ToString() ?? "Empty"}, reason={reason}).", this);
#endif
        }
    }

    private void SelectPopupSlot(int slotIndex)
    {
        selectedSlotIndex = slotIndex;
        RefreshAllSlots();
    }

    private void AssignSelectedSlot(SkillType type)
    {
        if (inventory == null)
            BindInventory();

        bool succeeded = inventory != null && inventory.TryAssignQuickSlot(selectedSlotIndex, type);
        if (!succeeded)
        {
            QuickSlotAssignmentFailed?.Invoke(selectedSlotIndex, type);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"INVENTORY UI: assigning {type} to slot {selectedSlotIndex + 1} failed.", this);
#endif
        }
    }

    private void ClearSelectedSlot()
    {
        if (inventory == null)
            BindInventory();
        inventory?.ClearQuickSlot(selectedSlotIndex);
    }

    private void RefreshAll()
    {
        RefreshChestBadge();
        RefreshAllSlots();
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            RefreshEntry(type);
    }

    private void RefreshChestBadge()
    {
        if (chestCountText == null || inventory == null)
            return;

        long total = 0;
        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            total += inventory.GetCount(type);
        chestCountText.text = total.ToString();
    }

    private void RefreshAllSlots()
    {
        for (int i = 0; i < SkillInventoryManager.QuickSlotCount; i++)
        {
            RefreshSlot(hudSlots, i, false);
            RefreshSlot(popupSlots, i, true);
        }
    }

    private void RefreshSlot(List<SlotView> views, int index, bool popupSlot)
    {
        if (inventory == null || index < 0 || index >= views.Count)
            return;

        SlotView view = views[index];
        bool assigned = inventory.TryGetQuickSlot(index, out SkillType type);
        int count = assigned ? inventory.GetCount(type) : 0;

        view.icon.sprite = assigned ? IconFor(type) : emptySlotSprite;
        view.icon.enabled = view.icon.sprite != null;
        view.icon.color = assigned && count <= 0 ? unavailableIconColor : Color.white;
        view.badgeRoot.SetActive(assigned);
        view.countText.text = count.ToString();
        view.button.interactable = popupSlot || (assigned && count > 0);
        if (view.selectedHighlight != null)
            view.selectedHighlight.gameObject.SetActive(
                selectedSlotHighlight != null && popupSlot && selectedSlotIndex == index);
        bool selected = popupSlot && selectedSlotIndex == index;
        view.root.localScale = selected
            ? view.baseScale * Mathf.Max(1f, selectedSlotScale)
            : view.baseScale;
        view.frame.color = selected ? selectedSlotTint : view.baseFrameColor;
    }

    private void RefreshEntry(SkillType type)
    {
        if (inventory == null || !entries.TryGetValue(type, out EntryView view))
            return;
        int count = inventory.GetCount(type);
        view.icon.sprite = IconFor(type);
        view.icon.enabled = view.icon.sprite != null;
        view.icon.color = count > 0 ? Color.white : unavailableIconColor;
        view.countText.text = count.ToString();
    }

    private Sprite IconFor(SkillType type)
    {
        int index = (int)type;
        if (skillIcons != null && index >= 0 && index < skillIcons.Length && skillIcons[index] != null)
            return skillIcons[index];
        return SkillFlightManager.Instance != null ? SkillFlightManager.Instance.GetSkillIcon(type) : null;
    }

    private void ResolveSceneReferences()
    {
        if (chestButton != null && canvasRoot == null)
        {
            Canvas canvas = chestButton.GetComponentInParent<Canvas>();
            if (canvas != null) canvasRoot = canvas.transform as RectTransform;
        }
    }

    private void ResolveAuthoredUI()
    {
        hudSlots.Clear();
        popupSlots.Clear();
        entries.Clear();

        if (canvasRoot == null || safeAreaRoot == null)
            return;

        Transform hudBar = safeAreaRoot.Find("QuickSlotBar");
        if (hudBar != null)
        {
            for (int i = 0; i < SkillInventoryManager.QuickSlotCount && i < hudBar.childCount; i++)
            {
                SlotView view = ResolveSlotView(hudBar.GetChild(i));
                ConfigureTimedEffectIndicator(view);
                hudSlots.Add(view);
            }
        }

        Transform popup = canvasRoot.Find("SkillInventoryPopup");
        popupRoot = popup != null ? popup.gameObject : null;
        if (popup == null)
            return;

        Transform panel = popup.Find("Panel");
        if (panel == null)
            return;

        popupCloseButton = panel.Find("CloseButton")?.GetComponent<Button>();
        clearSlotButton = panel.Find("ClearSelectedSlot")?.GetComponent<Button>();

        Transform popupRow = panel.Find("PopupQuickSlots");
        if (popupRow != null)
        {
            for (int i = 0; i < SkillInventoryManager.QuickSlotCount && i < popupRow.childCount; i++)
                popupSlots.Add(ResolveSlotView(popupRow.GetChild(i)));
        }

        Transform content = panel.Find("SkillGridViewport/Content");
        if (content != null)
        {
            EnsureSkillGridEntry(content, SkillType.PlanetReroll);
            EnsureSkillGridEntry(content, SkillType.CosmicShield);
            EnsureSkillGridEntry(content, SkillType.CosmicAbduction);
            EnsureSkillGridEntry(content, SkillType.MeteorShower);
            foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            {
                Transform entry = content.Find(type.ToString());
                if (entry == null)
                    continue;
                entries[type] = new EntryView
                {
                    icon = entry.Find("Icon")?.GetComponent<Image>(),
                    countText = entry.Find("Count")?.GetComponent<TextMeshProUGUI>()
                };
            }
        }

        Transform badge = chestButton != null ? chestButton.transform.Find("TotalSkillBadge/Count") : null;
        chestCountText = badge != null ? badge.GetComponent<TextMeshProUGUI>() : null;
    }

    // Older authored scenes have the original four entries. Reuse that exact
    // layout for the appended skill; its icon remains Inspector-assigned.
    private static void EnsureSkillGridEntry(Transform content, SkillType type)
    {
        if (content.Find(type.ToString()) != null)
            return;

        Transform template = content.Find(nameof(SkillType.CosmicMimic));
        if (template == null)
            return;

        GameObject clone = Instantiate(template.gameObject, content);
        clone.name = type.ToString();
    }

    private static SlotView ResolveSlotView(Transform root)
    {
        Transform badge = root.Find("CountBadge");
        RectTransform rect = root as RectTransform;
        Image frame = root.GetComponent<Image>();
        return new SlotView
        {
            root = rect,
            button = root.GetComponent<Button>(),
            frame = frame,
            icon = root.Find("Icon")?.GetComponent<Image>(),
            selectedHighlight = root.Find("Selected")?.GetComponent<Image>(),
            badgeRoot = badge != null ? badge.gameObject : null,
            countText = badge != null ? badge.Find("Count")?.GetComponent<TextMeshProUGUI>() : null,
            baseScale = rect != null ? rect.localScale : Vector3.one,
            basePosition = rect != null ? rect.anchoredPosition : Vector2.zero,
            baseFrameColor = frame != null ? frame.color : Color.white
        };
    }

    private void ConfigureTimedEffectIndicator(SlotView view)
    {
        if (view == null || view.root == null || view.icon == null)
            return;
        view.timedEffectIndicator = view.root.GetComponent<TimedEffectRadialIndicator>();
        if (view.timedEffectIndicator == null)
            view.timedEffectIndicator = view.root.gameObject.AddComponent<TimedEffectRadialIndicator>();
        view.timedEffectIndicator.Initialize(view.icon, timedEffectRadialSprite, timedEffectOverlayColor,
            showTimedEffectSeconds, timedEffectTextColor, timedEffectTextFontSize);
    }

    private void BindShieldLauncher()
    {
        PlanetLauncher current = FindFirstObjectByType<PlanetLauncher>();
        if (shieldLauncher == current)
            return;
        UnbindShieldLauncher();
        shieldLauncher = current;
        if (shieldLauncher != null)
            shieldLauncher.CosmicShieldStateChanged += HandleCosmicShieldStateChanged;
    }

    private void UnbindShieldLauncher()
    {
        if (shieldLauncher != null)
            shieldLauncher.CosmicShieldStateChanged -= HandleCosmicShieldStateChanged;
        shieldLauncher = null;
    }

    private void HandleCosmicShieldStateChanged(bool active)
    {
        if (active)
            return;
        foreach (SlotView view in hudSlots)
            view.timedEffectIndicator?.Clear();
    }

    private void StartCosmicShieldIndicator(int slotIndex)
    {
        BindShieldLauncher();
        if (shieldLauncher == null || !shieldLauncher.IsCosmicShieldActive ||
            slotIndex < 0 || slotIndex >= hudSlots.Count)
            return;

        hudSlots[slotIndex].timedEffectIndicator?.Begin(
            shieldLauncher.CosmicShieldActiveDurationSeconds,
            () => shieldLauncher != null ? shieldLauncher.CosmicShieldRemainingSeconds : 0f,
            () => shieldLauncher != null && shieldLauncher.IsCosmicShieldActive);
    }

    private void PlayHudSlotFeedback(int slotIndex, bool succeeded)
    {
        if (slotIndex < 0 || slotIndex >= hudSlots.Count)
            return;

        SlotView view = hudSlots[slotIndex];
        ResetSlotFeedback(view);
        // LayoutGroups may resolve anchored positions after Awake; capture the
        // actual authored/layout position at the moment feedback begins.
        view.basePosition = view.root.anchoredPosition;
        view.feedbackRoutine = StartCoroutine(AnimateHudSlotFeedback(view, succeeded));
    }

    private IEnumerator AnimateHudSlotFeedback(SlotView view, bool succeeded)
    {
        float duration = Mathf.Max(0.05f, succeeded ? successPulseDuration : failureShakeDuration);
        float elapsed = 0f;
        while (elapsed < duration && view.root != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float envelope = Mathf.Sin(t * Mathf.PI);

            if (succeeded)
            {
                view.root.localScale = view.baseScale * Mathf.Lerp(1f, successPulseScale, envelope);
                if (view.frame != null)
                    view.frame.color = Color.Lerp(view.baseFrameColor, successFlashColor, envelope);
            }
            else
            {
                float shake = Mathf.Sin(t * Mathf.PI * 2f * failureShakeCycles) *
                              failureShakeDistance * (1f - t);
                view.root.anchoredPosition = view.basePosition + Vector2.right * shake;
                if (view.frame != null)
                    view.frame.color = Color.Lerp(view.baseFrameColor, failureFlashColor, envelope);
            }
            yield return null;
        }

        view.feedbackRoutine = null;
        RestoreSlotFeedback(view);
    }

    private void ResetAllHudFeedback()
    {
        foreach (SlotView view in hudSlots)
            ResetSlotFeedback(view);
    }

    private void ResetSlotFeedback(SlotView view)
    {
        if (view.feedbackRoutine == null)
            return;

        StopCoroutine(view.feedbackRoutine);
        view.feedbackRoutine = null;
        RestoreSlotFeedback(view);
    }

    private static void RestoreSlotFeedback(SlotView view)
    {
        if (view.root != null)
        {
            view.root.localScale = view.baseScale;
            view.root.anchoredPosition = view.basePosition;
        }
        if (view.frame != null)
            view.frame.color = view.baseFrameColor;
    }

    private void WireButtons()
    {
        AddButtonBinding(chestButton, OpenPopup);
        AddButtonBinding(popupCloseButton, ClosePopup);
        AddButtonBinding(clearSlotButton, ClearSelectedSlot);

        for (int i = 0; i < hudSlots.Count; i++)
        {
            int index = i;
            AddButtonBinding(hudSlots[i].button, () => UseQuickSlot(index));
        }
        for (int i = 0; i < popupSlots.Count; i++)
        {
            int index = i;
            AddButtonBinding(popupSlots[i].button, () => SelectPopupSlot(index));
        }
        foreach (KeyValuePair<SkillType, EntryView> pair in entries)
        {
            Transform entry = pair.Value.icon != null ? pair.Value.icon.transform.parent : null;
            SkillType type = pair.Key;
            AddButtonBinding(entry != null ? entry.GetComponent<Button>() : null,
                () => AssignSelectedSlot(type));
        }
    }

    private void AddButtonBinding(Button button, UnityAction action)
    {
        if (button == null || action == null)
            return;
        button.onClick.AddListener(action);
        buttonBindings.Add(new ButtonBinding { button = button, action = action });
    }

    private void UnwireButtons()
    {
        foreach (ButtonBinding binding in buttonBindings)
        {
            if (binding.button != null)
                binding.button.onClick.RemoveListener(binding.action);
        }
        buttonBindings.Clear();
    }


}
