namespace SystemCore.Core.Settings;

public enum UserTheme { System, Light, Dark }

public interface IUserSettingsService
{
    bool LastSaveSucceeded => true;
    UserTheme GetTheme();
    void SetTheme(UserTheme theme);
    string GetLanguage();
    void SetLanguage(string language);
}
