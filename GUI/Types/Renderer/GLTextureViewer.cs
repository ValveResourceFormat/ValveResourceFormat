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
        public class TextureViewerCamera : Camera
        {
            public float ZoomLevel = 1f;

            public float ModifyZoom(bool increase)
            {
                if (increase)
                {
                    ZoomLevel *= 1.25f;
                }
                else
                {
                    ZoomLevel /= 1.25f;
                }

                ZoomLevel = Math.Clamp(ZoomLevel, 0.1f, 50f);

                return ZoomLevel;
            }
        }

        private readonly VrfGuiContext GuiContext;
        private readonly ValveResourceFormat.Resource Resource;
        private readonly TextureViewerCamera TextureCamera;
        private RenderTexture texture;
        private Shader shader;
        private int vao;

        public GLTextureViewer(VrfGuiContext guiContext, ValveResourceFormat.Resource resource) : base()
        {
            GuiContext = guiContext;
            Resource = resource;

            TextureCamera = new TextureViewerCamera();
            Camera = TextureCamera;

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

            shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_decode", arguments);
            vao = GL.GenVertexArray();

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

                shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_decode", arguments);
            };

            AddControl(button);
#endif
        }

        public static ChannelMapping RGB_AlphaSeparate = ChannelMapping.FromChannels(
            ChannelMapping.Channel.R,
            ChannelMapping.Channel.G,
            ChannelMapping.Channel.B,
            0x1C
        );

        private void OnPaint(object sender, RenderEventArgs e)
        {
            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();

            GL.UseProgram(shader.Program);

            //shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographic(1f, 1f, 0, 1));
            shader.SetUniform1("g_bTextureViewer", 1u);
            shader.SetUniform2("g_vViewportSize", new Vector2(MainFramebuffer.Width, MainFramebuffer.Height));
            shader.SetUniform1("g_fZoomScale", TextureCamera.ZoomLevel);

            shader.SetTexture(0, "g_tInputTexture", texture);
            shader.SetUniform4("g_vInputTextureSize", new Vector4(texture.Width, texture.Height, texture.Depth, texture.NumMipLevels));
            shader.SetUniform1("g_nSelectedMip", 0);
            shader.SetUniform1("g_nSelectedDepth", 0);
            shader.SetUniform1("g_nSelectedChannels", ChannelMapping.RGBA.PackedValue);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }
}
