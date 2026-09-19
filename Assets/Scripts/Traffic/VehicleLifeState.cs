/// <summary>Lifecycle phase of one <see cref="NpcVehicleInstance"/>. Transitions are contracts §4: Pooled -> Reserved -> Active -> CrashRecovery (live) -> Active or Wreck -> Pooled.</summary>
public enum VehicleLifeState {
    Pooled,
    Reserved,
    Active,
    CrashRecovery,
    Wreck
}
