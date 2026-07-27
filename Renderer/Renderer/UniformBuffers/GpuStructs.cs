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
    /// <summary>Packed bounding box used for meshlet frustum and occlusion culling.</summary>
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
    /// <summary>
    /// Packed index pair: light probe volume in the low 16 bits, env map array index in the high 16. Both
    /// are bounded by their arrays, which cap far below 65535, so sharing a word costs nothing and keeps
    /// this struct at 16 bytes. The env map half is read only by the legacy per batch cube map path; the
    /// array path culls probes per screen tile and has no per object probe list.
    /// </summary>
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

/// <summary>
/// One item class culled by <c>compute_tile_cullbits</c> and <c>compute_depthbin_cullbits</c>. Three are
/// dispatched together, so a single pass covers barn lights, env maps and light probe volumes at once.
/// </summary>
/// <remarks>Matches <c>CullBatch_t</c>, 16 bytes.</remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CullBatch
{
    /// <summary>First uint of this batch's region in the cull bits buffer.</summary>
    public uint OutputOffset;
    /// <summary>Masks per tile or per depth bin, that is <c>ceil(itemCount / 32)</c>.</summary>
    public uint OutputStride;
    /// <summary>
    /// First index into the cull item buffer. Must be a multiple of 32: the depth bin shader shifts by the
    /// absolute item index while the tile shader shifts by the mask relative one, and they only agree
    /// under that alignment.
    /// </summary>
    public uint FirstItem;
    /// <summary>One past this batch's last item index.</summary>
    public uint ItemEnd;
};

/// <summary>
/// Screen space description of one cull item. The whole test is 2D plus a view depth range: an AABB, an
/// optional implicit conic, and up to two convex hulls whose vertices live in the cull plane buffer.
/// Frustum rejection is a byproduct of building this, so the compute passes never see world space.
/// </summary>
/// <remarks>
/// Matches <c>CullItem_t</c>, 80 bytes. The two unused fields are present in Source 2's layout but read by
/// neither cull shader, so they are kept as padding rather than guessed at.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CullItem
{
    /// <summary>Index into the cull plane buffer of hull 0's first vertex.</summary>
    public uint FirstPlane;
    /// <summary>Unused by both cull shaders.</summary>
    public uint _Unused04;
    /// <summary>Vertex count of hull 0. Zero skips the hull test.</summary>
    public uint NumPlanes0;
    /// <summary>Vertex count of hull 1, which starts at <see cref="FirstPlane"/> + <see cref="NumPlanes0"/>.</summary>
    public uint NumPlanes1;
    /// <summary>Minimum corner of the screen AABB, in the same space as the tile centers.</summary>
    public Vector2 BoundsMin;
    /// <summary>Maximum corner of the screen AABB.</summary>
    public Vector2 BoundsMax;
    /// <summary>Nearest view depth the item reaches, used only by the depth bin pass.</summary>
    public float DepthMin;
    /// <summary>Furthest view depth the item reaches.</summary>
    public float DepthMax;
    /// <summary>
    /// X is the item's nearest NDC z, used by the optional occlusion test in <c>compute_tile_cullbits</c>.
    /// Source 2 reads neither float at this offset, so this borrows a slot its layout already spends.
    /// </summary>
    public Vector2 NdcDepthNear;
    /// <summary>Conic x squared coefficient. The test is <c>A x^2 + B y^2 + C xy + D x + E y + F &gt;= 0</c>.</summary>
    public float ConicXX;
    /// <summary>Conic y squared coefficient.</summary>
    public float ConicYY;
    /// <summary>Conic xy coefficient.</summary>
    public float ConicXY;
    /// <summary>Conic x coefficient.</summary>
    public float ConicX;
    /// <summary>Conic y coefficient.</summary>
    public float ConicY;
    /// <summary>Conic constant term.</summary>
    public float ConicConst;
    /// <summary>Exactly zero disables the conic test, which is what AABB only items use.</summary>
    public float ConicEnable;
    /// <summary>Padding to 80 bytes.</summary>
    public float _Unused76;
};

/// <summary>Three <see cref="CullBatch"/> entries, matching the fixed size arrays in <c>CullParams_t</c>.</summary>
[InlineArray(3)]
public struct CullBatchTriple
{
    private CullBatch Batch0;
}

/// <summary>
/// Shared constants for both cull passes.
/// </summary>
/// <remarks>
/// Matches <c>CullParams_t</c>, 160 bytes. Sequential .NET packing reproduces the std140 offsets, which in
/// turn reproduce the packoffsets Source 2's own build uses (c0, c1, c3, c4, c7).
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public struct CullParams
{
    /// <summary>Total tile count. Read by neither shader, present to keep the layout intact.</summary>
    public uint Tiles;
    /// <summary>Tile grid width.</summary>
    public uint TilesX;
    /// <summary>Tile grid height.</summary>
    public uint TilesY;
    /// <summary>
    /// Conservative hull dilation in cull space units. Tiles are tested as their center point, so this is
    /// what accounts for a tile's area; a half diagonal makes the test conservative.
    /// </summary>
    public float TileEpsilon;
    /// <summary>Scale mapping a tile index to its center in cull space.</summary>
    public Vector2 TileToCenterScale;
    /// <summary>Offset mapping a tile index to its center in cull space.</summary>
    public Vector2 TileToCenterOffset;
    /// <summary>Depth bin count. Read by neither shader, the depth bin dispatch is exact.</summary>
    public uint DepthBins;
    /// <summary>Width of one depth bin. Bins are linear in view depth, not logarithmic.</summary>
    public float DepthBinWidth;
    /// <summary>Conservative depth bin dilation.</summary>
    public float BinEpsilon;
    /// <summary>View depth of bin 0's near edge.</summary>
    public float NearPlane;
    /// <summary>
    /// First mask index of batch 0, which is always zero. Both shaders use the dispatch's mask axis to find
    /// which batch they belong to and the mask index within it.
    /// </summary>
    public uint FirstMaskForBatch0;
    /// <summary>First mask index of batch 1.</summary>
    public uint FirstMaskForBatch1;
    /// <summary>First mask index of batch 2.</summary>
    public uint FirstMaskForBatch2;
    /// <summary>Total mask count across all three batches. Read by neither shader; it is the dispatch size.</summary>
    public uint MaskCount;
    /// <summary>Output layout of each batch in the tile pass.</summary>
    public CullBatchTriple TileBatches;
    /// <summary>Output layout of each batch in the depth bin pass.</summary>
    public CullBatchTriple BinBatches;
};
