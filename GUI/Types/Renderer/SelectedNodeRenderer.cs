using System.Globalization;
using System.Linq;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SelectedNodeRenderer : SceneNode
    {
        private readonly Shader shader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private bool disableDepth;
        private bool debugCubeMaps;
        private bool debugLightProbes;
        private readonly List<SceneNode> selectedNodes = new(1);
        private readonly TextRenderer textRenderer;

        private readonly Vector2 SelectedNodeNameOffset = new Vector2(0, -20);

        public bool UpdateEveryFrame { get; set; }

        public SelectedNodeRenderer(Scene scene, TextRenderer textRenderer) : base(scene)
        {
            this.textRenderer = textRenderer;
            shader = scene.GuiContext.ShaderLoader.LoadShader("vrf.default");

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, SimpleVertex.SizeInBytes);
            SimpleVertex.BindDefaultShaderLayout(vaoHandle, shader.Program);

#if DEBUG
            var vaoLabel = nameof(SelectedNodeRenderer);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public struct EdgeInfo
        {
            public int Axis;
            public Vector3 StartPos;
            public Vector3 EndPos;
            public float Length;

            public EdgeInfo(int axis, Vector3 start, Vector3 end)
            {
                Axis = axis;
                StartPos = start;
                EndPos = end;
                Length = Vector3.Distance(start, end);
            }
        }

        public class VertexInfo
        {
            public Vector3 Position;
            public EdgeInfo[] Edges;

            public VertexInfo(Vector3 position, EdgeInfo[] edges)
            {
                Position = position;
                Edges = edges;
            }
        }

        public static VertexInfo[] GetAABBVertices(AABB AABB, Matrix4x4 transform)
        {
            var minPoint = AABB.Min;
            var maxPoint = AABB.Max;

            var modelAABBVertices = new VertexInfo[8];

            var vertices = new Vector3[8];
            vertices[0] = new Vector3(minPoint.X, minPoint.Y, minPoint.Z);
            vertices[1] = new Vector3(maxPoint.X, minPoint.Y, minPoint.Z);
            vertices[2] = new Vector3(minPoint.X, maxPoint.Y, minPoint.Z);
            vertices[3] = new Vector3(maxPoint.X, maxPoint.Y, minPoint.Z);
            vertices[4] = new Vector3(minPoint.X, minPoint.Y, maxPoint.Z);
            vertices[5] = new Vector3(maxPoint.X, minPoint.Y, maxPoint.Z);
            vertices[6] = new Vector3(minPoint.X, maxPoint.Y, maxPoint.Z);
            vertices[7] = new Vector3(maxPoint.X, maxPoint.Y, maxPoint.Z);

            for (int i = 0; i < 8; i++)
            {
                var vertex = vertices[i];
                var transformedVertex = Vector3.Transform(vertex, transform);

                var edges = new EdgeInfo[3];
                for (int axis = 0; axis < 3; axis++)
                {
                    var endVertex = vertex;

                    if (Math.Abs(vertex[axis] - minPoint[axis]) < Math.Abs(vertex[axis] - maxPoint[axis]))
                    {
                        endVertex[axis] = maxPoint[axis];
                    }
                    else
                    {
                        endVertex[axis] = minPoint[axis];
                    }

                    edges[axis] = new EdgeInfo(axis, transformedVertex, Vector3.Transform(endVertex, transform));
                }

                modelAABBVertices[i] = new VertexInfo(transformedVertex, edges);
            }

            return modelAABBVertices;
        }

        public static VertexInfo GetClosestVertexInfo(Camera camera, VertexInfo[] vertexInfos)
        {
            float minDistance = float.MaxValue;
            int closestIndex = 0;

            for (int i = 0; i < 8; i++)
            {
                var vertexPos = vertexInfos[i].Position;

                if (!camera.ViewFrustum.Intersects(vertexPos))
                {
                    continue;
                }

                float distance = Vector3.DistanceSquared(vertexPos, camera.Location);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestIndex = i;
                }
            }

            return vertexInfos[closestIndex];
        }

        private Dictionary<SceneNode, VertexInfo[]> NodeVertexInfos = [];
        private Dictionary<SceneNode, VertexInfo> NodeClosestVertexInfo = [];

        public void ToggleNode(SceneNode node)
        {
            var selectedNode = selectedNodes.IndexOf(node);

            if (selectedNode >= 0)
            {
                selectedNodes.RemoveAt(selectedNode);
            }
            else
            {
                selectedNodes.Add(node);
            }

            UpdateBuffer();
        }

        public void SelectNode(SceneNode? node, bool forceDisableDepth = false)
        {
            selectedNodes.Clear();

            if (node == null)
            {
                RemoveLightProbeDebugGrid();
                vertexCount = 0;
                return;
            }

            selectedNodes.Add(node);

            UpdateBuffer();

            if (forceDisableDepth)
            {
                disableDepth = true;
            }
        }

        public void DisableSelectedNodes()
        {
            foreach (var node in selectedNodes)
            {
                node.LayerEnabled = !node.LayerEnabled;
            }
        }

        private void UpdateBuffer()
        {
            disableDepth = selectedNodes.Count > 1;

            if (selectedNodes.Count == 0)
            {
                // We don't need to reupload an empty array
                vertexCount = 0;
                return;
            }

            var vertices = new List<SimpleVertex>();

            foreach (var node in selectedNodes)
            {
                OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.Transform, node.LocalBoundingBox, new(1.0f, 1.0f, 0.0f, 1.0f));

                NodeClosestVertexInfo.TryGetValue(node, out var vertexInfo);

                if (vertexInfo != null)
                {
                    foreach (var edge in vertexInfo.Edges)
                    {
                        switch (edge.Axis)
                        {
                            case 0:
                                OctreeDebugRenderer<SceneNode>.AddLine(vertices, edge.StartPos, edge.EndPos, new(1.0f, 0.0f, 0.0f, 1.0f));
                                break;
                            case 1:
                                OctreeDebugRenderer<SceneNode>.AddLine(vertices, edge.StartPos, edge.EndPos, new(0.0f, 1.0f, 0.0f, 1.0f));
                                break;
                            case 2:
                                OctreeDebugRenderer<SceneNode>.AddLine(vertices, edge.StartPos, edge.EndPos, new(0.0f, 0.0f, 1.0f, 1.0f));
                                break;
                            default:
                                break;
                        }
                    }
                }

                if (debugCubeMaps && node.EnvMapIds != null)
                {
                    var tiedEnvmaps = Scene.LightingInfo.CubemapType switch
                    {
                        Scene.CubemapType.CubemapArray => node.EnvMapIds.Select(id => Scene.LightingInfo.EnvMaps[id]),
                        _ => node.EnvMaps
                    };

                    var i = 0;

                    foreach (var tiedEnvMap in tiedEnvmaps)
                    {
                        OctreeDebugRenderer<SceneNode>.AddBox(vertices, tiedEnvMap.Transform, tiedEnvMap.LocalBoundingBox, new(0.7f, 0.0f, 1.0f, 1.0f));

                        if (Scene.LightingInfo.CubemapType is Scene.CubemapType.IndividualCubemaps && i == 0)
                        {
                            OctreeDebugRenderer<SceneNode>.AddLine(vertices, tiedEnvMap.Transform.Translation, node.BoundingBox.Center, new(0.0f, 1.0f, 0.0f, 1.0f));
                            i++;
                            continue;
                        }

                        var fractionToTen = (float)i / 10;
                        var color = new Utils.Color32(1.0f, fractionToTen, fractionToTen, 1.0f);
                        OctreeDebugRenderer<SceneNode>.AddLine(vertices, tiedEnvMap.Transform.Translation, node.BoundingBox.Center, color);
                        i++;
                    }
                }

                if (debugLightProbes && node.LightProbeBinding is not null)
                {
                    OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.LightProbeBinding.Transform, node.LightProbeBinding.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
                    OctreeDebugRenderer<SceneNode>.AddLine(vertices, node.LightProbeBinding.Transform.Translation, node.BoundingBox.Center, new(1.0f, 0.0f, 1.0f, 1.0f));

                    if (Scene.LightingInfo.LightingData.IsSkybox == 0u)
                    {
                        RemoveLightProbeDebugGrid();
                        node.LightProbeBinding.CrateDebugGridSpheres();
                    }
                }

                if (node.EntityData != null)
                {
                    var classname = node.EntityData.GetProperty<string>("classname");

                    if (classname is "env_combined_light_probe_volume" or "env_light_probe_volume" or "env_cubemap_box" or "env_cubemap")
                    {
                        AABB bounds = default;

                        if (classname == "env_cubemap")
                        {
                            var radius = node.EntityData.GetProperty<float>("influenceradius");
                            bounds = new AABB(-radius, -radius, -radius, radius, radius, radius);
                        }
                        else
                        {
                            bounds = new AABB(
                                node.EntityData.GetVector3Property("box_mins"),
                                node.EntityData.GetVector3Property("box_maxs")
                            );
                        }

                        OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.Transform, bounds, new(0.0f, 1.0f, 0.0f, 1.0f));

                        disableDepth = true;
                    }
                    else if (classname is "light_barn" or "light_omni2")
                    {
                        var bounds = new AABB(
                            EntityTransformHelper.ParseVector(node.EntityData.GetProperty<string>("precomputedboundsmins")),
                            EntityTransformHelper.ParseVector(node.EntityData.GetProperty<string>("precomputedboundsmaxs"))
                        );

                        var origin = EntityTransformHelper.ParseVector(node.EntityData.GetProperty<string>("precomputedobbextent"));
                        var extent = EntityTransformHelper.ParseVector(node.EntityData.GetProperty<string>("precomputedobborigin"));

                        OctreeDebugRenderer<SceneNode>.AddBox(vertices, Matrix4x4.Identity, bounds, new(0.0f, 1.0f, 0.0f, 1.0f));

                        OctreeDebugRenderer<SceneNode>.AddLine(vertices, node.Transform.Translation, origin, new(0.0f, 0.0f, 1.0f, 1.0f));
                        OctreeDebugRenderer<SceneNode>.AddLine(vertices, node.Transform.Translation, extent, new(1.0f, 1.0f, 0.0f, 1.0f));

                        disableDepth = true;
                    }
                }
            }

            vertexCount = vertices.Count;

            GL.NamedBufferData(vboHandle, vertices.Count * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.StaticDraw);
        }

        private void RemoveLightProbeDebugGrid()
        {
            Scene.LightingInfo.LightProbes.ForEach(probe => probe.RemoveDebugGridSpheres());
        }

        public override void Update(Scene.UpdateContext context)
        {
            if (UpdateEveryFrame)
            {
                UpdateBuffer();
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (vertexCount == 0)
            {
                return;
            }

            GL.Enable(EnableCap.Blend);

            if (disableDepth)
            {
                GL.Disable(EnableCap.DepthTest);
            }

            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            shader.SetUniform4x4("transform", Matrix4x4.Identity);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.DepthTest);

            foreach (var node in selectedNodes)
            {
                NodeClosestVertexInfo.TryGetValue(node, out var closestVertexInfo);
                NodeVertexInfos.TryGetValue(node, out var vertexInfos);

                if (vertexInfos == null)
                {
                    vertexInfos = GetAABBVertices(node.LocalBoundingBox, node.Transform);

                    NodeVertexInfos.Add(node, vertexInfos);
                    UpdateBuffer();
                }

                if (closestVertexInfo == null)
                {
                    closestVertexInfo = GetClosestVertexInfo(context.Camera, vertexInfos);

                    NodeClosestVertexInfo.Add(node, closestVertexInfo);
                    UpdateBuffer();
                }

                var newClosestInfo = GetClosestVertexInfo(context.Camera, vertexInfos);

                if (closestVertexInfo != newClosestInfo)
                {
                    NodeClosestVertexInfo[node] = newClosestInfo;
                    UpdateBuffer();
                }

                string name;

                if (node.EntityData != null)
                {
                    name = node.EntityData.GetProperty<string>("classname");
                }
                else if (!string.IsNullOrEmpty(node.Name))
                {
                    name = node.Name;
                }
                else
                {
                    name = node.GetType().Name;
                }

                foreach (var edge in closestVertexInfo.Edges)
                {
                    Vector4 edgeColor = edge.Axis switch
                    {
                        0 => edgeColor = new Vector4(0.8f, 0.2f, 0.2f, 1),
                        1 => edgeColor = new Vector4(0.2f, 0.8f, 0.2f, 1),
                        2 => edgeColor = new Vector4(0.2f, 0.2f, 0.8f, 1),
                        _ => edgeColor = Vector4.Zero
                    };

                    textRenderer.AddTextBillboard(context.Camera, Vector3.Lerp(edge.StartPos, edge.EndPos, 0.5f), new TextRenderer.TextRenderRequest
                    {
                        Scale = 14f,
                        Color = edgeColor,
                        Text = edge.Length.ToString("0.##", CultureInfo.InvariantCulture),
                        Center = true
                    });
                }

                var position = node.BoundingBox.Center;
                position.Z = node.BoundingBox.Max.Z;

                textRenderer.AddTextBillboard(context.Camera, position, new TextRenderer.TextRenderRequest
                {
                    Scale = 20f,
                    Text = name,
                    Center = true,
                    TextOffset = SelectedNodeNameOffset
                });
            }
        }

        public override void SetRenderMode(string mode)
        {
            debugCubeMaps = mode == "Cubemaps";
            debugLightProbes = mode == "Irradiance" || mode == "Illumination";

            RemoveLightProbeDebugGrid();
            UpdateBuffer();
        }
    }
}
