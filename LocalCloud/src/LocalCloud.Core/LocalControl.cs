using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace LocalCloud.Core;

// Same-user local IPC only; no unauthenticated network shutdown endpoint.
public static class LocalControl
{
    public static string Name(string role, string path)
    {
        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        if (OperatingSystem.IsWindows()) canonical = canonical.ToUpperInvariant();
        return "localcloud-" + role + "-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..24];
    }

    public static async Task<string?> Send(string role, string path, string command, int timeoutMs = 1500)
    {
        if (!OperatingSystem.IsWindows()) return await SendFile(role, path, command, timeoutMs);
        try
        {
            using var timeout = new CancellationTokenSource(timeoutMs);
            await using var pipe = new NamedPipeClientStream(".", Name(role, path), PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(command.AsMemory(), timeout.Token);
            return await reader.ReadLineAsync(timeout.Token);
        }
        catch (Exception e) when (e is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException or System.Net.Sockets.SocketException) { return null; }
    }

    // Portable dev/test hosts may prohibit Unix sockets. Windows always uses the
    // same-user named pipe; this private inbox is the non-Windows transport only.
    public static string Inbox(string role, string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "." + Name(role, path));
    static async Task<string?> SendFile(string role, string path, string command, int timeoutMs)
    {
        var directory = Inbox(role, path); if (!Directory.Exists(directory)) return null;
        var request = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".request"); var reply = request + ".reply";
        try
        {
            await File.WriteAllTextAsync(request + ".tmp", command);
            File.Move(request + ".tmp", request);
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until) { if (File.Exists(reply)) return await File.ReadAllTextAsync(reply); await Task.Delay(50); }
            return null;
        }
        catch (IOException) { return null; }
        finally { foreach (var file in new[] { request, request + ".tmp", reply }) try { File.Delete(file); } catch (IOException) { } }
    }
}
