using System.Collections;
using Unity.VisualScripting;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

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

    [SerializeField] Color32 crashColor = new Color32(1, 1, 1, 255);

    [SerializeField] float baseCamSpeed = 6f;
    Camera mainCam;

    SpriteRenderer spriteRenderer;
    Color32 baseColor;
    Color32 currentColor;

    float turboBoost = 1.0f;
    bool turboMode;

    private IEnumerator Coroutine;

    Vector2 movementInput;

    private void Start() {
        spriteRenderer = GetComponent<SpriteRenderer>();
        baseColor = spriteRenderer.color;
        mainCam = FindFirstObjectByType<Camera>();
    }
    void FixedUpdate() {
        float steerAmount = movementInput.x;
        float moveAmount = movementInput.y;

        transform.Rotate(0, 0, -steerAmount * turnSpeed * turboBoost * Time.deltaTime);
        transform.Translate(0, moveAmount * moveSpeed *(2 * turboBoost) * Time.deltaTime, 0);
    }

    void OnMove(InputValue value) {
        movementInput = value.Get<Vector2>();
        //spriteRenderer.colorr = baseColor;

    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Speedboost")) {
            moveSpeed += boostSpeed;
            Debug.Log("SpeedBuff Value = " + moveSpeed);
            mainCam.orthographicSize += 0.1f;
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
    private void NormalizeColor() {
        spriteRenderer.color = currentColor;
        Debug.Log("NormalizeColor");

    }
    private void OnCollisionEnter2D(Collision2D other) {       
        if (!turboMode) {
            driverHealth -= 5 + (moveSpeed);
            moveSpeed = baseSpeed;
            turnSpeed = baseTurn;
            mainCam.orthographicSize = baseCamSpeed;
            currentColor = spriteRenderer.color;
            spriteRenderer.color = crashColor;
            Invoke (nameof(NormalizeColor), 1f);
            
        }
        
        if (driverHealth <= 0) {
            Debug.Log("You're dead");
            Destroy(gameObject);
        }
        Debug.Log("Crash! Health = " + driverHealth);

    }
    

}
