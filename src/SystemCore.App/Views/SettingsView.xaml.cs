using Microsoft.UI.Xaml.Controls;
using SystemCore.App.ViewModels;

namespace SystemCore.App.Views;

public sealed partial class SettingsView : UserControl
{
    public SettingsViewModel ViewModel { get; }
    private bool refreshingThemes;
    public SettingsView(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DiagnosticsHost.Content = new DiagnosticsPanel(viewModel.Support);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private void OnLoaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) { ViewModel.PropertyChanged += OnChanged; RefreshSelectedTheme(); }
    private void OnUnloaded(object sender, Microsoft.UI.Xaml.RoutedEventArgs args) => ViewModel.PropertyChanged -= OnChanged;
    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName)) RefreshSelectedTheme();
    }
    private void RefreshSelectedTheme() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!IsLoaded) return;
        // Replace localized choices so WinUI also invalidates its cached collapsed
        // selection content. A display refresh must never write a preference.
        var selected = ViewModel.ThemeIndex;
        refreshingThemes = true;
        try
        {
            ThemePicker.ItemsSource = new[] { ViewModel.SystemLabel, ViewModel.LightLabel, ViewModel.DarkLabel };
            ThemePicker.SelectedIndex = selected;
        }
        finally { refreshingThemes = false; }
    });
    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!refreshingThemes && ThemePicker.SelectedIndex >= 0) ViewModel.ThemeIndex = ThemePicker.SelectedIndex;
    }
}
