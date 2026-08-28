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
        deliveredText.text = "Teslimat: " + deliveredCount;
        missedText.text = "Kaçırılan: " + missedCount;
        scoreText.text = "Skor: " + score;

        grossText.text = "Vardiya kazancı: +$" + result.grossEarnings;

        int deathPenalty = result.grossEarnings - result.keptEarnings;
        if (deathPenaltyText != null) {
            bool showPenalty = result.reason == EndReason.Wrecked && deathPenalty > 0;
            deathPenaltyText.gameObject.SetActive(showPenalty);
            if (showPenalty) deathPenaltyText.text = "Ölüm cezası: -$" + deathPenalty;
        }

        int unpaid = result.repairBeforeClamp - result.repairCost;
        repairText.text = unpaid > 0
            ? "Tamir: -$" + result.repairCost + " (kısmi, $" + unpaid + " ödenmedi)"
            : "Tamir: -$" + result.repairCost;

        int net = result.keptEarnings - result.repairCost;
        netText.text = "Net: $" + net;

        bankText.text = "Cüzdan: $" + result.bankBefore + " -> $" + result.bankAfter;
    }

    string GetReasonLabel(EndReason reason) {
        switch (reason) {
            case EndReason.TimeUp: return "Vardiya Tamamlandı";
            case EndReason.Extracted: return "Erken Çıkış";
            case EndReason.Wrecked: return "Hurdaya Çıktın";
            case EndReason.Abandoned: return "Seans Terk Edildi";
            default: return "Vardiya Bitti";
        }
    }

    void OnClickReturnToGarage() {
        Time.timeScale = 1f;
        SceneManager.LoadScene(destinationScene);
    }
}
