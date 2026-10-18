using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SystemCore.App.Services;

// Central safety net for event handlers. A handler declared `async void` cannot let an exception
// escape: it would terminate the process instead of reaching the UI. Every asynchronous UI handler
// routes through here so a fault is contained and surfaced once, consistently.
public static class UiSafety
{
    // The single place a UI action failure is reported. Set once by the shell.
    public static Action<Exception>? Report { get; set; }

    public static async void Run(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (OutOfMemoryException) { throw; }
        catch (Exception e) { Report?.Invoke(e); }
    }

    public static async void Run(Func<object?, Task> action, object? argument)
    {
        try { await action(argument); }
        catch (OperationCanceledException) { }
        catch (OutOfMemoryException) { throw; }
        catch (Exception e) { Report?.Invoke(e); }
    }

    // Shows a modal message on the correct visual root, tolerating a detached element.
    public static async Task ShowErrorAsync(FrameworkElement owner, string title, string message)
    {
        try
        {
            if (owner.XamlRoot is null) return;
            var dialog = new ContentDialog
            {
                XamlRoot = owner.XamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close,
                FlowDirection = owner.FlowDirection,
                RequestedTheme = owner.ActualTheme == ElementTheme.Dark ? ElementTheme.Dark : ElementTheme.Light
            };
            await dialog.ShowAsync();
        }
        catch (Exception) { /* A dialog that cannot be shown must not fail the handler again. */ }
    }
}
