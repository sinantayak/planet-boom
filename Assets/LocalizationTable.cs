using System;
using System.Collections.Generic;
using UnityEngine;

// The single localization data source: one row per stable key, one column
// per supported language, edited entirely in the Inspector. The runtime
// loads the one instance at Assets/Resources/LocalizationTable.asset (see
// Localization). Adding a language later = add a field to Entry and one
// case to Resolve — nothing else in the runtime changes.
[CreateAssetMenu(menuName = "Planet Boom/Localization Table", fileName = "LocalizationTable")]
public sealed class LocalizationTable : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        // Stable lookup key, e.g. "ui.continue" — never the display text.
        public string key;
        [TextArea(1, 3)] public string english;
        [TextArea(1, 3)] public string turkish;
    }

    public List<Entry> entries = new List<Entry>();

    private Dictionary<string, Entry> lookup;

    // Null when the key is unknown; falls back to English for rows whose
    // requested language cell is still empty.
    public string GetValue(string key, GameLanguage language)
    {
        if (string.IsNullOrEmpty(key))
            return null;
        if (lookup == null)
            RebuildLookup();
        if (!lookup.TryGetValue(key, out Entry entry) || entry == null)
            return null;
        string value = Resolve(entry, language);
        if (!string.IsNullOrEmpty(value))
            return value;
        return string.IsNullOrEmpty(entry.english) ? null : entry.english;
    }

    private static string Resolve(Entry entry, GameLanguage language)
    {
        switch (language)
        {
            case GameLanguage.Turkish: return entry.turkish;
            default: return entry.english;
        }
    }

    private void RebuildLookup()
    {
        lookup = new Dictionary<string, Entry>(entries.Count, StringComparer.Ordinal);
        foreach (Entry entry in entries)
            if (entry != null && !string.IsNullOrEmpty(entry.key))
                lookup[entry.key] = entry;
    }

#if UNITY_EDITOR
    // Inspector edits invalidate the cache so play-mode tests see them live.
    private void OnValidate() => lookup = null;
#endif
}
