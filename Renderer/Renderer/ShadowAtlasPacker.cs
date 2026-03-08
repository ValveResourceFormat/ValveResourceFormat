using System.Diagnostics;

namespace ValveResourceFormat.Renderer;

public record struct ShadowRequest(int Width, int Height);

public record struct ShadowAtlasRegion(int X, int Y, int Width, int Height)
{
    public readonly bool IsValid => Width > 0 && Height > 0;
}

public class ShadowAtlasPacker
{
    public int MaxShadowMaps { get; }
    private readonly ShadowAtlasRegion[] regions;
    private readonly (int Height, int Width, int Index)[] sortBuffer;

    public ShadowAtlasPacker(int capacity)
    {
        MaxShadowMaps = capacity;
        regions = new ShadowAtlasRegion[capacity];
        sortBuffer = new (int, int, int)[capacity];
    }

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
