using System.Globalization;
using System.Linq;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SelectedNodeRenderer : SceneNode
    {
        private readonly GLSceneViewer viewer;
        private readonly Shader shader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private bool disableDepth;
        private bool debugCubeMaps;
        private bool debugLightProbes;
        private readonly List<SceneNode> selectedNodes = new(1);
        private readonly List<SimpleVertex> vertices = new(48);

        private readonly Vector2 SelectedNodeNameOffset = new(0, -20);
        public string ScreenDebugText { get; set; } = string.Empty;

        public SelectedNodeRenderer(GLSceneViewer viewer, Scene scene) : base(scene)
        {
            this.viewer = viewer;
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

        private void AddBox(List<SimpleVertex> vertices, in Matrix4x4 transform, in AABB box, Color32 color, bool showSize = false)
        {
            // Adding a box will add many vertices, so ensure the required capacity for it up front
            vertices.EnsureCapacity(vertices.Count + 2 * 12);

            ReadOnlySpan<Vector3> c =
            [
                Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Min.Z), transform),
                Vector3.Transform(new Vector3(box.Min.X, box.Min.Y, box.Max.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Min.Y, box.Max.Z), transform),
                Vector3.Transform(new Vector3(box.Max.X, box.Max.Y, box.Max.Z), transform),
                Vector3.Transform(new Vector3(box.Min.X, box.Max.Y, box.Max.Z), transform),
            ];

            ReadOnlySpan<(int Start, int End)> Lines =
            [
                (0, 1), (1, 2), (2, 3), (3, 0), // Bottom face
                (4, 5), (5, 6), (6, 7), (7, 4), // Top face
                (0, 4), (1, 5), (2, 6), (3, 7), // Vertical edges
            ];

            int ClosestVertexInView(ReadOnlySpan<Vector3> vertices)
            {
                var minDistance = float.MaxValue;
                var closestIndex = -1;

                for (var i = 0; i < vertices.Length; i++)
                {
                    if (viewer.Camera.ViewFrustum.Intersects(vertices[i]))
                    {
                        var distance = Vector3.DistanceSquared(vertices[i], viewer.Camera.Location);
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestIndex = i;
                        }
                    }
                }

                return closestIndex;
            }

            var closestIndex = showSize ? ClosestVertexInView(c) : -1;

            for (var i = 0; i < Lines.Length; i++)
            {
                var line = Lines[i];

                if (closestIndex == line.Start || closestIndex == line.End)
                {
                    var axis = i >= 8 ? 2 : i % 2;

                    var axisColor = axis switch
                    {
                        0 => new Color32(0.8f, 0.2f, 0.2f, 1),
                        1 => new Color32(0.2f, 0.8f, 0.2f, 1),
                        2 => new Color32(0.2f, 0.2f, 0.8f, 1),
                        _ => color,
                    };

                    var (v0, v1) = (c[line.Start], c[line.End]);
                    var length = Vector3.Distance(v0, v1);

                    viewer.TextRenderer.AddTextBillboard(Vector3.Lerp(v0, v1, 0.5f), new TextRenderer.TextRenderRequest
                    {
                        Scale = 12f,
                        Color = axisColor,
                        Text = length.ToString("0.##", CultureInfo.InvariantCulture),
                        Center = true
                    }, fixedScale: false);

                    OctreeDebugRenderer<SceneNode>.AddLine(vertices, c[line.Start], c[line.End], axisColor);
                    continue;
                }

                OctreeDebugRenderer<SceneNode>.AddLine(vertices, c[line.Start], c[line.End], color);
            }
        }

        public override void Update(Scene.UpdateContext context)
        {
            disableDepth = selectedNodes.Count > 1;

            if (selectedNodes.Count == 0)
            {
                // We don't need to reupload an empty array
                vertexCount = 0;
                return;
            }

            foreach (var node in selectedNodes)
            {
                var nodeName = node.Name ?? node.GetType().Name;
                AddBox(vertices, node.Transform, node.LocalBoundingBox, Color32.Yellow, showSize: true);

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
                        AddBox(vertices, tiedEnvMap.Transform, tiedEnvMap.LocalBoundingBox, new(0.7f, 0.0f, 1.0f, 1.0f));

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
                    AddBox(vertices, node.LightProbeBinding.Transform, node.LightProbeBinding.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
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
                    nodeName = classname;

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

                        AddBox(vertices, node.Transform, bounds, new(0.0f, 1.0f, 0.0f, 1.0f));

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

                        AddBox(vertices, Matrix4x4.Identity, bounds, new(0.0f, 1.0f, 0.0f, 1.0f));

                        OctreeDebugRenderer<SceneNode>.AddLine(vertices, node.Transform.Translation, origin, new(0.0f, 0.0f, 1.0f, 1.0f));
                        OctreeDebugRenderer<SceneNode>.AddLine(vertices, node.Transform.Translation, extent, new(1.0f, 1.0f, 0.0f, 1.0f));

                        disableDepth = true;
                    }
                }

                // draw node name above the bounding box
                var position = node.BoundingBox.Center;
                position.Z = node.BoundingBox.Max.Z;

                viewer.TextRenderer.AddTextBillboard(position, new TextRenderer.TextRenderRequest
                {
                    Scale = 20f,
                    Text = nodeName,
                    Center = true,
                    TextOffset = SelectedNodeNameOffset
                }, fixedScale: false);
            }

            if (ScreenDebugText.Length > 0)
            {
                viewer.TextRenderer.AddTextRelative(new TextRenderer.TextRenderRequest
                {
                    X = 0.005f,
                    Y = 0.03f,
                    Scale = 14f,
                    Text = ScreenDebugText,
                });
            }

            vertexCount = vertices.Count;

            GL.NamedBufferData(vboHandle, vertexCount * SimpleVertex.SizeInBytes, ListAccessors<SimpleVertex>.GetBackingArray(vertices), BufferUsageHint.DynamicDraw);

            vertices.Clear();
        }

        private void RemoveLightProbeDebugGrid()
        {
            Scene.LightingInfo.LightProbes.ForEach(probe => probe.RemoveDebugGridSpheres());
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
        }

        public override void SetRenderMode(string mode)
        {
            debugCubeMaps = mode == "Cubemaps";
            debugLightProbes = mode is "Irradiance" or "Illumination";

            RemoveLightProbeDebugGrid();
        }
    }
}
