using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Phase 3B presentation layer. It creates a deterministic UI hierarchy under
// the existing Canvas/SafeArea and reads every count/assignment from
// SkillInventoryManager; no inventory data is cached here.
public class SkillInventoryUI : MonoBehaviour
{
    [Header("Existing Scene Wiring")]
    [SerializeField] private RectTransform canvasRoot;
    [SerializeField] private RectTransform safeAreaRoot;
    [SerializeField] private Button chestButton;

    [Header("Optional Art Overrides")]
    [SerializeField] private Sprite quickSlotFrame;
    [SerializeField] private Sprite selectedSlotHighlight;
    [SerializeField] private Sprite popupBackground;
    [SerializeField] private Sprite skillEntryBackground;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Sprite[] skillIcons = Array.Empty<Sprite>();

    [Header("Fallback Colors (used when sprites are empty)")]
    [SerializeField] private Color slotColor = new Color(0.08f, 0.10f, 0.20f, 0.92f);
    [SerializeField] private Color popupColor = new Color(0.05f, 0.06f, 0.14f, 0.98f);
    [SerializeField] private Color selectedColor = new Color(0.25f, 0.75f, 1f, 0.8f);
    [SerializeField] private Color unavailableIconColor = new Color(0.35f, 0.35f, 0.35f, 0.75f);

    // Hooks for Phase 3C feedback (shake/toast/audio). UI does not invent a
    // failure animation yet, but callers receive the exact failed operation.
    public event Action<int, SkillType?> QuickSlotUseFailed;
    public event Action<int, SkillType> QuickSlotAssignmentFailed;

    private sealed class SlotView
    {
        public Button button;
        public Image icon;
        public GameObject badgeRoot;
        public TextMeshProUGUI countText;
        public Image selectedHighlight;
    }

    private sealed class EntryView
    {
        public Image icon;
        public TextMeshProUGUI countText;
    }

    private readonly List<SlotView> hudSlots = new List<SlotView>();
    private readonly List<SlotView> popupSlots = new List<SlotView>();
    private readonly Dictionary<SkillType, EntryView> entries = new Dictionary<SkillType, EntryView>();

    private SkillInventoryManager inventory;
    private GameObject popupRoot;
    private TextMeshProUGUI chestCountText;
    private int selectedSlotIndex;
    private bool popupOpen;

    void Awake()
    {
        ResolveSceneReferences();
        BuildUI();
        if (chestButton != null)
            chestButton.onClick.AddListener(OpenPopup);
    }

    void Start()
    {
        BindInventory();
    }

    void OnEnable()
    {
        BindInventory();
    }

    void OnDisable()
    {
        UnbindInventory();
        ClosePopup();
    }

    void OnDestroy()
    {
        if (chestButton != null)
            chestButton.onClick.RemoveListener(OpenPopup);
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
        bool succeeded = inventory != null && inventory.TryUseQuickSlot(slotIndex);
        if (!succeeded)
        {
            QuickSlotUseFailed?.Invoke(slotIndex, assignment);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log($"QUICK SLOT UI: slot {slotIndex + 1} use failed ({assignment?.ToString() ?? "Empty"}).", this);
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

        view.icon.enabled = assigned || emptySlotSprite != null;
        view.icon.sprite = assigned ? IconFor(type) : emptySlotSprite;
        view.icon.color = assigned && count <= 0 ? unavailableIconColor : Color.white;
        view.badgeRoot.SetActive(assigned);
        view.countText.text = count.ToString();
        view.button.interactable = popupSlot || (assigned && count > 0);
        if (view.selectedHighlight != null)
            view.selectedHighlight.gameObject.SetActive(popupSlot && selectedSlotIndex == index);
    }

    private void RefreshEntry(SkillType type)
    {
        if (inventory == null || !entries.TryGetValue(type, out EntryView view))
            return;
        int count = inventory.GetCount(type);
        view.icon.sprite = IconFor(type);
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

    private void BuildUI()
    {
        if (canvasRoot == null || safeAreaRoot == null || chestButton == null)
        {
            Debug.LogWarning("SkillInventoryUI: Canvas, SafeAreaRoot or chest Button is missing; UI was not built.", this);
            return;
        }

        BuildChestBadge();
        BuildHudSlots();
        BuildPopup();
        popupRoot.SetActive(false);
    }

    private void BuildChestBadge()
    {
        RectTransform badge = CreateRect("TotalSkillBadge", chestButton.transform);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, 1f);
        badge.pivot = new Vector2(1f, 1f);
        badge.anchoredPosition = new Vector2(8f, -8f);
        badge.sizeDelta = new Vector2(64f, 48f);
        Image bg = badge.gameObject.AddComponent<Image>();
        bg.color = new Color(0.85f, 0.12f, 0.2f, 0.95f);
        bg.raycastTarget = false;
        chestCountText = CreateText("Count", badge, "0", 30f);
    }

    private void BuildHudSlots()
    {
        RectTransform bar = CreateRect("QuickSlotBar", safeAreaRoot);
        bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 120f);
        bar.sizeDelta = new Vector2(420f, 120f);
        HorizontalLayoutGroup layout = bar.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = layout.childControlHeight = false;

        for (int i = 0; i < SkillInventoryManager.QuickSlotCount; i++)
        {
            int index = i;
            hudSlots.Add(CreateSlotView(bar, false, () => UseQuickSlot(index)));
        }
    }

    private void BuildPopup()
    {
        RectTransform overlay = CreateRect("SkillInventoryPopup", canvasRoot);
        Stretch(overlay);
        popupRoot = overlay.gameObject;
        Image dimmer = overlay.gameObject.AddComponent<Image>();
        dimmer.color = new Color(0f, 0f, 0f, 0.72f);

        RectTransform panel = CreateRect("Panel", overlay);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(900f, 1260f);
        Image panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = popupBackground;
        panelImage.color = popupBackground != null ? Color.white : popupColor;

        TextMeshProUGUI title = CreateText("Title", panel, "SKILL INVENTORY", 48f);
        SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -55f), new Vector2(620f, 80f));

        Button close = CreateTextButton("CloseButton", panel, "X", new Vector2(90f, 80f));
        SetRect(close.transform as RectTransform, new Vector2(1f, 1f), new Vector2(-60f, -55f), new Vector2(90f, 80f));
        close.onClick.AddListener(ClosePopup);

        RectTransform slotRow = CreateRect("PopupQuickSlots", panel);
        SetRect(slotRow, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(500f, 130f));
        HorizontalLayoutGroup slotLayout = slotRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        slotLayout.spacing = 30f;
        slotLayout.childAlignment = TextAnchor.MiddleCenter;
        slotLayout.childControlWidth = slotLayout.childControlHeight = false;
        for (int i = 0; i < SkillInventoryManager.QuickSlotCount; i++)
        {
            int index = i;
            popupSlots.Add(CreateSlotView(slotRow, true, () => SelectPopupSlot(index)));
        }

        Button clear = CreateTextButton("ClearSelectedSlot", panel, "CLEAR SLOT", new Vector2(250f, 65f));
        SetRect(clear.transform as RectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -285f), new Vector2(250f, 65f));
        clear.onClick.AddListener(ClearSelectedSlot);

        RectTransform viewport = CreateRect("SkillGridViewport", panel);
        viewport.anchorMin = new Vector2(0f, 0f);
        viewport.anchorMax = new Vector2(1f, 1f);
        viewport.offsetMin = new Vector2(55f, 70f);
        viewport.offsetMax = new Vector2(-55f, -350f);
        viewport.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(230f, 245f);
        grid.spacing = new Vector2(25f, 25f);
        grid.padding = new RectOffset(25, 25, 25, 25);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        foreach (SkillType type in Enum.GetValues(typeof(SkillType)))
            entries[type] = CreateSkillEntry(content, type);
    }

    private SlotView CreateSlotView(Transform parent, bool popupSlot, UnityAction onClick)
    {
        RectTransform root = CreateRect(popupSlot ? "PopupSlot" : "QuickSlot", parent);
        root.sizeDelta = new Vector2(120f, 120f);
        Image frame = root.gameObject.AddComponent<Image>();
        frame.sprite = quickSlotFrame;
        frame.color = quickSlotFrame != null ? Color.white : slotColor;
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        button.onClick.AddListener(onClick);

        RectTransform iconRect = CreateRect("Icon", root);
        Stretch(iconRect, 14f);
        Image icon = iconRect.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        RectTransform highlightRect = CreateRect("Selected", root);
        Stretch(highlightRect, -5f);
        Image highlight = highlightRect.gameObject.AddComponent<Image>();
        highlight.sprite = selectedSlotHighlight;
        highlight.color = selectedSlotHighlight != null ? Color.white : selectedColor;
        highlight.raycastTarget = false;
        highlight.gameObject.SetActive(false);

        RectTransform badge = CreateRect("CountBadge", root);
        badge.anchorMin = badge.anchorMax = new Vector2(1f, 0f);
        badge.pivot = new Vector2(1f, 0f);
        badge.anchoredPosition = new Vector2(6f, -4f);
        badge.sizeDelta = new Vector2(52f, 40f);
        Image badgeBg = badge.gameObject.AddComponent<Image>();
        badgeBg.color = new Color(0.05f, 0.05f, 0.08f, 0.95f);
        badgeBg.raycastTarget = false;
        TextMeshProUGUI count = CreateText("Count", badge, "0", 25f);

        return new SlotView
        {
            button = button,
            icon = icon,
            badgeRoot = badge.gameObject,
            countText = count,
            selectedHighlight = highlight
        };
    }

    private EntryView CreateSkillEntry(Transform parent, SkillType type)
    {
        RectTransform root = CreateRect(type.ToString(), parent);
        Image background = root.gameObject.AddComponent<Image>();
        background.sprite = skillEntryBackground;
        background.color = skillEntryBackground != null ? Color.white : slotColor;
        Button button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(() => AssignSelectedSlot(type));

        RectTransform iconRect = CreateRect("Icon", root);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -18f);
        iconRect.sizeDelta = new Vector2(140f, 140f);
        Image icon = iconRect.gameObject.AddComponent<Image>();
        icon.sprite = IconFor(type);
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        TextMeshProUGUI name = CreateText("Name", root, SplitName(type.ToString()), 23f);
        SetRect(name.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(210f, 55f));
        TextMeshProUGUI count = CreateText("Count", root, "0", 30f);
        SetRect(count.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(120f, 40f));

        return new EntryView { icon = icon, countText = count };
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
        go.layer = 5;
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float size)
    {
        RectTransform rect = CreateRect(name, parent);
        Stretch(rect);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (TMP_Settings.defaultFontAsset != null)
            text.font = TMP_Settings.defaultFontAsset;
        text.text = value;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private Button CreateTextButton(string name, Transform parent, string label, Vector2 size)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.sizeDelta = size;
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = quickSlotFrame;
        image.color = quickSlotFrame != null ? Color.white : slotColor;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        CreateText("Label", rect, label, 28f);
        return button;
    }

    private static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static string SplitName(string value)
    {
        for (int i = value.Length - 1; i > 0; i--)
        {
            if (char.IsUpper(value[i]) && !char.IsUpper(value[i - 1]))
                value = value.Insert(i, " ");
        }
        return value;
    }
}
