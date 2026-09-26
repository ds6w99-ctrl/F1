using SystemCore.Core.Performance;

namespace SystemCore.Core.MemoryTools;

// Conservative, documented thresholds, not a diagnosis of responsiveness. Uses
// only the shared scalar history; no separate timer or retained process snapshots.
public sealed class MemoryPressureEvaluator : IMemoryPressureEvaluator
{
    private MemoryPressure previous = MemoryPressure.Observing;
    private DateTimeOffset? evaluated;
    public MemoryPressureAssessment Evaluate(MemorySnapshot? current, IReadOnlyList<HistoryPoint> history, DateTimeOffset now)
    {
        if (current is null || !current.ValidPressureInputs) return Reset(MemoryAssessmentReason.Unavailable);
        if (now - current.Timestamp > TimeSpan.FromSeconds(5) || current.Timestamp > now.AddSeconds(1)) return Reset(MemoryAssessmentReason.Stale);
        if (evaluated.HasValue && (current.Timestamp < evaluated || current.Timestamp - evaluated > TimeSpan.FromSeconds(3))) previous = MemoryPressure.Observing;
        evaluated = current.Timestamp;
        var window = new List<HistoryPoint>(); DateTimeOffset? last = null;
        foreach (var point in history)
        {
            if (point.Timestamp > current.Timestamp || current.Timestamp - point.Timestamp >= TimeSpan.FromSeconds(60)) continue;
            if (point.TotalPhysical != current.TotalPhysical || point.AvailableBytes is null || point.AvailableBytes > point.TotalPhysical
                || point.CommitPercent is null or < 0 or > 100 || !double.IsFinite(point.CommitPercent.Value)) { window.Clear(); last = null; continue; }
            if (last.HasValue && (point.Timestamp - last > TimeSpan.FromSeconds(2.5) || point.Timestamp <= last)) window.Clear();
            window.Add(point); last = point.Timestamp;
        }
        var observed = window.Count > 0 ? (window[^1].Timestamp - window[0].Timestamp).TotalSeconds : 0;
        if (observed < 3 || last != current.Timestamp)
        { previous = MemoryPressure.Observing; return new(previous, MemoryAssessmentReason.WarmingUp, observed); }
        var total = current.TotalPhysical!.Value;
        var low = Math.Max(512d * 1024 * 1024, Math.Min(2d * 1024 * 1024 * 1024, total * .10));
        var veryLow = Math.Max(256d * 1024 * 1024, Math.Min(1d * 1024 * 1024 * 1024, total * .05));
        var recovered = Math.Max(1d * 1024 * 1024 * 1024, Math.Min(3d * 1024 * 1024 * 1024, total * .15));
        bool High(HistoryPoint p) => p.AvailableBytes <= veryLow && p.CommitPercent >= 95;
        bool Elevated(HistoryPoint p) => p.AvailableBytes <= low && p.CommitPercent >= 85;
        bool Sustained(Func<HistoryPoint, bool> predicate, double seconds)
        {
            var first = window.Count - 1;
            while (first >= 0 && predicate(window[first])) first--;
            return first < window.Count - 1 && (window[^1].Timestamp - window[first + 1].Timestamp).TotalSeconds >= seconds;
        }
        if (Sustained(High, 15)) previous = MemoryPressure.High;
        else if (previous is MemoryPressure.High or MemoryPressure.Elevated && Sustained(p => p.AvailableBytes >= recovered && p.CommitPercent < 80, 10)) previous = MemoryPressure.Normal;
        else if (previous == MemoryPressure.High && Sustained(p => !High(p), 10)) previous = MemoryPressure.Elevated;
        else if (previous != MemoryPressure.High && Sustained(Elevated, 5)) previous = MemoryPressure.Elevated;
        else if (previous is MemoryPressure.Unknown or MemoryPressure.Observing) previous = MemoryPressure.Normal;
        var reason = previous switch
        {
            MemoryPressure.High => High(window[^1]) ? MemoryAssessmentReason.SustainedPressure : MemoryAssessmentReason.RecoveryObservation,
            MemoryPressure.Elevated => Elevated(window[^1]) ? MemoryAssessmentReason.SustainedReducedAvailable : MemoryAssessmentReason.RecoveryObservation,
            _ => MemoryAssessmentReason.WithinRange
        };
        return new(previous, reason, observed);
    }
    private MemoryPressureAssessment Reset(MemoryAssessmentReason reason)
    { previous = MemoryPressure.Unknown; evaluated = null; return new(previous, reason, 0); }
}
