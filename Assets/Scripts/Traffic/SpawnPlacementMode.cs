/// <summary>
/// Distinguishes a pre-session, no-player-motion initial layout pass from an active-game
/// replacement spawn. <see cref="VehicleSpawnPolicy"/> only applies the player-approach-time check
/// under <see cref="ActiveReplacement"/> — nothing is moving with intent yet during initial layout.
/// </summary>
public enum SpawnPlacementMode {
    InitialPlacement,
    ActiveReplacement
}
