using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SystemCore.App.ViewModels;
using Windows.UI.ViewManagement;

namespace SystemCore.App.Services;

public sealed class WindowAppearanceController : IDisposable
{
    private readonly Window window;
    private readonly FrameworkElement root;
    private readonly Border solid;
    private readonly SettingsViewModel settings;
    private readonly LocalizationService localizer;
    private readonly CompatibilityService compatibility;
    private readonly UISettings ui = new();
    private readonly AccessibilitySettings accessibility = new();
    private bool disposed;
    private bool contrastSubscribed;
    private readonly FrameworkElement scenery, content;

    public WindowAppearanceController(Window window, FrameworkElement root, Border solid,
        SettingsViewModel settings, LocalizationService localizer, CompatibilityService compatibility, FrameworkElement scenery, FrameworkElement content)
    {
        this.window = window;
        this.root = root;
        this.solid = solid;
        this.settings = settings;
        this.localizer = localizer;
        this.compatibility = compatibility;
        this.scenery = scenery; this.content = content;
        settings.PropertyChanged += OnSettingsChanged;
        localizer.Changed += OnLanguageChanged;
        ui.AdvancedEffectsEnabledChanged += OnEffectsChanged;
        try
        {
            accessibility.HighContrastChanged += OnContrastChanged;
            contrastSubscribed = true;
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x80070490))
        {
            // ERROR_NOT_FOUND: this unpackaged host has no event registration.
            // A single attempted subscription per window; Apply rechecks contrast
            // on activation/theme changes. Other COM errors remain visible.
        }
        root.ActualThemeChanged += OnActualThemeChanged;
        Apply();
    }

    public void Apply()
    {
        if (disposed) return;
        root.RequestedTheme = settings.ThemeIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        root.FlowDirection = FlowDirection.LeftToRight;
        content.FlowDirection = localizer.IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        root.Language = localizer.Culture.Name;
        window.SystemBackdrop = null; // Deterministic local scenery/surfaces, no animated blur.
        solid.Visibility = Visibility.Visible;
        // Static local art is composited over an opaque window, never desktop glass.
        // With reduced transparency both the art and the cards are fully opaque.
        scenery.Visibility = !accessibility.HighContrast && root.ActualTheme == ElementTheme.Dark ? Visibility.Visible : Visibility.Collapsed;
        scenery.Opacity = ui.AdvancedEffectsEnabled ? 0.62 : 1;
        window.AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        window.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        window.AppWindow.TitleBar.ButtonForegroundColor = accessibility.HighContrast ? null : root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(SettingsViewModel.ThemeIndex) or "" or null) Apply();
    }
    private void OnLanguageChanged(object? sender, EventArgs args) => Apply();
    private void OnActualThemeChanged(FrameworkElement sender, object args) => Apply();
    private void OnEffectsChanged(UISettings sender, object args) => window.DispatcherQueue.TryEnqueue(Apply);
    private void OnContrastChanged(AccessibilitySettings sender, object args) => window.DispatcherQueue.TryEnqueue(Apply);

    public void Dispose()
    {
        disposed = true;
        settings.PropertyChanged -= OnSettingsChanged;
        localizer.Changed -= OnLanguageChanged;
        ui.AdvancedEffectsEnabledChanged -= OnEffectsChanged;
        if (contrastSubscribed) accessibility.HighContrastChanged -= OnContrastChanged;
        root.ActualThemeChanged -= OnActualThemeChanged;
    }
}
