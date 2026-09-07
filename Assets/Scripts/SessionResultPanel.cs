using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Displays a settled session and refreshes its presentation when the language changes.</summary>
public class SessionResultPanel : MonoBehaviour {
    [Header("Localization Keys")]
    [SerializeField] string deliveredKey = "result.delivered";
    [SerializeField] string missedKey = "result.missed";
    [SerializeField] string scoreKey = "result.score";
    [SerializeField] string grossKey = "result.gross";
    [SerializeField] string penaltyKey = "result.penalty";
    [SerializeField] string repairKey = "result.repair";
    [SerializeField] string partialRepairKey = "result.partialRepair";
    [SerializeField] string netKey = "result.net";
    [SerializeField] string walletKey = "result.wallet";
    [SerializeField] string timeUpKey = "result.timeUp";
    [SerializeField] string extractedKey = "result.extracted";
    [SerializeField] string wreckedKey = "result.wrecked";
    [SerializeField] string abandonedKey = "result.abandoned";
    [SerializeField] string interruptedKey = "result.interrupted";
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
    [SerializeField] string checkKey = "common.check";
    [SerializeField] string crossKey = "common.cross";
    SessionResult shownResult;
    int shownDelivered, shownMissed, shownScore;
    bool hasResult;

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

    string destinationScene;

    void Awake() {
        if (returnToGarageButton != null) returnToGarageButton.onClick.AddListener(OnClickReturnToGarage);
    }

    /// <summary>Fills in the session breakdown and shows the panel.</summary>
    /// <param name="result">Settlement figures produced by the game manager.</param>
    /// <param name="deliveredCount">Pizzas delivered this session.</param>
    /// <param name="missedCount">Customers who timed out.</param>
    /// <param name="score">Session score.</param>
    /// <param name="destination">
    /// Scene the return button loads. Leave empty to use the garage from <see cref="GameConfig"/>.
    /// </param>
    public void Show(SessionResult result, int deliveredCount, int missedCount, int score, string destination = null) {
        shownResult = result;
        shownDelivered = deliveredCount;
        shownMissed = missedCount;
        shownScore = score;
        hasResult = true;
        gameObject.SetActive(true);
        destinationScene = !string.IsNullOrEmpty(destination) ? destination
            : (GameManager.Instance != null ? GameManager.Instance.Config.garageScene : null);

        RefreshText();
    }

    void RefreshText() {
        if (!hasResult) return;
        var result = shownResult;
        reasonText.text = GetReasonLabel(result.reason);
        LocalizationManager.SetText(deliveredText, deliveredKey, shownDelivered);
        LocalizationManager.SetText(missedText, missedKey, shownMissed);
        LocalizationManager.SetText(scoreText, scoreKey, shownScore);

        LocalizationManager.SetText(grossText, grossKey, result.grossEarnings);

        int deathPenalty = result.grossEarnings - result.keptEarnings;
        if (deathPenaltyText != null) {
            bool showPenalty = result.reason == EndReason.Wrecked && deathPenalty > 0;
            deathPenaltyText.gameObject.SetActive(showPenalty);
            if (showPenalty) LocalizationManager.SetText(deathPenaltyText, penaltyKey, deathPenalty);
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
    }

    string GetReasonLabel(EndReason reason) {
        switch (reason) {
            case EndReason.TimeUp: return LocalizationManager.Get(timeUpKey);
            case EndReason.Extracted: return LocalizationManager.Get(extractedKey);
            case EndReason.Wrecked: return LocalizationManager.Get(wreckedKey);
            case EndReason.Abandoned: return LocalizationManager.Get(abandonedKey);
            case EndReason.Interrupted: return LocalizationManager.Get(interruptedKey);
            default: return LocalizationManager.Get(endedKey);
        }
    }

    void OnClickReturnToGarage() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(destinationScene);
    }
}
