using UnityEngine;

/// <summary>
/// Single source of truth for the game version.
///
/// Versioning scheme: MAJOR.MINOR.PATCH
///   MAJOR — milestone releases (1.0 = full release)
///   MINOR — feature-complete milestones (0.4 = demo, 0.5 = vertical slice, etc.)
///   PATCH — bug fixes and polish within a milestone
///
/// History:
///   v1.0–v3.0  — early prototypes (pre-versioning)
///   v0.4.0     — demo build (single day, Paris date, apartment cleanup)
///   v0.5.0     — juice pass (feel, audio, UI polish, placement improvements)
/// </summary>
public static class GameVersion
{
    public const int Major = 0;
    public const int Minor = 5;
    public const int Patch = 0;

    public const string Label = ""; // e.g. "alpha", "beta", "rc1", ""

    public static string Full => string.IsNullOrEmpty(Label)
        ? $"{Major}.{Minor}.{Patch}"
        : $"{Major}.{Minor}.{Patch}-{Label}";

    /// <summary>Short display version for UI (e.g. "v0.4").</summary>
    public static string Short => $"v{Major}.{Minor}";

    /// <summary>Full display version for UI (e.g. "v0.4.0" or "v0.4.0-alpha").</summary>
    public static string Display => $"v{Full}";
}
