using System.Diagnostics;

namespace ValveResourceFormat.Renderer;

/// <summary>Describes the dimensions requested for a single shadow map.</summary>
public record struct ShadowRequest(int Width, int Height);

/// <summary>Describes the position and size of a shadow map region within the atlas texture.</summary>
public record struct ShadowAtlasRegion(int X, int Y, int Width, int Height)
{
    /// <summary>Gets whether this region has valid non-zero dimensions.</summary>
    public readonly bool IsValid => Width > 0 && Height > 0;
}

/// <summary>Packs shadow map requests into a single atlas texture using a shelf-based bin-packing algorithm.</summary>
public class ShadowAtlasPacker
{
    /// <summary>Gets the maximum number of shadow maps this packer can accommodate.</summary>
    public int MaxShadowMaps { get; }

    private readonly ShadowAtlasRegion[] regions;
    private readonly (int Height, int Width, int Index)[] sortBuffer;

    /// <summary>Initializes the packer with a fixed capacity.</summary>
    /// <param name="capacity">Maximum number of shadow map entries to support.</param>
    public ShadowAtlasPacker(int capacity)
    {
        MaxShadowMaps = capacity;
        regions = new ShadowAtlasRegion[capacity];
        sortBuffer = new (int, int, int)[capacity];
    }

    /// <summary>Packs the given shadow map requests into an atlas of the specified size and returns the resulting regions.</summary>
    /// <param name="atlasSize">Width and height of the square atlas texture in texels.</param>
    /// <param name="requests">Shadow map dimension requests to pack.</param>
    /// <returns>A span of regions in request-index order; regions that did not fit will have zero dimensions.</returns>
    public Span<ShadowAtlasRegion> Pack(int atlasSize, ReadOnlySpan<ShadowRequest> requests)
    {
        var count = requests.Length;
        if (count == 0)
        {
            return [];
        }

        Debug.Assert(count <= regions.Length, "ShadowAtlasPacker capacity exceeded.");
        count = Math.Min(count, regions.Length);

        for (var i = 0; i < count; i++)
        {
            sortBuffer[i] = (requests[i].Height, requests[i].Width, i);
        }

        sortBuffer.AsSpan(0, count).Sort();
        regions.AsSpan(0, count).Clear();

        var penX = 0;
        var shelfY = 0;
        var shelfHeight = 0;

        for (var s = count - 1; s >= 0; s--)
        {
            var idx = sortBuffer[s].Index;
            var w = requests[idx].Width;
            var h = requests[idx].Height;

            if (w <= 0 || h <= 0)
            {
                continue;
            }

            if (penX + w > atlasSize)
            {
                shelfY += shelfHeight;
                shelfHeight = 0;
                penX = 0;
            }

            if (shelfY + h > atlasSize)
            {
                continue;
            }

            regions[idx] = new ShadowAtlasRegion(penX, shelfY, w, h);

            shelfHeight = Math.Max(shelfHeight, h);
            penX += w;
        }

        return regions.AsSpan(0, count);
    }
}
