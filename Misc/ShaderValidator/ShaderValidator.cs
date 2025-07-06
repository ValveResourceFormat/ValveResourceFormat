internal class ShaderValidator
{
    private class LogProgress() : IProgress<string>
    {
        public void Report(string str) => GUI.Utils.Log.Info(nameof(ShaderValidator), str);
    }

    public static int Main()
    {
        var progressReporter = new LogProgress();

        GUI.Utils.Settings.Load();

        GUI.Types.Renderer.ShaderLoader.ValidateShadersCore(progressReporter);

        return 0;
    }
}
