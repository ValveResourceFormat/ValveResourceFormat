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

        using var window = new OpenTK.Windowing.Desktop.NativeWindow(new()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            Flags = OpenTK.Windowing.Common.ContextFlags.ForwardCompatible | OpenTK.Windowing.Common.ContextFlags.Offscreen,
            StartVisible = false,
            Title = "Source 2 Viewer Shader Validator"
        });

        window.MakeCurrent();

        ShaderLoader.ValidateShaders(progressReporter, logger, shaderFilter);

        return 0;
    }
}
