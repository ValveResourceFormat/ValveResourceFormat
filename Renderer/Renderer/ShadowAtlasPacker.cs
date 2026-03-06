using System;

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

public static class ShadowAtlasPacker
{
    public static ShadowAtlasRegion[] Pack(int atlasSize, ShadowRequest[] requests)
    {
        var count = requests.Length;
        var regions = new ShadowAtlasRegion[count];

        if (count == 0)
        {
            return regions;
        }

        var sorted = new int[count];
        for (var i = 0; i < count; i++)
        {
            sorted[i] = i;
        }

        Array.Sort(sorted, (a, b) =>
        {
            var cmp = requests[b].Height.CompareTo(requests[a].Height);
            return cmp != 0 ? cmp : requests[b].Width.CompareTo(requests[a].Width);
        });

        var penX = 0;
        var shelfY = 0;
        var shelfHeight = 0;

        foreach (var idx in sorted)
        {
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

        return regions;
    }
}
