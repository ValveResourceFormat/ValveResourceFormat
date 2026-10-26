using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// Draws the shapes of the editor the way Hammer's tool render context does: a stack of render modes that
/// say whether shapes are lines or faces, opaque or blended, hidden by the scene or over it, a stack of
/// transforms, and a draw color and line width that the shapes take as they are drawn. What is drawn goes
/// into a <see cref="ToolGeometry"/>.
/// </summary>
public sealed class ToolRenderContext
{
    private const int StackDepth = 8;

    // Faces are lit by the CPU in the flat modes, from this direction
    private static readonly Vector3 FlatShadeLightDirection = Vector3.Normalize(new Vector3(1f, 2f, 3f));

    // What lines carry as their normal
    private static readonly Vector3 DefaultNormal = Vector3.UnitZ;

    private readonly ToolRenderMode[] modeStack = new ToolRenderMode[StackDepth];
    private readonly Matrix4x4[] transformStack = new Matrix4x4[StackDepth];
    private int modeDepth;
    private int transformDepth;
    private ToolPickTargets? pickTargets;
    private int pickId;

    /// <summary>Gets what is drawn.</summary>
    public ToolGeometry Output { get; }

    /// <summary>Gets or sets the color shapes take when they are not given one.</summary>
    public Color32 DrawColor { get; set; } = Color32.White;

    /// <summary>
    /// Gets or sets the width lines are drawn with, in pixels. At zero they are the thinnest the GPU draws.
    /// </summary>
    public float LineWidth { get; set; }

    /// <summary>
    /// Gets or sets the layer shapes are drawn in whatever their mode, or <see langword="null"/> for the one of
    /// their mode.
    /// </summary>
    public ToolRenderLayer? LayerOverride { get; set; }

    /// <summary>Gets or sets whether triangles get the normal of their face, which the shaded modes light them by.</summary>
    public bool ComputeFaceNormals { get; set; }

    /// <summary>Gets or sets the camera the shapes that face the camera are turned towards.</summary>
    public Camera? Camera { get; set; }

    /// <summary>Gets the current render mode.</summary>
    public ToolRenderMode RenderMode => modeStack[modeDepth];

    /// <summary>Gets the transform shapes are drawn with.</summary>
    public Matrix4x4 Transform => transformStack[transformDepth];

    /// <summary>Initializes a context drawing into some geometry, in the default mode and without a transform.</summary>
    /// <param name="output">Where the shapes go.</param>
    public ToolRenderContext(ToolGeometry output)
    {
        ArgumentNullException.ThrowIfNull(output);

        Output = output;
        Reset();
    }

    /// <summary>Goes back to the default mode, transform and draw settings, to draw again from the start.</summary>
    internal void Reset()
    {
        modeStack[0] = ToolRenderMode.Default;
        transformStack[0] = Matrix4x4.Identity;
        modeDepth = 0;
        transformDepth = 0;
        pickTargets = null;
        pickId = 0;
        DrawColor = Color32.White;
        LineWidth = 0f;
        LayerOverride = null;
        ComputeFaceNormals = false;
        Camera = null;
    }

    // The flags shapes are drawn with right now: lines get thick when there is a line width
    private ToolRenderState State
    {
        get
        {
            var flags = ToolRenderModes.GetFlags(RenderMode);

            if ((flags & ToolRenderModeFlags.Wireframe) != 0 && LineWidth != 0f)
            {
                flags |= ToolRenderModeFlags.ThickLines;
            }

            return new ToolRenderState(flags, (flags & ToolRenderModeFlags.ThickLines) != 0 ? LineWidth : 0f, LayerOverride);
        }
    }

    /// <summary>Draws in another mode until <see cref="PopRenderMode"/>.</summary>
    /// <param name="mode">The mode, or <see cref="ToolRenderMode.Current"/> to repeat the current one.</param>
    public void PushRenderMode(ToolRenderMode mode)
    {
        if (modeDepth + 1 >= StackDepth)
        {
            return;
        }

        if (mode == ToolRenderMode.Current)
        {
            mode = RenderMode;
        }

        modeStack[++modeDepth] = mode;
    }

    /// <summary>Goes back to the mode before the last <see cref="PushRenderMode"/>.</summary>
    public void PopRenderMode()
    {
        if (modeDepth > 0)
        {
            modeDepth--;
        }
    }

    /// <summary>Draws in a space placed in the current one until <see cref="PopTransform"/>.</summary>
    /// <param name="transform">Where the space is, in the current one.</param>
    public void PushTransform(in Matrix4x4 transform) => PushTransform(transform, multiply: true);

    /// <summary>Draws in a space placed in the world, whatever the current one, until <see cref="PopTransform"/>.</summary>
    /// <param name="transform">Where the space is.</param>
    public void PushAbsoluteTransform(in Matrix4x4 transform) => PushTransform(transform, multiply: false);

    private void PushTransform(in Matrix4x4 transform, bool multiply)
    {
        if (transformDepth + 1 >= StackDepth)
        {
            return;
        }

        transformStack[transformDepth + 1] = multiply ? transform * Transform : transform;
        transformDepth++;
    }

    /// <summary>Goes back to the transform before the last push.</summary>
    public void PopTransform()
    {
        if (transformDepth > 0)
        {
            transformDepth--;
        }
    }

    /// <summary>
    /// Makes the faces and handles drawn until <see cref="EndPickObject"/> pickable as one object, as well as
    /// drawn unless the mode is <see cref="ToolRenderMode.PickProxy"/>.
    /// </summary>
    /// <param name="targets">Where what can be picked goes.</param>
    /// <param name="id">What the object is picked as.</param>
    public void BeginPickObject(ToolPickTargets targets, int id)
    {
        ArgumentNullException.ThrowIfNull(targets);

        pickTargets = targets;
        pickId = id;
    }

    /// <summary>Stops making what is drawn pickable.</summary>
    public void EndPickObject()
    {
        pickTargets = null;
    }

    // Shapes drawn in this mode are only picked
    private bool IsPickOnly => RenderMode == ToolRenderMode.PickProxy;

    private Vector3 ToWorld(Vector3 point) => Vector3.Transform(point, Transform);

    private Vector3 NormalToWorld(Vector3 normal) => Vector3.TransformNormal(normal, Transform);

    /// <summary>Draws a line in the draw color.</summary>
    public void DrawLine(Vector3 start, Vector3 end) => DrawLine(start, end, DrawColor, DrawColor);

    /// <summary>Draws a line in one color.</summary>
    public void DrawLine(Vector3 start, Vector3 end, Color32 color) => DrawLine(start, end, color, color);

    /// <summary>Draws a line fading from one color to another.</summary>
    public void DrawLine(Vector3 start, Vector3 end, Color32 startColor, Color32 endColor)
    {
        if (IsPickOnly)
        {
            return;
        }

        Output.AddLine(State, ToWorld(start), ToWorld(end), startColor, endColor, NormalToWorld(DefaultNormal));
    }

    /// <summary>Draws lines through points in the draw color, from the last back to the first when closed.</summary>
    /// <param name="points">The points.</param>
    /// <param name="closed">Whether the last point joins the first.</param>
    /// <param name="normal">The normal the lines carry.</param>
    public void DrawPolyLine(ReadOnlySpan<Vector3> points, bool closed, Vector3? normal = null)
    {
        if (points.Length < 2 || IsPickOnly)
        {
            return;
        }

        var lineNormal = NormalToWorld(normal ?? DefaultNormal);
        var state = State;

        // A closed line starts with the segment from the last point
        var previous = closed ? points.Length - 1 : 0;

        for (var i = closed ? 0 : 1; i < points.Length; i++)
        {
            Output.AddLine(state, ToWorld(points[previous]), ToWorld(points[i]), DrawColor, DrawColor, lineNormal);
            previous = i;
        }
    }

    /// <summary>Draws the twelve edges of a box in the draw color: the bottom, the top, then the sides.</summary>
    public void DrawWireBox(Vector3 mins, Vector3 maxs)
    {
        Span<Vector3> loop = [mins, new(maxs.X, mins.Y, 0f), new(maxs.X, maxs.Y, 0f), new(mins.X, maxs.Y, 0f)];

        foreach (var z in (ReadOnlySpan<float>)[mins.Z, maxs.Z])
        {
            for (var i = 0; i < 4; i++)
            {
                var start = loop[i] with { Z = z };
                var end = loop[(i + 1) % 4] with { Z = z };
                DrawLine(start, end);
            }
        }

        for (var i = 0; i < 4; i++)
        {
            DrawLine(loop[i] with { Z = mins.Z }, loop[i] with { Z = maxs.Z });
        }
    }

    // The corners of a box, the X, Y and Z bits of the index picking the maximum of each axis
    private static Vector3 GetBoxCorner(Vector3 mins, Vector3 maxs, int index)
        => new((index & 4) != 0 ? maxs.X : mins.X, (index & 2) != 0 ? maxs.Y : mins.Y, (index & 1) != 0 ? maxs.Z : mins.Z);

    // Corner indices of the faces of a box, as the box faces are drawn
    private static ReadOnlySpan<int> BoxFaceCorners =>
    [
        0, 2, 3, 1,
        0, 1, 5, 4,
        4, 5, 7, 6,
        2, 6, 7, 3,
        1, 3, 7, 5,
        0, 4, 6, 2,
    ];

    /// <summary>
    /// Draws the six faces of a box in one color, each face lit by the CPU in the flat modes. Leaves the draw
    /// color as the last face was drawn in.
    /// </summary>
    public void DrawBoxFaces(Vector3 mins, Vector3 maxs, Color32 color)
    {
        var flatShaded = RenderMode is ToolRenderMode.Flat or ToolRenderMode.FlatNoZ;
        var faces = BoxFaceCorners;

        for (var i = 0; i < faces.Length; i += 4)
        {
            var a = GetBoxCorner(mins, maxs, faces[i]);
            var b = GetBoxCorner(mins, maxs, faces[i + 1]);
            var c = GetBoxCorner(mins, maxs, faces[i + 2]);
            var d = GetBoxCorner(mins, maxs, faces[i + 3]);

            var faceColor = color;

            if (flatShaded)
            {
                faceColor = ShadeFlat(color, GetShadeNormal(a, b, d));
            }

            DrawColor = faceColor;

            AddTriangle(a, c, b, faceColor, faceColor, faceColor, DefaultNormal);
            AddTriangle(a, d, c, faceColor, faceColor, faceColor, DefaultNormal);
        }
    }

    /// <summary>Draws the outline of each face of a box in the draw color, so every edge is drawn twice.</summary>
    public void DrawBoxFaceOutlines(Vector3 mins, Vector3 maxs)
    {
        var faces = BoxFaceCorners;
        Span<Vector3> corners = stackalloc Vector3[4];

        for (var i = 0; i < faces.Length; i += 4)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                corners[corner] = GetBoxCorner(mins, maxs, faces[i + corner]);
            }

            DrawPolyLine(corners, closed: true);
        }
    }

    /// <summary>Draws a box as solid faces in the draw color, each with its own normal, seen from inside too when double sided.</summary>
    public void DrawSolidBox(Vector3 mins, Vector3 maxs, bool doubleSided = false)
    {
        // Per face: its four corners, then its normal
        ReadOnlySpan<(int A, int B, int C, int D, Vector3 Normal)> faces =
        [
            (4, 6, 7, 5, Vector3.UnitX),
            (0, 1, 3, 2, -Vector3.UnitX),
            (2, 3, 7, 6, Vector3.UnitY),
            (0, 4, 5, 1, -Vector3.UnitY),
            (1, 5, 7, 3, Vector3.UnitZ),
            (0, 2, 6, 4, -Vector3.UnitZ),
        ];

        foreach (var (ia, ib, ic, id, normal) in faces)
        {
            var a = GetBoxCorner(mins, maxs, ia);
            var b = GetBoxCorner(mins, maxs, ib);
            var c = GetBoxCorner(mins, maxs, ic);
            var d = GetBoxCorner(mins, maxs, id);

            AddTriangle(a, b, c, DrawColor, DrawColor, DrawColor, normal);
            AddTriangle(a, c, d, DrawColor, DrawColor, DrawColor, normal);

            if (doubleSided)
            {
                AddTriangle(a, c, b, DrawColor, DrawColor, DrawColor, normal);
                AddTriangle(a, d, c, DrawColor, DrawColor, DrawColor, normal);
            }
        }
    }

    /// <summary>Draws a triangle in one color.</summary>
    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 color) => DrawTriangle(a, b, c, color, color, color);

    /// <summary>Draws a triangle with a color at each corner.</summary>
    public void DrawTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 colorA, Color32 colorB, Color32 colorC)
    {
        var normal = ComputeFaceNormals ? MathUtils.SafeNormalize(MathUtils.TriangleCross(a, b, c)) : DefaultNormal;

        AddTriangle(a, b, c, colorA, colorB, colorC, normal);
    }

    /// <summary>Draws a quad as two triangles in one color, from both sides when double sided.</summary>
    public void DrawQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color, bool doubleSided = false)
    {
        DrawTriangle(a, b, c, color);
        DrawTriangle(a, c, d, color);

        if (doubleSided)
        {
            DrawTriangle(c, b, a, color);
            DrawTriangle(d, c, a, color);
        }
    }

    /// <summary>Draws the four edges of a quad in one color.</summary>
    public void DrawQuadOutline(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 color)
    {
        DrawLine(a, b, color);
        DrawLine(b, c, color);
        DrawLine(c, d, color);
        DrawLine(d, a, color);
    }

    /// <summary>
    /// Draws a circle in the draw color, through the tip of <paramref name="axisA"/> reversed and on around
    /// towards <paramref name="axisB"/>.
    /// </summary>
    /// <param name="center">The center.</param>
    /// <param name="axisA">One axis of the plane of the circle.</param>
    /// <param name="axisB">The other axis of the plane.</param>
    /// <param name="radius">The radius, the axes being scaled by it.</param>
    /// <param name="segments">How many lines the circle is drawn with.</param>
    public void DrawCircle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, int segments)
    {
        Vector3 GetPoint(float angle)
        {
            var (sin, cos) = MathF.SinCos(angle);
            return center + (-cos * axisA + sin * axisB) * radius;
        }

        var previous = GetPoint(0f);

        for (var i = 1; i <= segments; i++)
        {
            var point = GetPoint(i / (float)segments * MathF.PI * 2f);
            DrawLine(previous, point);
            previous = point;
        }
    }

    /// <summary>
    /// Draws an arc from one angle to another around a center, as lines with both ends joined to the center, or
    /// as the faces of the slice it bounds.
    /// </summary>
    /// <param name="center">The center.</param>
    /// <param name="axisX">The axis at zero degrees.</param>
    /// <param name="axisY">The axis at ninety degrees.</param>
    /// <param name="radius">The radius, the axes being scaled by it.</param>
    /// <param name="segments">How many lines or faces the arc is drawn with.</param>
    /// <param name="startDegrees">The angle the arc starts at.</param>
    /// <param name="endDegrees">The angle the arc ends at.</param>
    /// <param name="color">The color.</param>
    /// <param name="wireframe">Whether the arc is drawn as lines rather than faces.</param>
    /// <param name="sides">Which sides of the faces are drawn.</param>
    public void DrawArcEx(Vector3 center, Vector3 axisX, Vector3 axisY, float radius, int segments, float startDegrees, float endDegrees, Color32 color, bool wireframe, ToolFaceSides sides)
    {
        segments = Math.Max(segments, 1);

        var angle = float.DegreesToRadians(startDegrees);
        var step = float.DegreesToRadians((endDegrees - startDegrees) / segments);

        Vector3 GetPoint(float angle)
        {
            var (sin, cos) = MathF.SinCos(angle);
            return center + (axisX * cos + axisY * sin) * radius;
        }

        var first = GetPoint(angle);
        var previous = first;

        for (var i = 0; i < segments; i++)
        {
            angle += step;
            var point = GetPoint(angle);

            if (wireframe)
            {
                DrawLine(point, previous, color);
            }
            else
            {
                AddFaces(center, point, previous, color, color, color, sides);
            }

            previous = point;
        }

        if (wireframe)
        {
            DrawLine(center, first, color);
            DrawLine(center, previous, color);
        }
    }

    /// <summary>
    /// Draws the side of a cylinder as faces, and the end caps when capped. When shaded, each face is lit by
    /// the CPU and blends into the next, and each cap is lit as one face; otherwise everything is in the color.
    /// </summary>
    /// <param name="start">The center of one end.</param>
    /// <param name="end">The center of the other end.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="segments">How many sides the cylinder has.</param>
    /// <param name="capped">Whether the ends are closed.</param>
    /// <param name="color">The color.</param>
    /// <param name="shaded">Whether the faces are lit by the CPU.</param>
    /// <param name="sides">Which sides of the faces are drawn.</param>
    public void DrawSolidCylinderPart(Vector3 start, Vector3 end, float radius, int segments, bool capped, Color32 color, bool shaded, ToolFaceSides sides)
    {
        if (segments < 3)
        {
            return;
        }

        var axis = MathUtils.SafeNormalize(end - start);
        var (right, up) = GetVectorVectors(axis);

        // The rim around the start, then the one around the end
        Span<Vector3> rims = segments <= 32 ? stackalloc Vector3[segments * 2] : new Vector3[segments * 2];
        var startRim = rims[..segments];
        var endRim = rims[segments..];

        // The angle is stepped rather than multiplied out, which puts the rim where the editor puts it
        var angle = 0f;
        var step = MathF.Tau / segments;

        for (var i = 0; i < segments; i++)
        {
            var (sin, cos) = MathF.SinCos(angle);
            var offset = (up * cos + right * sin) * radius;

            startRim[i] = start + offset;
            endRim[i] = end + offset;
            angle += step;
        }

        // Each face blends from the light of the one before it, the first from the light of the last
        var previousShade = ShadeFlat(color, GetShadeNormal(startRim[^1], endRim[^1], startRim[0]));

        for (var i = 0; i < segments; i++)
        {
            var next = (i + 1) % segments;
            var shade = ShadeFlat(color, GetShadeNormal(startRim[i], endRim[i], startRim[next]));

            var (a, b, c) = shaded ? (previousShade, previousShade, shade) : (color, color, color);
            AddFaces(startRim[i], endRim[i], startRim[next], a, b, c, sides);

            (a, b, c) = shaded ? (previousShade, shade, shade) : (color, color, color);
            AddFaces(endRim[i], endRim[next], startRim[next], a, b, c, sides);

            previousShade = shade;
        }

        if (!capped)
        {
            return;
        }

        var startCap = shaded ? ShadeFlat(color, -axis) : color;
        var endCap = shaded ? ShadeFlat(color, axis) : color;

        for (var i = 1; i < segments - 1; i++)
        {
            AddFaces(startRim[0], startRim[i], startRim[i + 1], startCap, startCap, startCap, sides);
            AddFaces(endRim[i + 1], endRim[i], endRim[0], endCap, endCap, endCap, sides);
        }
    }

    /// <summary>
    /// Draws a capsule as faces in one color: a half sphere around each end, the start one first, and a band
    /// joining their rims. Every vertex has its normal on the sphere it lies on, which the shaded modes light
    /// it by.
    /// </summary>
    /// <param name="start">The center of the sphere at one end.</param>
    /// <param name="end">The center of the sphere at the other end.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="segments">How many sides the capsule has around, from 4 to 256. Each half sphere has a quarter as many rings.</param>
    /// <param name="color">The color.</param>
    public void DrawSolidCapsule(Vector3 start, Vector3 end, float radius, int segments, Color32 color)
    {
        var sides = Math.Clamp(segments, 4, 256);
        var rings = sides / 4;
        var axis = end - start;
        var length = axis.Length();

        // The capsule is built along +Z from the origin, then turned onto the axis and moved to the start
        var frame = GetCapsuleFrame(axis, length, start);
        var startCap = BuildCapsuleCap(Vector3.Zero, radius, sides, rings, towardsMinusZ: true, frame);
        var endCap = BuildCapsuleCap(new Vector3(0f, 0f, length), radius, sides, rings, towardsMinusZ: false, frame);

        // Index of a vertex of a cap, the rings counted from one at the pole
        int Ring(int ring, int side) => 1 + (ring - 1) * sides + side;

        void Add((Vector3 Position, Vector3 Normal) a, (Vector3 Position, Vector3 Normal) b, (Vector3 Position, Vector3 Normal) c)
            => AddTriangle(a.Position, b.Position, c.Position, color, color, color, a.Normal, b.Normal, c.Normal);

        // Each side goes from the start pole to the end pole, then across the band, with the side before it
        for (var side = 0; side < sides; side++)
        {
            var previous = (side + sides - 1) % sides;

            Add(startCap[0], startCap[Ring(1, side)], startCap[Ring(1, previous)]);

            for (var ring = 2; ring <= rings; ring++)
            {
                Add(startCap[Ring(ring - 1, previous)], startCap[Ring(ring - 1, side)], startCap[Ring(ring, side)]);
                Add(startCap[Ring(ring - 1, previous)], startCap[Ring(ring, side)], startCap[Ring(ring, previous)]);
            }

            Add(endCap[Ring(1, previous)], endCap[Ring(1, side)], endCap[0]);

            for (var ring = 2; ring <= rings; ring++)
            {
                Add(endCap[Ring(ring, side)], endCap[Ring(ring - 1, side)], endCap[Ring(ring - 1, previous)]);
                Add(endCap[Ring(ring, previous)], endCap[Ring(ring, side)], endCap[Ring(ring - 1, previous)]);
            }

            Add(startCap[Ring(rings, previous)], startCap[Ring(rings, side)], endCap[Ring(rings, side)]);
            Add(startCap[Ring(rings, previous)], endCap[Ring(rings, side)], endCap[Ring(rings, previous)]);
        }
    }

    // Local X goes to the frame's down, Y to its left and Z along the axis. An axis too short to have a
    // direction leaves the capsule along +Z, only moved to the start.
    private static Matrix4x4 GetCapsuleFrame(Vector3 axis, float length, Vector3 origin)
    {
        if (length <= 1.1920929e-7f)
        {
            return Matrix4x4.CreateTranslation(origin);
        }

        var forward = MathUtils.SafeNormalize(axis);
        Vector3 left;
        Vector3 up;

        // Left is level with the ground, except along a vertical axis where it is +Y
        if (MathF.Abs(forward.X) >= 0.001f || MathF.Abs(forward.Y) >= 0.001f)
        {
            left = MathUtils.SafeNormalize(new Vector3(-forward.Y, forward.X, 0f));
            up = MathUtils.SafeNormalize(Vector3.Cross(forward, left));
        }
        else
        {
            left = Vector3.UnitY;
            up = new Vector3(-forward.Z, 0f, 0f);
        }

        return new Matrix4x4(
            -up.X, -up.Y, -up.Z, 0f,
            left.X, left.Y, left.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            origin.X, origin.Y, origin.Z, 1f);
    }

    // Half a sphere around Z, in the frame: its pole first, then each ring around from +X, out to the rim
    // at the middle of the sphere. The polar fraction runs from 0 at +Z to 1 at -Z.
    private static (Vector3 Position, Vector3 Normal)[] BuildCapsuleCap(Vector3 center, float radius, int sides, int rings, bool towardsMinusZ, in Matrix4x4 frame)
    {
        var vertices = new (Vector3 Position, Vector3 Normal)[1 + rings * sides];
        var transform = frame;

        (Vector3, Vector3) GetVertex(float around, float polar)
        {
            var (sinTheta, cosTheta) = MathF.SinCos((around + around) * MathF.PI);
            var (sinPhi, cosPhi) = MathF.SinCos(polar * MathF.PI);
            var normal = new Vector3(sinPhi * cosTheta, sinPhi * sinTheta, cosPhi);

            return (Vector3.Transform(center + normal * radius, transform), Vector3.TransformNormal(normal, transform));
        }

        vertices[0] = GetVertex(0f, towardsMinusZ ? 1f : 0f);

        for (var ring = 1; ring <= rings; ring++)
        {
            var polar = ring / (float)rings * 0.5f;

            if (towardsMinusZ)
            {
                polar = 1f - polar;
            }

            for (var side = 0; side < sides; side++)
            {
                vertices[1 + (ring - 1) * sides + side] = GetVertex(side / (float)sides, polar);
            }
        }

        return vertices;
    }

    /// <summary>Writes text at a point, facing the screen and over everything else.</summary>
    public void DrawText(string text, Vector3 position, Color32 color)
    {
        Output.AddLabel(new ToolLabel(ToWorld(position), text, color));
    }

    /// <summary>Draws a handle, a marker that keeps its size on the screen, at a point.</summary>
    /// <param name="size">Half its width, in pixels.</param>
    /// <param name="type">Its shape.</param>
    /// <param name="color">Its color.</param>
    /// <param name="position">The point.</param>
    /// <param name="noZ">Whether it is drawn over the scene.</param>
    /// <param name="border">Whether it is edged in black.</param>
    /// <param name="translucent">Whether it is drawn after the opaque shapes and keeps the faint parts of its shape.</param>
    public void DrawHandle(float size, ToolHandleType type, Color32 color, Vector3 position, bool noZ, bool border, bool translucent)
    {
        if (type == ToolHandleType.Generic)
        {
            return;
        }

        var worldPosition = ToWorld(position);
        pickTargets?.AddHandle(worldPosition, size, pickId);

        if (!IsPickOnly)
        {
            Output.AddHandle(new ToolHandleState(type, noZ, border, translucent, LayerOverride), worldPosition, size, color);
        }
    }

    /// <summary>
    /// Draws the part of a sphere around Z between two angles from its +Z pole as a grid of lines: along the
    /// meridians between rows, and along each row but the last.
    /// </summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="slices">How many meridians.</param>
    /// <param name="stacks">How many rows, the first at <paramref name="thetaStart"/> and the last at <paramref name="thetaEnd"/>.</param>
    /// <param name="thetaStart">Angle of the first row from +Z, in radians.</param>
    /// <param name="thetaEnd">Angle of the last row from +Z, in radians.</param>
    /// <param name="color">The color, or the draw color.</param>
    public void DrawWireSphere(Vector3 center, float radius, int slices, int stacks, float thetaStart, float thetaEnd, Color32? color = null)
    {
        var lineColor = color ?? DrawColor;

        Vector3 GetPoint(int stack, int slice)
        {
            var (sinTheta, cosTheta) = MathF.SinCos(stack / (float)(stacks - 1) * (thetaEnd - thetaStart) + thetaStart);
            var (sinPhi, cosPhi) = MathF.SinCos(slice / (float)slices * MathF.Tau);

            return center + new Vector3(cosPhi * sinTheta, sinPhi * sinTheta, cosTheta) * radius;
        }

        for (var stack = 0; stack < stacks - 1; stack++)
        {
            for (var slice = 0; slice < slices; slice++)
            {
                var point = GetPoint(stack, slice);

                DrawLine(point, GetPoint(stack + 1, slice), lineColor);
                DrawLine(point, GetPoint(stack, slice + 1), lineColor);
            }
        }
    }

    /// <summary>Draws a whole sphere as a grid of lines.</summary>
    public void DrawWireSphere(Vector3 center, float radius, int slices, int stacks, Color32? color = null)
        => DrawWireSphere(center, radius, slices, stacks, 0f, MathF.PI, color);

    /// <summary>Draws three circles around a center, in the XY, XZ and YZ planes in that order, in the draw color.</summary>
    public void DrawSphereCircles(Vector3 center, float radius, int segments, Color32? color = null)
    {
        var lineColor = color ?? DrawColor;

        ReadOnlySpan<(Vector3 Cos, Vector3 Sin)> planes = [(Vector3.UnitX, Vector3.UnitY), (Vector3.UnitX, Vector3.UnitZ), (Vector3.UnitY, Vector3.UnitZ)];

        foreach (var (cosAxis, sinAxis) in planes)
        {
            var previous = center + cosAxis * radius;

            for (var i = 1; i <= segments; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / segments);
                var point = center + (cosAxis * cos + sinAxis * sin) * radius;

                DrawLine(previous, point, lineColor);
                previous = point;
            }
        }
    }

    /// <summary>
    /// Draws a cone as faces from a disc around its base to its tip, the base in one color and the tip in
    /// another, with the disc itself lit by the CPU when it is capped.
    /// </summary>
    /// <param name="baseCenter">The center of the base.</param>
    /// <param name="tip">The tip.</param>
    /// <param name="radius">The radius of the base.</param>
    /// <param name="segments">How many sides the cone has.</param>
    /// <param name="baseColor">The color at the base, and of the cap.</param>
    /// <param name="tipColor">The color at the tip.</param>
    /// <param name="capped">Whether the base is closed.</param>
    /// <param name="sides">Which sides of the faces are drawn.</param>
    public void DrawSolidCone(Vector3 baseCenter, Vector3 tip, float radius, int segments, Color32 baseColor, Color32 tipColor, bool capped = true, ToolFaceSides sides = ToolFaceSides.Front)
    {
        if (segments < 3)
        {
            return;
        }

        var (right, up) = GetVectorVectors(MathUtils.SafeNormalize(tip - baseCenter));
        Span<Vector3> rim = segments <= 64 ? stackalloc Vector3[segments] : new Vector3[segments];

        var angle = 0f;
        var step = MathF.Tau / segments;

        for (var i = 0; i < segments; i++)
        {
            var (sin, cos) = MathF.SinCos(angle);
            rim[i] = baseCenter + (up * cos + right * sin) * radius;
            angle += step;
        }

        for (var i = 0; i < segments; i++)
        {
            AddFaces(rim[i], tip, rim[(i + 1) % segments], baseColor, tipColor, baseColor, sides);
        }

        if (!capped)
        {
            return;
        }

        var capColor = ShadeFlat(baseColor, GetShadeNormal(rim[0], rim[1], rim[2]));

        for (var i = 1; i < segments - 1; i++)
        {
            AddFaces(rim[0], rim[i], rim[i + 1], capColor, capColor, capColor, sides);
        }
    }

    /// <summary>
    /// Draws a line as a quad of some width in one color, turned around the line to face the <see cref="Camera"/>.
    /// </summary>
    /// <param name="start">Where the line starts.</param>
    /// <param name="end">Where the line ends.</param>
    /// <param name="width">The width of the quad.</param>
    /// <param name="color">The color.</param>
    /// <param name="doubleSided">Whether the quad is drawn from both sides.</param>
    public void DrawScreenAlignedLine(Vector3 start, Vector3 end, float width, Color32 color, bool doubleSided = false)
    {
        var direction = MathUtils.SafeNormalize(end - start);
        var toMiddle = (start + end) * 0.5f - GetCameraPosition();
        var side = MathUtils.SafeNormalize(Vector3.Cross(toMiddle, direction)) * (width * 0.5f);

        DrawQuad(start + side, end + side, end - side, start - side, color, doubleSided);
    }

    /// <summary>
    /// Draws a square of some size in one color around a point, in the plane of the screen of the <see cref="Camera"/>.
    /// </summary>
    /// <param name="center">The center of the square.</param>
    /// <param name="width">How wide the square is.</param>
    /// <param name="height">How high the square is.</param>
    /// <param name="color">The color.</param>
    public void DrawScreenAlignedRect(Vector3 center, float width, float height, Color32 color)
    {
        var camera = Camera ?? throw new InvalidOperationException("Shapes that face the camera need a camera.");
        var up = MathUtils.SafeNormalize(Vector3.TransformNormal(camera.Up, GetWorldToLocal())) * (height * 0.5f);
        var left = MathUtils.SafeNormalize(Vector3.TransformNormal(-camera.Right, GetWorldToLocal())) * (width * 0.5f);

        DrawQuad(center + left + up, center + left - up, center - left - up, center - left + up, color, doubleSided: true);
    }

    /// <summary>
    /// Draws a rectangle in the plane of two axes, spanning from one distance to another along each, filled from
    /// both sides or as its outline.
    /// </summary>
    /// <param name="origin">Where the distances are measured from.</param>
    /// <param name="axisA">The first axis.</param>
    /// <param name="axisB">The second axis.</param>
    /// <param name="minA">Where the rectangle starts along the first axis.</param>
    /// <param name="maxA">Where the rectangle ends along the first axis.</param>
    /// <param name="minB">Where the rectangle starts along the second axis.</param>
    /// <param name="maxB">Where the rectangle ends along the second axis.</param>
    /// <param name="color">The color.</param>
    /// <param name="outline">Whether only the edges are drawn.</param>
    public void DrawOrientedRect(Vector3 origin, Vector3 axisA, Vector3 axisB, float minA, float maxA, float minB, float maxB, Color32 color, bool outline)
    {
        var a = origin + axisA * minA + axisB * minB;
        var b = origin + axisA * minA + axisB * maxB;
        var c = origin + axisA * maxA + axisB * maxB;
        var d = origin + axisA * maxA + axisB * minB;

        if (outline)
        {
            DrawQuadOutline(a, b, c, d, color);
            return;
        }

        DrawQuad(d, c, b, a, color, doubleSided: true);
    }

    /// <summary>
    /// Draws a ring as a band of some width in one color, between two angles of a circle in the plane of two axes.
    /// The band is turned around the circle to face the <see cref="Camera"/>, so it keeps its width on the screen.
    /// </summary>
    /// <param name="center">The center of the circle.</param>
    /// <param name="axisX">The axis at zero degrees.</param>
    /// <param name="axisY">The axis at ninety degrees.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="width">The width of the band.</param>
    /// <param name="segments">How many quads the band is drawn with, at least three.</param>
    /// <param name="startDegrees">The angle the band starts at.</param>
    /// <param name="endDegrees">The angle the band ends at, the whole circle when a turn or more from the start.</param>
    /// <param name="color">The color.</param>
    public void DrawSolidRingArc(Vector3 center, Vector3 axisX, Vector3 axisY, float radius, float width, int segments, float startDegrees, float endDegrees, Color32 color)
    {
        if (segments < 3)
        {
            return;
        }

        if (MathF.Abs(startDegrees - endDegrees) >= 360f)
        {
            (startDegrees, endDegrees) = (0f, 360f);
        }

        Vector3 GetPoint(float degrees)
        {
            var (sin, cos) = MathF.SinCos(float.DegreesToRadians(degrees));
            return center + (axisX * cos + axisY * sin) * radius;
        }

        var step = (endDegrees - startDegrees) / segments;
        var toCenter = center - GetCameraPosition();
        var previous = GetPoint(startDegrees);
        var previousInner = previous;
        var previousOuter = previous;

        // Each point is widened across the segment that ends at it, so the first one, which has no segment, is not
        for (var i = 0; i <= segments; i++)
        {
            var point = GetPoint(startDegrees + step * i);
            var offset = MathUtils.SafeNormalize(Vector3.Cross(toCenter, point - previous)) * (width * 0.5f);
            var (inner, outer) = (point - offset, point + offset);

            if (i > 0)
            {
                AddFaces(previousInner, previousOuter, outer, color, color, color, ToolFaceSides.Both);
                AddFaces(previousInner, outer, inner, color, color, color, ToolFaceSides.Both);
            }

            (previous, previousInner, previousOuter) = (point, inner, outer);
        }
    }

    /// <summary>Draws a whole ring around a normal, as <see cref="DrawSolidRingArc"/> does.</summary>
    /// <param name="center">The center of the circle.</param>
    /// <param name="normal">The normal of the plane of the circle.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="width">The width of the band.</param>
    /// <param name="segments">How many quads the band is drawn with, at least three.</param>
    /// <param name="color">The color.</param>
    public void DrawSolidRing(Vector3 center, Vector3 normal, float radius, float width, int segments, Color32 color)
    {
        var (right, up) = GetVectorVectors(MathUtils.SafeNormalize(normal));

        DrawSolidRingArc(center, right, up, radius, width, segments, 0f, 360f, color);
    }

    /// <summary>
    /// Draws a filled slice of a circle around a center, from the direction of one point to the direction of another,
    /// as far out as the first point.
    /// </summary>
    /// <param name="center">The center.</param>
    /// <param name="start">The point the slice starts towards.</param>
    /// <param name="end">The point the slice ends towards.</param>
    /// <param name="segments">How many faces the slice is drawn with.</param>
    /// <param name="color">The color.</param>
    /// <param name="sides">Which sides of the faces are drawn.</param>
    public void DrawAngleArc(Vector3 center, Vector3 start, Vector3 end, int segments, Color32 color, ToolFaceSides sides)
    {
        var radius = Vector3.Distance(start, center);
        var from = MathUtils.SafeNormalize(start - center);
        var to = MathUtils.SafeNormalize(end - center);
        var degrees = float.RadiansToDegrees(MathUtils.SafeAcos(Vector3.Dot(from, to)));
        var normal = MathUtils.SafeNormalize(Vector3.Cross(to, from));

        // From the end around towards the start
        DrawArcEx(center, to, MathUtils.SafeNormalize(Vector3.Cross(normal, to)), radius, segments, 0f, degrees, color, wireframe: false, sides);
    }

    /// <summary>Draws a sphere as faces in one color, as a grid of rows from its +Z pole to its -Z pole.</summary>
    /// <param name="center">The center.</param>
    /// <param name="radius">The radius.</param>
    /// <param name="slices">How many points each row has around, the last one closing it.</param>
    /// <param name="stacks">How many rows there are, the first and last at the poles.</param>
    /// <param name="color">The color.</param>
    public void DrawSolidSphere(Vector3 center, float radius, int slices, int stacks, Color32 color)
    {
        if (slices < 2 || stacks < 2)
        {
            return;
        }

        Vector3 GetPoint(int stack, int slice)
        {
            var (sinTheta, cosTheta) = MathF.SinCos(stack / (float)(stacks - 1) * MathF.PI);
            var (sinPhi, cosPhi) = MathF.SinCos(slice / (float)(slices - 1) * MathF.Tau);

            return center + new Vector3(cosPhi * sinTheta, sinPhi * sinTheta, cosTheta) * radius;
        }

        for (var stack = 0; stack < stacks - 1; stack++)
        {
            for (var slice = 0; slice < slices - 1; slice++)
            {
                var a = GetPoint(stack, slice);
                var b = GetPoint(stack + 1, slice);
                var c = GetPoint(stack + 1, slice + 1);
                var d = GetPoint(stack, slice + 1);

                AddFaces(a, b, c, color, color, color, ToolFaceSides.Front);
                AddFaces(a, c, d, color, color, color, ToolFaceSides.Front);
            }
        }
    }

    // The camera, in the space the shapes are drawn in
    private Vector3 GetCameraPosition()
    {
        var camera = Camera ?? throw new InvalidOperationException("Shapes that face the camera need a camera.");
        return Vector3.Transform(camera.Location, GetWorldToLocal());
    }

    private Matrix4x4 GetWorldToLocal() => Matrix4x4.Invert(Transform, out var inverse) ? inverse : Matrix4x4.Identity;

    // Two axes across a normalized direction, both normalized: the first across it and the X axis, or the Y
    // axis when the direction is along X, and the second across that and the direction
    private static (Vector3 Right, Vector3 Up) GetVectorVectors(Vector3 forward)
    {
        var reference = MathF.Abs(forward.X) > 0.999f ? Vector3.UnitY : Vector3.UnitX;
        var up = Vector3.Cross(forward, reference);
        var right = Vector3.Cross(up, forward);

        return (MathUtils.SafeNormalize(right), MathUtils.SafeNormalize(up));
    }

    // The normal the CPU lights a face by, which faces away from the side its corners turn clockwise on
    private static Vector3 GetShadeNormal(Vector3 a, Vector3 b, Vector3 c) => MathUtils.SafeNormalize(Vector3.Cross(c - a, b - a));

    private void AddFaces(Vector3 a, Vector3 b, Vector3 c, Color32 colorA, Color32 colorB, Color32 colorC, ToolFaceSides sides)
    {
        if (sides != ToolFaceSides.Back)
        {
            AddTriangle(a, b, c, colorA, colorB, colorC, DefaultNormal);
        }

        if (sides != ToolFaceSides.Front)
        {
            AddTriangle(c, b, a, colorC, colorB, colorA, DefaultNormal);
        }
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 colorA, Color32 colorB, Color32 colorC, Vector3 normalA, Vector3 normalB, Vector3 normalC)
    {
        var (worldA, worldB, worldC) = (ToWorld(a), ToWorld(b), ToWorld(c));
        pickTargets?.AddTriangle(worldA, worldB, worldC, pickId);

        if (!IsPickOnly)
        {
            Output.AddTriangle(State, worldA, worldB, worldC, colorA, colorB, colorC, NormalToWorld(normalA), NormalToWorld(normalB), NormalToWorld(normalC));
        }
    }

    private void AddTriangle(Vector3 a, Vector3 b, Vector3 c, Color32 colorA, Color32 colorB, Color32 colorC, Vector3 normal)
    {
        AddTriangle(a, b, c, colorA, colorB, colorC, normal, normal, normal);
    }

    /// <summary>A color lit by the CPU the way the flat modes light faces, its alpha kept.</summary>
    internal static Color32 ShadeFlat(Color32 color, Vector3 normal)
    {
        var factor = Vector3.Dot(normal, FlatShadeLightDirection) * 0.35f + 0.65f;

        return new Color32((byte)(int)(color.R * factor), (byte)(int)(color.G * factor), (byte)(int)(color.B * factor), color.A);
    }
}
