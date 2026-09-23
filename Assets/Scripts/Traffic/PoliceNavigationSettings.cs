using System;

/// <summary>Authorable navigation values copied into a validated runtime snapshot for each query.</summary>
[Serializable]
public sealed class PoliceNavigationSettings {
    /// <summary>Minimum spacing reserved for the later cadence wrapper.</summary>
    public float hardMinimumInterval = 0.25f;
    /// <summary>Refresh interval reserved for the later cadence wrapper.</summary>
    public float refreshInterval = 0.75f;
    /// <summary>Player displacement threshold reserved for the later cadence wrapper.</summary>
    public float targetDisplacementThreshold = 2f;
    /// <summary>Projection search radius in map-local units.</summary>
    public float projectionSearchRadius = 20f;
    /// <summary>Spatial index cell size in map-local units.</summary>
    public float indexCellSize = 8f;
    /// <summary>Default finite aggregate query budget.</summary>
    public int workBudget = 4096;
    /// <summary>Extra full-dimensional footprint margin for clearance checks.</summary>
    public float clearanceMargin = 0.5f;
    /// <summary>Maximum allowed direction change used by anchored path constraints.</summary>
    public float maxTurnAngle = 135f;
    /// <summary>Distance treated as already on-road for connector decisions.</summary>
    public float onRoadTolerance = 0.05f;

    /// <summary>Creates the bounded defaults agreed for the police navigation layer.</summary>
    public PoliceNavigationSettings(float hardMinimumInterval = 0.25f, float refreshInterval = 0.75f,
        float targetDisplacementThreshold = 2f, float projectionSearchRadius = 20f, float indexCellSize = 8f,
        int workBudget = 4096, float clearanceMargin = 0.5f, float maxTurnAngle = 135f,
        float onRoadTolerance = 0.05f) {
        this.hardMinimumInterval = hardMinimumInterval;
        this.refreshInterval = refreshInterval;
        this.targetDisplacementThreshold = targetDisplacementThreshold;
        this.projectionSearchRadius = projectionSearchRadius;
        this.indexCellSize = indexCellSize;
        this.workBudget = workBudget;
        this.clearanceMargin = clearanceMargin;
        this.maxTurnAngle = maxTurnAngle;
        this.onRoadTolerance = onRoadTolerance;
    }

    /// <summary>Creates an independent runtime snapshot of this authorable settings object.</summary>
    public PoliceNavigationSettings Clone() => new PoliceNavigationSettings(hardMinimumInterval, refreshInterval,
        targetDisplacementThreshold, projectionSearchRadius, indexCellSize, workBudget, clearanceMargin,
        maxTurnAngle, onRoadTolerance);

    /// <summary>Validates every serialized tuning value without mutating the snapshot.</summary>
    /// <param name="reason">Failure reason, or null when valid.</param>
    /// <returns>True when all settings are finite and bounded.</returns>
    public bool IsValid(out string reason) {
        reason = null;
        if (!FinitePositive(hardMinimumInterval) || !FinitePositive(refreshInterval) || !FinitePositive(targetDisplacementThreshold) ||
            !FinitePositive(projectionSearchRadius) || !FinitePositive(indexCellSize) || workBudget <= 0 ||
            !FiniteNonnegative(clearanceMargin) || !FinitePositive(maxTurnAngle) || maxTurnAngle > 180f || !FiniteNonnegative(onRoadTolerance)) {
            reason = "invalid police navigation settings";
            return false;
        }
        return true;
    }

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    static bool FinitePositive(float value) => Finite(value) && value > 0f;
    static bool FiniteNonnegative(float value) => Finite(value) && value >= 0f;
}
