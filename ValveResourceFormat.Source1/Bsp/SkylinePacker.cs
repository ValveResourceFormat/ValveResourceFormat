namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Packs rectangles into an area of a fixed size. The skyline is the top edge of the rectangles placed so far, and
/// a new rectangle rests at the lowest place along it, the leftmost one when several are equally low.
/// </summary>
internal sealed class SkylinePacker
{
    private readonly record struct Segment(int X, int Width, int Y);

    private readonly int width;
    private readonly int height;
    private readonly List<Segment> skyline = [];

    public SkylinePacker(int width, int height)
    {
        this.width = width;
        this.height = height;

        skyline.Add(new Segment(0, width, 0));
    }

    /// <summary>Places a rectangle.</summary>
    /// <param name="rectWidth">The width of the rectangle.</param>
    /// <param name="rectHeight">The height of the rectangle.</param>
    /// <param name="x">The left edge of the place.</param>
    /// <param name="y">The top edge of the place.</param>
    /// <returns>Whether the rectangle fits in the area.</returns>
    public bool TryPack(int rectWidth, int rectHeight, out int x, out int y)
    {
        var bestIndex = -1;
        var bestTop = int.MaxValue;
        var bestY = 0;

        for (var i = 0; i < skyline.Count; i++)
        {
            if (!TryGetRestingHeight(i, rectWidth, out var restingY))
            {
                continue;
            }

            var top = restingY + rectHeight;

            if (top > height || top >= bestTop)
            {
                continue;
            }

            bestIndex = i;
            bestTop = top;
            bestY = restingY;
        }

        if (bestIndex < 0)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = skyline[bestIndex].X;
        y = bestY;
        Raise(bestIndex, rectWidth, bestTop);
        return true;
    }

    private bool TryGetRestingHeight(int index, int rectWidth, out int restingY)
    {
        restingY = 0;

        if (skyline[index].X + rectWidth > width)
        {
            return false;
        }

        var remaining = rectWidth;

        for (var i = index; remaining > 0; i++)
        {
            restingY = Math.Max(restingY, skyline[i].Y);
            remaining -= skyline[i].Width;
        }

        return true;
    }

    private void Raise(int index, int rectWidth, int top)
    {
        var x = skyline[index].X;
        var right = x + rectWidth;

        skyline.Insert(index, new Segment(x, rectWidth, top));

        var next = index + 1;

        while (next < skyline.Count && skyline[next].X < right)
        {
            var covered = skyline[next];
            var coveredRight = covered.X + covered.Width;

            if (coveredRight > right)
            {
                skyline[next] = new Segment(right, coveredRight - right, covered.Y);
                break;
            }

            skyline.RemoveAt(next);
        }

        if (next < skyline.Count && skyline[next].Y == top)
        {
            skyline[index] = skyline[index] with { Width = skyline[index].Width + skyline[next].Width };
            skyline.RemoveAt(next);
        }

        if (index > 0 && skyline[index - 1].Y == top)
        {
            skyline[index - 1] = skyline[index - 1] with { Width = skyline[index - 1].Width + skyline[index].Width };
            skyline.RemoveAt(index);
        }
    }
}
