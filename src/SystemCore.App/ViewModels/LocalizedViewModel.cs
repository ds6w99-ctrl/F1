using CommunityToolkit.Mvvm.ComponentModel;
using SystemCore.App.Services;

namespace SystemCore.App.ViewModels;

public abstract class LocalizedViewModel : ObservableObject, IDisposable
{
    protected LocalizationService L { get; }
    public System.Globalization.CultureInfo Culture => L.Culture;
    protected LocalizedViewModel(LocalizationService localizer)
    {
        L = localizer;
        L.Changed += OnLanguageChanged;
    }
    private void OnLanguageChanged(object? sender, EventArgs args) => RefreshLanguage();
    protected virtual void RefreshLanguage() => OnPropertyChanged(string.Empty);
    public virtual void Dispose() => L.Changed -= OnLanguageChanged;
}
