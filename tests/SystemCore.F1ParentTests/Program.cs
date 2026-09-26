using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

// Isolated parent-path/pipe integration probe. The payload is always invalid:
// Version=0 rejects before any inventory/mutation. It never targets a file/item.
if (args.Length != 3 || args[1] is not ("cleaner" or "startup") || args[2] is not ("accept-parent" or "reject-parent")) return 64;
using var identity = WindowsIdentity.GetCurrent();
if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
{
    Console.Error.WriteLine("Probe requires an already elevated validation controller; it never requests elevation.");
    return 5;
}
var expectConnection = args[2] == "accept-parent";
var endpoint = args[1];
var nonce = new string('A', 64);
var pipeName = $"SystemCore-{(endpoint == "cleaner" ? "Cleaner" : "Startup")}-{Guid.NewGuid():N}";
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
var start = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, CreateNoWindow = true };
foreach (var arg in new[] { "--" + endpoint, pipeName, Environment.ProcessId.ToString(), nonce }) start.ArgumentList.Add(arg);
using var helper = Process.Start(start) ?? throw new IOException("Probe helper did not start");
var connecting = pipe.WaitForConnectionAsync(deadline.Token);
var exiting = helper.WaitForExitAsync(deadline.Token);
var connected = false;
bool? responseRejected = null;
try
{
    await Task.WhenAny(connecting, exiting);
    if (connecting.IsCompletedSuccessfully)
    {
        connected = true;
        var request = endpoint == "cleaner"
            ? JsonSerializer.SerializeToUtf8Bytes(new { Version = 0, Nonce = nonce, SessionId = Guid.NewGuid(), ScannedAt = DateTimeOffset.UtcNow, Items = Array.Empty<object>() })
            : JsonSerializer.SerializeToUtf8Bytes(new { Version = 0, Nonce = nonce, CreatedAt = DateTimeOffset.UtcNow, ItemId = nonce, Fingerprint = nonce, Action = 0 });
        await pipe.WriteAsync(BitConverter.GetBytes(request.Length), deadline.Token);
        await pipe.WriteAsync(request, deadline.Token);
        await pipe.FlushAsync(deadline.Token);
        if (endpoint == "cleaner")
        {
            var header = new byte[4]; await pipe.ReadExactlyAsync(header, deadline.Token);
            var size = BitConverter.ToInt32(header);
            if (size is <= 0 or > 4096) throw new InvalidDataException("Unexpected response size");
            var payload = new byte[size]; await pipe.ReadExactlyAsync(payload, deadline.Token);
            using var response = JsonDocument.Parse(payload);
            responseRejected = !response.RootElement.GetProperty("Accepted").GetBoolean()
                && response.RootElement.GetProperty("Items").GetArrayLength() == 0;
        }
    }
    await exiting;
    var passed = expectConnection
        ? connected && helper.ExitCode == 64 && (endpoint != "cleaner" || responseRejected == true)
        : !connected && helper.ExitCode == 5;
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        Test = $"{endpoint}: {args[2]}", Passed = passed, Parent = Environment.ProcessPath,
        PipeConnected = connected, HelperExit = helper.ExitCode, ResponseRejected = responseRejected,
        Payload = "Version 0; no eligible target; rejected before any operation", AlreadyElevatedFixture = true
    }));
    return passed ? 0 : 1;
}
finally
{
    deadline.Cancel();
    try { await connecting; } catch (OperationCanceledException) { }
    // The helper owns its bounded deadline; no termination of any user process.
}
