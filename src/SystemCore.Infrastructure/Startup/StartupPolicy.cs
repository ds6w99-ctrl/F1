using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Infrastructure.Uninstaller;

namespace SystemCore.Infrastructure.Startup;

public sealed class StartupIdentityResolver : IStartupIdentityResolver
{
    public string Resolve(string sourceIdentity) => Hash(sourceIdentity.ToUpperInvariant());
    internal static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
internal sealed record StartupCommand(string? Executable, string? Arguments, string? Publisher);
internal static class StartupCommandParser
{
    internal static StartupCommand Read(string? command)
    {
        // Parsing is display-only. Shell hosts/ambiguous/missing targets remain
        // unknown and protected. Nothing in this module starts the command.
        if (command is null || command.Contains('^')) return new(null, null, null);
        var parsed = UninstallCommandParser.Parse(command);
        if (parsed is null) return new(null, null, null);
        string? publisher = null;
        try
        {
            using var image = PinnedCleanerPath.Open(parsed.Executable, false, shareRead: true);
            publisher = FileVersionInfo.GetVersionInfo(parsed.Executable).CompanyName;
            if (string.IsNullOrWhiteSpace(publisher)) publisher = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { return new(null, null, null); }
        return new(parsed.Executable, string.Join(" ", parsed.Arguments.Select(a => a.Contains(' ') ? "\"" + a.Replace("\"", "\\\"") + "\"" : a)), publisher);
    }
}
public sealed class StartupSafetyClassifier : IStartupSafetyClassifier
{
    public StartupProtection Classify(StartupItem item)
    {
        if (item.ProtectionReason != StartupProtection.None) return item.ProtectionReason;
        if (item.ExecutablePath is null) return StartupProtection.UnknownCommand;
        var path = CleanerSafety.Canonical(item.ExecutablePath);
        if (path is null) return StartupProtection.UnsafePath;
        if (CleanerSafety.IsChild(Environment.GetFolderPath(Environment.SpecialFolder.Windows), path)
            || (item.Publisher?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ?? false)) return StartupProtection.SystemComponent;
        var description = item.Name + " " + item.Publisher + " " + path;
        if (new[] { "defender", "security", "antivirus", "anti-virus", "antimalware", "driver", "kaspersky", "mcafee", "avast", "bitdefender", "crowdstrike", "sentinelone", "sophos", "eset", "norton",
            "nvidia", "intel corporation", "advanced micro devices", "realtek", "qualcomm", "synaptics" }
            .Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase))) return StartupProtection.SecurityOrDriver;
        return StartupProtection.None;
    }
    internal StartupItem Apply(StartupItem item)
    {
        var reason = Classify(item);
        return item with { IsProtected = reason != StartupProtection.None, ProtectionReason = reason,
            Status = reason != StartupProtection.None ? StartupStatus.Protected : item.Status };
    }
}
