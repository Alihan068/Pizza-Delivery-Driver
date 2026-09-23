#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

// Small typed adapter to the recorder integrated by Main; no renderer arrays or runtime reflection.
sealed class S12BenchmarkCapture : IDisposable {
    TrafficBenchmarkRecorder recorder;
    DevelopmentTestProfile profile;

    internal void Begin(DevelopmentTestProfile isolated, TrafficSessionHost host, S12BenchmarkCase plan,
        S12BenchmarkCaseReport record) {
        Dispose();
        profile = isolated;
        recorder = new TrafficBenchmarkRecorder();
        var metadata = new TrafficBenchmarkMetadata(host.gameObject.scene.name, "0",
            Screen.width + "x" + Screen.height, plan.Label);
        // Recorder sums frame deltas; give it readiness-timeout headroom so its duration boundary
        // cannot truncate the runner's independent realtime window by a startup/frame difference.
        if (!recorder.Initialize(profile, S12BenchmarkPlan.MaximumSamples,
            plan.Seconds + (float)S12BenchmarkPlan.ReadyTimeout, metadata, host, out string reason))
            throw new InvalidOperationException("Recorder initialization: " + reason);
        record.recorder = "Recording; real frame intervals, rendered/GPU performance Not Measured";
    }

    internal void Sample() {
        if (recorder == null || !recorder.IsRunning)
            throw new InvalidOperationException("Recorder stopped before full realtime coverage: " + recorder?.StopReason);
        recorder.Update();
        if (!recorder.IsRunning)
            throw new InvalidOperationException("Recorder capacity/duration exhausted: " + recorder.StopReason);
    }

    internal void End(S12BenchmarkCaseReport record) {
        if (recorder == null) return;
        try {
            if (recorder.IsRunning && !recorder.Stop(out _, out string reason))
                throw new IOException("Recorder output: " + reason);
            if (!string.IsNullOrEmpty(recorder.LastError)) throw new IOException(recorder.LastError);
            string path = recorder.LastOutputPath;
            if (string.IsNullOrEmpty(path) || !DevelopmentTestProfile.IsContainedPath(profile.RootPath, path) || !File.Exists(path))
                throw new IOException("Recorder did not preserve a profile-contained output.");
            if (record != null) {
                record.recorder = recorder.StopReason == TrafficBenchmarkStopReason.Explicit ?
                    "Captured; inspect availability/cap violations in recorder JSON" : "Partial: " + recorder.StopReason;
                record.recorderPath = path;
            }
        } finally { Dispose(); }
    }

    /// <summary>Releases only this capture's profiler handles; never deletes output.</summary>
    public void Dispose() { recorder?.Dispose(); recorder = null; }
}
#endif
