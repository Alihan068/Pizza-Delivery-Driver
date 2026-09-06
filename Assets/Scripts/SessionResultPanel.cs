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
    [SerializeField] string endedKey = "result.ended";
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
    }

    string GetReasonLabel(EndReason reason) {
        switch (reason) {
            case EndReason.TimeUp: return LocalizationManager.Get(timeUpKey);
            case EndReason.Extracted: return LocalizationManager.Get(extractedKey);
            case EndReason.Wrecked: return LocalizationManager.Get(wreckedKey);
            case EndReason.Abandoned: return LocalizationManager.Get(abandonedKey);
            default: return LocalizationManager.Get(endedKey);
        }
    }

    void OnClickReturnToGarage() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(destinationScene);
    }
}
