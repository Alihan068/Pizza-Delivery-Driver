using System.Collections;
using Unity.VisualScripting;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.InputSystem;

public class Driver : MonoBehaviour {
    [SerializeField] float driverHealth = 100;
    [SerializeField] float turnSpeed = 150f;
    [SerializeField] float moveSpeed = 10f;

    [SerializeField] float baseSpeed = 5f;
    [SerializeField] float baseTurn = 150f;
    [SerializeField] float boostSpeed = 1f;
    [SerializeField] float boostTurn = 75f;
    [SerializeField] float penaltySpeed = 1.5f;
    [SerializeField] float penaltyTurn = 50f;
    int turboBoost = 1;
    bool turboMode;
    private IEnumerator coroutine;
    Vector2 movementInput;

    //void Start() { 
    //StartCoroutine()
    //}
    void FixedUpdate() {
        float steerAmount = movementInput.x;
        float moveAmount = movementInput.y;

        transform.Rotate(0, 0, -steerAmount * turnSpeed * turboBoost * Time.deltaTime);
        transform.Translate(0, moveAmount * moveSpeed * turboBoost * Time.deltaTime, 0);
    }

    void OnMove(InputValue value) {
        movementInput = value.Get<Vector2>();

    }


    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Speedboost")) {
            moveSpeed += boostSpeed;
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Turnboost")) {
            turnSpeed += boostTurn;
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Turboboost")) {
            turnSpeed += boostTurn;
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Debuff") && !turboMode) {
            if (moveSpeed < boostSpeed) {
                moveSpeed -= penaltySpeed;
            }
            if (turnSpeed < boostTurn) {
                turnSpeed -= penaltyTurn;
            }
            Debug.Log("Debuffed!");
            Destroy(other.gameObject);
        }
    }
    private void TurboMode() {

        
        turboMode = false;
    }

    private void OnCollisionEnter2D(Collision2D other) {
        moveSpeed = baseSpeed;
        turnSpeed = baseTurn;
        if (!turboMode) {
            driverHealth -= 5 + (moveSpeed);
        }
        
        if (driverHealth <= 0) {
            Debug.Log("You're dead");
            Destroy(gameObject);
        }
        Debug.Log("Crash! Health = " + driverHealth);

    }

}
