using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.NavMesh;

namespace GUI.Types.Renderer
{
    class NavMeshSceneNode : SceneNode
    {
        public static readonly Color32 NavMeshColor = new Color32(64, 32, 255, 100);
        public static readonly Color32 NavMeshLadderColor = new Color32(16, 255, 32, 100);
        protected Shader shader;
        protected int indexCount;
        protected int vaoHandle;

        public NavMeshSceneNode(Scene scene, IEnumerable<NavMeshArea> areas)
            : base(scene)
        {
            List<SimpleVertexNormal> verts = new();
            List<int> inds = new();

            var minBounds = new Vector3(float.MinValue);
            var maxBounds = new Vector3(float.MaxValue);
            foreach (var area in areas)
            {
                AddArea(area, verts, inds, NavMeshColor, ref minBounds, ref maxBounds);
            }

            LocalBoundingBox = new AABB(minBounds, maxBounds);
            Init(verts, inds);
        }

        public NavMeshSceneNode(Scene scene, IEnumerable<NavMeshLadder> ladders)
            : base(scene)
        {
            List<SimpleVertexNormal> verts = new();
            List<int> inds = new();

            var minBounds = new Vector3(float.MinValue);
            var maxBounds = new Vector3(float.MaxValue);
            foreach (var ladder in ladders)
            {
                AddLadder(ladder, verts, inds, NavMeshLadderColor, ref minBounds, ref maxBounds);
            }

            LocalBoundingBox = new AABB(minBounds, maxBounds);
            Init(verts, inds);
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
            var vaoLabel = nameof(NavMeshSceneNode);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        protected static void AddLadder(NavMeshLadder ladder, List<SimpleVertexNormal> verts, List<int> inds, Color32 color, ref Vector3 minBounds, ref Vector3 maxBounds)
        {
            var normal = ladder.Direction switch
            {
                NavDirectionType.North => new Vector3(0, -1, 0),
                NavDirectionType.East => new Vector3(1, 0, 0),
                NavDirectionType.West => new Vector3(-1, 0, 0),
                _ => new Vector3(0, 1, 0),
            };
            var sidewaysVector = Vector3.Cross(normal, Vector3.UnitZ) * (ladder.Width / 2);

            var bottom1 = ladder.Bottom - sidewaysVector;
            var bottom2 = ladder.Bottom + sidewaysVector;
            var top1 = ladder.Top - sidewaysVector;
            var top2 = ladder.Top + sidewaysVector;

            minBounds = Vector3.Min(minBounds, bottom1);
            minBounds = Vector3.Min(minBounds, bottom2);
            maxBounds = Vector3.Max(maxBounds, top1);
            maxBounds = Vector3.Max(maxBounds, top2);

            var firstVertexIndex = verts.Count;

            verts.Add(new(bottom2, color, normal));
            verts.Add(new(bottom1, color, normal));
            verts.Add(new(top1, color, normal));
            verts.Add(new(top2, color, normal));

            for (var i = 0; i < 4; i++)
            {
                inds.Add(firstVertexIndex + i);
            }
            inds.Add(int.MaxValue);
        }

        protected static void AddArea(NavMeshArea area, List<SimpleVertexNormal> verts, List<int> inds, Color32 color, ref Vector3 minBounds, ref Vector3 maxBounds)
        {
            var firstVertexIndex = verts.Count;

            foreach (var position in area.Corners)
            {
                minBounds = Vector3.Min(minBounds, position);
                maxBounds = Vector3.Max(maxBounds, position);

                verts.Add(new(position, color, Vector3.UnitZ));
            }

            for (var i = 0; i < area.Corners.Length; i++)
            {
                inds.Add(firstVertexIndex + i);
            }
            inds.Add(int.MaxValue);
        }

        public override void Render(Scene.RenderContext context)
        {
            if (context.RenderPass != RenderPass.Translucent)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            renderShader.SetUniform1("g_bNormalShaded", true);
            renderShader.SetUniform1("g_bTriplanarMapping", false);

            GL.BindVertexArray(vaoHandle);

            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.Enable(EnableCap.PolygonOffsetLine);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffsetClamp(0, 96, 0.0005f);

            GL.Enable(EnableCap.PrimitiveRestart);
            GL.PrimitiveRestartIndex(int.MaxValue);
            GL.DrawElements(PrimitiveType.LineLoop, indexCount, DrawElementsType.UnsignedInt, 0);

            GL.DrawElements(PrimitiveType.TriangleFan, indexCount, DrawElementsType.UnsignedInt, 0);

            GL.Disable(EnableCap.PrimitiveRestart);
            GL.Disable(EnableCap.PolygonOffsetLine);
            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffsetClamp(0, 0, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => shader.RenderModes;

        public override void Update(Scene.UpdateContext context)
        {
        }

        public static void AddNavNodesToScene(NavMeshFile navMeshFile, Scene scene)
        {
            if (navMeshFile == null || scene == null)
            {
                return;
            }

            if (navMeshFile.GenerationParams != null)
            {
                for (byte i = 0; i < navMeshFile.GenerationParams.HullCount; i++)
                {
                    var hullAreas = navMeshFile.GetHullAreas(i);
                    if (hullAreas == null)
                    {
                        continue;
                    }

                    var sceneNode = new NavMeshSceneNode(scene, hullAreas);
                    sceneNode.LayerName = $"Navigation mesh (hull {i})";
                    scene.Add(sceneNode, false);
                }
            }

            if (navMeshFile.Ladders != null && navMeshFile.Ladders.Length > 0)
            {
                var laddersSceneNode = new NavMeshSceneNode(scene, navMeshFile.Ladders);
                laddersSceneNode.LayerName = "Navigation mesh (ladders)";
                scene.Add(laddersSceneNode, false);
            }
        }
    }
}
