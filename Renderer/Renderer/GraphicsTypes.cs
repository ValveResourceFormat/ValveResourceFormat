namespace ValveResourceFormat.Renderer;

/// <summary>
/// Shape of a texture's storage.
/// </summary>
public enum TextureType
{
    /// <summary>One two dimensional image.</summary>
    Texture2D,

    /// <summary>An array of two dimensional images that share a size and format.</summary>
    Texture2DArray,

    /// <summary>A two dimensional image with more than one sample per texel.</summary>
    Texture2DMultisample,

    /// <summary>A volume filtered across its slices.</summary>
    Texture3D,

    /// <summary>Six faces forming one cube.</summary>
    TextureCube,

    /// <summary>An array of cubes.</summary>
    TextureCubeArray,
}

/// <summary>
/// What a query object measures.
/// </summary>
public enum QueryType
{
    /// <summary>GPU nanoseconds spent on the commands the query encloses.</summary>
    TimeElapsed,

    /// <summary>One point on the GPU timeline in nanoseconds, recorded rather than begun and ended.</summary>
    Timestamp,

    /// <summary>Primitives produced by the draws the query encloses.</summary>
    PrimitivesGenerated,
}

/// <summary>
/// One programmable stage of a pipeline.
/// </summary>
public enum ShaderStage
{
    /// <summary>Runs once per vertex.</summary>
    Vertex,

    /// <summary>Runs once per fragment.</summary>
    Fragment,

    /// <summary>Runs once per work item of a dispatch, outside the draw pipeline.</summary>
    Compute,
}

/// <summary>
/// How the pipeline reads a buffer, which decides the slot namespace it binds into.
/// </summary>
public enum BufferType
{
    /// <summary>Read only constants, small enough to live in fast constant storage.</summary>
    Uniform,

    /// <summary>Read write structured data, sized past what constant storage takes.</summary>
    Storage,

    /// <summary>Draw arguments consumed by indirect draws.</summary>
    Indirect,

    /// <summary>The draw count consumed by indirect draws that source it from the GPU.</summary>
    IndirectCount,
}

/// <summary>
/// Who writes a buffer and who reads it, which decides the memory it is allocated from.
/// </summary>
public enum BufferUsage
{
    /// <summary>Written by the CPU only when it changes, read by the GPU.</summary>
    Static,

    /// <summary>Written by the CPU most frames, read by the GPU.</summary>
    Dynamic,

    /// <summary>Written and read by the GPU, never mapped by the CPU.</summary>
    GpuOnly,

    /// <summary>Written by the GPU and read back by the CPU.</summary>
    Readback,
}
