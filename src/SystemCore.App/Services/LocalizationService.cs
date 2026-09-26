using System.Globalization;
using System.Resources;
using SystemCore.Core.Settings;

namespace SystemCore.App.Services;

public sealed class LocalizationService
{
    private readonly ResourceManager resources = new("SystemCore.App.Resources.Shell", typeof(App).Assembly);
    private readonly IUserSettingsService settings;
    public CultureInfo Culture { get; private set; }
    public LocalizationService(IUserSettingsService settings)
    {
        this.settings = settings;
        Culture = CultureInfo.GetCultureInfo(settings.GetLanguage());
    }
    public bool IsRightToLeft => Culture.TextInfo.IsRightToLeft;
    public event EventHandler? Changed;
    public string this[string key] => (resources.GetString(key, Culture)
        ?? throw new InvalidOperationException($"Missing localization resource: {key}")).Replace("{Brand}", Core.ProductIdentity.Name, StringComparison.Ordinal);
    public string Format(string key, params object[] args) => string.Format(Culture, this[key], args);

    public void SetLanguage(string language)
    {
        var next = CultureInfo.GetCultureInfo(language == "ar" ? "ar" : "en");
        if (next.Name == Culture.Name) return;
        Culture = next;
        Changed?.Invoke(this, EventArgs.Empty);
        settings.SetLanguage(next.Name);
    }
}
