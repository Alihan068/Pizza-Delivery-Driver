using TMPro;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Displays a settled session and refreshes its presentation when the language changes.</summary>
public class SessionResultPanel : MonoBehaviour {
    [Header("Localization Keys")]
    [SerializeField] string deliveredKey = "result.delivered";
    [SerializeField] string missedKey = "result.missed";
    [SerializeField] string scoreKey = "result.score";
    [SerializeField] string rawScoreKey = "result.score.raw";
    [SerializeField] string scoreMultiplierKey = "result.score.multiplier";
    [SerializeField] string finalScoreKey = "result.score.final";
    [SerializeField] string modifiersKey = "result.modifiers";
    [SerializeField] string noModifiersKey = "result.modifiers.none";
    [SerializeField] string grossKey = "result.gross";
    [SerializeField] string penaltyKey = "result.penalty";
    [SerializeField] string bailKey = "result.bail";
    [SerializeField] string repairKey = "result.repair";
    [SerializeField] string partialRepairKey = "result.partialRepair";
    [SerializeField] string netKey = "result.net";
    [SerializeField] string walletKey = "result.wallet";
    [SerializeField] string timeUpKey = "result.timeUp";
    [SerializeField] string extractedKey = "result.extracted";
    [SerializeField] string wreckedKey = "result.wrecked";
    [SerializeField] string abandonedKey = "result.abandoned";
    [SerializeField] string interruptedKey = "result.interrupted";
    [SerializeField] string arrestedKey = "result.arrested";
    [SerializeField] string endedKey = "result.ended";
    [SerializeField] string savedKey = "result.saved";
    [SerializeField] string reputationKey = "result.reputation";
    [SerializeField] string rentPaidKey = "result.rent.paid";
    [SerializeField] string rentShortfallKey = "result.rent.shortfall";
    [SerializeField] string rentFreeKey = "result.rent.free";
    [SerializeField] string personalBestScoreKey = "result.personalBestScore";
    [SerializeField] string personalBestDeliveriesKey = "result.personalBestDeliveries";
    [SerializeField] string personalBestFreeplayKey = "result.personalBestFreeplay";
    [SerializeField] string perfectShiftKey = "result.perfectShift";
    [SerializeField] string finalChallengeKey = "ending.finalChallenge";
    [SerializeField] string finalCompleteKey = "ending.complete";
    [SerializeField] string finalCreditsKey = "ending.credits";
    [SerializeField] string replayKey = "result.replay";
    [SerializeField] string checkKey = "common.check";
    [SerializeField] string crossKey = "common.cross";
    [Header("Runtime Score Layout")]
    [SerializeField] float scoreBreakdownMinimumHeight = 120f;
    [SerializeField] float scoreBreakdownMinimumFontSize = 12f;
    SessionResult shownResult;
    int shownDelivered, shownMissed;
    bool hasResult;
    bool scoreBreakdownLayoutConfigured;

    void OnEnable() { LocalizationManager.LanguageChanged += RefreshText; }
    void OnDisable() { LocalizationManager.LanguageChanged -= RefreshText; }
    [Header("Header")]
    [SerializeField] TextMeshProUGUI reasonText;
    [SerializeField] TextMeshProUGUI deliveredText;
    [SerializeField] TextMeshProUGUI missedText;
    [SerializeField] TextMeshProUGUI scoreText;

    [Header("Earnings Breakdown")]
    [SerializeField] TextMeshProUGUI grossText;
    [SerializeField] TextMeshProUGUI deathPenaltyText;
    [SerializeField] TextMeshProUGUI repairText;
    [SerializeField] TextMeshProUGUI netText;
    [SerializeField] TextMeshProUGUI bankText;
    [SerializeField] TextMeshProUGUI savedText;

    [Header("Career")]
    [SerializeField] TextMeshProUGUI reputationText;
    [Tooltip("Shown only when this shift ended the day, reporting that day's rent settlement.")]
    [SerializeField] TextMeshProUGUI rentText;
    [SerializeField] TextMeshProUGUI recordText;
    [SerializeField] TextMeshProUGUI perfectShiftText;
    [SerializeField] TextMeshProUGUI finalCreditsText;

    [SerializeField] Button returnToGarageButton;
    [SerializeField] Button replayButton;
    [SerializeField] float replayButtonGap = 16f;

    string destinationScene;
    bool replayRequested;

    void Awake() {
        if (returnToGarageButton != null) returnToGarageButton.onClick.AddListener(OnClickReturnToGarage);
        ConfigureReplayButton();
    }

    /// <summary>Fills in the session breakdown and shows the panel.</summary>
    /// <param name="result">Settlement figures produced by the game manager.</param>
    /// <param name="deliveredCount">Pizzas delivered this session.</param>
    /// <param name="missedCount">Customers who timed out.</param>
    /// <param name="score">Legacy score argument retained for caller compatibility; ignored in favor of <paramref name="result"/>.finalScore.</param>
    /// <param name="destination">
    /// Scene the return button loads. Leave empty to use the garage from <see cref="GameConfig"/>.
    /// </param>
    public void Show(SessionResult result, int deliveredCount, int missedCount, int score, string destination = null) {
        _ = score;
        shownResult = result;
        shownDelivered = deliveredCount;
        shownMissed = missedCount;
        hasResult = true;
        replayRequested = false;
        gameObject.SetActive(true);
        destinationScene = !string.IsNullOrEmpty(destination) ? destination
            : (GameManager.Instance != null ? GameManager.Instance.Config.garageScene : null);

        RefreshText();
        if (replayButton != null && replayButton.interactable && UnityEngine.EventSystems.EventSystem.current != null)
            UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(replayButton.gameObject);
    }

    void RefreshText() {
        if (!hasResult) return;
        var result = shownResult;
        reasonText.text = GetReasonLabel(result.reason);
        LocalizationManager.SetText(deliveredText, deliveredKey, shownDelivered);
        LocalizationManager.SetText(missedText, missedKey, shownMissed);
        ConfigureScoreBreakdownLayout();
        scoreText.text = BuildScoreBreakdown(result);

        LocalizationManager.SetText(grossText, grossKey, result.grossEarnings);

        int deathPenalty = result.grossEarnings - result.keptEarnings;
        if (deathPenaltyText != null) {
            bool showPenalty = (result.reason == EndReason.Wrecked || result.reason == EndReason.Arrested) && deathPenalty > 0;
            deathPenaltyText.gameObject.SetActive(showPenalty);
            if (showPenalty) LocalizationManager.SetText(deathPenaltyText, result.reason == EndReason.Arrested ? bailKey : penaltyKey, deathPenalty);
        }

        int unpaid = result.repairBeforeClamp - result.repairCost;
        LocalizationManager.SetText(repairText, unpaid > 0 ? partialRepairKey : repairKey, result.repairCost, unpaid);

        int net = result.keptEarnings - result.repairCost;
        LocalizationManager.SetText(netText, netKey, net);

        LocalizationManager.SetText(bankText, walletKey, result.bankBefore, result.bankAfter);

        // SettleSession always writes to disk as part of closing the session, so this is a plain
        // confirmation rather than something conditional on a save result.
        if (savedText != null) savedText.text = LocalizationManager.Get(savedKey);

        if (reputationText != null) {
            string signedDelta = result.ratingDelta > 0 ? "+" + result.ratingDelta.ToString()
                : result.ratingDelta.ToString();
            reputationText.text = LocalizationManager.Get(reputationKey, signedDelta, result.rankAfter);
        }

        if (rentText != null) {
            rentText.gameObject.SetActive(result.dayEnded);
            if (result.dayEnded && result.rent != null) {
                if (result.rent.wasFree) {
                    LocalizationManager.SetText(rentText, rentFreeKey, result.dayNumber);
                }
                else if (result.rent.shortfall > 0) {
                    rentText.text = LocalizationManager.Get(rentShortfallKey, result.dayNumber, result.rent.rentDue, result.rent.shortfall);
                }
                else {
                    LocalizationManager.SetText(rentText, rentPaidKey, result.dayNumber, result.rent.rentDue);
                }
            }
        }

        if (recordText != null) {
            bool hasRecord = result.personalBestScore || result.personalBestDeliveries || result.personalBestFreeplayDeliveries;
            recordText.gameObject.SetActive(hasRecord);
            if (hasRecord) {
                string key = result.personalBestFreeplayDeliveries ? personalBestFreeplayKey :
                    (result.personalBestScore && result.personalBestDeliveries ? personalBestScoreKey : personalBestDeliveriesKey);
                recordText.text = LocalizationManager.Get(key);
            }
        }

        if (perfectShiftText != null) {
            perfectShiftText.gameObject.SetActive(!result.isFreeplay);
            if (!result.isFreeplay) {
                string check = LocalizationManager.Get(checkKey);
                string cross = LocalizationManager.Get(crossKey);
                perfectShiftText.text = LocalizationManager.Get(perfectShiftKey,
                    result.noMissedOrders ? check : cross,
                    result.noCollisionDamage ? check : cross,
                    result.noPizzasLost ? check : cross,
                    result.perfectShift ? check : cross);
            }
        }

        if (finalCreditsText != null) {
            finalCreditsText.gameObject.SetActive(result.isFinalShift || result.careerCompleted);
            if (result.isFinalShift || result.careerCompleted) {
                finalCreditsText.text = LocalizationManager.Get(
                    result.finalShiftSucceeded || result.careerCompleted ? finalCompleteKey : finalChallengeKey);
                if (result.careerCompleted) finalCreditsText.text += "\n" + LocalizationManager.Get(finalCreditsKey);
            }
        }

        if (replayButton != null) {
            replayButton.gameObject.SetActive(true);
            replayButton.interactable = !replayRequested && GameManager.Instance != null;
            TMP_Text label = replayButton.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = LocalizationManager.Get(replayKey);
        }
    }

    /// <summary>Returns the immutable final score that result presentation must display.</summary>
    /// <param name="result">Settled session result created by the authoritative settlement path.</param>
    /// <returns>The result's final score, never a mutable manager preview or legacy argument.</returns>
    public static int GetDisplayedFinalScore(SessionResult result) => result.finalScore;

    string BuildScoreBreakdown(SessionResult result) {
        var builder = new StringBuilder();
        builder.Append(LocalizationManager.Get(scoreKey, GetDisplayedFinalScore(result)));
        builder.Append('\n');
        builder.Append(LocalizationManager.Get(rawScoreKey, result.rawScore));
        builder.Append('\n');
        builder.Append(LocalizationManager.Get(scoreMultiplierKey, result.scoreMultiplier));
        builder.Append('\n');
        builder.Append(LocalizationManager.Get(finalScoreKey, result.finalScore));
        builder.Append('\n');
        builder.Append(LocalizationManager.Get(modifiersKey, BuildModifierNames(result)));
        return builder.ToString();
    }

    void ConfigureScoreBreakdownLayout() {
        if (scoreBreakdownLayoutConfigured || scoreText == null) return;
        scoreBreakdownLayoutConfigured = true;
        scoreText.enableWordWrapping = true;
        scoreText.enableAutoSizing = true;
        scoreText.fontSizeMin = Mathf.Max(1f, scoreBreakdownMinimumFontSize);
        scoreText.fontSizeMax = Mathf.Max(scoreText.fontSizeMin, scoreText.fontSize);
        scoreText.overflowMode = TextOverflowModes.Overflow;
        RectTransform rect = scoreText.rectTransform;
        if (rect != null) rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
            Mathf.Max(rect.rect.height, scoreBreakdownMinimumHeight));
    }

    string BuildModifierNames(SessionResult result) {
        if (result.appliedModifierIds == null || result.appliedModifierIds.Length == 0)
            return LocalizationManager.Get(noModifiersKey);
        var manager = GameManager.Instance;
        if (manager == null || manager.allModifiers == null) return LocalizationManager.Get(noModifiersKey);
        var names = new StringBuilder();
        for (int i = 0; i < result.appliedModifierIds.Length; i++) {
            string id = result.appliedModifierIds[i];
            for (int j = 0; j < manager.allModifiers.Length; j++) {
                ShiftModifierData modifier = manager.allModifiers[j];
                if (modifier == null || modifier.modifierId != id) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(modifier.GetDisplayName());
                break;
            }
        }
        return names.Length > 0 ? names.ToString() : LocalizationManager.Get(noModifiersKey);
    }

    string GetReasonLabel(EndReason reason) {
        switch (reason) {
            case EndReason.TimeUp: return LocalizationManager.Get(timeUpKey);
            case EndReason.Extracted: return LocalizationManager.Get(extractedKey);
            case EndReason.Wrecked: return LocalizationManager.Get(wreckedKey);
            case EndReason.Abandoned: return LocalizationManager.Get(abandonedKey);
            case EndReason.Interrupted: return LocalizationManager.Get(interruptedKey);
            case EndReason.Arrested: return LocalizationManager.Get(arrestedKey);
            default: return LocalizationManager.Get(endedKey);
        }
    }

    void OnClickReturnToGarage() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(destinationScene);
    }

    void ConfigureReplayButton() {
        if (replayButton == null && returnToGarageButton != null) {
            replayButton = Instantiate(returnToGarageButton, returnToGarageButton.transform.parent);
            replayButton.name = returnToGarageButton.name + "_Replay";
            replayButton.transform.SetSiblingIndex(returnToGarageButton.transform.GetSiblingIndex() + 1);
            replayButton.onClick = new Button.ButtonClickedEvent();
            var localizedLabel = replayButton.GetComponentInChildren<LocalizedText>(true);
            if (localizedLabel != null) localizedLabel.SetKey(replayKey);

            RectTransform parentRect = returnToGarageButton.transform.parent as RectTransform;
            if (parentRect != null && parentRect.GetComponent<LayoutGroup>() == null) {
                RectTransform replayRect = replayButton.transform as RectTransform;
                RectTransform returnRect = returnToGarageButton.transform as RectTransform;
                if (replayRect != null && returnRect != null) {
                    float gap = Mathf.Min(Mathf.Max(0f, replayButtonGap), returnRect.rect.width * 0.25f);
                    float width = (returnRect.rect.width - gap) * 0.5f;
                    Vector2 center = returnRect.anchoredPosition;
                    replayRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    returnRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    replayRect.anchoredPosition = center + Vector2.left * (width + gap) * 0.5f;
                    returnRect.anchoredPosition = center + Vector2.right * (width + gap) * 0.5f;
                }
            }

            Navigation replayNavigation = replayButton.navigation;
            Navigation returnNavigation = returnToGarageButton.navigation;
            replayNavigation.mode = Navigation.Mode.Explicit;
            returnNavigation.mode = Navigation.Mode.Explicit;
            replayNavigation.selectOnRight = returnToGarageButton;
            replayNavigation.selectOnDown = returnToGarageButton;
            returnNavigation.selectOnLeft = replayButton;
            returnNavigation.selectOnUp = replayButton;
            replayButton.navigation = replayNavigation;
            returnToGarageButton.navigation = returnNavigation;
        }
        if (replayButton != null) replayButton.onClick.AddListener(OnClickReplay);
        if (replayButton != null) replayButton.gameObject.SetActive(false);
    }

    void OnClickReplay() {
        if (replayRequested) return;
        GameManager manager = GameManager.Instance;
        if (manager == null) return;
        replayRequested = true;
        replayButton.interactable = false;
        if (!manager.ReplayLastSession()) {
            replayRequested = false;
            replayButton.interactable = true;
        }
    }
}
