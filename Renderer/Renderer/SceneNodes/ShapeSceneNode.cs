using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.Materials;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Base class for scene nodes that render primitive shapes like boxes, spheres, and capsules.
    /// </summary>
    public abstract class ShapeSceneNode : SceneNode
    {
        /// <summary>Gets whether this shape renders with transparency.</summary>
        public virtual bool IsTranslucent { get; } = true;

        /// <summary>Gets or sets whether the current render mode allows translucent rendering.</summary>
        public bool IsTranslucentRenderMode { get; set; } = true;

        // sphere/capsule constants
        private const int SphereSegments = 8;
        private const int SphereBands = 5;

        /// <summary>Number of vertices in one hemisphere (used for sphere and capsule allocation).</summary>
        public const int HemisphereVerts = SphereBands * SphereSegments + 1;

        /// <summary>Number of triangles in one hemisphere.</summary>
        public const int HemisphereTriangles = SphereSegments * (2 * SphereBands - 1);

        /// <summary>Number of triangles in a full capsule (two hemispheres plus the connecting cylinder band).</summary>
        public const int CapsuleTriangles = 2 * HemisphereTriangles + 2 * SphereSegments;

        /// <summary>Gets the shader used to render this shape.</summary>
        protected Shader shader { get; init; }

        /// <summary>Gets the number of triangle indices at the start of the GPU index buffer.</summary>
        protected int indexCount { get; private set; }

        // Line index pairs that follow the triangle indices, drawn instead of a wireframe
        private int lineIndexCount;

        /// <summary>Gets the vertex array state for this shape.</summary>
        protected int vao { get; private set; }

        /// <summary>Gets whether this shape uses normal-based shading.</summary>
        protected virtual bool Shaded { get; } = true;

        /// <summary>Gets whether this shape draws its back faces when translucent.</summary>
        protected virtual bool IsDoubleSided { get; } = true;

        /// <summary>Gets or sets an optional tool texture overlay rendered on top of the shape.</summary>
        protected RenderTexture? ToolTexture { get; set; }

        /// <summary>
        /// Gets whether the translucent fill is drawn with order independent transparency where the renderer supports it,
        /// for volumes such as triggers that can span the whole map, which no sort order draws correctly.
        /// </summary>
        internal virtual bool HasOrderIndependentFill => false;

        private ShapeSceneNode(Scene scene) : base(scene)
        {
            shader = Scene.RendererContext.ShaderLoader.LoadShader("basic_shape", scene.LightingInfo.CreateShaderArguments());
        }

        internal ShapeSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : this(scene)
        {
            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node whose last <paramref name="lineIndexCount"/> indices are pairs that draw its edges as lines.
        /// </summary>
        internal ShapeSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds, int lineIndexCount) : this(scene)
        {
            Init(verts, inds);

            indexCount -= lineIndexCount;
            this.lineIndexCount = lineIndexCount;
        }

        /// <summary>
        /// Constructs a node with a single box shape
        /// </summary>
        internal ShapeSceneNode(Scene scene, Vector3 minBounds, Vector3 maxBounds, Color32 color) : this(scene)
        {
            var verts = new List<SimpleVertexNormal>(6 * 4);
            var inds = new List<int>(6 * 6);
            AddBox(verts, inds, minBounds, maxBounds, color);

            LocalBoundingBox = new AABB(minBounds, maxBounds);

            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node with a single capsule shape
        /// </summary>
        internal ShapeSceneNode(Scene scene, Vector3 from, Vector3 to, float radius, Color32 color) : this(scene)
        {
            var verts = new List<SimpleVertexNormal>(HemisphereVerts * 2);
            var inds = new List<int>(CapsuleTriangles * 6);
            AddCapsule(verts, inds, from, to, radius, color);

            var min = Vector3.Min(from, to);
            var max = Vector3.Max(from, to);
            LocalBoundingBox = new AABB(min - new Vector3(radius), max + new Vector3(radius));

            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node with a single sphere shape
        /// </summary>
        internal ShapeSceneNode(Scene scene, Vector3 center, float radius, Color32 color) : this(scene)
        {
            var verts = new List<SimpleVertexNormal>(HemisphereVerts * 2);
            var inds = new List<int>(HemisphereTriangles * 6 * 2);
            AddSphere(verts, inds, center, radius, color);

            LocalBoundingBox = new AABB(center, radius);

            Init(verts, inds);
        }

        /// <inheritdoc/>
        public override void SetRenderMode(string mode)
        {
            IsTranslucentRenderMode = mode is not "Color" and not "Normals" and not "VertexColor";
        }

        private void Init(List<SimpleVertexNormal> verts, List<int> inds)
        {
            indexCount = inds.Count;

            var label = GetType().Name;
            var vboHandle = GraphicsDevice.CreateBuffer<SimpleVertexNormal>(label, CollectionsMarshal.AsSpan(verts), BufferUsage.Static);
            var iboHandle = GraphicsDevice.CreateBuffer<int>(label, CollectionsMarshal.AsSpan(inds), BufferUsage.Static);

            vao = SimpleVertexNormal.InputLayout.CreateVertexArray(nameof(ShapeSceneNode), vboHandle, iboHandle);
        }

        /// <summary>Appends capsule geometry (two hemispheres and a cylinder band) to the given vertex and index lists.</summary>
        protected static void AddCapsule(List<SimpleVertexNormal> verts, List<int> inds, Vector3 c0, Vector3 c1, float radius, Color32 color)
        {
            var up = Vector3.Normalize(c0 - c1);

            var baseVert0 = verts.Count;
            AddHemisphere(verts, inds, c0, radius, up, color);

            var baseVert1 = verts.Count;
            AddHemisphere(verts, inds, c1, radius, -up, color);

            // connect hemispheres to create the cylinder
            for (var segment = 0; segment < SphereSegments; segment++)
            {
                var a = baseVert0 + segment;
                var b = baseVert0 + (segment + 1) % SphereSegments;

                // second sphere has indices in reverse order (since it's rotated the other way)
                var c = baseVert1 + (SphereSegments - segment) % SphereSegments;
                var d = baseVert1 + (SphereSegments - (segment + 1)) % SphereSegments;

                AddTriangle(inds, 0, b, a, c);
                AddTriangle(inds, 0, b, c, d);
            }
        }

        /// <summary>Returns a unit vector perpendicular to <paramref name="a"/>.</summary>
        protected static Vector3 GetOrthogonal(Vector3 a)
        {
            // Any vector not parallel to the given vector
            var arbitraryVector = new Vector3(1, 0, 0);
            var arbitraryDot = Vector3.Dot(arbitraryVector, a);
            if (Math.Abs(arbitraryDot) == 1)
            {
                arbitraryVector = new Vector3(0, 1, 0);
            }

            return GetOrthogonal(a, arbitraryVector);
        }

        /// <summary>Returns a unit vector perpendicular to both <paramref name="a"/> and <paramref name="b"/>.</summary>
        protected static Vector3 GetOrthogonal(Vector3 a, Vector3 b)
        {
            var sideVector = Vector3.Cross(a, b);
            return Vector3.Normalize(sideVector);
        }

        /// <summary>Appends hemisphere geometry oriented along <paramref name="up"/> to the given vertex and index lists.</summary>
        protected static void AddHemisphere(List<SimpleVertexNormal> verts, List<int> inds, Vector3 center, float radius, Vector3 up, Color32 color)
        {
            var baseVertex = verts.Count;

            var axisUp = Vector3.Normalize(up);
            var axisAround = GetOrthogonal(up);
            var v = GetOrthogonal(axisAround, axisUp);

            // generate vertices
            for (var band = 0; band < SphereBands; band++)
            {
                var angleUp = -float.DegreesToRadians(band * (90.0f / SphereBands));
                var quatUp = Quaternion.CreateFromAxisAngle(axisAround, angleUp);

                for (var segment = 0; segment < SphereSegments; segment++)
                {
                    var angleAround = float.DegreesToRadians(segment * (360.0f / SphereSegments));
                    var quatAround = Quaternion.CreateFromAxisAngle(axisUp, angleAround);

                    var point = Vector3.Transform(v, Quaternion.Multiply(quatAround, quatUp));
                    var transformed = center + radius * Vector3.Normalize(point);

                    verts.Add(new(transformed, color, point));
                }
            }

            // midpoint
            var topVertexIndex = verts.Count - baseVertex;
            var transformedUp = center + radius * Vector3.Normalize(up);
            verts.Add(new(transformedUp, color, Vector3.Normalize(up)));

            // generate triangles
            for (var band = 0; band < SphereBands; band++)
            {
                for (var segment = 0; segment < SphereSegments; segment++)
                {
                    var i = band * SphereSegments + segment;
                    var iNext = segment == SphereSegments - 1 ? (band * SphereSegments) : i + 1;

                    if (band == SphereBands - 1)
                    {
                        // last band connects to midpoint only
                        AddTriangle(inds, baseVertex, i, iNext, topVertexIndex);
                    }
                    else
                    {
                        AddTriangle(inds, baseVertex, i, iNext, i + SphereSegments);
                        AddTriangle(inds, baseVertex, iNext, iNext + SphereSegments, i + SphereSegments);
                    }
                }
            }
        }

        /// <summary>
        /// Appends line index pairs along a capsule from <see cref="AddCapsule(List{SimpleVertexNormal}, List{int}, Vector3, Vector3, float, Color32)"/>
        /// that starts at <paramref name="baseVertex"/>: both rims, and four lines along its length that go on over the caps.
        /// </summary>
        protected static void AddCapsuleLines(List<int> lines, int baseVertex)
        {
            var secondHemisphere = baseVertex + HemisphereVerts;

            AddHemisphereLines(lines, baseVertex, withRim: true);
            AddHemisphereLines(lines, secondHemisphere, withRim: true);

            // The second hemisphere winds the other way around, as in AddCapsule
            for (var segment = 0; segment < SphereSegments; segment += SphereSegments / 4)
            {
                lines.Add(baseVertex + segment);
                lines.Add(secondHemisphere + (SphereSegments - segment) % SphereSegments);
            }
        }

        /// <summary>
        /// Appends line index pairs along a sphere from <see cref="AddSphere(List{SimpleVertexNormal}, List{int}, Vector3, float, Color32)"/>
        /// that starts at <paramref name="baseVertex"/>: its equator and two circles through its poles.
        /// </summary>
        protected static void AddSphereLines(List<int> lines, int baseVertex)
        {
            AddHemisphereLines(lines, baseVertex, withRim: true);
            AddHemisphereLines(lines, baseVertex + HemisphereVerts, withRim: false);
        }

        // Four arcs from the rim to the top, a quarter turn apart, which meet the arcs of an opposite hemisphere
        private static void AddHemisphereLines(List<int> lines, int baseVertex, bool withRim)
        {
            var topVertex = baseVertex + SphereBands * SphereSegments;

            for (var segment = 0; segment < SphereSegments; segment += SphereSegments / 4)
            {
                for (var band = 0; band < SphereBands; band++)
                {
                    var from = baseVertex + band * SphereSegments + segment;
                    var to = band == SphereBands - 1 ? topVertex : from + SphereSegments;

                    lines.Add(from);
                    lines.Add(to);
                }
            }

            if (withRim)
            {
                for (var segment = 0; segment < SphereSegments; segment++)
                {
                    lines.Add(baseVertex + segment);
                    lines.Add(baseVertex + (segment + 1) % SphereSegments);
                }
            }
        }

        /// <summary>Appends solid box geometry to the given vertex and index lists.</summary>
        protected static void AddBox(List<SimpleVertexNormal> verts, List<int> inds, Vector3 minBounds, Vector3 maxBounds, Color32 color)
        {
            Span<Vector3> corners =
            [
                new(minBounds.X, minBounds.Y, minBounds.Z),
                new(minBounds.X, minBounds.Y, maxBounds.Z),
                new(minBounds.X, maxBounds.Y, maxBounds.Z),
                new(minBounds.X, maxBounds.Y, minBounds.Z),

                new(maxBounds.X, minBounds.Y, minBounds.Z),
                new(maxBounds.X, minBounds.Y, maxBounds.Z),
                new(maxBounds.X, maxBounds.Y, maxBounds.Z),
                new(maxBounds.X, maxBounds.Y, minBounds.Z),
            ];

            ReadOnlySpan<(int A, int B, int C, int D, Vector3 Normal)> faces =
            [
                (0, 1, 2, 3, -Vector3.UnitX),
                (1, 5, 6, 2, Vector3.UnitZ),
                (5, 4, 7, 6, Vector3.UnitX),
                (0, 3, 7, 4, -Vector3.UnitZ),
                (3, 2, 6, 7, Vector3.UnitY),
                (1, 0, 4, 5, -Vector3.UnitY),
            ];

            // Faces do not share corners, so each keeps a flat normal instead of blending into its neighbours
            foreach (var (a, b, c, d, normal) in faces)
            {
                var baseVertex = verts.Count;

                verts.Add(new(corners[a], color, normal));
                verts.Add(new(corners[b], color, normal));
                verts.Add(new(corners[c], color, normal));
                verts.Add(new(corners[d], color, normal));

                AddTriangle(inds, baseVertex, 0, 1, 2);
                AddTriangle(inds, baseVertex, 2, 3, 0);
            }
        }

        /// <summary>Appends full sphere geometry (two hemispheres) to the given vertex and index lists.</summary>
        protected static void AddSphere(List<SimpleVertexNormal> verts, List<int> inds, Vector3 center, float radius, Color32 color)
        {
            AddHemisphere(verts, inds, center, radius, Vector3.UnitZ, color);
            AddHemisphere(verts, inds, center, radius, -Vector3.UnitZ, color);
        }

        /// <summary>Appends a single triangle (three absolute indices offset by <paramref name="baseVertex"/>) to the index list.</summary>
        protected static void AddTriangle(List<int> inds, int baseVertex, int a, int b, int c)
        {
            inds.Add(baseVertex + a);
            inds.Add(baseVertex + b);
            inds.Add(baseVertex + c);
        }

        /// <summary>Appends a line segment as two colored vertices to the given list.</summary>
        public static void AddLine(List<SimpleVertex> vertices, Vector3 from, Vector3 to, Color32 color)
        {
            vertices.Add(new SimpleVertex(from, color));
            vertices.Add(new SimpleVertex(to, color));
        }

        /// <summary>Appends the 12 wireframe edges of an <see cref="AABB"/> as line segments to the given vertex list.</summary>
        public static void AddBox(List<SimpleVertex> vertices, in AABB box, Color32 color)
        {
            // Adding a box will add many vertices, so ensure the required capacity for it up front
            vertices.EnsureCapacity(vertices.Count + 2 * 12);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Min.Y, box.Min.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Min.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Max.Y, box.Min.Z), color);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Min.Y, box.Min.Z), color);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Max.Z), new Vector3(box.Max.X, box.Min.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Max.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Max.Z), new Vector3(box.Min.X, box.Max.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Max.Z), new Vector3(box.Min.X, box.Min.Y, box.Max.Z), color);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Min.X, box.Min.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Min.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z), color);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Max.Y, box.Max.Z), color);
        }

        /// <summary>Appends a wireframe sphere as three orthogonal circles of line segments to the given vertex list.</summary>
        public static void AddSphere(List<SimpleVertex> vertices, Vector3 center, float radius, Color32 color)
        {
            const int Segments = 16;

            vertices.EnsureCapacity(vertices.Count + 2 * 3 * Segments);

            for (var axis = 0; axis < 3; axis++)
            {
                var previous = Vector3.Zero;

                for (var i = 0; i <= Segments; i++)
                {
                    var (sin, cos) = MathF.SinCos(MathF.Tau * i / Segments);
                    var point = center + radius * (axis switch
                    {
                        0 => new Vector3(0f, cos, sin),
                        1 => new Vector3(cos, 0f, sin),
                        _ => new Vector3(cos, sin, 0f),
                    });

                    if (i > 0)
                    {
                        AddLine(vertices, previous, point, color);
                    }

                    previous = point;
                }
            }
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            var isTranslucent = IsTranslucent && IsTranslucentRenderMode && context.ReplacementShader == null;
            var renderPass = isTranslucent ? RenderPass.Translucent : RenderPass.Opaque;

            if (context.RenderPass != renderPass && context.RenderPass != RenderPass.Outline)
            {
                return;
            }

            if (HasOrderIndependentFill && context.OrderIndependentTranslucency)
            {
                return; // The renderer draws the fill with RenderOrderIndependentFill after this layer
            }

            var renderShader = context.ReplacementShader ?? shader;
            renderShader.Use();

            renderShader.SetUniform("g_bNormalShaded", Shaded);
            renderShader.SetUniform("g_bTriplanarMapping", ToolTexture != null);

            renderShader.SetTexture(0, "g_tColor", ToolTexture ?? Scene.RendererContext.MaterialLoader.GetDefaultColor());

            VertexArray.Bind(vao, renderShader);

            var renderState = GraphicsContext.RenderState;
            var state = renderState.CurrentPass;
            state.DepthStencil.DepthFunc = RsComparison.CloserEqual;

            if (isTranslucent && IsDoubleSided)
            {
                state.Rasterizer.CullMode = RsCullMode.None;
            }

            using var _ = renderState.Scope(in state);

            if (isTranslucent)
            {
                var lineState = state;

                if (lineIndexCount > 0)
                {
                    lineState.BlendEnable = true;
                    lineState.SetBlend(RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha);
                    renderState.Apply(in lineState);

                    DrawLines(shader.WithCombo("D_LINES", 1));

                    renderShader.Use();
                    VertexArray.Bind(vao, renderShader);
                }
                else
                {
                    lineState.Rasterizer.FillMode = RsFillMode.Wireframe;
                    lineState.BlendEnable = false;
                    renderState.Apply(in lineState);

                    renderShader.SetUniform("g_bNormalShaded", false);
                    GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0, 1, Id);
                }

                // Triangles
                var fillState = WithFillDepthBias(state);
                fillState.BlendEnable = true;
                fillState.SetBlend(RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha);
                renderState.Apply(in fillState);
                renderShader.SetUniform("g_bNormalShaded", Shaded);
                GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0, 1, Id);
            }
            else
            {
                GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0, 1, Id);
            }
        }

        /// <summary>
        /// Draws the translucent fill into the order independent layers the caller binds, which the fill shader
        /// writes instead of its color.
        /// </summary>
        internal void RenderOrderIndependentFill()
        {
            var fillShader = shader.WithCombo("D_OIT", 1);
            fillShader.Use();

            fillShader.SetUniform("g_bNormalShaded", Shaded);
            fillShader.SetUniform("g_bTriplanarMapping", ToolTexture != null);

            // Drawn outside the batch renderer, which rebinds the reserved units after each node it draws
            fillShader.SetTexture(RenderMaterial.TextureUnitStart, "g_tColor", ToolTexture ?? Scene.RendererContext.MaterialLoader.GetDefaultColor());

            VertexArray.Bind(vao, fillShader);

            var renderState = GraphicsContext.RenderState;
            var state = WithFillDepthBias(renderState.CurrentPass);
            state.DepthStencil.DepthFunc = RsComparison.CloserEqual;

            if (IsDoubleSided)
            {
                state.Rasterizer.CullMode = RsCullMode.None;
            }

            using var _ = renderState.Scope(in state);

            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0, 1, Id);
        }

        /// <summary>Draws the edge lines over the composited order independent fills, faded by the fills in front of them.</summary>
        internal void RenderOrderIndependentLines()
        {
            if (lineIndexCount == 0)
            {
                return;
            }

            var renderState = GraphicsContext.RenderState;
            var state = renderState.CurrentPass;
            state.DepthStencil.DepthFunc = RsComparison.CloserEqual;

            using var _ = renderState.Scope(in state);

            DrawLines(shader.WithCombo("D_LINES", 1).WithCombo("D_OIT", 1));
        }

        private void DrawLines(Shader lineShader)
        {
            lineShader.Use();
            lineShader.SetUniform("g_bTriplanarMapping", ToolTexture != null);

            if (ToolTexture != null)
            {
                lineShader.SetUniform("g_vToolTextureAverage", ToolTexture.Reflectivity.AsVector3());
            }

            VertexArray.Bind(vao, lineShader);

            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Lines, lineIndexCount, DrawElementsType.UnsignedInt, indexCount * sizeof(int), 1, Id);
        }

        // Pulls the fill in front of the scene surfaces it lies on, such as the walls that world collision follows
        private static RenderState WithFillDepthBias(RenderState state)
        {
            state.Rasterizer.SlopeScaledDepthBias = 2f;
            state.Rasterizer.DepthBias = 100;
            state.Rasterizer.DepthBiasClamp = 0.05f;
            return state;
        }

        /// <inheritdoc/>
        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;
    }
}
