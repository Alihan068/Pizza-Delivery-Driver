using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Adds the bounded Replay result-panel translations without opening or changing scenes.</summary>
public static class SessionReplayLocalizationAuthoring {
    const string EnglishPath = "Assets/ScriptableObjects/Localization/English.asset";
    const string TurkishPath = "Assets/ScriptableObjects/Localization/Turkish.asset";

    /// <summary>Adds the Replay label to the shipped English and Turkish language assets if absent.</summary>
    [MenuItem("Tools/PizzaGame/Authoring/Add Session Replay Localization")]
    public static void AddSessionReplayLocalization() {
        AddEntry(EnglishPath, "Replay");
        AddEntry(TurkishPath, "Tekrar Oyna");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void AddEntry(string path, string text) {
        LanguageData language = AssetDatabase.LoadAssetAtPath<LanguageData>(path);
        if (language == null) {
            Debug.LogError("Session Replay localization asset was not found: " + path);
            return;
        }
        var entries = new List<LocalizationEntry>(language.entries ?? System.Array.Empty<LocalizationEntry>());
        foreach (var entry in entries)
            if (entry != null && entry.key == "result.replay") return;
        entries.Add(new LocalizationEntry { key = "result.replay", text = text });
        language.entries = entries.ToArray();
        EditorUtility.SetDirty(language);
    }
}
