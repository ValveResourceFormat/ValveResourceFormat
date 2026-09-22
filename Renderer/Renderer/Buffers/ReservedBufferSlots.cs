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

    // ssbo: fixed slots are for buffers that stay bound across draws. Everything else takes a BufferSlotN
    // and binds it right before its dispatch or draw, so different passes can share a number.

    /// <summary>Per draw data read at the base instance SSBO slot.</summary>
    Instances = 0,
    /// <summary>Transform matrices SSBO slot.</summary>
    Transforms = 1,
    /// <summary>Per scene node lighting data SSBO slot, read by the fragment stage.</summary>
    Objects = 2,
    /// <summary>Barn light constants SSBO slot.</summary>
    BarnLights = 3,
    /// <summary>Tile and depth slice bit masks the cull passes produce.</summary>
    CullBits = 4,
    /// <summary>Quad overdraw debug lock and count pairs SSBO slot.</summary>
    QuadOverdraw = 5,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot6 = 6,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot7 = 7,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot8 = 8,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot9 = 9,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot10 = 10,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot11 = 11,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot12 = 12,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot13 = 13,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot14 = 14,
    /// <summary>Scratch slot for buffers bound right before use.</summary>
    BufferSlot15 = 15,

    /// <summary>Aggregate indirect draw commands SSBO slot.</summary>
    AggregateDraws = BufferSlot12,
    /// <summary>Aggregate draw bounding boxes SSBO slot.</summary>
    AggregateDrawBounds = BufferSlot13,
    /// <summary>Meshlet cull data SSBO slot.</summary>
    AggregateMeshlets = BufferSlot6,
    /// <summary>Occluded bounds debug output SSBO slot.</summary>
    OccludedBoundsDebug = BufferSlot7,
    /// <summary>Compacted draw commands SSBO slot.</summary>
    CompactedDraws = BufferSlot8,
    /// <summary>Compacted draw counts SSBO slot.</summary>
    CompactedCounts = BufferSlot9,
    /// <summary>Meshlet index of each aggregate indirect draw command SSBO slot.</summary>
    AggregateCommandMeshlets = BufferSlot14,

    /// <summary>Guaranteed minimum binding point count in OpenGL 4.6.</summary>
    Max = 8,
}

#pragma warning restore CA1069 // Enum values should not be duplicated
