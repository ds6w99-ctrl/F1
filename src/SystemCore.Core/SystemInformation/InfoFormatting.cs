using System.Globalization;

namespace SystemCore.Core.SystemInformation;

public static class InfoFormatting
{
    // UI convention: decimal-style labels with the existing 1024-based scaling.
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];
    public static string Size(ulong bytes, CultureInfo culture) => FormatBytes(bytes, culture, 4);
    public static string Rate(ulong bytesPerSecond, CultureInfo culture) => FormatBytes(bytesPerSecond, culture, 3) + "/s";
    public static string Time(DateTimeOffset value, CultureInfo culture) => value.ToLocalTime().ToString("T", culture);
    public static string Date(DateTimeOffset value, CultureInfo culture) => value.ToString("d", culture);
    public static string Date(DateOnly value, CultureInfo culture) => value.ToString("d", culture);
    public static string Technical(string value) => "\u2066" + value + "\u2069";
    public static string Duration(TimeSpan value, CultureInfo culture, string localizedFormat) =>
        string.Format(culture, localizedFormat, (int)value.TotalDays, value.Hours, value.Minutes, value.Seconds);
    private static string FormatBytes(ulong bytes, CultureInfo culture, int maximumUnit)
    {
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < maximumUnit) { value /= 1024; index++; }
        return $"{value.ToString(index == 0 ? "N0" : "N1", culture)} {Units[index]}";
    }
    public static double? Percentage(ulong used, ulong total) => total == 0 || used > total ? null : used * 100.0 / total;
    public static string? Clean(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrWhiteSpace(text) || text.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Default string", StringComparison.OrdinalIgnoreCase) || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ? null : text;
    }
}
