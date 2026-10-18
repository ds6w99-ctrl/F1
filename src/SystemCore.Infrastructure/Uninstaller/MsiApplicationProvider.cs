using System.Runtime.InteropServices;
using System.Text;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class MsiApplicationProvider(IApplicationIdentityResolver identity) : IMsiApplicationProvider
{
    public Task<InventoryResult> ReadAsync(CancellationToken token) => Task.Run(() =>
    {
        var rows = new List<InstalledApplication>();
        var failures = new List<string>();
        for (uint index = 0; index < 10000; index++)
        {
            token.ThrowIfCancellationRequested();
            var code = new StringBuilder(39);
            var sid = new StringBuilder(256); uint sidLength = 256;
            var result = MsiEnumProductsExW(null, null, 7, index, code, out var context, sid, ref sidLength);
            if (result == 259) return new InventoryResult(rows.AsReadOnly(), failures.Count == 0, failures);
            if (result != 0) { failures.Add("Windows Installer enumeration unavailable"); break; }
            if (!Guid.TryParse(code.ToString(), out var product) || product == Guid.Empty) { failures.Add("Invalid MSI identity"); continue; }
            string? Property(string property)
            {
                uint length = 0;
                var owner = context == 4 ? null : sid.ToString();
                var error = MsiGetProductInfoExW(code.ToString(), owner, context, property, null, ref length);
                if (error is not 0 and not 234 || length > 32767) return null;
                var text = new StringBuilder((int)length + 1); length++;
                return MsiGetProductInfoExW(code.ToString(), owner, context, property, text, ref length) == 0 && text.Length > 0 ? text.ToString() : null;
            }
            var name = Property("InstalledProductName");
            if (name is null) { failures.Add("MSI product metadata unavailable"); continue; }
            // Native registration is authoritative. Registry restrictions are merged
            // conservatively by the inventory before enabling a user action.
            var installed = Property("State") == "5";
            var location = Property("InstallLocation") ?? ResolveComponentLocation(code.ToString(), sid, context, token);
            rows.Add(new(identity.Resolve(ApplicationType.Msi, null, product, null), name, Property("Publisher"), Property("VersionString"),
                null, location, null, null, ApplicationType.Msi, null, false, product, null, null, null, Property("ProductIcon"),
                false, false, false, installed, installed ? null : "MSI product is not installed"));
        }
        failures.Add("Windows Installer inventory incomplete");
        return new InventoryResult(rows.AsReadOnly(), false, failures.Distinct().ToArray());
    }, token);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiEnumProductsExW(string? product, string? userSid, uint contexts, uint index, StringBuilder productCode, out uint context, StringBuilder sid, ref uint sidLength);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiGetProductInfoExW(string productCode, string? sid, uint context, string property, StringBuilder? value, ref uint length);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiEnumComponentsW(uint index, StringBuilder componentCode);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiGetProductCodeW(string component, StringBuilder productCode);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern int MsiGetComponentPathExW(string productCode, string? userSid, uint context, string component, StringBuilder? path, ref uint pathLength);

    // Many MSI products (7-Zip is a common example) register no InstallLocation property.
    // Windows Installer does track every component path, so resolve the install location as the
    // deepest common directory of the product's component paths. Bounded and fail-closed:
    // the result must be a real directory that is not itself a protected/known root.
    private static string? ResolveComponentLocation(string productCode, StringBuilder userSid, uint context, CancellationToken token)
    {
        const uint ComponentLimit = 30000;
        var paths = new List<string>(16);
        var component = new StringBuilder(39);
        for (uint index = 0; index < ComponentLimit; index++)
        {
            token.ThrowIfCancellationRequested();
            if (MsiEnumComponentsW(index, component) != 0) break;
            var code = component.ToString();
            var product = new StringBuilder(39);
            if (MsiGetProductCodeW(code, product) != 0 || !string.Equals(product.ToString(), productCode, StringComparison.OrdinalIgnoreCase)) continue;
            var owner = context == 4 ? null : userSid.ToString();
            uint length = 0;
            var sizeState = MsiGetComponentPathExW(productCode, owner, context, code, null, ref length);
            if (length == 0 || (sizeState < 0 && sizeState != 234)) continue;
            var text = new StringBuilder((int)length + 1);
            var state = MsiGetComponentPathExW(productCode, owner, context, code, text, ref length);
            if (state < 0 || text.Length == 0) continue;
            paths.Add(text.ToString());
            if (paths.Count >= 64) break;
        }
        if (paths.Count == 0) return null;
        var common = Path.GetDirectoryName(paths[0]) ?? "";
        foreach (var path in paths.Skip(1))
        {
            var directory = Path.GetDirectoryName(path) ?? "";
            while (common.Length > 0 && !string.Equals(directory, common, StringComparison.OrdinalIgnoreCase)
                && !directory.StartsWith(common.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                common = Path.GetDirectoryName(common) ?? "";
            if (common.Length == 0) break;
        }
        if (common.Length <= 3 || !Directory.Exists(common)) return null;
        var roots = new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData }
            .Select(Environment.GetFolderPath).Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase);
        if (roots.Contains(common, StringComparer.OrdinalIgnoreCase)) return null;
        return common;
    }
}
