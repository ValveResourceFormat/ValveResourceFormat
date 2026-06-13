using Microsoft.Extensions.Logging;

namespace GUI.Utils;

internal sealed class GuiLoggerAdapter : ILogger
{
    private readonly string categoryName;

    public GuiLoggerAdapter(string categoryName)
    {
        this.categoryName = categoryName;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
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

        switch (logLevel)
        {
            case LogLevel.Trace:
            case LogLevel.Debug:
                Utils.Log.Debug(categoryName, message);
                break;

            case LogLevel.Information:
                Utils.Log.Info(categoryName, message);
                break;

            case LogLevel.Warning:
                Utils.Log.Warn(categoryName, message);
                break;

            case LogLevel.Error:
            case LogLevel.Critical:
                Utils.Log.Error(categoryName, message);
                break;

            case LogLevel.None:
                break;
        }
    }
}

internal sealed class GuiLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new GuiLoggerAdapter(categoryName);
    }

    public void Dispose()
    {
        //
    }
}
