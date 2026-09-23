using System.Collections.Generic;
using UnityEngine;

/// <summary>Passive police wrapper around one shared NPC vehicle profile.</summary>
[CreateAssetMenu(fileName = "NewPoliceVehicleProfile", menuName = "PizzaGame/Traffic/Police Vehicle Profile")]
public sealed class PoliceVehicleProfile : ScriptableObject {
    /// <summary>One authored role weight for this stable vehicle profile.</summary>
    [System.Serializable]
    public sealed class RoleWeight {
        /// <summary>Tactical role represented by this weight.</summary>
        public PoliceTacticalRole role;
        /// <summary>Non-negative selection weight for the role.</summary>
        public float weight;
    }

    /// <summary>Shared civilian/police physical profile; it owns the stable vehicle identity and tuning.</summary>
    public NpcVehicleProfile sharedNpc;

    /// <summary>Behavior roles declared as compatible with this vehicle.</summary>
    public List<PoliceTacticalRole> supportedTacticalRoles = new List<PoliceTacticalRole>();

    /// <summary>Authored role weights for the profile's later tactical coordinator.</summary>
    public List<RoleWeight> roleWeights = new List<RoleWeight>();

    /// <summary>Authored stable per-life variation bounds for this vehicle profile.</summary>
    public PoliceUnitVariationSettings variationSettings = new PoliceUnitVariationSettings();

    /// <summary>Returns whether this profile explicitly supports the supplied tactical role.</summary>
    /// <param name="role">Role to inspect.</param>
    /// <returns>True when the authored supported-role list contains the role.</returns>
    public bool SupportsRole(PoliceTacticalRole role) => supportedTacticalRoles != null && supportedTacticalRoles.Contains(role);

    /// <summary>Reads one authored role weight without inferring vehicle class from an enum or switch.</summary>
    /// <param name="role">Role to resolve.</param>
    /// <param name="weight">Authored non-negative weight, or zero when absent.</param>
    /// <returns>True when a finite positive authored weight exists.</returns>
    public bool TryGetRoleWeight(PoliceTacticalRole role, out float weight) {
        weight = 0f;
        if (roleWeights == null) return false;
        foreach (RoleWeight entry in roleWeights) {
            if (entry == null || entry.role != role || float.IsNaN(entry.weight) || float.IsInfinity(entry.weight)) continue;
            weight = Mathf.Max(0f, entry.weight);
            return weight > 0f;
        }
        return false;
    }
}
