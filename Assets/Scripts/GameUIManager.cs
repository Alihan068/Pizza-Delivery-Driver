using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] TextMeshProUGUI pizzaCount;

    [SerializeField] Slider healthbar;


    private void Start() {
        scoreText.text = "Score = 0";
        pizzaCount.text = "= 0";

        healthbar.value = healthbar.maxValue;
    }

    public void UpdateScoreText(int score, int deliveredPizza) {
        scoreText.text = "Score: = " + score;
    }
    public void UpdatePizzaText(int deliveredPizza) {
        pizzaCount.text = "= " + deliveredPizza;
    }

    public void UpdateDriverHpBar(float hp) {
        healthbar.value = hp;
    }
}
