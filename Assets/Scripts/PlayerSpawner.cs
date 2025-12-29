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

                

            }
            else {
                Debug.LogError("'Virtual Camera' slotu is emtpy in spawnObject");
            }
        }
        else {
            Debug.LogError("GameManager cant be found");
        }
    }
}