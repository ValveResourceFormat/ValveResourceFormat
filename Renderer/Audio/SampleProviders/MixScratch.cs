namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// Scratch buffers for the mix tree, one per nesting level per thread. A mix reads its children into a
/// scratch buffer before summing them, and those reads nest, so several buffers are live at once - which
/// is why <see cref="System.Buffers.ArrayPool{T}"/> is a poor fit (its thread cache holds one array per
/// bucket, so every level past the first goes to the shared per-core stacks and allocates when they are
/// cold or trimmed), and why a buffer per provider is one too: the mix tree grows a provider at a time as
/// events and their children are built, so each new one paid its own allocation. Keyed on depth instead,
/// the whole mixer needs as many buffers as the tree is deep, allocated on the first mix and reused for
/// the process lifetime.
/// </summary>
internal static class MixScratch
{
    [ThreadStatic]
    private static float[]?[]? buffers;

    [ThreadStatic]
    private static int depth;

    /// <summary>
    /// Gets the current nesting level's buffer, at least <paramref name="minimumLength"/> long, and
    /// enters the next level. Contents are undefined. Every call must be matched by a <see cref="Pop"/>.
    /// </summary>
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
            // The mix chunk size is fixed, so a level allocates once and never again
            buffer = new float[minimumLength];
            levels[depth] = buffer;
        }

        depth++;
        return buffer;
    }

    /// <summary>Leaves the nesting level entered by <see cref="Push"/>.</summary>
    public static void Pop() => depth--;
}
