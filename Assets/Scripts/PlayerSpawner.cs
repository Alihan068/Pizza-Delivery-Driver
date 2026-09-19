using System;
using UnityEngine;
using Unity.Cinemachine;

public class PlayerSpawner : MonoBehaviour {
    [SerializeField] TrafficSessionHost sessionHost;
    public CinemachineCamera virtualCamera;

    /// <summary>Raised once, right after the player vehicle is instantiated and its camera is attached.</summary>
    public event Action<GameObject> PlayerSpawned;

    void Start() {
        if (GameManager.Instance != null && GameManager.Instance.currentVehicle != null) {
            if (sessionHost == null) sessionHost = SessionSceneRules.ResolveHost(gameObject.scene);
            if (sessionHost != null) sessionHost.PrepareSession(GameManager.Instance);
            else GameManager.Instance.PrepareSession(gameObject.scene);
            GameObject vehiclePrefab = GameManager.Instance.currentVehicle.vehiclePrefab;

            //Create Player Then attach Camera
            GameObject player = Instantiate(vehiclePrefab, transform.position, transform.rotation, transform);
            player.transform.SetParent(null, true);


            if (virtualCamera != null) {

                virtualCamera.Follow = player.transform;



            }
            else {
                Debug.LogError("'Virtual Camera' slotu is emtpy in spawnObject");
            }

            if (sessionHost != null) {
                PlayerSpawned += sessionHost.RegisterPlayer;
            }
            PlayerSpawned?.Invoke(player);
        }
        else {
            Debug.LogError("GameManager cant be found");
        }
    }

    void OnDestroy() {
        if (sessionHost != null) PlayerSpawned -= sessionHost.RegisterPlayer;
    }
}
