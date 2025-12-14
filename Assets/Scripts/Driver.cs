using System.Collections;
using Unity.VisualScripting;
using Unity.VisualScripting.FullSerializer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

public class Driver : MonoBehaviour {

    [Header("Driver Stats")]
    [SerializeField] float driverHealth = 100;

    [SerializeField] float baseTurn = 150f;
    float turnSpeed;

    [SerializeField] float baseSpeed = 5f;
    float moveSpeed;

    [SerializeField] float baseCamSize = 6f;

    [Header("Powerup / Debuff Values")]
    [SerializeField] float boostSpeed = 1f;
    [SerializeField] float boostTurn = 75f;

    [SerializeField] float penaltySpeed = 1.5f;
    [SerializeField] float penaltyTurn = 50f;

    [SerializeField] float turboDuration = 5f;

    [SerializeField] Color32 crashColor = new Color32(1, 1, 1, 255);

    Camera mainCam;

    SpriteRenderer spriteRenderer;
    Color32 baseColor;
    Color32 currentColor;

    float turboBoost = 1.0f;
    bool turboMode;

    private IEnumerator Coroutine;

    Vector2 movementInput;

    Delivery delivery;
    GameUIManager gameUIManager;
    ScoreHandler scoreHandler;

    [Header("Score Rewards/Penalties")]

    [SerializeField] int flatScorePerDelivery = 10;
    [SerializeField] int penaltyPerDebuff = -25;
    [SerializeField] int penaltyPerCrash = -50;
    [SerializeField] int extraPenaltyPerWastedPizza = -50;

    [Header("Audio")]

    AudioSource audioSource;

    [SerializeField] AudioClip[] crashSound;
    [SerializeField] AudioClip boostSound;

    [SerializeField] GameObject wastedPizza;


    private void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        delivery = GetComponent<Delivery>();    
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        mainCam = FindFirstObjectByType<Camera>();
        audioSource = GetComponent<AudioSource>();
        baseColor = spriteRenderer.color;

        turnSpeed = baseTurn;
        moveSpeed = baseSpeed;

        UpdateUIMethod();
    }
    void FixedUpdate() {
        if (!gameObject.activeInHierarchy) return;
            
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
            //mainCam.orthographicSize += 0.1f;
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
            scoreHandler.AddScore(penaltyPerDebuff);
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
        UpdateUIMethod();
    }

    IEnumerator TurboTimer() {
        Debug.Log("Turbo Mode Started");
        turboMode = true;
        turboBoost = 1.5f;
        yield return new WaitForSeconds(turboDuration);
        turboMode = false;
        turboBoost = 1;
        Debug.Log("Turbo Mode Finished");
        yield return null;
    }
    private void NormalizeColor() {
        spriteRenderer.color = baseColor;
        Debug.Log("NormalizeColor");

    }
    private void OnCollisionEnter2D(Collision2D other) {   
        if (other.gameObject.CompareTag("Border")) {
         return;
        }
        if (!turboMode) {
            driverHealth -= 5 + (moveSpeed);
            TryPlayAudioClipFromArray(crashSound);

            if (driverHealth <= 0) {
                Debug.Log("You're dead");
                gameUIManager.PlayGameOverSound();
                Destroy(gameObject);
            }

            if (delivery.carryPizzaAmount >= 1) {
                Instantiate(wastedPizza, transform.position, Quaternion.identity);
                scoreHandler.AddScore(extraPenaltyPerWastedPizza);
            }

            delivery.havePizzaStatus(false);

            
            scoreHandler.AddScore(penaltyPerCrash);
            moveSpeed = baseSpeed;
            turnSpeed = baseTurn;
            currentColor = spriteRenderer.color;
            spriteRenderer.color = crashColor;
            Invoke (nameof(NormalizeColor), 0.5f);
            UpdateUIMethod();

        }
        
        
        Debug.Log("Crash! Health = " + driverHealth);

    }

    void UpdateUIMethod() {
        gameUIManager.UpdateStatPanel(driverHealth, moveSpeed, turnSpeed);
        mainCam.orthographicSize = baseCamSize + (moveSpeed / 10);
    }

    public void TryPlayAudioClip(AudioClip clip) {
        if (clip != null && audioSource != null)
            audioSource.PlayOneShot(clip);
        else
            Debug.LogWarning("AudioClip is not assigned.");
    }
    public void TryPlayAudioClipFromArray(AudioClip[] clips) {
        if (clips != null && clips.Length > 0 && audioSource != null) {
            AudioClip clip = clips[Random.Range(0, clips.Length)];
            audioSource.PlayOneShot(clip);
        }
        else
            Debug.LogWarning("AudioClip array is not assigned or empty.");
    }


}
