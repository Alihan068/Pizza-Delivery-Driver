using System;
using System.IO;

/// <summary>
/// Resolves the bounded S12 development-only career profile opt-in without touching disk.
/// </summary>
/// <remarks>
/// The resolver is deliberately pure: command-line parsing and path derivation are performed
/// before any career service is constructed or any save file is read. Directory creation is a
/// separate operation and is allowed only for a valid opt-in profile.
/// </remarks>
public readonly struct DevelopmentTestProfile {
    /// <summary>Exact command-line switch that enables one bounded S12 profile.</summary>
    public const string ArgumentName = "-pizzaTestProfile";
    /// <summary>Fixed child directory below the Unity persistent-data root.</summary>
    public const string ProfileDirectoryName = "S12TestProfiles";

    /// <summary>True when the exact opt-in switch was present.</summary>
    public readonly bool IsRequested;
    /// <summary>True only when the requested switch carried one valid bounded profile.</summary>
    public readonly bool IsValid;
    /// <summary>Normalized 32-hex profile identifier, or empty when inactive/invalid.</summary>
    public readonly string GuidText;
    /// <summary>Validated profile directory, or empty when inactive/invalid.</summary>
    public readonly string RootPath;

    DevelopmentTestProfile(bool isRequested, bool isValid, string guidText, string rootPath) {
        IsRequested = isRequested;
        IsValid = isValid;
        GuidText = guidText;
        RootPath = rootPath;
    }

    /// <summary>
    /// Resolves the S12 profile flag only in a development player. In the Unity Editor and in
    /// non-development players, the result is inactive even if command-line text is present.
    /// </summary>
    /// <param name="arguments">The process arguments to inspect.</param>
    /// <param name="persistentDataPath">The Unity persistent data root used for bounded derivation.</param>
    /// <returns>An inactive result, a valid isolated result, or a requested-invalid result.</returns>
    public static DevelopmentTestProfile Resolve(string[] arguments, string persistentDataPath) {
#if DEVELOPMENT_BUILD && !UNITY_EDITOR
        return ResolveArguments(arguments, persistentDataPath);
#else
        return new DevelopmentTestProfile(false, true, string.Empty, string.Empty);
#endif
    }

    /// <summary>
    /// Pure development-argument resolver used by bounded tests and by the player-only wrapper.
    /// It performs no directory or file I/O and always derives a root below the supplied
    /// persistent-data root.
    /// </summary>
    public static DevelopmentTestProfile ResolveArguments(string[] arguments, string persistentDataPath) {
        return ResolveArgumentsCore(arguments, persistentDataPath);
    }

    /// <summary>
    /// Creates the already-validated profile directory. It refuses inactive, invalid, or
    /// out-of-bound results, so a caller cannot turn this helper into an arbitrary directory writer.
    /// </summary>
    /// <returns>True when the profile root exists after the call.</returns>
    public bool EnsureDirectory() {
        if (!IsRequested || !IsValid || string.IsNullOrEmpty(RootPath)) return false;
        try {
            Directory.CreateDirectory(RootPath);
            return Directory.Exists(RootPath);
        }
        catch (Exception) {
            return false;
        }
    }

    static DevelopmentTestProfile ResolveArgumentsCore(string[] arguments, string persistentDataPath) {
        if (arguments == null)
            return new DevelopmentTestProfile(false, true, string.Empty, string.Empty);

        int flagIndex = -1;
        int occurrences = 0;
        for (int i = 0; i < arguments.Length; i++) {
            if (!string.Equals(arguments[i], ArgumentName, StringComparison.Ordinal)) continue;
            flagIndex = i;
            occurrences++;
        }

        if (occurrences == 0)
            return new DevelopmentTestProfile(false, true, string.Empty, string.Empty);

        if (occurrences != 1 || flagIndex + 1 >= arguments.Length ||
            string.Equals(arguments[flagIndex + 1], ArgumentName, StringComparison.Ordinal))
            return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);

        if (string.IsNullOrEmpty(persistentDataPath))
            return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);

        try {
            string rawGuid = arguments[flagIndex + 1];
            if (!Guid.TryParseExact(rawGuid, "N", out Guid parsedGuid))
                return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);

            string guidText = parsedGuid.ToString("N").ToLowerInvariant();
            string dataRoot = Path.GetFullPath(persistentDataPath);
            string root = Path.GetFullPath(Path.Combine(dataRoot, ProfileDirectoryName, guidText));
            if (!IsContainedPath(dataRoot, root))
                return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);

            return new DevelopmentTestProfile(true, true, guidText, root);
        }
        catch (ArgumentException) {
            return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);
        }
        catch (NotSupportedException) {
            return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);
        }
        catch (PathTooLongException) {
            return new DevelopmentTestProfile(true, false, string.Empty, string.Empty);
        }
    }

    /// <summary>Returns true when a candidate path is inside the specified root.</summary>
    public static bool IsContainedPath(string rootPath, string candidatePath) {
        if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(candidatePath)) return false;
        try {
            string root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                          Path.DirectorySeparatorChar;
            string candidate = Path.GetFullPath(candidatePath);
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) {
            return false;
        }
        catch (NotSupportedException) {
            return false;
        }
        catch (PathTooLongException) {
            return false;
        }
    }
}
