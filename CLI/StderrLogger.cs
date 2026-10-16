using Microsoft.Extensions.Logging;

namespace CLI
{
    /// <summary>
    /// Writes library log messages to stderr, so that stdout only has the requested output.
    /// </summary>
    internal sealed class StderrLogger(LogLevel minimumLevel) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= minimumLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);

            if (exception != null)
            {
                message = $"{message}\n{exception}";
            }

            Console.Error.WriteLine(message);
        }
    }
}
