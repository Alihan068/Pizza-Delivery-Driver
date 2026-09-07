using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents registered maps and shift modifiers in the map selection scene and applies the selected
/// pair to the next session.
/// </summary>
/// <remarks>
/// Browsing is local to this panel. A map is saved only when the player presses Select Map, while a
/// modifier is temporary and is deliberately never written to the career save.
/// </remarks>
public class MapSelectionPanel : MonoBehaviour {
    [Header("Map Display")]
    [SerializeField] TextMeshProUGUI mapNameText;
    [SerializeField] TextMeshProUGUI mapDescriptionText;
    [SerializeField] TextMeshProUGUI mapStatusText;
    [SerializeField] Button previousMapButton;
    [SerializeField] Button nextMapButton;
    [SerializeField] Button selectMapButton;

    [Header("Modifier Display")]
    [SerializeField] TextMeshProUGUI modifierNameText;
    [SerializeField] TextMeshProUGUI modifierDescriptionText;
    [SerializeField] Button previousModifierButton;
    [SerializeField] Button nextModifierButton;
    [SerializeField] Button closeButton;

    [Header("Localization Keys")]
    [SerializeField] string mapLockedKey = "garage.mapSelection.locked";
    [SerializeField] string mapAvailableKey = "garage.mapSelection.available";
    [SerializeField] string mapSelectedKey = "garage.mapSelection.selected";
    [SerializeField] string mapPurchaseKey = "garage.mapSelection.purchase";
    [SerializeField] string selectMapKey = "garage.mapSelection.select";
    [SerializeField] string modifierNoneKey = "garage.modifier.none";
    [SerializeField] string modifierNoneDescriptionKey = "garage.modifier.noneDescription";

    int mapIndex;
    int modifierIndex;
    bool listenersBound;

    /// <summary>Raised when the previewed map or modifier changes, or when the active selection is applied.</summary>
    public event System.Action SelectionChanged;

    void OnEnable() {
        LocalizationManager.LanguageChanged += RefreshUI;
    }

    void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshUI;
    }

    void Start() {
        BindListeners();
        ResolveInitialSelection();
        RefreshUI();
    }

    void BindListeners() {
        if (listenersBound) return;
        if (previousMapButton != null) previousMapButton.onClick.AddListener(() => ChangeMap(-1));
        if (nextMapButton != null) nextMapButton.onClick.AddListener(() => ChangeMap(1));
        if (selectMapButton != null) selectMapButton.onClick.AddListener(SelectMap);
        if (previousModifierButton != null) previousModifierButton.onClick.AddListener(() => ChangeModifier(-1));
        if (nextModifierButton != null) nextModifierButton.onClick.AddListener(() => ChangeModifier(1));
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        listenersBound = true;
    }

    /// <summary>Opens the map and modifier picker and refreshes its current selections.</summary>
    public void Open() {
        gameObject.SetActive(true);
        ResolveInitialSelection();
        RefreshUI();
    }

    /// <summary>Closes the map and modifier picker without changing the active selection.</summary>
    public void Close() {
        gameObject.SetActive(false);
    }

    void ResolveInitialSelection() {
        var manager = GameManager.Instance;
        if (manager == null || manager.Content == null) return;

        mapIndex = FindMapIndex(manager.Content.Maps, manager.currentMap);
        modifierIndex = FindModifierIndex(manager.allModifiers, manager.CurrentModifier);
    }

    void ChangeMap(int direction) {
        var manager = GameManager.Instance;
        if (manager == null || manager.Content == null || manager.Content.Maps.Count == 0) return;
        mapIndex = WrapIndex(mapIndex + direction, manager.Content.Maps.Count);
        RefreshUI();
    }

    void ChangeModifier(int direction) {
        var manager = GameManager.Instance;
        int count = manager != null && manager.allModifiers != null ? manager.allModifiers.Length + 1 : 1;
        if (count <= 0) return;
        modifierIndex = WrapIndex(modifierIndex + direction, count);
        RefreshUI();
    }

    void SelectMap() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        if (manager == null || map == null) return;

        if (!manager.IsMapOwned(map) && !manager.TryPurchaseMap(map)) return;

        manager.SelectMap(map);
        manager.SelectModifier(GetSelectedModifier(manager));
        RefreshUI();
    }

    /// <summary>Returns whether the preview currently matches the owned map selected for the next shift.</summary>
    /// <returns>True when the previewed map is the active owned map.</returns>
    public bool IsPreviewApplied() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        var modifier = GetSelectedModifier(manager);
        return manager != null && map != null && manager.IsMapOwned(map) && manager.currentMap == map && manager.CurrentModifier == modifier;
    }

    void RefreshUI() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        if (manager == null || map == null) {
            SetMapControlsActive(false);
            return;
        }

        SetMapControlsActive(true);
        if (mapNameText != null) mapNameText.text = map.GetDisplayName();
        if (mapDescriptionText != null) mapDescriptionText.text = map.GetDescription();

        bool mapOwned = manager.IsMapOwned(map);
        bool isSelected = manager.currentMap == map;
        if (mapStatusText != null) {
            if (!mapOwned) mapStatusText.text = LocalizationManager.Get(mapLockedKey, Mathf.Max(0, map.unlockPrice));
            else if (isSelected) mapStatusText.text = LocalizationManager.Get(mapSelectedKey);
            else mapStatusText.text = LocalizationManager.Get(mapAvailableKey);
        }
        if (selectMapButton != null) {
            // The same action also applies a newly previewed modifier, so it must remain usable
            // when the map itself is already selected.
            selectMapButton.interactable = mapOwned || manager.CanPurchaseMap(map);
            var label = selectMapButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = LocalizationManager.Get(mapOwned ? selectMapKey : mapPurchaseKey);
        }

        var modifier = GetSelectedModifier(manager);
        if (modifierNameText != null) modifierNameText.text = modifier != null ? modifier.GetDisplayName() : LocalizationManager.Get(modifierNoneKey);
        if (modifierDescriptionText != null) modifierDescriptionText.text = modifier != null ? modifier.GetDescription() : LocalizationManager.Get(modifierNoneDescriptionKey);

        if (SelectionChanged != null) SelectionChanged.Invoke();
    }

    void SetMapControlsActive(bool active) {
        if (previousMapButton != null) previousMapButton.gameObject.SetActive(active);
        if (nextMapButton != null) nextMapButton.gameObject.SetActive(active);
        if (selectMapButton != null) selectMapButton.gameObject.SetActive(active);
    }

    MapData GetSelectedMap(GameManager manager) {
        if (manager == null || manager.Content == null || manager.Content.Maps.Count == 0) return null;
        mapIndex = Mathf.Clamp(mapIndex, 0, manager.Content.Maps.Count - 1);
        return manager.Content.Maps[mapIndex];
    }

    ShiftModifierData GetSelectedModifier(GameManager manager) {
        if (manager == null || manager.allModifiers == null || manager.allModifiers.Length == 0) return null;
        if (modifierIndex <= 0) return null;
        int index = Mathf.Clamp(modifierIndex - 1, 0, manager.allModifiers.Length - 1);
        return manager.allModifiers[index];
    }

    int FindMapIndex(IReadOnlyList<MapData> maps, MapData selected) {
        if (maps == null || maps.Count == 0 || selected == null) return 0;
        for (int i = 0; i < maps.Count; i++) if (maps[i] == selected) return i;
        return 0;
    }

    int FindModifierIndex(ShiftModifierData[] modifiers, ShiftModifierData selected) {
        if (selected == null || modifiers == null) return 0;
        for (int i = 0; i < modifiers.Length; i++) if (modifiers[i] == selected) return i + 1;
        return 0;
    }

    int WrapIndex(int value, int count) {
        if (count <= 0) return 0;
        value %= count;
        return value < 0 ? value + count : value;
    }
}
