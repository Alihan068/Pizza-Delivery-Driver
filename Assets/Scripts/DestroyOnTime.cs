using UnityEngine;

/// <summary>Destroys the attached object after its configured lifetime.</summary>
public class DestroyOnTime : MonoBehaviour {
    [SerializeField] int destroyAfterSeconds = 5;
    private void OnEnable() {
        Destroy(gameObject, destroyAfterSeconds);
    }
}
