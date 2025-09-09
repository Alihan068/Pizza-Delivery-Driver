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

    [SerializeField] float turboDuration = 5f;

    float turboBoost = 1.0f;
    bool turboMode;

    private IEnumerator Coroutine;

    Vector2 movementInput;
  
    void FixedUpdate() {
        float steerAmount = movementInput.x;
        float moveAmount = movementInput.y;

        transform.Rotate(0, 0, -steerAmount * turnSpeed * turboBoost * Time.deltaTime);
        transform.Translate(0, moveAmount * moveSpeed *(2 * turboBoost) * Time.deltaTime, 0);
    }

    void OnMove(InputValue value) {
        movementInput = value.Get<Vector2>();

    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Speedboost")) {
            moveSpeed += boostSpeed;
            Debug.Log("SpeedBuff Value = " + moveSpeed);
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Turnboost")) {
            turnSpeed += boostTurn;
            Debug.Log("TurnBuff Value = " + turnSpeed);
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Turboboost")) {
            StartCoroutine(TurboTimer());
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Debuff") && !turboMode) {
            if (moveSpeed < boostSpeed) {
                moveSpeed -= penaltySpeed;
                Debug.Log("SpeedBuff Value = " + moveSpeed);
            }
            if (turnSpeed < boostTurn) {
                turnSpeed -= penaltyTurn;
                Debug.Log("TurnBuff Value = " + turnSpeed);
            }
            Debug.Log("Debuffed!");
            Destroy(other.gameObject);
        }
    }

    IEnumerator TurboTimer() {
        Debug.Log("Turbo Mode Started");
        turboMode = true;
        turboBoost = 1.5f;
        yield return new WaitForSeconds(turboDuration);
        turboMode = false;
        turboBoost = 1;
        Debug.Log("Turbo Mode ");
        yield return null;
    }

    private void OnCollisionEnter2D(Collision2D other) {       
        if (!turboMode) {
            driverHealth -= 5 + (moveSpeed);
            moveSpeed = baseSpeed;
            turnSpeed = baseTurn;
        }
        
        if (driverHealth <= 0) {
            Debug.Log("You're dead");
            Destroy(gameObject);
        }
        Debug.Log("Crash! Health = " + driverHealth);

    }

}
