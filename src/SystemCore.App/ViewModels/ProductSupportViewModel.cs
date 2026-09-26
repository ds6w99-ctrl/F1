using System.Reflection;
using System.Runtime.InteropServices;
using SystemCore.App.Services;
using SystemCore.Core.Diagnostics;
using SystemCore.Core.SystemInformation;

namespace SystemCore.App.ViewModels;

public sealed class ProductSupportViewModel(LocalizationService localizer, IAppDiagnostics diagnostics) : LocalizedViewModel(localizer)
{
    private readonly CancellationTokenSource lifetime = new();
    private bool busy;
    private string status = "DiagnosticsReady";
    private string? notices;
    private DiagnosticsStatus? state;
    public static string BuildVersion => typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
    public string Text(string key) => L[key];
    public string Version => L.Format("Version", BuildVersion);
    public string VersionLabel => L.Format("Version", string.Empty).Trim();
    public string VersionNumber => BuildVersion;
    public string Architecture => L.Format("AboutArchitecture", RuntimeInformation.ProcessArchitecture);
    public string Status => L[status];
    public bool CanAct => !busy;
    public string Storage => state is null ? L["DiagnosticsRetention"] : L.Format("DiagnosticsStorage", state.Files, InfoFormatting.Size((ulong)Math.Max(0, state.Bytes), L.Culture), state.DroppedRecords) + "\n" + L["DiagnosticsRetention"];
    public string Notices
    {
        get
        {
            if (notices is not null) return notices;
            var assembly = typeof(App).Assembly; var text = new System.Text.StringBuilder();
            foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Notices.", StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal))
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null || stream.Length > 512 * 1024 || text.Length + stream.Length > 2 * 1024 * 1024) continue;
                using var reader = new StreamReader(stream); text.AppendLine(name[8..]).AppendLine(reader.ReadToEnd()).AppendLine();
            }
            return notices = text.ToString();
        }
    }
    public async Task RefreshAsync() => await Run(async () => { await diagnostics.FlushAsync(lifetime.Token); state = await Task.Run(diagnostics.GetStatus); status = state.Available ? "DiagnosticsReady" : "DiagnosticsFailure"; });
    public async Task ExportAsync() => await Run(async () => { var result = await diagnostics.ExportAsync(lifetime.Token); status = result.Succeeded ? "DiagnosticsExported" : "DiagnosticsExportFailed"; state = await Task.Run(diagnostics.GetStatus); });
    public async Task ClearOldAsync() => await Run(async () => { var result = await diagnostics.ClearOldAsync(lifetime.Token); status = result.Succeeded ? "DiagnosticsCleared" : "DiagnosticsClearFailed"; state = await Task.Run(diagnostics.GetStatus); });
    public async Task OpenAsync(DiagnosticFolder folder) => await Run(async () => { status = await diagnostics.OpenFolderAsync(folder, lifetime.Token) ? "DiagnosticsFolderOpened" : "DiagnosticsOpenFailed"; });
    public void CopyVersion()
    {
        try { var content = new Windows.ApplicationModel.DataTransfer.DataPackage(); content.SetText(Core.ProductIdentity.Name + " " + BuildVersion + " " + RuntimeInformation.ProcessArchitecture); Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(content); status = "VersionCopied"; }
        catch (Exception e) when (e is COMException or InvalidOperationException) { status = "VersionCopyFailed"; }
        OnPropertyChanged(string.Empty);
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return; busy = true; status = "DiagnosticsWorking"; OnPropertyChanged(string.Empty);
        try { await action(); } catch (OperationCanceledException) { status = "DiagnosticsReady"; }
        catch (Exception e) when (e is not OutOfMemoryException) { status = "DiagnosticsFailure"; diagnostics.Record(DiagnosticFeature.Diagnostics, DiagnosticEvent.ProviderFailure, e.HResult); }
        finally { busy = false; OnPropertyChanged(string.Empty); }
    }
    public override void Dispose() { lifetime.Cancel(); base.Dispose(); }
}
