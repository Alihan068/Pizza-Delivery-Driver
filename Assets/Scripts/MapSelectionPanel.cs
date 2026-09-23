using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presents registered maps and shift modifiers in the map selection scene and applies the selected
/// pair to the next session.
/// </summary>
/// <remarks>
/// Browsing is local to this panel. The scene's single Start Job action applies the previewed map,
/// difficulty, and modifier before the scene controller loads the gameplay scene. A modifier is
/// temporary and is deliberately never written to the career save.
/// </remarks>
public class MapSelectionPanel : MonoBehaviour {
    [Header("Map Display")]
    [SerializeField] TextMeshProUGUI mapNameText;
    [SerializeField] TextMeshProUGUI mapDescriptionText;
    [SerializeField] TextMeshProUGUI mapStatusText;
    [SerializeField] Button previousMapButton;
    [SerializeField] Button nextMapButton;
    [Tooltip("Unified action button used to apply the preview and start the shift.")]
    [SerializeField] Button startJobButton;
    [SerializeField] Image mapPreviewImage;
    [SerializeField] TextMeshProUGUI panelTitleText;

    [Header("Runtime Map Browser")]
    [Tooltip("Builds the registry-driven map card grid when the scene has no authored card references.")]
    [SerializeField] bool buildRuntimeMapGrid = true;
    [SerializeField] Vector2 mapGridOffset = new Vector2(-300f, 0f);
    [SerializeField] Vector2 mapGridSize = new Vector2(500f, 600f);
    [SerializeField] Vector2 detailPanelOffset = new Vector2(300f, 0f);
    [SerializeField] Vector2 detailPanelSize = new Vector2(540f, 900f);
    [Header("Responsive Browser Layout")]
    [SerializeField] Vector2 mapBrowserAnchorMin = new Vector2(0.02f, 0.05f);
    [SerializeField] Vector2 mapBrowserAnchorMax = new Vector2(0.52f, 0.95f);
    [Tooltip("Horizontal inset applied to the map card viewport.")]
    [SerializeField] float mapBrowserViewportSidePadding = 12f;
    [Tooltip("Top inset reserves space for the map browser title.")]
    [SerializeField] float mapBrowserViewportTopPadding = 64f;
    [Tooltip("Bottom inset keeps the last map card clear of the browser edge.")]
    [SerializeField] float mapBrowserViewportBottomPadding = 12f;
    [SerializeField] Vector2 detailPanelAnchorMin = new Vector2(0.55f, 0.05f);
    [SerializeField] Vector2 detailPanelAnchorMax = new Vector2(0.98f, 0.95f);
    [SerializeField] Vector2 mapCardSize = new Vector2(210f, 170f);
    [SerializeField] Vector2 mapCardSpacing = new Vector2(16f, 16f);
    [Min(1)] [SerializeField] int mapGridColumnCount = 2;
    [SerializeField] Color runtimePanelColor = new Color(0.04f, 0.08f, 0.12f, 0.92f);
    [SerializeField] Color runtimeCardColor = new Color(0.12f, 0.24f, 0.30f, 1f);
    [SerializeField] Color runtimeLockedCardColor = new Color(0.08f, 0.12f, 0.15f, 1f);
    [SerializeField] Color runtimeSelectedCardColor = new Color(0.20f, 0.48f, 0.58f, 1f);
    [SerializeField] string mapBrowserTitleKey = "garage.mapSelection.title";
    [SerializeField] RectTransform mapBrowserRoot;
    [SerializeField] RectTransform mapBrowserContent;
    [SerializeField] TextMeshProUGUI mapBrowserTitleText;
    [SerializeField] ScrollRect mapBrowserScroll;

    [Header("Typography")]
    [SerializeField] float browserTitleFontSize = 28f;
    [SerializeField] float panelTitleFontSize = 28f;
    [SerializeField] float mapNameFontSize = 24f;
    [SerializeField] float descriptionFontSize = 18f;
    [SerializeField] float statusFontSize = 18f;
    [SerializeField] float difficultyNameFontSize = 24f;
    [SerializeField] float difficultyDescriptionFontSize = 17f;
    [SerializeField] float difficultyStatusFontSize = 17f;
    [SerializeField] float difficultySummaryFontSize = 17f;
    [SerializeField] float difficultyStarsFontSize = 28f;
    [SerializeField] float modifierTitleFontSize = 17f;
    [SerializeField] float modifierNameFontSize = 21f;
    [SerializeField] float modifierDescriptionFontSize = 17f;
    [SerializeField] float mapCardLabelFontSize = 20f;
    [SerializeField] float runtimeMinimumFontSize = 13f;

    [Header("Difficulty Display")]
    [SerializeField] TextMeshProUGUI difficultyNameText;
    [SerializeField] TextMeshProUGUI difficultyDescriptionText;
    [SerializeField] TextMeshProUGUI difficultyStatusText;
    [SerializeField] TextMeshProUGUI difficultySummaryText;
    [SerializeField] TextMeshProUGUI difficultyStarsText;
    [SerializeField] DifficultyStarGraphic difficultyStarsGraphic;
    [SerializeField] Button previousDifficultyButton;
    [SerializeField] Button nextDifficultyButton;
    [Min(1)] [SerializeField] int difficultyStarCount = 6;
    [SerializeField] Color filledStarColor = new Color(1f, 0.78f, 0.08f, 1f);
    [SerializeField] Color emptyStarColor = Color.black;

    [Header("Shift Duration")]
    [Tooltip("Finite shift durations offered by this selection screen. The list is data-driven so new durations can be added without changing the picker logic.")]
    [SerializeField] int[] sessionDurationOptionsMinutes = new int[] { 3, 5, 10 };
    [SerializeField] TextMeshProUGUI sessionDurationText;
    [SerializeField] Button previousSessionDurationButton;
    [SerializeField] Button nextSessionDurationButton;
    [SerializeField] float sessionDurationFontSize = 21f;

    [Header("Modifier Display")]
    [SerializeField] TextMeshProUGUI modifierTitleText;
    [SerializeField] TextMeshProUGUI modifierNameText;
    [SerializeField] TextMeshProUGUI modifierDescriptionText;
    [SerializeField] Button previousModifierButton;
    [SerializeField] Button nextModifierButton;
    [SerializeField] Button closeButton;

    [Header("Runtime Modifier Selection")]
    [Tooltip("Optional authored scroll container. When absent, the picker is created at runtime from the legacy modifier field parent.")]
    [SerializeField] ScrollRect modifierScroll;
    [SerializeField] RectTransform modifierContent;
    [SerializeField] TextMeshProUGUI modifierSummaryText;
    [SerializeField] TextMeshProUGUI modifierStatusText;

    [Header("Localization Keys")]
    [SerializeField] string mapLockedKey = "garage.mapSelection.locked";
    [SerializeField] string mapAvailableKey = "garage.mapSelection.available";
    [SerializeField] string mapSelectedKey = "garage.mapSelection.selected";
    [SerializeField] string mapPurchaseKey = "garage.mapSelection.purchase";
    [SerializeField] string startJobKey = "garage.mapSelection.startJob";
    [SerializeField] string difficultyUnavailableKey = "map.difficulty.unavailable";
    [SerializeField] string difficultyLockedKey = "map.difficulty.locked";
    [SerializeField] string difficultyUnlockedKey = "map.difficulty.unlocked";
    [SerializeField] string difficultyOrdersKey = "map.difficulty.orders";
    [SerializeField] string difficultyLossKey = "map.difficulty.loss";
    [SerializeField] string difficultyRewardKey = "map.difficulty.reward";
    [SerializeField] string difficultyBestKey = "map.difficulty.best";
    [SerializeField] string difficultyUnlockKey = "map.difficulty.unlock";
    [SerializeField] string difficultyRequiresKey = "map.difficulty.requires";
    [SerializeField] string difficultyCounterKey = "map.difficulty.counter";
    [SerializeField] string sessionDurationTitleKey = "garage.mapSelection.duration";
    [SerializeField] string sessionDurationValueKey = "garage.mapSelection.durationValue";
    [SerializeField] string modifierNoneKey = "garage.modifier.none";
    [SerializeField] string modifierNoneDescriptionKey = "garage.modifier.noneDescription";
    [SerializeField] string modifierSelectionSummaryKey = "garage.modifier.selectionSummary";
    [SerializeField] string modifierMultiplierKey = "garage.modifier.multiplier";
    [SerializeField] string modifierEffectsKey = "garage.modifier.effects";
    [SerializeField] string modifierUnsupportedTrafficKey = "garage.modifier.unsupportedTraffic";
    [SerializeField] string modifierUnsupportedPoliceKey = "garage.modifier.unsupportedPolice";
    [SerializeField] string modifierConflictKey = "garage.modifier.conflict";

    int mapIndex;
    int modifierIndex;
    int difficultyIndex;
    int sessionDurationIndex;
    bool listenersBound;
    bool runtimeLayoutBuilt;
    RectTransform runtimeMapContent;
    GameObject runtimeMapBrowser;
    TextMeshProUGUI runtimeMapBrowserTitle;
    readonly List<Image> runtimeMapCardBackgrounds = new List<Image>();
    readonly List<TMP_Text> runtimeMapCardLabels = new List<TMP_Text>();
    readonly List<Toggle> runtimeModifierToggles = new List<Toggle>();
    readonly List<TMP_Text> runtimeModifierLabels = new List<TMP_Text>();
    readonly List<TMP_Text> runtimeModifierDetails = new List<TMP_Text>();
    readonly List<string> previewModifierIds = new List<string>();
    RectTransform runtimeModifierRoot;
    bool modifierLayoutBuilt;
    bool ownsRuntimeModifierRoot;

    /// <summary>Raised when the previewed map or modifier changes, or when the active selection is applied.</summary>
    public event System.Action SelectionChanged;

    void OnEnable() {
        LocalizationManager.LanguageChanged += RefreshUI;
    }

    void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshUI;
    }

    void OnDestroy() {
        LocalizationManager.LanguageChanged -= RefreshUI;
        ClearRuntimeModifierListeners();
        if (!ownsRuntimeModifierRoot || runtimeModifierRoot == null) return;
        GameObject ownedRoot = runtimeModifierRoot.gameObject;
        runtimeModifierRoot = null;
        modifierContent = null;
        modifierScroll = null;
        if (Application.isPlaying) Destroy(ownedRoot);
        else DestroyImmediate(ownedRoot);
    }

    void ClearRuntimeModifierListeners() {
        if (modifierContent == null) return;
        for (int i = 0; i < modifierContent.childCount; i++) {
            Toggle toggle = modifierContent.GetChild(i).GetComponent<Toggle>();
            if (toggle != null) toggle.onValueChanged.RemoveAllListeners();
        }
    }

    void Start() {
        BuildRuntimeLayout();
        BuildRuntimeModifierLayout();
        BindListeners();
        ResolveInitialSelection();
        RefreshUI();
    }

    void BindListeners() {
        if (listenersBound) return;
        if (previousMapButton != null && previousMapButton != previousDifficultyButton)
            previousMapButton.onClick.AddListener(() => ChangeMap(-1));
        if (nextMapButton != null && nextMapButton != nextDifficultyButton)
            nextMapButton.onClick.AddListener(() => ChangeMap(1));
        if (previousDifficultyButton != null) previousDifficultyButton.onClick.AddListener(() => ChangeDifficulty(-1));
        if (nextDifficultyButton != null) nextDifficultyButton.onClick.AddListener(() => ChangeDifficulty(1));
        if (previousSessionDurationButton != null) previousSessionDurationButton.onClick.AddListener(() => ChangeSessionDuration(-1));
        if (nextSessionDurationButton != null) nextSessionDurationButton.onClick.AddListener(() => ChangeSessionDuration(1));
        if (previousModifierButton != null) previousModifierButton.gameObject.SetActive(false);
        if (nextModifierButton != null) nextModifierButton.gameObject.SetActive(false);
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
        previewModifierIds.Clear();
        if (manager.SelectedModifierIds != null)
            for (int i = 0; i < manager.SelectedModifierIds.Count; i++)
                if (!previewModifierIds.Contains(manager.SelectedModifierIds[i])) previewModifierIds.Add(manager.SelectedModifierIds[i]);
        MapData map = GetSelectedMap(manager);
        difficultyIndex = FindDifficultyIndex(map != null ? map.levelData : null, manager.currentDifficultyId);
        if (difficultyIndex < 0) difficultyIndex = 0;
        sessionDurationIndex = FindSessionDurationIndex(manager.SelectedShiftDurationMinutes);
    }

    void ChangeMap(int direction) {
        var manager = GameManager.Instance;
        if (manager == null || manager.Content == null || manager.Content.Maps.Count == 0) return;
        mapIndex = WrapIndex(mapIndex + direction, manager.Content.Maps.Count);
        difficultyIndex = 0;
        RefreshUI();
    }

    void ChangeDifficulty(int direction) {
        var manager = GameManager.Instance;
        MapData map = GetSelectedMap(manager);
        int count = map != null && map.levelData != null && map.levelData.difficultyLevels != null
            ? map.levelData.difficultyLevels.Count : 0;
        if (count <= 0) return;
        difficultyIndex = WrapIndex(difficultyIndex + direction, count);
        RefreshUI();
    }

    void ChangeModifier(int direction) {
        var manager = GameManager.Instance;
        int count = manager != null && manager.allModifiers != null ? manager.allModifiers.Length + 1 : 1;
        if (count <= 0) return;
        modifierIndex = WrapIndex(modifierIndex + direction, count);
        RefreshUI();
    }

    void ToggleModifier(string modifierId, bool selected) {
        List<string> next = ModifierSelectionUiHelper.Toggle(previewModifierIds, modifierId, selected);
        previewModifierIds.Clear();
        previewModifierIds.AddRange(next);
        RefreshUI();
    }

    void ChangeSessionDuration(int direction) {
        if (sessionDurationOptionsMinutes == null || sessionDurationOptionsMinutes.Length == 0) return;
        sessionDurationIndex = WrapIndex(sessionDurationIndex + direction, sessionDurationOptionsMinutes.Length);
        RefreshUI();
    }

    /// <summary>Applies the currently previewed map, difficulty, and modifier to the next shift.</summary>
    /// <returns>True when the preview was valid and applied successfully.</returns>
    public bool TryApplyPreviewSelection() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        if (manager == null || map == null) return false;

        MapDifficultyData difficulty = GetSelectedDifficulty(map);
        if (difficulty != null && !manager.IsDifficultyUnlocked(map, difficultyIndex)) return false;
        if (!CanApplyModifiers(manager, map)) return false;
        if (!TryGetPreviewDuration(out int selectedDuration)) return false;
        if (!manager.IsMapOwned(map) && !manager.CanPurchaseMap(map)) return false;

        if (!manager.IsMapOwned(map) && !manager.TryPurchaseMap(map)) return false;
        manager.SelectMap(map);
        if (difficulty != null)
            manager.SelectDifficulty(map, difficulty.difficultyId);
        manager.SelectModifiers(previewModifierIds);
        manager.SelectShiftDuration(selectedDuration);
        ResolveInitialSelection();
        RefreshUI();
        return IsPreviewApplied();
    }

    /// <summary>Returns whether the current preview can be applied by the unified Start Job action.</summary>
    /// <returns>True when the selected map is owned or affordable and its difficulty is unlocked.</returns>
    public bool CanApplyPreviewSelection() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        if (manager == null || map == null) return false;
        MapDifficultyData difficulty = GetSelectedDifficulty(map);
        bool difficultyAvailable = difficulty == null || manager.IsDifficultyUnlocked(map, difficultyIndex);
        return difficultyAvailable && CanApplyModifiers(manager, map) && TryGetPreviewDuration(out _) &&
            (manager.IsMapOwned(map) || manager.CanPurchaseMap(map));
    }

    /// <summary>Returns whether the preview currently matches the owned map selected for the next shift.</summary>
    /// <returns>True when the previewed map is the active owned map.</returns>
    public bool IsPreviewApplied() {
        var manager = GameManager.Instance;
        if (manager == null) return false;
        var map = GetSelectedMap(manager);
        MapDifficultyData difficulty = GetSelectedDifficulty(map);
        bool difficultyApplied = difficulty == null || manager.CurrentDifficulty == difficulty ||
            (manager.CurrentDifficulty != null && difficulty != null && manager.CurrentDifficulty.difficultyId == difficulty.difficultyId);
        bool durationApplied = sessionDurationOptionsMinutes != null && sessionDurationOptionsMinutes.Length > 0 &&
            manager.SelectedShiftDurationMinutes == Mathf.Max(1, sessionDurationOptionsMinutes[Mathf.Clamp(sessionDurationIndex, 0, sessionDurationOptionsMinutes.Length - 1)]);
        return map != null && manager.IsMapOwned(map) && manager.currentMap == map &&
            AreModifierSelectionsApplied(manager) && difficultyApplied &&
            durationApplied &&
            (difficulty == null || manager.IsDifficultyUnlocked(map, difficultyIndex));
    }

    // The authoritative applied state remains GameManager's duplicate-free multi-select id set.
    // Preview ids stay local until the Start Job action completes all validation.
    bool AreModifierSelectionsApplied(GameManager manager) {
        ModifierSelectionResult preview = ModifierSelectionResolver.Resolve(manager.allModifiers, previewModifierIds);
        if (preview.rejectedIds.Count > 0) return false;
        var selectedIds = manager.SelectedModifierIds;
        if (selectedIds == null || selectedIds.Count != preview.resolvedModifiers.Count) return false;
        for (int i = 0; i < selectedIds.Count; i++)
            if (selectedIds[i] != preview.resolvedModifiers[i].modifierId) return false;
        return true;
    }

    bool CanApplyModifiers(GameManager manager, MapData map) {
        ModifierSelectionResult resolved = ModifierSelectionResolver.Resolve(manager.allModifiers, previewModifierIds);
        if (resolved.rejectedIds.Count > 0) {
            RefreshModifierUI(manager, map, resolved);
            return false;
        }
        for (int i = 0; i < resolved.resolvedModifiers.Count; i++) {
            if (!ModifierSelectionUiHelper.IsSupported(resolved.resolvedModifiers[i], map)) {
                RefreshModifierUI(manager, map, resolved);
                return false;
            }
        }
        return true;
    }

    bool TryGetPreviewDuration(out int duration) {
        duration = 0;
        if (sessionDurationOptionsMinutes == null || sessionDurationOptionsMinutes.Length == 0) return false;
        int index = Mathf.Clamp(sessionDurationIndex, 0, sessionDurationOptionsMinutes.Length - 1);
        duration = sessionDurationOptionsMinutes[index];
        return duration > 0;
    }

    void RefreshUI() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        if (runtimeMapBrowserTitle != null) runtimeMapBrowserTitle.text = LocalizationManager.Get(mapBrowserTitleKey);
        if (manager == null || map == null) {
            SetMapControlsActive(false);
            return;
        }

        SetMapControlsActive(true);
        if (mapNameText != null) mapNameText.text = map.GetDisplayName();
        if (mapDescriptionText != null) mapDescriptionText.text = map.GetDescription();
        if (mapPreviewImage != null) {
            bool hasPreview = map.previewImage != null;
            mapPreviewImage.sprite = hasPreview ? map.previewImage : MapCardPreview.GetFallbackSprite();
            mapPreviewImage.color = hasPreview ? Color.white : map.previewFallbackColor;
            mapPreviewImage.enabled = true;
        }

        bool mapOwned = manager.IsMapOwned(map);
        bool isSelected = manager.currentMap == map;
        if (mapStatusText != null) {
            if (!mapOwned) mapStatusText.text = LocalizationManager.Get(mapLockedKey, Mathf.Max(0, map.unlockPrice));
            else if (isSelected) mapStatusText.text = LocalizationManager.Get(mapSelectedKey);
            else mapStatusText.text = LocalizationManager.Get(mapAvailableKey);
        }
        if (startJobButton != null) {
            MapDifficultyData selectedDifficulty = GetSelectedDifficulty(map);
            bool difficultyAvailable = selectedDifficulty == null || manager.IsDifficultyUnlocked(map, difficultyIndex);
            startJobButton.interactable = difficultyAvailable && (mapOwned || manager.CanPurchaseMap(map));
            var label = startJobButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = LocalizationManager.Get(mapOwned ? startJobKey : mapPurchaseKey);
        }

        RefreshDifficultyUI(manager, map);
        RefreshSessionDurationUI();
        RefreshRuntimeMapCards(manager);

        RefreshModifierUI(manager, map, null);

        if (SelectionChanged != null) SelectionChanged.Invoke();
    }

    void SetMapControlsActive(bool active) {
        if (previousMapButton != null && previousMapButton != previousDifficultyButton)
            previousMapButton.gameObject.SetActive(active && !runtimeLayoutBuilt);
        if (nextMapButton != null && nextMapButton != nextDifficultyButton)
            nextMapButton.gameObject.SetActive(active && !runtimeLayoutBuilt);
        if (startJobButton != null) startJobButton.gameObject.SetActive(active);
        if (previousDifficultyButton != null) previousDifficultyButton.gameObject.SetActive(active);
        if (nextDifficultyButton != null) nextDifficultyButton.gameObject.SetActive(active);
        if (runtimeMapBrowser != null) runtimeMapBrowser.SetActive(active);
    }

    void BuildRuntimeLayout() {
        if (!buildRuntimeMapGrid || runtimeLayoutBuilt || mapNameText == null || startJobButton == null) return;

        RectTransform detailBox = mapNameText.transform.parent as RectTransform;
        if (detailBox == null) return;

        if (mapBrowserRoot != null && mapBrowserContent != null) {
            runtimeMapBrowser = mapBrowserRoot.gameObject;
            runtimeMapContent = mapBrowserContent;
            runtimeMapBrowserTitle = mapBrowserTitleText;
            ConfigureAuthoredMapBrowser();
        }
        else {
            runtimeMapBrowser = CreateRuntimeMapBrowser();
        }
        if (runtimeMapBrowser == null || runtimeMapContent == null) return;

        SetResponsiveRect(detailBox, detailPanelAnchorMin, detailPanelAnchorMax);
        CreateRuntimeDetailFields(detailBox);
        runtimeLayoutBuilt = true;
    }

    void ConfigureAuthoredMapBrowser() {
        RectTransform browserRect = runtimeMapBrowser != null ? runtimeMapBrowser.transform as RectTransform : null;
        SetResponsiveRect(browserRect, mapBrowserAnchorMin, mapBrowserAnchorMax);
        Image background = runtimeMapBrowser != null ? runtimeMapBrowser.GetComponent<Image>() : null;
        if (background != null) {
            background.sprite = MapCardPreview.GetFallbackSprite();
            background.type = Image.Type.Simple;
            background.color = runtimePanelColor;
        }
        ConfigureMapBrowserViewport(runtimeMapContent != null ? runtimeMapContent.parent as RectTransform : null);
        SetResponsiveRect(runtimeMapContent, new Vector2(0f, 1f), new Vector2(1f, 1f));
        runtimeMapContent.pivot = new Vector2(0f, 1f);
        runtimeMapContent.anchoredPosition = Vector2.zero;
        if (mapBrowserScroll != null) {
            mapBrowserScroll.viewport = mapBrowserScroll.viewport != null
                ? mapBrowserScroll.viewport : runtimeMapContent.parent as RectTransform;
            mapBrowserScroll.content = runtimeMapContent;
            mapBrowserScroll.horizontal = false;
            mapBrowserScroll.vertical = true;
            mapBrowserScroll.movementType = ScrollRect.MovementType.Clamped;
        }
        if (runtimeMapBrowserTitle != null) {
            SetResponsiveText(runtimeMapBrowserTitle, new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.99f));
            SetRuntimeTextStyle(runtimeMapBrowserTitle, browserTitleFontSize, FontStyles.Bold);
        }
    }

    GameObject CreateRuntimeMapBrowser() {
        GameObject browser = new GameObject("MapBrowserRuntime", typeof(RectTransform), typeof(Image));
        browser.transform.SetParent(transform, false);
        RectTransform browserRect = browser.GetComponent<RectTransform>();
        browserRect.anchorMin = mapBrowserAnchorMin;
        browserRect.anchorMax = mapBrowserAnchorMax;
        browserRect.pivot = new Vector2(0.5f, 0.5f);
        browserRect.offsetMin = Vector2.zero;
        browserRect.offsetMax = Vector2.zero;
        Image browserBackground = browser.GetComponent<Image>();
        browserBackground.sprite = MapCardPreview.GetFallbackSprite();
        browserBackground.type = Image.Type.Simple;
        browserBackground.color = runtimePanelColor;

        GameObject viewport = new GameObject("MapBrowserViewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(browser.transform, false);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        ConfigureMapBrowserViewport(viewportRect);

        GameObject content = new GameObject("MapBrowserContent", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        float viewportVerticalPadding = mapBrowserViewportTopPadding + mapBrowserViewportBottomPadding;
        contentRect.sizeDelta = new Vector2(0f, Mathf.Max(0f, mapGridSize.y - viewportVerticalPadding));
        runtimeMapContent = contentRect;

        runtimeMapBrowserTitle = CreateRuntimeText(browserRect, mapNameText,
            new Vector2(0.04f, 0.91f), new Vector2(0.96f, 0.99f));
        runtimeMapBrowserTitle.text = LocalizationManager.Get(mapBrowserTitleKey);
        SetRuntimeTextStyle(runtimeMapBrowserTitle, browserTitleFontSize, FontStyles.Bold);

        ScrollRect scroll = browser.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return browser;
    }

    void ConfigureMapBrowserViewport(RectTransform viewport) {
        if (viewport == null) return;
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(mapBrowserViewportSidePadding, mapBrowserViewportBottomPadding);
        viewport.offsetMax = new Vector2(-mapBrowserViewportSidePadding, -mapBrowserViewportTopPadding);
        viewport.pivot = new Vector2(0.5f, 0.5f);
    }

    void CreateRuntimeDetailFields(RectTransform detailBox) {
        SetResponsiveText(panelTitleText, new Vector2(0.05f, 0.93f), new Vector2(0.95f, 0.99f));
        SetRuntimeTextStyle(panelTitleText, panelTitleFontSize, FontStyles.Bold);
        SetResponsiveText(mapNameText, new Vector2(0.05f, 0.87f), new Vector2(0.95f, 0.93f));
        SetRuntimeTextStyle(mapNameText, mapNameFontSize, FontStyles.Bold);
        SetResponsiveText(mapDescriptionText, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.87f));
        SetRuntimeTextStyle(mapDescriptionText, descriptionFontSize, FontStyles.Normal);
        SetResponsiveText(mapStatusText, new Vector2(0.05f, 0.75f), new Vector2(0.95f, 0.80f));
        SetRuntimeTextStyle(mapStatusText, statusFontSize, FontStyles.Normal);
        ConfigureRuntimeMapPreview(detailBox, new Vector2(0.18f, 0.56f), new Vector2(0.82f, 0.75f));
        difficultyNameText = ConfigureRuntimeText(difficultyNameText, detailBox, mapNameText,
            new Vector2(0.05f, 0.50f), new Vector2(0.95f, 0.55f), "DifficultyName");
        SetRuntimeTextStyle(difficultyNameText, difficultyNameFontSize, FontStyles.Bold);
        difficultyDescriptionText = ConfigureRuntimeText(difficultyDescriptionText, detailBox, mapDescriptionText,
            new Vector2(0.05f, 0.45f), new Vector2(0.95f, 0.50f), "DifficultyDescription");
        SetRuntimeTextStyle(difficultyDescriptionText, difficultyDescriptionFontSize, FontStyles.Normal);
        difficultyStatusText = ConfigureRuntimeText(difficultyStatusText, detailBox, mapStatusText,
            new Vector2(0.05f, 0.41f), new Vector2(0.95f, 0.45f), "DifficultyStatus");
        SetRuntimeTextStyle(difficultyStatusText, difficultyStatusFontSize, FontStyles.Normal);
        difficultySummaryText = ConfigureRuntimeText(difficultySummaryText, detailBox, mapDescriptionText,
            new Vector2(0.05f, 0.30f), new Vector2(0.95f, 0.40f), "DifficultySummary");
        SetRuntimeTextStyle(difficultySummaryText, difficultySummaryFontSize, FontStyles.Normal);
        difficultyStarsGraphic = ConfigureRuntimeStars(difficultyStarsGraphic, difficultyStarsText, detailBox,
            new Vector2(0.35f, 0.27f), new Vector2(0.65f, 0.32f));
        previousDifficultyButton = ConfigureRuntimeButton(previousDifficultyButton, detailBox, previousMapButton,
            new Vector2(0.06f, 0.25f), new Vector2(0.22f, 0.34f), "<");
        nextDifficultyButton = ConfigureRuntimeButton(nextDifficultyButton, detailBox, nextMapButton,
            new Vector2(0.78f, 0.25f), new Vector2(0.94f, 0.34f), ">");

        sessionDurationText = ConfigureRuntimeText(sessionDurationText, detailBox, mapDescriptionText,
            new Vector2(0.25f, 0.20f), new Vector2(0.75f, 0.25f), "SessionDuration");
        SetRuntimeTextStyle(sessionDurationText, sessionDurationFontSize, FontStyles.Bold);
        previousSessionDurationButton = ConfigureRuntimeButton(previousSessionDurationButton, detailBox, previousDifficultyButton,
            new Vector2(0.06f, 0.19f), new Vector2(0.22f, 0.25f), "<");
        nextSessionDurationButton = ConfigureRuntimeButton(nextSessionDurationButton, detailBox, nextDifficultyButton,
            new Vector2(0.78f, 0.19f), new Vector2(0.94f, 0.25f), ">");

        SetResponsiveText(modifierTitleText, new Vector2(0.05f, 0.15f), new Vector2(0.95f, 0.19f));
        SetRuntimeTextStyle(modifierTitleText, modifierTitleFontSize, FontStyles.Bold);
        SetResponsiveText(modifierNameText, new Vector2(0.05f, 0.10f), new Vector2(0.95f, 0.15f));
        SetRuntimeTextStyle(modifierNameText, modifierNameFontSize, FontStyles.Bold);
        SetResponsiveText(modifierDescriptionText, new Vector2(0.05f, 0.05f), new Vector2(0.95f, 0.10f));
        SetRuntimeTextStyle(modifierDescriptionText, modifierDescriptionFontSize, FontStyles.Normal);
        SetResponsiveRect(previousModifierButton != null ? previousModifierButton.transform as RectTransform : null,
            new Vector2(0.06f, 0.01f), new Vector2(0.22f, 0.05f));
        SetResponsiveRect(nextModifierButton != null ? nextModifierButton.transform as RectTransform : null,
            new Vector2(0.78f, 0.01f), new Vector2(0.94f, 0.05f));
        SetResponsiveRect(startJobButton != null ? startJobButton.transform as RectTransform : null,
            new Vector2(0.25f, 0.01f), new Vector2(0.75f, 0.06f));
    }

    void BuildRuntimeModifierLayout() {
        if (modifierLayoutBuilt || modifierNameText == null) return;
        RectTransform parent = modifierNameText.transform.parent as RectTransform;
        if (parent == null) return;

        if (modifierTitleText != null) modifierTitleText.gameObject.SetActive(false);
        if (modifierNameText != null) modifierNameText.gameObject.SetActive(false);
        if (modifierDescriptionText != null) modifierDescriptionText.gameObject.SetActive(false);
        if (previousModifierButton != null) previousModifierButton.gameObject.SetActive(false);
        if (nextModifierButton != null) nextModifierButton.gameObject.SetActive(false);

        if (modifierScroll != null && modifierContent != null) {
            runtimeModifierRoot = modifierScroll.transform as RectTransform;
            ownsRuntimeModifierRoot = false;
        }
        else {
            GameObject scrollObject = new GameObject("ModifierSelectionScroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            scrollObject.transform.SetParent(parent, false);
            runtimeModifierRoot = scrollObject.transform as RectTransform;
            ownsRuntimeModifierRoot = true;
            SetResponsiveRect(runtimeModifierRoot, new Vector2(0.04f, 0.06f), new Vector2(0.96f, 0.15f));
            Image background = scrollObject.GetComponent<Image>();
            background.sprite = MapCardPreview.GetFallbackSprite();
            background.type = Image.Type.Simple;
            background.color = runtimePanelColor;
            GameObject viewportObject = new GameObject("ModifierSelectionViewport", typeof(RectTransform), typeof(RectMask2D));
            viewportObject.transform.SetParent(scrollObject.transform, false);
            RectTransform viewport = viewportObject.transform as RectTransform;
            SetResponsiveRect(viewport, Vector2.zero, Vector2.one);
            GameObject contentObject = new GameObject("ModifierSelectionContent", typeof(RectTransform));
            contentObject.transform.SetParent(viewportObject.transform, false);
            modifierContent = contentObject.transform as RectTransform;
            modifierContent.anchorMin = new Vector2(0f, 1f);
            modifierContent.anchorMax = new Vector2(1f, 1f);
            modifierContent.pivot = new Vector2(0f, 1f);
            modifierContent.anchoredPosition = Vector2.zero;
            ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = modifierContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            modifierScroll = scroll;
        }

        if (modifierSummaryText == null)
            modifierSummaryText = CreateRuntimeText(parent, modifierNameText, new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.19f));
        else SetResponsiveText(modifierSummaryText, new Vector2(0.04f, 0.15f), new Vector2(0.96f, 0.19f));
        SetRuntimeTextStyle(modifierSummaryText, modifierTitleFontSize, FontStyles.Bold);
        modifierSummaryText.gameObject.name = "ModifierSelectionSummary";
        modifierLayoutBuilt = true;
    }

    void RefreshModifierUI(GameManager manager, MapData map, ModifierSelectionResult resolved) {
        if (!modifierLayoutBuilt || modifierContent == null || manager == null) return;
        float previousScroll = modifierScroll != null ? modifierScroll.verticalNormalizedPosition : 1f;
        while (modifierContent.childCount < (manager.allModifiers != null ? manager.allModifiers.Length : 0))
            CreateRuntimeModifierRow(modifierContent, modifierContent.childCount);
        int targetCount = manager.allModifiers != null ? manager.allModifiers.Length : 0;
        int excessCount = Mathf.Max(0, modifierContent.childCount - targetCount);
        for (int i = 0; i < excessCount; i++) {
            Transform child = modifierContent.GetChild(modifierContent.childCount - 1);
            Toggle toggle = child.GetComponent<Toggle>();
            if (toggle != null) toggle.onValueChanged.RemoveAllListeners();
            child.SetParent(null);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        runtimeModifierToggles.Clear();
        runtimeModifierLabels.Clear();
        runtimeModifierDetails.Clear();
        int count = manager.allModifiers != null ? manager.allModifiers.Length : 0;
        float rowHeight = 70f;
        modifierContent.sizeDelta = new Vector2(0f, Mathf.Max(rowHeight, count * rowHeight));
        ModifierSelectionResult previewResult = resolved ?? ModifierSelectionResolver.Resolve(manager.allModifiers, previewModifierIds);
        for (int i = 0; i < count; i++) {
            ShiftModifierData modifier = manager.allModifiers[i];
            Transform row = modifierContent.GetChild(i);
            Toggle toggle = row.GetComponent<Toggle>();
            TMP_Text label = row.GetChild(0).GetComponent<TMP_Text>();
            TMP_Text detail = row.GetChild(1).GetComponent<TMP_Text>();
            runtimeModifierToggles.Add(toggle);
            runtimeModifierLabels.Add(label);
            runtimeModifierDetails.Add(detail);
            RectTransform rowRect = row as RectTransform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.sizeDelta = new Vector2(0f, rowHeight);
            rowRect.anchoredPosition = new Vector2(0f, -i * rowHeight);
            bool supported = ModifierSelectionUiHelper.IsSupported(modifier, map);
            bool selected = ContainsId(previewModifierIds, modifier != null ? modifier.modifierId : string.Empty);
            toggle.onValueChanged.RemoveAllListeners();
            toggle.isOn = selected;
            toggle.interactable = modifier != null && (supported || selected);
            toggle.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            Image rowBackground = row.GetComponent<Image>();
            if (rowBackground != null)
                rowBackground.color = selected ? runtimeSelectedCardColor : runtimeCardColor;
            string capturedModifierId = modifier != null ? modifier.modifierId : string.Empty;
            toggle.onValueChanged.AddListener(value => {
                if (!string.IsNullOrEmpty(capturedModifierId)) ToggleModifier(capturedModifierId, value);
            });
            label.text = modifier != null ? modifier.GetDisplayName() : string.Empty;
            detail.text = modifier == null ? string.Empty : modifier.GetDescription() + "\n" +
                LocalizationManager.Get(modifierMultiplierKey, modifier.scoreMultiplier) + " | " +
                LocalizationManager.Get(modifierEffectsKey, modifier.speedDelta, modifier.turnDelta, modifier.healthDelta,
                    modifier.capacityDelta) +
                (supported ? string.Empty : "\n" + GetUnsupportedModifierText(modifier));
            label.raycastTarget = false;
            detail.raycastTarget = false;
        }
        if (modifierScroll != null) {
            modifierScroll.verticalNormalizedPosition = previousScroll;
            EnsureFocusedModifierVisible();
        }

        var supportedPreview = new List<ShiftModifierData>();
        for (int i = 0; i < previewResult.resolvedModifiers.Count; i++)
            if (ModifierSelectionUiHelper.IsSupported(previewResult.resolvedModifiers[i], map))
                supportedPreview.Add(previewResult.resolvedModifiers[i]);
        float product = ModifierSelectionUiHelper.GetPreviewScoreMultiplier(supportedPreview);
        string summary = LocalizationManager.Get(modifierSelectionSummaryKey, supportedPreview.Count, product,
            ModifierEffectResolver.GetCombinedStatDelta(supportedPreview, VehicleStatId.Speed),
            ModifierEffectResolver.GetCombinedStatDelta(supportedPreview, VehicleStatId.Turn),
            ModifierEffectResolver.GetCombinedStatDelta(supportedPreview, VehicleStatId.Health),
            ModifierEffectResolver.GetCombinedStatDelta(supportedPreview, VehicleStatId.Armor),
            ModifierEffectResolver.GetCombinedStatDelta(supportedPreview, VehicleStatId.Capacity),
            ModifierEffectResolver.GetCombinedStatDelta(supportedPreview, VehicleStatId.Protection));
        if (previewResult.rejectedIds.Count > 0) summary += "\n" + LocalizationManager.Get(modifierConflictKey);
        if (modifierSummaryText != null) modifierSummaryText.text = summary;
        if (modifierStatusText != null) modifierStatusText.text = string.Empty;
    }

    void CreateRuntimeModifierRow(RectTransform parent, int index) {
        GameObject rowObject = new GameObject("ModifierToggleRow", typeof(RectTransform), typeof(Image), typeof(Toggle));
        rowObject.transform.SetParent(parent, false);
        Image background = rowObject.GetComponent<Image>();
        background.sprite = MapCardPreview.GetFallbackSprite();
        background.type = Image.Type.Simple;
        background.color = runtimeCardColor;
        Toggle toggle = rowObject.GetComponent<Toggle>();
        toggle.targetGraphic = background;
        TextMeshProUGUI label = CreateRuntimeText(rowObject.transform as RectTransform, modifierNameText,
            new Vector2(0.02f, 0.52f), new Vector2(0.34f, 0.98f));
        TextMeshProUGUI detail = CreateRuntimeText(rowObject.transform as RectTransform, modifierDescriptionText,
            new Vector2(0.36f, 0.05f), new Vector2(0.98f, 0.98f));
        SetRuntimeTextStyle(label, modifierNameFontSize, FontStyles.Bold);
        SetRuntimeTextStyle(detail, modifierDescriptionFontSize, FontStyles.Normal);
    }

    void EnsureFocusedModifierVisible() {
        if (modifierScroll == null || modifierContent == null || modifierContent.childCount == 0) return;
        GameObject focused = UnityEngine.EventSystems.EventSystem.current != null
            ? UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject : null;
        if (focused == null || !focused.transform.IsChildOf(modifierContent)) return;
        Transform focusedRow = focused.transform;
        while (focusedRow.parent != modifierContent && focusedRow.parent != null) focusedRow = focusedRow.parent;
        if (focusedRow.parent != modifierContent) return;
        int index = focusedRow.GetSiblingIndex();
        float contentHeight = modifierContent.rect.height;
        float viewportHeight = modifierScroll.viewport != null ? modifierScroll.viewport.rect.height : 0f;
        float maxScroll = Mathf.Max(0f, contentHeight - viewportHeight);
        if (maxScroll <= 0f) return;
        float scroll = (1f - modifierScroll.verticalNormalizedPosition) * maxScroll;
        RectTransform focusedRowRect = modifierContent.GetChild(index) as RectTransform;
        float rowHeight = focusedRowRect != null ? focusedRowRect.rect.height : 0f;
        float rowTop = index * rowHeight;
        float rowBottom = rowTop + rowHeight;
        if (rowTop < scroll) scroll = rowTop;
        else if (rowBottom > scroll + viewportHeight) scroll = rowBottom - viewportHeight;
        modifierScroll.verticalNormalizedPosition = 1f - Mathf.Clamp(scroll, 0f, maxScroll) / maxScroll;
    }

    string GetUnsupportedModifierText(ShiftModifierData modifier) {
        return modifier.disablesPolice
            ? LocalizationManager.Get(modifierUnsupportedPoliceKey)
            : LocalizationManager.Get(modifierUnsupportedTrafficKey);
    }

    static bool ContainsId(IReadOnlyList<string> ids, string id) {
        if (ids == null || string.IsNullOrEmpty(id)) return false;
        for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
        return false;
    }

    TextMeshProUGUI ConfigureRuntimeText(TextMeshProUGUI existing, RectTransform parent, TMP_Text template,
        Vector2 anchorMin, Vector2 anchorMax, string objectName) {
        if (existing == null) existing = CreateRuntimeText(parent, template, anchorMin, anchorMax);
        else SetResponsiveText(existing, anchorMin, anchorMax);
        if (existing != null) existing.gameObject.name = objectName;
        return existing;
    }

    DifficultyStarGraphic ConfigureRuntimeStars(DifficultyStarGraphic existing, TextMeshProUGUI legacyText,
        RectTransform parent, Vector2 anchorMin, Vector2 anchorMax) {
        if (existing == null && legacyText != null)
            existing = legacyText.transform.parent != null
                ? legacyText.transform.parent.GetComponentInChildren<DifficultyStarGraphic>(true)
                : null;

        if (existing == null && legacyText != null) {
            legacyText.text = string.Empty;
            legacyText.enabled = false;
        }

        if (existing == null) {
            GameObject starObject = new GameObject("DifficultyStars", typeof(RectTransform),
                typeof(CanvasRenderer), typeof(DifficultyStarGraphic));
            starObject.transform.SetParent(parent, false);
            existing = starObject.GetComponent<DifficultyStarGraphic>();
        }

        SetResponsiveRect(existing.transform as RectTransform, anchorMin, anchorMax);
        existing.SetStars(difficultyStarCount, Mathf.Clamp(difficultyIndex + 1, 0, difficultyStarCount));
        existing.SetColors(filledStarColor, emptyStarColor);
        existing.SetLayout(Mathf.Max(1f, difficultyStarsFontSize), 8f);
        existing.raycastTarget = false;
        return existing;
    }

    Button ConfigureRuntimeButton(Button existing, RectTransform parent, Button template,
        Vector2 anchorMin, Vector2 anchorMax, string labelText) {
        if (existing == null) return CreateRuntimeButton(parent, template, anchorMin, anchorMax, labelText);
        existing.onClick.RemoveAllListeners();
        SetResponsiveRect(existing.transform as RectTransform, anchorMin, anchorMax);
        TMP_Text label = existing.GetComponentInChildren<TMP_Text>(true);
        if (label != null) {
            label.text = labelText;
            label.raycastTarget = false;
        }
        LocalizedText localizedLabel = existing.GetComponentInChildren<LocalizedText>(true);
        if (localizedLabel != null) localizedLabel.enabled = false;
        return existing;
    }

    void SetResponsiveText(TMP_Text text, Vector2 anchorMin, Vector2 anchorMax) {
        if (text == null) return;
        RectTransform rect = text.transform as RectTransform;
        SetResponsiveRect(rect, anchorMin, anchorMax);
    }

    void SetResponsiveRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax) {
        if (rect == null) return;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
    }

    void ConfigureRuntimeMapPreview(RectTransform detailBox, Vector2 anchorMin, Vector2 anchorMax) {
        if (mapPreviewImage != null) {
            SetResponsiveRect(mapPreviewImage.transform as RectTransform, anchorMin, anchorMax);
            mapPreviewImage.preserveAspect = true;
            return;
        }
        GameObject preview = new GameObject("SelectedMapPreview", typeof(RectTransform), typeof(Image));
        preview.transform.SetParent(detailBox, false);
        RectTransform rect = preview.GetComponent<RectTransform>();
        SetResponsiveRect(rect, anchorMin, anchorMax);
        mapPreviewImage = preview.GetComponent<Image>();
        mapPreviewImage.preserveAspect = true;
    }

    TextMeshProUGUI CreateRuntimeText(RectTransform parent, TMP_Text template, Vector2 anchorMin, Vector2 anchorMax) {
        GameObject textObject = template != null ? Instantiate(template.gameObject, parent) : new GameObject("RuntimeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        textObject.SetActive(true);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        LocalizedText localizedText = textObject.GetComponent<LocalizedText>();
        if (localizedText != null) localizedText.enabled = false;
        RectTransform rect = textObject.GetComponent<RectTransform>();
        SetResponsiveRect(rect, anchorMin, anchorMax);
        text.text = string.Empty;
        text.raycastTarget = false;
        text.enableAutoSizing = true;
        text.fontSizeMin = runtimeMinimumFontSize;
        text.fontSizeMax = Mathf.Max(runtimeMinimumFontSize, text.fontSize);
        text.alignment = TextAlignmentOptions.Center;
        return text;
    }

    void SetRuntimeTextStyle(TMP_Text text, float fontSize, FontStyles fontStyle) {
        if (text == null) return;
        text.enableAutoSizing = true;
        text.fontSizeMin = runtimeMinimumFontSize;
        text.fontSizeMax = fontSize;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
    }

    Button CreateRuntimeButton(RectTransform parent, Button template, Vector2 anchorMin, Vector2 anchorMax, string labelText) {
        GameObject buttonObject = template != null ? Instantiate(template.gameObject, parent) : new GameObject("RuntimeButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        SetResponsiveRect(rect, anchorMin, anchorMax);
        TMP_Text label = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (label != null) {
            label.text = labelText;
            label.raycastTarget = false;
        }
        LocalizedText localizedLabel = buttonObject.GetComponentInChildren<LocalizedText>(true);
        if (localizedLabel != null) localizedLabel.enabled = false;
        return button;
    }

    void RefreshRuntimeMapCards(GameManager manager) {
        if (runtimeMapContent == null || manager == null || manager.Content == null) return;

        while (runtimeMapContent.childCount < manager.Content.Maps.Count)
            CreateRuntimeMapCard(runtimeMapContent, runtimeMapContent.childCount);
        while (runtimeMapContent.childCount > manager.Content.Maps.Count) {
            Transform surplus = runtimeMapContent.GetChild(runtimeMapContent.childCount - 1);
            surplus.SetParent(null, false);
            surplus.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(surplus.gameObject);
            else DestroyImmediate(surplus.gameObject);
        }

        int columnCount = Mathf.Max(1, mapGridColumnCount);
        int rowCount = (manager.Content.Maps.Count + columnCount - 1) / columnCount;
        float requiredHeight = 16f + rowCount * mapCardSize.y + Mathf.Max(0, rowCount - 1) * mapCardSpacing.y;
        float viewportVerticalPadding = mapBrowserViewportTopPadding + mapBrowserViewportBottomPadding;
        runtimeMapContent.sizeDelta = new Vector2(0f,
            Mathf.Max(mapGridSize.y - viewportVerticalPadding, requiredHeight));

        runtimeMapCardBackgrounds.Clear();
        runtimeMapCardLabels.Clear();
        for (int i = 0; i < runtimeMapContent.childCount; i++) {
            MapData map = manager.Content.Maps[i];
            Button button = runtimeMapContent.GetChild(i).GetComponent<Button>();
            RectTransform cardRect = runtimeMapContent.GetChild(i) as RectTransform;
            if (button != null) {
                button.onClick.RemoveAllListeners();
                int capturedIndex = i;
                button.onClick.AddListener(() => {
                    mapIndex = capturedIndex;
                    difficultyIndex = 0;
                    RefreshUI();
                });
            }
            if (cardRect != null) {
                int column = i % columnCount;
                int row = i / columnCount;
                cardRect.anchorMin = new Vector2(0f, 1f);
                cardRect.anchorMax = new Vector2(0f, 1f);
                cardRect.pivot = new Vector2(0f, 1f);
                cardRect.sizeDelta = mapCardSize;
                cardRect.anchoredPosition = new Vector2(
                    8f + column * (mapCardSize.x + mapCardSpacing.x),
                    -8f - row * (mapCardSize.y + mapCardSpacing.y));
            }
            Image background = button != null ? button.targetGraphic as Image : null;
            TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (background != null) {
                background.sprite = MapCardPreview.GetFallbackSprite();
                background.type = Image.Type.Simple;
                bool owned = map != null && manager.IsMapOwned(map);
                background.color = i == mapIndex ? runtimeSelectedCardColor : owned ? runtimeCardColor : runtimeLockedCardColor;
                runtimeMapCardBackgrounds.Add(background);
            }
            if (label != null) {
                if (map == null) {
                    label.text = string.Empty;
                }
                else {
                    bool owned = manager.IsMapOwned(map);
                    string status = i == mapIndex
                        ? LocalizationManager.Get(mapSelectedKey)
                        : owned
                            ? LocalizationManager.Get(mapAvailableKey)
                            : LocalizationManager.Get(mapLockedKey, Mathf.Max(0, map.unlockPrice));
                    label.text = map.GetDisplayName() + "\n" + status;
                }
                SetRuntimeTextStyle(label, mapCardLabelFontSize, FontStyles.Bold);
                runtimeMapCardLabels.Add(label);
            }
            MapCardPreview preview = button != null ? button.GetComponent<MapCardPreview>() : null;
            if (preview != null) {
                preview.Initialize(cardRect);
                preview.SetSprite(map != null ? map.previewImage : null,
                    map != null ? map.previewFallbackColor : runtimeLockedCardColor);
            }
        }
    }

    void CreateRuntimeMapCard(RectTransform parent, int index) {
        GameObject cardObject = new GameObject("MapCard", typeof(RectTransform), typeof(Image), typeof(Button), typeof(MapCardPreview));
        cardObject.transform.SetParent(parent, false);
        Button button = cardObject.GetComponent<Button>();
        Image background = cardObject.GetComponent<Image>();
        background.sprite = MapCardPreview.GetFallbackSprite();
        background.type = Image.Type.Simple;
        background.color = runtimeCardColor;
        button.targetGraphic = background;
        int capturedIndex = index;
        button.onClick.AddListener(() => {
            mapIndex = capturedIndex;
            difficultyIndex = 0;
            RefreshUI();
        });

        GameObject labelObject = mapNameText != null ? Instantiate(mapNameText.gameObject, cardObject.transform) : new GameObject("MapCardLabel", typeof(RectTransform), typeof(TextMeshProUGUI));
        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        LocalizedText localizedLabel = labelObject.GetComponent<LocalizedText>();
        if (localizedLabel != null) localizedLabel.enabled = false;
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.anchoredPosition = new Vector2(0f, 8f);
        labelRect.sizeDelta = new Vector2(-10f, 42f);
        label.fontSize = Mathf.Min(label.fontSize, 15f);
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        cardObject.GetComponent<MapCardPreview>().Initialize(cardObject.GetComponent<RectTransform>());
        runtimeMapCardBackgrounds.Add(background);
        runtimeMapCardLabels.Add(label);
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

    MapDifficultyData GetSelectedDifficulty(MapData map) {
        if (map == null || map.levelData == null) return null;
        return map.levelData.GetDifficultyAt(difficultyIndex);
    }

    void RefreshDifficultyUI(GameManager manager, MapData map) {
        MapDifficultyData difficulty = GetSelectedDifficulty(map);
        bool hasDifficulty = difficulty != null;
        int difficultyCount = map != null && map.levelData != null && map.levelData.difficultyLevels != null
            ? map.levelData.difficultyLevels.Count : 0;
        if (difficultyNameText != null) {
            if (!hasDifficulty) {
                difficultyNameText.text = LocalizationManager.Get(difficultyUnavailableKey);
            }
            else {
                string name = difficulty.GetDisplayName();
                difficultyNameText.text = difficultyCount > 1
                    ? name + " (" + LocalizationManager.Get(difficultyCounterKey, difficultyIndex + 1, difficultyCount) + ")"
                    : name;
            }
        }
        if (difficultyDescriptionText != null) difficultyDescriptionText.text = hasDifficulty ? difficulty.GetDescription() : string.Empty;
        bool unlocked = hasDifficulty && manager != null && manager.IsDifficultyUnlocked(map, difficultyIndex);
        if (difficultyStatusText != null) {
            difficultyStatusText.text = !hasDifficulty ? string.Empty : LocalizationManager.Get(unlocked ? difficultyUnlockedKey : difficultyLockedKey);
        }
        if (difficultySummaryText != null) {
            if (!hasDifficulty) {
                difficultySummaryText.text = string.Empty;
            }
            else {
                int bestScore = manager != null ? manager.GetBestDifficultyScore(map, difficulty.difficultyId) : 0;
                string orders = LocalizationManager.Get(difficultyOrdersKey, difficulty.GetSafeOrderMin(), difficulty.GetSafeOrderMax());
                string loss = LocalizationManager.Get(difficultyLossKey, difficulty.pizzaLossChancePercent);
                string reward = LocalizationManager.Get(difficultyRewardKey, difficulty.rewardMultiplier);
                string best = LocalizationManager.Get(difficultyBestKey, bestScore);
                string target;
                if (!unlocked && difficultyIndex > 0) {
                    MapDifficultyData previous = map.levelData.GetDifficultyAt(difficultyIndex - 1);
                    target = previous != null && previous.unlockTargetScoreForNext > 0
                        ? LocalizationManager.Get(difficultyRequiresKey, previous.unlockTargetScoreForNext) : string.Empty;
                }
                else {
                    target = difficulty.unlockTargetScoreForNext > 0
                        ? LocalizationManager.Get(difficultyUnlockKey, difficulty.unlockTargetScoreForNext) : string.Empty;
                }
                difficultySummaryText.text = string.Join("\n", orders, loss, reward, best, target);
            }
        }
        if (difficultyStarsGraphic != null)
            difficultyStarsGraphic.SetStars(difficultyStarCount,
                hasDifficulty ? Mathf.Clamp(difficultyIndex + 1, 0, difficultyStarCount) : 0);
        else if (difficultyStarsText != null)
            difficultyStarsText.text = string.Empty;
        bool canCycle = map != null && map.levelData != null && map.levelData.difficultyLevels != null && map.levelData.difficultyLevels.Count > 1;
        if (previousDifficultyButton != null) previousDifficultyButton.interactable = canCycle;
        if (nextDifficultyButton != null) nextDifficultyButton.interactable = canCycle;
    }

    void RefreshSessionDurationUI() {
        int count = sessionDurationOptionsMinutes != null ? sessionDurationOptionsMinutes.Length : 0;
        bool available = count > 0;
        int minutes = available ? Mathf.Max(1, sessionDurationOptionsMinutes[Mathf.Clamp(sessionDurationIndex, 0, count - 1)]) : 0;
        if (sessionDurationText != null) {
            sessionDurationText.text = available
                ? LocalizationManager.Get(sessionDurationTitleKey) + "\n" + LocalizationManager.Get(sessionDurationValueKey, minutes)
                : string.Empty;
        }
        bool canCycle = count > 1;
        if (previousSessionDurationButton != null) previousSessionDurationButton.interactable = canCycle;
        if (nextSessionDurationButton != null) nextSessionDurationButton.interactable = canCycle;
    }

    int FindSessionDurationIndex(int minutes) {
        if (sessionDurationOptionsMinutes == null || sessionDurationOptionsMinutes.Length == 0) return 0;
        for (int i = 0; i < sessionDurationOptionsMinutes.Length; i++)
            if (sessionDurationOptionsMinutes[i] == minutes) return i;
        return 0;
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

    int FindDifficultyIndex(LevelData data, string difficultyId) {
        if (data == null || data.difficultyLevels == null || string.IsNullOrEmpty(difficultyId)) return -1;
        for (int i = 0; i < data.difficultyLevels.Count; i++) {
            MapDifficultyData difficulty = data.GetDifficultyAt(i);
            if (difficulty != null && difficulty.difficultyId == difficultyId) return i;
        }
        return -1;
    }

    int WrapIndex(int value, int count) {
        if (count <= 0) return 0;
        value %= count;
        return value < 0 ? value + count : value;
    }
}
