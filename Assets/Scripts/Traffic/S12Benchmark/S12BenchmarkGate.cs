using System;
using System.IO;
using UnityEngine;

/// <summary>Fail-closed, process-local opt-in checked before career I/O or display/input boot.</summary>
public static class S12BenchmarkGate {
    /// <summary>Exact benchmark switch; no flag leaves ordinary startup unchanged.</summary>
    public const string ArgumentName = "-pizzaBenchmark";
    /// <summary>Only supported, bounded workload revision.</summary>
    public const string Workload = "stationary-v2";
    static bool evaluated, requested, allowed;
    static string rejection;
    static DevelopmentTestProfile profile;

    /// <summary>Suppresses display preferences and hardware input even for a rejected request.</summary>
    public static bool Requested { get { Evaluate(); return requested; } }
    /// <summary>Prevents a malformed or unsupported benchmark from falling back to real career I/O.</summary>
    public static bool BlockCareerStartup { get { Evaluate(); return requested && !allowed; } }
    /// <summary>The validated isolated profile; usable only after successful admission.</summary>
    public static DevelopmentTestProfile Profile { get { Evaluate(); return profile; } }

    /// <summary>Pure admission rule; callers supply build kind and preexisting-root observation.</summary>
    public static bool Validate(string[] arguments, string persistentRoot, bool developmentStandalone,
        bool rootAlreadyExists, out bool isRequested, out DevelopmentTestProfile parsed, out string reason) {
        int count = 0, index = -1;
        if (arguments != null) for (int i = 0; i < arguments.Length; i++) {
            if (!string.Equals(arguments[i], ArgumentName, StringComparison.Ordinal)) continue;
            count++; index = i;
        }
        isRequested = count > 0;
        parsed = DevelopmentTestProfile.ResolveArguments(arguments, persistentRoot);
        reason = null;
        if (!isRequested) return true;
        if (!developmentStandalone) reason = "Benchmark requires a Development standalone player.";
        else if (count != 1 || index + 1 >= arguments.Length || arguments[index + 1] != Workload)
            reason = "Benchmark switch must occur once with stationary-v2.";
        else if (!parsed.IsRequested || !parsed.IsValid) reason = "A valid explicit isolated profile is required.";
        else if (rootAlreadyExists) reason = "Profile must be fresh; existing data is never reused or deleted.";
        return reason == null;
    }

    static void Evaluate() {
        if (evaluated) return;
        evaluated = true;
        bool standalone = false;
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        standalone = true;
#endif
        try {
            string[] args = Environment.GetCommandLineArgs();
            allowed = Validate(args, Application.persistentDataPath, standalone, false,
                out requested, out profile, out rejection);
            if (!requested || !allowed) return;
            string parent = Path.GetDirectoryName(profile.RootPath);
            if (Directory.Exists(profile.RootPath) || File.Exists(profile.RootPath) ||
                (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Profile exists or its parent is a redirected directory.");
            if (!profile.EnsureDirectory()) throw new IOException("Cannot create isolated profile directory.");
            // CreateNew arbitrates simultaneous launches using the same fresh GUID. Never overwrite.
            using (var claim = new FileStream(Path.Combine(profile.RootPath, "benchmark.claim"),
                FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        } catch (Exception exception) {
            allowed = false;
            rejection = "Benchmark admission failed: " + exception.Message;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() {
        Evaluate();
        if (!requested) return;
        if (!allowed) { Debug.LogError(rejection); Application.Quit(2); return; }
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        Application.runInBackground = true;
        // Process-local settings only: never use DisplaySettings setters or persist preferences.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = S12BenchmarkPlan.FrameCap;
        var owner = new GameObject("S12 Stationary Benchmark");
        UnityEngine.Object.DontDestroyOnLoad(owner);
        owner.AddComponent<S12BenchmarkRunner>();
#endif
    }
}
