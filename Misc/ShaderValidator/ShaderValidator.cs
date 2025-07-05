
internal class Program
{
    public static int Main(string[] args)
    {
        var progressReporter = new Progress<string>(Console.WriteLine);

        GUI.Utils.Settings.Load();
        GUI.Types.Renderer.ShaderParser.ShadersFolderPathOnDisk = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

        GUI.Types.Renderer.ShaderLoader.ValidateShadersCore(progressReporter);
        return 0;
    }
}
