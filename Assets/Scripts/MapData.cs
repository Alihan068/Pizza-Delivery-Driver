using UnityEngine;

/// <summary>
/// One playable map: which scene it loads, how a session in it is configured, and how it is
/// presented in the map selection screen and purchased once with in-game currency.
/// </summary>
/// <remarks>
/// Maps are content, exactly like vehicles. They are discovered through <see cref="ContentRegistry"/>
/// rather than referenced from a fixed array, so a map that ships with the game and a map that
/// arrives later from an external source are handled by the same code path.
/// <para>
/// The scene is stored as a name rather than an object reference because a runtime script cannot
/// reference a <c>SceneAsset</c> — that type is editor-only. The name is validated against the
/// build settings by the content validator instead.
/// </para>
/// </remarks>
[CreateAssetMenu(fileName = "NewMapData", menuName = "PizzaGame/Map Data")]
public class MapData : ScriptableObject {
    [Header("Identity")]
    [Tooltip("Permanent identifier. Saves and unlock state reference this, never the display name or the asset name. Never change it after players have saves.")]
    public string mapId;

    [Tooltip("Localization key for the name shown to the player.")]
    public string displayNameKey;

    [Tooltip("Localization key for the one-line description shown on the selection card.")]
    public string descriptionKey;

    [Header("Presentation")]
    public Sprite previewImage;

    [Header("Content")]
    [Tooltip("Scene loaded when this map is played. Must be present in Build Settings.")]
    public string sceneName;

    [Tooltip("Session tuning used while playing this map: order sizes, patience, rewards, spawn caps.")]
    public LevelData levelData;

    [Header("Progression")]
    [Tooltip("Legacy field retained for compatibility. Map access is never gated by rank.")]
    public int requiredRank;

    [Tooltip("Legacy region grouping retained for content compatibility. It does not affect courier rating or access.")]
    public int regionTier = 1;

    [Tooltip("One-time currency price to purchase this map. Zero makes it free.")]
    public int unlockPrice;

    /// <summary>Name shown to the player, resolved from <see cref="displayNameKey"/>.</summary>
    /// <returns>The localized name, or the asset name when no key is set.</returns>
    public string GetDisplayName() {
        return string.IsNullOrEmpty(displayNameKey) ? name : LocalizationManager.Get(displayNameKey);
    }

    /// <summary>Description shown to the player, resolved from <see cref="descriptionKey"/>.</summary>
    /// <returns>The localized description, or an empty string when no key is set.</returns>
    public string GetDescription() {
        return string.IsNullOrEmpty(descriptionKey) ? string.Empty : LocalizationManager.Get(descriptionKey);
    }
}
