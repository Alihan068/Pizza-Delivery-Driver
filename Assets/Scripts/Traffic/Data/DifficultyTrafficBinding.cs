/// <summary>Binds a map's authored difficulty id to the population/director profiles active at that difficulty. Not the same as shift-time difficulty ramp.</summary>
[System.Serializable]
public class DifficultyTrafficBinding {
    public string difficultyId;
    public string civilianPopulationProfileId;
    public string policeDirectorProfileId;
}
