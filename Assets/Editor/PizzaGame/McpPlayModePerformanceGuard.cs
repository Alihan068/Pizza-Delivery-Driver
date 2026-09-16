#if UNITY_EDITOR
using MCPForUnity.Editor.Services;
using MCPForUnity.Editor.Services.Transport;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Prevents a disconnected MCP transport from retrying while the game is being tested in the
/// Unity Editor. The MCP package keeps reconnecting on a short schedule and logs each failed
/// attempt; stopping only disconnected transports removes that editor-side hitch without
/// interrupting an already connected MCP session.
/// </summary>
[InitializeOnLoad]
static class McpPlayModePerformanceGuard {
    static bool httpSuppressed;
    static bool stdioSuppressed;

    static McpPlayModePerformanceGuard() {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state) {
        switch (state) {
            case PlayModeStateChange.ExitingEditMode:
                ResetSuppressionState();
                EditorApplication.update -= MonitorPlayMode;
                EditorApplication.update += MonitorPlayMode;
                StopDisconnectedTransports();
                break;
            case PlayModeStateChange.EnteredPlayMode:
                StopDisconnectedTransports();
                break;
            case PlayModeStateChange.ExitingPlayMode:
                EditorApplication.update -= MonitorPlayMode;
                break;
            case PlayModeStateChange.EnteredEditMode:
                ResetSuppressionState();
                break;
        }
    }

    static void StopDisconnectedTransports() {
        bool httpConnected = StopIfDisconnected(TransportMode.Http, ref httpSuppressed);
        bool stdioConnected = StopIfDisconnected(TransportMode.Stdio, ref stdioSuppressed);
        if (!httpConnected && !stdioConnected) EditorApplication.update -= MonitorPlayMode;
    }

    static bool StopIfDisconnected(TransportMode mode, ref bool suppressed) {
        try {
            TransportManager manager = MCPServiceLocator.TransportManager;
            TransportState state = manager.GetState(mode);
            if (state != null && state.IsConnected) {
                suppressed = false;
                return true;
            }

            if (suppressed) return false;

            manager.ForceStop(mode);
            suppressed = true;
        }
        catch (System.Exception exception) {
            suppressed = true;
            Debug.LogWarning("MCP play-mode performance guard could not stop " + mode +
                " transport: " + exception.Message);
        }

        return false;
    }

    static void MonitorPlayMode() {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) {
            EditorApplication.update -= MonitorPlayMode;
            return;
        }

        StopDisconnectedTransports();
    }

    static void ResetSuppressionState() {
        httpSuppressed = false;
        stdioSuppressed = false;
    }
}
#endif
