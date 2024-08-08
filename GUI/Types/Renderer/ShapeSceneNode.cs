using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    abstract class ShapeSceneNode : SceneNode
    {
        public virtual bool IsTranslucent { get; } = true;
        // sphere/capsule constants
        public const int SphereSegments = 8;
        public const int SphereBands = 5;

        protected Shader shader;
        protected virtual bool Shaded { get; } = true;
        protected RenderTexture ToolTexture;
        protected int indexCount;
        protected int vaoHandle;
        private bool IsTranslucentRenderMode = true;

        public ShapeSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds)
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
            var verts = new List<SimpleVertexNormal>();
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
            var verts = new List<SimpleVertexNormal>();
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
            var verts = new List<SimpleVertexNormal>();
            AddSphere(verts, inds, center, radius, color);

            LocalBoundingBox = new AABB(new Vector3(radius), new Vector3(-radius));

            Init(verts, inds);
        }

        public override void SetRenderMode(string mode)
        {
            IsTranslucentRenderMode = mode is not "Color" and not "Normals" and not "VertexColor";
        }

        private void Init(List<SimpleVertexNormal> verts, List<int> inds)
        {
            indexCount = inds.Count;
            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.basic_shape");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.CreateBuffers(1, out int iboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertexNormal.SizeInBytes);
            GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
            SimpleVertexNormal.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, verts.Count * SimpleVertexNormal.SizeInBytes, verts.ToArray(), BufferUsageHint.StaticDraw);
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

                // second sphere has indices in reverse order (since its rotated the other way)
                var c = baseVert1 + (SphereSegments - segment) % SphereSegments;
                var d = baseVert1 + (SphereSegments - (segment + 1)) % SphereSegments;

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

        protected static void AddHemisphere(List<SimpleVertexNormal> verts, List<int> inds, Vector3 center, float radius, Vector3 up, Color32 color)
        {
            var baseVertex = verts.Count;

            var axisUp = Vector3.Normalize(up);
            var axisAround = GetOrtogonal(up);
            var v = GetOrtogonal(axisAround, axisUp);

            // generate vertices
            for (var band = 0; band < SphereBands; band++)
            {
                var angleUp = -MathUtils.ToRadians(band * (90.0f / SphereBands));
                var quatUp = Quaternion.CreateFromAxisAngle(axisAround, angleUp);

                for (var segment = 0; segment < SphereSegments; segment++)
                {
                    var angleAround = MathUtils.ToRadians(segment * (360.0f / SphereSegments));
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

        protected static void AddBox(List<SimpleVertexNormal> verts, List<int> inds, Vector3 minBounds, Vector3 maxBounds, Color32 color)
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

        protected static void AddSphere(List<SimpleVertexNormal> verts, List<int> inds, Vector3 center, float radius, Color32 color)
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
            var translucentPass = IsTranslucent && IsTranslucentRenderMode;

            if (translucentPass)
            {
                if (context.RenderPass != RenderPass.Translucent)
                {
                    return;
                }
            }
            else if (context.RenderPass != RenderPass.AfterOpaque)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            renderShader.SetUniform1("g_bNormalShaded", Shaded);
            renderShader.SetUniform1("g_bTriplanarMapping", ToolTexture != null);

            if (ToolTexture != null)
            {
                renderShader.SetTexture(0, "g_tColor", ToolTexture);
            }

            GL.BindVertexArray(vaoHandle);

            if (translucentPass)
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

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;
    }
}
