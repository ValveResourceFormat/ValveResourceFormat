using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// Straight lanes between the nodes of a path, reaching as far to each side of a node as its radius scale,
/// with an arrow above each pointing from one node to the next. Each node faces along the path, halfway
/// between the directions it is reached from and left in, upright to the path but rolled as far about that
/// direction as the node itself is.
/// </summary>
internal sealed class AccompanyHelper(IHelperOwner owner, FgdHelper declaration) : EntityHelper(owner, declaration)
{
    /// <inheritdoc/>
    public override bool IsToolShape => true;

    /// <inheritdoc/>
    public override bool NeedsShowHelpers => true;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => true;

    private const string RadiusScalesKey = "pathnoderadiusscales";

    // How far above the lanes the arrows run
    private const float ArrowHeight = 5f;

    private const float MaxHeadLength = 20f;

    // Below this the up axis of the path is taken to run along the path
    private const float MinSideLength = 1.1920929e-7f;

    private static readonly Color32 LaneColor = new(64, 255, 255, 16);
    private static readonly Color32 ArrowColor = new(255, 255, 255, 128);

    // Made once every node of the path exists
    private NodeFrame[]? Frames;

    /// <inheritdoc/>
    public override void AddToScene()
    {
        AddShapes();
    }

    private NodeFrame[] GetFrames()
    {
        if (Frames != null)
        {
            return Frames;
        }

        var nodes = PathSpline.ReadNodes(Owner, includeClosingNode: false);
        var radiusScales = PathParticleRope.ParseRadiusScales(Owner.KeyValues.GetStringProperty(RadiusScalesKey));
        var pathUp = Vector3.TransformNormal(Vector3.UnitZ, Owner.RigidTransform);
        var count = nodes.Length;
        var previousDirection = Vector3.Zero;

        var frames = new NodeFrame[count];

        for (var i = 0; i < count; i++)
        {
            var origin = nodes[i].Position;

            var direction = i < count - 1 ? MathUtils.SafeNormalize(nodes[i + 1].Position - origin)
                : count == 1 ? Vector3.UnitX
                : MathUtils.SafeNormalize(origin - nodes[i - 1].Position);

            var forward = i > 0 && i < count - 1 ? MathUtils.SafeNormalize(direction + previousDirection) : direction;
            previousDirection = direction;

            var (left, up) = GetAxes(pathUp, forward);

            if (Owner.FindPathNode(i) is { } node)
            {
                (left, up) = ApplyRoll(node.RigidTransform, forward, left, up);
            }

            var radiusScale = i < radiusScales.Length ? radiusScales[i] : 1f;

            frames[i] = new NodeFrame(origin, left * radiusScale, up);
        }

        return Frames = frames;
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        var frames = GetFrames();

        if (frames.Length < 2)
        {
            return;
        }

        var lineWidth = context.LineWidth;

        context.PushRenderMode(ToolRenderMode.TranslucentFlat);

        for (var i = 1; i < frames.Length; i++)
        {
            var (fromOrigin, fromSide, fromUp) = frames[i - 1];
            var (toOrigin, toSide, toUp) = frames[i];

            var start = fromOrigin + fromUp * ArrowHeight;
            var end = toOrigin + toUp * ArrowHeight;
            var length = Vector3.Distance(start, end);
            var direction = MathUtils.SafeNormalize(end - start);
            var headLength = MathF.Min(length * 0.25f, MaxHeadLength);

            // The lane is drawn on the nodes, and again at the height of the arrow facing the other way
            var laneA = fromOrigin - fromSide;
            var laneB = fromOrigin + fromSide;
            var laneC = toOrigin + toSide;
            var laneD = toOrigin - toSide;
            var raisedA = start - fromSide;
            var raisedB = start + fromSide;
            var raisedC = end + toSide;
            var raisedD = end - toSide;

            DrawFan(context, (fromOrigin + toOrigin) * 0.5f, laneA, laneB, laneC, laneD, reversed: false);
            DrawFan(context, (start + end) * 0.5f, raisedA, raisedB, raisedC, raisedD, reversed: true);

            context.PushRenderMode(ToolRenderMode.WireframeTranslucent);
            context.LineWidth = 1.5f;
            context.DrawQuadOutline(laneA, laneB, laneC, laneD, ArrowColor);
            context.DrawQuadOutline(raisedA, raisedB, raisedC, raisedD, ArrowColor);

            var headBase = end - direction * headLength;

            context.LineWidth = 8f;
            context.DrawLine(start, headBase, ArrowColor);
            context.PopRenderMode();

            if (headLength != 0f)
            {
                var headSide = new Vector3(-direction.Y, direction.X, 0f) * (headLength * 0.25f);

                context.DrawTriangle(end, headBase - headSide, headBase + headSide, ArrowColor);
                context.DrawTriangle(headBase + headSide, headBase - headSide, end, ArrowColor);
            }

            context.DrawQuad(laneB, raisedB, raisedC, laneC, LaneColor);
            context.DrawQuad(laneA, laneD, raisedD, raisedA, LaneColor);

            // The ends of the whole path are closed
            if (i == 1)
            {
                context.DrawQuad(laneA, raisedA, raisedB, laneB, LaneColor);
            }
            else if (i == frames.Length - 1)
            {
                context.DrawQuad(laneD, laneC, raisedC, raisedD, LaneColor);
            }
        }

        context.PopRenderMode();
        context.LineWidth = lineWidth;
    }

    private static void DrawFan(ToolRenderContext context, Vector3 center, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool reversed)
    {
        ReadOnlySpan<Vector3> corners = [a, b, c, d];

        for (var i = 0; i < corners.Length; i++)
        {
            var corner = corners[i];
            var next = corners[(i + 1) % corners.Length];

            if (reversed)
            {
                context.DrawTriangle(next, corner, center, LaneColor);
            }
            else
            {
                context.DrawTriangle(center, corner, next, LaneColor);
            }
        }
    }

    // The node keeps how far it is rolled about its forward axis from the frame upright to the path
    private static (Vector3 Left, Vector3 Up) ApplyRoll(Matrix4x4 nodeTransform, Vector3 forward, Vector3 left, Vector3 up)
    {
        var frame = new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0f,
            left.X, left.Y, left.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f);

        nodeTransform.Translation = Vector3.Zero;

        if (!Matrix4x4.Invert(frame, out var worldToFrame))
        {
            return (left, up);
        }

        var roll = EntityTransformHelper.ToEulerAngles(Quaternion.CreateFromRotationMatrix(nodeTransform * worldToFrame)).Z;

        if (roll == 0f)
        {
            return (left, up);
        }

        var rolled = EntityTransformHelper.EulerAnglesToRotationMatrix(new Vector3(0f, 0f, roll)) * frame;

        return (new Vector3(rolled.M21, rolled.M22, rolled.M23), new Vector3(rolled.M31, rolled.M32, rolled.M33));
    }

    // The left and up axes of a node facing forward, upright to the path where it can be
    private static (Vector3 Left, Vector3 Up) GetAxes(Vector3 pathUp, Vector3 forward)
    {
        var left = Vector3.Cross(pathUp, forward);
        var length = left.Length();

        if (length < MinSideLength)
        {
            left = MathF.Abs(pathUp.X) >= 0.001f || MathF.Abs(pathUp.Y) >= 0.001f
                ? MathUtils.SafeNormalize(new Vector3(-pathUp.Y, pathUp.X, 0f))
                : Vector3.UnitY;
        }
        else
        {
            left /= length;
        }

        return (left, MathUtils.SafeNormalize(Vector3.Cross(forward, left)));
    }

    private readonly record struct NodeFrame(Vector3 Origin, Vector3 Side, Vector3 Up);
}
