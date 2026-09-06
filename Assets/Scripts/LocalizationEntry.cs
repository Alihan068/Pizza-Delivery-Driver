using System;
using UnityEngine;

/// <summary>One authored translation. Keys remain stable when the displayed wording changes.</summary>
[Serializable]
public class LocalizationEntry {
    /// <summary>Stable, case-sensitive identifier shared by every language table.</summary>
    public string key;
    /// <summary>Translated text or composite format. Numeric TMP formats use indexed placeholders.</summary>
    [TextArea] public string text;
}
