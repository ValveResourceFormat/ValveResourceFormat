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
    /// <summary>Shared constants for the tile and depth bin cull passes.</summary>
    CullParams = 5,
    /// <summary>Per scene cull mask layout read by the shading passes.</summary>
    LightCull = 6,
    /// <summary>Packed material properties.</summary>
    Globals = 7,

    // ssbo

    /// <summary>Per-object data SSBO slot.</summary>
    Objects = 0,
    /// <summary>Transform matrices SSBO slot.</summary>
    Transforms = 1,

    /// <summary>Scratch slot for buffers.</summary>
    BufferSlot2 = 2,
    /// <summary>Scratch slot for buffers.</summary>
    BufferSlot3 = 3,

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
    /// <summary>Bone transform matrices SSBO slot.</summary>
    BoneTransforms = 10,
    /// <summary>Barn light constants SSBO slot.</summary>
    BarnLights = 11,
    /// <summary>Tile and depth slice bit masks the cull passes produce.</summary>
    CullBits = 12,
    /// <summary>Quad overdraw debug lock and count pairs SSBO slot.</summary>
    QuadOverdraw = 13,

    /// <summary>Guaranteed minimum binding point count in OpenGL 4.6.</summary>
    Max = 8,
}

#pragma warning restore CA1069 // Enum values should not be duplicated
