namespace ValveResourceFormat.Graphs;

/// <summary>
/// The square cells the layout passes bucket wires and cards into, so a query only visits what
/// sits near the box or segment it asks about.
/// </summary>
internal static class GraphGrid
{
    private const float CellSize = 256f;

    private static int Cell(float value) => (int)MathF.Floor(value / CellSize);

    /// <summary>Replaces the contents of <paramref name="into"/> with the cells a box overlaps.</summary>
    public static void BoxCells(Vector2 min, Vector2 max, List<(int, int)> into)
    {
        into.Clear();

        for (var x = Cell(min.X); x <= Cell(max.X); x++)
        {
            for (var y = Cell(min.Y); y <= Cell(max.Y); y++)
            {
                into.Add((x, y));
            }
        }
    }

    /// <summary>
    /// Replaces the contents of <paramref name="into"/> with the cells a segment passes through, so
    /// a long diagonal wire is bucketed along its length rather than over its whole bounding box.
    /// </summary>
    public static void SegmentCells(Vector2 a, Vector2 b, List<(int, int)> into)
    {
        into.Clear();

        var x = Cell(a.X);
        var y = Cell(a.Y);
        var endX = Cell(b.X);
        var endY = Cell(b.Y);
        var direction = b - a;
        var stepX = Math.Sign(endX - x);
        var stepY = Math.Sign(endY - y);
        var deltaX = direction.X != 0f ? MathF.Abs(CellSize / direction.X) : float.PositiveInfinity;
        var deltaY = direction.Y != 0f ? MathF.Abs(CellSize / direction.Y) : float.PositiveInfinity;
        var nextX = direction.X != 0f ? (((stepX > 0 ? x + 1 : x) * CellSize) - a.X) / direction.X : float.PositiveInfinity;
        var nextY = direction.Y != 0f ? (((stepY > 0 ? y + 1 : y) * CellSize) - a.Y) / direction.Y : float.PositiveInfinity;

        into.Add((x, y));

        while (x != endX || y != endY)
        {
            if ((nextX < nextY && x != endX) || y == endY)
            {
                x += stepX;
                nextX += deltaX;
            }
            else
            {
                y += stepY;
                nextY += deltaY;
            }

            into.Add((x, y));
        }
    }
}
