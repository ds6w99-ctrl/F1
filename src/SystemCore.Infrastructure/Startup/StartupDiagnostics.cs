using System.Diagnostics.Tracing;

namespace SystemCore.Infrastructure.Startup;

// Opt-in local EventSource diagnostics. Never emit source names, commands,
// arguments or paths. No background log writer or retained event collection.
[EventSource(Name = "SystemCore-Startup")]
internal sealed class StartupDiagnostics : EventSource
{
    internal static readonly StartupDiagnostics Log = new();
    [Event(1, Level = EventLevel.Informational)]
    public void InventorySource(int source, int count, bool complete) => WriteEvent(1, source, count, complete);
    [Event(2, Level = EventLevel.Informational)]
    public void Operation(int action, int outcome) => WriteEvent(2, action, outcome);
}
