using UnityEngine;

public class CollisionDetector : MonoBehaviour
{
    
    private void OnCollisionEnter2D(Collision2D collision) {
        if (collision.gameObject.CompareTag("Obstacle")) {
            Debug.Log("Player has hit an Obstacle");
        
        }
    }
}
