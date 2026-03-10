namespace ValveResourceFormat.Renderer.Buffers;

#pragma warning disable CA1069 // Enum values should not be duplicated

/// <summary>
/// Reserved GPU buffer binding slots for uniform and storage buffers.
/// </summary>
public enum ReservedBufferSlots
{
    // ubo

    /// <summary>View constants UBO slot.</summary>
    View = 0,
    /// <summary>Lighting constants UBO slot.</summary>
    Lighting = 1,
    /// <summary>Environment map array UBO slot.</summary>
    EnvironmentMap = 2,
    /// <summary>Light probe volume array UBO slot.</summary>
    LightProbe = 3,
    /// <summary>Frustum planes UBO slot.</summary>
    FrustumPlanes = 4,

    // ssbo

    /// <summary>Per-object data SSBO slot.</summary>
    Objects = 0,
    /// <summary>Transform matrices SSBO slot.</summary>
    Transforms = 1,
    /// <summary>Histogram SSBO slot.</summary>
    Histogram = 2,
    /// <summary>Average luminance SSBO slot.</summary>
    AverageLuminance = 3,
    /// <summary>Aggregate indirect draw commands SSBO slot.</summary>
    AggregateDraws = 4,
    /// <summary>Aggregate draw bounding boxes SSBO slot.</summary>
    AggregateDrawBounds = 5,
    /// <summary>Meshlet cull data SSBO slot.</summary>
    AggregateMeshlets = 6,
    /// <summary>Occluded bounds debug output SSBO slot.</summary>
    OccludedBoundsDebug = 7,
    /// <summary>Compacted draw commands SSBO slot.</summary>
    CompactedDraws = 8,
    /// <summary>Compacted draw counts SSBO slot.</summary>
    CompactedCounts = 9,
    /// <summary>Compaction request descriptors SSBO slot.</summary>
    CompactionRequests = 10,
    /// <summary>Bone transform matrices SSBO slot.</summary>
    BoneTransforms = 11,
    /// <summary>Barn light constants SSBO slot.</summary>
    BarnLights = 12,

    /// <summary>Guaranteed minimum binding point count in OpenGL 4.6.</summary>
    Max = 8,
}

#pragma warning restore CA1069 // Enum values should not be duplicated
