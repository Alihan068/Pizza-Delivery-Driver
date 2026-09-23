using UnityEngine;

/// <summary>Passive stable configuration for one police tactical behavior.</summary>
[CreateAssetMenu(fileName = "NewPoliceBehaviorProfile", menuName = "PizzaGame/Traffic/Police Behavior Profile")]
public sealed class PoliceBehaviorProfile : ScriptableObject {
    /// <summary>Stable behavior identifier used by police configuration.</summary>
    public string behaviorProfileId;

    /// <summary>Tactical role represented by this profile.</summary>
    public PoliceTacticalRole tacticalRole;

    /// <summary>Bounded driving configuration consumed by a pursuing controller after a police life is active.</summary>
    public PoliceDrivingSettings driving = new PoliceDrivingSettings();

    /// <summary>Provisional bounded prediction configuration consumed only by the Intercept role.</summary>
    public PoliceInterceptSettings intercept = new PoliceInterceptSettings();
    /// <summary>Goal selection for the Shadow role: how far ahead to lead and how side streets are found and blocked.</summary>
    public PoliceShadowTactics.Settings shadow = new PoliceShadowTactics.Settings();

    /// <summary>Returns whether this behavior profile represents the supplied role.</summary>
    /// <param name="role">Role to compare.</param>
    /// <returns>True when the stable behavior profile role matches.</returns>
    public bool SupportsRole(PoliceTacticalRole role) => tacticalRole == role;
}
