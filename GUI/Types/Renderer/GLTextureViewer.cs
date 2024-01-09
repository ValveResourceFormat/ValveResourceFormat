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
            public float TextureScale = 1f;

            public float ModifyZoom(bool increase)
            {
                if (increase)
                {
                    TextureScale *= 1.25f;
                }
                else
                {
                    TextureScale /= 1.25f;
                }

                TextureScale = Math.Clamp(TextureScale, 0.1f, 50f);

                return TextureScale;
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
            GLControl.MouseDown += OnMouseDown;
            GLControl.MouseMove += OnMouseMove;
            GLControl.MouseUp += OnMouseUp;
            //GLControl.MouseWheel += OnMouseWheel;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GLPaint -= OnPaint;
            }

            base.Dispose(disposing);
        }

        private Vector2? ClickPosition;
        private Vector2 Position;

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ClickPosition == null)
            {
                return;
            }

            var p = new Vector2(e.Location.X, e.Location.Y);

            Position = ClickPosition.Value - p;
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            ClickPosition = new Vector2(e.Location.X, e.Location.Y);
        }

        private void OnMouseUp(object sender, MouseEventArgs mouseEventArgs)
        {
            ClickPosition = null;
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
            // TODO: Remove this later
            void Hotload(object s, System.IO.FileSystemEventArgs e)
            {
                if (e.FullPath.EndsWith(".TMP", StringComparison.Ordinal))
                {
                    return;
                }

                GuiContext.ShaderLoader.ClearCache();

                shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_decode", arguments);
            }

            GuiContext.ShaderLoader.ShaderWatcher.SynchronizingObject = this;
            GuiContext.ShaderLoader.ShaderWatcher.Changed += Hotload;
            GuiContext.ShaderLoader.ShaderWatcher.Created += Hotload;
            GuiContext.ShaderLoader.ShaderWatcher.Renamed += Hotload;
#endif
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();

            GL.UseProgram(shader.Program);

            //shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographic(1f, 1f, 0, 1));
            shader.SetUniform1("g_bTextureViewer", 1u);
            shader.SetUniform2("g_vViewportSize", new Vector2(MainFramebuffer.Width, MainFramebuffer.Height));
            shader.SetUniform2("g_vViewportPosition", Position);
            shader.SetUniform1("g_flScale", TextureCamera.TextureScale);

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
