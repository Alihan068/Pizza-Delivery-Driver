using TMPro;
using UnityEngine;

/// <summary>
/// Shows what leaving the pause menu through Garage or Main Menu actually costs, in place of the
/// quick-save button it replaced. Both exits abandon the session identically (the difference is
/// only which scene loads next), so one figure covers both.
/// </summary>
/// <remarks>
/// Repair here is estimated with <paramref name="died"/> false, matching how
/// <see cref="ScoreHandler.EndLevel"/> actually settles an abandoned session — leaving does not
/// bill the full health bar the way a wreck does.
/// </remarks>
public class PauseCostPanel : MonoBehaviour {

    [SerializeField] TextMeshProUGUI costText;

    [Header("Localization Keys")]
    [SerializeField] string unbankedKey = "pause.cost.unbanked";
    [SerializeField] string bankKey = "pause.cost.bank";
    [SerializeField] string healthKey = "pause.cost.health";
    [SerializeField] string exitCostKey = "pause.cost.exit";

    /// <summary>Recomputes and displays the current figures. Call each time the pause menu opens.</summary>
    public void Refresh() {
        if (costText == null) return;

        var manager = GameManager.Instance;
        var scoreHandler = FindFirstObjectByType<ScoreHandler>();
        var driver = FindFirstObjectByType<Driver>();
        if (manager == null || scoreHandler == null || driver == null) return;

        int unbanked = scoreHandler.sessionEarnings;
        int bank = manager.totalMoney;
        float health = driver.currentHealth;
        float maxHealth = driver.maxHealth;
        int repairCost = manager.CalculateRepairCost(health, maxHealth, died: false);

        costText.text = string.Join("\n",
            LocalizationManager.Get(unbankedKey, unbanked),
            LocalizationManager.Get(bankKey, bank),
            LocalizationManager.Get(healthKey, Mathf.RoundToInt(health), Mathf.RoundToInt(maxHealth)),
            LocalizationManager.Get(exitCostKey, repairCost, unbanked));
    }
}
