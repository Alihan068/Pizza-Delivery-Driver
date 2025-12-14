using UnityEngine;
using TMPro;

public class GarageUI : MonoBehaviour {
    public TextMeshProUGUI moneyText;
    public int upgradeCost = 100; 

    void Update() {
        moneyText.text = "Money: " + GameManager.Instance.totalMoney;
    }

    public void OnClickSpeedUpgrade() {
        //GameManager.Instance.addSpeed(upgradeCost);
        Debug.Log("Hız Yükseltildi! Yeni Seviye: " + GameManager.Instance.speedLevel);
    }

    public void OnClickPlayGame() {
        
        UnityEngine.SceneManagement.SceneManager.LoadScene("GameScene");
    }
}