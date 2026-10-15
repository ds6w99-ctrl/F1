using System.Buffers.Binary;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using SystemCore.Core.Cleaner;
using SystemCore.Core.Uninstaller;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Uninstaller;

// Elevated filesystem leftover removal. Restricted to roots a standard user cannot
// mutate; registry leftovers are never routed here because the helper cannot repeat
// the receipt-verified "application is still absent" check independently.
public sealed record ElevatedLeftoverItem(Guid Id, LeftoverType Type, string Path, string Root, CleanerIdentity Identity);
public sealed record ElevatedLeftoverRequest(int Version, string Nonce, Guid ReceiptId, DateTimeOffset ScannedAt, string ApplicationId,
    string DisplayName, string? Publisher, string? InstallLocation, ItemKind Kind, ElevatedLeftoverItem[] Items);
public enum ElevatedLeftoverOutcome { Deleted, Changed, SafetyRejected, AccessDenied, InUse, AlreadyGone, Skipped, Failed, Cancelled }
public sealed record ElevatedLeftoverItemResult(Guid ItemId, ElevatedLeftoverOutcome Outcome, long RemovedBytes);
public sealed record ElevatedLeftoverResponse(int Version, bool Accepted, ElevatedLeftoverItemResult[] Items);

public static class LeftoverElevationProtocol
{
    public const int Version = 1;
    public const int MaximumItems = 2000;
    public const int MaximumPayload = 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new() { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 12 };
    public static bool FitsPayload(ElevatedLeftoverRequest request) => JsonSerializer.SerializeToUtf8Bytes(request, Json).Length <= MaximumPayload;
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
    public static bool Validate(ElevatedLeftoverRequest? request, string? nonce, DateTimeOffset now)
    {
        if (request is null || nonce is not { Length: 64 } || !nonce.All(char.IsAsciiHexDigit) || request.Version != Version || request.Nonce != nonce
            || request.ReceiptId == Guid.Empty || string.IsNullOrWhiteSpace(request.ApplicationId) || string.IsNullOrWhiteSpace(request.DisplayName)
            || request.ScannedAt > now || now - request.ScannedAt > TimeSpan.FromMinutes(10)
            || request.Items is null || request.Items.Length is 0 or > MaximumItems) return false;
        // The helper only holds the identity the scanner already proved; it re-applies the
        // same structural ownership rules rather than trusting the caller's judgement.
        var application = new InstalledApplication(request.ApplicationId, request.DisplayName, request.Publisher, null, null, request.InstallLocation,
            null, null, ApplicationType.Win32, null, false, null, null, null, null, null, false, false, false, true, null) { ItemKind = request.Kind };
        var ids = new HashSet<Guid>(); var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in request.Items)
        {
            if (item is null || item.Id == Guid.Empty || !ids.Add(item.Id) || item.Identity is null || item.Identity.Links != 1
                || item.Type is not (LeftoverType.File or LeftoverType.Directory)) return false;
            if (CleanerSafety.Canonical(item.Path) is not { } path || CleanerSafety.Canonical(item.Root) is not { } root) return false;
            if (root.Length <= 3 || (path != root && !CleanerSafety.IsChild(root, path))) return false;
            if (!LeftoverSafety.IsElevatedRoot(root) || !LeftoverSafety.CanOwnDirectory(application, root, [])) return false;
            if (!paths.Add(path)) return false;
        }
        return true;
    }
    public static ElevatedLeftoverResponse Execute(ElevatedLeftoverRequest request, string nonce, CancellationToken token)
    {
        if (!Validate(request, nonce, DateTimeOffset.UtcNow)) return new(Version, false, []);
        var results = new List<ElevatedLeftoverItemResult>(request.Items.Length);
        foreach (var item in request.Items)
        {
            if (token.IsCancellationRequested) { results.Add(new(item.Id, ElevatedLeftoverOutcome.Cancelled, 0)); continue; }
            try
            {
                var directory = item.Type == LeftoverType.Directory;
                using var pinned = PinnedCleanerPath.Open(item.Path, directory, true);
                var expected = item.Identity;
                var matches = directory
                    ? pinned.Identity.Volume == expected.Volume && pinned.Identity.FileId == expected.FileId && pinned.Identity.CreationFileTime == expected.CreationFileTime
                    : pinned.Identity == expected;
                if (!matches) { results.Add(new(item.Id, ElevatedLeftoverOutcome.Changed, 0)); continue; }
                token.ThrowIfCancellationRequested();
                if (directory) pinned.MarkEmptyDirectoryForDeletion(); else pinned.MarkFileForDeletion();
                results.Add(new(item.Id, ElevatedLeftoverOutcome.Deleted, directory ? 0 : pinned.Identity.Size));
            }
            catch (OperationCanceledException) { results.Add(new(item.Id, ElevatedLeftoverOutcome.Cancelled, 0)); }
            catch (UnauthorizedAccessException) { results.Add(new(item.Id, ElevatedLeftoverOutcome.AccessDenied, 0)); }
            catch (Win32Exception ex)
            {
                results.Add(new(item.Id, ex.NativeErrorCode switch { 2 or 3 => ElevatedLeftoverOutcome.AlreadyGone, 5 => ElevatedLeftoverOutcome.AccessDenied,
                    32 or 33 => ElevatedLeftoverOutcome.InUse, 145 => ElevatedLeftoverOutcome.Skipped, 6800 => ElevatedLeftoverOutcome.Changed,
                    _ => ElevatedLeftoverOutcome.Failed }, 0));
            }
            catch (IOException) { results.Add(new(item.Id, ElevatedLeftoverOutcome.SafetyRejected, 0)); }
        }
        return new(Version, true, results.ToArray());
    }
}
