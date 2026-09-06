using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Full screen slot picker used for New Game, Load Game, and picking a destination to copy the
/// active career into. One reusable component driven by <see cref="SaveSlotSelectMode"/> rather
/// than separate screens, per the owner's decision that switching slots is just "go back to the
/// menu and Load Game" — no separate profile-switch flow.
/// </summary>
/// <remarks>
/// Cards are instantiated from <see cref="cardPrefab"/> up to <see cref="SaveSlotService.SlotCount"/>,
/// which is read from <see cref="GameConfig"/> at runtime — adding a fourth save slot later is an
/// Inspector change on that asset, not a change here or in the scene.
/// </remarks>
public class SaveSlotSelectPanel : MonoBehaviour {

    [Header("Layout")]
    [SerializeField] SaveSlotCardView cardPrefab;
    [SerializeField] Transform cardContainer;

    [Header("Header")]
    [SerializeField] TextMeshProUGUI titleText;
    [SerializeField] string newGameTitleKey = "panel.newGame.title";
    [SerializeField] string loadGameTitleKey = "panel.loadGame.title";
    [SerializeField] string copyTargetTitleKey = "panel.copyTarget.title";

    [Header("Navigation")]
    [SerializeField] Button backButton;

    [Header("Confirmation")]
    [Tooltip("Seconds the player must hold a card to overwrite it when starting a new career over one that already exists.")]
    [SerializeField] float overwriteHoldSeconds = 1.5f;
    [Tooltip("Seconds the player must hold the delete action to remove a career permanently.")]
    [SerializeField] float deleteHoldSeconds = 1.5f;

    readonly List<SaveSlotCardView> cards = new List<SaveSlotCardView>();
    SaveSlotSelectMode mode;

    void Awake() {
        if (backButton != null) backButton.onClick.AddListener(Close);
    }

    void OnEnable() { LocalizationManager.LanguageChanged += RefreshCards; }
    void OnDisable() { LocalizationManager.LanguageChanged -= RefreshCards; }

    /// <summary>Opens the panel in the given mode and populates every slot card from disk.</summary>
    /// <param name="requestedMode">Whether the player is starting a new career or loading one.</param>
    public void Open(SaveSlotSelectMode requestedMode) {
        mode = requestedMode;
        gameObject.SetActive(true);
        if (titleText != null) titleText.text = LocalizationManager.Get(GetTitleKey(mode));
        RefreshCards();
    }

    void Close() {
        gameObject.SetActive(false);
    }

    string GetTitleKey(SaveSlotSelectMode requestedMode) {
        switch (requestedMode) {
            case SaveSlotSelectMode.LoadGame: return loadGameTitleKey;
            case SaveSlotSelectMode.CopyTarget: return copyTargetTitleKey;
            default: return newGameTitleKey;
        }
    }

    void RefreshCards() {
        var manager = GameManager.Instance;
        if (manager == null || cardPrefab == null || cardContainer == null) return;

        EnsureCardCount(manager.Saves.SlotCount);

        // Copying hides the active slot's own card: it makes no sense as a destination for its own
        // career, and this way the card pool never needs a different slot-to-card mapping.
        bool hideActiveSlot = mode == SaveSlotSelectMode.CopyTarget;
        int visibleCount = 0;
        for (int i = 0; i < cards.Count; i++) {
            bool visible = !(hideActiveSlot && i == manager.ActiveSlot);
            cards[i].gameObject.SetActive(visible);
            if (!visible) continue;
            visibleCount++;

            var info = manager.Saves.Peek(i, manager.Content);
            cards[i].Populate(info, mode, overwriteHoldSeconds, deleteHoldSeconds);
        }
        ResizeCardContainer(visibleCount);
    }

    // Cards are laid out by CardContainer's own VerticalLayoutGroup with childControlHeight
    // false, so its total height has to be set explicitly rather than left to a
    // ContentSizeFitter: this runs the same frame the cards are populated, with no dependency on
    // a later layout pass, which matters because this project cannot rely on Unity's per-frame
    // layout rebuild queue draining predictably under the editor's MCP-driven Play Mode.
    void ResizeCardContainer(int visibleCount) {
        var containerRect = cardContainer as RectTransform;
        if (containerRect == null || visibleCount == 0) return;

        var cardRect = cardPrefab.GetComponent<RectTransform>();
        var cardLayoutElement = cardPrefab.GetComponent<LayoutElement>();
        float cardHeight = cardLayoutElement != null && cardLayoutElement.preferredHeight > 0f
            ? cardLayoutElement.preferredHeight
            : cardRect.sizeDelta.y;

        var containerLayout = cardContainer.GetComponent<VerticalLayoutGroup>();
        float spacing = containerLayout != null ? containerLayout.spacing : 0f;

        float totalHeight = visibleCount * cardHeight + Mathf.Max(0, visibleCount - 1) * spacing;
        containerRect.sizeDelta = new Vector2(containerRect.sizeDelta.x, totalHeight);
    }

    void EnsureCardCount(int count) {
        // Slot count does not change while the game is running (GameConfig is an authored asset,
        // not runtime state), so a pool sized once here never needs to shrink again.
        while (cards.Count < count) {
            var card = Instantiate(cardPrefab, cardContainer);
            card.CreateConfirmed += OnCreateConfirmed;
            card.LoadConfirmed += OnLoadConfirmed;
            card.OverwriteConfirmed += OnCreateConfirmed;
            card.DamagedResetConfirmed += OnDamagedResetConfirmed;
            card.DeleteConfirmed += OnDeleteConfirmed;
            cards.Add(card);
        }
        for (int i = 0; i < cards.Count; i++) cards[i].gameObject.SetActive(i < count);
    }

    void OnCreateConfirmed(int slotIndex) {
        if (mode == SaveSlotSelectMode.CopyTarget) {
            // Copy reads the active slot's file from disk, so the running career is flushed first —
            // otherwise a change made this session but not yet auto-saved would be copied stale.
            GameManager.Instance.SaveGame();
            GameManager.Instance.Saves.Copy(GameManager.Instance.ActiveSlot, slotIndex);
            RefreshCards();
            return;
        }
        GameManager.Instance.StartNewCareer(slotIndex);
        LoadGarage();
    }

    void OnLoadConfirmed(int slotIndex) {
        GameManager.Instance.LoadCareer(slotIndex);
        LoadGarage();
    }

    void OnDamagedResetConfirmed(int slotIndex) {
        GameManager.Instance.Saves.Delete(slotIndex);
        RefreshCards();
    }

    void OnDeleteConfirmed(int slotIndex) {
        GameManager.Instance.Saves.Delete(slotIndex);
        RefreshCards();
    }

    void LoadGarage() {
        var config = GameManager.Instance != null ? GameManager.Instance.Config : null;
        if (config == null) {
            Debug.LogError("No GameConfig is assigned, so the garage scene name is unknown.");
            return;
        }
        SceneManager.LoadScene(config.garageScene);
    }
}
