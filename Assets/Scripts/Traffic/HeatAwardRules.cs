/// <summary>Authored heat award per victim role, for a Player-originated kill. Never a fixed unauthored number.</summary>
[System.Serializable]
public class HeatAwardRules {
    public float civilianVictimHeat = 10f;
    public float policeVictimHeat = 25f;

    public float Resolve(VehicleRole victimRole) {
        switch (victimRole) {
            case VehicleRole.Civilian: return civilianVictimHeat;
            case VehicleRole.Police: return policeVictimHeat;
            default: return 0f;
        }
    }
}
