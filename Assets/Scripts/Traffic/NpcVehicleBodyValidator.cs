using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Authoring-time checks for one <see cref="NpcMotorSettings"/>/<see cref="NpcVehicleProfile"/> and
/// for the physical prefab a profile's visual catalog id ultimately resolves to. Structural only —
/// it never simulates physics or judges how a vehicle feels, only whether the authored data and
/// component set are even usable.
/// </summary>
public static class NpcVehicleBodyValidator {
    /// <summary>Rejects NaN/Infinity/non-positive tuning values and a max speed below cruise speed.</summary>
    public static bool ValidateMotorSettings(NpcMotorSettings settings, out string reason) {
        reason = null;
        if (settings == null) { reason = "motor settings are null"; return false; }

        if (!IsFinitePositive(settings.cruiseSpeed)) { reason = "cruiseSpeed must be a finite positive value"; return false; }
        if (!IsFinitePositive(settings.maxSpeed)) { reason = "maxSpeed must be a finite positive value"; return false; }
        if (settings.maxSpeed < settings.cruiseSpeed) { reason = "maxSpeed cannot be below cruiseSpeed"; return false; }
        if (!IsFiniteNonNegative(settings.reverseSpeed)) { reason = "reverseSpeed must be finite and non-negative"; return false; }
        if (!IsFinitePositive(settings.acceleration)) { reason = "acceleration must be a finite positive value"; return false; }
        if (!IsFinitePositive(settings.brakeDeceleration)) { reason = "brakeDeceleration must be a finite positive value"; return false; }
        if (!IsFinitePositive(settings.maxEngineForce)) { reason = "maxEngineForce must be a finite positive value"; return false; }
        if (!IsFinitePositive(settings.maxBrakeForce)) { reason = "maxBrakeForce must be a finite positive value"; return false; }
        if (!IsFinitePositive(settings.turnRate)) { reason = "turnRate must be a finite positive value"; return false; }
        if (!IsFinitePositive(settings.minimumTurningRadius)) { reason = "minimumTurningRadius must be a finite positive value"; return false; }
        if (float.IsNaN(settings.lateralGrip) || settings.lateralGrip < 0f || settings.lateralGrip > 1f) { reason = "lateralGrip must be within [0, 1]"; return false; }
        if (!IsFinitePositive(settings.sensorInterval)) { reason = "sensorInterval must be a finite positive value"; return false; }
        if (!IsFiniteNonNegative(settings.reactionTime)) { reason = "reactionTime must be finite and non-negative"; return false; }
        if (!IsFiniteNonNegative(settings.minimumGap)) { reason = "minimumGap must be finite and non-negative"; return false; }

        return true;
    }

    /// <summary>Rejects an invalid baseMass/colliderSize or invalid motor settings for a whole profile.</summary>
    public static bool ValidateProfileBody(NpcVehicleProfile profile, out string reason) {
        reason = null;
        if (profile == null) { reason = "profile is null"; return false; }
        if (!IsFinitePositive(profile.baseMass)) { reason = "baseMass must be a finite positive value"; return false; }
        if (!IsFinitePositive(profile.colliderSize.x) || !IsFinitePositive(profile.colliderSize.y)) {
            reason = "colliderSize must have finite positive width and length";
            return false;
        }
        return ValidateMotorSettings(profile.motorSettings, out reason);
    }

    /// <summary>
    /// Rejects a prefab root that is missing a required NPC body part, carries player-only
    /// components (a sign the wrong prefab was bound — the player's own vehicle, or static decor
    /// with no Rigidbody2D at all), or whose collider footprint is wildly out of proportion with its
    /// sprite. Never checks +Y-forward sprite authoring itself — that convention is not structurally
    /// verifiable from components alone.
    /// </summary>
    public static bool ValidatePrefabStructure(GameObject root, out string reason) {
        reason = null;
        if (root == null) { reason = "prefab root is null"; return false; }

        if (root.GetComponent<Driver>() != null || root.GetComponent<PlayerInput>() != null ||
            root.GetComponent<Delivery>() != null || root.GetComponent<GameManager>() != null) {
            reason = "prefab carries player-only components — the player's own vehicle prefab was bound instead of an NPC body";
            return false;
        }

        var rigidbody = root.GetComponent<Rigidbody2D>();
        if (rigidbody == null) { reason = "prefab has no Rigidbody2D — looks like static decor, not a drivable NPC body"; return false; }
        if (rigidbody.bodyType != RigidbodyType2D.Dynamic) { reason = "Rigidbody2D must be Dynamic"; return false; }
        if (rigidbody.collisionDetectionMode != CollisionDetectionMode2D.Continuous) { reason = "Rigidbody2D must use Continuous collision detection"; return false; }
        if (rigidbody.interpolation != RigidbodyInterpolation2D.Interpolate) { reason = "Rigidbody2D must use Interpolate"; return false; }

        var collider = root.GetComponent<Collider2D>();
        if (collider == null) { reason = "prefab has no Collider2D"; return false; }

        var renderer = root.GetComponent<SpriteRenderer>();
        if (renderer == null || renderer.sprite == null) { reason = "prefab has no SpriteRenderer with a sprite assigned"; return false; }

        float rendererArea = renderer.bounds.size.x * renderer.bounds.size.y;
        float colliderArea = collider.bounds.size.x * collider.bounds.size.y;
        if (rendererArea <= 0f || colliderArea <= 0f) { reason = "renderer or collider bounds collapse to zero area"; return false; }

        float ratio = colliderArea / rendererArea;
        if (ratio < 0.25f || ratio > 4f) {
            reason = "collider footprint is not reasonably matched to the sprite's renderer bounds";
            return false;
        }

        return true;
    }

    static bool IsFinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    static bool IsFiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
}
