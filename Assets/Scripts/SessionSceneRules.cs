using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Builds detached scene rules and validates explicit traffic bindings without scene mutation.</summary>
public static class SessionSceneRules {
    /// <summary>Resolves the sole opt-in host inside the given scene; multiple hosts are rejected as ambiguous.</summary>
    public static TrafficSessionHost ResolveHost(Scene scene) {
        var host = ResolveComponent<TrafficSessionHost>(scene);
        return host != null && host.isActiveAndEnabled ? host : null;
    }

    /// <summary>Resolves an unambiguous typed dependency within one scene only, never another loaded scene.</summary>
    public static T ResolveComponent<T>(Scene scene, bool includeInactive = false) where T : Component {
        T result = null;
        foreach (var root in scene.GetRootGameObjects()) foreach (var component in root.GetComponentsInChildren<T>(includeInactive)) {
            if (result != null) throw new InvalidOperationException("Ambiguous scene dependency: " + typeof(T).Name);
            result = component;
        }
        return result;
    }

    /// <summary>Captures resolved values; an explicitly supplied editor-test difficulty is validated and always makes the run ineligible.</summary>
    public static TrafficSessionContext Create(GameManager manager, MapData map, bool explicitBinding, string sceneName, string editorTestDifficultyId = null) {
        if (!explicitBinding && manager != null) map = manager.currentMap;
        var resolution = ModifierSelectionResolver.Resolve(manager != null ? manager.allModifiers : null,
            manager != null ? manager.SelectedModifierIds : null);
        var modifiers = new FrozenModifierRules(resolution.resolvedModifiers);
        bool testContext = !string.IsNullOrEmpty(editorTestDifficultyId);
        string difficultyId = manager != null && map == manager.currentMap && manager.CurrentDifficulty != null ? manager.CurrentDifficulty.difficultyId : string.Empty;
        if (testContext) difficultyId = map != null && map.sceneName == sceneName && map.levelData != null &&
            map.levelData.GetDifficulty(editorTestDifficultyId) != null ? editorTestDifficultyId : string.Empty;
        var draft = new SessionSetupDraft(Guid.NewGuid().ToString(), map != null ? map.mapId : string.Empty,
            manager != null && manager.currentVehicle != null ? manager.currentVehicle.vehicleId : string.Empty,
            difficultyId,
            manager != null ? manager.SelectedShiftDurationMinutes : 1, manager != null && manager.IsFreeplayMode,
            manager != null ? manager.SelectedModifierIds : null);
        var context = new TrafficSessionContext(draft, modifiers);
        if (S12BenchmarkGate.Requested)
            context.Integrity.MarkInvalid("S12 stationary benchmark; not a competitive career run.");
        if (testContext) context.Integrity.MarkInvalid("Authored editor traffic test context; not a career run.");
        if (resolution.rejectedIds.Count > 0) context.Integrity.MarkInvalid("Modifier selection contains unresolved identities.");
        foreach (var modifier in resolution.resolvedModifiers)
            if (!modifier.HasValidScoreMultiplier()) context.Integrity.MarkInvalid("Modifier score multiplier is invalid.");
        return context;
    }

    /// <summary>Rejects missing/mismatched bindings and malformed navigation; unsupported maps remain traffic-free.</summary>
    public static MapNavigationDocument ValidateNavigation(MapData map, string sceneName,
        TrafficValidationLimits limits, SessionIntegrityState integrity) {
        if (map == null || string.IsNullOrEmpty(map.mapId) || map.sceneName != sceneName) {
            integrity.MarkInvalid("Missing or mismatched explicit map binding.");
            return null;
        }
        if (map.trafficMapData == null) return null;
        var document = map.trafficMapData.ResolveDocument();
        if (document == null || document.mapId != map.mapId || string.IsNullOrEmpty(document.documentId)) {
            integrity.MarkInvalid("Navigation identity does not match the authored map.");
            return null;
        }
        var issues = TrafficMapValidator.Validate(document, limits);
        foreach (var issue in issues) integrity.MarkInvalid(issue.message);
        return issues.Count == 0 ? document : null;
    }

    /// <summary>Freezes capability flags only after validation; missing traffic data never enables traffic.</summary>
    public static void Freeze(TrafficSessionContext context, bool navigationReady, string documentId = null,
        SessionContentEvidence contentEvidence = null) {
        if (context == null) return;
        var draft = context.Draft;
        var modifiers = context.Modifiers;
        SessionContentEvidence acceptedEvidence = context.ContentEvidence;
        if (acceptedEvidence != null && contentEvidence != null &&
            !ReferenceEquals(acceptedEvidence, contentEvidence)) {
            context.Integrity.MarkInvalid("Rejected content evidence did not match the accepted session evidence.");
        }
        if (acceptedEvidence == null) {
            SessionContentEvidence candidate = contentEvidence ?? SessionContentEvidence.CreateMinimal(draft, modifiers);
            if (!context.CaptureContentEvidence(candidate)) {
                context.CaptureContentEvidence(SessionContentEvidence.CreateMinimal(draft, modifiers));
            }
            acceptedEvidence = context.ContentEvidence;
        }
        if (acceptedEvidence == null) return;
        context.FreezeSnapshot(new SessionRulesSnapshot(draft.sessionId, draft.mapId, draft.vehicleId,
            draft.difficultyId, draft.shiftDurationMinutes, draft.isFreeplay, 0, modifiers.Ids,
            modifiers.ScoreMultiplier, navigationReady && !modifiers.DisablesCivilianTraffic,
            navigationReady && !modifiers.DisablesPolice, documentId, modifiers, acceptedEvidence));
    }
}
