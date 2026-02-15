namespace ValveResourceFormat.Renderer.Buffers;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable CA1069 // Enum values should not be duplicated

/// <summary>
/// Reserved GPU buffer binding slots for uniform and storage buffers.
/// </summary>
public enum ReservedBufferSlots
{
    // ubo
    View = 0,
    Lighting = 1,
    EnvironmentMap = 2,
    LightProbe = 3,
    FrustumPlanes = 4,

    // ssbo
    Objects = 0,
    Transforms = 1,
    Histogram = 2,
    AverageLuminance = 3,
    AggregateDraws = 4,
    AggregateDrawBounds = 5,
    AggregateMeshlets = 6,
    OccludedBoundsDebug = 7,

    Max = 8, // guaranteed minimum in 4.6
}

#pragma warning restore CA1069 // Enum values should not be duplicated
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
