using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    abstract class ShapeSceneNode : SceneNode
    {
        public virtual bool IsTranslucent { get; } = true;
        // sphere/capsule constants
        private const int Segments = 8;
        private const int Bands = 5;

        protected Shader shader;
        protected int indexCount;
        protected int vaoHandle;

        public ShapeSceneNode(Scene scene, List<SimpleVertex> verts, List<int> inds)
            : base(scene)
        {
            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node with a single box shape
        /// </summary>
        public ShapeSceneNode(Scene scene, Vector3 minBounds, Vector3 maxBounds, Color32 color) : base(scene)
        {
            var inds = new List<int>();
            var verts = new List<SimpleVertex>();
            AddBox(verts, inds, minBounds, maxBounds, color);

            LocalBoundingBox = new AABB(minBounds, maxBounds);

            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node with a single capsule shape
        /// </summary>
        public ShapeSceneNode(Scene scene, Vector3 from, Vector3 to, float radius, Color32 color) : base(scene)
        {
            var inds = new List<int>();
            var verts = new List<SimpleVertex>();
            AddCapsule(verts, inds, from, to, radius, color);

            var min = Vector3.Min(from, to);
            var max = Vector3.Max(from, to);
            LocalBoundingBox = new AABB(min - new Vector3(radius), max + new Vector3(radius));

            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node with a single sphere shape
        /// </summary>
        public ShapeSceneNode(Scene scene, Vector3 center, float radius, Color32 color) : base(scene)
        {
            var inds = new List<int>();
            var verts = new List<SimpleVertex>();
            AddSphere(verts, inds, center, radius, color);

            LocalBoundingBox = new AABB(new Vector3(radius), new Vector3(-radius));

            Init(verts, inds);
        }

        private void Init(List<SimpleVertex> verts, List<int> inds)
        {
            indexCount = inds.Count;
            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.scene_node");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.CreateBuffers(1, out int iboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, verts.Count * SimpleVertex.SizeInBytes, verts.ToArray(), BufferUsageHint.StaticDraw);
            GL.NamedBufferData(iboHandle, inds.Count * sizeof(int), inds.ToArray(), BufferUsageHint.StaticDraw);

#if DEBUG
            var vaoLabel = nameof(PhysSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        protected static void AddFace(List<int> inds, int a, int b, int c, int d)
        {
            AddTriangle(inds, 0, a, b, c);
            AddTriangle(inds, 0, c, d, a);
        }

        protected static void AddCapsule(List<SimpleVertex> verts, List<int> inds, Vector3 c0, Vector3 c1, float radius, Color32 color)
        {
            var up = Vector3.Normalize(c0 - c1);

            var baseVert0 = verts.Count;
            AddHemisphere(verts, inds, c0, radius, up, color);

            var baseVert1 = verts.Count;
            AddHemisphere(verts, inds, c1, radius, -up, color);

            // connect hemispheres to create the cylinder
            for (var segment = 0; segment < Segments; segment++)
            {
                var a = baseVert0 + segment;
                var b = baseVert0 + (segment + 1) % Segments;

                // second sphere has indices in reverse order (since its rotated the other way)
                var c = baseVert1 + (Segments - segment) % Segments;
                var d = baseVert1 + (Segments - (segment + 1)) % Segments;

                AddTriangle(inds, 0, b, a, c);
                AddTriangle(inds, 0, b, c, d);
            }
        }

        protected static Vector3 GetOrtogonal(Vector3 a)
        {
            // Any vector not parallel to the given vector
            var arbitraryVector = new Vector3(1, 0, 0);
            var arbitraryDot = Vector3.Dot(arbitraryVector, a);
            if (Math.Abs(arbitraryDot) == 1)
            {
                arbitraryVector = new Vector3(0, 1, 0);
            }

            return GetOrtogonal(a, arbitraryVector);
        }

        protected static Vector3 GetOrtogonal(Vector3 a, Vector3 b)
        {
            var sideVector = Vector3.Cross(a, b);
            return Vector3.Normalize(sideVector);
        }

        protected static void AddHemisphere(List<SimpleVertex> verts, List<int> inds, Vector3 center, float radius, Vector3 up, Color32 color)
        {
            var baseVertex = verts.Count;

            var axisUp = Vector3.Normalize(up);
            var axisAround = GetOrtogonal(up);
            var v = GetOrtogonal(axisAround, axisUp);

            // generate vertices
            for (var band = 0; band < Bands; band++)
            {
                var angleUp = -MathUtils.ToRadians(band * (90.0f / Bands));
                var quatUp = Quaternion.CreateFromAxisAngle(axisAround, angleUp);

                for (var segment = 0; segment < Segments; segment++)
                {
                    var angleAround = MathUtils.ToRadians(segment * (360.0f / Segments));
                    var quatAround = Quaternion.CreateFromAxisAngle(axisUp, angleAround);

                    var point = Vector3.Transform(v, Quaternion.Multiply(quatAround, quatUp));
                    var transformed = center + radius * Vector3.Normalize(point);

                    verts.Add(new(transformed, point, color));
                }
            }

            // midpoint
            var topVertexIndex = verts.Count - baseVertex;
            var transformedUp = center + radius * Vector3.Normalize(up);
            verts.Add(new(transformedUp, Vector3.Normalize(up), color));

            // generate triangles
            for (var band = 0; band < Bands; band++)
            {
                for (var segment = 0; segment < Segments; segment++)
                {
                    var i = band * Segments + segment;
                    var iNext = segment == Segments - 1 ? (band * Segments) : i + 1;

                    if (band == Bands - 1)
                    {
                        // last band connects to midpoint only
                        AddTriangle(inds, baseVertex, i, iNext, topVertexIndex);
                    }
                    else
                    {
                        AddTriangle(inds, baseVertex, i, iNext, i + Segments);
                        AddTriangle(inds, baseVertex, iNext, iNext + Segments, i + Segments);
                    }
                }
            }
        }

        protected static void AddBox(List<SimpleVertex> verts, List<int> inds, Vector3 minBounds, Vector3 maxBounds, Color32 color)
        {
            verts.AddRange(
                [
                    new(new(minBounds.X, minBounds.Y, minBounds.Z), color),
                    new(new(minBounds.X, minBounds.Y, maxBounds.Z), color),
                    new(new(minBounds.X, maxBounds.Y, maxBounds.Z), color),
                    new(new(minBounds.X, maxBounds.Y, minBounds.Z), color),

                    new(new(maxBounds.X, minBounds.Y, minBounds.Z), color),
                    new(new(maxBounds.X, minBounds.Y, maxBounds.Z), color),
                    new(new(maxBounds.X, maxBounds.Y, maxBounds.Z), color),
                    new(new(maxBounds.X, maxBounds.Y, minBounds.Z), color)
                ]
            );

            AddFace(inds, 0, 1, 2, 3);
            AddFace(inds, 1, 5, 6, 2);
            AddFace(inds, 5, 4, 7, 6);
            AddFace(inds, 0, 3, 7, 4);
            AddFace(inds, 3, 2, 6, 7);
            AddFace(inds, 1, 0, 4, 5);
        }

        protected static void AddSphere(List<SimpleVertex> verts, List<int> inds, Vector3 center, float radius, Color32 color)
        {
            AddHemisphere(verts, inds, center, radius, Vector3.UnitZ, color);
            AddHemisphere(verts, inds, center, radius, -Vector3.UnitZ, color);
        }

        protected static void AddTriangle(List<int> inds, int baseVertex, int a, int b, int c)
        {
            inds.Add(baseVertex + a);
            inds.Add(baseVertex + b);
            inds.Add(baseVertex + b);
            inds.Add(baseVertex + c);
            inds.Add(baseVertex + c);
            inds.Add(baseVertex + a);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (IsTranslucent && context.RenderPass != RenderPass.Translucent || !IsTranslucent && context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);

            if (IsTranslucent)
            {
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

                GL.Enable(EnableCap.PolygonOffsetLine);
                GL.Enable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffsetClamp(0, 96, 0.0005f);

                GL.DrawElements(PrimitiveType.Lines, indexCount, DrawElementsType.UnsignedInt, 0);

                // triangles
                GL.Disable(EnableCap.CullFace);

                GL.DrawElements(PrimitiveType.TrianglesAdjacency, indexCount, DrawElementsType.UnsignedInt, 0);

                GL.Disable(EnableCap.PolygonOffsetLine);
                GL.Disable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffsetClamp(0, 0, 0);
                GL.Enable(EnableCap.CullFace);
            }
            else
            {
                GL.DrawElements(PrimitiveType.TrianglesAdjacency, indexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }
    }
}
