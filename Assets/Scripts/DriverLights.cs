using UnityEngine;

/// <summary>Controls the visual lights attached to a driver vehicle.</summary>
public class DriverLights : MonoBehaviour {
    [SerializeField] GameObject lightsParent;


    void Start() {
        SetLights(false);
    }
    void SetLights(bool state) {
        if (lightsParent != null) {
            lightsParent.SetActive(state);
        }
    }
    private void OnEnable() {
        SetLights(true);
    }
    private void OnDisable() {
        SetLights(false);
    }
}
