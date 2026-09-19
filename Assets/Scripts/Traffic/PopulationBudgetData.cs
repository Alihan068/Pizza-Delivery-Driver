using UnityEngine;

/// <summary>Authored population/physics budget consumed by <see cref="VehiclePopulationService"/>. Every cap is Inspector data, never hardcoded.</summary>
[CreateAssetMenu(fileName = "NewPopulationBudget", menuName = "PizzaGame/Traffic/Population Budget")]
public class PopulationBudgetData : ScriptableObject {
    [Header("Moving Caps")]
    [Tooltip("Maximum civilian vehicles counted as live + pending at once.")]
    public int maxCivilianMoving = 20;

    [Tooltip("Maximum police vehicles counted as live + pending at once.")]
    public int maxPoliceMoving = 8;

    [Tooltip("Global cap across civilian and police combined, live + pending.")]
    public int maxTotalMoving = 24;

    [Header("Wreck And Physics Caps")]
    [Tooltip("Maximum wreck slots: occupied wrecks plus every accepted spawn's reserved future-wreck token.")]
    public int maxWreckSlots = 10;

    [Tooltip("Maximum active one-shot effects (explosions, debris) at once.")]
    public int maxActiveEffects = 16;

    [Tooltip("Global physics object budget across every traffic/police physical object.")]
    public int maxTotalPhysicsObjects = 40;
}
