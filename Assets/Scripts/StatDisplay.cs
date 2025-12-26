using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class StatDisplay : MonoBehaviour {
    [Header("UI References")]
    public TextMeshProUGUI statNameText;
    public TextMeshProUGUI descriptionText;
    public TextMeshProUGUI levelText;
    public TextMeshProUGUI costText;
    public Button upgradeButton;

    // Updates the panel UI based on the passed parameters
    public void Setup(string name, string desc, int currentLvl, int maxLvl, int cost, bool isMaxed) {
        statNameText.text = name;
        descriptionText.text = desc;
        levelText.text = $"Lvl {currentLvl}/{maxLvl}";

        if (isMaxed) {
            costText.text = "MAX";
            costText.color = Color.red;
            // Disable button if maxed
            upgradeButton.interactable = false; 
        }
        else {
            costText.text = "$ " + cost;
            costText.color = Color.white;
            upgradeButton.interactable = true;
        }
    }
}