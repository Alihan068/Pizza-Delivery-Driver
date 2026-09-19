using UnityEngine;

/// <summary>Authored per-role blast damage multipliers. The player's is deliberately separate and low, never shared with the NPC roles.</summary>
[System.Serializable]
public class BlastRoleMultipliers {
    public float civilianMultiplier = 1f;
    public float policeMultiplier = 1f;

    [Tooltip("Deliberately low and separate from the NPC role multipliers.")]
    public float playerMultiplier = 0.3f;

    public float Resolve(VehicleRole role) {
        switch (role) {
            case VehicleRole.Player: return playerMultiplier;
            case VehicleRole.Police: return policeMultiplier;
            case VehicleRole.Civilian: return civilianMultiplier;
            default: return 1f;
        }
    }
}
