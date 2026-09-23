using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-only presentation for the active police director. The view reads the director and
/// contact tracker as sources of truth; it never owns a timer, heat calculation, or pursuit state.
/// </summary>
public sealed class PoliceStatusView : MonoBehaviour {
    [Header("Localization Keys")]
    [SerializeField] string heatKey = "police.heat.raw";
    [SerializeField] string pursuitKey = "police.pursuit";
    [SerializeField] string captureKey = "police.capture";
    [SerializeField] string heatFilledKey = "police.heat.star";
    [SerializeField] string heatEmptyKey = "police.heat.empty";

    [Header("Runtime Layout")]
    [SerializeField] Vector2 panelSize = new Vector2(360f, 150f);
    [SerializeField] Vector2 panelOffset = new Vector2(-32f, -32f);
    [SerializeField] float heatDisplaySteps = 10f;
    [SerializeField] float captureDisplaySteps = 100f;

    PoliceDirector policeDirector;
    PoliceContactBridge contactBridge;
    PoliceStatusPresentationModel lastModel;
    bool hasModel;
    bool sessionActive;
    int cachedTierCount = -1;
    int cachedFilledTierCount = -1;
    string cachedHeatStars;

    TextMeshProUGUI heatText;
    TextMeshProUGUI heatValueText;
    TextMeshProUGUI pursuitText;
    TextMeshProUGUI captureText;
    Image captureFill;
    RectTransform captureFillRect;
    Sprite captureFillSprite;
    CanvasGroup canvasGroup;

    /// <summary>Presentation-only state derived from one director/contact snapshot.</summary>
    public readonly struct PoliceStatusPresentationModel : IEquatable<PoliceStatusPresentationModel> {
        /// <summary>Whether the police status panel is allowed to be visible.</summary>
        public readonly bool visible;
        /// <summary>Whether at least one living police unit is actively pursuing the player.</summary>
        public readonly bool pursuitActive;
        /// <summary>Number of committed living police units shown by the pursuit label.</summary>
        public readonly int activePoliceCount;
        /// <summary>Number of authored heat tiers, including tiers not currently reached.</summary>
        public readonly int heatTierCount;
        /// <summary>Number of authored heat tiers currently lit by the effective heat.</summary>
        public readonly int filledHeatTierCount;
        /// <summary>Effective director heat copied for presentation.</summary>
        public readonly float effectiveHeat;
        /// <summary>Whether a live stationary police contact should show capture progress.</summary>
        public readonly bool captureVisible;
        /// <summary>Clamped contact progress in the authored [0, 1] range.</summary>
        public readonly float captureProgress01;

        /// <summary>Creates an immutable status snapshot for UI and EditMode verification.</summary>
        public PoliceStatusPresentationModel(bool visible, bool pursuitActive, int activePoliceCount,
            int heatTierCount, int filledHeatTierCount, float effectiveHeat, bool captureVisible, float captureProgress01) {
            this.visible = visible;
            this.pursuitActive = pursuitActive;
            this.activePoliceCount = Mathf.Max(0, activePoliceCount);
            this.heatTierCount = Mathf.Max(0, heatTierCount);
            this.filledHeatTierCount = Mathf.Clamp(filledHeatTierCount, 0, this.heatTierCount);
            this.effectiveHeat = FiniteNonNegative(effectiveHeat) ? effectiveHeat : 0f;
            this.captureVisible = captureVisible;
            this.captureProgress01 = Mathf.Clamp01(FiniteNonNegative(captureProgress01) ? captureProgress01 : 0f);
        }

        /// <summary>Compares all display-relevant values so unchanged frames do not rebuild text.</summary>
        public bool Equals(PoliceStatusPresentationModel other) {
            return visible == other.visible && pursuitActive == other.pursuitActive && activePoliceCount == other.activePoliceCount &&
                heatTierCount == other.heatTierCount && filledHeatTierCount == other.filledHeatTierCount &&
                effectiveHeat == other.effectiveHeat && captureVisible == other.captureVisible &&
                captureProgress01 == other.captureProgress01;
        }

        /// <summary>Value equality required by the cached presentation update.</summary>
        public override bool Equals(object obj) => obj is PoliceStatusPresentationModel other && Equals(other);
        /// <summary>Returns a stable hash for the immutable presentation snapshot.</summary>
        public override int GetHashCode() => visible.GetHashCode() ^ pursuitActive.GetHashCode() ^ activePoliceCount.GetHashCode() ^
            heatTierCount.GetHashCode() ^ filledHeatTierCount.GetHashCode() ^ effectiveHeat.GetHashCode() ^
            captureVisible.GetHashCode() ^ captureProgress01.GetHashCode();
        /// <summary>Compares two presentation snapshots.</summary>
        public static bool operator ==(PoliceStatusPresentationModel left, PoliceStatusPresentationModel right) => left.Equals(right);
        /// <summary>Compares two presentation snapshots.</summary>
        public static bool operator !=(PoliceStatusPresentationModel left, PoliceStatusPresentationModel right) => !left.Equals(right);
    }

    void OnEnable() { LocalizationManager.LanguageChanged += RefreshLocalization; }
    void OnDisable() { LocalizationManager.LanguageChanged -= RefreshLocalization; }

    /// <summary>
    /// Builds a display snapshot without reading scene objects. The runtime's enabled state controls
    /// visibility, while active count alone controls pursuit wording; this keeps NoTraffic and
    /// Peaceful distinct without inventing a second police state in the UI.
    /// </summary>
    /// <param name="directorData">Authored profile whose tier list determines the star count.</param>
    /// <param name="policeRuntimeEnabled">Whether the session runtime can run police logic.</param>
    /// <param name="activePoliceCount">Committed living police count from the runtime.</param>
    /// <param name="effectiveHeat">Clamped heat from the runtime.</param>
    /// <param name="currentTierIndex">Zero-based current tier index, or -1 when no tier is reached.</param>
    /// <param name="holdSeconds">Contact tracker hold duration.</param>
    /// <param name="contactThreshold">Authored arrest hold threshold.</param>
    /// <param name="hasLiveContact">Whether the contact tracker currently has a live contact.</param>
    /// <returns>A deterministic presentation snapshot.</returns>
    public static PoliceStatusPresentationModel BuildModel(PoliceDirectorData directorData,
        bool policeRuntimeEnabled, int activePoliceCount, float effectiveHeat, int currentTierIndex,
        float holdSeconds, float contactThreshold, bool hasLiveContact, float heatDisplaySteps = 10f,
        float captureDisplaySteps = 100f) {
        int tierCount = directorData != null && directorData.heatTiers != null ? directorData.heatTiers.Count : 0;
        int filledTierCount = tierCount == 0 ? 0 : Mathf.Clamp(currentTierIndex + 1, 0, tierCount);
        bool visible = policeRuntimeEnabled && tierCount > 0;
        int safeActiveCount = Mathf.Max(0, activePoliceCount);
        bool pursuitActive = visible && safeActiveCount > 0;
        bool captureVisible = pursuitActive && hasLiveContact && FiniteNonNegative(contactThreshold) && contactThreshold > 0f;
        float progress = captureVisible ? Mathf.Clamp01(Mathf.Max(0f, holdSeconds) / contactThreshold) : 0f;
        return new PoliceStatusPresentationModel(visible, pursuitActive, safeActiveCount, tierCount, filledTierCount,
            Quantize(effectiveHeat, heatDisplaySteps), captureVisible, Quantize(progress, captureDisplaySteps));
    }

    /// <summary>Connects the runtime-only view to one validated scene host and police director.</summary>
    /// <param name="host">The active scene-local traffic session host.</param>
    /// <param name="director">The same-scene police director.</param>
    public void Configure(TrafficSessionHost host, PoliceDirector director) {
        policeDirector = director;
        sessionActive = host != null && host.IsActive;
        BindPlayer(host != null ? host.Player : null);
        EnsureRuntimeLayout();
        Refresh(true);
    }

    /// <summary>Binds the already registered player without performing a scene lookup.</summary>
    /// <param name="player">Player object that owns the contact bridge.</param>
    public void BindPlayer(GameObject player) {
        contactBridge = player != null ? player.GetComponent<PoliceContactBridge>() : null;
    }

    /// <summary>Updates the session lifecycle flag while retaining the cached typed dependencies.</summary>
    /// <param name="active">True while the traffic session is active.</param>
    public void SetSessionActive(bool active) {
        sessionActive = active;
        Refresh(true);
    }

    void Update() { Refresh(false); }

    void RefreshLocalization() {
        cachedHeatStars = null;
        hasModel = false;
        Refresh(true);
    }

    void Refresh(bool force) {
        PoliceDirectorRuntime runtime = policeDirector != null ? policeDirector.Runtime : null;
        PoliceDirectorData data = policeDirector != null ? policeDirector.DirectorData : null;
        int tierIndex = FindTierIndex(data, runtime != null ? runtime.CurrentTier : null);
        float holdSeconds = policeDirector != null && policeDirector.ContactTracker != null
            ? policeDirector.ContactTracker.HoldSeconds : 0f;
        bool hasLiveContact = policeDirector != null && policeDirector.ContactTracker != null &&
            policeDirector.ContactTracker.ContactCount > 0;
        float threshold = contactBridge != null ? contactBridge.ArrestHoldSeconds : 0f;
        PoliceStatusPresentationModel model = BuildModel(data, sessionActive && runtime != null && runtime.IsEnabled,
            runtime != null ? runtime.ActiveCount : 0, runtime != null ? runtime.EffectiveHeat : 0f,
            tierIndex, holdSeconds, threshold, hasLiveContact, heatDisplaySteps, captureDisplaySteps);
        PoliceStatusPresentationModel previous = lastModel;
        bool hadPrevious = hasModel;
        if (!force && hadPrevious && model == previous) return;
        hasModel = true;
        lastModel = model;
        bool visibilityChanged = !hadPrevious || model.visible != previous.visible;
        bool heatChanged = !hadPrevious || model.heatTierCount != previous.heatTierCount ||
            model.filledHeatTierCount != previous.filledHeatTierCount || model.effectiveHeat != previous.effectiveHeat;
        bool pursuitChanged = !hadPrevious || model.pursuitActive != previous.pursuitActive ||
            model.activePoliceCount != previous.activePoliceCount;
        bool captureChanged = !hadPrevious || model.captureVisible != previous.captureVisible ||
            model.captureProgress01 != previous.captureProgress01;
        if (visibilityChanged) SetPresentationVisible(model.visible);
        if (!model.visible) return;

        if (heatChanged && heatText != null) heatText.text = BuildHeatStars(model);
        if (heatChanged && heatValueText != null) LocalizationManager.SetText(heatValueText, heatKey, model.effectiveHeat);
        if (pursuitChanged && pursuitText != null) {
            pursuitText.gameObject.SetActive(model.pursuitActive);
            if (model.pursuitActive) LocalizationManager.SetText(pursuitText, pursuitKey, model.activePoliceCount);
        }
        if (captureChanged && captureText != null) {
            captureText.gameObject.SetActive(model.captureVisible);
            if (model.captureVisible) LocalizationManager.SetText(captureText, captureKey, model.captureProgress01 * 100f);
        }
        if (captureChanged && captureFillRect != null) {
            captureFillRect.anchorMax = new Vector2(Mathf.Lerp(0.8f, 0.96f, model.captureProgress01), 0.36f);
        }
        if (captureChanged && captureFill != null) {
            captureFill.gameObject.SetActive(model.captureVisible);
        }
    }

    void SetPresentationVisible(bool visible) {
        if (canvasGroup == null) return;
        canvasGroup.alpha = visible ? 1f : 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    string BuildHeatStars(PoliceStatusPresentationModel model) {
        if (cachedTierCount == model.heatTierCount && cachedFilledTierCount == model.filledHeatTierCount && cachedHeatStars != null)
            return cachedHeatStars;
        cachedTierCount = model.heatTierCount;
        cachedFilledTierCount = model.filledHeatTierCount;
        StringBuilder stars = new StringBuilder(Mathf.Max(0, model.heatTierCount));
        string filled = LocalizationManager.Get(heatFilledKey);
        string empty = LocalizationManager.Get(heatEmptyKey);
        for (int i = 0; i < model.heatTierCount; i++) stars.Append(i < model.filledHeatTierCount ? filled : empty);
        cachedHeatStars = stars.ToString();
        return cachedHeatStars;
    }

    void EnsureRuntimeLayout() {
        if (heatText != null) return;
        RectTransform root = gameObject.GetComponent<RectTransform>();
        if (root == null) root = gameObject.AddComponent<RectTransform>();
        root.anchorMin = Vector2.one;
        root.anchorMax = Vector2.one;
        root.pivot = Vector2.one;
        root.anchoredPosition = panelOffset;
        root.sizeDelta = panelSize;
        canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        Image panel = gameObject.GetComponent<Image>();
        if (panel == null) panel = gameObject.AddComponent<Image>();
        panel.color = new Color(0f, 0f, 0f, 0.48f);
        panel.raycastTarget = false;
        heatText = CreateText("HeatStars", new Vector2(0.04f, 0.66f), new Vector2(0.56f, 1f), 24f);
        heatValueText = CreateText("HeatValue", new Vector2(0.56f, 0.66f), new Vector2(0.96f, 1f), 20f);
        pursuitText = CreateText("Pursuit", new Vector2(0f, 0.36f), new Vector2(1f, 0.66f), 22f);
        captureText = CreateText("Capture", new Vector2(0f, 0.06f), new Vector2(0.78f, 0.36f), 20f);

        GameObject fillObject = new GameObject("CaptureFill", typeof(RectTransform), typeof(Image));
        fillObject.transform.SetParent(transform, false);
        captureFillRect = fillObject.GetComponent<RectTransform>();
        captureFillRect.anchorMin = new Vector2(0.8f, 0.06f);
        captureFillRect.anchorMax = new Vector2(0.8f, 0.36f);
        captureFillRect.offsetMin = Vector2.zero;
        captureFillRect.offsetMax = Vector2.zero;
        captureFill = fillObject.GetComponent<Image>();
        captureFillSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
        captureFill.sprite = captureFillSprite;
        captureFill.color = new Color(0.85f, 0.2f, 0.2f, 0.9f);
        captureFill.raycastTarget = false;
    }

    TextMeshProUGUI CreateText(string objectName, Vector2 anchorMin, Vector2 anchorMax, float size) {
        GameObject child = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        child.transform.SetParent(transform, false);
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = new Vector2(12f, 2f);
        rect.offsetMax = new Vector2(-12f, -2f);
        TextMeshProUGUI text = child.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    static int FindTierIndex(PoliceDirectorData data, PoliceHeatTier selected) {
        if (data == null || selected == null || data.heatTiers == null) return -1;
        for (int i = 0; i < data.heatTiers.Count; i++) {
            PoliceHeatTier authored = data.heatTiers[i];
            if (authored != null && authored.tierId == selected.tierId) return i;
        }
        return -1;
    }

    static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;

    static float Quantize(float value, float steps) {
        if (!FiniteNonNegative(value)) return 0f;
        if (!FiniteNonNegative(steps) || steps <= 0f) return value;
        return Mathf.Round(value * steps) / steps;
    }

    void OnDestroy() {
        if (captureFillSprite == null) return;
        if (Application.isPlaying) Destroy(captureFillSprite); else DestroyImmediate(captureFillSprite);
    }
}
