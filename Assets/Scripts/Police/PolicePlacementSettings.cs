using System;
using UnityEngine;

/// <summary>Authorable bounded timing and range values shared by police placement attempts.</summary>
[Serializable]
public sealed class PolicePlacementSettings {
    /// <summary>Minimum surface-to-surface distance from the player at placement.</summary>
    public float minimumDistance = 8f;
    /// <summary>Additional distance reserved for the player's current speed.</summary>
    public float reactionSeconds = 2f;
    /// <summary>Invisible camera margin around the current camera rectangle.</summary>
    public float cameraMargin = 4f;
    /// <summary>Maximum authored entries inspected by one bounded attempt.</summary>
    public int maxCandidates = 8;
    /// <summary>Reserved dwell duration for later offscreen relocation cadence.</summary>
    public float offscreenDwellSeconds = 3f;
    /// <summary>Minimum authored rear-pursuer quota retained by relocation composition.</summary>
    public int keepRearPursuers = 1;
    /// <summary>Active seconds between waves that move part of the offscreen police in front of the player.</summary>
    public float aheadWaveIntervalSeconds = 5f;
    /// <summary>Share of offscreen police (at least one) moved in front of the player per wave.</summary>
    [Range(0f, 1f)] public float aheadWaveShare = 1f / 3f;
    /// <summary>Authored horizontal camera size used to freeze relocation safety.</summary>
    public float authoredCameraHorizontalWorldSize = 20f;
    /// <summary>Authored camera aspect used to freeze relocation safety.</summary>
    public float authoredCameraAspect = 16f / 9f;
    /// <summary>Frozen reference half-diagonal of the authored camera.</summary>
    public float referenceHalfDiagonal => Mathf.Sqrt(Mathf.Pow(authoredCameraHorizontalWorldSize * .5f, 2f) +
        Mathf.Pow(authoredCameraHorizontalWorldSize / authoredCameraAspect * .5f, 2f));
    /// <summary>Frozen three-times reference relocation radius.</summary>
    public float relocationRadius => referenceHalfDiagonal * 3f;

    /// <summary>Creates an independent snapshot so later Inspector edits cannot alter an active attempt.</summary>
    public PolicePlacementSettings Clone() {
        return new PolicePlacementSettings {
            minimumDistance = minimumDistance,
            reactionSeconds = reactionSeconds,
            cameraMargin = cameraMargin,
            maxCandidates = maxCandidates,
            offscreenDwellSeconds = offscreenDwellSeconds,
            keepRearPursuers = keepRearPursuers,
            aheadWaveIntervalSeconds = aheadWaveIntervalSeconds,
            aheadWaveShare = aheadWaveShare,
            authoredCameraHorizontalWorldSize = authoredCameraHorizontalWorldSize,
            authoredCameraAspect = authoredCameraAspect
        };
    }

    /// <summary>Validates all placement tuning without clamping or mutating the supplied values.</summary>
    /// <param name="reason">Failure reason, or null when the snapshot is valid.</param>
    /// <returns>True when every range, timing and candidate bound is finite and usable.</returns>
    public bool IsValid(out string reason) {
        reason = null;
        if (!FiniteNonnegative(minimumDistance) || !FiniteNonnegative(reactionSeconds) ||
            !FiniteNonnegative(cameraMargin) || maxCandidates <= 0 || !FiniteNonnegative(offscreenDwellSeconds) ||
            keepRearPursuers < 0 || !FiniteNonnegative(aheadWaveIntervalSeconds) || !FiniteNonnegative(aheadWaveShare) || aheadWaveShare > 1f || !FinitePositive(authoredCameraHorizontalWorldSize) || !FinitePositive(authoredCameraAspect)) {
            reason = "invalid police placement settings";
            return false;
        }
        return true;
    }

    static bool FiniteNonnegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
}
