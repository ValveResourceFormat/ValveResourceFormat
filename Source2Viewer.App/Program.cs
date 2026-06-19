using Avalonia;
using Avalonia.OpenGL;

namespace Source2Viewer.App;

internal static class Program
{
    public static string[] StartupArgs { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        if (args is ["--self-check"])
        {
            MainWindow.RunExportPathSelfCheck();
            return;
        }

        StartupArgs = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                GlProfiles =
                [
                    new GlVersion(GlProfileType.OpenGL, 4, 6),
                    new GlVersion(GlProfileType.OpenGL, 4, 5),
                    new GlVersion(GlProfileType.OpenGL, 4, 3),
                ],
            })
            .WithInterFont()
            .LogToTrace();
    }
}
