using UnityEngine;

public class DriverLights : MonoBehaviour
{
    [SerializeField] GameObject lightsParent;


    void Start() {
        SetLights(false);
    }
    void SetLights(bool state)
    {
        if (lightsParent != null)
        {
            lightsParent.SetActive(state);
        }
    }
    private void OnEnable()
    {
        SetLights(true);
    }
    private void OnDisable()
    {
        SetLights(false);
    }
}
