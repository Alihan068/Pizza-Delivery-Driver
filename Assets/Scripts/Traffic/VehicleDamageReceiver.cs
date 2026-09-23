using System.Collections.Generic;
using UnityEngine;

/// <summary>Maps one physical body to a session life and applies NPC damage or forwards player blasts.</summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class VehicleDamageReceiver : MonoBehaviour {
    readonly NpcVehicleHealth health = new NpcVehicleHealth();
    readonly VehicleContactTracker contacts = new VehicleContactTracker();
    TrafficDamageWorld world;
    Rigidbody2D body;
    Driver player;
    NpcVehicleMotor motor;
    NpcDamagePresentation presentation;
    NpcVehicleProfile profile;
    TrafficDamageProfile collisionTuning;
    TrafficDamageProfile explosionTuning;
    NpcVehicleInstance instance;
    NpcVehiclePool pool;
    VehiclePopulationService population;
    TrafficRespawnScheduler respawns;
    float armor, resistance, lastImpactTime, wreckAt, recoverAt;
    string deficitId;

    /// <summary>Current session-scoped identity; changes only on a new pool life.</summary>
    public VehicleIdentity Identity { get; private set; }
    /// <summary>Pre-solver velocity sample used independently of callback ordering.</summary>
    public ContactParticipant Sample { get; private set; }
    /// <summary>True only for a bound active session and a living vehicle.</summary>
    public bool CanTakeDamage => world != null && world.IsActive &&
        (player != null ? !player.IsDisabled : instance != null && health.CanReceiveDamage(Identity.lifeId) && instance.Identity.HasValue && instance.Identity.Value.lifeId == Identity.lifeId);

    /// <summary>
    /// Verifies that this receiver still owns the exact active session life expected by a caller.
    /// This is intentionally a read-only authority check: it neither restores health nor changes
    /// registration, and an optional NPC profile must be the exact profile bound to this life.
    /// </summary>
    /// <param name="owner">The active damage world expected to own this receiver.</param>
    /// <param name="expected">The exact role and life identifier expected by the caller.</param>
    /// <param name="expectedNpcProfile">Optional exact NPC profile; null is required for player bindings.</param>
    /// <returns>True only while the private world, active life, identity, role, and optional profile all match.</returns>
    public bool IsBoundTo(TrafficDamageWorld owner, VehicleIdentity expected, NpcVehicleProfile expectedNpcProfile = null) {
        if (owner == null || world != owner || !CanTakeDamage || expected.lifeId <= 0 ||
            Identity.lifeId != expected.lifeId || Identity.role != expected.role) return false;
        if (expectedNpcProfile == null) return player != null;
        return player == null && profile == expectedNpcProfile;
    }
    /// <summary>Runtime health for diagnostics and lifecycle tests.</summary>
    public float CurrentHealth => player != null ? player.currentHealth : health.CurrentHealth;
    /// <summary>Frozen explosion resistance for this life.</summary>
    public float ExplosionResistance => resistance;
    /// <summary>World-space body center used for blast distance and contact orientation.</summary>
    public Vector2 Position => body != null ? body.worldCenterOfMass : (Vector2)transform.position;
    /// <summary>Whether this body is a solid NPC wreck awaiting cleanup.</summary>
    public bool IsWreck => instance != null && instance.State == VehicleLifeState.Wreck;
    /// <summary>Raised once per distinct physical impact (after contact dedupe, before damage) with the closing speed, so a driving controller can react to touches too light to damage.</summary>
    public event System.Action<float> ImpactObserved;

    void CacheBody() {
        body = GetComponent<Rigidbody2D>();
        motor = GetComponent<NpcVehicleMotor>();
        presentation = GetComponent<NpcDamagePresentation>();
    }

    /// <summary>Registers the player under the coordinator's identity; never attaches NPC health behavior to Driver.</summary>
    public void BindPlayer(TrafficDamageWorld owner, Driver driver, VehicleIdentity identity, float blastResistance) {
        Unbind(); CacheBody();
        world = owner; player = driver; Identity = identity; resistance = Mathf.Clamp01(blastResistance);
        player.BindDamageSession(owner.Coordinator);
        owner.Register(this); CaptureBeforePhysics();
    }

    /// <summary>Binds an already-reserved, budgeted pool instance to its physical body; malformed profiles fail before registration.</summary>
    public bool BindNpc(TrafficDamageWorld owner, NpcVehicleInstance life, NpcVehicleProfile authoredProfile,
        NpcVehiclePool owningPool, VehiclePopulationService budget, TrafficRespawnScheduler scheduler = null) {
        if (owner == null || !owner.IsActive || life == null || !life.Identity.HasValue || life.State != VehicleLifeState.Active ||
            authoredProfile == null || owningPool == null || budget == null || authoredProfile.maxHealth <= 0f ||
            float.IsNaN(authoredProfile.maxHealth) || float.IsInfinity(authoredProfile.maxHealth) ||
            !owner.Settings.TryResolve(authoredProfile.damageProfileId, out var collision) ||
            !owner.Settings.TryResolve(authoredProfile.explosionProfileId, out var explosion) ||
            !health.IsSessionActive || life.Identity.Value.lifeId <= health.LifeId) return false;
        Unbind(); CacheBody();
        world = owner; instance = life; profile = authoredProfile; pool = owningPool; population = budget; respawns = scheduler;
        Identity = life.Identity.Value; armor = Mathf.Clamp01(profile.collisionArmor); resistance = Mathf.Clamp01(profile.explosionResistance);
        collisionTuning = collision; explosionTuning = explosion;
        lastImpactTime = float.NegativeInfinity; wreckAt = float.PositiveInfinity; recoverAt = float.PositiveInfinity;
        deficitId = Identity.lifeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        health.InitializeForNewLife(profile.maxHealth, Identity.lifeId);
        if (motor != null) { motor.ResetForNewLife(); motor.Configure(profile.motorSettings); motor.SetMass(profile.baseMass); }
        if (presentation != null) presentation.ResetForLife();
        owner.Register(this); CaptureBeforePhysics();
        return true;
    }

    /// <summary>Captures linear/angular contact-point inputs before Box2D resolves this step.</summary>
    public void CaptureBeforePhysics() {
        if (body == null) return;
        InstigatorKind kind = Identity.role == VehicleRole.Player ? InstigatorKind.Player :
            Identity.role == VehicleRole.Police ? InstigatorKind.Police : InstigatorKind.Civilian;
        Sample = new ContactParticipant(Identity.lifeId, kind, body.linearVelocity, body.angularVelocity * Mathf.Deg2Rad, body.worldCenterOfMass);
    }

    /// <summary>Clears only transient collider-pair history before same-life relocation.</summary>
    public void ClearContactHistoryForRelocation() {
        contacts.Clear();
    }

    void OnCollisionEnter2D(Collision2D collision) {
        if (!CanTakeDamage || player != null || collision.collider == null) return;
        var otherCollider = collision.collider;
        int otherBodyId = otherCollider.attachedRigidbody != null ? otherCollider.attachedRigidbody.GetInstanceID() : otherCollider.GetInstanceID();
        if (!contacts.TryEnter(collision.otherCollider.GetInstanceID(), otherCollider.GetInstanceID(), otherBodyId)) return;
        var other = otherCollider.GetComponentInParent<VehicleDamageReceiver>();
        ContactParticipant otherSample = other != null ? other.Sample : new ContactParticipant(0, InstigatorKind.Environment, Vector2.zero, 0f, otherCollider.bounds.center, true);
        float greatestImpact = 0f;
        Vector2 point = Position, normal = Vector2.zero;
        for (int i = 0; i < collision.contactCount; i++) {
            var contact = collision.GetContact(i);
            Vector2 n = contact.normal;
            if (Vector2.Dot(n, otherSample.centerOfMass - Sample.centerOfMass) < 0f) n = -n;
            Vector2 selfVelocity = PointVelocity(Sample, contact.point);
            Vector2 otherVelocity = PointVelocity(otherSample, contact.point);
            float impact = VehicleDrivingMath.CalculateClosingSpeed(selfVelocity - otherVelocity, n);
            if (impact > greatestImpact) { greatestImpact = impact; point = contact.point; normal = n; }
        }
        ImpactObserved?.Invoke(greatestImpact);
        world.ApplyCollision(this, otherSample, point, normal, greatestImpact);
    }

    void OnCollisionExit2D(Collision2D collision) {
        if (collision.collider != null && collision.otherCollider != null)
            contacts.Exit(collision.otherCollider.GetInstanceID(), collision.collider.GetInstanceID());
    }

    static Vector2 PointVelocity(ContactParticipant participant, Vector2 point) {
        Vector2 offset = point - participant.centerOfMass;
        return participant.linearVelocity + participant.angularVelocityRadPerSec * new Vector2(-offset.y, offset.x);
    }

    /// <summary>Accepts one distinct impact after contact dedupe; cooldown is per life, never per collider.</summary>
    public void ApplyCollision(float impactSpeed, DamageContext context) {
        if (!CanTakeDamage || player != null) return;
        if (!NpcCollisionDamage.TryCalculateDamage(collisionTuning.collision, impactSpeed, armor, lastImpactTime, world.SessionTime, out float amount) || amount <= 0f) return;
        lastImpactTime = world.SessionTime;
        ApplyNpcDamage(amount, context);
        if (!health.IsDestroyed && instance.TryEnterCrashRecovery()) {
            float recoverySeconds = Identity.role == VehicleRole.Police
                ? Mathf.Min(world.Settings.recoverySeconds, 0.35f) : world.Settings.recoverySeconds;
            recoverAt = world.SessionTime + recoverySeconds;
            if (motor != null) motor.SetCrashMode(true);
        }
    }

    /// <summary>Rejects stale queued victim IDs and ended sessions immediately before applying a blast.</summary>
    public bool ApplyBlast(BlastApplication application) {
        if (!CanTakeDamage || application.victimLifeId != Identity.lifeId || application.damage <= 0f ||
            float.IsNaN(application.damage) || float.IsInfinity(application.damage)) return false;
        if (player != null) return player.ApplyBlastDamage(application.damage);
        ApplyNpcDamage(application.damage, application.context);
        return true;
    }

    void ApplyNpcDamage(float amount, DamageContext context) {
        bool killed = health.ApplyDamage(Identity.lifeId, amount, context, Identity.role, world.SessionTime, out var death);
        if (killed && instance.TryMarkWrecked()) {
            population.MarkActiveVehicleWrecked(Identity.role);
            wreckAt = world.SessionTime;
            if (motor != null) motor.StopMovement();
            respawns?.TryRegisterDeficit(deficitId, wreckAt, Position);
            world.PublishDeath(this, death, explosionTuning);
        }
        if (presentation != null) presentation.Show(NpcDamageFeedbackResolver.Resolve(health.CurrentHealth, health.MaxHealth, profile), health.IsDestroyed);
    }

    /// <summary>Advances recovery and wreck cleanup with active session time; no wall-clock timers are used.</summary>
    public void Tick() {
        if (world == null || !world.IsActive || player != null || instance == null) return;
        if (IsWreck && world.SessionTime - wreckAt >= world.Settings.wreckLifetimeSeconds) { ReleaseWreck(); return; }
        if (instance.State == VehicleLifeState.CrashRecovery && world.SessionTime >= recoverAt) {
            instance.TryRecoverToActive();
            if (motor != null) motor.SetCrashMode(false);
        }
    }

    /// <summary>Recycles collider and visuals together after the minimum wreck lifetime; the deficit remains pending for a safe spawn.</summary>
    public bool ReleaseWreck() {
        if (world == null || !world.IsActive || !IsWreck || world.SessionTime - wreckAt < world.Settings.wreckLifetimeSeconds) return false;
        if (!pool.Release(instance)) return false;
        population.ReleaseWreck();
        if (motor != null) motor.ResetForNewLife();
        if (presentation != null) presentation.ResetForLife();
        Unbind(); gameObject.SetActive(false);
        return true;
    }

    /// <summary>Closes damage acceptance on session end, preserving the physical wreck until scene cleanup.</summary>
    public void Suspend() {
        health.SetSessionActive(false); contacts.Clear();
        if (motor != null) motor.StopMovement();
    }

    /// <summary>Detaches this body's previous life before reuse or destruction.</summary>
    public void Unbind() {
        if (world != null) world.Unregister(this);
        health.InvalidateLife();
        contacts.Clear(); world = null; player = null; instance = null;
    }

    void OnDestroy() { Unbind(); }
}
