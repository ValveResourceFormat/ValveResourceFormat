namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Reserved GPU buffer binding slots for uniform and storage buffers.
/// </summary>
public enum ReservedBufferSlots
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
    // ubo
    View = 0,
    Lighting = 1,
    EnvironmentMap = 2,
    LightProbe = 3,
    FrustumPlanes = 8,

    // ssbo
    Objects = 9,
    Transforms = 10,
    Histogram = 11,
    AverageLuminance = 12,
    AggregateDraws = 13,
    AggregateDrawBounds = 14,
    AggregateMeshlets = 15,
    OccludedBoundsDebug = 16,

    // do not exceed 16 (8 is the guaranteed minimum in 4.6)
    // todo: separate ssbo and ubo into different sets of slots

#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
}
