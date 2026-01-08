using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer;

internal class ShaderValidator
{
    private class LogProgress(ILogger logger) : IProgress<string>
    {
        public void Report(string str) => logger.LogInformation("{Message}", str);
    }

    public static int Main(string[] args)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var logger = loggerFactory.CreateLogger<ShaderValidator>();
        var progressReporter = new LogProgress(logger);

        var shaderFilter = args.Length > 0 ? $"*{args[0]}*" : null;

        ShaderLoader.ValidateShaders(progressReporter, logger, shaderFilter);

        return 0;
    }
}
