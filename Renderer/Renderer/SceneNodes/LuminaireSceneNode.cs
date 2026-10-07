using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// The visible geometry of a rect or omni2 light with <c>showlight</c> set: its rectangle, disc, sphere or tube,
    /// built in the light's space facing its forward axis and drawn additively with the light's radiance.
    /// </summary>
    internal sealed class LuminaireSceneNode : SceneNode
    {
        private enum LuminaireShape
        {
            Rectangle,
            Disc,
            Sphere,
            Tube,
            CappedTube,
        }

        private const int Segments = 24;
        private const int SphereRings = 16;

        private readonly SceneLight light;
        private readonly Shader shader;
        private readonly int vao;
        private readonly int vertexBuffer;
        private readonly int indexBuffer;
        private readonly int indexCount;

        // The light's color is spread over the luminaire's area and tinted by the luminaire material's color texture
        private readonly Vector3 luminanceScale;

        public LuminaireSceneNode(Scene scene, SceneLight light) : base(scene)
        {
            this.light = light;
            shader = scene.RendererContext.ShaderLoader.LoadShader("luminaire");

            var shape = light switch
            {
                { Entity: SceneLight.EntityType.Rect, Shape: 1f } => LuminaireShape.Disc,
                { Entity: SceneLight.EntityType.Rect } => LuminaireShape.Rectangle,
                { LuminaireShape: 1 } => LuminaireShape.Tube,
                { LuminaireShape: 2 } => LuminaireShape.CappedTube,
                _ => LuminaireShape.Sphere,
            };

            // Half extents for a rectangle or disc, the radius and half length for a sphere or tube
            var sizeX = MathF.Max(light.SizeParams.X, 0.001f);
            var sizeY = MathF.Max(light.SizeParams.Y, 0.001f);

            var verts = new List<SimpleVertex>();
            var inds = new List<int>();
            float area;
            Vector3 halfExtents;

            switch (shape)
            {
                case LuminaireShape.Rectangle:
                    AddRectangle(verts, inds, Vector3.UnitY * sizeX, Vector3.UnitZ * sizeY);
                    area = 4f * sizeX * sizeY;
                    halfExtents = new Vector3(0f, sizeX, sizeY);
                    break;

                case LuminaireShape.Disc:
                    AddDisc(verts, inds, Vector3.Zero, Vector3.UnitY * sizeX, Vector3.UnitZ * sizeY);
                    area = MathF.PI * sizeX * sizeY;
                    halfExtents = new Vector3(0f, sizeX, sizeY);
                    break;

                case LuminaireShape.Sphere:
                    AddSphere(verts, inds, sizeX);
                    area = 4f * MathF.PI * sizeX * sizeX;
                    halfExtents = new Vector3(sizeX);
                    break;

                default:
                    AddTube(verts, inds, sizeX, sizeY);
                    area = 4f * MathF.PI * sizeX * sizeY;

                    if (shape == LuminaireShape.CappedTube)
                    {
                        // The end caps face out of the tube along +Y and -Y
                        AddDisc(verts, inds, Vector3.UnitY * sizeY, Vector3.UnitZ * sizeX, Vector3.UnitX * sizeX);
                        AddDisc(verts, inds, -Vector3.UnitY * sizeY, Vector3.UnitX * sizeX, Vector3.UnitZ * sizeX);
                        area += 2f * MathF.PI * sizeX * sizeX;
                    }

                    halfExtents = new Vector3(sizeX, sizeY, sizeX);
                    break;
            }

            // Flat luminaires only emit from their front
            var isFlat = shape is LuminaireShape.Rectangle or LuminaireShape.Disc;
            var materialColor = ColorSpace.SrgbGammaToLinear(new Vector3(186f, 188f, 186f) / 255f);
            luminanceScale = materialColor * (isFlat ? MathF.PI : 4f * MathF.PI) / area;
            LocalBoundingBox = new AABB(-halfExtents, halfExtents);

            indexCount = inds.Count;

            var label = nameof(LuminaireSceneNode);
            vertexBuffer = GraphicsDevice.CreateBuffer<SimpleVertex>(label, CollectionsMarshal.AsSpan(verts), BufferUsage.Static);
            indexBuffer = GraphicsDevice.CreateBuffer<int>(label, CollectionsMarshal.AsSpan(inds), BufferUsage.Static);
            vao = SimpleVertex.InputLayout.CreateVertexArray(label, vertexBuffer, indexBuffer);
        }

        /// <inheritdoc/>
        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent || context.ReplacementShader != null)
            {
                return;
            }

            var luminance = light.ComputeOmni2Color() * luminanceScale;

            if (luminance == Vector3.Zero)
            {
                return;
            }

            shader.Use();
            shader.SetUniform("g_vLuminance", luminance);

            VertexArray.Bind(vao, shader);

            var renderState = GraphicsContext.RenderState;
            var state = renderState.CurrentPass;
            state.DepthStencil.DepthWriteEnable = false;
            state.BlendEnable = true;
            state.SetBlend(RsBlendMode.One, RsBlendMode.One);

            using var _ = renderState.Scope(in state);

            GL.DrawElementsInstancedBaseInstance(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0, 1, Id);
        }

        /// <inheritdoc/>
        public override void Delete()
        {
            VertexArray.Delete(vao);
            GL.DeleteBuffer(vertexBuffer);
            GL.DeleteBuffer(indexBuffer);
        }

        // A rectangle around the origin spanned by two half axes, facing their cross product
        private static void AddRectangle(List<SimpleVertex> verts, List<int> inds, Vector3 axisA, Vector3 axisB)
        {
            var first = verts.Count;

            verts.Add(new SimpleVertex(axisA - axisB, Color32.White));
            verts.Add(new SimpleVertex(axisA + axisB, Color32.White));
            verts.Add(new SimpleVertex(-axisA + axisB, Color32.White));
            verts.Add(new SimpleVertex(-axisA - axisB, Color32.White));

            inds.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        // An ellipse around center spanned by two half axes, facing their cross product
        private static void AddDisc(List<SimpleVertex> verts, List<int> inds, Vector3 center, Vector3 axisA, Vector3 axisB)
        {
            var first = verts.Count;

            for (var i = 0; i < Segments; i++)
            {
                var angle = i * MathF.Tau / Segments;
                verts.Add(new SimpleVertex(center + axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle), Color32.White));
            }

            for (var i = 1; i < Segments - 1; i++)
            {
                inds.AddRange([first, first + i, first + i + 1]);
            }
        }

        private static void AddSphere(List<SimpleVertex> verts, List<int> inds, float radius)
        {
            var first = verts.Count;

            for (var ring = 0; ring <= SphereRings; ring++)
            {
                var theta = ring * MathF.PI / SphereRings;

                for (var segment = 0; segment <= Segments; segment++)
                {
                    var phi = segment * MathF.Tau / Segments;
                    var direction = new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Sin(theta) * MathF.Sin(phi), MathF.Cos(theta));
                    verts.Add(new SimpleVertex(direction * radius, Color32.White));
                }
            }

            const int Row = Segments + 1;

            for (var ring = 0; ring < SphereRings; ring++)
            {
                for (var segment = 0; segment < Segments; segment++)
                {
                    var a = first + ring * Row + segment;
                    var b = a + Row;
                    inds.AddRange([a, b, a + 1, a + 1, b, b + 1]);
                }
            }
        }

        private static void AddTube(List<SimpleVertex> verts, List<int> inds, float radius, float halfLength)
        {
            var first = verts.Count;

            for (var segment = 0; segment <= Segments; segment++)
            {
                var angle = segment * MathF.Tau / Segments;
                var x = radius * MathF.Cos(angle);
                var z = radius * MathF.Sin(angle);
                verts.Add(new SimpleVertex(new Vector3(x, -halfLength, z), Color32.White));
                verts.Add(new SimpleVertex(new Vector3(x, halfLength, z), Color32.White));
            }

            for (var segment = 0; segment < Segments; segment++)
            {
                var a = first + segment * 2;
                inds.AddRange([a, a + 1, a + 2, a + 2, a + 1, a + 3]);
            }
        }
    }
}
