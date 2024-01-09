using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

namespace GUI.Types.Renderer
{
    class GLTextureViewer : GLViewerControl, IGLViewer
    {
        private readonly VrfGuiContext GuiContext;
        private readonly ValveResourceFormat.Resource Resource;
        private RenderTexture texture;
        private Shader shader;

        public GLTextureViewer(VrfGuiContext guiContext, ValveResourceFormat.Resource resource) : base()
        {
            GuiContext = guiContext;
            Resource = resource;

            GLLoad += OnLoad;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GLPaint -= OnPaint;
            }

            base.Dispose(disposing);
        }

        private void OnLoad(object sender, EventArgs e)
        {
            texture = GuiContext.MaterialLoader.LoadTexture(Resource);

            var textureType = "TYPE_" + texture.Target.ToString().ToUpperInvariant();

            shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_viewer", new Dictionary<string, byte>
            {
                [textureType] = 1,
            });

            MainFramebuffer.ClearColor = OpenTK.Graphics.Color4.Green;
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace); // TODO: the triangle should be frontfacing?

            GLLoad -= OnLoad;
            GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();

            GL.UseProgram(shader.Program);

            texture.Bind();

            //shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographic(texture.Width, texture.Height, 0, 1));
            shader.SetUniform4x4("transform", Matrix4x4.Identity);

            shader.SetTexture(0, "g_tInputTexture", texture);
            shader.SetUniform4("g_vInputTextureSize", new Vector4(
                texture.Width, texture.Height, texture.Depth, texture.NumMipLevels
            ));
            shader.SetUniform1("g_nSelectedMip", 0);
            shader.SetUniform1("g_nSelectedDepth", 0);
            shader.SetUniform1("g_nChannelMapping", ChannelMapping.RGBA.PackedValue);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            //GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            texture.Unbind();

            GL.UseProgram(0);
        }
    }
}
