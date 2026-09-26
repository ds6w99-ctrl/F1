using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.Win32;
using SystemCore.Core.Compatibility;
using Windows.UI.ViewManagement;

namespace SystemCore.App;

// UI capabilities stay in the WinUI adapter, not in the platform-independent Core.
public sealed class CompatibilityService
{
    public FeatureCapabilities Detect()
    {
        var version = Environment.OSVersion.Version;
        var architecture = RuntimeInformation.OSArchitecture;
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var edition = key?.GetValue("EditionID") as string;
        var editionInScope = CompatibilityRules.IsSupportedEdition(edition) &&
            key?.GetValue("InstallationType") as string == "Client";
        var candidate = CompatibilityRules.IsCandidate(version,
            architecture == Architecture.X64, editionInScope);
        var mica = IsMicaAvailable();
        return new(version, architecture.ToString(), candidate, mica,
            "Windows 11 23H2+ x64: Home / Pro / Enterprise / Education. " +
            "Technical eligibility is not certification or Microsoft servicing status.");
    }

    public bool IsMicaAvailable()
    {
        try
        {
            return CompatibilityRules.CanUseMica(MicaController.IsSupported(),
                new AccessibilitySettings().HighContrast, new UISettings().AdvancedEffectsEnabled);
        }
        catch (COMException) { return false; } // Optional visual API unavailable; use native solid background.
    }
}
