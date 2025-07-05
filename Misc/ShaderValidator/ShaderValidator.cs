internal class ShaderValidator
{
    public static int Main()
    {
        var progressReporter = new Progress<string>(static (str) => GUI.Utils.Log.Info(nameof(ShaderValidator), str));

        GUI.Utils.Settings.Load();
        GUI.Types.Renderer.ShaderParser.ShadersFolderPathOnDisk = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));

        GUI.Types.Renderer.ShaderLoader.ValidateShadersCore(progressReporter);
        return 0;
    }
}
