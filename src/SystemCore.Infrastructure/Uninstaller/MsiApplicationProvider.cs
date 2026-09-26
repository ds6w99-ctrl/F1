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
            rows.Add(new(identity.Resolve(ApplicationType.Msi, null, product, null), name, Property("Publisher"), Property("VersionString"),
                null, Property("InstallLocation"), null, null, ApplicationType.Msi, null, false, product, null, null, null, Property("ProductIcon"),
                false, false, false, installed, installed ? null : "MSI product is not installed"));
        }
        failures.Add("Windows Installer inventory incomplete");
        return new InventoryResult(rows.AsReadOnly(), false, failures.Distinct().ToArray());
    }, token);

    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiEnumProductsExW(string? product, string? userSid, uint contexts, uint index, StringBuilder productCode, out uint context, StringBuilder sid, ref uint sidLength);
    [DllImport("msi.dll", CharSet = CharSet.Unicode)] private static extern uint MsiGetProductInfoExW(string productCode, string? sid, uint context, string property, StringBuilder? value, ref uint length);
}
