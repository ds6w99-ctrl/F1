using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
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
    private readonly UISettings ui = new();
    private readonly AccessibilitySettings accessibility = new();
    private bool disposed;
    private bool contrastSubscribed;
    private readonly FrameworkElement scenery, content;
    // Packaged resources: owner-supplied "Quiet Summits" artwork, one per theme.
    internal const string DarkBackground = "ms-appx:///Assets/F1/background-dark.png";
    internal const string LightBackground = "ms-appx:///Assets/F1/background-light.png";

    public WindowAppearanceController(Window window, FrameworkElement root, Border solid,
        SettingsViewModel settings, LocalizationService localizer, FrameworkElement scenery, FrameworkElement content)
    {
        this.window = window;
        this.root = root;
        this.solid = solid;
        this.settings = settings;
        this.localizer = localizer;
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
        // The window shell flips with the language: in Arabic the navigation pane must sit on the
        // right, so the whole root mirrors instead of only the content area. The scenery image is
        // geometry-relative and must not mirror.
        var rtl = localizer.IsRightToLeft;
        root.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        content.FlowDirection = rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        scenery.FlowDirection = FlowDirection.LeftToRight;
        if (window.Content is FrameworkElement page && page != content) page.FlowDirection = root.FlowDirection;
        root.Language = localizer.Culture.Name;
        window.SystemBackdrop = null; // Deterministic local scenery/surfaces, no animated blur.
        solid.Visibility = Visibility.Visible;
        // The background follows the effective theme: light art in light mode, dark art otherwise,
        // and in System mode it re-evaluates whenever ActualTheme changes. UniformToFill keeps the
        // aspect ratio and crops instead of stretching.
        if (scenery is Image background)
        {
            // ActualTheme lags RequestedTheme by a layout pass, so an explicit user choice wins;
            // only System mode reads the effective theme.
            var light = settings.ThemeIndex == 1 || (settings.ThemeIndex == 0 && root.ActualTheme == ElementTheme.Light);
            var art = light ? LightBackground : DarkBackground;
            if (!Equals(background.Tag, art))
            {
                background.Tag = art;
                background.Source = new BitmapImage(new Uri(art));
            }
        }
        // High contrast drops the artwork entirely; otherwise it stays fully opaque.
        scenery.Visibility = accessibility.HighContrast ? Visibility.Collapsed : Visibility.Visible;
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
