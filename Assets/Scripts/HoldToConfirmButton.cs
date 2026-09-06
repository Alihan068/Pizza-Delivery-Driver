using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Turns a UI element into a button that must be held to confirm, with an optional instant mode.
/// </summary>
/// <remarks>
/// Mirrors the hold-to-extract interaction on <see cref="ExtractionZone"/>, but driven by the
/// pointer rather than a keyboard key, and reusable anywhere a destructive action needs a
/// deliberate press: overwriting or deleting a save slot today, anything else later.
/// <para>
/// Runs on unscaled time so it keeps working while a paused screen has frozen
/// <see cref="Time.timeScale"/>.
/// </para>
/// <para>
/// Calling <see cref="SetHoldDuration"/> with zero turns this into an ordinary button that confirms
/// on release instead of requiring a hold, so one component serves both safe and destructive
/// actions depending on what is wired to it.
/// </para>
/// <para>
/// When a <see cref="Selectable"/> (for example a <see cref="Button"/>, kept alongside for its
/// built-in disabled visuals) sits on the same object, its interactable state also gates this
/// component. Unity delivers pointer events to every matching handler on a GameObject regardless
/// of a sibling Selectable's interactable flag, so that gate has to be applied here too.
/// </para>
/// </remarks>
public class HoldToConfirmButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler {

    [Tooltip("Seconds the pointer must stay down to confirm. Zero confirms immediately on release, like a normal button.")]
    [SerializeField] float holdDurationSeconds = 1.5f;

    [Tooltip("Optional fill visual, 0 to 1, showing hold progress. Left empty for instant buttons.")]
    [SerializeField] Image fillImage;

    Selectable selectable;
    bool pointerDown;
    float heldSeconds;

    /// <summary>Raised once the hold (or click, when the duration is zero) completes.</summary>
    public event System.Action Confirmed;

    void Awake() {
        selectable = GetComponent<Selectable>();
    }

    /// <summary>
    /// Sets the hold duration at runtime, so one card can switch between instant and destructive
    /// behavior as its slot's state changes.
    /// </summary>
    /// <param name="seconds">New hold duration; zero makes this an ordinary click-to-confirm button.</param>
    public void SetHoldDuration(float seconds) {
        holdDurationSeconds = Mathf.Max(0f, seconds);
        ResetPress();
    }

    void Update() {
        if (!pointerDown || holdDurationSeconds <= 0f) return;

        heldSeconds += Time.unscaledDeltaTime;
        UpdateFill();

        if (heldSeconds < holdDurationSeconds) return;
        ResetPress();
        Confirmed?.Invoke();
    }

    /// <summary>Starts tracking a press, unless a sibling Selectable marks this element non-interactable.</summary>
    /// <param name="eventData">Pointer event supplied by the UI system.</param>
    public void OnPointerDown(PointerEventData eventData) {
        if (selectable != null && !selectable.IsInteractable()) return;
        pointerDown = true;
        heldSeconds = 0f;
        UpdateFill();
    }

    /// <summary>Ends a press; confirms immediately when the hold duration is zero.</summary>
    /// <param name="eventData">Pointer event supplied by the UI system.</param>
    public void OnPointerUp(PointerEventData eventData) {
        bool wasInstantClick = pointerDown && holdDurationSeconds <= 0f;
        ResetPress();
        if (wasInstantClick) Confirmed?.Invoke();
    }

    /// <summary>Cancels a press that leaves the element before it completes, matching Button's own behavior.</summary>
    /// <param name="eventData">Pointer event supplied by the UI system.</param>
    public void OnPointerExit(PointerEventData eventData) {
        ResetPress();
    }

    void ResetPress() {
        pointerDown = false;
        heldSeconds = 0f;
        UpdateFill();
    }

    void UpdateFill() {
        if (fillImage == null) return;
        fillImage.fillAmount = holdDurationSeconds > 0f ? Mathf.Clamp01(heldSeconds / holdDurationSeconds) : 0f;
    }
}
