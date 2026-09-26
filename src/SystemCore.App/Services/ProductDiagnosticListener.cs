using System.Diagnostics.Tracing;
using SystemCore.Core.Diagnostics;

namespace SystemCore.App.Services;

// Payload strings are deliberately excluded: upstream events can contain paths,
// identifiers or command metadata. Keep only known source/event identifiers.
public sealed class ProductDiagnosticListener : EventListener
{
    private IAppDiagnostics? diagnostics;
    public ProductDiagnosticListener(IAppDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
        foreach (var source in EventSource.GetSources()) Subscribe(source);
    }
    protected override void OnEventSourceCreated(EventSource eventSource) => Subscribe(eventSource);
    private void Subscribe(EventSource source)
    {
        if (source.Name is "SystemCore-MemoryTools" or "SystemCore-Startup" or "SystemCore-Reliability") EnableEvents(source, EventLevel.Informational);
    }
    protected override void OnEventWritten(EventWrittenEventArgs args)
    {
        if (args.EventSource.Name == "SystemCore-Reliability")
        {
            diagnostics?.Record(DiagnosticFeature.App, DiagnosticEvent.ProviderFailure,
                args.Payload is { Count: >= 2 } && args.Payload[1] is int code ? code : args.EventId);
            return;
        }
        var feature = args.EventSource.Name == "SystemCore-MemoryTools" ? DiagnosticFeature.MemoryTools : DiagnosticFeature.Startup;
        diagnostics?.Record(feature, DiagnosticEvent.SourceEvent, args.EventId);
    }
}
