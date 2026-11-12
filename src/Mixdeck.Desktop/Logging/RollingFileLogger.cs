using Microsoft.Extensions.Logging;

namespace Mixdeck.Desktop.Logging;

public sealed class RollingFileLoggerProvider(string directory) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(directory, categoryName);
    public void Dispose() { }

    private sealed class FileLogger(string directory, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "mixdeck-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".log");
                var line = DateTimeOffset.Now.ToString("O") + " " + logLevel + " " + category + " " + formatter(state, exception);
                if (exception is not null) line += " " + exception;
                File.AppendAllText(path, line + Environment.NewLine);
            }
            catch (IOException) { }
        }
    }
}
