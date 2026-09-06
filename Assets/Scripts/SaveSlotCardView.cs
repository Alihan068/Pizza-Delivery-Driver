using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One card on the save slot selection screen: shows a slot's state and turns player intent
/// (create, load, overwrite, delete, or reset) into events the owning panel reacts to.
/// </summary>
/// <remarks>
/// The card does not decide game state itself; <see cref="SaveSlotSelectPanel"/> supplies the slot
/// summary and the active mode through <see cref="Populate"/>, and this view only reports what the
/// player did with it.
/// </remarks>
public class SaveSlotCardView : MonoBehaviour {

    [Header("Primary Action")]
    [Tooltip("Kept alongside the hold component purely for its built-in disabled/hover visuals.")]
    [SerializeField] Button primaryButton;
    [SerializeField] HoldToConfirmButton primaryHold;
    [SerializeField] TextMeshProUGUI primaryActionText;

    [Header("Text")]
    [SerializeField] TextMeshProUGUI titleText;
    [SerializeField] TextMeshProUGUI subtitleText;
    [SerializeField] TextMeshProUGUI lastPlayedText;

    [Header("Badges")]
    [SerializeField] GameObject missingContentBadge;

    [Header("Delete")]
    [SerializeField] GameObject deleteRoot;
    [SerializeField] Button deleteButton;
    [SerializeField] HoldToConfirmButton deleteHold;
    [SerializeField] TextMeshProUGUI deleteActionText;

    [Header("Localization Keys")]
    [SerializeField] string emptyTitleKey = "slot.empty";
    [SerializeField] string emptyHintKey = "slot.emptyHint";
    [SerializeField] string occupiedSubtitleKey = "slot.occupied";
    [SerializeField] string lastPlayedKey = "slot.lastPlayed";
    [SerializeField] string damagedTitleKey = "slot.damaged";
    [SerializeField] string damagedHintKey = "slot.damagedHint";
    [SerializeField] string createActionKey = "slot.action.create";
    [SerializeField] string loadActionKey = "slot.action.load";
    [SerializeField] string overwriteActionKey = "slot.action.overwrite";
    [SerializeField] string resetActionKey = "slot.action.reset";
    [SerializeField] string deleteActionKey = "slot.action.deleteHold";
    [SerializeField] string copyHereActionKey = "slot.action.copyHere";

    enum PrimaryIntent { None, Create, Load, Overwrite, ResetDamaged }

    /// <summary>Slot index this card currently represents.</summary>
    public int SlotIndex { get; private set; }

    /// <summary>Raised when the player confirms creating a career in an empty slot.</summary>
    public event System.Action<int> CreateConfirmed;

    /// <summary>Raised when the player confirms loading an existing career.</summary>
    public event System.Action<int> LoadConfirmed;

    /// <summary>Raised when the player holds to overwrite an occupied slot.</summary>
    public event System.Action<int> OverwriteConfirmed;

    /// <summary>Raised when the player confirms clearing a damaged slot so it can be reused.</summary>
    public event System.Action<int> DamagedResetConfirmed;

    /// <summary>Raised when the player holds the delete action to completion.</summary>
    public event System.Action<int> DeleteConfirmed;

    PrimaryIntent primaryIntent = PrimaryIntent.None;
    bool wired;

    void Awake() {
        Wire();
    }

    void Wire() {
        if (wired) return;
        wired = true;
        if (primaryHold != null) primaryHold.Confirmed += OnPrimaryConfirmed;
        if (deleteHold != null) deleteHold.Confirmed += OnDeleteConfirmedInternal;
    }

    void OnPrimaryConfirmed() {
        switch (primaryIntent) {
            case PrimaryIntent.Create: CreateConfirmed?.Invoke(SlotIndex); break;
            case PrimaryIntent.Load: LoadConfirmed?.Invoke(SlotIndex); break;
            case PrimaryIntent.Overwrite: OverwriteConfirmed?.Invoke(SlotIndex); break;
            case PrimaryIntent.ResetDamaged: DamagedResetConfirmed?.Invoke(SlotIndex); break;
        }
    }

    void OnDeleteConfirmedInternal() {
        DeleteConfirmed?.Invoke(SlotIndex);
    }

    /// <summary>
    /// Fills in this card for one slot and configures how its primary and delete actions behave.
    /// </summary>
    /// <param name="info">Slot summary read from disk.</param>
    /// <param name="mode">Whether the screen is starting a new career or loading one.</param>
    /// <param name="overwriteHoldSeconds">Hold duration required to overwrite an occupied slot.</param>
    /// <param name="deleteHoldSeconds">Hold duration required to delete a slot.</param>
    public void Populate(SaveSlotInfo info, SaveSlotSelectMode mode, float overwriteHoldSeconds, float deleteHoldSeconds) {
        Wire();
        SlotIndex = info.slotIndex;

        bool isCopyMode = mode == SaveSlotSelectMode.CopyTarget;
        bool isDamaged = info.status == SaveLoadStatus.Corrupt;
        bool hasCareer = info.HasCareer;

        // The damaged state is conveyed by the title text itself ("Damaged"), so there is no
        // separate badge for it; the badge here is reserved for a save that is otherwise fine but
        // references content that is not installed.
        if (missingContentBadge != null) missingContentBadge.SetActive(hasCareer && info.hasMissingContent);

        // A damaged slot has nothing worth deleting separately from resetting it, so the delete
        // affordance is reserved for slots that actually hold a readable career. It is also hidden
        // while picking a copy destination: that screen is about placing a career, not removing one.
        bool canDelete = hasCareer && !isCopyMode;
        if (deleteRoot != null) deleteRoot.SetActive(canDelete);
        if (deleteButton != null) deleteButton.interactable = canDelete;
        if (deleteHold != null) deleteHold.SetHoldDuration(deleteHoldSeconds);
        if (deleteActionText != null) deleteActionText.text = LocalizationManager.Get(deleteActionKey);

        // Outside copy mode a damaged slot only offers clearing itself. As a copy destination it is
        // just an unreadable slot worth overwriting, so it falls through to the empty-slot branch.
        if (isDamaged && !isCopyMode) {
            SetTexts(LocalizationManager.Get(damagedTitleKey), LocalizationManager.Get(damagedHintKey), string.Empty);
            SetPrimary(PrimaryIntent.ResetDamaged, interactable: true, holdSeconds: 0f, actionKey: resetActionKey);
            return;
        }

        if (!hasCareer) {
            string title = isDamaged ? LocalizationManager.Get(damagedTitleKey) : LocalizationManager.Get(emptyTitleKey);
            string hint = isDamaged ? string.Empty : BuildEmptyHint();
            SetTexts(title, hint, string.Empty);
            // Creating on an empty slot is safe and instant, and so is copying into one. Loading one
            // is not a valid action.
            bool canAct = mode == SaveSlotSelectMode.NewGame || isCopyMode;
            SetPrimary(canAct ? PrimaryIntent.Create : PrimaryIntent.None, interactable: canAct, holdSeconds: 0f,
                actionKey: isCopyMode ? copyHereActionKey : createActionKey);
            return;
        }

        string lastPlayed = info.lastPlayedUtc == default
            ? string.Empty
            : LocalizationManager.Get(lastPlayedKey, FormatLastPlayed(info.lastPlayedUtc));
        SetTexts(info.vehicleDisplayName, LocalizationManager.Get(occupiedSubtitleKey, info.totalMoney), lastPlayed);

        // Loading an existing career is safe and instant. Overwriting one — from New Game or by
        // copying the active career onto it — is destructive and needs a deliberate hold; the panel
        // owns the duration so it stays one tunable value instead of being repeated per card.
        if (mode == SaveSlotSelectMode.NewGame || isCopyMode) SetPrimary(PrimaryIntent.Overwrite, interactable: true, holdSeconds: overwriteHoldSeconds, actionKey: overwriteActionKey);
        else SetPrimary(PrimaryIntent.Load, interactable: true, holdSeconds: 0f, actionKey: loadActionKey);
    }

    string BuildEmptyHint() {
        var manager = GameManager.Instance;
        var config = manager != null ? manager.Config : null;
        var registry = manager != null ? manager.Content : null;
        VehicleData startingVehicle = registry != null ? registry.ResolveStartingVehicle(config != null ? config.startingVehicle : null) : null;
        string vehicleName = startingVehicle != null ? startingVehicle.GetDisplayName() : string.Empty;
        int money = config != null ? config.startingMoney : 0;
        return LocalizationManager.Get(emptyHintKey, vehicleName, money);
    }

    void SetPrimary(PrimaryIntent intent, bool interactable, float holdSeconds, string actionKey) {
        primaryIntent = intent;
        if (primaryButton != null) primaryButton.interactable = interactable;
        if (primaryHold != null) primaryHold.SetHoldDuration(holdSeconds);
        if (primaryActionText != null) primaryActionText.text = intent == PrimaryIntent.None ? string.Empty : LocalizationManager.Get(actionKey);
    }

    void SetTexts(string title, string subtitle, string lastPlayed) {
        if (titleText != null) titleText.text = title;
        if (subtitleText != null) subtitleText.text = subtitle;
        if (lastPlayedText != null) {
            lastPlayedText.text = lastPlayed;
            lastPlayedText.gameObject.SetActive(!string.IsNullOrEmpty(lastPlayed));
        }
    }

    static string FormatLastPlayed(System.DateTime utc) {
        return utc.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture);
    }
}
