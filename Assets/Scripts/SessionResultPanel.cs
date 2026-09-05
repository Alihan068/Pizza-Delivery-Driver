using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SessionResultPanel : MonoBehaviour {
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

    string destinationScene = "GarageScene";

    void Awake() {
        if (returnToGarageButton != null) returnToGarageButton.onClick.AddListener(OnClickReturnToGarage);
    }

    public void Show(SessionResult result, int deliveredCount, int missedCount, int score, string destination = "GarageScene") {
        gameObject.SetActive(true);
        destinationScene = destination;

        reasonText.text = GetReasonLabel(result.reason);
        deliveredText.text = "Delivered: " + deliveredCount;
        missedText.text = "Missed: " + missedCount;
        scoreText.text = "Score: " + score;

        grossText.text = "Shift earnings: +$" + result.grossEarnings;

        int deathPenalty = result.grossEarnings - result.keptEarnings;
        if (deathPenaltyText != null) {
            bool showPenalty = result.reason == EndReason.Wrecked && deathPenalty > 0;
            deathPenaltyText.gameObject.SetActive(showPenalty);
            if (showPenalty) deathPenaltyText.text = "Death penalty: -$" + deathPenalty;
        }

        int unpaid = result.repairBeforeClamp - result.repairCost;
        repairText.text = unpaid > 0
            ? "Repairs: -$" + result.repairCost + " (partial, $" + unpaid + " unpaid)"
            : "Repairs: -$" + result.repairCost;

        int net = result.keptEarnings - result.repairCost;
        netText.text = "Net: $" + net;

        bankText.text = "Wallet: $" + result.bankBefore + " -> $" + result.bankAfter;
    }

    string GetReasonLabel(EndReason reason) {
        switch (reason) {
            case EndReason.TimeUp: return "Shift Complete";
            case EndReason.Extracted: return "Early Exit";
            case EndReason.Wrecked: return "Wrecked";
            case EndReason.Abandoned: return "Shift Abandoned";
            default: return "Shift Over";
        }
    }

    void OnClickReturnToGarage() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(destinationScene);
    }
}
