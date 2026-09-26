#if MEMORY_VALIDATION
using SystemCore.Core.MemoryTools;
using SystemCore.Core.Performance;

namespace SystemCore.App.MemoryTools;

// Compile-time-only fixture: excluded from production assemblies, with no runtime
// environment switch. Real metrics + explicitly synthetic pressure/cache state.
public sealed class MemoryValidationEnvironment : IMemoryAnalysisService, ISystemCoreMemoryCacheOwner
{
    public static MemoryValidationEnvironment Current { get; private set; } = null!;
    private readonly MemoryAnalysisService inner;
    private readonly object sync = new();
    private byte[]? buffer;
    private Guid version;
    private bool active;
    private MemoryPressure state = MemoryPressure.Normal;
    private ulong released;
    public MemoryValidationEnvironment(IPerformanceMonitorService monitor, IMemoryPressureEvaluator evaluator)
    { inner = new(monitor, evaluator); Current = this; }
    public string Audit { get { lock (sync) return $"TEST ONLY — Synthetic pressure:{state}; Cache:{buffer?.Length ?? 0}; Active:{active}; Released:{released}"; } }
    public void SetState(bool high) { lock (sync) state = high ? MemoryPressure.High : MemoryPressure.Normal; }
    public void Prepare(bool inUse)
    {
        lock (sync)
        {
            buffer = new byte[16 * 1024 * 1024];
            for (var index = 0; index < buffer.Length; index += 4096) buffer[index] = 0x5a;
            version = Guid.NewGuid(); active = inUse; released = 0;
        }
    }
    public MemoryAnalysis Analyze()
    {
        var actual = inner.Analyze();
        lock (sync) return actual with { Pressure = new(state, state == MemoryPressure.High ? MemoryAssessmentReason.SustainedPressure : MemoryAssessmentReason.WithinRange, 30) };
    }
    public OwnedMemoryCache? Inspect() { lock (sync) return buffer is null ? null : new(version, true, true, active, (ulong)buffer.Length); }
    public CacheReleaseResult ReleaseIfInactive(Guid expected)
    {
        lock (sync)
        {
            if (buffer is null || active || version != expected) return new(CacheReleaseStatus.Changed, 0);
            var size = (ulong)buffer.Length; buffer = null; released += size;
            return new(CacheReleaseStatus.Released, size);
        }
    }
}
#endif
