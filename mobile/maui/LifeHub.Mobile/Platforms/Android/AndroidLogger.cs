using Android.Util;
using Microsoft.Extensions.Logging;

namespace LifeHub.Mobile;

/// <summary>Warnings and errors go to the Android log (adb logcat -s LifeHub), so problems can be found on a phone.</summary>
public sealed class AndroidLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new AndroidLogger(categoryName);
    public void Dispose() { }

    private sealed class AndroidLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var text = $"{category}: {formatter(state, exception)}{(exception is null ? "" : "\n" + exception)}";
            if (logLevel >= LogLevel.Error) Log.Error("LifeHub", text); else Log.Warn("LifeHub", text);
        }
    }
}
