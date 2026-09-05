using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Outcome of a vehicle creation attempt: what was created, what was done, what was found.
/// </summary>
public class VehicleCreationResult {

    /// <summary>True when creation completed. When false no asset was created at all.</summary>
    public bool success;

    /// <summary>The created prefab asset, or null on failure.</summary>
    public GameObject createdPrefab;

    /// <summary>The created vehicle data asset, or null on failure.</summary>
    public VehicleData createdData;

    /// <summary>Human readable summary of the steps that were taken.</summary>
    public List<string> steps = new List<string>();

    /// <summary>Findings from validation before and after creation.</summary>
    public List<VehicleIssue> issues = new List<VehicleIssue>();

    /// <summary>Explains why creation failed; empty on success.</summary>
    public string failureReason = string.Empty;

    /// <summary>Creates a failed result.</summary>
    /// <param name="reason">The reason shown to the user.</param>
    /// <returns>A new result whose success field is false.</returns>
    public static VehicleCreationResult Fail(string reason) {
        return new VehicleCreationResult { success = false, failureReason = reason };
    }
}
