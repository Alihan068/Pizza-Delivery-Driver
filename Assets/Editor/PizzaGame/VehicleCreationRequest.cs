using UnityEngine;

/// <summary>
/// Everything needed to create a new vehicle.
/// </summary>
/// <remarks>
/// <see cref="draftData"/> is deliberately a ready-made <see cref="VehicleData"/> instance:
/// the wizard clones it from the template asset with <c>Object.Instantiate</c>, the user edits
/// it in the Inspector, and the service copies it to disk. That keeps every balance field
/// editable while the starting values still come from the real balance table -- the stale C#
/// initializers behind <c>ScriptableObject.CreateInstance</c> never come into play.
/// </remarks>
public class VehicleCreationRequest {

    /// <summary>Template vehicle prefab to clone.</summary>
    public GameObject templatePrefab;

    /// <summary>In-memory draft of the vehicle data that will be written to disk.</summary>
    public VehicleData draftData;

    /// <summary>Body sprite for the new vehicle. When null the template's sprite is kept.</summary>
    public Sprite bodySprite;

    /// <summary>How the collision shape is determined.</summary>
    public VehicleColliderMode colliderMode = VehicleColliderMode.AutoFromSprite;

    /// <summary>
    /// Whether the created vehicle is appended to <c>allVehicles</c> on the GameManager prefab.
    /// When disabled the vehicle is created but never shows up in the game.
    /// </summary>
    public bool registerInGameManager = true;
}
