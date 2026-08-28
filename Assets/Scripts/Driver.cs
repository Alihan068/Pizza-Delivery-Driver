using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class Driver : MonoBehaviour {

    [Header("Stats (From GameManager)")]
    public float currentHealth;
    public float maxHealth;
    [SerializeField] float moveSpeed;
    [SerializeField] float turnSpeed;
    [SerializeField] float armorPercent;

    private float baseMoveSpeed;
    private float baseTurnSpeed;

    [Header("Settings")]
    [SerializeField] float turboDuration = 5f;

    [Header("Damage")]
    [SerializeField] float damageBase = 3f;
    [SerializeField] float damageFactor = 0.85f;
    [SerializeField] float damageExponent = 2f;
    [SerializeField] float invulnerabilityWindow = 0.7f;
    float lastDamageTime = -999f;

    [Header("Speedboost")]
    [SerializeField] float speedBoostAmount = 5f;
    [SerializeField] float speedBoostMaxMult = 1.5f;

    [Header("Visuals")]
    [SerializeField] Color32 crashColor = new Color32(255, 0, 0, 255);
    [SerializeField] Color32 protectionColor = new Color32(80, 200, 255, 255);
    Color32 baseColor;
    SpriteRenderer spriteRenderer;
    CinemachineImpulseSource impulseSource;

    // State
    float turboBoost = 1.0f;
    bool isDisabled;
    Vector2 movementInput;

    // References
    Rigidbody2D rb;
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
        rb = GetComponent<Rigidbody2D>();
        delivery = GetComponent<Delivery>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        audioSource = GetComponent<AudioSource>();
        impulseSource = GetComponent<CinemachineImpulseSource>();
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

        maxHealth = currentHealth;
        moveSpeed = baseMoveSpeed;
        turnSpeed = baseTurnSpeed;
    }

    void FixedUpdate() {
        if (!gameObject.activeInHierarchy) return;

        if (isDisabled) {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        float steerAmount = movementInput.x;
        float moveAmount = movementInput.y;

        rb.MoveRotation(rb.rotation - steerAmount * turnSpeed * Time.fixedDeltaTime);
        rb.linearVelocity = (Vector2)transform.up * (moveAmount * moveSpeed * turboBoost);
    }

    void OnMove(InputValue value) {
        movementInput = value.Get<Vector2>();
    }

    private void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Speedboost")) {
            moveSpeed = Mathf.Min(moveSpeed + speedBoostAmount, baseMoveSpeed * speedBoostMaxMult);
            Destroy(other.gameObject);
        }
        else if (other.CompareTag("Turboboost")) {
            StartCoroutine(TurboTimer());
            Destroy(other.gameObject);
        }
        UpdateUIMethod();
    }

    IEnumerator TurboTimer() {
        turboBoost = 1.5f;
        yield return new WaitForSeconds(turboDuration);
        turboBoost = 1f;
    }

    private void NormalizeColor() {
        spriteRenderer.color = baseColor;
    }

    private void OnCollisionEnter2D(Collision2D other) {
        if (isDisabled) return;
        if (other.gameObject.CompareTag("Border")) return;
        if (Time.time - lastDamageTime < invulnerabilityWindow) return;
        lastDamageTime = Time.time;

        ApplyCollisionDamage();
    }

    void ApplyCollisionDamage() {
        float impactSpeed = rb.linearVelocity.magnitude;
        float rawDamage = damageBase + damageFactor * Mathf.Pow(impactSpeed, damageExponent);
        float finalDamage = rawDamage * (1f - armorPercent);
        float severity = Mathf.Clamp01(impactSpeed / (baseMoveSpeed * speedBoostMaxMult));

        currentHealth -= finalDamage;

        TryPlayAudioClipFromArray(crashSound, Mathf.Lerp(0.5f, 1f, severity));

        if (currentHealth <= 0) {
            HandleDeath();
            return;
        }

        if (delivery != null) {
            delivery.AttemptDropPizza(transform.position);
        }

        moveSpeed = baseMoveSpeed;
        turnSpeed = baseTurnSpeed;

        PlayCrashFlash();
        if (impulseSource != null) impulseSource.GenerateImpulseWithForce(severity);
        if (gameUIManager != null) gameUIManager.FlashHealthBar();
        UpdateUIMethod();
    }

    void PlayCrashFlash() {
        spriteRenderer.color = crashColor;
        Invoke(nameof(NormalizeColor), 0.5f);
    }

    public void PlayProtectionFlash() {
        spriteRenderer.color = protectionColor;
        Invoke(nameof(NormalizeColor), 0.3f);
    }

    void HandleDeath() {
        currentHealth = 0;
        isDisabled = true;
        movementInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;

        var playerInput = GetComponent<PlayerInput>();
        if (playerInput != null) playerInput.enabled = false;

        if (gameUIManager != null) gameUIManager.PlayGameOverSound();
        UpdateUIMethod();
        if (scoreHandler != null) scoreHandler.EndLevel(EndReason.Wrecked);
    }

    void UpdateUIMethod() {
        if (gameUIManager != null)
            gameUIManager.UpdateStatPanel(currentHealth, maxHealth, moveSpeed, turnSpeed);
    }

    public void TryPlayAudioClipFromArray(AudioClip[] clips, float volume = 1f) {
        if (clips != null && clips.Length > 0 && audioSource != null) {
            audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
        }
    }
    public void TryPlayAudioClip(AudioClip clip) {
        if (clip != null && audioSource != null) {
            audioSource.PlayOneShot(clip);
        }
    }
}
