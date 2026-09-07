/// <summary>
/// Persistent best-score progress for one map difficulty tier.
/// </summary>
[System.Serializable]
public class MapDifficultyProgress {

    /// <summary>Permanent map identifier.</summary>
    public string mapId;

    /// <summary>Permanent difficulty identifier inside the map.</summary>
    public string difficultyId;

    /// <summary>Highest valid score reached on this map difficulty.</summary>
    public int bestScore;

    /// <summary>Creates an empty progress record for a map difficulty pair.</summary>
    public MapDifficultyProgress() {
    }

    /// <summary>Creates a progress record with an initial best score.</summary>
    /// <param name="mapId">Permanent map identifier.</param>
    /// <param name="difficultyId">Permanent difficulty identifier.</param>
    /// <param name="bestScore">Initial best score.</param>
    public MapDifficultyProgress(string mapId, string difficultyId, int bestScore) {
        this.mapId = mapId;
        this.difficultyId = difficultyId;
        this.bestScore = bestScore;
    }
}
