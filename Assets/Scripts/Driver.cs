using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owns vehicle health, collision consequences and session death state.
/// </summary>
/// <remarks>
/// Rigidbody2D movement belongs to <see cref="VehicleMovement"/>. Keeping damage and delivery
/// consequences here preserves the existing gameplay contract while preventing two components from
/// writing velocity or rotation in the same physics step.
/// </remarks>
[RequireComponent(typeof(Rigidbody2D))]
public class Driver : MonoBehaviour {

    [Header("Stats (From GameManager)")]
    public float currentHealth;
    public float maxHealth;
    [SerializeField] float moveSpeed;
    [SerializeField] float turnSpeed;
    [SerializeField] float armorPercent;

    float baseMoveSpeed;
    float baseTurnSpeed;

    [Header("Damage")]
    [SerializeField] float damageBase = 3f;
    [SerializeField] float damageFactor = 0.85f;
    [SerializeField] float damageExponent = 2f;
    [SerializeField] float invulnerabilityWindow = 0.7f;
    float lastDamageTime = -999f;

    [Header("Blast Damage")]
    [Tooltip("Separate from invulnerabilityWindow (collision) — a blast never secretly benefits from the collision invulnerability window, and vice versa.")]
    [SerializeField] float blastCooldownSeconds = 1f;
    float lastBlastDamageTime = -999f;
    TrafficSessionCoordinator damageSession;

    [Header("Obstacle Hit")]
    [SerializeField] float obstacleDamage = 6f;
    [SerializeField] float obstacleSlowMult = 0.6f;
    [SerializeField] float obstacleSlowDuration = 2f;
    Coroutine slowCoroutine;

    [Header("Visuals")]
    [SerializeField] Color32 crashColor = new Color32(255, 0, 0, 255);
    [SerializeField] Color32 protectionColor = new Color32(80, 200, 255, 255);
    Color32 baseColor;
    SpriteRenderer spriteRenderer;
    CinemachineImpulseSource impulseSource;

    bool isDisabled;

    Rigidbody2D rb;
    Delivery delivery;
    GameUIManager gameUIManager;
    ScoreHandler scoreHandler;
    AudioSource audioSource;
    VehicleInput vehicleInput;
    VehicleMovement vehicleMovement;
    VehicleDriftFeedback driftFeedback;

    [Header("Audio & Effects")]
    [SerializeField] AudioClip[] crashSound;
    [SerializeField] GameObject wastedPizza;

    /// <summary>Whether this driver has been permanently disabled by session end.</summary>
    public bool IsDisabled => isDisabled;

    /// <summary>The resolved base movement speed before a temporary obstacle debuff.</summary>
    public float BaseMoveSpeed => baseMoveSpeed;

    /// <summary>The resolved base turn rate supplied to the movement motor.</summary>
    public float BaseTurnSpeed => baseTurnSpeed;

    void Awake() {
        rb = GetComponent<Rigidbody2D>();
        vehicleInput = GetComponent<VehicleInput>();
        if (vehicleInput == null) vehicleInput = gameObject.AddComponent<VehicleInput>();

        vehicleMovement = GetComponent<VehicleMovement>();
        if (vehicleMovement == null) vehicleMovement = gameObject.AddComponent<VehicleMovement>();

        driftFeedback = GetComponent<VehicleDriftFeedback>();
        if (driftFeedback == null) driftFeedback = gameObject.AddComponent<VehicleDriftFeedback>();
    }

    void Start() {
        gameUIManager = FindFirstObjectByType<GameUIManager>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        delivery = GetComponent<Delivery>();
        scoreHandler = FindFirstObjectByType<ScoreHandler>();
        audioSource = GetComponent<AudioSource>();
        impulseSource = GetComponent<CinemachineImpulseSource>();
        if (spriteRenderer != null) baseColor = spriteRenderer.color;

        InitializeStats();
        VehicleDrivingSettings settings = GameManager.Instance != null
            ? GameManager.Instance.GetEffectiveShiftDrivingSettings()
            : null;
        vehicleMovement.Initialize(vehicleInput, rb, baseMoveSpeed, baseTurnSpeed, settings);
        UpdateUIMethod();
    }

    void InitializeStats() {
        if (GameManager.Instance != null) {
            baseMoveSpeed = GameManager.Instance.GetEffectiveShiftSpeed();
            baseTurnSpeed = GameManager.Instance.GetShiftStatValue(VehicleStatId.Turn);
            currentHealth = GameManager.Instance.GetShiftStatValue(VehicleStatId.Health);
            armorPercent = GameManager.Instance.GetShiftStatValue(VehicleStatId.Armor);

            VehicleData vehicle = GameManager.Instance.currentVehicle;
            int healthLevel = GameManager.Instance.GetLevel(VehicleStatId.Health);
            rb.mass = vehicle != null && vehicle.bodySettings != null ? vehicle.bodySettings.ResolveMass(healthLevel) : 1f;
        }
        else {
            baseMoveSpeed = 4f;
            baseTurnSpeed = 150f;
            currentHealth = 100f;
            armorPercent = 0f;
            rb.mass = 1f;
        }

        maxHealth = currentHealth;
        moveSpeed = baseMoveSpeed;
        turnSpeed = baseTurnSpeed;
    }

    void OnTriggerEnter2D(Collider2D other) {
        if (other.CompareTag("Debuff")) HandleObstacleHit();
    }

    void HandleObstacleHit() {
        if (isDisabled) return;
        if (Time.time - lastDamageTime < invulnerabilityWindow) return;
        lastDamageTime = Time.time;
        if (scoreHandler != null) scoreHandler.RegisterCollisionDamageEvent();

        float finalDamage = obstacleDamage * (1f - armorPercent);
        currentHealth -= finalDamage;

        if (currentHealth <= 0) {
            HandleDeath();
            return;
        }

        if (delivery != null) delivery.AttemptDropPizza(transform.position);

        ApplySpeedDebuff();
        PlayCrashFlash();
        if (gameUIManager != null) gameUIManager.FlashHealthBar();
        UpdateUIMethod();
    }

    void ApplySpeedDebuff() {
        if (slowCoroutine != null) StopCoroutine(slowCoroutine);
        slowCoroutine = StartCoroutine(SpeedDebuffRoutine());
    }

    IEnumerator SpeedDebuffRoutine() {
        float safeMultiplier = Mathf.Clamp(obstacleSlowMult, 0f, 1f);
        moveSpeed = baseMoveSpeed * safeMultiplier;
        vehicleMovement.SetSpeedMultiplier(safeMultiplier);
        UpdateUIMethod();
        yield return new WaitForSeconds(obstacleSlowDuration);
        moveSpeed = baseMoveSpeed;
        vehicleMovement.SetSpeedMultiplier(1f);
        UpdateUIMethod();
        slowCoroutine = null;
    }

    void NormalizeColor() {
        if (spriteRenderer != null) spriteRenderer.color = baseColor;
    }

    void OnCollisionEnter2D(Collision2D other) {
        if (isDisabled) return;
        if (other.gameObject.CompareTag("Border")) return;
        if (Time.time - lastDamageTime < invulnerabilityWindow) return;
        lastDamageTime = Time.time;
        if (scoreHandler != null) scoreHandler.RegisterCollisionDamageEvent();

        Vector2 relativeVelocity = other.relativeVelocity;
        float impactSpeed = 0f;
        if (other.contactCount > 0) {
            for (int i = 0; i < other.contactCount; i++) {
                ContactPoint2D contact = other.GetContact(i);
                impactSpeed = Mathf.Max(impactSpeed,
                    VehicleDrivingMath.CalculateClosingSpeed(relativeVelocity, contact.normal));
            }
        }
        else {
            impactSpeed = relativeVelocity.magnitude;
        }

        ApplyCollisionDamage(impactSpeed);
    }

    void ApplyCollisionDamage(float impactSpeed) {
        float rawDamage = damageBase + damageFactor * Mathf.Pow(impactSpeed, damageExponent);
        float finalDamage = rawDamage * (1f - armorPercent);
        float severity = baseMoveSpeed > 0.01f ? Mathf.Clamp01(impactSpeed / baseMoveSpeed) : 0f;

        currentHealth -= finalDamage;
        TryPlayAudioClipFromArray(crashSound, Mathf.Lerp(0.5f, 1f, severity));

        if (currentHealth <= 0) {
            HandleDeath();
            return;
        }

        if (delivery != null) delivery.AttemptDropPizza(transform.position);

        // A normal wall collision must not cancel an active obstacle debuff. The temporary effect
        // belongs to the obstacle hit and is restored by its own scaled timer.
        PlayCrashFlash();
        if (impulseSource != null) impulseSource.GenerateImpulseWithForce(severity);
        if (gameUIManager != null) gameUIManager.FlashHealthBar();
        UpdateUIMethod();
    }

    void PlayCrashFlash() {
        if (spriteRenderer == null) return;
        spriteRenderer.color = crashColor;
        CancelInvoke(nameof(NormalizeColor));
        Invoke(nameof(NormalizeColor), 0.5f);
    }

    /// <summary>
    /// Applies pre-resolved blast damage (already run through BlastDamageMath with this vehicle's own
    /// explosionResistance and the player's role multiplier baked in by the caller — collision armor
    /// is never applied here a second time). Gated by its own blastCooldownSeconds, entirely separate
    /// from the collision invulnerabilityWindow, so neither can be exploited to dodge the other.
    /// First-scope behavior only: HP/UI/HandleDeath — no collisionDamageEvents or pizza-loss are
    /// raised here, per contracts §5.
    /// </summary>
    /// <param name="amount">Final, already-resistance-adjusted damage to apply.</param>
    /// <returns>True when the blast was accepted (not blocked by cooldown/already-disabled).</returns>
    public bool ApplyBlastDamage(float amount) {
        if (isDisabled || float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f) return false;
        if (damageSession != null && !damageSession.IsActive) return false;
        if (scoreHandler != null && !scoreHandler.IsGameActive) return false;
        if (Time.time - lastBlastDamageTime < blastCooldownSeconds) return false;
        lastBlastDamageTime = Time.time;

        currentHealth -= amount;

        if (currentHealth <= 0) {
            HandleDeath();
            return true;
        }

        PlayCrashFlash();
        if (impulseSource != null) impulseSource.GenerateImpulseWithForce(0.5f);
        if (gameUIManager != null) gameUIManager.FlashHealthBar();
        UpdateUIMethod();
        return true;
    }

    /// <summary>Binds blast acceptance to the same scene session used by NPC damage receivers.</summary>
    public void BindDamageSession(TrafficSessionCoordinator session) {
        damageSession = session;
    }

    /// <summary>Shows protection feedback when a damaging event loses no pizza.</summary>
    public void PlayProtectionFlash() {
        if (spriteRenderer == null) return;
        spriteRenderer.color = protectionColor;
        CancelInvoke(nameof(NormalizeColor));
        Invoke(nameof(NormalizeColor), 0.3f);
    }

    void HandleDeath() {
        currentHealth = 0;
        isDisabled = true;
        if (vehicleMovement != null) vehicleMovement.StopMovement();
        if (driftFeedback != null) driftFeedback.StopFeedback();

        if (slowCoroutine != null) {
            StopCoroutine(slowCoroutine);
            slowCoroutine = null;
        }

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

    /// <summary>Plays a randomly selected clip from an authored impact array.</summary>
    /// <param name="clips">Candidate audio clips.</param>
    /// <param name="volume">One-shot volume multiplier.</param>
    public void TryPlayAudioClipFromArray(AudioClip[] clips, float volume = 1f) {
        if (clips != null && clips.Length > 0 && audioSource != null)
            audioSource.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
    }

    /// <summary>Plays one authored audio clip when both the clip and source are available.</summary>
    /// <param name="clip">Clip to play.</param>
    public void TryPlayAudioClip(AudioClip clip) {
        if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
    }
}
