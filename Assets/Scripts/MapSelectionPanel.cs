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
    [SerializeField] Image mapPreviewImage;

    [Header("Runtime Map Browser")]
    [Tooltip("Builds the registry-driven map card grid when the scene has no authored card references.")]
    [SerializeField] bool buildRuntimeMapGrid = true;
    [SerializeField] Vector2 mapGridOffset = new Vector2(-300f, 0f);
    [SerializeField] Vector2 mapGridSize = new Vector2(500f, 600f);
    [SerializeField] Vector2 detailPanelOffset = new Vector2(300f, 0f);
    [SerializeField] Vector2 detailPanelSize = new Vector2(540f, 900f);
    [SerializeField] Vector2 mapCardSize = new Vector2(210f, 170f);
    [SerializeField] Vector2 mapCardSpacing = new Vector2(16f, 16f);
    [Min(1)] [SerializeField] int mapGridColumnCount = 2;
    [SerializeField] Color runtimePanelColor = new Color(0.04f, 0.08f, 0.12f, 0.92f);
    [SerializeField] Color runtimeCardColor = new Color(0.12f, 0.24f, 0.30f, 1f);
    [SerializeField] Color runtimeLockedCardColor = new Color(0.08f, 0.12f, 0.15f, 1f);
    [SerializeField] Color runtimeSelectedCardColor = new Color(0.20f, 0.48f, 0.58f, 1f);

    [Header("Difficulty Display")]
    [SerializeField] TextMeshProUGUI difficultyNameText;
    [SerializeField] TextMeshProUGUI difficultyDescriptionText;
    [SerializeField] TextMeshProUGUI difficultyStatusText;
    [SerializeField] TextMeshProUGUI difficultySummaryText;
    [SerializeField] Button previousDifficultyButton;
    [SerializeField] Button nextDifficultyButton;

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
    [SerializeField] string modifierNoneKey = "garage.modifier.none";
    [SerializeField] string modifierNoneDescriptionKey = "garage.modifier.noneDescription";

    int mapIndex;
    int modifierIndex;
    int difficultyIndex;
    bool listenersBound;
    bool runtimeLayoutBuilt;
    RectTransform runtimeMapContent;
    GameObject runtimeMapBrowser;
    readonly List<Image> runtimeMapCardBackgrounds = new List<Image>();
    readonly List<TMP_Text> runtimeMapCardLabels = new List<TMP_Text>();

    /// <summary>Raised when the previewed map or modifier changes, or when the active selection is applied.</summary>
    public event System.Action SelectionChanged;

    void OnEnable() {
        LocalizationManager.LanguageChanged += RefreshUI;
    }

    void OnDisable() {
        LocalizationManager.LanguageChanged -= RefreshUI;
    }

    void Start() {
        BuildRuntimeLayout();
        BindListeners();
        ResolveInitialSelection();
        RefreshUI();
    }

    void BindListeners() {
        if (listenersBound) return;
        if (previousMapButton != null) previousMapButton.onClick.AddListener(() => ChangeMap(-1));
        if (nextMapButton != null) nextMapButton.onClick.AddListener(() => ChangeMap(1));
        if (selectMapButton != null) selectMapButton.onClick.AddListener(SelectMap);
        if (previousDifficultyButton != null) previousDifficultyButton.onClick.AddListener(() => ChangeDifficulty(-1));
        if (nextDifficultyButton != null) nextDifficultyButton.onClick.AddListener(() => ChangeDifficulty(1));
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
        MapData map = GetSelectedMap(manager);
        difficultyIndex = FindDifficultyIndex(map != null ? map.levelData : null, manager.currentDifficultyId);
        if (difficultyIndex < 0) difficultyIndex = 0;
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

    void SelectMap() {
        var manager = GameManager.Instance;
        var map = GetSelectedMap(manager);
        if (manager == null || map == null) return;

        if (!manager.IsMapOwned(map) && !manager.TryPurchaseMap(map)) return;

        manager.SelectMap(map);
        MapDifficultyData difficulty = GetSelectedDifficulty(map);
        if (difficulty != null && manager.IsDifficultyUnlocked(map, difficultyIndex))
            manager.SelectDifficulty(map, difficulty.difficultyId);
        manager.SelectModifier(GetSelectedModifier(manager));
        ResolveInitialSelection();
        RefreshUI();
    }

    /// <summary>Returns whether the preview currently matches the owned map selected for the next shift.</summary>
    /// <returns>True when the previewed map is the active owned map.</returns>
    public bool IsPreviewApplied() {
        var manager = GameManager.Instance;
        if (manager == null) return false;
        var map = GetSelectedMap(manager);
        var modifier = GetSelectedModifier(manager);
        MapDifficultyData difficulty = GetSelectedDifficulty(map);
        bool difficultyApplied = difficulty == null || manager.CurrentDifficulty == difficulty ||
            (manager.CurrentDifficulty != null && difficulty != null && manager.CurrentDifficulty.difficultyId == difficulty.difficultyId);
        return map != null && manager.IsMapOwned(map) && manager.currentMap == map &&
            manager.CurrentModifier == modifier && difficultyApplied &&
            (difficulty == null || manager.IsDifficultyUnlocked(map, difficultyIndex));
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
        if (selectMapButton != null) {
            // The same action also applies a newly previewed modifier, so it must remain usable
            // when the map itself is already selected.
            MapDifficultyData selectedDifficulty = GetSelectedDifficulty(map);
            bool difficultyAvailable = selectedDifficulty == null || manager.IsDifficultyUnlocked(map, difficultyIndex);
            selectMapButton.interactable = (mapOwned && difficultyAvailable) || manager.CanPurchaseMap(map);
            var label = selectMapButton.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = LocalizationManager.Get(mapOwned ? selectMapKey : mapPurchaseKey);
        }

        RefreshDifficultyUI(manager, map);
        RefreshRuntimeMapCards(manager);

        var modifier = GetSelectedModifier(manager);
        if (modifierNameText != null) modifierNameText.text = modifier != null ? modifier.GetDisplayName() : LocalizationManager.Get(modifierNoneKey);
        if (modifierDescriptionText != null) modifierDescriptionText.text = modifier != null ? modifier.GetDescription() : LocalizationManager.Get(modifierNoneDescriptionKey);

        if (SelectionChanged != null) SelectionChanged.Invoke();
    }

    void SetMapControlsActive(bool active) {
        if (previousMapButton != null) previousMapButton.gameObject.SetActive(active && !runtimeLayoutBuilt);
        if (nextMapButton != null) nextMapButton.gameObject.SetActive(active && !runtimeLayoutBuilt);
        if (selectMapButton != null) selectMapButton.gameObject.SetActive(active);
        if (previousDifficultyButton != null) previousDifficultyButton.gameObject.SetActive(active);
        if (nextDifficultyButton != null) nextDifficultyButton.gameObject.SetActive(active);
        if (runtimeMapBrowser != null) runtimeMapBrowser.SetActive(active);
    }

    void BuildRuntimeLayout() {
        if (!buildRuntimeMapGrid || runtimeLayoutBuilt || mapNameText == null || selectMapButton == null) return;

        RectTransform detailBox = selectMapButton.transform.parent as RectTransform;
        if (detailBox == null) return;

        runtimeMapBrowser = CreateRuntimeMapBrowser();
        GridLayoutGroup grid = runtimeMapBrowser != null ? runtimeMapBrowser.GetComponentInChildren<GridLayoutGroup>() : null;
        runtimeMapContent = grid != null ? grid.transform as RectTransform : null;
        if (runtimeMapBrowser == null || runtimeMapContent == null) return;

        detailBox.anchoredPosition = detailPanelOffset;
        detailBox.sizeDelta = detailPanelSize;
        CreateRuntimeDetailFields(detailBox);
        runtimeLayoutBuilt = true;
    }

    GameObject CreateRuntimeMapBrowser() {
        GameObject browser = new GameObject("MapBrowserRuntime", typeof(RectTransform), typeof(Image));
        browser.transform.SetParent(transform, false);
        RectTransform browserRect = browser.GetComponent<RectTransform>();
        browserRect.anchorMin = new Vector2(0.5f, 0.5f);
        browserRect.anchorMax = new Vector2(0.5f, 0.5f);
        browserRect.pivot = new Vector2(0.5f, 0.5f);
        browserRect.anchoredPosition = mapGridOffset;
        browserRect.sizeDelta = mapGridSize;
        browser.GetComponent<Image>().color = runtimePanelColor;

        GameObject viewport = new GameObject("MapBrowserViewport", typeof(RectTransform), typeof(Image), typeof(Mask));
        viewport.transform.SetParent(browser.transform, false);
        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0f, 0f);
        viewportRect.anchorMax = new Vector2(1f, 1f);
        viewportRect.offsetMin = new Vector2(12f, 12f);
        viewportRect.offsetMax = new Vector2(-12f, -12f);
        viewport.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        viewport.GetComponent<Mask>().showMaskGraphic = false;

        GameObject content = new GameObject("MapBrowserContent", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(viewport.transform, false);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0f, 0f);
        GridLayoutGroup grid = content.GetComponent<GridLayoutGroup>();
        grid.cellSize = mapCardSize;
        grid.spacing = mapCardSpacing;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Mathf.Max(1, mapGridColumnCount);
        grid.childAlignment = TextAnchor.UpperCenter;
        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = browser.AddComponent<ScrollRect>();
        scroll.viewport = viewportRect;
        scroll.content = contentRect;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        return browser;
    }

    void CreateRuntimeDetailFields(RectTransform detailBox) {
        CreateRuntimeMapPreview(detailBox);
        SetRect(mapNameText, new Vector2(0f, 330f), new Vector2(500f, 34f));
        SetRect(mapDescriptionText, new Vector2(0f, 285f), new Vector2(500f, 42f));
        SetRect(mapStatusText, new Vector2(0f, 245f), new Vector2(500f, 28f));
        difficultyNameText = CreateRuntimeText(detailBox, mapNameText, new Vector2(0f, 25f), new Vector2(480f, 30f));
        difficultyDescriptionText = CreateRuntimeText(detailBox, mapDescriptionText, new Vector2(0f, -12f), new Vector2(480f, 42f));
        difficultyStatusText = CreateRuntimeText(detailBox, mapStatusText, new Vector2(0f, -60f), new Vector2(480f, 28f));
        difficultySummaryText = CreateRuntimeText(detailBox, mapDescriptionText, new Vector2(0f, -145f), new Vector2(480f, 100f));
        previousDifficultyButton = CreateRuntimeButton(detailBox, previousMapButton, new Vector2(-175f, -245f), "<");
        nextDifficultyButton = CreateRuntimeButton(detailBox, nextMapButton, new Vector2(175f, -245f), ">");

        RectTransform modifierNameRect = modifierNameText != null ? modifierNameText.transform as RectTransform : null;
        RectTransform modifierDescriptionRect = modifierDescriptionText != null ? modifierDescriptionText.transform as RectTransform : null;
        RectTransform previousModifierRect = previousModifierButton != null ? previousModifierButton.transform as RectTransform : null;
        RectTransform nextModifierRect = nextModifierButton != null ? nextModifierButton.transform as RectTransform : null;
        if (modifierNameRect != null) modifierNameRect.anchoredPosition = new Vector2(0f, -315f);
        if (modifierDescriptionRect != null) modifierDescriptionRect.anchoredPosition = new Vector2(0f, -350f);
        if (previousModifierRect != null) previousModifierRect.anchoredPosition = new Vector2(-155f, -400f);
        if (nextModifierRect != null) nextModifierRect.anchoredPosition = new Vector2(155f, -400f);
        if (selectMapButton != null) {
            RectTransform selectRect = selectMapButton.transform as RectTransform;
            selectRect.anchoredPosition = new Vector2(0f, -455f);
        }
    }

    void SetRect(TMP_Text text, Vector2 position, Vector2 size) {
        if (text == null) return;
        RectTransform rect = text.transform as RectTransform;
        if (rect == null) return;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    void CreateRuntimeMapPreview(RectTransform detailBox) {
        GameObject preview = new GameObject("SelectedMapPreview", typeof(RectTransform), typeof(Image));
        preview.transform.SetParent(detailBox, false);
        RectTransform rect = preview.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, 125f);
        rect.sizeDelta = new Vector2(300f, 170f);
        mapPreviewImage = preview.GetComponent<Image>();
        mapPreviewImage.preserveAspect = true;
    }

    TextMeshProUGUI CreateRuntimeText(RectTransform parent, TMP_Text template, Vector2 position, Vector2 size) {
        GameObject textObject = template != null ? Instantiate(template.gameObject, parent) : new GameObject("RuntimeText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        LocalizedText localizedText = textObject.GetComponent<LocalizedText>();
        if (localizedText != null) localizedText.enabled = false;
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        text.text = string.Empty;
        text.raycastTarget = false;
        return text;
    }

    Button CreateRuntimeButton(RectTransform parent, Button template, Vector2 position, string labelText) {
        GameObject buttonObject = template != null ? Instantiate(template.gameObject, parent) : new GameObject("RuntimeButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(72f, 34f);
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
        while (runtimeMapContent.childCount > manager.Content.Maps.Count)
            Destroy(runtimeMapContent.GetChild(runtimeMapContent.childCount - 1).gameObject);

        runtimeMapCardBackgrounds.Clear();
        runtimeMapCardLabels.Clear();
        for (int i = 0; i < runtimeMapContent.childCount; i++) {
            MapData map = manager.Content.Maps[i];
            Button button = runtimeMapContent.GetChild(i).GetComponent<Button>();
            Image background = button != null ? button.targetGraphic as Image : null;
            TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (background != null) {
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
                runtimeMapCardLabels.Add(label);
            }
            MapCardPreview preview = button != null ? button.GetComponent<MapCardPreview>() : null;
            if (preview != null) {
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
        bool canCycle = map != null && map.levelData != null && map.levelData.difficultyLevels != null && map.levelData.difficultyLevels.Count > 1;
        if (previousDifficultyButton != null) previousDifficultyButton.interactable = canCycle;
        if (nextDifficultyButton != null) nextDifficultyButton.interactable = canCycle;
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
