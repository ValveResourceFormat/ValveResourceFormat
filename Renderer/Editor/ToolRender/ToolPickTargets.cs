namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// What a <see cref="ToolRenderContext"/> draws inside its pick objects, which a ray through the cursor picks the
/// nearest of. This is how Hammer finds the part of a tool under the cursor: by the faces and handles it draws for
/// it, including those drawn in <see cref="ToolRenderMode.PickProxy"/> only to be picked.
/// </summary>
public sealed class ToolPickTargets
{
    private readonly List<(Vector3 A, Vector3 B, Vector3 C, int Id)> triangles = [];
    private readonly List<(Vector3 Position, float Size, int Id)> handles = [];

    /// <summary>Gets whether nothing has been drawn to be picked.</summary>
    public bool IsEmpty => triangles.Count == 0 && handles.Count == 0;

    /// <summary>Drops everything drawn.</summary>
    public void Clear()
    {
        triangles.Clear();
        handles.Clear();
    }

    internal void AddTriangle(Vector3 a, Vector3 b, Vector3 c, int id) => triangles.Add((a, b, c, id));

    internal void AddHandle(Vector3 position, float size, int id) => handles.Add((position, size, id));

    /// <summary>
    /// Finds what is nearest the camera under a point of the screen: faces from either side, and handles by the
    /// square of their size around where they are on the screen.
    /// </summary>
    /// <param name="camera">The camera the shapes are seen with.</param>
    /// <param name="screenPoint">The point, in pixels from the top left of the view.</param>
    /// <returns>The pick object id of what is hit, or <see langword="null"/> when nothing is.</returns>
    public int? Pick(Camera camera, Vector2 screenPoint)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var origin = camera.Location;
        var direction = camera.ScreenPointToDirection(screenPoint);
        var nearest = float.MaxValue;
        int? hit = null;

        foreach (var (a, b, c, id) in triangles)
        {
            if (IntersectTriangle(origin, direction, a, b, c) is { } distance && distance < nearest)
            {
                nearest = distance;
                hit = id;
            }
        }

        foreach (var (position, size, id) in handles)
        {
            if (camera.WorldToScreenPoint(position) is not { } screen)
            {
                continue;
            }

            var offset = Vector2.Abs(screen - screenPoint);
            var distance = Vector3.Dot(position - origin, direction);

            if (offset.X <= size && offset.Y <= size && distance < nearest)
            {
                nearest = distance;
                hit = id;
            }
        }

        return hit;
    }

    // Distance along a ray to where it crosses a triangle, from either side
    private static float? IntersectTriangle(Vector3 origin, Vector3 direction, Vector3 a, Vector3 b, Vector3 c)
    {
        const float Epsilon = 1e-9f;

        var edgeAB = b - a;
        var edgeAC = c - a;
        var p = Vector3.Cross(direction, edgeAC);
        var determinant = Vector3.Dot(edgeAB, p);

        if (MathF.Abs(determinant) < Epsilon)
        {
            return null;
        }

        var inverse = 1f / determinant;
        var toOrigin = origin - a;
        var u = Vector3.Dot(toOrigin, p) * inverse;

        if (u < 0f || u > 1f)
        {
            return null;
        }

        var q = Vector3.Cross(toOrigin, edgeAB);
        var v = Vector3.Dot(direction, q) * inverse;

        if (v < 0f || u + v > 1f)
        {
            return null;
        }

        var distance = Vector3.Dot(edgeAC, q) * inverse;
        return distance > 0f ? distance : null;
    }
}
