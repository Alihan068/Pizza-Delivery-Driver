using UnityEngine;

public class DestroyOnTime : MonoBehaviour
{
    [SerializeField] int destroyAfterSeconds = 5;
    private void OnEnable() {
        Destroy(gameObject, destroyAfterSeconds);
    }
}
