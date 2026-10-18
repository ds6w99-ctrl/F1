using System.Runtime.InteropServices;
using System.Text;
using SystemCore.Core.Startup;
using SystemCore.Infrastructure.Cleaner;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Startup;

internal sealed record StartupFileSnapshot(StartupItem Item, StartupFolderLocation Folder, string Path, CleanerIdentity? FileIdentity);
public sealed class StartupFolderProvider : IStartupFolderProvider
{
    internal IStartupLocations Locations { get; }
    public StartupFolderProvider() : this(new WindowsStartupLocations()) { }
    internal StartupFolderProvider(IStartupLocations locations) => Locations = locations;
    // Same contract as the registry provider: Read is synchronous, so the lambda returns its result.
    public Task<StartupInventoryResult> ReadAsync(CancellationToken token) => Task.Run(() => Read(token).Result, token);
    internal (StartupInventoryResult Result, IReadOnlyList<StartupFileSnapshot> Snapshots) Read(CancellationToken token)
    {
        var items = new List<StartupFileSnapshot>(); var errors = new List<string>();
        foreach (var folder in Locations.Folders)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (string.IsNullOrWhiteSpace(folder.Path)) { errors.Add("StartupFolderUnavailable"); continue; }
                if (!Directory.Exists(folder.Path)) continue;
                using var directory = PinnedCleanerPath.Open(folder.Path, true);
                foreach (var path in Directory.EnumerateFiles(folder.Path))
                {
                    token.ThrowIfCancellationRequested();
                    if (items.Count >= 2000) { errors.Add("StartupFolderLimit"); break; }
                    if (Path.GetFileName(path).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var canonical = CleanerSafety.Canonical(path);
                    var protection = canonical is null ? StartupProtection.UnsafePath : StartupProtection.None;
                    CleanerIdentity? fileIdentity = null; var command = new StartupCommand(null, null, null); string display = "";
                    try
                    {
                        using var pinned = PinnedCleanerPath.Open(path, false, shareRead: true);
                        fileIdentity = pinned.Identity;
                        if (!CleanerSafety.SafeFileAttributes(pinned.Attributes, pinned.Identity.Links)) protection = StartupProtection.UnsafePath;
                        else if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            display = ReadShortcut(path);
                            command = StartupCommandParser.Read(display);
                        }
                        else if (Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) { display = "\"" + path + "\""; command = StartupCommandParser.Read(display); }
                        else protection = StartupProtection.UnsupportedSource;
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or COMException) { protection = StartupProtection.UnsafePath; }
                    var identity = $"Folder:{folder.Scope}:{canonical ?? path}";
                    var item = new StartupItem(new StartupIdentityResolver().Resolve(identity), Path.GetFileName(path), command.Publisher,
                        folder.Scope == StartupScope.CurrentUser ? StartupSourceType.StartupFolderUser : StartupSourceType.StartupFolderCommon,
                        path, display, command.Executable, command.Arguments, StartupStatus.Enabled, folder.Scope, folder.Scope == StartupScope.AllUsers,
                        false, protection, "NotMeasured", identity, StartupIdentityResolver.Hash(identity + "\0" + fileIdentity + "\0" + display));
                    items.Add(new(new StartupSafetyClassifier().Apply(item), folder, path, fileIdentity));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { errors.Add("StartupFolder:" + folder.Scope); }
        }
        return (new(Array.AsReadOnly(items.Select(i => i.Item).ToArray()), errors.Count == 0, errors.Distinct().ToArray()), items);
    }
    internal static string ReadShortcut(string path) => ReadShortcutMetadata(path).Command;
    internal static (string Command, string Icon, int Index) ReadShortcutMetadata(string path)
    {
        using var pinned = PinnedCleanerPath.Open(path, false, shareRead: true);
        var link = (IShellLinkW)Activator.CreateInstance(Type.GetTypeFromCLSID(new("00021401-0000-0000-C000-000000000046"))!)!;
        try
        {
            ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(path, 0);
            var target = new StringBuilder(512); var arguments = new StringBuilder(32768);
            link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetArguments(arguments, arguments.Capacity);
            // No Resolve call: do not contact remote targets or trigger shortcut repair.
            var icon = new StringBuilder(1024); link.GetIconLocation(icon, icon.Capacity, out var index);
            return ("\"" + target + "\" " + arguments, icon.ToString(), index);
        }
        finally { Marshal.FinalReleaseComObject(link); }
    }
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr data, uint flags);
        void GetIDList(out IntPtr id); void SetIDList(IntPtr id);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count); void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count); void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetHotkey(out short hotkey); void SetHotkey(short hotkey);
        void GetShowCmd(out int command); void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int count, out int index);
    }
}
