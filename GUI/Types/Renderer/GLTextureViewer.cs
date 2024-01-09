using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Forms;
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

            using (texture.BindingContext())
            {
                texture.SetWrapMode(TextureWrapMode.ClampToBorder);
                texture.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Nearest);
            }

            var textureType = "TYPE_" + texture.Target.ToString().ToUpperInvariant();

            var arguments = new Dictionary<string, byte>
            {
                [textureType] = 1,
            };

            shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_viewer", arguments);

            MainFramebuffer.ClearColor = OpenTK.Graphics.Color4.Green;
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace); // TODO: the triangle should be frontfacing?

            GLLoad -= OnLoad;
            GLPaint += OnPaint;

#if DEBUG
            var button = new Button
            {
                Text = "Reload shader",
                AutoSize = true,
            };

            button.Click += (_, _) =>
            {
                GuiContext.ShaderLoader.ClearCache();

                shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_viewer", arguments);
            };

            AddControl(button);
#endif
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();

            GL.UseProgram(shader.Program);

            texture.Bind();

            //shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographic(1f, 1f, 0, 1));
            shader.SetUniform4x4("transform", Matrix4x4.Identity);

            shader.SetTexture(0, "g_tInputTexture", texture);
            shader.SetUniform1("g_bMaintainAspectRatio", 1);
            shader.SetUniform2("g_vViewportSize", new Vector2(MainFramebuffer.Width, MainFramebuffer.Height));
            shader.SetUniform4("g_vInputTextureSize", new Vector4(
                texture.Width, texture.Height, texture.Depth, texture.NumMipLevels
            ));
            shader.SetUniform1("g_nSelectedMip", 0);
            shader.SetUniform1("g_nSelectedDepth", 0);
            shader.SetUniform1("g_nChannelMapping", ChannelMapping.RGBA.PackedValue);
            shader.SetUniform1("g_fZoomScale", (float)Camera.CurrentSpeedModifier);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            //GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            texture.Unbind();

            GL.UseProgram(0);
        }
    }
}
