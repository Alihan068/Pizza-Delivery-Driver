using UnityEngine;
using TMPro;
using UnityEngine.UI;

/// <summary>Presents an upgrade quote supplied by the garage; it does not buy or mutate upgrades.</summary>
public class StatDisplay : MonoBehaviour {
    [Header("Localization Keys")]
    [SerializeField] string levelKey = "stat.level";
    [SerializeField] string maxKey = "stat.max";
    [SerializeField] string costKey = "common.money";
    [Header("UI References")]
    public TextMeshProUGUI statNameText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI valueText;
    public TextMeshProUGUI costText;
    public Button upgradeButton;

    /// <summary>Refreshes one card using already translated title, description and value text.</summary>
    /// <param name="name">Translated stat title.</param>
    /// <param name="desc">Translated stat description.</param>
    /// <param name="currentLvl">Purchased level.</param>
    /// <param name="maxLvl">Authored level cap.</param>
    /// <param name="cost">Price of the next level.</param>
    /// <param name="isMaxed">Disables purchasing and shows the translated cap label when true.</param>
    /// <param name="valueDisplay">Localized current-to-next value comparison.</param>
    public void Setup(string name, string desc, int currentLvl, int maxLvl, int cost, bool isMaxed, string valueDisplay) {
        statNameText.text = name;
        descriptionText.text = desc;
        LocalizationManager.SetText(levelText, levelKey, currentLvl, maxLvl);
        if (valueText != null) valueText.text = valueDisplay;

        if (isMaxed) {
            costText.text = LocalizationManager.Get(maxKey);
            costText.color = Color.red;
            // Disable button if maxed
            upgradeButton.interactable = false;
        }
        else {
            LocalizationManager.SetText(costText, costKey, cost);
            costText.color = Color.white;
            upgradeButton.interactable = true;
        }
    }
}
