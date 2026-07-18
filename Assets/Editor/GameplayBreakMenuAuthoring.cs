#if UNITY_EDITOR
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GameplayBreakMenuAuthoring
{
    private const string ScenePath = "Assets/Scenes/GameScene.unity";

    [MenuItem("Tools/Planet Boom/Gameplay/Author Break Menu")]
    public static void Author()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null) throw new System.InvalidOperationException("Gameplay Canvas is missing.");
        Transform safeArea = Find(canvas.transform, "SafeAreaRoot");
        if (safeArea == null) throw new System.InvalidOperationException("SafeAreaRoot is missing.");

        Sprite stop = Load("Assets/Buttons/Stop.png");
        Sprite panelSprite = Load("Assets/UI Elements/BreakMenu.png");
        Sprite emptyButton = Load("Assets/Buttons/Empty-Button.png");
        Sprite audioOn = Load("Assets/Buttons/Audio-On.png");
        Sprite audioOff = Load("Assets/Buttons/Audio-Off.png");

        TMP_Text timer = Find(canvas.transform, "Gameplay Timer Text")?.GetComponent<TMP_Text>();
        if (timer == null) throw new System.InvalidOperationException("Gameplay Timer Text is missing.");
        Transform existingTimerHud = FindDirect(safeArea, "TimerHUD");
        RectTransform timerHud = existingTimerHud != null ? (RectTransform)existingTimerHud : EnsureRect(safeArea, "TimerHUD");
        if (existingTimerHud == null)
            SetRect(timerHud, new Vector2(0,1), new Vector2(255,-56), new Vector2(150,64), new Vector2(0,1));
        Transform existingFrame = FindDirect(timerHud, "TimerFrame");
        Image timerFrame = EnsureImage(timerHud, "TimerFrame", null, Color.white);
        if (existingFrame == null)
            SetRect(timerFrame.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(180,84));
        timerFrame.preserveAspect = true;
        timerFrame.raycastTarget = false;
        timer.transform.SetParent(timerHud, false);
        Stretch(timer.rectTransform);
        timerFrame.transform.SetAsFirstSibling();
        timer.raycastTarget = false;

        Image pauseImage = EnsureImage(safeArea, "PauseButton", stop, Color.white);
        SetRect(pauseImage.rectTransform, new Vector2(1,1), new Vector2(-78,-70), new Vector2(108,108));
        pauseImage.preserveAspect = true; pauseImage.raycastTarget = true;
        Button pauseButton = pauseImage.GetComponent<Button>() ?? pauseImage.gameObject.AddComponent<Button>();
        pauseButton.targetGraphic = pauseImage;

        Image popupOverlay = EnsureImage(canvas.transform, "BreakMenuPopup", null, new Color(0,0,0,.58f));
        Stretch(popupOverlay.rectTransform); popupOverlay.raycastTarget = true;
        Image panel = EnsureImage(popupOverlay.transform, "PanelBackground", panelSprite, Color.white);
        SetRect(panel.rectTransform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(760,1180));
        panel.preserveAspect = true; panel.raycastTarget = true;

        RectTransform toggleRow = EnsureRect(panel.transform, "ToggleRow");
        SetRect(toggleRow, new Vector2(.5f,.5f), new Vector2(0,205), new Vector2(600,180));
        Button sound = EnsureToggle(toggleRow, "SoundToggle", audioOn, new Vector2(-200,0));
        Button music = EnsureToggle(toggleRow, "MusicToggle", audioOn, Vector2.zero);
        Button vibration = EnsureToggle(toggleRow, "VibrationToggle", audioOn, new Vector2(200,0));

        Button home = EnsureLongButton(panel.transform, "HomeButton", "HOME", emptyButton, new Vector2(0,-40));
        Button restart = EnsureLongButton(panel.transform, "RestartButton", "RESTART", emptyButton, new Vector2(0,-230));
        Button continueButton = EnsureLongButton(panel.transform, "ContinueButton", "CONTINUE", emptyButton, new Vector2(0,-420));

        BreakMenuUI ui = canvas.GetComponent<BreakMenuUI>() ?? canvas.gameObject.AddComponent<BreakMenuUI>();
        SerializedObject serialized = new(ui);
        Set(serialized,"popupRoot",popupOverlay.gameObject); Set(serialized,"pauseButton",pauseButton);
        Set(serialized,"soundToggle",sound); Set(serialized,"soundImage",sound.GetComponent<Image>());
        Set(serialized,"musicToggle",music); Set(serialized,"musicImage",music.GetComponent<Image>());
        Set(serialized,"vibrationToggle",vibration); Set(serialized,"vibrationImage",vibration.GetComponent<Image>());
        Set(serialized,"onSprite",audioOn); Set(serialized,"offSprite",audioOff);
        Set(serialized,"homeButton",home); Set(serialized,"restartButton",restart); Set(serialized,"continueButton",continueButton);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        popupOverlay.gameObject.SetActive(false);

        EditorUtility.SetDirty(ui); EditorUtility.SetDirty(timer); EditorUtility.SetDirty(pauseImage);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new System.InvalidOperationException("GameScene save failed.");
        AssetDatabase.SaveAssets();
        Debug.Log("Gameplay Break Menu authored: persistent popup, pause HUD, toggles and actions.");
    }

    private static Button EnsureToggle(Transform parent, string name, Sprite sprite, Vector2 pos)
    {
        Image image = EnsureImage(parent,name,sprite,Color.white);
        SetRect(image.rectTransform,new Vector2(.5f,.5f),pos,new Vector2(130,130)); image.preserveAspect=true; image.raycastTarget=true;
        Button button=image.GetComponent<Button>()??image.gameObject.AddComponent<Button>(); button.targetGraphic=image; return button;
    }

    private static Button EnsureLongButton(Transform parent,string name,string label,Sprite sprite,Vector2 pos)
    {
        Image image=EnsureImage(parent,name,sprite,Color.white);
        SetRect(image.rectTransform,new Vector2(.5f,.5f),pos,new Vector2(560,145)); image.preserveAspect=true; image.raycastTarget=true;
        Button button=image.GetComponent<Button>()??image.gameObject.AddComponent<Button>(); button.targetGraphic=image;
        TMP_Text text=Find(image.transform,"Label")?.GetComponent<TMP_Text>();
        if(text==null){GameObject go=new("Label",typeof(RectTransform),typeof(TextMeshProUGUI));go.transform.SetParent(image.transform,false);text=go.GetComponent<TMP_Text>();}
        Stretch(text.rectTransform); text.text=label; text.fontSize=42; text.alignment=TextAlignmentOptions.Center; text.color=Color.white; text.raycastTarget=false;
        return button;
    }

    private static Image EnsureImage(Transform parent,string name,Sprite sprite,Color color)
    {
        Transform found=FindDirect(parent,name); GameObject go=found!=null?found.gameObject:new GameObject(name,typeof(RectTransform));
        if(found==null)go.transform.SetParent(parent,false); Image image=go.GetComponent<Image>()??go.AddComponent<Image>();
        if(sprite!=null)image.sprite=sprite; image.color=color; return image;
    }
    private static RectTransform EnsureRect(Transform parent,string name){Transform t=FindDirect(parent,name);if(t!=null)return (RectTransform)t;GameObject go=new(name,typeof(RectTransform));go.transform.SetParent(parent,false);return (RectTransform)go.transform;}
    private static Transform Find(Transform root,string name){if(root.name==name)return root;for(int i=0;i<root.childCount;i++){Transform found=Find(root.GetChild(i),name);if(found!=null)return found;}return null;}
    private static Transform FindDirect(Transform parent,string name){for(int i=0;i<parent.childCount;i++)if(parent.GetChild(i).name==name)return parent.GetChild(i);return null;}
    private static Sprite Load(string path)=>AssetDatabase.LoadAssetAtPath<Sprite>(path);
    private static void Set(SerializedObject so,string property,Object value)=>so.FindProperty(property).objectReferenceValue=value;
    private static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;}
    private static void SetRect(RectTransform r,Vector2 anchor,Vector2 pos,Vector2 size,Vector2? pivot=null){r.anchorMin=r.anchorMax=anchor;r.pivot=pivot??new Vector2(.5f,.5f);r.anchoredPosition=pos;r.sizeDelta=size;r.localScale=Vector3.one;}
}
#endif
