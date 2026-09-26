using System.Diagnostics.Tracing;

namespace SystemCore.Core.Diagnostics;

// Numeric component/failure codes only. Cancellation is handled separately by the
// operation owning the token, and is never emitted as a provider failure here.
public enum ReliabilityComponent { Monitor, Dashboard, SystemInformation, Shutdown, UnobservedTask, Ui, Runtime, Cleaner, Helper, Startup }

[EventSource(Name = "SystemCore-Reliability")]
public sealed class ReliabilityDiagnostics : EventSource
{
    public static readonly ReliabilityDiagnostics Log = new();
    [Event(1, Level = EventLevel.Error)]
    public void UnexpectedFailure(int component, int hresult) => WriteEvent(1, component, hresult);
}
