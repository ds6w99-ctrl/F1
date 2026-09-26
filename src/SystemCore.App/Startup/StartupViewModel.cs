using SystemCore.App.Services;
using SystemCore.App.ViewModels;
using SystemCore.Core.Startup;

namespace SystemCore.App.Startup;

public sealed class StartupViewModel(LocalizationService localizer, IStartupInventoryService inventory, IStartupMutationService mutation) : LocalizedViewModel(localizer)
{
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? operation;
    private StartupInventorySession? session;
    private bool busy;
    private bool changing;
    private string search = "", selected = "", message = "StartReady";
    private int filter, sort, page;
    public string Text(string key) => L[key];
    public bool Busy => busy;
    public string StatusText => L[busy ? "StartLoading" : message] + (!busy && session is { Inventory.Complete: true } current && Count == 0 ? "\n" + L[current.Inventory.Items.Count == 0 ? "StartEmpty" : "ProductNoMatches"] : "");
    public string Search { get => search; set { search = value; page = 0; selected = ""; Notify(); } }
    public int Filter { get => filter; set { filter = value; page = 0; selected = ""; Notify(); } }
    public int Sort { get => sort; set { sort = value; page = 0; selected = ""; Notify(); } }
    public string SelectedId { get => selected; set { selected = value; Notify(); } }
    public int RawCount => session?.Inventory.Items.Count ?? 0;
    public int ManageableCount => session?.Inventory.Items.Count(ManageableStartupItems.IsVisible) ?? 0;
    public int HiddenCount => RawCount - ManageableCount;
    public StartupItem? Selected => session?.Inventory.Items.FirstOrDefault(i => i.Id == selected && ManageableStartupItems.IsVisible(i));
    public bool CanDisable => !busy && Selected is { IsProtected: false, Status: StartupStatus.Enabled };
    public bool CanEnable => !busy && Selected is { IsProtected: false, Status: StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource or StartupStatus.RecoveryRequired };
    public bool CanCancel => busy && !changing;
    public string Confirmation(StartupAction action) => L[action == StartupAction.Disable ? "StartConfirmDisable" : "StartConfirmEnable"]
        + (Selected is { } item ? "\n\n" + item.Name + (item.RequiresElevation ? "\n" + L["StartConfirmElevation"] : "")
            + (action == StartupAction.Disable && item.SourceType == StartupSourceType.RegistryRunOnce ? "\n\n" + L["StartRunOnceWarning"] : "") : "");
    private IEnumerable<StartupItem> Filtered()
    {
        var items = (session?.Inventory.Items.AsEnumerable() ?? []).Where(ManageableStartupItems.IsVisible);
        items = items.Where(i => new[] { i.Name, i.Publisher, i.ExecutablePath, i.SourceLocation, L["StartSource" + i.SourceType] }
            .Any(v => v?.Contains(search, StringComparison.CurrentCultureIgnoreCase) == true));
        items = filter switch
        {
            1 => items.Where(i => i.Status == StartupStatus.Enabled),
            2 => items.Where(i => i.Status is StartupStatus.DisabledBySystemCore or StartupStatus.DisabledByWindowsOrSource),
            3 => items.Where(i => i.Scope == StartupScope.CurrentUser), 4 => items.Where(i => i.Scope == StartupScope.AllUsers),
            5 => items.Where(i => i.SourceType is StartupSourceType.RegistryRun or StartupSourceType.RegistryRunOnce),
            6 => items.Where(i => i.SourceType is StartupSourceType.StartupFolderUser or StartupSourceType.StartupFolderCommon),
            7 => items.Where(i => i.SourceType is StartupSourceType.ScheduledTaskLogon or StartupSourceType.ScheduledTaskBoot),
            8 => items.Where(i => i.IsProtected), _ => items
        };
        return sort switch { 1 => items.OrderBy(i => i.Publisher, StringComparer.CurrentCultureIgnoreCase), 2 => items.OrderBy(i => i.Status),
            3 => items.OrderBy(i => i.SourceType), 4 => items.OrderBy(i => i.Scope), _ => items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase) };
    }
    public IReadOnlyList<StartupItem> Rows => Filtered().Skip(page * 20).Take(20).ToArray();
    public int Count => Filtered().Count();
    public string PageText => $"{page + 1} / {Math.Max(1, (Count + 19) / 20)} — {Count}";
    public bool HasNext => !busy && (page + 1) * 20 < Count;
    public bool HasPrevious => !busy && page > 0;
    public void MovePage(int offset) { page = Math.Clamp(page + offset, 0, Math.Max(0, (Count - 1) / 20)); selected = ""; Notify(); }
    public string Label(StartupItem item) => $"{item.Name} — {item.Publisher ?? L["StartUnknown"]}\n{L["StartStatus" + item.Status]} | {L["StartSource" + item.SourceType]} | {L["StartScope" + item.Scope]}";
    public string Details => Selected is { } item ? Label(item) + "\n" + L["StartImpact"] + ": " + L["StartNotMeasured"]
        + "\n" + L["StartProtection"] + ": " + L["StartProtection" + item.ProtectionReason]
        + "\n" + L["StartElevation"] + ": " + L[item.RequiresElevation ? "StartYes" : "StartNo"]
        + (item.SourceType == StartupSourceType.RegistryRunOnce ? "\n" + L["StartRunOnceWarning"] : "") : "";
    public string TechnicalDetails => Selected is { } item ? $"{item.SourceLocation}\n{item.ExecutablePath ?? L["StartUnknown"]}\n{item.ArgumentsDisplay}\n{item.CommandDisplay}\n{item.TriggerDescription}" : "";
    public async Task RefreshAsync()
    {
        if (busy) return;
        busy = true; selected = ""; session = null;
        operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); Notify();
        try { session = await inventory.RefreshAsync(operation.Token); page = 0; message = session.Inventory.Complete ? "StartLoaded" : "StartPartial"; }
        catch (OperationCanceledException) { message = "StartCancelled"; }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { message = "StartFailed"; }
        finally { operation.Dispose(); operation = null; busy = false; Notify(); }
    }
    public void Cancel() => operation?.Cancel();
    public async Task ChangeAsync(StartupAction action, string confirmedId)
    {
        if (session is null || confirmedId != SelectedId || !(action == StartupAction.Disable ? CanDisable : CanEnable)) return;
        var request = new StartupOperationRequest(session.Id, selected, action, true);
        busy = changing = true; operation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); Notify();
        try
        {
            var result = await mutation.ApplyAsync(request, operation.Token);
            session = null; selected = "";
            session = await inventory.RefreshAsync(lifetime.Token); page = 0;
            message = "StartOutcome" + result.Outcome;
        }
        catch (OperationCanceledException) { message = "StartOutcomeRecoveryRequired"; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException) { message = "StartOutcomeRecoveryRequired"; }
        finally { operation.Dispose(); operation = null; busy = changing = false; Notify(); }
    }
    private void Notify() => OnPropertyChanged(string.Empty);
    public override void Dispose() { lifetime.Cancel(); session = null; base.Dispose(); }
}
