using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Police-only fail-closed validation composed around the unchanged shared NPC validator.</summary>
public static class PolicePreflight {
    /// <summary>Validates a passive police wrapper against the shared traffic identity and tuning.</summary>
    public static bool ValidateVehicleProfile(PoliceVehicleProfile police, ITrafficProfileCatalog catalog, out string reason) {
        reason = null;
        if (police == null || police.sharedNpc == null) return Fail("police wrapper or shared profile is null", out reason);
        var profile = police.sharedNpc;
        if (string.IsNullOrWhiteSpace(profile.vehicleProfileId)) return Fail("shared vehicle id is blank", out reason);
        if (catalog == null || catalog.ResolveVehicleProfile(profile.vehicleProfileId) != profile) return Fail("shared vehicle profile is unresolved or conflicting", out reason);
        if (profile.allowedRoles == null || !profile.allowedRoles.Contains(VehicleRole.Police)) return Fail("shared profile does not permit Police", out reason);
        if (!NpcVehicleBodyValidator.ValidateProfileBody(profile, out reason)) return false;
        if (!FinitePositive(profile.maxHealth)) return Fail("maxHealth must be finite and positive", out reason);
        if (!FiniteRange(profile.collisionArmor) || !FiniteRange(profile.explosionResistance)) return Fail("armor and resistance must be finite in [0,1]", out reason);
        if (!FiniteRange(profile.smokeHealthFraction01) || !FiniteRange(profile.criticalHealthFraction01) || profile.criticalHealthFraction01 > profile.smokeHealthFraction01)
            return Fail("feedback thresholds are invalid", out reason);
        if (string.IsNullOrWhiteSpace(profile.visualCatalogId) || string.IsNullOrWhiteSpace(profile.damageProfileId) || string.IsNullOrWhiteSpace(profile.explosionProfileId)) return Fail("visual, damage, and explosion ids are required", out reason);
        if (police.supportedTacticalRoles == null || police.supportedTacticalRoles.Count == 0) return Fail("police wrapper has no tactical roles", out reason);
        var roles = new HashSet<PoliceTacticalRole>();
        foreach (var role in police.supportedTacticalRoles)
            if (!roles.Add(role) || !Enum.IsDefined(typeof(PoliceTacticalRole), role)) return Fail("police tactical role is invalid or duplicated", out reason);
        return true;
    }

    /// <summary>Validates one passive behavior profile without implying behavior execution.</summary>
    public static bool ValidateBehaviorProfile(PoliceBehaviorProfile behavior, out string reason) {
        reason = null;
        if (behavior == null || string.IsNullOrWhiteSpace(behavior.behaviorProfileId)) return Fail("behavior id is blank", out reason);
        if (!Enum.IsDefined(typeof(PoliceTacticalRole), behavior.tacticalRole)) return Fail("behavior tactical role is invalid", out reason);
        return true;
    }

    /// <summary>Validates a police prefab and its profile footprint before Awake is allowed to matter.</summary>
    public static bool ValidatePrefab(PoliceVehicleBody body, NpcVehicleProfile profile, out string reason) {
        reason = null;
        if (body == null || body.gameObject == null) return Fail("police body is null", out reason);
        if (profile == null) return Fail("police body profile is null", out reason);
        var root = body.gameObject;
        if (root.GetComponentsInChildren<Driver>(true).Length > 0 || root.GetComponentsInChildren<GameManager>(true).Length > 0 || root.GetComponentsInChildren<CivilianVehicleBody>(true).Length > 0 ||
            root.GetComponentsInChildren<CivilianRouteFollower>(true).Length > 0 || root.GetComponentsInChildren<VehicleMovement>(true).Length > 0 ||
            root.GetComponentsInChildren<VehicleInput>(true).Length > 0 || root.GetComponentsInChildren<PlayerInput>(true).Length > 0 ||
            root.GetComponentsInChildren<Delivery>(true).Length > 0)
            return Fail("police prefab contains a forbidden player or civilian component", out reason);
        if (!FinitePositive(root.transform.lossyScale.x) || !FinitePositive(root.transform.lossyScale.y) || !FinitePositive(root.transform.lossyScale.z)) return Fail("police prefab scale is invalid", out reason);
        var bodies = root.GetComponentsInChildren<Rigidbody2D>(true);
        var colliders = root.GetComponentsInChildren<Collider2D>(true);
        var motors = root.GetComponentsInChildren<NpcVehicleMotor>(true);
        var sensors = root.GetComponentsInChildren<VehicleObstacleSensor>(true);
        var receivers = root.GetComponentsInChildren<VehicleDamageReceiver>(true);
        var bodyMarkers = root.GetComponentsInChildren<PoliceVehicleBody>(true);
        if (bodies.Length != 1 || colliders.Length != 1 || motors.Length != 1 || sensors.Length != 1 || receivers.Length != 1 || bodyMarkers.Length != 1) return Fail("police prefab is missing or duplicating a required component", out reason);
        var rb = bodies[0];
        var collider = colliders[0] as BoxCollider2D;
        if (collider == null || rb.transform != root.transform || collider.transform != root.transform || motors[0].transform != root.transform || sensors[0].transform != root.transform || receivers[0].transform != root.transform) return Fail("police physics components must be singular and on the root", out reason);
        if (!motors[0].enabled || !sensors[0].enabled || !receivers[0].enabled) return Fail("police behavior components must be enabled", out reason);
        if (!collider.enabled || !FiniteVector(collider.size) || !FiniteVector(collider.offset) || collider.offset.sqrMagnitude > 0.000001f) return Fail("police collider is disabled or its size/offset is invalid", out reason);
        if (rb.bodyType != RigidbodyType2D.Dynamic || rb.collisionDetectionMode != CollisionDetectionMode2D.Continuous || rb.interpolation != RigidbodyInterpolation2D.Interpolate ||
            !Mathf.Approximately(rb.gravityScale, 0f) || !rb.simulated || collider.isTrigger)
            return Fail("police prefab physics settings are invalid", out reason);
        if (body.Body != rb || body.MainCollider != collider || body.Motor != root.GetComponent<NpcVehicleMotor>() || body.Sensor != root.GetComponent<VehicleObstacleSensor>() || body.DamageReceiver != root.GetComponent<VehicleDamageReceiver>())
            return Fail("police body serialized references do not match its root", out reason);
        var footprint = new Vector2(Mathf.Abs(collider.size.x * root.transform.lossyScale.x), Mathf.Abs(collider.size.y * root.transform.lossyScale.y));
        if (!ExactMatches(footprint.x, profile.colliderSize.x) || !ExactMatches(footprint.y, profile.colliderSize.y)) return Fail("police prefab footprint does not match profile", out reason);
        var renderers = root.GetComponentsInChildren<SpriteRenderer>(true);
        SpriteRenderer renderer = null;
        foreach (var candidate in renderers) if (candidate.enabled && candidate.sprite != null) { renderer = candidate; break; }
        if (renderer == null) return Fail("police prefab has no enabled authored sprite", out reason);
        Vector2 spriteSize = renderer.sprite.bounds.size;
        spriteSize = new Vector2(Mathf.Abs(spriteSize.x * renderer.transform.lossyScale.x), Mathf.Abs(spriteSize.y * renderer.transform.lossyScale.y));
        if (!VisualMatches(footprint.x, spriteSize.x) || !VisualMatches(footprint.y, spriteSize.y)) return Fail("police authored collider and sprite geometry do not match", out reason);
        return true;
    }

    /// <summary>Validates every catalog binding and rejects missing or duplicate visual ids.</summary>
    public static bool ValidatePrefabCatalog(PoliceVehiclePrefabCatalog catalog, out string reason) {
        reason = null;
        if (catalog == null || catalog.entries == null || catalog.entries.Count == 0) return Fail("police prefab catalog is empty", out reason);
        var ids = new HashSet<string>();
        foreach (var entry in catalog.entries) {
            if (entry == null || string.IsNullOrWhiteSpace(entry.visualCatalogId) || entry.prefab == null || !ids.Add(entry.visualCatalogId)) return Fail("police visual binding is missing or duplicated", out reason);
        }
        return true;
    }

    static bool ExactMatches(float actual, float expected) => FinitePositive(actual) && FinitePositive(expected) && Mathf.Abs(actual - expected) <= 0.001f;
    static bool VisualMatches(float actual, float expected) => FinitePositive(actual) && FinitePositive(expected) && Mathf.Abs(actual - expected) <= Mathf.Max(0.08f, expected * 0.15f);
    static bool FiniteVector(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    static bool FiniteRange(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f && value <= 1f;
    static bool Fail(string message, out string reason) { reason = message; return false; }
}
