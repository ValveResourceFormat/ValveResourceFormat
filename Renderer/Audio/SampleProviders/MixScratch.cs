namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

internal static class MixScratch
{
    [ThreadStatic]
    private static float[]?[]? buffers;

    [ThreadStatic]
    private static int depth;

    public static float[] Push(int minimumLength)
    {
        var levels = buffers ??= new float[8][];

        if (depth == levels.Length)
        {
            Array.Resize(ref levels, levels.Length * 2);
            buffers = levels;
        }

        var buffer = levels[depth];

        if (buffer == null || buffer.Length < minimumLength)
        {
            buffer = new float[minimumLength];
            levels[depth] = buffer;
        }

        depth++;
        return buffer;
    }

    public static void Pop() => depth--;
}
