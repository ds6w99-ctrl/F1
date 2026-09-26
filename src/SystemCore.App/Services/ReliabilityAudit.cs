#if RELIABILITY_AUDIT
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;

namespace SystemCore.App.Services;

// Explicit audit publication only. No messages, exception objects, locals, filenames,
// paths, source line information or process inventories are retained. First chance
// means "thrown", not "unhandled". Classification is performed after source review.
internal static class ReliabilityAudit
{
    private sealed record Key(string Type, int HResult, int? NativeError, string Frames);
    private static readonly object Sync = new();
    private static readonly Dictionary<Key, long> Counts = [];
    private static long firstChance, overflow, unobserved, unhandled, uiUnhandled;
    private static readonly Dictionary<int, long> UnexpectedBoundaries = [];
    private static AuditListener? listener;
    private static readonly DateTimeOffset Started = DateTimeOffset.UtcNow;
    private static readonly string Output = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SystemCore", "ReliabilityAudit", $"audit-{DateTime.UtcNow:yyyyMMddHHmmss}-{Environment.ProcessId}.json");
    [ThreadStatic] private static bool recording;
    public static void Start(Microsoft.UI.Xaml.Application app)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Output)!);
        if (Directory.EnumerateFiles(Path.GetDirectoryName(Output)!, "audit-*.json").Take(32).Count() >= 32)
            throw new IOException("Audit retention limit reached; archive prior audit files before another diagnostic launch.");
        listener = new AuditListener();
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        AppDomain.CurrentDomain.UnhandledException += (_, _) => { Interlocked.Increment(ref unhandled); Save("Unhandled"); };
        TaskScheduler.UnobservedTaskException += (_, _) => Interlocked.Increment(ref unobserved);
        app.UnhandledException += (_, _) => { Interlocked.Increment(ref uiUnhandled); Save("UIUnhandled"); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Save("ProcessExit");
        Save("Started");
    }
    private static void OnFirstChance(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (recording) return;
        recording = true;
        try
        {
            // The exception's own stack is still partial at first chance. Capture
            // the current call chain, without source-file information, for attribution.
            var frames = new StackTrace(1, false).GetFrames().Take(20)
                .Select(f => f.GetMethod()).Where(m => m is not null)
                .Select(m => (m!.DeclaringType?.FullName ?? "<runtime>") + "." + m.Name)
                .Select(name => name.Length <= 512 ? name : name[..512]);
            var key = new Key(args.Exception.GetType().FullName ?? "<unknown>", args.Exception.HResult,
                (args.Exception as System.ComponentModel.Win32Exception)?.NativeErrorCode, string.Join("\n", frames));
            lock (Sync)
            {
                firstChance++;
                if (Counts.TryGetValue(key, out var count)) Counts[key] = count + 1;
                else if (Counts.Count < 256) Counts.Add(key, 1);
                else overflow++;
            }
        }
        finally { recording = false; }
    }
    private static void Save(string stage)
    {
        lock (Sync)
        {
            File.WriteAllText(Output, JsonSerializer.Serialize(new
            {
                Stage = stage, Started, Ended = DateTimeOffset.UtcNow, FirstChance = firstChance,
                UnobservedTasks = unobserved, Unhandled = unhandled, UIUnhandled = uiUnhandled,
                Overflow = overflow, Groups = Counts.OrderByDescending(p => p.Value)
                    .Select(p => new { p.Key.Type, p.Key.HResult, p.Key.NativeError, p.Key.Frames, Count = p.Value }).ToArray(),
                UnexpectedBoundaries = UnexpectedBoundaries.ToDictionary(p => ((SystemCore.Core.Diagnostics.ReliabilityComponent)p.Key).ToString(), p => p.Value),
                Note = "Audit-only publication. First-chance events do not establish catch outcome. No forced GC; zero unobserved events is limited to the observed lifetime."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private sealed class AuditListener : System.Diagnostics.Tracing.EventListener
    {
        protected override void OnEventSourceCreated(System.Diagnostics.Tracing.EventSource source)
        {
            if (source.Name == "SystemCore-Reliability") EnableEvents(source, System.Diagnostics.Tracing.EventLevel.Error);
        }
        protected override void OnEventWritten(System.Diagnostics.Tracing.EventWrittenEventArgs args)
        {
            if (args.EventId != 1 || args.Payload is not { Count: >= 2 } || args.Payload[0] is not int component) return;
            lock (Sync) UnexpectedBoundaries[component] = UnexpectedBoundaries.GetValueOrDefault(component) + 1;
        }
    }
}
#endif
