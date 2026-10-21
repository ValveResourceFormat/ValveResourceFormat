using System.Linq;

namespace ValveResourceFormat.Renderer.Utils;

/// <summary>Offset of a light probe volume's grid in the atlas, in probes.</summary>
public readonly record struct LightProbeAtlasSlot(int X, int Y, int Z);

/// <summary>
/// Packs light probe volume grids into a single 3D atlas. All sizes are in probes, matching the direct light textures.
/// The irradiance texture is six times deeper (one slice per ambient cube face), which the caller accounts for.
/// </summary>
/// <remarks>
/// Slots are aligned to 4 in X and Y so block compressed textures can be copied block by block. No padding is needed
/// between slots because the shader clamps lookups half a probe inside each volume.
/// </remarks>
public static class LightProbeAtlasPacker
{
    /// <summary>Slot alignment in X and Y, the size of a compressed block.</summary>
    public const int BlockSize = 4;

    /// <summary>Packs grids into an atlas within the given size limits.</summary>
    /// <param name="sizes">Grid sizes, in probes.</param>
    /// <param name="maxSide">Maximum atlas width and height.</param>
    /// <param name="maxDepth">Maximum atlas depth.</param>
    /// <param name="atlasSize">Resulting atlas size, in probes.</param>
    /// <returns>Slot per grid in the same order as <paramref name="sizes"/>, <see langword="null"/> where a grid did not fit.</returns>
    public static LightProbeAtlasSlot?[] Pack(IReadOnlyList<(int Width, int Height, int Depth)> sizes, int maxSide, int maxDepth,
        out (int Width, int Height, int Depth) atlasSize)
    {
        ArgumentNullException.ThrowIfNull(sizes);

        atlasSize = default;

        // Largest first, so smaller grids can fill the gaps left behind
        var order = Enumerable.Range(0, sizes.Count)
            .Where(i => sizes[i] is { Width: > 0, Height: > 0, Depth: > 0 } size
                && Align(size.Width) <= maxSide && Align(size.Height) <= maxSide && size.Depth <= maxDepth)
            .OrderByDescending(i => (long)sizes[i].Width * sizes[i].Height * sizes[i].Depth)
            .ToArray();

        if (order.Length == 0)
        {
            return new LightProbeAtlasSlot?[sizes.Count];
        }

        var minWidth = Align(order.Max(i => sizes[i].Width));
        var minHeight = Align(order.Max(i => sizes[i].Height));

        LightProbeAtlasSlot?[]? best = null;
        var bestPlaced = 0;
        var bestVolume = long.MaxValue;

        // Depth grows as needed up to maxDepth, so try a range of XY footprints and keep the one that places the most
        // grids with the smallest total volume
        for (var width = minWidth; width <= maxSide; width = NextSide(width))
        {
            for (var height = minHeight; height <= maxSide; height = NextSide(height))
            {
                var slots = PackInto(order, sizes, width, height, maxDepth, out var placed, out var depth);
                var volume = (long)width * height * depth;

                if (placed > bestPlaced || (placed == bestPlaced && placed > 0 && volume < bestVolume))
                {
                    (best, bestPlaced, bestVolume) = (slots, placed, volume);
                    atlasSize = (width, height, depth);
                }
            }
        }

        return best ?? new LightProbeAtlasSlot?[sizes.Count];
    }

    private static int Align(int value) => (value + BlockSize - 1) / BlockSize * BlockSize;

    // Grows by 25% per step, at least one block
    private static int NextSide(int side) => Align(side + Math.Max(side / 4, BlockSize));

    // Guillotine packing into a fixed XY footprint: each placed grid splits its free box into the remaining space
    // along X, Y and Z
    private static LightProbeAtlasSlot?[] PackInto(int[] order, IReadOnlyList<(int Width, int Height, int Depth)> sizes,
        int binWidth, int binHeight, int maxDepth, out int placed, out int usedDepth)
    {
        var slots = new LightProbeAtlasSlot?[sizes.Count];
        var free = new List<(int X, int Y, int Z, int Width, int Height, int Depth)> { (0, 0, 0, binWidth, binHeight, maxDepth) };

        placed = 0;
        usedDepth = 0;

        foreach (var index in order)
        {
            var (width, height, depth) = (Align(sizes[index].Width), Align(sizes[index].Height), sizes[index].Depth);
            var best = -1;
            var bestCost = long.MaxValue;

            for (var i = 0; i < free.Count; i++)
            {
                var box = free[i];

                if (box.Width < width || box.Height < height || box.Depth < depth)
                {
                    continue;
                }

                // Prefer the lowest Z to keep the atlas shallow, then the smallest box to keep large ones free
                var cost = ((long)box.Z << 40) + (long)box.Width * box.Height * box.Depth;

                if (cost < bestCost)
                {
                    (best, bestCost) = (i, cost);
                }
            }

            if (best < 0)
            {
                continue;
            }

            var chosen = free[best];
            free.RemoveAt(best);

            slots[index] = new LightProbeAtlasSlot(chosen.X, chosen.Y, chosen.Z);
            placed++;
            usedDepth = Math.Max(usedDepth, chosen.Z + depth);

            if (chosen.Width > width)
            {
                free.Add((chosen.X + width, chosen.Y, chosen.Z, chosen.Width - width, chosen.Height, chosen.Depth));
            }

            if (chosen.Height > height)
            {
                free.Add((chosen.X, chosen.Y + height, chosen.Z, width, chosen.Height - height, chosen.Depth));
            }

            if (chosen.Depth > depth)
            {
                free.Add((chosen.X, chosen.Y, chosen.Z + depth, width, height, chosen.Depth - depth));
            }
        }

        return slots;
    }
}
