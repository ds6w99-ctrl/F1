using SystemCore.App.Services;
using SystemCore.Core.Settings;

namespace SystemCore.App.ViewModels;

public sealed class SettingsViewModel : LocalizedViewModel
{
    private readonly IUserSettingsService settings;
    private int themeIndex;
    private readonly SystemCore.Core.Diagnostics.IAppDiagnostics diagnostics;
    private string status = "SessionSettingsActive";
    public ProductSupportViewModel Support { get; }
    public CommunityToolkit.Mvvm.Input.IRelayCommand AboutCommand { get; private set; } = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }, () => false);
    public void ConfigureNavigation(Action<string> navigate) => AboutCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => navigate("About"));
    public SettingsViewModel(LocalizationService localizer, IUserSettingsService settings, ProductSupportViewModel support, SystemCore.Core.Diagnostics.IAppDiagnostics diagnostics) : base(localizer)
    {
        this.settings = settings;
        this.diagnostics = diagnostics; Support = support;
        themeIndex = (int)settings.GetTheme();
    }
    public string Title => L["SettingsTitle"];
    public string Description => L["SettingsDescription"];
    public string LanguageLabel => L["Language"];
    public string ThemeLabel => L["Theme"];
    public string SessionNotice => L[status];
    public string Text(string key) => L[key];
    public string GeneralLabel => L["SettingsGeneral"];
    public string GeneralNotice => L["SettingsGeneralNotice"];
    public string BehaviorLabel => L["SettingsBehavior"];
    public string SafetyNotice => L["SettingsSafetyNotice"];
    public string CleanerLabel => L["NavCleaner"];
    public string CleanerNotice => L["SettingsCleanerNotice"];
    public string PrivacyLabel => L["SettingsPrivacy"];
    public string PrivacyNotice => L["SettingsPrivacyNotice"];
    public string AppearanceLabel => L["SettingsAppearance"];
    public string AboutLabel => L["NavAbout"];
    public string AboutNotice => L["AboutDescription"];
    public string EnglishLabel => L["English"];
    public string ArabicLabel => L["Arabic"];
    public string SystemLabel => L["ThemeSystem"];
    public string LightLabel => L["ThemeLight"];
    public string DarkLabel => L["ThemeDark"];
    public int LanguageIndex
    {
        get => L.IsRightToLeft ? 1 : 0;
        set { if (value is < 0 or > 1 || value == LanguageIndex) return; L.SetLanguage(value == 1 ? "ar" : "en"); Saved(); }
    }
    public int ThemeIndex
    {
        get => themeIndex;
        set
        {
            if (value is < 0 or > 2) return;
            if (SetProperty(ref themeIndex, value)) { settings.SetTheme((UserTheme)themeIndex); Saved(); }
        }
    }
    private void Saved()
    {
        status = settings.LastSaveSucceeded ? "SettingsSaved" : "SettingsSaveFailed";
        diagnostics.Record(SystemCore.Core.Diagnostics.DiagnosticFeature.Settings, settings.LastSaveSucceeded ? SystemCore.Core.Diagnostics.DiagnosticEvent.SettingsSaved : SystemCore.Core.Diagnostics.DiagnosticEvent.SettingsSaveFailed);
        OnPropertyChanged(nameof(SessionNotice));
    }
}
