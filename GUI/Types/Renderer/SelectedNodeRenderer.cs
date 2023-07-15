using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SelectedNodeRenderer
    {
        private readonly Shader shader;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private bool enableDepth;

        public SelectedNodeRenderer(VrfGuiContext guiContext)
        {
            shader = shader = guiContext.ShaderLoader.LoadShader("vrf.grid");
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

        public void SelectNode(SceneNode node)
        {
            if (node == null)
            {
                vertexCount = 0;
                return;
            }

            enableDepth = true;

            var vertices = new List<float>();
            OctreeDebugRenderer<SceneNode>.AddBox(vertices, node.BoundingBox, 1.0f, 1.0f, 0.0f, 1.0f);

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

                    bounds = bounds.Transform(node.Transform);

                    OctreeDebugRenderer<SceneNode>.AddBox(vertices, bounds, 0.0f, 1.0f, 0.0f, 1.0f);

                    enableDepth = false;
                }
            }

            vertexCount = vertices.Count / 7;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            if (vertexCount == 0 || renderPass != RenderPass.Both)
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

            shader.SetUniform4x4("uProjectionViewMatrix", camera.ViewProjectionMatrix);

            GL.BindVertexArray(vaoHandle);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);
        }
    }
}
