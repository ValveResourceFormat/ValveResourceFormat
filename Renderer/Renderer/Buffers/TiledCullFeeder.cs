using System.Diagnostics;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Buffers;

/// <summary>
/// Everything one barn light face is culled by, in world space. The shader lights a fragment only where
/// all three agree, so the item built from them is their intersection.
/// </summary>
public readonly struct BarnLightCullVolume
{
    /// <summary>Gets the map from the face's clip cube, x and y in [-1,1] and z in [0,1], to world space.</summary>
    public Matrix4x4 FrustumToWorld { get; init; }

    /// <summary>Gets the map from the cube [-1,1]^3 to the illumination OBB, or default when there is none.</summary>
    public Matrix4x4 ObbToWorld { get; init; }

    /// <summary>Gets the fade out sphere, centre in xyz and radius in w. Radius 0 means no distance cutoff.</summary>
    public Vector4 RangeSphere { get; init; }
}

/// <summary>
/// CPU side of <c>compute_tile_cullbits</c> and <c>compute_depthbin_cullbits</c>: projects world space
/// volumes into the screen AABBs, hulls and depth keys those passes rasterize into bits.
/// </summary>
/// <remarks>
/// Neither compute pass sees world space, so frustum rejection happens here, and a rejected item costs a
/// bit rather than a test - item count sets the word stride, the buffer size and the shading loop's
/// length. Cull space is pixels and depth is distance along the view axis.
/// <c>common/light_cull.slang</c> defines both the same way and the two must not drift.
/// </remarks>
public sealed class TiledCullFeeder
{
    /// <summary>Item classes culled together in one dispatch. Batch 2 is reserved for light probe volumes.</summary>
    public const int BatchCount = 3;

    /// <summary>Batch index holding barn light faces.</summary>
    public const int BatchBarnLights = 0;

    /// <summary>Batch index holding environment map probes.</summary>
    public const int BatchEnvMaps = 1;

    /// <summary>Items per mask. Batches pad up to a whole number of these so no word spans two batches.</summary>
    public const int ItemsPerMask = 32;

    /// <summary>Vertex budget per hull. A dilated box silhouette needs 10; past this an item falls back to a rect.</summary>
    private const int MaxHullVertices = 16;

    /// <summary>Hulls one item can own. A barn light face uses both: its frustum and its OBB.</summary>
    private const int MaxHullsPerItem = 2;

    /// <summary>Slack on the depth bin overlap test, in world units.</summary>
    private const float DepthBinEpsilon = 0.001f;

    /// <summary>
    /// Skin on a probe's box before it is projected. Must stay at or above the shading skin
    /// <see cref="SceneEnvMap.BoundsExtend"/>, or a probe drops from tiles it still lights.
    /// </summary>
    private const float EnvMapCullExtend = 0.1f;

    /// <summary>
    /// Slots reserved per batch. Must be a multiple of <see cref="ItemsPerMask"/>: <see cref="End"/> pads
    /// each batch out to whole masks, so a stride that is not would run past the item array.
    /// </summary>
    private const int MaxItemsPerBatch = 320; // 10 words
    private const int MaxItems = BatchCount * MaxItemsPerBatch;

    private const int TileGroupSizeX = 8;
    private const int TileGroupSizeY = 4;
    private const int BinGroupSizeX = 32;

    private readonly CullItem[] items = new CullItem[MaxItems];

    /// <summary>Hull vertices, packed back to back and indexed by <see cref="CullItem.FirstPlane"/>.</summary>
    private readonly Vector2[] planes = new Vector2[MaxItems * MaxHullVertices * MaxHullsPerItem];

    private readonly Vector2[] projected = new Vector2[20];
    private readonly Vector2[] minkowski = new Vector2[20 * 4];
    private readonly Vector2[] hull = new Vector2[(20 * 4) + 1];
    private readonly int[] batchItemCount = new int[BatchCount];
    private readonly int[] batchBinnedCount = new int[BatchCount];
    private readonly int[] batchFirstItem = new int[BatchCount];

    private CullParams cullParams;
    private int tileCols;
    private int tileRows;
    private int depthBins;
    private int maskCount;

    private Vector2 viewportSize;
    private Vector3 cameraPosition;
    private Vector3 cameraDirection;
    private Matrix4x4 worldToProjection;
    private Matrix4x4 projectionToWorld;
    private bool projectionToWorldValid;
    private float cameraNearPlane;
    private float minSliceFar;
    private float maxSliceFar;
    private float tileHalfSize;

    /// <summary>Last coordinate the tile grid reaches, past the viewport on a partial edge tile.</summary>
    private Vector2 cullSpaceMax;
    private int planeCount;

    /// <summary>Gets the uints the cull bits buffer needs for this frame's layout.</summary>
    public int TotalWords { get; private set; }

    /// <summary>Gets the mask count across all batches, which is the dispatch's mask axis.</summary>
    public int MaskCount => maskCount;

    /// <summary>Gets the viewport the items were projected against, which is the extent of cull space.</summary>
    public Vector2 ViewportSize => viewportSize;

    /// <summary>Gets the constants both compute passes read.</summary>
    public ref readonly CullParams Params => ref cullParams;

    /// <summary>Gets the backing array to upload from.</summary>
    public CullItem[] ItemArray => items;

    /// <summary>Gets the live entries of <see cref="ItemArray"/> this frame, tail padding included.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Gets the hull vertex array <see cref="CullItem.FirstPlane"/> indexes.</summary>
    public Vector2[] PlaneArray => planes;

    /// <summary>Gets the live entries of <see cref="PlaneArray"/> this frame.</summary>
    public int PlaneCount => planeCount;

    /// <summary>Gets how far along the view axis the furthest item this frame reached.</summary>
    /// <remarks>
    /// The slice distribution is fitted to this rather than to the render far plane: slices are uniform in
    /// view depth, so every empty unit past the last light thickens all of them. Unbounded volumes are
    /// excluded, since they would pin it to its ceiling.
    /// </remarks>
    public float MaxItemViewDepth { get; private set; }

    /// <summary>Gets the far distance the slice distribution was fitted to this frame, in world units.</summary>
    public float SliceFar { get; private set; }

    /// <summary>Gets the view depth one slice spans, in world units. Valid after <see cref="End"/>.</summary>
    public float SliceWidth => cullParams.DepthBinWidth;

    /// <summary>Gets the first uint of a batch's tile region.</summary>
    public uint TileBase(int batch) => cullParams.TileBatches[batch].OutputOffset;

    /// <summary>Gets the first uint of a batch's depth bin region.</summary>
    public uint BinBase(int batch) => cullParams.BinBatches[batch].OutputOffset;

    /// <summary>Gets a batch's word stride, <c>ceil(itemCount / 32)</c>.</summary>
    public uint Stride(int batch) => cullParams.TileBatches[batch].OutputStride;

    /// <summary>Gets the slots a batch claimed, which is every item the shading pass iterates.</summary>
    /// <param name="batch">Batch index.</param>
    public int SlotCount(int batch) => batchItemCount[batch];

    /// <summary>
    /// Gets the slots holding an item that can match a tile. The gap to <see cref="SlotCount"/> is what
    /// projected to nothing - behind the near plane or off screen - and the CPU rejected outright.
    /// </summary>
    /// <param name="batch">Batch index.</param>
    public int BinnedCount(int batch) => batchBinnedCount[batch];

    /// <summary>Whether an item projected to nothing and can never match a tile or a bin.</summary>
    private static bool IsRejected(in CullItem item) => item.DepthMin > item.DepthMax;

    /// <summary>Starts a frame, capturing everything the batches added afterwards are projected against.</summary>
    public void Begin(
        int tileCols, int tileRows, int tileSize,
        int depthBins, float minSliceFar, float maxSliceFar,
        Vector2 viewportSize,
        in Matrix4x4 worldToProjection,
        Vector3 cameraPosition, Vector3 cameraDirection, float cameraNearPlane)
    {
        // The depth bin dispatch is exact and the shader does not bound check, so a bin count that is not
        // a whole number of groups leaves the tail bins holding whatever the previous layout wrote there.
        Debug.Assert(depthBins % BinGroupSizeX == 0, $"Depth bin count must be a multiple of {BinGroupSizeX}");

        // End pads every batch out to whole masks, so a stride that is not a multiple of one writes past
        // the item array.
        Debug.Assert(MaxItemsPerBatch % ItemsPerMask == 0, $"Batch stride must be a multiple of {ItemsPerMask}");

        Debug.Assert(EnvMapArray.MAX_ENVMAPS <= MaxItemsPerBatch,
            $"Env map probes must fit the {MaxItemsPerBatch} slot batch stride");

        Debug.Assert(BarnLightConstants.MAX_BARN_LIGHTS <= MaxItemsPerBatch,
            $"Barn light faces must fit the {MaxItemsPerBatch} slot batch stride");

        this.tileCols = tileCols;
        this.tileRows = tileRows;
        this.depthBins = depthBins;
        this.viewportSize = viewportSize;
        this.worldToProjection = worldToProjection;
        projectionToWorldValid = Matrix4x4.Invert(worldToProjection, out projectionToWorld);
        this.cameraPosition = cameraPosition;
        this.cameraDirection = cameraDirection;
        this.cameraNearPlane = cameraNearPlane;
        this.minSliceFar = minSliceFar;
        this.maxSliceFar = maxSliceFar;

        Array.Clear(batchItemCount);
        Array.Clear(batchBinnedCount);
        MaxItemViewDepth = 0f;
        planeCount = 0;

        cullParams = default;
        cullParams.Tiles = (uint)(tileCols * tileRows);
        cullParams.TilesX = (uint)tileCols;
        cullParams.TilesY = (uint)tileRows;

        cullParams.TileToCenterScale = new Vector2(tileSize);
        cullParams.TileToCenterOffset = new Vector2(tileSize * 0.5f);

        cullParams.TileEpsilon = viewportSize.Length() / 16f;
        tileHalfSize = tileSize * 0.5f;
        cullSpaceMax = (new Vector2(tileCols, tileRows) * tileSize) - Vector2.One;

        cullParams.DepthBins = (uint)depthBins;
        cullParams.BinEpsilon = DepthBinEpsilon;

        cullParams.NearPlane = 0f;
    }

    /// <summary>Claims batch slots without projecting anything, for a frame that will not bin at all.</summary>
    /// <remarks>
    /// The masks are filled with ones instead, but the shading pass still indexes them through the bases
    /// and strides <see cref="End"/> derives from these counts, so the layout still has to be right.
    /// </remarks>
    /// <param name="barnLights">Barn light faces the shading pass will iterate.</param>
    /// <param name="envMaps">Env map probes the shading pass will iterate.</param>
    public void AddCounts(int barnLights, int envMaps)
    {
        batchItemCount[BatchBarnLights] = Math.Min(barnLights, MaxItemsPerBatch);
        batchItemCount[BatchEnvMaps] = Math.Min(envMaps, MaxItemsPerBatch);

        // Nothing was projected, so nothing was rejected either: every slot reaches every tile.
        batchBinnedCount[BatchBarnLights] = batchItemCount[BatchBarnLights];
        batchBinnedCount[BatchEnvMaps] = batchItemCount[BatchEnvMaps];
    }

    /// <summary>
    /// Adds one item per barn light face. Item <c>i</c> is always light <c>i</c>: the shading pass indexes
    /// the light array by bit position, so a face that fails to project is rejected in place, not dropped.
    /// </summary>
    /// <param name="volumes">Cull volume per barn light face, in shading pass index order.</param>
    public void AddBarnLights(ReadOnlySpan<BarnLightCullVolume> volumes)
    {
        var count = Math.Min(volumes.Length, MaxItemsPerBatch);
        var first = BatchBarnLights * MaxItemsPerBatch;
        var binned = 0;

        Span<Vector3> corners = stackalloc Vector3[8];

        for (var i = 0; i < count; i++)
        {
            ref readonly var volume = ref volumes[i];

            for (var corner = 0; corner < 8; corner++)
            {
                var clip = new Vector4(
                    (corner & 1) != 0 ? 1f : -1f,
                    (corner & 2) != 0 ? 1f : -1f,
                    (corner & 4) != 0 ? 1f : 0f,
                    1f);

                var world = Vector4.Transform(clip, volume.FrustumToWorld);
                corners[corner] = new Vector3(world.X, world.Y, world.Z) / world.W;
            }

            var item = BuildItem(corners);

            if (item.NumPlanes0 != 0u && volume.ObbToWorld.M44 != 0f)
            {
                for (var corner = 0; corner < 8; corner++)
                {
                    var cube = new Vector4(
                        (corner & 1) != 0 ? 1f : -1f,
                        (corner & 2) != 0 ? 1f : -1f,
                        (corner & 4) != 0 ? 1f : -1f,
                        1f);

                    var world = Vector4.Transform(cube, volume.ObbToWorld);
                    corners[corner] = new Vector3(world.X, world.Y, world.Z);
                }

                ApplySecondHull(ref item, corners);
            }

            if (item.NumPlanes0 != 0u)
            {
                var sphere = volume.RangeSphere;
                ApplyRangeConic(ref item, new Vector3(sphere.X, sphere.Y, sphere.Z), sphere.W);
            }

            items[first + i] = item;

            if (!IsRejected(item))
            {
                binned++;
            }
        }

        batchItemCount[BatchBarnLights] = count;
        batchBinnedCount[BatchBarnLights] = binned;
    }

    /// <summary>Adds one item per environment map probe, slotted by <see cref="SceneEnvMap.ShaderIndex"/>.</summary>
    public void AddEnvMaps(List<SceneEnvMap> envMaps)
    {
        var first = BatchEnvMaps * MaxItemsPerBatch;
        var count = 0;
        var binned = 0;

        for (var i = 0; i < MaxItemsPerBatch; i++)
        {
            items[first + i] = RejectAll;
        }

        Span<Vector3> corners = stackalloc Vector3[8];

        foreach (var envMap in envMaps)
        {
            var index = envMap.ShaderIndex;

            if (index < 0 || index >= MaxItemsPerBatch)
            {
                continue;
            }

            var bounds = envMap.LocalBoundingBox;
            var boundsMin = bounds.Min - new Vector3(EnvMapCullExtend);
            var boundsMax = bounds.Max + new Vector3(EnvMapCullExtend);

            for (var corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) != 0 ? boundsMax.X : boundsMin.X,
                    (corner & 2) != 0 ? boundsMax.Y : boundsMin.Y,
                    (corner & 4) != 0 ? boundsMax.Z : boundsMin.Z);

                corners[corner] = Vector3.Transform(local, envMap.Transform);
            }

            var item = BuildItem(corners);

            items[first + index] = item;
            count = Math.Max(count, index + 1);

            if (!IsRejected(item))
            {
                binned++;
            }
        }

        batchItemCount[BatchEnvMaps] = count;
        batchBinnedCount[BatchEnvMaps] = binned;
    }

    /// <summary>
    /// Lays the batches out back to back in the item and bits buffers, padding each tail so the tile
    /// pass's unconditional load stays in bounds. Call once every batch is added.
    /// </summary>
    public void End()
    {
        // Fitted here rather than in Begin because it is a property of the items, and they are only
        // projected in between.
        SliceFar = Math.Clamp(MaxItemViewDepth, minSliceFar, maxSliceFar);

        cullParams.DepthBinWidth = SliceFar / depthBins;

        var itemCursor = 0;
        var wordCursor = 0u;
        maskCount = 0;

        for (var batch = 0; batch < BatchCount; batch++)
        {
            var count = batchItemCount[batch];
            var masks = (count + ItemsPerMask - 1) / ItemsPerMask;
            var source = batch * MaxItemsPerBatch;

            if (source != itemCursor)
            {
                Array.Copy(items, source, items, itemCursor, count);
            }

            for (var i = count; i < masks * ItemsPerMask; i++)
            {
                items[itemCursor + i] = RejectAll;
            }

            batchFirstItem[batch] = itemCursor;

            var batchEntry = new CullBatch
            {
                OutputStride = (uint)masks,
                FirstItem = (uint)itemCursor,
                ItemEnd = (uint)(itemCursor + count),
            };

            cullParams.TileBatches[batch] = batchEntry;
            cullParams.BinBatches[batch] = batchEntry;

            itemCursor += masks * ItemsPerMask;
            maskCount += masks;
        }

        SetFirstMaskForBatch();

        for (var batch = 0; batch < BatchCount; batch++)
        {
            cullParams.TileBatches[batch].OutputOffset = wordCursor;
            wordCursor += (uint)(tileCols * tileRows) * cullParams.TileBatches[batch].OutputStride;
        }

        for (var batch = 0; batch < BatchCount; batch++)
        {
            cullParams.BinBatches[batch].OutputOffset = wordCursor;
            wordCursor += (uint)depthBins * cullParams.BinBatches[batch].OutputStride;
        }

        TotalWords = (int)wordCursor;
        ItemCount = itemCursor;
    }

    /// <summary>Gets the tile pass dispatch size.</summary>
    public (int X, int Y, int Z) TileDispatch => (
        (tileCols + TileGroupSizeX - 1) / TileGroupSizeX,
        (tileRows + TileGroupSizeY - 1) / TileGroupSizeY,
        maskCount);

    /// <summary>Gets the depth bin pass dispatch size.</summary>
    public (int X, int Y, int Z) BinDispatch => (depthBins / BinGroupSizeX, maskCount, 1);

    private void SetFirstMaskForBatch()
    {
        var running = 0u;

        cullParams.FirstMaskForBatch0 = running;
        running += cullParams.TileBatches[0].OutputStride;
        cullParams.FirstMaskForBatch1 = running;
        running += cullParams.TileBatches[1].OutputStride;
        cullParams.FirstMaskForBatch2 = running;
        running += cullParams.TileBatches[2].OutputStride;

        cullParams.MaskCount = running;
    }

    /// <summary>An item nothing matches, for rejected entries that keep their slot and for tail padding.</summary>
    private static CullItem RejectAll => new()
    {
        BoundsMin = new Vector2(float.MaxValue),
        BoundsMax = new Vector2(float.MinValue),
        DepthMin = float.MaxValue,
        DepthMax = float.MinValue,
        NdcDepthNear = Vector2.One,
    };

    /// <summary>An item everything matches, for a volume with no silhouette to project.</summary>
    /// <remarks>
    /// The fallback probe a viewer without lighting data gets spans the whole float range, so projection
    /// cannot describe it - but it reaches every pixel, so rejecting it is the one certainly wrong answer.
    /// Leaving it hull free and conic free keeps it through the fine pass too.
    /// </remarks>
    private CullItem AcceptAll => new()
    {
        BoundsMin = Vector2.Zero,
        BoundsMax = cullSpaceMax,
        DepthMin = 0f,
        DepthMax = float.MaxValue,

        // Reads as straddling the near plane, which the occlusion test can never call hidden.
        NdcDepthNear = Vector2.One,
    };

    /// <summary>
    /// Whether projecting a volume stays in range. Past this the clip space maths overflows, the divide
    /// yields NaN, and every downstream comparison answers false - which reads as "reaches no tile"
    /// rather than as the failure it is. Real map coordinates sit orders of magnitude below the bound.
    /// </summary>
    private static bool IsProjectable(ReadOnlySpan<Vector3> corners)
    {
        const float MaxCoordinate = 1e18f;

        foreach (var corner in corners)
        {
            if (!float.IsFinite(corner.X) || !float.IsFinite(corner.Y) || !float.IsFinite(corner.Z)
                || Math.Abs(corner.X) > MaxCoordinate
                || Math.Abs(corner.Y) > MaxCoordinate
                || Math.Abs(corner.Z) > MaxCoordinate)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>What projecting one volume into cull space produced, before it becomes an item.</summary>
    private struct Projection
    {
        /// <summary>Vertices of the dilated silhouette left in <c>hull</c>. Zero means nothing to keep.</summary>
        public int HullCount;

        /// <summary>View depth range over the volume, exact because depth is affine over it.</summary>
        public float DepthMin;
        public float DepthMax;

        public float NdcDepthNear;
        public bool AllCornersInFront;

        /// <summary>Screen bounds of the silhouette itself, before the tile square dilates it.</summary>
        public Vector2 RawMin;
        public Vector2 RawMax;

        /// <summary>
        /// The nearest NDC depth the occlusion test may use. A volume straddling the near plane has no
        /// trustworthy screen depth, so it reports the nearest value there is and is never called hidden.
        /// </summary>
        public readonly float OcclusionDepth
            => AllCornersInFront ? Math.Clamp(NdcDepthNear, -1f, 1f) : 1f;
    }

    /// <summary>
    /// Projects a volume's 8 corners into cull space, reducing them to a dilated silhouette hull and a
    /// depth range. Corner <c>i</c> must carry the x, y and z axis in bits 0, 1 and 2: the near plane edge
    /// walk pairs <c>i</c> with <c>i | bit</c>. The result lands in the shared <c>hull</c> scratch, so a
    /// second call invalidates the first.
    /// </summary>
    private Projection ProjectVolume(ReadOnlySpan<Vector3> corners)
    {
        Span<Vector4> clip = stackalloc Vector4[8];

        var depthMin = float.MaxValue;
        var depthMax = float.MinValue;
        var ndcDepthNear = float.MinValue;
        var allCornersInFront = true;
        var pointCount = 0;

        for (var i = 0; i < 8; i++)
        {
            var viewDepth = Vector3.Dot(corners[i] - cameraPosition, cameraDirection);
            depthMin = MathF.Min(depthMin, viewDepth);
            depthMax = MathF.Max(depthMax, viewDepth);

            clip[i] = Vector4.Transform(new Vector4(corners[i], 1f), worldToProjection);

            if (clip[i].W >= cameraNearPlane)
            {
                projected[pointCount++] = NdcToPixel(new Vector2(clip[i].X, clip[i].Y) / clip[i].W);

                ndcDepthNear = MathF.Max(ndcDepthNear, clip[i].Z / clip[i].W);
            }
            else
            {
                allCornersInFront = false;
            }
        }

        var projection = new Projection
        {
            DepthMin = depthMin,
            DepthMax = depthMax,
            NdcDepthNear = ndcDepthNear,
            AllCornersInFront = allCornersInFront,
            RawMin = new Vector2(float.MaxValue),
            RawMax = new Vector2(float.MinValue),
        };

        if (pointCount == 0)
        {
            return projection;
        }

        for (var i = 0; i < 8; i++)
        {
            for (var bit = 1; bit <= 4; bit <<= 1)
            {
                if ((i & bit) != 0)
                {
                    continue;
                }

                var wNear = clip[i].W;
                var wFar = clip[i | bit].W;

                if ((wNear >= cameraNearPlane) == (wFar >= cameraNearPlane))
                {
                    continue;
                }

                var t = (cameraNearPlane - wNear) / (wFar - wNear);

                var crossing = Vector2.Lerp(
                    new Vector2(clip[i].X, clip[i].Y),
                    new Vector2(clip[i | bit].X, clip[i | bit].Y),
                    t) / cameraNearPlane;

                projected[pointCount++] = NdcToPixel(crossing);
            }
        }

        for (var i = 0; i < pointCount; i++)
        {
            projection.RawMin = Vector2.Min(projection.RawMin, projected[i]);
            projection.RawMax = Vector2.Max(projection.RawMax, projected[i]);
        }

        projection.HullCount = BuildDilatedHull(pointCount);

        return projection;
    }

    /// <summary>
    /// Screen bounds of the hull a projection left behind, clamped to the viewport. This is the frustum
    /// rejection the compute passes no longer do: false for a degenerate or off screen volume, which
    /// clamping instead would smear across a whole edge row of tiles.
    /// </summary>
    private bool HullBounds(in Projection projection, out Vector2 boundsMin, out Vector2 boundsMax)
    {
        boundsMin = new Vector2(float.MaxValue);
        boundsMax = new Vector2(float.MinValue);

        for (var i = 0; i < projection.HullCount; i++)
        {
            boundsMin = Vector2.Min(boundsMin, hull[i]);
            boundsMax = Vector2.Max(boundsMax, hull[i]);
        }

        if (projection.HullCount == 0
            || projection.RawMin.X > viewportSize.X || projection.RawMin.Y > viewportSize.Y
            || projection.RawMax.X < 0f || projection.RawMax.Y < 0f)
        {
            return false;
        }

        boundsMin = Vector2.Clamp(boundsMin, Vector2.Zero, cullSpaceMax);
        boundsMax = Vector2.Clamp(boundsMax, Vector2.Zero, cullSpaceMax);

        return true;
    }

    /// <summary>
    /// Reduces a volume's 8 corners to an item: screen AABB, convex silhouette hull and depth range.
    /// Corner order is what <see cref="ProjectVolume"/> requires.
    /// </summary>
    private CullItem BuildItem(ReadOnlySpan<Vector3> corners)
    {
        if (!IsProjectable(corners))
        {
            return AcceptAll;
        }

        var projection = ProjectVolume(corners);

        if (!HullBounds(projection, out var boundsMin, out var boundsMax))
        {
            return RejectAll;
        }

        if (float.IsFinite(projection.DepthMax))
        {
            MaxItemViewDepth = MathF.Max(MaxItemViewDepth, projection.DepthMax);
        }

        var item = new CullItem
        {
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            DepthMin = projection.DepthMin,
            DepthMax = projection.DepthMax,
            NdcDepthNear = new Vector2(projection.OcclusionDepth, 0f),
            FirstPlane = (uint)planeCount,
        };

        if (projection.HullCount <= MaxHullVertices && planeCount + projection.HullCount <= planes.Length)
        {
            for (var i = 0; i < projection.HullCount; i++)
            {
                planes[planeCount + i] = hull[i];
            }

            item.NumPlanes0 = (uint)projection.HullCount;
            planeCount += projection.HullCount;
        }
        else if (planeCount + 4 <= planes.Length)
        {
            AddRectHull(item.BoundsMin, item.BoundsMax);
            item.NumPlanes0 = 4;
        }

        return item;
    }

    /// <summary>
    /// Intersects an item with a second convex volume, as the hull 1 the shader ANDs with hull 0. Must
    /// follow the <see cref="BuildItem"/> that produced the item: hull 1 is read from
    /// <c>FirstPlane + NumPlanes0</c>, so anything claiming vertices in between takes its slot.
    /// </summary>
    private void ApplySecondHull(ref CullItem item, ReadOnlySpan<Vector3> corners)
    {
        var projection = ProjectVolume(corners);

        if (!HullBounds(projection, out var boundsMin, out var boundsMax))
        {
            item = RejectAll;
            return;
        }

        boundsMin = Vector2.Max(boundsMin, item.BoundsMin);
        boundsMax = Vector2.Min(boundsMax, item.BoundsMax);

        var depthMin = MathF.Max(projection.DepthMin, item.DepthMin);
        var depthMax = MathF.Min(projection.DepthMax, item.DepthMax);

        if (boundsMin.X > boundsMax.X || boundsMin.Y > boundsMax.Y || depthMin > depthMax)
        {
            item = RejectAll;
            return;
        }

        item.BoundsMin = boundsMin;
        item.BoundsMax = boundsMax;
        item.DepthMin = depthMin;
        item.DepthMax = depthMax;

        item.NdcDepthNear = new Vector2(MathF.Min(item.NdcDepthNear.X, projection.OcclusionDepth), 0f);

        if (projection.HullCount <= MaxHullVertices && planeCount + projection.HullCount <= planes.Length)
        {
            for (var i = 0; i < projection.HullCount; i++)
            {
                planes[planeCount + i] = hull[i];
            }

            item.NumPlanes1 = (uint)projection.HullCount;
            planeCount += projection.HullCount;
        }
    }

    private Vector2 NdcToPixel(Vector2 ndc) => ((ndc * 0.5f) + new Vector2(0.5f)) * viewportSize;

    /// <summary>
    /// Convex hull of the projected points summed with the tile square, counter clockwise, by Andrew's
    /// monotone chain. Returns the vertex count left in <see cref="hull"/>, or 0 if degenerate.
    /// </summary>
    private int BuildDilatedHull(int pointCount)
    {
        var count = 0;

        for (var i = 0; i < pointCount; i++)
        {
            minkowski[count++] = projected[i] + new Vector2(-tileHalfSize, -tileHalfSize);
            minkowski[count++] = projected[i] + new Vector2(tileHalfSize, -tileHalfSize);
            minkowski[count++] = projected[i] + new Vector2(tileHalfSize, tileHalfSize);
            minkowski[count++] = projected[i] + new Vector2(-tileHalfSize, tileHalfSize);
        }

        for (var i = 1; i < count; i++)
        {
            var value = minkowski[i];
            var j = i - 1;

            while (j >= 0 && (minkowski[j].X > value.X || (minkowski[j].X == value.X && minkowski[j].Y > value.Y)))
            {
                minkowski[j + 1] = minkowski[j];
                j--;
            }

            minkowski[j + 1] = value;
        }

        var k = 0;

        for (var i = 0; i < count; i++)
        {
            while (k >= 2 && Cross(hull[k - 2], hull[k - 1], minkowski[i]) <= 0f)
            {
                k--;
            }

            hull[k++] = minkowski[i];
        }

        var lower = k + 1;

        for (var i = count - 2; i >= 0; i--)
        {
            while (k >= lower && Cross(hull[k - 2], hull[k - 1], minkowski[i]) <= 0f)
            {
                k--;
            }

            hull[k++] = minkowski[i];
        }

        var hullCount = k - 1;

        return hullCount >= 3 ? hullCount : 0;

        static float Cross(Vector2 o, Vector2 a, Vector2 b)
            => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
    }

    /// <summary>
    /// Emits an already dilated rect as a four vertex hull, for the overflow fallback. Counter clockwise,
    /// because the shader takes each edge's left normal as the inward one.
    /// </summary>
    private void AddRectHull(Vector2 boundsMin, Vector2 boundsMax)
    {
        planes[planeCount + 0] = new Vector2(boundsMin.X, boundsMin.Y);
        planes[planeCount + 1] = new Vector2(boundsMax.X, boundsMin.Y);
        planes[planeCount + 2] = new Vector2(boundsMax.X, boundsMax.Y);
        planes[planeCount + 3] = new Vector2(boundsMin.X, boundsMax.Y);

        planeCount += 4;
    }

    /// <summary>
    /// Sets the item's conic to the light's projected range sphere, or leaves it disabled when the sphere
    /// has no bounded screen region - the one case the shader's test would get wrong.
    /// </summary>
    /// <remarks>
    /// Derived as a ray test, not by projecting the silhouette, which would leave the conic's sign
    /// ambiguous. A pixel sees the sphere when its ray meets it: for eye relative centre <c>c</c> and
    /// radius <c>r</c> that is <c>(d.c)^2 - |d|^2 (|c|^2 - r^2) &gt;= 0</c>, and <c>d</c> is linear in the
    /// pixel, so substituting gives the quadratic form directly, already positive inside.
    /// </remarks>
    private void ApplyRangeConic(ref CullItem item, Vector3 centre, float radius)
    {
        if (!projectionToWorldValid || radius <= 0f)
        {
            return;
        }

        var c = centre - cameraPosition;

        if (Vector3.Dot(c, cameraDirection) <= radius + cameraNearPlane)
        {
            return;
        }

        var projectedRadius = ProjectedRadiusPixels(centre, radius);

        if (projectedRadius <= 0f)
        {
            return;
        }

        var dilated = radius + (tileHalfSize * MathF.Sqrt(2f) * (radius / projectedRadius));

        var rowX = new Vector4(projectionToWorld.M11, projectionToWorld.M12, projectionToWorld.M13, projectionToWorld.M14);
        var rowY = new Vector4(projectionToWorld.M21, projectionToWorld.M22, projectionToWorld.M23, projectionToWorld.M24);
        var rowZ = new Vector4(projectionToWorld.M31, projectionToWorld.M32, projectionToWorld.M33, projectionToWorld.M34);
        var rowW = new Vector4(projectionToWorld.M41, projectionToWorld.M42, projectionToWorld.M43, projectionToWorld.M44);

        static Vector3 Column(Vector4 axis, Vector4 z)
            => (new Vector3(z.X, z.Y, z.Z) * axis.W) - (new Vector3(axis.X, axis.Y, axis.Z) * z.W);

        var r0 = Column(rowX, rowZ);
        var r1 = Column(rowY, rowZ);
        var r2 = Column(rowW, rowZ);

        var k = c.LengthSquared() - (dilated * dilated);

        static float Form(Vector3 a, Vector3 b, Vector3 c, float k)
            => (Vector3.Dot(a, c) * Vector3.Dot(b, c)) - (Vector3.Dot(a, b) * k);

        var m00 = Form(r0, r0, c, k);
        var m01 = Form(r0, r1, c, k);
        var m02 = Form(r0, r2, c, k);
        var m11 = Form(r1, r1, c, k);
        var m12 = Form(r1, r2, c, k);
        var m22 = Form(r2, r2, c, k);

        var sx = 2f / viewportSize.X;
        var sy = 2f / viewportSize.Y;

        var a = m00 * sx * sx;
        var b = m11 * sy * sy;
        var cc = 2f * m01 * sx * sy;
        var d = 2f * sx * (m02 - m00 - m01);
        var e = 2f * sy * (m12 - m11 - m01);
        var f = m00 + m11 + m22 + (2f * m01) - (2f * m02) - (2f * m12);

        var scale = MathF.Max(MathF.Abs(a), MathF.Max(MathF.Abs(b), MathF.Max(MathF.Abs(cc),
            MathF.Max(MathF.Abs(d), MathF.Max(MathF.Abs(e), MathF.Abs(f))))));

        if (scale <= 0f || !float.IsFinite(scale))
        {
            return;
        }

        var inv = 1f / scale;

        item.ConicXX = a * inv;
        item.ConicYY = b * inv;
        item.ConicXY = cc * inv;
        item.ConicX = d * inv;
        item.ConicY = e * inv;
        item.ConicConst = f * inv;
        item.ConicEnable = 1f;
    }

    /// <summary>Projected radius of the sphere in pixels, measured by projecting an offset point.</summary>
    private float ProjectedRadiusPixels(Vector3 centre, float radius)
    {
        var toCentre = centre - cameraPosition;
        var axis = Vector3.Cross(toCentre, cameraDirection);

        if (axis.LengthSquared() < 1e-12f)
        {
            axis = Vector3.Cross(toCentre, Vector3.UnitZ);

            if (axis.LengthSquared() < 1e-12f)
            {
                axis = Vector3.Cross(toCentre, Vector3.UnitX);
            }
        }

        axis = Vector3.Normalize(axis) * radius;

        var atCentre = Vector4.Transform(new Vector4(centre, 1f), worldToProjection);
        var atEdge = Vector4.Transform(new Vector4(centre + axis, 1f), worldToProjection);

        if (atCentre.W <= 0f || atEdge.W <= 0f)
        {
            return 0f;
        }

        var pc = NdcToPixel(new Vector2(atCentre.X, atCentre.Y) / atCentre.W);
        var pe = NdcToPixel(new Vector2(atEdge.X, atEdge.Y) / atEdge.W);

        return (pe - pc).Length();
    }

}
