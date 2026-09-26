using System.Text.RegularExpressions;

namespace SystemCore.Core.Cleaner;

public static class CleanerSafety
{
    public const int MaximumPathLength = 240; // Conservative V1: no extended/device paths.
    public static string? Canonical(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength) return null;
        path = path.Replace('/', '\\');
        if (!Regex.IsMatch(path, @"^[A-Za-z]:\\") || path.IndexOf(':', 2) >= 0) return null;
        var parts = path[3..].TrimEnd('\\').Split('\\');
        if (parts.Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.') ||
            p.Any(c => c < 32 || "<>\"|?*:".Contains(c)) ||
            Regex.IsMatch(p.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))) return null;
        return char.ToUpperInvariant(path[0]) + path[1..].TrimEnd('\\');
    }
    public static bool IsChild(string? root, string? path)
    {
        var r = Canonical(root); var p = Canonical(path);
        return r is not null && p is not null && p.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
    }
    public static bool Eligible(long size, long lastWrite, DateTimeOffset now, TimeSpan age)
    {
        if (size < 0 || lastWrite <= 0) return false;
        try { var date = DateTime.FromFileTimeUtc(lastWrite); return date <= now.UtcDateTime && now.UtcDateTime - date >= age; }
        catch (ArgumentOutOfRangeException) { return false; }
    }
    public static bool TryAddSize(long current, long size, out long total)
    {
        total = current;
        if (current < 0 || size < 0 || size > long.MaxValue - current) return false;
        total = current + size; return true;
    }
    public static bool SafeFileAttributes(uint attributes, uint links) =>
        (attributes & (0x400u | 0x10u | 0x40u | 0x1000u | 0x200u | 0x800u)) == 0 && links == 1; // reparse/directory/device/offline/sparse/compressed
}
