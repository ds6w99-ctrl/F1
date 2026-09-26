using System.Globalization;
using SystemCore.Core.Settings;
using SystemCore.Infrastructure.Settings;

internal static class SettingsTests
{
    public static bool Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SystemCore-settings-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "preferences.json");
        var passed = true;
        void Check(string name, bool result)
        {
            Console.WriteLine($"{(result ? "PASS" : "FAIL")}: {name}");
            passed &= result;
        }
        LocalUserSettingsService Open(string culture = "en-US") => new(path, CultureInfo.GetCultureInfo(culture));
        try
        {
            Check("Missing settings default to System and English", Open().GetTheme() == UserTheme.System && Open().GetLanguage() == "en");
            Check("Windows Arabic default", Open("ar-SA").GetLanguage() == "ar");
            Check("Unsupported Windows language defaults to English", Open("fr-FR").GetLanguage() == "en");
            foreach (var theme in Enum.GetValues<UserTheme>())
            {
                var service = Open();
                service.SetTheme(theme);
                Check($"Save theme {theme}", service.GetTheme() == theme && File.Exists(path));
                Check($"Restore theme {theme}", Open().GetTheme() == theme);
            }
            foreach (var language in new[] { "ar", "en" })
            {
                var service = Open();
                service.SetLanguage(language);
                Check($"Save language {language}", service.GetLanguage() == language);
                Check($"Restore language {language}", Open("fr-FR").GetLanguage() == language);
            }
            var both = Open();
            both.SetTheme(UserTheme.Dark);
            both.SetLanguage("ar");
            Check("Saving language preserves theme", Open().GetTheme() == UserTheme.Dark);
            both.SetTheme(UserTheme.Light);
            Check("Saving theme preserves language", Open().GetLanguage() == "ar");
            File.WriteAllText(path, "{\"theme\":\"Unknown\",\"language\":\"xx\"}");
            Check("Invalid stored theme", Open().GetTheme() == UserTheme.System);
            Check("Invalid stored language uses Windows default", Open("ar-SA").GetLanguage() == "ar");
            foreach (var content in new[] { "{broken", "null", "[]", "{}", "{\"theme\":9,\"language\":false}", new string('x', 4097) })
            {
                File.WriteAllText(path, content);
                var result = Open();
                Check("Corrupt/missing fields default safely", result.GetTheme() == UserTheme.System && result.GetLanguage() == "en");
            }
            var blocked = new LocalUserSettingsService(Path.Combine(path, "child.json"), CultureInfo.GetCultureInfo("en"));
            blocked.SetTheme(UserTheme.Dark);
            blocked.SetLanguage("ar");
            Check("Write failure preserves usable session", blocked.GetTheme() == UserTheme.Dark && blocked.GetLanguage() == "ar");
            Check("Atomic save leaves no temporary files", Directory.GetFiles(directory, "*.tmp").Length == 0);
        }
        finally { Directory.Delete(directory, recursive: true); }
        return passed;
    }
}
