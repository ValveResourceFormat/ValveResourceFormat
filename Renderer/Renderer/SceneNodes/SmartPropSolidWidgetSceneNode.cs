using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Base class for SmartProp widgets that combine filled triangle handles with optional guide lines.
    /// </summary>
    public abstract class SmartPropSolidWidgetSceneNode : ShapeSceneNode
    {
        private readonly LineBuffer? lineBuffer;
        private readonly SolidBuffer? opaqueSolidBuffer;
        private readonly Vector3 labelPosition;
        private readonly string labelText;
        private readonly Color32 labelColor;

        /// <summary>Gets or sets whether the widget's text label is drawn.</summary>
        public bool ShowLabel { get; set; } = true;

        /// <inheritdoc/>
        protected override bool DrawWireframeWhenTranslucent => false;

        /// <inheritdoc/>
        protected override bool WriteDepthWhenTranslucent => false;

        /// <summary>Initializes filled widget geometry, guide lines, and an optional label.</summary>
        protected SmartPropSolidWidgetSceneNode(
            Scene scene,
            List<SimpleVertexNormal> solidVertices,
            List<int> solidIndices,
            List<SimpleVertex>? lineVertices = null,
            Vector3 labelPosition = default,
            string labelText = "",
            Color32 labelColor = default,
            List<SimpleVertexNormal>? opaqueSolidVertices = null,
            List<int>? opaqueSolidIndices = null)
            : base(scene, solidVertices, solidIndices)
        {
            this.labelPosition = labelPosition;
            this.labelText = labelText;
            this.labelColor = labelColor == default ? Color32.White : labelColor;

            if (lineVertices is { Count: > 0 })
            {
                lineBuffer = new LineBuffer(Scene.RendererContext, GetType().Name);
                lineBuffer.Upload(lineVertices, BufferUsage.Static);
            }

            if (opaqueSolidVertices is { Count: > 0 } && opaqueSolidIndices is { Count: > 0 })
            {
                opaqueSolidBuffer = new SolidBuffer(GetType().Name, opaqueSolidVertices, opaqueSolidIndices);
            }
            else if (opaqueSolidVertices is { Count: > 0 } || opaqueSolidIndices is { Count: > 0 })
            {
                throw new ArgumentException("Opaque widget geometry must contain both vertices and indices.");
            }

            var hasBounds = false;
            var boundsMin = Vector3.Zero;
            var boundsMax = Vector3.Zero;

            foreach (ref readonly var vertex in CollectionsMarshal.AsSpan(solidVertices))
            {
                AccumulateBounds(vertex.Position, ref hasBounds, ref boundsMin, ref boundsMax);
            }

            if (lineVertices != null)
            {
                foreach (ref readonly var vertex in CollectionsMarshal.AsSpan(lineVertices))
                {
                    AccumulateBounds(vertex.Position, ref hasBounds, ref boundsMin, ref boundsMax);
                }
            }

            if (opaqueSolidVertices != null)
            {
                foreach (ref readonly var vertex in CollectionsMarshal.AsSpan(opaqueSolidVertices))
                {
                    AccumulateBounds(vertex.Position, ref hasBounds, ref boundsMin, ref boundsMax);
                }
            }

            LocalBoundingBox = new AABB(boundsMin, boundsMax);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            lineBuffer?.Delete();
            opaqueSolidBuffer?.Delete();
            base.Delete();
        }

        /// <inheritdoc/>
        public override void Update(Scene.UpdateContext context)
        {
            if (ShowLabel && labelText.Length > 0)
            {
                context.TextRenderer.AddTextBillboard(labelPosition, new TextRenderer.TextRenderRequest
                {
                    Scale = 10f,
                    Text = labelText,
                    Color = labelColor,
                }, context.Camera);
            }
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            base.Render(context);

            RenderOpaqueSolid(context);

            if (lineBuffer == null)
            {
                return;
            }

            var isTranslucent = IsTranslucent && IsTranslucentRenderMode && context.ReplacementShader == null;
            var renderPass = isTranslucent ? RenderPass.Translucent : RenderPass.Opaque;
            if (context.RenderPass != renderPass && context.RenderPass != RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? lineBuffer.Shader;
            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);

            var state = GraphicsContext.RenderState.CurrentPass;
            state.DepthStencil.DepthTestEnable = DepthTested;
            state.DepthStencil.DepthWriteEnable = !isTranslucent;
            state.DepthStencil.DepthFunc = RsComparison.CloserEqual;
            state.BlendEnable = isTranslucent;
            if (isTranslucent)
            {
                state.SetBlend(RsBlendMode.SrcAlpha, RsBlendMode.InvSrcAlpha);
            }

            using var _ = GraphicsContext.RenderState.Scope(in state);
            lineBuffer.Draw(renderShader, Id);
        }

        private void RenderOpaqueSolid(Scene.RenderContext context)
        {
            var isTranslucent = IsTranslucent && IsTranslucentRenderMode && context.ReplacementShader == null;
            var renderPass = isTranslucent ? RenderPass.Translucent : RenderPass.Opaque;
            if (opaqueSolidBuffer == null
                || context.RenderPass != renderPass && context.RenderPass != RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;
            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);
            renderShader.SetUniform("g_bNormalShaded", Shaded);
            renderShader.SetUniform("g_bTriplanarMapping", false);
            renderShader.SetTexture(0, "g_tColor", Scene.RendererContext.MaterialLoader.GetDefaultColor());

            var state = GraphicsContext.RenderState.CurrentPass;
            state.DepthStencil.DepthTestEnable = DepthTested;
            state.DepthStencil.DepthWriteEnable = DepthTested;
            state.DepthStencil.DepthFunc = RsComparison.CloserEqual;
            state.Rasterizer.CullMode = RsCullMode.None;
            state.BlendEnable = false;

            using var _ = GraphicsContext.RenderState.Scope(in state);
            opaqueSolidBuffer.Draw(renderShader, Id);
        }

        /// <summary>Adds a flat-shaded triangle to indexed solid geometry.</summary>
        protected static void AddSolidTriangle(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Color32 color)
        {
            var normal = Vector3.Cross(b - a, c - a);
            normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
            var baseVertex = vertices.Count;
            vertices.Add(new(a, color, normal));
            vertices.Add(new(b, color, normal));
            vertices.Add(new(c, color, normal));
            AddTriangle(indices, baseVertex, 0, 1, 2);
        }

        /// <summary>Adds a flat-shaded quad as two triangles.</summary>
        protected static void AddSolidQuad(
            List<SimpleVertexNormal> vertices,
            List<int> indices,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Color32 color)
        {
            AddSolidTriangle(vertices, indices, a, b, c, color);
            AddSolidTriangle(vertices, indices, a, c, d, color);
        }

        /// <summary>Adds the edges of indexed triangle geometry as line segments.</summary>
        protected static List<SimpleVertex> BuildTriangleEdges(List<SimpleVertexNormal> vertices, List<int> indices)
        {
            List<SimpleVertex> lines = new(indices.Count * 2);
            for (var i = 0; i < indices.Count; i += 3)
            {
                var a = vertices[indices[i]];
                var b = vertices[indices[i + 1]];
                var c = vertices[indices[i + 2]];
                ShapeSceneNode.AddLine(lines, a.Position, b.Position, a.Color);
                ShapeSceneNode.AddLine(lines, b.Position, c.Position, b.Color);
                ShapeSceneNode.AddLine(lines, c.Position, a.Position, c.Color);
            }

            return lines;
        }

        /// <summary>Adds a line segment to guide-line geometry.</summary>
        protected static void AddGuideLine(List<SimpleVertex> vertices, Vector3 from, Vector3 to, Color32 color)
            => ShapeSceneNode.AddLine(vertices, from, to, color);

        /// <summary>Returns two normalized axes perpendicular to a normalized direction.</summary>
        protected static (Vector3 U, Vector3 V) OrthonormalBasis(Vector3 axis)
        {
            axis = NormalizeOr(axis, Vector3.UnitZ);
            var helper = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
            var u = Vector3.Normalize(Vector3.Cross(axis, helper));
            return (u, Vector3.Cross(axis, u));
        }

        /// <summary>Reads normalized forward, left, and up basis rows from a Source 2 matrix.</summary>
        protected static (Vector3 Forward, Vector3 Left, Vector3 Up) BasisRows(Matrix4x4 matrix)
            => (
                NormalizeOr(new(matrix.M11, matrix.M12, matrix.M13), Vector3.UnitX),
                NormalizeOr(new(matrix.M21, matrix.M22, matrix.M23), Vector3.UnitY),
                NormalizeOr(new(matrix.M31, matrix.M32, matrix.M33), Vector3.UnitZ));

        /// <summary>Normalizes a vector, returning a fallback for a zero-length input.</summary>
        protected static Vector3 NormalizeOr(Vector3 value, Vector3 fallback)
            => value.LengthSquared() > 1e-12f ? Vector3.Normalize(value) : fallback;

        private static void AccumulateBounds(
            Vector3 position,
            ref bool hasBounds,
            ref Vector3 boundsMin,
            ref Vector3 boundsMax)
        {
            if (!hasBounds)
            {
                boundsMin = position;
                boundsMax = position;
                hasBounds = true;
                return;
            }

            boundsMin = Vector3.Min(boundsMin, position);
            boundsMax = Vector3.Max(boundsMax, position);
        }

        private sealed class SolidBuffer
        {
            private readonly int indexCount;
            private readonly int vboHandle;
            private readonly int iboHandle;
            private readonly int vao;

            public SolidBuffer(
                string label,
                List<SimpleVertexNormal> vertices,
                List<int> indices)
            {
                indexCount = indices.Count;
                vboHandle = GraphicsDevice.CreateBuffer<SimpleVertexNormal>(
                    label,
                    CollectionsMarshal.AsSpan(vertices),
                    BufferUsage.Static);
                iboHandle = GraphicsDevice.CreateBuffer<int>(
                    label,
                    CollectionsMarshal.AsSpan(indices),
                    BufferUsage.Static);
                vao = SimpleVertexNormal.InputLayout.CreateVertexArray(label, vboHandle, iboHandle);
            }

            public void Draw(Shader shader, uint objectId)
            {
                VertexArray.Bind(vao, shader);
                GL.DrawElementsInstancedBaseInstance(
                    PrimitiveType.Triangles,
                    indexCount,
                    DrawElementsType.UnsignedInt,
                    0,
                    1,
                    objectId);
            }

            public void Delete()
            {
                VertexArray.Delete(vao);
                GL.DeleteBuffer(vboHandle);
                GL.DeleteBuffer(iboHandle);
            }
        }
    }
}
