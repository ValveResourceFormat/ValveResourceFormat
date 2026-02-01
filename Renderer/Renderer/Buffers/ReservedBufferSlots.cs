namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Reserved GPU buffer binding slots for uniform and storage buffers.
/// </summary>
public enum ReservedBufferSlots
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    View = 0,
    Lighting = 1,
    EnvironmentMap = 2,
    LightProbe = 3,
    Transforms = 10,
    Histogram = 11,
    AverageLuminance = 12,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
