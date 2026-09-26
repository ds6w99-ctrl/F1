using System.Globalization;
using System.Text.Json;
using SystemCore.Core.Settings;
using SystemCore.Infrastructure.Cleaner;

namespace SystemCore.Infrastructure.Settings;

// Unpackaged applications cannot assume package-identity LocalSettings availability.
// Two values only, stored in a bounded per-user file; replace atomically on the same volume.
public sealed class LocalUserSettingsService : IUserSettingsService
{
    private readonly string path;
    private readonly string defaultLanguage;
    private UserTheme theme;
    private string language;
    private readonly Dictionary<string, JsonElement> extensions = [];
    private int schemaVersion = 1;
    public bool LastSaveSucceeded { get; private set; } = true;

    public LocalUserSettingsService(string path, CultureInfo windowsLanguage)
    {
        this.path = path;
        defaultLanguage = windowsLanguage.TwoLetterISOLanguageName == "ar" ? "ar" : "en";
        language = defaultLanguage;
        try
        {
            using var lease = PinnedCleanerPath.Open(Path.GetFullPath(path), false, shareRead: true);
            if (lease.Identity.Links != 1 || lease.Identity.Size > 4096) return;
            using var stream = File.OpenRead(path);
            if (stream.Length > 4096) return;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;
            if (root.TryGetProperty("schemaVersion", out var schema) && schema.ValueKind == JsonValueKind.Number && schema.TryGetInt32(out var version) && version > 0) schemaVersion = version;
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("theme" or "language" or "schemaVersion")) extensions[property.Name] = property.Value.Clone();
            if (root.TryGetProperty("theme", out var storedTheme) && storedTheme.ValueKind == JsonValueKind.String)
                theme = storedTheme.GetString() switch { "Light" => UserTheme.Light, "Dark" => UserTheme.Dark, _ => UserTheme.System };
            if (root.TryGetProperty("language", out var storedLanguage) && storedLanguage.ValueKind == JsonValueKind.String)
                language = NormalizeLanguage(storedLanguage.GetString());
        }
        catch (Exception ex) when (IsStorageFailure(ex) || ex is JsonException) { }
    }

    public UserTheme GetTheme() => theme;
    public string GetLanguage() => language;
    public void SetTheme(UserTheme value)
    {
        theme = Enum.IsDefined(value) ? value : UserTheme.System;
        Save();
    }
    public void SetLanguage(string value)
    {
        language = NormalizeLanguage(value);
        Save();
    }
    private string NormalizeLanguage(string? value) => value is "en" or "ar" ? value : defaultLanguage;

    private void Save()
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        PinnedCleanerPath? parent = null, root = null;
        var created = false;
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            parent = PinnedCleanerPath.Open(Path.GetDirectoryName(directory)!, true, namespaceWrite: true);
            Directory.CreateDirectory(directory);
            root = PinnedCleanerPath.Open(directory, true, namespaceWrite: true);
            if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Redirected preferences rejected");
            var values = new Dictionary<string, object> { ["schemaVersion"] = schemaVersion, ["theme"] = theme.ToString(), ["language"] = language };
            foreach (var pair in extensions) values[pair.Key] = pair.Value;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(values);
            if (bytes.Length > 4096) throw new IOException("Preferences exceed size limit");
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { created = true; stream.Write(bytes); stream.Flush(true); }
            File.Move(temporary, path, overwrite: true);
            created = false;
            LastSaveSucceeded = true;
        }
        catch (Exception ex) when (IsStorageFailure(ex))
        {
            LastSaveSucceeded = false;
            // Keep the selected in-memory value usable even if storage becomes unavailable.
            System.Diagnostics.Trace.TraceWarning("User preferences could not be saved: {0}", ex.GetType().Name);
        }
        finally
        {
            try { if (created) File.Delete(temporary); }
            catch (Exception ex) when (IsStorageFailure(ex)) { }
            finally { root?.Dispose(); parent?.Dispose(); }
        }
    }

    private static bool IsStorageFailure(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.ComponentModel.Win32Exception or ArgumentException or NotSupportedException;
}
