#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Additive-only authoring for the Pre-Level starting-time label and the
// low-time urgency countdown. Unlike PreLevelPanelAuthoring.Apply (which
// rebuilds the whole panel), this command never clears or repositions
// existing objects: re-running it only rewires references, so manually
// authored RectTransform/font/color values survive.
public static class PreLevelTimeAndUrgencyAuthoring
{
    [MenuItem("Tools/Planet Boom/Gameplay/Add Pre-Level Time + Urgency Countdown Labels")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "GameScene")
            throw new System.InvalidOperationException("Open GameScene before adding the new labels.");

        PreLevelPanel preLevel = Object.FindAnyObjectByType<PreLevelPanel>(FindObjectsInactive.Include);
        MissionHUD missionHud = Object.FindAnyObjectByType<MissionHUD>(FindObjectsInactive.Include);
        if (preLevel == null || missionHud == null)
            throw new System.InvalidOperationException("PreLevelPanel or MissionHUD is missing in GameScene.");

        Transform panel = preLevel.transform.Find("Panel");
        if (panel == null)
            throw new System.InvalidOperationException("PreLevelPanel/Panel was not found.");

        SerializedObject preLevelSerialized = new SerializedObject(preLevel);
        TMP_Text styleTemplate =
            preLevelSerialized.FindProperty("levelTitle").objectReferenceValue as TMP_Text;
        if (styleTemplate == null)
            styleTemplate = Object.FindAnyObjectByType<TextMeshProUGUI>(FindObjectsInactive.Include);

        // ---- Pre-Level starting-time label ---------------------------------
        bool createdTime = panel.Find("LevelTimeText") == null;
        TextMeshProUGUI timeText = EnsureLabel(panel, "LevelTimeText", styleTemplate, "TIME: 90 SEC");
        if (createdTime)
        {
            // Initial placement only (just under the objectives area);
            // position/style freely in the Inspector afterwards.
            SetRect(timeText.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -660f), new Vector2(620f, 70f));
            timeText.fontSize = 44f;
        }
        preLevelSerialized.FindProperty("levelTimeText").objectReferenceValue = timeText;
        preLevelSerialized.ApplyModifiedPropertiesWithoutUndo();

        // ---- Urgency countdown below the Mission HUD -----------------------
        // Sibling of MissionHUDGroup (not a child) so the HUD's
        // HorizontalLayoutGroup can never move it.
        RectTransform missionGroup = (RectTransform)missionHud.transform;
        Transform hudParent = missionGroup.parent;
        bool createdUrgency = hudParent.Find("UrgencyCountdown") == null;
        TextMeshProUGUI urgencyText = EnsureLabel(hudParent, "UrgencyCountdown", styleTemplate, "10");
        if (createdUrgency)
        {
            SetRect(urgencyText.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -560f), new Vector2(280f, 150f));
            urgencyText.fontSize = 110f;
            urgencyText.fontStyle = FontStyles.Bold;
            urgencyText.color = new Color(1f, .42f, .32f, 1f);
        }
        UrgencyCountdown urgency = urgencyText.GetComponent<UrgencyCountdown>();
        if (urgency == null)
            urgency = Undo.AddComponent<UrgencyCountdown>(urgencyText.gameObject);
        SerializedObject urgencySerialized = new SerializedObject(urgency);
        urgencySerialized.FindProperty("countdownText").objectReferenceValue = urgencyText;
        urgencySerialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(preLevel);
        EditorUtility.SetDirty(urgency);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = createdUrgency ? urgencyText.gameObject : timeText.gameObject;

        Debug.Log("Pre-Level time label + urgency countdown " +
                  (createdTime || createdUrgency ? "created and wired" : "already present; references rewired") +
                  ". Adjust their RectTransform/TMP styling freely, then save GameScene manually.");
    }

    private static TextMeshProUGUI EnsureLabel(Transform parent, string name,
        TMP_Text template, string previewText)
    {
        Transform found = parent.Find(name);
        TextMeshProUGUI label;
        if (found == null)
        {
            GameObject go = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            Undo.RegisterCreatedObjectUndo(go, "Pre-Level Time + Urgency Labels");
            go.transform.SetParent(parent, false);
            label = go.GetComponent<TextMeshProUGUI>();
            if (template != null)
            {
                label.font = template.font;
                label.fontSharedMaterial = template.fontSharedMaterial;
            }
            label.alignment = TextAlignmentOptions.Center;
            label.enableAutoSizing = false;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.text = previewText;
        }
        else
        {
            label = found.GetComponent<TextMeshProUGUI>();
            if (label == null)
                label = Undo.AddComponent<TextMeshProUGUI>(found.gameObject);
        }
        label.raycastTarget = false;
        return label;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localScale = Vector3.one;
    }
}
#endif
