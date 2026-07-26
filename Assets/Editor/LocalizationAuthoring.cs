#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Localization authoring, in two additive commands:
//
// 1. "Create Or Update Table" — ensures Assets/Resources/LocalizationTable
//    .asset exists and contains every default key below. Existing rows are
//    NEVER overwritten (manual translation edits always win); only missing
//    keys are appended. Safe to re-run any time, e.g. after new keys are
//    introduced in code.
//
// 2. "Attach Localized Text In Open Scene" — walks every TMP text in the
//    CURRENTLY OPEN scene and, where its authored content exactly matches a
//    known STATIC UI string, attaches a LocalizedText with the matching key.
//    Texts driven by gameplay scripts (mission progress, timers, counts,
//    the language button) are deliberately absent from the match map so
//    they are never touched. Existing LocalizedText components are left
//    alone. The scene is marked dirty but never saved — run it once per
//    scene (GameScene, MainMenu, LevelMap) and save manually.
public static class LocalizationAuthoring
{
    private const string ResourcesFolder = "Assets/Resources";
    private const string TableAssetPath = ResourcesFolder + "/LocalizationTable.asset";

    // key, english, turkish — English values keep the currently authored
    // wording/casing so enabling localization changes nothing visually in
    // the default language.
    private static readonly string[,] DefaultEntries =
    {
        { "ui.continue", "CONTINUE", "DEVAM ET" },
        { "ui.restart", "Restart", "Yeniden Başlat" },
        { "ui.exit", "Exit", "Çıkış" },
        { "ui.support", "Support", "Destek" },
        { "ui.remove_ads", "REMOVE ADS", "REKLAMLARI KALDIR" },
        { "ui.settings", "SETTINGS", "AYARLAR" },
        { "ui.sound", "Sound", "Ses" },
        { "ui.music", "Music", "Müzik" },
        { "ui.haptic", "Haptic", "Titreşim" },
        { "ui.use", "USE", "KULLAN" },
        { "ui.used", "USED", "KULLANILDI" },
        { "language.english", "ENGLISH", "ENGLISH" },
        { "language.turkish", "TURKISH", "TÜRKÇE" },
        { "menu.start", "Start", "Başla" },
        { "menu.shop", "Shop", "Mağaza" },
        { "menu.options", "Options", "Seçenekler" },
        { "prelevel.level", "LEVEL {0}", "BÖLÜM {0}" },
        { "prelevel.time", "TIME: {0} SEC", "SÜRE: {0} SN" },
        { "prelevel.time_rush", "TIME RUSH: {0} SEC", "ZAMAN YARIŞI: {0} SN" },
        { "level_start.go", "GO!", "BAŞLA!" },
        { "mode.time_rush.title", "TIME RUSH", "ZAMAN YARIŞI" },
        { "mode.time_rush.description", "MERGE TO GAIN TIME", "BİRLEŞTİR, SÜRE KAZAN" },
        { "tutorial.time_rush.description", "MERGE PLANETS TO<br>GAIN TIME", "GEZEGENLERİ BİRLEŞTİR,<br>SÜRE KAZAN" },
        { "tutorial.time_bonus", "+{0} SEC", "+{0} SN" },
        { "tutorial.tap_to_continue", "TAP TO CONTINUE", "DEVAM ETMEK İÇİN DOKUN" },
        { "level_start.effect.lucky_drop", "LUCKY DROP", "ŞANS DAMLASI" },
        { "level_start.effect.double_time_drop", "DOUBLE TIME DROP", "ÇİFT SÜRE DAMLASI" },
        { "level_start.effect.star_booster", "STAR BOOSTER", "YILDIZ TAKVİYESİ" },
        { "prelevel.ready", "READY", "HAZIR" },
        { "prelevel.reach", "REACH: TIER {0}", "{0}. KADEMEYE ULAŞ" },
        { "prelevel.merge", "MERGE: {0}/{1}", "BİRLEŞTİR: {0}/{1}" },
        { "prelevel.combo", "COMBO: X{0}", "KOMBO: X{0}" },
        { "prelevel.meteor", "DESTROY: {0} METEOR", "{0} METEOR YOK ET" },
        { "prelevel.survive", "SURVIVE: {0} SEC", "{0} SN HAYATTA KAL" },
        { "prelevel.generic", "MISSION {0}", "GÖREV {0}" },
        { "booster.lucky_drop", "Lucky Drop", "Şans Damlası" },
        { "booster.double_time_drop", "2X Time Drop", "2X Süre Damlası" },
        { "booster.star_booster", "Star Booster", "Yıldız Takviyesi" },
        { "mission.reach", "REACH", "ULAŞ" },
        { "mission.merge", "MERGE", "BİRLEŞTİR" },
        { "mission.combo", "COMBO", "KOMBO" },
        { "mission.meteor", "METEOR", "METEOR" },
        { "mission.survive", "SURVIVE", "DAYAN" },
        { "mission.generic", "MISSION", "GÖREV" },
        { "mission.tier", "TIER {0}", "KADEME {0}" },
        { "mission.survive_progress", "{0}/{1}s", "{0}/{1}sn" },
        { "evolution.title", "EVOLUTION", "EVRİM" },
        { "evolution.upcoming", "UPCOMING", "YAKINDA" },
        { "evolution.new", "NEW", "YENİ" },
        { "gameover.title", "GAME OVER", "OYUN BİTTİ" },
        { "gameover.restart_description", "RESTART THE LEVEL", "BÖLÜMÜ YENİDEN BAŞLAT" },
        { "gameover.try_again", "TRY AGAIN", "TEKRAR DENE" },
        { "gameover.continue_description", "KEEP YOUR PROGRESS", "İLERLEMENİ KORU" },
        { "gameover.or", "OR", "VEYA" },
        { "gameover.continue", "CONTINUE", "DEVAM ET" },
        { "gameover.continue_bonus", "+{0} SEC", "+{0} SN" },
        { "levelcomplete.title", "LEVEL COMPLETED", "BÖLÜM TAMAMLANDI" },
        { "levelmap.sector", "SECTOR {0}", "SEKTÖR {0}" },
        { "levelmap.select_level", "Select a level", "Bir seviye seç" },
        { "levelmap.start", "START", "BAŞLAT" },
        { "hud.critical", "CRITICAL: {0:F1}s!", "KRİTİK: {0:F1}sn!" },
        { "hud.combo", "COMBO x{0}", "KOMBO x{0}" },
        { "inventory.clear_slot", "CLEAR SLOT", "SLOTU TEMİZLE" },
    };

    // Authored scene text → key, exact match after trimming. Uppercase and
    // authored-casing variants both appear because different panels were
    // authored at different times. Dynamic strings (LEVEL 1, TIME: 90 SEC,
    // OBJECTIVE, USE, booster names, "English", numbers, "$2.99") are
    // intentionally NOT listed.
    private static readonly (string text, string key)[] StaticTextMap =
    {
        ("CONTINUE", "ui.continue"),
        ("Restart", "ui.restart"), ("RESTART", "ui.restart"),
        ("Exit", "ui.exit"), ("EXIT", "ui.exit"),
        ("Support", "ui.support"), ("SUPPORT", "ui.support"),
        ("REMOVE ADS", "ui.remove_ads"),
        ("SETTINGS", "ui.settings"),
        ("Sound", "ui.sound"), ("SOUND", "ui.sound"),
        ("Music", "ui.music"), ("MUSIC", "ui.music"),
        ("Haptic", "ui.haptic"), ("HAPTIC", "ui.haptic"),
        ("EVOLUTION", "evolution.title"),
        ("READY", "prelevel.ready"),
        ("CLEAR SLOT", "inventory.clear_slot"),
        ("PLANET BOOM!", "gameover.title"), ("GAME OVER", "gameover.title"),
        ("TRY AGAIN", "gameover.try_again"),
        ("LEVEL COMPLETED", "levelcomplete.title"),
        ("Start", "menu.start"),
        ("Shop", "menu.shop"),
        ("Options", "menu.options"),
        ("START", "levelmap.start"),
    };

    [MenuItem("Tools/Planet Boom/Localization/Create Or Update Table")]
    public static void CreateOrUpdateTable()
    {
        if (!AssetDatabase.IsValidFolder(ResourcesFolder))
            AssetDatabase.CreateFolder("Assets", "Resources");

        LocalizationTable table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(TableAssetPath);
        if (table == null)
        {
            table = ScriptableObject.CreateInstance<LocalizationTable>();
            AssetDatabase.CreateAsset(table, TableAssetPath);
        }

        var known = new HashSet<string>();
        foreach (LocalizationTable.Entry entry in table.entries)
            if (entry != null && !string.IsNullOrEmpty(entry.key))
                known.Add(entry.key);

        int added = 0;
        for (int i = 0; i < DefaultEntries.GetLength(0); i++)
        {
            string key = DefaultEntries[i, 0];
            if (known.Contains(key)) continue;
            table.entries.Add(new LocalizationTable.Entry
            {
                key = key,
                english = DefaultEntries[i, 1],
                turkish = DefaultEntries[i, 2],
            });
            added++;
        }

        EditorUtility.SetDirty(table);
        AssetDatabase.SaveAssets();
        Selection.activeObject = table;
        Debug.Log($"Localization table ready at {TableAssetPath}: {added} entr{(added == 1 ? "y" : "ies")} added, " +
                  $"{table.entries.Count} total. Existing rows were not modified.");
    }

    [MenuItem("Tools/Planet Boom/Localization/Attach Localized Text In Open Scene")]
    public static void AttachInOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid())
            throw new System.InvalidOperationException("No valid scene open.");

        var map = new Dictionary<string, string>();
        foreach ((string text, string key) in StaticTextMap)
            map[text] = key;

        int attached = 0;
        int alreadyDone = 0;
        StringBuilder report = new StringBuilder();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string content = text.text != null ? text.text.Trim() : null;
                if (string.IsNullOrEmpty(content) || !map.TryGetValue(content, out string key))
                    continue;
                if (text.GetComponent<LocalizedText>() != null)
                {
                    alreadyDone++;
                    continue;
                }
                LocalizedText localized = Undo.AddComponent<LocalizedText>(text.gameObject);
                SerializedObject serialized = new SerializedObject(localized);
                serialized.FindProperty("key").stringValue = key;
                serialized.FindProperty("target").objectReferenceValue = text;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                attached++;
                report.Append($"\n- {Path(text.transform)} → {key}");
            }
        }

        if (attached > 0)
            EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"LocalizedText attach in '{scene.name}': {attached} attached, {alreadyDone} already present." +
                  (attached > 0 ? report + "\nSave the scene manually." : " Nothing to do."));
    }

    private static string Path(Transform transform)
    {
        StringBuilder path = new StringBuilder(transform.name);
        while (transform.parent != null)
        {
            transform = transform.parent;
            path.Insert(0, transform.name + "/");
        }
        return path.ToString();
    }
}
#endif
