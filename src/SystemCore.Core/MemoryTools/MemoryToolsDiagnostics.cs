using System.Diagnostics.Tracing;

namespace SystemCore.Core.MemoryTools;

[EventSource(Name = "SystemCore-MemoryTools")]
public sealed class MemoryToolsDiagnostics : EventSource
{
    public static readonly MemoryToolsDiagnostics Log = new();
    [Event(1, Level = EventLevel.Informational)] public void AnalysisRequested() => WriteEvent(1);
    [Event(2, Level = EventLevel.Informational)] public void PressureState(int state) => WriteEvent(2, state);
    [Event(3, Level = EventLevel.Informational)] public void OptimizationRequested() => WriteEvent(3);
    [Event(4, Level = EventLevel.Informational)] public void PlanActionCount(int count) => WriteEvent(4, count);
    [Event(5, Level = EventLevel.Informational)] public void KnownBytesReleased(ulong bytes) => WriteEvent(5, bytes);
    [Event(6, Level = EventLevel.Warning)] public void FailureReason(string reason) => WriteEvent(6, reason);
}
