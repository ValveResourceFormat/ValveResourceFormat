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

        public void SelectNode(SceneNode? node)
        {
            selectedNodes.Clear();

            if (node == null)
            {
                vertexCount = 0;
                return;
            }

            selectedNodes.Add(node);

            UpdateBuffer();
        }

        public void DisableSelectedNodes()
        {
            foreach (var node in selectedNodes)
            {
                node.LayerEnabled = !node.LayerEnabled;
                node.Scene.UpdateOctrees();
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

                RemoveLightProbeDebugGrid();

                if (debugLightProbes && node.LightProbeBinding is not null)
                {
                    OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.LightProbeBinding.Transform, node.LightProbeBinding.LocalBoundingBox, new(1.0f, 0.0f, 1.0f, 1.0f));
                    OctreeDebugRenderer<SceneNode>.AddLine(vertices, node.LightProbeBinding.Transform.Translation, node.BoundingBox.Center, new(1.0f, 0.0f, 1.0f, 1.0f));
                    node.LightProbeBinding.DebugGridSpheres.ForEach(sphere => sphere.LayerEnabled = true);
                    node.LightProbeBinding.Scene.UpdateOctrees();
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
            Scene.LightingInfo.LightProbes.ForEach(probe => probe.DebugGridSpheres.ForEach(sphere => sphere.LayerEnabled = false));
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
            GL.UseProgram(shader.Program);

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

                var position = node.BoundingBox.Center;
                position.Z = node.BoundingBox.Max.Z;

                textRenderer.RenderTextBillboard(context.Camera, position, 20f, Vector4.One, name, center: true);
            }
        }

        public override void SetRenderMode(string mode)
        {
            debugCubeMaps = mode == "Cubemaps";
            debugLightProbes = mode == "Irradiance" || mode == "Illumination";

            UpdateBuffer();
        }
    }
}
