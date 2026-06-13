using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Buffers;

[InlineArray(6)]
internal struct FrustumPlanesGpu
{
    private Plane Plane0;

    public FrustumPlanesGpu(Frustum frustum)
    {
        for (var i = 0; i < 6; i++)
        {
            this[i] = frustum.Planes[i];
        }
    }
}

/// <summary>GPU culling data for a single meshlet, including bounds and cone.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MeshletCullInfo
{
    /// <summary>Bounding sphere used for meshlet frustum and occlusion culling.</summary>
    public Meshlet.MeshletBounds Bounds { get; init; }
    /// <summary>Normal cone used for backface culling of the meshlet.</summary>
    public Meshlet.MeshletCone Cone { get; init; }
    /// <summary>Index into the draw bounds array for this meshlet's parent draw call.</summary>
    public uint ParentDrawBoundsIndex;
}

/// <summary>Axis-aligned bounding box for a draw call, used in GPU culling.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DrawBounds
{
    /// <summary>Minimum corner of the bounding box.</summary>
    public Vector3 Min;
    /// <summary>Padding to satisfy alignment requirements.</summary>
    public uint _Padding1;
    /// <summary>Maximum corner of the bounding box.</summary>
    public Vector3 Max;
    /// <summary>Padding to satisfy alignment requirements.</summary>
    public uint _Padding2;
};

/// <summary>Debug bounding box for a draw call that was culled as occluded.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OccludedBoundDebug
{
    /// <summary>Minimum corner of the occluded bounding box.</summary>
    public Vector3 Min;
    /// <summary>Padding to satisfy alignment requirements.</summary>
    public float _Padding1;
    /// <summary>Maximum corner of the occluded bounding box.</summary>
    public Vector3 Max;
    /// <summary>Padding to satisfy alignment requirements.</summary>
    public float _Padding2;
};

/// <summary>Per-object GPU data including tint, transform, and environment map visibility.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ObjectDataStandard
{
    /// <summary>Packed tint color and alpha for the object.</summary>
    public uint TintAlpha;
    /// <summary>Index into the transform buffer for this object.</summary>
    public uint TransformIndex;
    /// <summary>Index of the visible light probe volume for this object.</summary>
    public uint VisibleLPV;
    /// <summary>Unique identifier for this object used in selection and highlighting.</summary>
    public uint Identification;
    /// <summary>Bitmask of which environment maps are visible to this object.</summary>
    public SceneEnvMap.EnvMapVisibility128 EnvMapVisibility;
};


/// <summary>Arguments for a <c>glDrawElementsIndirect</c> GPU draw call.</summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct DrawElementsIndirectCommand
{
    /// <summary>Number of indices to draw.</summary>
    public readonly uint Count { get; init; }
    /// <summary>Number of instances to draw.</summary>
    public readonly uint InstanceCount { get; init; }
    /// <summary>Starting index offset into the index buffer.</summary>
    public readonly uint FirstIndex { get; init; }
    /// <summary>Constant added to each index before fetching a vertex.</summary>
    public readonly int BaseVertex { get; init; }
    /// <summary>Base instance used to index per-instance data.</summary>
    public readonly uint BaseInstance { get; init; }
};
