using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemCore.Core.Cleaner;

namespace SystemCore.Infrastructure.Cleaner;

public sealed record ElevatedCleanerItem(Guid Id, CleanerCategory Category, string RelativePath, CleanerIdentity Identity);
public sealed record ElevatedCleanerRequest(int Version, string Nonce, Guid SessionId, DateTimeOffset ScannedAt, ElevatedCleanerItem[] Items);
public sealed record ElevatedCleanerResponse(int Version, bool Accepted, CleanerItemResult[] Items);

public static class CleanerElevationProtocol
{
    public const int Version = 1;
    public const int MaximumItems = 2000;
    public const int MaximumPayload = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12 };
    public static bool FitsPayload(ElevatedCleanerRequest request) => JsonSerializer.SerializeToUtf8Bytes(request, Json).Length <= MaximumPayload;
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        if (bytes.Length > MaximumPayload) throw new InvalidDataException("Payload limit");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token); await stream.WriteAsync(bytes, token); await stream.FlushAsync(token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, token);
        var size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size is <= 0 or > MaximumPayload) throw new InvalidDataException("Payload limit");
        var bytes = new byte[size]; await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes, Json) ?? throw new InvalidDataException("Missing request");
    }
    public static bool Validate(ElevatedCleanerRequest? request, string? nonce, string trustedRoot, DateTimeOffset now)
    {
        if (request is null || nonce is not { Length: 64 } || !nonce.All(char.IsAsciiHexDigit) || request.Version != Version || request.Nonce != nonce ||
            request.SessionId == Guid.Empty || request.ScannedAt > now || now - request.ScannedAt > TimeSpan.FromMinutes(10) ||
            request.Items is null || request.Items.Length is 0 or > MaximumItems) return false;
        var ids = new HashSet<Guid>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var item in request.Items)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Category != CleanerCategory.WindowsTemp ||
                string.IsNullOrWhiteSpace(item.RelativePath) || item.RelativePath.Length > CleanerSafety.MaximumPathLength || Path.IsPathRooted(item.RelativePath) ||
                item.RelativePath.Contains(':') || item.Identity is null || item.Identity.Links != 1 || item.Identity.FileId == 0 ||
                item.Identity.CreationFileTime <= 0 || !CleanerSafety.Eligible(item.Identity.Size, item.Identity.LastWriteFileTime, now, TimeSpan.FromHours(24))) return false;
            var full = CleanerSafety.Canonical(Path.Combine(trustedRoot, item.RelativePath));
            if (full is null || !CleanerSafety.IsChild(trustedRoot, full) || !paths.Add(full) ||
                !CleanerSafety.TryAddSize(total, item.Identity.Size, out total)) return false;
        }
        return true;
    }
    public static ElevatedCleanerResponse Execute(ElevatedCleanerRequest request, string nonce, CancellationToken token)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var target = new CleanerTarget(CleanerCategory.WindowsTemp, root, "*", true, TimeSpan.FromHours(24), false, true, false);
        return ExecuteAtTrustedTarget(request, nonce, target, token);
    }
    // Friend-test entry point. Not selected by any argument, environment variable or IPC field.
    internal static ElevatedCleanerResponse ExecuteAtTrustedTarget(ElevatedCleanerRequest request, string nonce, CleanerTarget target, CancellationToken token)
    {
        if (!WindowsCleanerTargets.IsAllowed(target) || !Validate(request, nonce, target.Root, DateTimeOffset.UtcNow)) return new(Version, false, []);
        var items = request.Items.Select(i => new CleanerItem(i.Id, target.Category, i.RelativePath, Path.Combine(target.Root, i.RelativePath),
            i.Identity.Size, i.Identity.LastWriteFileTime, i.Identity)).ToArray();
        var result = WindowsCleanerService.ExecuteValidated(items, [target], token, allowWindowsTemp: true);
        return new(Version, result.Accepted, result.Items.ToArray());
    }
}
