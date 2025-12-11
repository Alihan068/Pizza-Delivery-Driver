using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameUIManager : MonoBehaviour
{
    [SerializeField] TextMeshProUGUI scoreText;
    [SerializeField] TextMeshProUGUI pizzaCount;

    [SerializeField] Slider healthbar;
    [SerializeField] AudioClip gameOverClip;

    [SerializeField] TextMeshProUGUI speedText;
    [SerializeField] TextMeshProUGUI steeringText;

    [SerializeField] TextMeshProUGUI LeastTimeCustomerDistance;
    [SerializeField] TextMeshProUGUI LeastTİmeCustomerRemainingTime;

    AudioSource audioSource;

    ScoreHandler scoreHandler;

    private void Start() {
        audioSource = GetComponent<AudioSource>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        scoreText.text = "Score = 0";
        pizzaCount.text = "= 0";

        healthbar.value = healthbar.maxValue;
    }

    public void UpdateScoreText() {
        scoreText.text = "Score: = " + scoreHandler.score;
    }
    public void UpdatePizzaText(int deliveredPizza) {
        pizzaCount.text = "= " + deliveredPizza;
    }

    public void UpdateStatPanel(float hp, float speed, float steering) {
        healthbar.value = hp;
        steeringText.text = "Steering: \n" + (steering / 10);
        speedText.text = "Speed: \n" + speed;
    }

    public void PlayGameOverSound() {
        if (audioSource != null && gameOverClip != null) {
            audioSource.Stop();
            audioSource.PlayOneShot(gameOverClip);
        }
    }
}
