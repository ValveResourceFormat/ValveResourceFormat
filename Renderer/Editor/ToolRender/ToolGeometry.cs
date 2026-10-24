using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Buffers;

namespace ValveResourceFormat.Renderer.Editor.ToolRender;

/// <summary>
/// A vertex of the tool shapes. A line keeps where it starts, which its dashes run from, and a thick line
/// keeps both its ends and which corner of the quad the vertex is.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal record struct ToolVertex(
    [field: VertexAttribute(VertexSlot.Position)] Vector3 Position,
    [field: VertexAttribute(VertexSlot.Normal)] Vector3 Normal,
    [field: VertexAttribute(VertexSlot.Color)] Color32 Color,
    [field: VertexAttribute("vLineStart")] Vector3 LineStart,
    [field: VertexAttribute("vLineEnd")] Vector3 LineEnd,
    [field: VertexAttribute("vLineCorner")] float LineCorner)
{
    public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<ToolVertex>();
}

/// <summary>A corner of the quad a handle is drawn as, around the point it marks.</summary>
[StructLayout(LayoutKind.Sequential)]
internal record struct ToolHandleVertex(
    [field: VertexAttribute(VertexSlot.Position)] Vector3 Position,
    [field: VertexAttribute(VertexSlot.Color)] Color32 Color,
    [field: VertexAttribute("vHandleCorner")] Vector2 Corner,
    [field: VertexAttribute("vHandleSize")] float Size)
{
    public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<ToolHandleVertex>();
}

/// <summary>
/// What the shapes of one batch are drawn with: the flags of their mode, the width of their lines, and the layer
/// they are drawn in when it is not the one of their mode.
/// </summary>
internal readonly record struct ToolRenderState(ToolRenderModeFlags Flags, float LineWidth, ToolRenderLayer? LayerOverride = null)
{
    public ToolRenderLayer Layer => LayerOverride ?? ToolRenderModes.GetLayer(Flags);

    public bool IsThick => (Flags & ToolRenderModeFlags.ThickLines) != 0;
}

/// <summary>What the handles of one batch are drawn with.</summary>
internal readonly record struct ToolHandleState(ToolHandleType Type, bool NoZ, bool Border, bool Translucent, ToolRenderLayer? LayerOverride = null)
{
    public ToolRenderLayer Layer => LayerOverride ?? (NoZ
        ? Translucent ? ToolRenderLayer.TranslucentNoZ : ToolRenderLayer.OpaqueNoZ
        : Translucent ? ToolRenderLayer.Translucent : ToolRenderLayer.Opaque);
}

/// <summary>Text written at a point, facing the screen.</summary>
/// <param name="Position">The point, in the space of the shapes.</param>
/// <param name="Text">The text.</param>
/// <param name="Color">The color of the text.</param>
public readonly record struct ToolLabel(Vector3 Position, string Text, Color32 Color);

/// <summary>
/// The shapes a <see cref="ToolRenderContext"/> draws, batched by what they are drawn with, and the GL objects
/// they are drawn from once uploaded.
/// </summary>
public sealed class ToolGeometry
{
    private sealed class Batch
    {
        // Lines as two vertices each, which also pick thick lines
        public List<ToolVertex> Lines { get; } = [];

        // Thick lines as two triangles each
        public List<ToolVertex> ThickLines { get; } = [];

        public List<ToolVertex> Triangles { get; } = [];

        public int LinesStart { get; set; }
        public int ThickLinesStart { get; set; }
        public int TrianglesStart { get; set; }
    }

    private sealed class HandleBatch
    {
        // Two triangles to a handle
        public List<ToolHandleVertex> Vertices { get; } = [];

        public int Start { get; set; }
    }

    // The corners of the two triangles of a handle
    private static readonly Vector2[] HandleCorners = [new(-1f, 1f), new(-1f, -1f), new(1f, 1f), new(-1f, -1f), new(1f, 1f), new(1f, -1f)];

    private readonly Dictionary<ToolRenderState, Batch> batches = [];
    private readonly Dictionary<ToolHandleState, HandleBatch> handleBatches = [];
    private readonly List<ToolLabel> labels = [];
    private readonly List<ToolVertex> uploadVertices = [];
    private readonly List<ToolHandleVertex> uploadHandleVertices = [];

    private StreamingVertexBuffer? vertexBuffer;
    private StreamingVertexBuffer? handleVertexBuffer;
    private int vao;
    private int handleVao;
    private bool isDirty;
    private bool hasBounds;

    /// <summary>Gets how many lines, triangles, handles and labels have been drawn.</summary>
    public int ShapeCount { get; private set; }

    /// <summary>Gets whether nothing has been drawn.</summary>
    public bool IsEmpty => ShapeCount == 0;

    /// <summary>Gets the bounds of everything drawn.</summary>
    public AABB Bounds { get; private set; }

    /// <summary>Gets the text written among the shapes, which is drawn with the other text of the view.</summary>
    public IReadOnlyList<ToolLabel> Labels => labels;

    internal IEnumerable<ToolRenderState> States => batches.Keys;

    private Batch GetBatch(ToolRenderState state)
    {
        if (!batches.TryGetValue(state, out var batch))
        {
            batches[state] = batch = new Batch();
        }

        isDirty = true;
        return batch;
    }

    private void Extend(Vector3 point)
    {
        Bounds = hasBounds ? Bounds.Union(new AABB(point, point)) : new AABB(point, point);
        hasBounds = true;
    }

    internal void AddLine(ToolRenderState state, Vector3 start, Vector3 end, Color32 startColor, Color32 endColor, Vector3 normal)
    {
        var batch = GetBatch(state);
        ShapeCount++;

        batch.Lines.Add(new ToolVertex(start, normal, startColor, start, end, 0f));
        batch.Lines.Add(new ToolVertex(end, normal, endColor, start, end, 1f));

        if (state.IsThick)
        {
            // Two triangles across the line, as the strip start, end, start, end with the sides swapped
            var corner0 = new ToolVertex(start, normal, startColor, start, end, 0f);
            var corner1 = new ToolVertex(end, normal, endColor, start, end, 1f);
            var corner2 = new ToolVertex(start, normal, startColor, start, end, 2f);
            var corner3 = new ToolVertex(end, normal, endColor, start, end, 3f);

            batch.ThickLines.AddRange([corner0, corner1, corner2, corner1, corner2, corner3]);
        }

        Extend(start);
        Extend(end);
    }

    internal void AddTriangle(ToolRenderState state, Vector3 a, Vector3 b, Vector3 c, Color32 colorA, Color32 colorB, Color32 colorC, Vector3 normalA, Vector3 normalB, Vector3 normalC)
    {
        var batch = GetBatch(state);
        ShapeCount++;

        batch.Triangles.Add(new ToolVertex(a, normalA, colorA, a, a, 0f));
        batch.Triangles.Add(new ToolVertex(b, normalB, colorB, b, b, 0f));
        batch.Triangles.Add(new ToolVertex(c, normalC, colorC, c, c, 0f));

        Extend(a);
        Extend(b);
        Extend(c);
    }

    internal void AddHandle(ToolHandleState state, Vector3 position, float size, Color32 color)
    {
        if (!handleBatches.TryGetValue(state, out var batch))
        {
            handleBatches[state] = batch = new HandleBatch();
        }

        isDirty = true;
        ShapeCount++;

        foreach (var corner in HandleCorners)
        {
            batch.Vertices.Add(new ToolHandleVertex(position, color, corner, size));
        }

        Extend(position);
    }

    internal void AddLabel(ToolLabel label)
    {
        labels.Add(label);
        ShapeCount++;
        Extend(label.Position);
    }

    /// <summary>Drops everything drawn, keeping the GL objects.</summary>
    public void Clear()
    {
        batches.Clear();
        handleBatches.Clear();
        labels.Clear();
        ShapeCount = 0;
        Bounds = default;
        hasBounds = false;
        isDirty = true;
    }

    // Everything goes in one buffer, each batch and primitive a range of it, and the handles in another
    private void Upload()
    {
        if (!isDirty)
        {
            return;
        }

        isDirty = false;
        uploadVertices.Clear();

        foreach (var batch in batches.Values)
        {
            batch.LinesStart = uploadVertices.Count;
            uploadVertices.AddRange(batch.Lines);
            batch.ThickLinesStart = uploadVertices.Count;
            uploadVertices.AddRange(batch.ThickLines);
            batch.TrianglesStart = uploadVertices.Count;
            uploadVertices.AddRange(batch.Triangles);
        }

        if (vertexBuffer == null)
        {
            vertexBuffer = new StreamingVertexBuffer(nameof(ToolGeometry));
            vao = ToolVertex.InputLayout.CreateVertexArray(nameof(ToolGeometry), vertexBuffer.Handle);
            vertexBuffer.AttachTo(vao, ToolVertex.InputLayout.Stride);
        }

        vertexBuffer.Upload(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(uploadVertices)));

        if (handleBatches.Count == 0)
        {
            return;
        }

        uploadHandleVertices.Clear();

        foreach (var batch in handleBatches.Values)
        {
            batch.Start = uploadHandleVertices.Count;
            uploadHandleVertices.AddRange(batch.Vertices);
        }

        if (handleVertexBuffer == null)
        {
            const string HandlesName = nameof(ToolGeometry) + "Handles";

            handleVertexBuffer = new StreamingVertexBuffer(HandlesName);
            handleVao = ToolHandleVertex.InputLayout.CreateVertexArray(HandlesName, handleVertexBuffer.Handle);
            handleVertexBuffer.AttachTo(handleVao, ToolHandleVertex.InputLayout.Stride);
        }

        handleVertexBuffer.Upload(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(uploadHandleVertices)));
    }

    /// <summary>Draws the batches of one layer with the tools shaders, the GL state set from what each is drawn with.</summary>
    internal void DrawLayer(ToolRenderLayer layer, ToolShaders shaders, uint objectId)
    {
        Upload();

        foreach (var (state, batch) in batches)
        {
            if (state.Layer != layer)
            {
                continue;
            }

            var flags = state.Flags;
            var translucent = (flags & ToolRenderModeFlags.Translucent) != 0;

            using var _ = GraphicsContext.RenderState.Scope(
                depthTest: (flags & ToolRenderModeFlags.NoZ) == 0,
                depthWrite: !translucent,
                blend: translucent,
                cullMode: RsCullMode.None);

            if (batch.Triangles.Count > 0)
            {
                using var cull = GraphicsContext.RenderState.Scope(cullMode: (flags & ToolRenderModeFlags.NoCull) != 0 ? RsCullMode.None : RsCullMode.Back);

                // The wireframe modes draw the edges of faces
                var wireframe = (flags & ToolRenderModeFlags.Wireframe) != 0;

                if (wireframe)
                {
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                }

                var shader = shaders.Get(flags, thickLines: false);
                shader.SetUniform("g_flPatternType", 0f);
                Draw(shader, batch.TrianglesStart, batch.Triangles.Count, PrimitiveType.Triangles, objectId);

                if (wireframe)
                {
                    GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                }
            }

            var pattern = (flags & ToolRenderModeFlags.Dashed) != 0 ? 1f : 0f;

            if (state.IsThick && batch.ThickLines.Count > 0)
            {
                var shader = shaders.Get(flags, thickLines: true);
                shader.SetUniform("g_flPatternType", pattern);
                shader.SetUniform("g_flLineThickness", state.LineWidth);
                Draw(shader, batch.ThickLinesStart, batch.ThickLines.Count, PrimitiveType.Triangles, objectId);
            }
            else if (batch.Lines.Count > 0)
            {
                var shader = shaders.Get(flags, thickLines: false);
                shader.SetUniform("g_flPatternType", pattern);
                Draw(shader, batch.LinesStart, batch.Lines.Count, PrimitiveType.Lines, objectId);
            }
        }

        foreach (var (state, batch) in handleBatches)
        {
            if (state.Layer != layer || !shaders.TryGetHandle(state, out var handle))
            {
                continue;
            }

            using var _ = GraphicsContext.RenderState.Scope(
                depthTest: !state.NoZ,
                depthWrite: !state.Translucent,
                blend: state.Translucent,
                cullMode: RsCullMode.None);

            handle.Use();
            VertexArray.Bind(handleVao, handle.Shader);
            GL.DrawArraysInstancedBaseInstance(PrimitiveType.Triangles, batch.Start, batch.Vertices.Count, 1, objectId);
        }
    }

    /// <summary>Draws the lines and triangles with another shader, such as the picking one.</summary>
    internal void DrawWith(Shader shader, uint objectId)
    {
        Upload();

        foreach (var batch in batches.Values)
        {
            Draw(shader, batch.TrianglesStart, batch.Triangles.Count, PrimitiveType.Triangles, objectId);
            Draw(shader, batch.LinesStart, batch.Lines.Count, PrimitiveType.Lines, objectId);
        }
    }

    private void Draw(Shader shader, int first, int count, PrimitiveType primitive, uint objectId)
    {
        if (count == 0)
        {
            return;
        }

        shader.Use();
        VertexArray.Bind(vao, shader);
        GL.DrawArraysInstancedBaseInstance(primitive, first, count, 1, objectId);
    }

    /// <summary>Deletes the GL objects.</summary>
    public void Delete()
    {
        if (vertexBuffer != null)
        {
            VertexArray.Delete(vao);
            vertexBuffer.Delete();
            vertexBuffer = null;
        }

        if (handleVertexBuffer != null)
        {
            VertexArray.Delete(handleVao);
            handleVertexBuffer.Delete();
            handleVertexBuffer = null;
        }
    }
}
