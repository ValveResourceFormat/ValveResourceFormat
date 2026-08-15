using Microsoft.Extensions.Logging;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Shaders;

internal class ShaderValidator
{
    private sealed class ConsoleProgress : IProgress<string>
    {
        public void Report(string str) => Console.WriteLine(str);
    }

    public static int Main(string[] args)
    {
        // Warning level hides the per-variant "compiled successfully" spam, progress is reported directly instead
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Warning)
            .AddSimpleConsole(options => options.SingleLine = true));
        var logger = loggerFactory.CreateLogger<ShaderValidator>();
        var progressReporter = new ConsoleProgress();

        var shaderFilter = args.Length > 0 ? args[0] : null;

        using var window = new OpenTK.Windowing.Desktop.NativeWindow(new()
        {
            APIVersion = GLEnvironment.RequiredVersion,
            Flags = OpenTK.Windowing.Common.ContextFlags.ForwardCompatible | OpenTK.Windowing.Common.ContextFlags.Offscreen,
            StartVisible = false,
            Title = "Source 2 Viewer Shader Validator"
        });

        window.MakeCurrent();

        try
        {
            ShaderLoader.ValidateShaders(progressReporter, logger, shaderFilter);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }

        return 0;
    }
}
