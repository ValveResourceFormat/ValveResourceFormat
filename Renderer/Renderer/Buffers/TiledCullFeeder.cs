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

    /// <summary>
    /// Gets the map from the cube [-1,1]^3 to the face's illumination OBB, or default when the face has
    /// none and the frustum is the only volume.
    /// </summary>
    public Matrix4x4 ObbToWorld { get; init; }

    /// <summary>
    /// Gets the sphere the light fades out at, centre in xyz and radius in w. Radius 0 means the face has
    /// no distance cutoff, which is the case for every barn light: only omni2 fades by distance.
    /// </summary>
    public Vector4 RangeSphere { get; init; }
}

/// <summary>
/// CPU side of <c>compute_tile_cullbits</c> and <c>compute_depthbin_cullbits</c>.
/// </summary>
/// <remarks>
/// <para>
/// Neither compute pass sees world space. A <see cref="CullItem"/> is already a screen space AABB plus a
/// depth range, so projecting the volume and rejecting it against the frustum is this class's job, and an
/// item that fails is never a wasted bit rather than a wasted test: item count sets the word stride, which
/// sets both the buffer size and the length of the fragment shader's loop.
/// </para>
/// <para>
/// The shaders are agnostic about what "cull space" and "depth" mean; they only ever add, scale and
/// compare. Tile space here is pixels, and the depth key is <c>log2(max(viewDepth, 1))</c>, which is what
/// makes the depth bins logarithmic despite the shader stepping through them linearly. Both choices have
/// to agree with what <c>common/light_cull.slang</c> does on the consumer side.
/// </para>
/// </remarks>
public sealed class TiledCullFeeder
{
    /// <summary>Item classes culled together in one dispatch. Batch 2 is reserved for light probe volumes.</summary>
    public const int BatchCount = 3;

    /// <summary>Batch index holding barn light faces.</summary>
    public const int BatchBarnLights = 0;

    /// <summary>Batch index holding environment map probes.</summary>
    public const int BatchEnvMaps = 1;

    /// <summary>
    /// Items per mask, and the alignment every batch's first item needs. The depth bin shader shifts by the
    /// absolute item index while the tile shader shifts by the mask relative one, so they agree only when a
    /// batch starts on a multiple of this.
    /// </summary>
    public const int ItemsPerMask = 32;

    /// <summary>
    /// Vertex budget per hull. A box silhouette is at most 6 vertices and the tile square adds at most 4
    /// edge directions, so this clears a projected box comfortably; anything past it falls back to a rect.
    /// </summary>
    private const int MaxHullVertices = 16;

    /// <summary>Hulls one item can own. The shader reads two, and a barn light face uses both.</summary>
    private const int MaxHullsPerItem = 2;

    private const int MaxItemsPerBatch = 128;
    private const int MaxItems = BatchCount * MaxItemsPerBatch;

    private const int TileGroupSizeX = 8;
    private const int TileGroupSizeY = 4;
    private const int BinGroupSizeX = 32;

    private readonly CullItem[] items = new CullItem[MaxItems];

    /// <summary>Silhouette hull vertices, packed back to back and indexed by CullItem.FirstPlane.</summary>
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
    private float depthKeyRange;
    private float tileHalfSize;

    /// <summary>Last coordinate the tile grid reaches, which runs past the viewport on a partial edge.</summary>
    private Vector2 cullSpaceMax;
    private int planeCount;

    /// <summary>Gets the number of uints the cull bits buffer needs for the layout built this frame.</summary>
    public int TotalWords { get; private set; }

    /// <summary>Gets the total mask count across all batches, which is the dispatch's mask axis.</summary>
    public int MaskCount => maskCount;

    /// <summary>Gets the viewport the items were projected against, which is also the extent of cull space.</summary>
    public Vector2 ViewportSize => viewportSize;

    /// <summary>Gets the constants both compute passes read.</summary>
    public ref readonly CullParams Params => ref cullParams;

    /// <summary>Gets the backing array to upload from. Exposed raw because the buffer upload takes an array.</summary>
    public CullItem[] ItemArray => items;

    /// <summary>Gets how many entries of <see cref="ItemArray"/> are live this frame, tail padding included.</summary>
    public int ItemCount { get; private set; }

    /// <summary>Gets the hull vertex backing array referenced by <see cref="CullItem.FirstPlane"/>.</summary>
    public Vector2[] PlaneArray => planes;

    /// <summary>Gets how many entries of <see cref="PlaneArray"/> are live this frame.</summary>
    public int PlaneCount => planeCount;

    /// <summary>Gets the first uint of a batch's tile region, for the consumer's tile lookup.</summary>
    public uint TileBase(int batch) => cullParams.TileBatches[batch].OutputOffset;

    /// <summary>Gets the first uint of a batch's depth bin region, for the consumer's bin lookup.</summary>
    public uint BinBase(int batch) => cullParams.BinBatches[batch].OutputOffset;

    /// <summary>Gets a batch's word stride, which is <c>ceil(itemCount / 32)</c>.</summary>
    public uint Stride(int batch) => cullParams.TileBatches[batch].OutputStride;

    /// <summary>Gets how many slots a batch claimed, which is every item the shading pass can iterate.</summary>
    /// <param name="batch">Batch index.</param>
    public int SlotCount(int batch) => batchItemCount[batch];

    /// <summary>
    /// Gets how many of a batch's slots hold an item that can actually match a tile. The rest projected to
    /// nothing - wholly behind the near plane, or off screen - and were reduced to an item no tile and no
    /// bin can match, so the gap between this and <see cref="SlotCount"/> is what the CPU rejected outright.
    /// </summary>
    /// <param name="batch">Batch index.</param>
    public int BinnedCount(int batch) => batchBinnedCount[batch];

    /// <summary>Whether an item projected to nothing and will never match a tile or a bin.</summary>
    private static bool IsRejected(in CullItem item) => item.DepthMin > item.DepthMax;

    /// <summary>
    /// Starts a frame. Everything an item is projected against is captured here, so the batches added
    /// afterwards all land in the same space.
    /// </summary>
    public void Begin(
        int tileCols, int tileRows, int tileSize,
        int depthBins, float depthKeyRange,
        Vector2 viewportSize,
        in Matrix4x4 worldToProjection,
        Vector3 cameraPosition, Vector3 cameraDirection, float cameraNearPlane)
    {
        this.tileCols = tileCols;
        this.tileRows = tileRows;
        this.depthBins = depthBins;
        this.viewportSize = viewportSize;
        this.worldToProjection = worldToProjection;
        projectionToWorldValid = Matrix4x4.Invert(worldToProjection, out projectionToWorld);
        this.cameraPosition = cameraPosition;
        this.cameraDirection = cameraDirection;
        this.cameraNearPlane = cameraNearPlane;
        this.depthKeyRange = depthKeyRange;

        Array.Clear(batchItemCount);
        Array.Clear(batchBinnedCount);
        planeCount = 0;

        cullParams = default;
        cullParams.Tiles = (uint)(tileCols * tileRows);
        cullParams.TilesX = (uint)tileCols;
        cullParams.TilesY = (uint)tileRows;

        cullParams.TileToCenterScale = new Vector2(tileSize);
        cullParams.TileToCenterOffset = new Vector2(tileSize * 0.5f);

        cullParams.TileEpsilon = 0f;
        tileHalfSize = tileSize * 0.5f;
        cullSpaceMax = (new Vector2(tileCols, tileRows) * tileSize) - Vector2.One;

        cullParams.DepthBins = (uint)depthBins;
        cullParams.DepthBinWidth = depthKeyRange / depthBins;
        cullParams.BinEpsilon = 0f;

        cullParams.NearPlane = 0f;
    }

    /// <summary>
    /// Adds one barn light face per volume, keeping index alignment: item <c>i</c> of the batch is always
    /// light <c>i</c>, and a light that fails the projection gets an item nothing can match rather than
    /// being dropped. The fragment shader indexes the light array by bit position, so compacting here
    /// would need a remap table it does not have.
    /// </summary>
    /// <summary>
    /// Claims batch slots without projecting anything, for a frame that will not bin at all.
    /// </summary>
    /// <remarks>
    /// The masks are filled with ones instead, so the shading pass still indexes them through the bases and
    /// strides <see cref="End"/> derives from these counts. Everything else <see cref="AddBarnLights"/> and
    /// <see cref="AddEnvMaps"/> do - projecting corners, clipping against the near plane, hulling the
    /// Minkowski sum - only feeds a compute pass that is not going to run.
    /// </remarks>
    /// <param name="barnLights">Number of barn light faces the shading pass will iterate.</param>
    /// <param name="envMaps">Number of env map probes the shading pass will iterate.</param>
    public void AddCounts(int barnLights, int envMaps)
    {
        batchItemCount[BatchBarnLights] = Math.Min(barnLights, MaxItemsPerBatch);
        batchItemCount[BatchEnvMaps] = Math.Min(envMaps, MaxItemsPerBatch);

        // Nothing was projected, so nothing was rejected either: every slot reaches every tile.
        batchBinnedCount[BatchBarnLights] = batchItemCount[BatchBarnLights];
        batchBinnedCount[BatchEnvMaps] = batchItemCount[BatchEnvMaps];
    }

    /// <summary>
    /// Adds one item per barn light face, keeping index alignment: item <c>i</c> of the batch is always
    /// light <c>i</c>, and a light that fails the projection gets an item nothing can match rather than
    /// being dropped. The shading pass indexes the light array by bit position, so compacting here would
    /// need a remap table it does not have.
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

    /// <summary>
    /// Adds one item per environment map probe. Probes are AABB only, so they exercise the whole pipeline
    /// without any hull or conic geometry.
    /// </summary>
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
            var boundsMin = bounds.Min - new Vector3(SceneEnvMap.BoundsExtend);
            var boundsMax = bounds.Max + new Vector3(SceneEnvMap.BoundsExtend);

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
    /// Lays the batches out back to back in both the item buffer and the bits buffer, and pads every
    /// batch's tail so the tile pass's unconditional load stays in bounds. Call once all batches are added.
    /// </summary>
    public void End()
    {
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

    /// <summary>
    /// An item no tile and no bin can match. Used for culled entries, which have to keep their slot, and
    /// for the padding the tile pass reads past the end of a batch.
    /// </summary>
    private static CullItem RejectAll => new()
    {
        BoundsMin = new Vector2(float.MaxValue),
        BoundsMax = new Vector2(float.MinValue),
        DepthMin = float.MaxValue,
        DepthMax = float.MinValue,
        NdcDepthNear = Vector2.One,
    };

    /// <summary>What projecting one volume into cull space produced, before it becomes part of an item.</summary>
    private struct Projection
    {
        /// <summary>Vertices of the dilated silhouette left in <c>hull</c>. Zero means nothing to keep.</summary>
        public int HullCount;

        /// <summary>View depth range over the volume, which is exact because depth is affine over it.</summary>
        public float DepthMin;
        public float DepthMax;

        public float NdcDepthNear;
        public bool AllCornersInFront;

        /// <summary>Screen bounds of the silhouette itself, before the tile square dilates it.</summary>
        public Vector2 RawMin;
        public Vector2 RawMax;

        /// <summary>
        /// The nearest NDC depth the occlusion test may use. A volume straddling the near plane has no
        /// trustworthy screen depth, so it hands the test the nearest value there is and can never be
        /// decided hidden.
        /// </summary>
        public readonly float OcclusionDepth
            => AllCornersInFront ? Math.Clamp(NdcDepthNear, -1f, 1f) : 1f;
    }

    /// <summary>
    /// Projects a volume's 8 corners into cull space and reduces them to a dilated silhouette hull and a
    /// depth range. Corner <c>i</c> must have the x, y and z axis in bits 0, 1 and 2, because the near
    /// plane edge walk pairs <c>i</c> with <c>i | bit</c>.
    /// </summary>
    /// <remarks>
    /// Leaves its result in the shared <c>hull</c> scratch buffer, so a second call invalidates the first:
    /// callers have to consume one projection before starting the next.
    /// </remarks>
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
    /// Screen bounds of the hull a projection left behind, clamped to the viewport. False when the volume
    /// is degenerate or entirely off screen: clamping that instead would light up a whole edge row of
    /// tiles, and this is the frustum rejection the compute passes no longer do.
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
    /// Projects a volume's 8 corners into cull space and reduces them to an item: a screen AABB, a convex
    /// silhouette hull, and a depth key range. Corner order is what <see cref="ProjectVolume"/> requires.
    /// </summary>
    private CullItem BuildItem(ReadOnlySpan<Vector3> corners)
    {
        var projection = ProjectVolume(corners);

        if (!HullBounds(projection, out var boundsMin, out var boundsMax))
        {
            return RejectAll;
        }

        var item = new CullItem
        {
            BoundsMin = boundsMin,
            BoundsMax = boundsMax,
            DepthMin = DepthKey(projection.DepthMin),
            DepthMax = DepthKey(projection.DepthMax),
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
    /// Intersects an item with a second convex volume, as the hull 1 the shader ANDs with hull 0. Must be
    /// called straight after the <see cref="BuildItem"/> that produced the item, because hull 1 is read
    /// from <c>FirstPlane + NumPlanes0</c>: anything else claiming vertices in between would take its slot.
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

        var depthMin = MathF.Max(DepthKey(projection.DepthMin), item.DepthMin);
        var depthMax = MathF.Min(DepthKey(projection.DepthMax), item.DepthMax);

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
    /// monotone chain. Returns the vertex count in <see cref="hull"/>, or 0 when the points are degenerate.
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
    /// Emits a rect as a four vertex hull. Only the degenerate fallback uses this now; the bounds arrive
    /// already dilated, so it must not grow them again.
    /// </summary>
    /// <remarks>
    /// Counter clockwise, because the shader takes each edge's left normal as the inward one. Cull space
    /// has y growing upward, same as gl_FragCoord.
    /// </remarks>
    private void AddRectHull(Vector2 boundsMin, Vector2 boundsMax)
    {
        planes[planeCount + 0] = new Vector2(boundsMin.X, boundsMin.Y);
        planes[planeCount + 1] = new Vector2(boundsMax.X, boundsMin.Y);
        planes[planeCount + 2] = new Vector2(boundsMax.X, boundsMax.Y);
        planes[planeCount + 3] = new Vector2(boundsMin.X, boundsMax.Y);

        planeCount += 4;
    }

    /// <summary>
    /// Sets the item's conic to the light's range sphere, projected. Leaves the conic disabled when the
    /// sphere cannot produce a bounded screen region, which is the only case the shader's test would get
    /// wrong.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Derived as a ray test rather than by projecting the sphere's outline. A pixel sees the sphere when
    /// the ray through it meets the sphere, which for eye relative centre <c>c</c> and radius <c>r</c> is
    /// <c>(d.c)^2 - |d|^2 (|c|^2 - r^2) &gt;= 0</c>. The ray direction <c>d</c> is a linear function of the
    /// pixel, so substituting turns that straight into the quadratic form the shader evaluates, already
    /// oriented so that inside is positive. Projecting the silhouette instead would leave the sign of the
    /// conic ambiguous and need a separate test to resolve.
    /// </para>
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

    /// <summary>Radius of the sphere once projected, in pixels, measured by projecting an offset point.</summary>
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

    /// <summary>
    /// The depth bin key. Must stay in step with <c>GetLightCullDepthSlice</c> in the consumer, which reads
    /// <c>log2(max(viewDepth, 1))</c> and clamps to the last slice, so the key is clamped to the same range
    /// rather than allowed to fall outside every bin.
    /// </summary>
    private float DepthKey(float viewDepth)
        => Math.Clamp(MathF.Log2(MathF.Max(viewDepth, 1f)), 0f, depthKeyRange);
}
