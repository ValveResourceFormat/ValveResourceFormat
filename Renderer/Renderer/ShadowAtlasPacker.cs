namespace ValveResourceFormat.Renderer;

/// <summary>Describes the position and size of a shadow map region within the atlas texture.</summary>
public record struct ShadowAtlasRegion(int X, int Y, int Width, int Height)
{
    /// <summary>Gets whether this region has valid non-zero dimensions.</summary>
    public readonly bool IsValid => Width > 0 && Height > 0;
}

/// <summary>Allocates shadow map regions from an atlas using a bitmask occupancy grid of <see cref="CellSize"/>-pixel cells.</summary>
public class ShadowAtlasPacker
{
    /// <summary>Size in pixels of one occupancy grid cell.</summary>
    public const int CellSize = 64;

    private ulong[] occupancy = new ulong[64];
    private ulong[] combinedRow = new ulong[1];
    private int gridCells;
    private int wordsPerRow;
    private ulong lastWordOccupiedTail;

    /// <summary>Resets the atlas to empty for a new allocation pass.</summary>
    /// <param name="atlasSize">Width and height of the square atlas texture in texels.</param>
    public void Begin(int atlasSize)
    {
        gridCells = Math.Max(atlasSize / CellSize, 0);
        wordsPerRow = (gridCells + 63) >> 6;

        if (combinedRow.Length < wordsPerRow)
        {
            combinedRow = new ulong[wordsPerRow];
        }

        if (occupancy.Length < gridCells * wordsPerRow)
        {
            occupancy = new ulong[gridCells * wordsPerRow];
        }

        var tailBits = gridCells & 63;
        lastWordOccupiedTail = tailBits == 0 ? 0UL : ~((1UL << tailBits) - 1);
        occupancy.AsSpan(0, gridCells * wordsPerRow).Clear();
    }

    /// <summary>Attempts to allocate a cell-aligned region of the given pixel dimensions.</summary>
    /// <param name="width">Requested region width in texels.</param>
    /// <param name="height">Requested region height in texels.</param>
    /// <param name="region">The allocated region, or a zero region on failure.</param>
    /// <returns>True if the region was allocated.</returns>
    public bool TryAllocate(int width, int height, out ShadowAtlasRegion region)
    {
        region = default;

        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var wCells = (width + CellSize - 1) / CellSize;
        var hCells = (height + CellSize - 1) / CellSize;

        if (wCells > gridCells || hCells > gridCells)
        {
            return false;
        }

        var combined = combinedRow.AsSpan(0, wordsPerRow);
        var maxY = gridCells - hCells;

        for (var y = 0; y <= maxY; y++)
        {
            BuildCombinedRow(y, hCells, combined);

            var cellX = FindFreeRun(combined, wCells);
            if (cellX >= 0)
            {
                for (var r = y; r < y + hCells; r++)
                {
                    SetBits(occupancy.AsSpan(r * wordsPerRow, wordsPerRow), cellX, wCells);
                }

                region = new ShadowAtlasRegion(cellX * CellSize, y * CellSize, width, height);
                return true;
            }
        }

        return false;
    }

    private void BuildCombinedRow(int y, int hCells, Span<ulong> combined)
    {
        occupancy.AsSpan(y * wordsPerRow, wordsPerRow).CopyTo(combined);

        for (var r = y + 1; r < y + hCells; r++)
        {
            var row = occupancy.AsSpan(r * wordsPerRow, wordsPerRow);
            for (var w = 0; w < combined.Length; w++)
            {
                combined[w] |= row[w];
            }
        }

        combined[^1] |= lastWordOccupiedTail;
    }

    private static int FindFreeRun(ReadOnlySpan<ulong> combined, int wCells)
    {
        var run = 0;

        for (var w = 0; w < combined.Length; w++)
        {
            var occupied = combined[w];
            var pos = 0;

            while (pos < 64)
            {
                var shifted = occupied >> pos;
                var freeBits = shifted == 0 ? 64 - pos : BitOperations.TrailingZeroCount(shifted);
                run += freeBits;

                if (run >= wCells)
                {
                    return w * 64 + pos + freeBits - run;
                }

                pos += freeBits;
                if (pos >= 64)
                {
                    break;
                }

                pos += BitOperations.TrailingZeroCount(~(occupied >> pos));
                run = 0;
            }
        }

        return -1;
    }

    private static void SetBits(Span<ulong> row, int start, int count)
    {
        while (count > 0)
        {
            var word = start >> 6;
            var bit = start & 63;
            var take = Math.Min(count, 64 - bit);
            row[word] |= (take == 64 ? ulong.MaxValue : (1UL << take) - 1) << bit;
            start += take;
            count -= take;
        }
    }
}
