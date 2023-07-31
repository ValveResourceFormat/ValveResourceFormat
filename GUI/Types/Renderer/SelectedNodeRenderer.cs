using System.Collections.Generic;
using System.Numerics;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SelectedNodeRenderer : SceneNode
    {
        private Shader shader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private bool enableDepth;
        private bool debugCubeMaps;
        private readonly List<SceneNode> selectedNodes = new(1);

        public bool UpdateEveryFrame { get; set; }

        public SelectedNodeRenderer(Scene scene) : base(scene)
        {
            shader = scene.GuiContext.ShaderLoader.LoadShader("vrf.grid");
            GL.UseProgram(shader.Program);

            vboHandle = GL.GenBuffer();

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);

            const int stride = sizeof(float) * 7;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);

            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            GL.BindVertexArray(0);
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

        public void SelectNode(SceneNode node)
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

        private void UpdateBuffer()
        {
            enableDepth = selectedNodes.Count == 1;

            var vertices = new List<float>();

            foreach (var node in selectedNodes)
            {
                OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.Transform, node.LocalBoundingBox, 1.0f, 1.0f, 0.0f, 1.0f);

                if (debugCubeMaps)
                {
                    foreach (var envMap in node.EnvMaps)
                    {
                        OctreeDebugRenderer<SceneNode>.AddBox(vertices, envMap.Transform, envMap.LocalBoundingBox, 0.7f, 0.0f, 1.0f, 1.0f);
                        OctreeDebugRenderer<SceneNode>.AddLine(vertices, envMap.Transform.Translation, node.BoundingBox.Center, 1.0f, 0.0f, 0.0f, 1.0f);
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
                                node.EntityData.GetProperty<Vector3>("box_mins"),
                                node.EntityData.GetProperty<Vector3>("box_maxs")
                            );
                        }

                        OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.Transform, bounds, 0.0f, 1.0f, 0.0f, 1.0f);

                        enableDepth = false;
                    }
                }
            }

            vertexCount = vertices.Count / 7;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
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
            if (vertexCount == 0 || context.RenderPass != RenderPass.Both)
            {
                return;
            }

            GL.Enable(EnableCap.Blend);

            if (enableDepth)
            {
                GL.Enable(EnableCap.DepthTest);
            }

            GL.DepthMask(false);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.UseProgram(shader.Program);

            shader.SetUniform4x4("uProjectionViewMatrix", context.Camera.ViewProjectionMatrix);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
        }

        public override void SetRenderMode(string mode)
        {
            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.grid");

            debugCubeMaps = mode == "Cubemaps";
        }
    }
}
