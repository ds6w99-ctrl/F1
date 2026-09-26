using System.Diagnostics;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure;

// Caller selects only one of the two closed operations. Installation-relative
// payload is pinned across dispatch; cwd, PATH and environment overrides are unused.
internal sealed class HelperLaunchLease : IDisposable
{
    private readonly PinnedCleanerPath executable;
    internal string Path { get; }
    internal HelperLaunchLease() : this(AppContext.BaseDirectory) { }
    internal HelperLaunchLease(string applicationDirectory)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetFullPath(applicationDirectory), "ElevatedHost", "SystemCore.ElevatedHost.exe");
        executable = PinnedCleanerPath.Open(Path, false, shareRead: true);
        if (executable.Identity.Links != 1) { executable.Dispose(); throw new IOException("Helper identity unavailable"); }
    }
    internal ProcessStartInfo StartInfo(bool startup, string? pipe, int parent, string? nonce)
    {
        var prefix = startup ? "SystemCore-Startup-" : "SystemCore-Cleaner-";
        if (pipe is null || !pipe.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(pipe[prefix.Length..], "N", out _) || parent <= 0
            || nonce is not { Length: 64 } || !nonce.All(char.IsAsciiHexDigit)) throw new IOException("Invalid helper launch identity");
        var info = new ProcessStartInfo(Path) { UseShellExecute = true, Verb = "runas", WorkingDirectory = System.IO.Path.GetDirectoryName(Path)! };
        info.ArgumentList.Add(startup ? "--startup" : "--cleaner");
        info.ArgumentList.Add(pipe); info.ArgumentList.Add(parent.ToString(System.Globalization.CultureInfo.InvariantCulture)); info.ArgumentList.Add(nonce);
        return info;
    }
    public void Dispose() => executable.Dispose();
}
