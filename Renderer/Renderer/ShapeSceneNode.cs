using System.Diagnostics;
using System.Reflection;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    public abstract class ShapeSceneNode : SceneNode
    {
        public virtual bool IsTranslucent { get; } = true;
        public bool IsTranslucentRenderMode { get; set; } = true;

        // sphere/capsule constants
        private const int SphereSegments = 8;
        private const int SphereBands = 5;
        // constants for sizes of spheres/capsules
        public const int HemisphereVerts = SphereBands * SphereSegments + 1;
        public const int HemisphereTriangles = SphereSegments * (2 * SphereBands - 1);
        public const int CapsuleTriangles = 2 * HemisphereTriangles + 2 * SphereSegments;

        protected Shader shader { get; init; }
        protected int indexCount { get; private set; }
        protected int vaoHandle { get; private set; }
        protected virtual bool Shaded { get; } = true;
        protected RenderTexture? ToolTexture { get; set; }

        private ShapeSceneNode(Scene scene) : base(scene)
        {
            shader = Scene.RendererContext.ShaderLoader.LoadShader("vrf.basic_shape");
        }

        internal ShapeSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : this(scene)
        {
            Init(verts, inds);
        }

        /// <summary>
        /// Constructs a node with a single box shape
        /// </summary>
        internal ShapeSceneNode(Scene scene, Vector3 minBounds, Vector3 maxBounds, Color32 color) : this(scene)
        {
            var verts = new List<SimpleVertexNormal>(8);
            var inds = new List<int>(8 * 9);
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

            GL.CreateVertexArrays(1, out int vaoHandleLocal);
            vaoHandle = vaoHandleLocal;
            GL.CreateBuffers(1, out int vboHandle);
            GL.CreateBuffers(1, out int iboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertexNormal.SizeInBytes);
            GL.VertexArrayElementBuffer(vaoHandle, iboHandle);
            SimpleVertexNormal.BindDefaultShaderLayout(vaoHandle, shader.Program);

            GL.NamedBufferData(vboHandle, verts.Count * SimpleVertexNormal.SizeInBytes, ListAccessors<SimpleVertexNormal>.GetBackingArray(verts), BufferUsageHint.StaticDraw);
            GL.NamedBufferData(iboHandle, inds.Count * sizeof(int), ListAccessors<int>.GetBackingArray(inds), BufferUsageHint.StaticDraw);

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
            Span<SimpleVertexNormal> boxVertices =
            [
                new(new(minBounds.X, minBounds.Y, minBounds.Z), color),
                new(new(minBounds.X, minBounds.Y, maxBounds.Z), color),
                new(new(minBounds.X, maxBounds.Y, maxBounds.Z), color),
                new(new(minBounds.X, maxBounds.Y, minBounds.Z), color),

                new(new(maxBounds.X, minBounds.Y, minBounds.Z), color),
                new(new(maxBounds.X, minBounds.Y, maxBounds.Z), color),
                new(new(maxBounds.X, maxBounds.Y, maxBounds.Z), color),
                new(new(maxBounds.X, maxBounds.Y, minBounds.Z), color)
            ];

            // calculate box normals
            var center = (minBounds + maxBounds) / 2f;
            for (var i = 0; i < boxVertices.Length; i++)
            {
                var normalFromBoxCenter = Vector3.Normalize(boxVertices[i].Position - center);
                boxVertices[i].Normal = normalFromBoxCenter;
            }

            verts.AddRange(boxVertices);

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
            inds.Add(baseVertex + c);
        }

        public static void AddLine(List<SimpleVertex> vertices, Vector3 from, Vector3 to, Color32 color)
        {
            vertices.Add(new SimpleVertex(from, color));
            vertices.Add(new SimpleVertex(to, color));
        }

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

        public override void Render(Scene.RenderContext context)
        {
            var isTranslucent = IsTranslucent && IsTranslucentRenderMode && context.ReplacementShader == null;
            var renderPass = isTranslucent ? RenderPass.Translucent : RenderPass.Opaque;

            if (context.RenderPass != renderPass && context.RenderPass != RenderPass.Outline)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;
            renderShader.Use();
            renderShader.SetUniform3x4("transform", Transform);
            renderShader.SetBoneAnimationData(false);
            renderShader.SetUniform1("sceneObjectId", Id);

            renderShader.SetUniform1("g_bNormalShaded", Shaded);
            renderShader.SetUniform1("g_bTriplanarMapping", ToolTexture != null);

            if (ToolTexture != null)
            {
                renderShader.SetTexture(0, "g_tColor", ToolTexture);
            }

            GL.BindVertexArray(vaoHandle);

            GL.DepthFunc(DepthFunction.Gequal);

            if (isTranslucent)
            {
                GL.Disable(EnableCap.CullFace);

                // Lines
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);
                GL.Disable(EnableCap.Blend);
                GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

                // Triangles
                GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.Enable(EnableCap.PolygonOffsetLine);
                GL.Enable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffsetClamp(2, 100, 0.05f);

                GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

                GL.Disable(EnableCap.PolygonOffsetLine);
                GL.Disable(EnableCap.PolygonOffsetFill);
                GL.PolygonOffsetClamp(0, 0, 0);
                GL.Enable(EnableCap.CullFace);
            }
            else
            {
                GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);
            }

            GL.DepthFunc(DepthFunction.Greater);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public static Lazy<ValveResourceFormat.Resource> CubemapResource { get; } = new(() =>
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Renderer.Resources.env_cubemap.vmdl_c");
            var resource = new ValveResourceFormat.Resource()
            {
                FileName = "env_cubemap.vmdl_c"
            };

            Debug.Assert(stream != null);
            resource.Read(stream);
            return resource;
        });
    }
}
