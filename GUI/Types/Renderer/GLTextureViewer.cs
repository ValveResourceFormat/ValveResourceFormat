using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    class GLTextureViewer : GLViewerControl, IGLViewer
    {
        private readonly VrfGuiContext GuiContext;
        private readonly Resource Resource;
        private RenderTexture texture;
        private Shader shader;
        private int vao;

        private Vector2? ClickPosition;
        private Vector2 Position;
        private float TextureScale = 1f;
        private Button ResetButton;
        private bool FirstPaint = true;

        public GLTextureViewer(VrfGuiContext guiContext, Resource resource) : base()
        {
            GuiContext = guiContext;
            Resource = resource;

            GLLoad += OnLoad;
            GLControl.MouseMove += OnMouseMove;

            SetZoomLabel();

            ResetButton = new Button
            {
                Text = "Reset zoom",
                AutoSize = true,
            };

            ResetButton.Click += (_, __) =>
            {
                TextureScale = 1f;
                Position = Vector2.Zero;
                SetZoomLabel();
            };

            AddControl(ResetButton);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GLControl.MouseMove -= OnMouseMove;
                GLPaint -= OnPaint;

                ResetButton.Dispose();
                ResetButton = null;
            }

            base.Dispose(disposing);
        }

        private void SetZoomLabel() => SetMoveSpeedOrZoomLabel($"Zoom: {TextureScale * 100:0.0}% (scroll to change)");

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ClickPosition == null)
            {
                return;
            }

            Position = ClickPosition.Value - new Vector2(e.Location.X, e.Location.Y);
            ClampPosition();
        }

        protected override void OnMouseDown(object sender, MouseEventArgs e)
        {
            ClickPosition = Position + new Vector2(e.Location.X, e.Location.Y);
        }

        protected override void OnMouseUp(object sender, MouseEventArgs mouseEventArgs)
        {
            ClickPosition = null;
        }

        protected override void OnMouseWheel(object sender, MouseEventArgs e)
        {
            var oldScale = TextureScale;

            if (e.Delta < 0)
            {
                TextureScale /= 1.25f;
            }
            else
            {
                TextureScale *= 1.25f;
            }

            TextureScale = Math.Clamp(TextureScale, 0.1f, 50f);

            var pos = new Vector2(e.Location.X, e.Location.Y);
            var posPrev = (pos + Position) / oldScale;
            var posNewScale = posPrev * TextureScale;
            Position = posNewScale - pos;

            ClampPosition();
            SetZoomLabel();
        }

        private void ClampPosition()
        {
            var width = texture.Width * TextureScale;
            var height = texture.Height * TextureScale;

            if (GLControl.Width >= width)
            {
                Position.X = -(GLControl.Width / 2f - width / 2f);
            }
            else
            {
                Position.X = Math.Clamp(Position.X, 0, width - GLControl.Width);
            }

            if (GLControl.Height >= height)
            {
                Position.Y = -(GLControl.Height / 2f - height / 2f);
            }
            else
            {
                Position.Y = Math.Clamp(Position.Y, 0, height - GLControl.Height);
            }
        }

        protected override void OnResize(object sender, EventArgs e)
        {
            base.OnResize(sender, e);

            if (texture != null)
            {
                ClampPosition();
            }
        }

        private void OnLoad(object sender, EventArgs e)
        {
            var textureData = (Texture)Resource.DataBlock;

            if (textureData.IsRawJpeg || textureData.IsRawPng)
            {
                using var bitmap = textureData.GenerateBitmap();
                texture = new RenderTexture(TextureTarget.Texture2D, textureData);
                using var _ = texture.BindingContext();

                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());
            }
            else
            {
                // TODO: LoadTexture has things like max texture size and anisotrophy, need to ignore these
                texture = GuiContext.MaterialLoader.LoadTexture(Resource);
            }

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
            if (FirstPaint)
            {
                FirstPaint = false; // OnLoad has control size of 0 for some reason

                if (GLControl.Width < texture.Width || GLControl.Height < texture.Height)
                {
                    TextureScale = Math.Min(
                        GLControl.Width / (float)texture.Width,
                        GLControl.Height / (float)texture.Height
                    );

                    SetZoomLabel();
                }

                Position = -new Vector2(
                    GLControl.Width / 2f - texture.Width / 2f * TextureScale,
                    GLControl.Height / 2f - texture.Height / 2f * TextureScale
                );
            }

            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();

            GL.UseProgram(shader.Program);

            //shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographic(1f, 1f, 0, 1));
            shader.SetUniform1("g_bTextureViewer", 1u);
            shader.SetUniform2("g_vViewportSize", new Vector2(MainFramebuffer.Width, MainFramebuffer.Height));
            shader.SetUniform2("g_vViewportPosition", Position);
            shader.SetUniform1("g_flScale", TextureScale);

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
