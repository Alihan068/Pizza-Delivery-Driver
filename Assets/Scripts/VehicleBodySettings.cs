/// <summary>
/// Authored Rigidbody2D mass for one vehicle: a flat <see cref="baseMass"/>, plus an optional
/// per-Health-level override table (index 0 = zero Health upgrades bought). Health is this
/// project's closest existing stat to chassis durability, so the level-dependent table is bound to
/// it rather than introducing a new paid stat. An empty table means every Health level resolves to
/// <see cref="baseMass"/>. Mass only ever affects collision push/momentum — the vehicle's own
/// acceleration target is unaffected, since <see cref="VehicleMovement.AddAcceleration"/> already
/// scales its applied force by the live Rigidbody2D mass to cancel it out.
/// </summary>
[System.Serializable]
public class VehicleBodySettings {
    public float baseMass = 1f;

    /// <summary>Optional per-Health-level mass override; index 0 is zero Health upgrades. Leave empty to use baseMass at every level.</summary>
    public float[] massByHealthLevel = new float[0];

    /// <summary>Resolves the Rigidbody2D mass for a given Health upgrade level.</summary>
    /// <param name="healthLevel">Health levels already purchased.</param>
    public float ResolveMass(int healthLevel) {
        if (massByHealthLevel != null && healthLevel >= 0 && healthLevel < massByHealthLevel.Length) {
            float overrideValue = massByHealthLevel[healthLevel];
            if (IsValid(overrideValue)) return overrideValue;
        }
        return IsValid(baseMass) ? baseMass : 1f;
    }

    static bool IsValid(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
}
