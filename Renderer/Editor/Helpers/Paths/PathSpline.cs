using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The spline through the nodes of a path entity, in the world: a cubic Bezier between each two nodes, out of
/// one along its out handle and into the next along its in handle.
/// </summary>
internal sealed class PathSpline
{
    private const string NodesKey = "pathnodes";
    private const string ClosedLoopKey = "closed_loop";

    // A segment is measured with twice as many points each time, until its length changes by less than this part
    private const float LengthTolerance = 0.01f;
    private const int MaxLengthPoints = 128;

    /// <summary>Gets the length of the spline.</summary>
    public float Length { get; }

    private PathSpline(PathParticleRopeNode[] nodes)
    {
        for (var i = 0; i < nodes.Length - 1; i++)
        {
            var from = nodes[i];
            var to = nodes[i + 1];

            Length += MeasureSegment(from.Position, from.Position + from.OutTangent, to.Position + to.InTangent, to.Position);
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
