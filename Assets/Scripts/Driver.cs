using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class Driver : MonoBehaviour {

    [Header("Stats (From GameManager)")]
    [SerializeField] float currentHealth;
    [SerializeField] float moveSpeed;
    [SerializeField] float turnSpeed;
    [SerializeField] float armorPercent; // YENİ: Hasar azaltma oranı

    private float baseMoveSpeed;
    private float baseTurnSpeed;

    [Header("Settings")]
    [SerializeField] float baseCamSize = 6f;
    [SerializeField] float turboDuration = 5f;

    [Header("Visuals")]
    [SerializeField] Color32 crashColor = new Color32(255, 0, 0, 255);
    Color32 baseColor;
    SpriteRenderer spriteRenderer;

    // State
    float turboBoost = 1.0f;
    bool turboMode;
    Vector2 movementInput;

    // References
    Camera mainCam;
    Delivery delivery;
    GameUIManager gameUIManager;
    ScoreHandler scoreHandler;
    AudioSource audioSource;

    [Header("Audio & Effects")]
    [SerializeField] AudioClip[] crashSound;
    [SerializeField] GameObject wastedPizza;

    private void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        delivery = GetComponent<Delivery>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        mainCam = Camera.main;
        audioSource = GetComponent<AudioSource>();
        baseColor = spriteRenderer.color;

        InitializeStats();
        UpdateUIMethod();
    }

    void InitializeStats() {
        if (GameManager.Instance != null) {
            baseMoveSpeed = GameManager.Instance.GetSpeed();
            baseTurnSpeed = GameManager.Instance.GetTurn();
            currentHealth = GameManager.Instance.GetHealth();
            armorPercent = GameManager.Instance.GetArmor(); 
        }
        else {
            baseMoveSpeed = 10f;
            baseTurnSpeed = 150f;
            currentHealth = 100f;
            armorPercent = 0f;
        }

        moveSpeed = baseMoveSpeed;
        turnSpeed = baseTurnSpeed;
    }

    void FixedUpdate() {
        if (!gameObject.activeInHierarchy) return;

        float steerAmount = movementInput.x;
        float moveAmount = movementInput.y;

        transform.Rotate(0, 0, -steerAmount * turnSpeed * Time.deltaTime);
        transform.Translate(0, moveAmount * (moveSpeed * turboBoost) * Time.deltaTime, 0);
    }

    void OnMove(InputValue value) {
        movementInput = value.Get<Vector2>();
    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Speedboost")) {
            moveSpeed += 5f;
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Turboboost")) {
            StartCoroutine(TurboTimer());
            Destroy(other.gameObject);
        }
        UpdateUIMethod();
    }

    IEnumerator TurboTimer() {
        turboMode = true;
        turboBoost = 1.5f;
        yield return new WaitForSeconds(turboDuration);
        turboMode = false;
        turboBoost = 1f;
    }

    private void NormalizeColor() {
        spriteRenderer.color = baseColor;
    }

    private void OnCollisionEnter2D(Collision2D other) {
        if (other.gameObject.CompareTag("Border")) return;

        if (!turboMode) {

            float rawDamage = 5 + (moveSpeed * 0.5f);


            float finalDamage = rawDamage * (1.0f - armorPercent);

            currentHealth -= finalDamage;

            TryPlayAudioClipFromArray(crashSound);

            if (currentHealth <= 0) {
                gameUIManager.PlayGameOverSound();
                if (scoreHandler != null) scoreHandler.EndLevel(false);
                Destroy(gameObject);
                return;
            }

            if (delivery != null) {
                delivery.AttemptDropPizza(transform.position);
            }

            moveSpeed = baseMoveSpeed;
            turnSpeed = baseTurnSpeed;

            spriteRenderer.color = crashColor;
            Invoke(nameof(NormalizeColor), 0.5f);
            UpdateUIMethod();
        }
    }

    void UpdateUIMethod() {
        if (gameUIManager != null)
            gameUIManager.UpdateStatPanel(currentHealth, moveSpeed, turnSpeed);
        if (mainCam != null)
            mainCam.orthographicSize = baseCamSize + (moveSpeed / 10);
    }

    public void TryPlayAudioClipFromArray(AudioClip[] clips) {
        if (clips != null && clips.Length > 0 && audioSource != null) {
            audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)]);
        }
    }
    public void TryPlayAudioClip(AudioClip clip) {
        if (clip != null && audioSource != null) {
            audioSource.PlayOneShot(clip);
        }
    }
}