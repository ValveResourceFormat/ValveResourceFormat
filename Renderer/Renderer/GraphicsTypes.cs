namespace ValveResourceFormat.Renderer;

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
