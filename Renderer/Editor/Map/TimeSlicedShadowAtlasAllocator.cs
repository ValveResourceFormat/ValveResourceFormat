using ValveResourceFormat.Renderer.SceneEnvironment;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// Keeps the shadow faces of lights in the atlas between frames and renders them again in turns: the closest light
/// every frame, the next one every second frame, the two after it every fourth frame, and so on. A face is rendered
/// at once when it gets a new region or its light moves, while objects moving through a light only show in its shadow
/// on its turn. Every light of a source map shadows everything in real time, which is too slow to render each frame.
/// </summary>
internal sealed class TimeSlicedShadowAtlasAllocator : ShadowAtlasAllocator
{
    private sealed class CachedFace
    {
        public ShadowAtlasRegion Region { get; init; }

        /// <summary>Gets the size the face asked for, which the region may have been shrunk from to fit.</summary>
        public (int Width, int Height) RequestedSize { get; init; }

        /// <summary>Gets or sets the transform the face was last rendered with.</summary>
        public Matrix4x4 WorldToFrustum { get; set; }

        public uint LastUsedFrame { get; set; }
    }

    private readonly Dictionary<(SceneLight Light, int Face), CachedFace> CachedFaces = [];
    private int AtlasSize;
    private uint Frame;
    private bool IsRepacked;
    private long RequestedTexels;
    private long UnplaceableTexels = long.MaxValue;

    public override void BeginFrame(int atlasSize)
    {
        Frame++;
        RequestedTexels = 0;

        if (atlasSize != AtlasSize)
        {
            Reset(atlasSize);
        }

        IsRepacked = CachedFaces.Count == 0;
    }

    public override bool TryPlaceFace(SceneLight light, int lightPlace, int face, int width, int height,
        out ShadowAtlasRegion region, out bool isUpToDate)
    {
        RequestedTexels += (long)width * height;
        isUpToDate = false;

        var key = (light, face);
        var worldToFrustum = light.BarnFaces[face].WorldToFrustum;

        if (CachedFaces.TryGetValue(key, out var cached))
        {
            if (cached.RequestedSize == (width, height))
            {
                region = cached.Region;
                isUpToDate = cached.WorldToFrustum == worldToFrustum && !IsLightsTurn(light, lightPlace);
                cached.WorldToFrustum = worldToFrustum;
                cached.LastUsedFrame = Frame;
                return true;
            }

            Atlas.Free(cached.Region);
            CachedFaces.Remove(key);
        }

        if (!TryAllocate(width, height, out region))
        {
            return false;
        }

        CachedFaces.Add(key, new CachedFace
        {
            Region = region,
            RequestedSize = (width, height),
            WorldToFrustum = worldToFrustum,
            LastUsedFrame = Frame,
        });

        return true;
    }

    // Faces kept from earlier frames can leave the atlas too cut up for a face that fits the budget, so every face is
    // placed and rendered anew rather than one going without. That is not tried again while asking for as much as an
    // empty atlas last had no room for.
    public override bool Repack(bool placedAll)
    {
        if (placedAll)
        {
            UnplaceableTexels = long.MaxValue;
            return false;
        }

        if (IsRepacked)
        {
            UnplaceableTexels = RequestedTexels;
            return false;
        }

        if (RequestedTexels >= UnplaceableTexels)
        {
            return false;
        }

        Reset(AtlasSize);
        IsRepacked = true;
        RequestedTexels = 0;

        return true;
    }

    // Evicts the face unused for the longest
    protected override bool MakeRoom()
    {
        (SceneLight Light, int Face)? oldestKey = null;
        var oldestFrame = Frame;

        foreach (var (key, cached) in CachedFaces)
        {
            if (cached.LastUsedFrame < oldestFrame)
            {
                oldestKey = key;
                oldestFrame = cached.LastUsedFrame;
            }
        }

        if (oldestKey is not { } evicted)
        {
            return false;
        }

        Atlas.Free(CachedFaces[evicted].Region);
        CachedFaces.Remove(evicted);

        return true;
    }

    private void Reset(int atlasSize)
    {
        Atlas.Begin(atlasSize);
        CachedFaces.Clear();
        AtlasSize = atlasSize;
    }

    // The light at a place in the binned order is rendered every power of two frames above that place, on the frames
    // its id picks, so the lights sharing a period take turns
    private bool IsLightsTurn(SceneLight light, int lightPlace)
    {
        var periodMask = BitOperations.RoundUpToPowerOf2((uint)lightPlace + 1) - 1;

        return (light.Id & periodMask) == (Frame & periodMask);
    }
}
