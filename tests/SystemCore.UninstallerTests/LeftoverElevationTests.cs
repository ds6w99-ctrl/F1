using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Uninstaller;

// Read-only checks for the elevated filesystem broker. Nothing here launches the helper or
// touches a protected root: the helper is only reachable through UAC, which the harness
// cannot exercise. What is verified is the closed validation gate the helper runs first.
static class LeftoverElevationTests
{
    public static Task Run(Action<string, bool> check)
    {
        var program = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var now = DateTimeOffset.UtcNow;
        var nonce = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var identity = new CleanerIdentity(7, 42, 4096, now.UtcDateTime.ToFileTimeUtc(), 1, now.UtcDateTime.ToFileTimeUtc());
        var root = Path.Combine(program, "SystemCoreFixture");
        var file = Path.Combine(root, "app.exe");

        check("Elevated root is an admin-only tree", LeftoverSafety.IsElevatedRoot(root) && LeftoverSafety.IsElevatedRoot(Path.Combine(common, "SystemCoreFixture")));
        check("User-writable and system roots are never elevated", !LeftoverSafety.IsElevatedRoot(Path.Combine(local, "SystemCoreFixture"))
            && !LeftoverSafety.IsElevatedRoot(windows) && !LeftoverSafety.IsElevatedRoot(program));

        ElevatedLeftoverRequest Build(params ElevatedLeftoverItem[] items) => new(LeftoverElevationProtocol.Version, nonce, Guid.NewGuid(), now,
            "fixture", "SystemCoreFixture", "SystemCore tests", null, ItemKind.Application, items);

        var valid = Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, file, root, identity));
        check("Well-formed admin file item validates", LeftoverElevationProtocol.Validate(valid, nonce, now) && LeftoverElevationProtocol.FitsPayload(valid));
        var directoryRoot = Path.Combine(programX86, "SystemCoreFixture", "bin");
        check("Admin directory item validates", LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.Directory, directoryRoot, Path.Combine(programX86, "SystemCoreFixture"), identity)), nonce, now));
        check("ProgramData root validates", LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(common, "SystemCoreFixture", "data.bin"), Path.Combine(common, "SystemCoreFixture"), identity)), nonce, now));

        check("Null request rejected", !LeftoverElevationProtocol.Validate(null, nonce, now));
        check("Nonce mismatch rejected", !LeftoverElevationProtocol.Validate(valid, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), now));
        check("Short nonce rejected", !LeftoverElevationProtocol.Validate(valid, "abcd", now));
        check("Empty batch rejected", !LeftoverElevationProtocol.Validate(Build(), nonce, now));
        check("Stale scan rejected", !LeftoverElevationProtocol.Validate(valid with { ScannedAt = now - TimeSpan.FromMinutes(11) }, nonce, now));
        check("Future scan rejected", !LeftoverElevationProtocol.Validate(valid with { ScannedAt = now + TimeSpan.FromMinutes(1) }, nonce, now));
        check("Wrong version rejected", !LeftoverElevationProtocol.Validate(valid with { Version = 2 }, nonce, now));
        check("Empty receipt rejected", !LeftoverElevationProtocol.Validate(valid with { ReceiptId = Guid.Empty }, nonce, now));
        check("Registry items are never elevated here", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.RegistryKey, file, root, identity)), nonce, now));
        check("Hard-linked item rejected", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, file, root, identity with { Links = 2 })), nonce, now));
        var first = Guid.NewGuid();
        check("Duplicate item ids rejected", !LeftoverElevationProtocol.Validate(Build(
            new ElevatedLeftoverItem(first, LeftoverType.File, file, root, identity),
            new ElevatedLeftoverItem(first, LeftoverType.File, Path.Combine(root, "b.exe"), root, identity)), nonce, now));
        check("Duplicate paths rejected", !LeftoverElevationProtocol.Validate(Build(
            new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, file, root, identity),
            new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, file, root, identity)), nonce, now));
        check("Path escape rejected", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(root, "..", "..", "Windows", "x.dll"), root, identity)), nonce, now));
        check("Root outside the item rejected", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(common, "Other", "x.dll"), root, identity)), nonce, now));
        check("User-writable root rejected", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(local, "SystemCoreFixture", "app.exe"), Path.Combine(local, "SystemCoreFixture"), identity)), nonce, now));
        check("Mixed batch with a user root rejected", !LeftoverElevationProtocol.Validate(Build(
            new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, file, root, identity),
            new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(local, "SystemCoreFixture", "app.exe"), Path.Combine(local, "SystemCoreFixture"), identity)), nonce, now));
        check("Shared-name root rejected", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(program, "Microsoft", "x.dll"), Path.Combine(program, "Microsoft"), identity)), nonce, now));
        check("Protected subtree rejected", !LeftoverElevationProtocol.Validate(
            Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(program, "WindowsApps", "x.dll"), Path.Combine(program, "WindowsApps"), identity)), nonce, now));
        var tooMany = Enumerable.Range(0, LeftoverElevationProtocol.MaximumItems + 1)
            .Select(i => new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(root, i + ".exe"), root, identity)).ToArray();
        check("Oversized batch rejected", !LeftoverElevationProtocol.Validate(Build(tooMany), nonce, now));

        // Execution re-runs the same gate: a refused request never mutates anything and never
        // reports success for it.
        var refused = LeftoverElevationProtocol.Execute(Build(new ElevatedLeftoverItem(Guid.NewGuid(), LeftoverType.File, Path.Combine(local, "SystemCoreFixture", "app.exe"), Path.Combine(local, "SystemCoreFixture"), identity)), nonce, default);
        check("Refused execution performs no work", !refused.Accepted && refused.Items.Length == 0);
        return Task.CompletedTask;
    }
}
