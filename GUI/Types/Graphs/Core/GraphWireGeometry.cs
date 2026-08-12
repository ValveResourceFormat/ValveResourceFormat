using ValveResourceFormat.Graphs;
namespace GUI.Types.Graphs.Core;

/// <summary>
/// Wire geometry shared by drawing and hit testing, which build one path per wire so a click lands
/// on the line that is on screen.
/// </summary>
internal static class GraphWireGeometry
{
    /// <summary>Longest a backward wire's handle may grow.</summary>
    private const float BackwardHandleLimit = 250f;

    /// <summary>Horizontal run a straight wire leaves its socket on before angling away.</summary>
    public const float StraightStub = 14f;

    /// <summary>
    /// Length of the horizontal bezier handles. It grows with horizontal distance and is damped
    /// when the endpoints are nearly level, so a wire between aligned sockets draws straight.
    /// </summary>
    public static float HandleOffset(Vector2 from, Vector2 to)
    {
        var dx = to.X - from.X;
        var distX = Math.Abs(dx);
        var distY = Math.Abs(to.Y - from.Y);

        if (dx >= 0f)
        {
            var clampFactor = distX <= 0f ? 1f : Math.Min(1f, 3.5f * distY / distX);
            return 0.4f * distX * clampFactor;
        }

        return Math.Min(0.5f * distX + 40f, BackwardHandleLimit);
    }
}
