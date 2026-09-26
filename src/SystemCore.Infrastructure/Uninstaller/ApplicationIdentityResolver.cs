using System.Security.Cryptography;
using System.Text;
using SystemCore.Core.Uninstaller;

namespace SystemCore.Infrastructure.Uninstaller;

public sealed class ApplicationIdentityResolver : IApplicationIdentityResolver
{
    public string Resolve(ApplicationType type, RegistryApplicationSource? source, Guid? productCode, string? packageFullName)
    {
        var key = type switch
        {
            ApplicationType.Msi when productCode is { } code => $"MSI:{code:D}",
            ApplicationType.Msix when !string.IsNullOrWhiteSpace(packageFullName) => $"MSIX:{packageFullName}",
            ApplicationType.Win32 when source is not null => $"REG:{source.Hive}:{source.View}:{source.KeyName}",
            _ => throw new ArgumentException("Insufficient application identity")
        };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant())));
    }
}
