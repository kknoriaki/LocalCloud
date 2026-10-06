using System.IO.Pipes;
using System.Text.Json;
using LocalCloud.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LocalCloud.Infrastructure;

public sealed class UpdateActivity
{
    readonly object gate = new();
    int requests;
    public bool Draining { get; private set; }
    public bool Enter() { lock (gate) { if (Draining) return false; requests++; return true; } }
    public void Leave() { lock (gate) requests--; }
    public bool Prepare(bool busy) { lock (gate) { if (busy || requests != 0) return false; Draining = true; return true; } }
    public void Resume() { lock (gate) Draining = false; }
    public int Requests { get { lock (gate) return requests; } }
}

public sealed class UpdateControl(SettingsStore settings, Database database, Indexer indexer, UpdateActivity activity, IHostApplicationLifetime lifetime, ILogger<UpdateControl> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) { await ListenPortable(stoppingToken); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(LocalControl.Name("server", settings.ConfigPath), PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(stoppingToken);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var command = await reader.ReadLineAsync(timeout.Token);
                if (command == "stop") { await writer.WriteLineAsync("{\"stopping\":true}"); lifetime.StopApplication(); } else await writer.WriteLineAsync(Reply(command));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) when (e is IOException or OperationCanceledException) { log.LogDebug(e, "Local update control disconnected"); }
        }
    }
    string Reply(string? command)
    {
        var uploads = Convert.ToInt32(database.Rows("SELECT COUNT(*) AS n FROM UploadSessions WHERE status NOT IN ('complete','skipped','cancelled')")[0]["n"]);
        var busy = indexer.Scanning || activity.Requests > 0;
        if(command=="prepare")indexer.Pause();
        var prepared = command == "prepare" && activity.Prepare(busy);
        if (command == "resume") {activity.Resume();indexer.Resume();}
        return JsonSerializer.Serialize(new { pid = Environment.ProcessId, serverPath = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar), uploads, indexing = indexer.Scanning, requests = activity.Requests, prepared, version = "1.0.0" });
    }
    async Task ListenPortable(CancellationToken ct)
    {
        var directory = LocalControl.Inbox("server", settings.ConfigPath); Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                foreach (var request in Directory.GetFiles(directory, "*.request"))
                {
                    if (new FileInfo(request).Length > 128) { File.Delete(request); continue; }
                    var command = await File.ReadAllTextAsync(request, ct);
                    var reply = request + ".reply";
                    await File.WriteAllTextAsync(reply + ".tmp", command=="stop"?"{\"stopping\":true}":Reply(command), ct); File.Move(reply + ".tmp", reply, true); File.Delete(request);
                    if (command == "stop") lifetime.StopApplication();
                }
                await Task.Delay(50, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }
}
