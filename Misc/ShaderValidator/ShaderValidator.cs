internal class ShaderValidator
{
    private class LogProgress() : IProgress<string>
    {
        public void Report(string str) => GUI.Utils.Log.Info(nameof(ShaderValidator), str);
    }

    public static int Main(string[] args)
    {
        var progressReporter = new LogProgress();

        GUI.Utils.Settings.Load();

        var shaderFilter = args.Length > 0 ? $"*{args[0]}*" : null;

        GUI.Types.Renderer.ShaderLoader.ValidateShadersCore(progressReporter, shaderFilter);

        return 0;
    }
}
