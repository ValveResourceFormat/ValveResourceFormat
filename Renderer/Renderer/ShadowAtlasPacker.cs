using System;
using System.Collections.Generic;

namespace ValveResourceFormat.Renderer;

public struct ShadowRequest
{
    public int Width { get; set; }
    public int Height { get; set; }
}

public struct ShadowAtlasRegion
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool IsValid => Width > 0 && Height > 0;
}

public class ShadowAtlasPacker
{
    private ShadowAtlasRegion[] regions = [];
    private (int Height, int Width, int Index)[] sortBuffer = [];

    public Span<ShadowAtlasRegion> Pack(int atlasSize, List<ShadowRequest> requests)
    {
        var count = requests.Count;
        if (count == 0)
        {
            return [];
        }

        if (regions.Length < count)
        {
            regions = new ShadowAtlasRegion[int.Max(count, regions.Length * 2)];
        }

        if (sortBuffer.Length < count)
        {
            sortBuffer = new (int, int, int)[int.Max(count, sortBuffer.Length * 2)];
        }

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

            regions[idx] = new ShadowAtlasRegion
            {
                X = penX,
                Y = shelfY,
                Width = w,
                Height = h,
            };

            shelfHeight = Math.Max(shelfHeight, h);
            penX += w;
        }

        return regions.AsSpan(0, count);
    }
}
