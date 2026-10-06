using Microsoft.Extensions.Logging;
namespace LocalCloud.Infrastructure;

public sealed class LocalLogging(string directory) : ILoggerProvider
{
    readonly object gate = new();
    public ILogger CreateLogger(string category) => new FileLogger(this, category);
    public void Dispose() { }
    void Write(string category, LogLevel level, string text) { lock (gate) { Directory.CreateDirectory(directory); var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".log"); if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024) { File.Move(path, path + ".1", true); } File.AppendAllText(path, $"{DateTime.UtcNow:O} [{level}] {category}: {text}\n"); foreach (var old in Directory.EnumerateFiles(directory, "*.log*").Where(p => File.GetLastWriteTimeUtc(p) < DateTime.UtcNow.AddDays(-14))) File.Delete(old); } }
    sealed class FileLogger(LocalLogging provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter) { if (IsEnabled(level)) provider.Write(category, level, formatter(state, error) + (error == null ? "" : " " + error.GetType().Name + ": " + error.Message)); }
    }
}
