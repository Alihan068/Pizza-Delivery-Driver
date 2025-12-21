using UnityEngine;
using Unity.Cinemachine;

public class PlayerSpawner : MonoBehaviour {

    
    public CinemachineCamera virtualCamera;

    void Start() {
        if (GameManager.Instance != null && GameManager.Instance.currentVehicle != null) {
            GameObject vehiclePrefab = GameManager.Instance.currentVehicle.vehiclePrefab;

            //Create Player Then attach Camera
            GameObject player = Instantiate(vehiclePrefab, transform.position, transform.rotation);

            
            if (virtualCamera != null) {
                
                virtualCamera.Follow = player.transform;

                

                Debug.Log("Kamera hedefi ayarlandı: " + player.name);
            }
            else {
                Debug.LogError("HATA: SpawnPoint objesindeki 'Virtual Camera' slotu boş!");
            }
        }
        else {
            Debug.LogError("GameManager veya Seçili Araç Bulunamadı!");
        }
    }
}