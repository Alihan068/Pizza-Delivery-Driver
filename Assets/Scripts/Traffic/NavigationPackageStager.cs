using System;
using System.Text;

/// <summary>
/// In-memory stage/activate/rollback boundary for a detached navigation module. It has no disk,
/// network, Steam, scene, or full-map loading behavior; a future creator-owned package host can
/// provide those concerns around this narrow module boundary.
/// </summary>
public sealed class NavigationPackageStager {
    readonly TrafficValidationLimits limits;
    MapNavigationDocument active;
    MapNavigationDocument staged;
    MapNavigationDocument rollbackDocument;

    /// <summary>Currently activated detached navigation, or null before the first activation.</summary>
    public MapNavigationDocument ActiveDocument {
        get {
            return TryDetach(active, out var detached, out _) ? detached : null;
        }
    }
    /// <summary>Whether a validated detached document is waiting for activation.</summary>
    public bool HasStagedDocument => staged != null;

    /// <summary>Creates a stager with explicit bounded import limits.</summary>
    public NavigationPackageStager(TrafficValidationLimits limits = null) { this.limits = limits ?? new TrafficValidationLimits(); }

    /// <summary>Stages a document after bounded decode, external hash verification and dependency resolution.</summary>
    public bool TryStage(byte[] utf8, NavigationModuleManifest manifest, string expectedHash, ITrafficProfileCatalog catalog, out string reason) {
        reason = null;
        if (manifest == null || manifest.schemaVersion != NavigationDocumentCodec.CurrentSchemaVersion) { reason = "Unsupported or missing navigation module manifest."; return false; }
        if (string.IsNullOrEmpty(expectedHash)) { reason = "An external expected navigation hash is required."; return false; }
        if (!NavigationDocumentCodec.TryDecode(utf8, limits, out var decoded, out reason)) return false;
        if (decoded.mapId != manifest.mapId || decoded.documentId != manifest.documentId) { reason = "Navigation identity does not match the module manifest."; return false; }
        if (!NavigationDocumentCodec.TryEncode(decoded, limits, out _, out var actualHash, out reason)) return false;
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(actualHash, manifest.canonicalHash, StringComparison.OrdinalIgnoreCase)) { reason = "Navigation hash does not match the external expected hash or manifest descriptor."; return false; }
        if (catalog == null) { reason = "A typed traffic profile catalog is required."; return false; }
        if (!(catalog is ITrafficDependencyFingerprintResolver fingerprintResolver) ||
            !NavigationDependencyValidation.TryValidate(decoded, manifest, catalog,
                fingerprintResolver, limits, out reason))
            return false;
        if (!TryDetach(decoded, out staged, out reason)) return false;
        return true;
    }

    /// <summary>Activates the staged detached document without changing the previous active document on failure.</summary>
    public bool TryActivate(out string reason) {
        reason = null;
        if (staged == null) { reason = "No staged navigation document is available."; return false; }
        rollbackDocument = active;
        active = staged;
        staged = null;
        return true;
    }

    /// <summary>Restores the document that was active immediately before the last successful activation.</summary>
    public bool TryRollback(out string reason) {
        reason = null;
        if (rollbackDocument == null) { reason = "No previous navigation document is available for rollback."; return false; }
        active = rollbackDocument;
        rollbackDocument = null;
        return true;
    }

    static bool TryDetach(MapNavigationDocument source, out MapNavigationDocument detached, out string reason) {
        detached = null; reason = null;
        if (!NavigationDocumentCodec.TryEncode(source, null, out var json, out _, out reason)) return false;
        if (!NavigationDocumentCodec.TryDecode(Encoding.UTF8.GetBytes(json), null, out detached, out reason)) return false;
        return true;
    }

}
