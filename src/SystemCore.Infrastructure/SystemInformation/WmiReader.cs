using System.Management;
using SystemCore.Core.SystemInformation;

namespace SystemCore.Infrastructure.SystemInformation;

internal static class WmiReader
{
    // Bound concurrent native work even if a timed-out caller stops waiting for WMI.
    private static readonly SemaphoreSlim Slots = new(3);
    internal static async Task<IReadOnlyList<T>> Query<T>(string scope, string query, Func<ManagementBaseObject, T> map, CancellationToken token)
    {
        await Slots.WaitAsync(token).ConfigureAwait(false);
        return await Task.Run(() =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using var searcher = new ManagementObjectSearcher(new ManagementScope(scope), new ObjectQuery(query),
                    new System.Management.EnumerationOptions { ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(5) });
                using var results = searcher.Get();
                var items = new List<T>();
                foreach (ManagementBaseObject row in results)
                {
                    using (row) { token.ThrowIfCancellationRequested(); items.Add(map(row)); }
                }
                return (IReadOnlyList<T>)items.AsReadOnly();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Trace.TraceWarning("WMI read failed: {0} (0x{1:X8})", ex.GetType().Name, ex.HResult);
                throw;
            }
            finally { Slots.Release(); }
        }).ConfigureAwait(false);
    }
    internal static string? Text(ManagementBaseObject row, string key) => InfoFormatting.Clean(row[key]?.ToString());
    internal static uint? Number(ManagementBaseObject row, string key) => uint.TryParse(row[key]?.ToString(), out var n) ? n : null;
    internal static ulong? Bytes(ManagementBaseObject row, string key) => ulong.TryParse(row[key]?.ToString(), out var n) && n > 0 ? n : null;
    internal static DateTimeOffset? Date(ManagementBaseObject row, string key)
    {
        var text = Text(row, key);
        if (text is null || text.Contains('*')) return null;
        try { return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(text)); }
        // A malformed WMI date must degrade to "unknown", never fail the whole section.
        catch (Exception ex) when (ex is ArgumentException or FormatException) { return null; }
    }
}
