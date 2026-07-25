using System;
using UnityEngine;

// Project-native localization runtime — no external package. Strings live
// in the LocalizationTable asset at Assets/Resources/LocalizationTable.asset
// (created/updated via Tools → Planet Boom → Localization); the selected
// language stays GameSettings.Language, whose setter raises LanguageChanged
// here so every LocalizedText (and any subscribed system) refreshes on the
// same frame — no polling anywhere.
//
// Usage:
//   Localization.Get("ui.continue")                → "CONTINUE" / "DEVAM ET"
//   Localization.Get("prelevel.time", 90)          → "TIME: 90 SEC" / "SÜRE: 90 SN"
// A missing key returns the key itself, so untranslated spots are visible
// in-game instead of throwing or rendering empty.
public static class Localization
{
    public const string TableResourcePath = "LocalizationTable";

    // Raised (via GameSettings) whenever the persisted language actually
    // changes value.
    public static event Action LanguageChanged;

    private static LocalizationTable table;
    private static bool tableLoadAttempted;

    private static LocalizationTable Table
    {
        get
        {
            if (!tableLoadAttempted)
            {
                tableLoadAttempted = true;
                table = Resources.Load<LocalizationTable>(TableResourcePath);
                if (table == null)
                    Debug.LogWarning("Localization: no table at Resources/" + TableResourcePath +
                                     " — run Tools → Planet Boom → Localization → Create Or Update Table.");
            }
            return table;
        }
    }

    public static string Get(string key)
    {
        LocalizationTable source = Table;
        string value = source != null ? source.GetValue(key, GameSettings.Language) : null;
        return value ?? key;
    }

    // Formatted variant for dynamic strings; the table cell is a
    // string.Format pattern ("TIME: {0} SEC"), so word order is free to
    // differ per language. A malformed pattern degrades to the raw pattern
    // rather than throwing mid-frame.
    public static string Get(string key, params object[] args)
    {
        string format = Get(key);
        try { return string.Format(format, args); }
        catch (FormatException) { return format; }
    }

    // Called by the GameSettings.Language setter — the persisted preference
    // stays the single source of truth for the current language.
    public static void NotifyLanguageChanged() => LanguageChanged?.Invoke();
}
