using UnityEngine;
using UnityEngine.UI;

public class ScoreHandler : MonoBehaviour
{
    GameUIManager gameUIManager;

    public int score = 0;
    public int money= 0;

    void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();
    }

    public void AddScore(int value)
    {
        score += value;
        gameUIManager.UpdateScoreText();
    }
    public void AddMoney(int value)
    {
        money += value;
        //gameUIManager.UpdateMoneyText();
    }

}
