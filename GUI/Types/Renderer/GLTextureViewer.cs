using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;

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
        private Vector2 PositionOld;
        private float TextureScale = 1f;
        private float TextureScaleOld = 1f;
        private float TextureScaleChangeTime;

        private int SelectedDepth;
        private TextureCodec decodeFlags;

        private bool FirstPaint = true;
        private Button ResetButton;
        private ComboBox depthComboBox;
        private CheckedListBox decodeFlagsListBox;

        public GLTextureViewer(VrfGuiContext guiContext, Resource resource) : base()
        {
            GuiContext = guiContext;
            Resource = resource;

            GLLoad += OnLoad;
            GLControl.MouseMove += OnMouseMove;

            SetZoomLabel();

            var textureData = (Texture)Resource.DataBlock;
            decodeFlags = Texture.RetrieveCodecFromResourceEditInfo(Resource);

            AddControl(new Label
            {
                Text = $"Size: {textureData.Width}x{textureData.Height}",
                Width = 200,
            });
            AddControl(new Label
            {
                Text = $"Format: {textureData.Format}",
                Width = 200,
            });

            ResetButton = new Button
            {
                Text = "Reset zoom",
                AutoSize = true,
            };

            ResetButton.Click += (_, __) =>
            {
                PositionOld = Position;
                Position = Vector2.Zero;

                TextureScaleOld = TextureScale;
                TextureScale = 1f;
                TextureScaleChangeTime = 0f;

                SetZoomLabel();
            };

            AddControl(ResetButton);

            if (textureData.Depth > 1)
            {
                depthComboBox = AddSelection("Depth", (name, index) =>
                {
                    SelectedDepth = index;
                });

                depthComboBox.Items.AddRange(Enumerable.Range(0, textureData.Depth).Select(x => $"#{x}").ToArray());
                depthComboBox.SelectedIndex = 0;
            }

            decodeFlagsListBox = AddMultiSelection("Texture Conversion",
                listBox =>
                {
                    var values = Enum.GetValues(typeof(TextureCodec));

                    var i = 0;
                    for (var flag = 0; flag < values.Length; flag++)
                    {
                        var value = (TextureCodec)values.GetValue(flag);
                        var name = Enum.GetName(value);

                        // check for combined flag, or flag 0 (none)
                        if (value == 0 || (value & (value - 1)) != 0)
                        {
                            continue;
                        }

                        listBox.Items.Add(name);
                        var setCheckedState = decodeFlags.HasFlag(value);
                        listBox.SetItemChecked(i, setCheckedState);
                        i++;
                    }

                },
                checkedItemNames =>
                {
                    decodeFlags = TextureCodec.None;

                    foreach (var itemName in checkedItemNames)
                    {
                        decodeFlags |= (TextureCodec)Enum.Parse(typeof(TextureCodec), itemName);
                    }
                }
            );
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GLControl.MouseMove -= OnMouseMove;
                GLPaint -= OnPaint;

                ResetButton.Dispose();
                ResetButton = null;

                depthComboBox?.Dispose();
                depthComboBox = null;

                decodeFlagsListBox?.Dispose();
                decodeFlagsListBox = null;
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
            (TextureScaleOld, PositionOld) = GetCurrentPositionAndScale();
            TextureScaleChangeTime = 0f;
            ClickPosition = null;

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
            var posPrev = (pos + PositionOld) / TextureScaleOld;
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
            var isCpuDecodedImage = textureData.IsRawJpeg || textureData.IsRawPng;

            if (isCpuDecodedImage)
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

            if (isCpuDecodedImage)
            {
                decodeFlags = TextureCodec.None;
            }

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

            TextureScaleChangeTime += e.FrameTime;

            var (scale, position) = GetCurrentPositionAndScale();

            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();

            GL.UseProgram(shader.Program);

            //shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographic(1f, 1f, 0, 1));
            shader.SetUniform1("g_bTextureViewer", 1u);
            shader.SetUniform2("g_vViewportSize", new Vector2(MainFramebuffer.Width, MainFramebuffer.Height));
            shader.SetUniform2("g_vViewportPosition", position);
            shader.SetUniform1("g_flScale", scale);

            shader.SetTexture(0, "g_tInputTexture", texture);
            shader.SetUniform4("g_vInputTextureSize", new Vector4(texture.Width, texture.Height, texture.Depth, texture.NumMipLevels));
            shader.SetUniform1("g_nSelectedMip", 0);
            shader.SetUniform1("g_nSelectedDepth", SelectedDepth);
            shader.SetUniform1("g_nSelectedChannels", ChannelMapping.RGBA.PackedValue);
            shader.SetUniform1("g_nDecodeFlags", (int)decodeFlags);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        private (float Scale, Vector2 Position) GetCurrentPositionAndScale()
        {
            var time = Math.Min(TextureScaleChangeTime / 0.4f, 1.0f);
            time = 1f - MathF.Pow(1f - time, 5f); // easeOutQuint

            var position = Vector2.Lerp(PositionOld, Position, time);
            var scale = float.Lerp(TextureScaleOld, TextureScale, time);

            return (scale, position);
        }
    }
}
