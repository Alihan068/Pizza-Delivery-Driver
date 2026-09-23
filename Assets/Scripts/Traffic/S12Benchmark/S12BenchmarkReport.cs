#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
sealed class S12BenchmarkReport {
    public string outcome = "In progress", profileGuid, unityVersion, buildGuid, device, vehicleId, difficultyId;
    public string conditions = "Stationary input; real physics; startup included; background/contended by owner's game.";
    public string render = "GPU/render/presentation/occlusion Not Measured; Update intervals are not rendered FPS.";
    public string determinism = "Traffic seed 0 only; other gameplay RNG uncontrolled; not bitwise deterministic.";
    public string naturalEndings = "Not tested: explicit Abandoned; independent accepted native ending tests retained.";
    public string maxHeat = "Not Achieved: no approved pre-freeze typed director hook installed.";
    public string blast = "Not Achieved: no approved typed real damage pipeline trigger installed.";
    public string cap = "Not Achieved", baselineComparison = "Not Achieved", coreLifecycle = "Not completed";
    public string ownerFeel = "Not Measured", leakProof = "Not Measured: counts/managed heap observations only; no forced GC.";
    public bool isolatedDevelopmentUnlock;
    public double totalWallSeconds;
    public List<S12BenchmarkCaseReport> cases = new List<S12BenchmarkCaseReport>();

    internal void Write(DevelopmentTestProfile profile, string checkpoint) {
        if (!profile.IsRequested || !profile.IsValid) throw new IOException("No isolated report destination.");
        foreach (var item in cases) if (item.civilianCap == 10 && item.secondsAtCivilianCap > 0)
            cap = "Observed authored civilian cap 10; same profile, no override; inspect sampled duration/counts.";
        if (cases.Count > 5) baselineComparison = cases[3].ComparableTo(cases[5]) ?
            "Two same-config completed 60s baselines; compare recorder JSON; performance acceptance remains unjudged" :
            "Not Achieved: baseline coverage/provenance/pose/camera/focus mismatch; do not compare as identical.";
        if (cases.Count >= 5) {
            bool complete = true;
            for (int i = 0; i < 5; i++) complete &= cases[i].explicitAbandoned &&
                cases[i].window.StartsWith("Complete", StringComparison.Ordinal) &&
                cases[i].cleanup.StartsWith("Passed", StringComparison.Ordinal) &&
                cases[i].sampledBudgetViolations == 0 && cases[i].sampledEmptyViolations == 0;
            coreLifecycle = complete ? "Five real cycles completed, including one continuous 600s Peaceful FREEPLAY" :
                "Not Achieved: inspect incomplete/partial/invalid lifecycle window";
        }
        string path = Path.Combine(profile.RootPath, "s12-driver-" + checkpoint + "-" + Guid.NewGuid().ToString("N") + ".json");
        if (!DevelopmentTestProfile.IsContainedPath(profile.RootPath, path)) throw new IOException("Report path escaped profile.");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
        using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(this, true));
    }
}

static class S12BenchmarkProvenance {
    [Serializable] sealed class NpcSet { public List<Npc> civilian = new List<Npc>(); public List<Npc> police = new List<Npc>(); }
    [Serializable] sealed class Npc {
        public string id, visual, motor, behavior, damage, explosion;
        public float mass, armor, resistance, health;
    }
    internal static string Npcs(SessionContentEvidence evidence) {
        var set = new NpcSet();
        Copy(evidence.civilianProfiles, set.civilian); Copy(evidence.policeProfiles, set.police);
        return JsonUtility.ToJson(set);
    }
    static void Copy(IReadOnlyList<SessionNpcProfileEvidence> source, List<Npc> destination) {
        foreach (var item in source) destination.Add(new Npc {
            id = item.profileId, visual = item.visualCatalogId, motor = item.motorFingerprint,
            behavior = item.behaviorFingerprint, damage = item.damageProfileId, explosion = item.explosionProfileId,
            mass = item.baseMass, armor = item.collisionArmor, resistance = item.explosionResistance, health = item.maxHealth
        });
    }
}
#endif
