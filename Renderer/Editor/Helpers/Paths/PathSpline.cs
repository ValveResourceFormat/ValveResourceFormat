using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The spline through the nodes of a path entity, in the world: a cubic Bezier between each two nodes, out of
/// one along its out handle and into the next along its in handle. A position along the whole spline, from
/// zero to one, runs through each segment in proportion to its length.
/// </summary>
internal sealed class PathSpline
{
    private const string NodesKey = "pathnodes";
    private const string ClosedLoopKey = "closed_loop";

    // A segment is measured with twice as many points each time, until its length changes by less than this part
    private const float LengthTolerance = 0.01f;
    private const int MaxLengthPoints = 128;

    private readonly PathParticleRopeNode[] Nodes;

    // Where each node is along the spline, from zero to one
    private readonly float[] Knots;

    /// <summary>Gets the length of the spline.</summary>
    public float Length { get; }

    private PathSpline(PathParticleRopeNode[] nodes)
    {
        Nodes = nodes;
        Knots = new float[nodes.Length];

        for (var i = 0; i < nodes.Length - 1; i++)
        {
            var (p0, p1, p2, p3) = GetControlPoints(i);
            Length += MeasureSegment(p0, p1, p2, p3);
            Knots[i + 1] = Length;
        }

        if (Length > 0f)
        {
            for (var i = 0; i < Knots.Length; i++)
            {
                Knots[i] /= Length;
            }
        }
    }

    /// <summary>Creates the spline of a path.</summary>
    /// <param name="owner">The path entity.</param>
    /// <returns>The spline, or <see langword="null"/> when the path has fewer than two nodes.</returns>
    public static PathSpline? Create(IHelperOwner owner)
    {
        var nodes = ReadNodes(owner, includeClosingNode: true);

        return nodes.Length >= 2 ? new PathSpline(nodes) : null;
    }

    /// <summary>Reads the nodes of a path, which it keeps in its own space, placed in the world.</summary>
    /// <param name="owner">The path entity.</param>
    /// <param name="includeClosingNode">
    /// Whether a closed path keeps the copy of its first node it ends on, as its spline does.
    /// </param>
    /// <returns>The nodes.</returns>
    public static PathParticleRopeNode[] ReadNodes(IHelperOwner owner, bool includeClosingNode)
    {
        var nodes = PathParticleRope.ParseNodes(owner.KeyValues.GetStringProperty(NodesKey));
        var count = nodes.Count;

        // A closed path of three nodes or more ends on its first node again
        if (!includeClosingNode && count > 3 && owner.KeyValues.GetInt32Property(ClosedLoopKey) != 0)
        {
            count--;
        }

        var transform = owner.Transform;
        var placed = new PathParticleRopeNode[count];

        for (var i = 0; i < count; i++)
        {
            var node = nodes[i];

            placed[i] = new PathParticleRopeNode
            {
                Position = Vector3.Transform(node.Position, transform),
                InTangent = Vector3.TransformNormal(node.InTangent, transform),
                OutTangent = Vector3.TransformNormal(node.OutTangent, transform),
            };
        }

        return placed;
    }

    /// <summary>Gets a point on the spline.</summary>
    /// <param name="position">How far along the spline the point is, from zero to one.</param>
    /// <returns>The point.</returns>
    public Vector3 GetPoint(float position)
    {
        var last = Knots.Length - 1;
        position = Math.Clamp(position, Knots[0], Knots[last]);

        // The segment is the first one that ends past the position
        var segment = 0;

        while (segment < last - 1 && Knots[segment + 1] <= position)
        {
            segment++;
        }

        var start = Knots[segment];
        var end = Knots[segment + 1];
        var t = start < end ? Math.Clamp((position - start) / (end - start), 0f, 1f) : 0f;
        var s = 1f - t;

        var (p0, p1, p2, p3) = GetControlPoints(segment);

        return s * s * s * p0 + 3f * s * s * t * p1 + 3f * s * t * t * p2 + t * t * t * p3;
    }

    private (Vector3 P0, Vector3 P1, Vector3 P2, Vector3 P3) GetControlPoints(int segment)
    {
        var from = Nodes[segment];
        var to = Nodes[segment + 1];

        return (from.Position, from.Position + from.OutTangent, to.Position + to.InTangent, to.Position);
    }

    // The length of the lines through evenly spaced points of the curve, with more points until it settles
    private static float MeasureSegment(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        var a = p1 * 3f - p0 - p2 * 3f + p3;
        var b = p0 * 3f - p1 * 6f + p2 * 3f;
        var c = p1 * 3f - p0 * 3f;

        var length = 0f;

        for (var count = 2; count <= MaxLengthPoints; count *= 2)
        {
            var previousLength = length;
            var previous = p0;
            var step = 1f / (count - 1);

            length = 0f;

            for (var i = 1; i < count; i++)
            {
                var t = i * step;
                var point = a * (t * t * t) + b * (t * t) + c * t + p0;

                length += Vector3.Distance(point, previous);
                previous = point;
            }

            if (MathF.Abs(length - previousLength) / length < LengthTolerance)
            {
                break;
            }
        }

        return length;
    }
}
