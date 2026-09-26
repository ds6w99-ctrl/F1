namespace SystemCore.Core.Compatibility;

public sealed record FeatureCapabilities(
    Version WindowsVersion,
    string Architecture,
    bool InCandidateScope,
    bool MicaAvailable,
    string ScopeExplanation);

public static class CompatibilityRules
{
    public const int MinimumBuild = 22631; // Windows 11 23H2.

    // Technical scope is distinct from Microsoft servicing and tested product coverage.
    public static bool IsCandidate(Version version, bool x64, bool workstation) =>
        x64 && workstation && version.Major == 10 && version.Minor == 0 &&
        version.Build >= MinimumBuild;

    public static bool IsSupportedEdition(string? edition) => edition is
        "Core" or "CoreN" or "CoreSingleLanguage" or "CoreCountrySpecific" or
        "Professional" or "ProfessionalN" or "Enterprise" or "EnterpriseN" or
        "Education" or "EducationN";

    public static bool CanUseMica(bool apiSupported, bool highContrast, bool effectsEnabled) =>
        apiSupported && !highContrast && effectsEnabled;
}
