using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored identity for one kind of civilian or police vehicle: permanent id, role permissions,
/// a visual/prefab catalog reference (never a direct prefab field here), motor tuning, collider
/// envelope, mass, health, and damage/explosion profile bindings. S04 wires this to the shared NPC
/// motor; S05 wires the damage/explosion profile ids to real behavior.
/// </summary>
[CreateAssetMenu(fileName = "NewNpcVehicleProfile", menuName = "PizzaGame/Traffic/Npc Vehicle Profile")]
public class NpcVehicleProfile : ScriptableObject {
    [Header("Identity")]
    [Tooltip("Permanent identifier used by routes, pools, and spawns. Never change it after release.")]
    public string vehicleProfileId;

    [Tooltip("Roles allowed to use this profile.")]
    public List<VehicleRole> allowedRoles = new List<VehicleRole>();

    [Tooltip("Catalog id resolved by the content/visual system to an actual prefab or sprite. Never a direct prefab reference here.")]
    public string visualCatalogId;

    [Header("Motor")]
    public NpcMotorSettings motorSettings = new NpcMotorSettings();

    [Header("Physical Envelope")]
    [Tooltip("Collider footprint used for clearance/fit checks (width, length).")]
    public Vector2 colliderSize = new Vector2(2f, 4f);

    [Tooltip("Base physical mass used for collision response.")]
    public float baseMass = 1f;

    [Header("Health And Damage")]
    public float maxHealth = 100f;

    [Range(0f, 1f)]
    [Tooltip("Fraction of incoming collision damage this profile resists.")]
    public float collisionArmor;

    [Range(0f, 1f)]
    [Tooltip("Fraction of incoming explosion damage this profile resists.")]
    public float explosionResistance;

    [Tooltip("Damage profile catalog id consumed by S05.")]
    public string damageProfileId;

    [Tooltip("Explosion profile catalog id consumed by S05.")]
    public string explosionProfileId;

    [Header("Damage Feedback (visual/audio only, never affects gameplay math)")]
    [Range(0f, 1f)]
    [Tooltip("Remaining health fraction at/below which smoke feedback should show.")]
    public float smokeHealthFraction01 = 0.5f;

    [Range(0f, 1f)]
    [Tooltip("Remaining health fraction at/below which critical feedback should show. Must be <= smokeHealthFraction01.")]
    public float criticalHealthFraction01 = 0.2f;
}
