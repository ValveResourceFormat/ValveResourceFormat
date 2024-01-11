using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;
using ValveResourceFormat.Utils;

namespace GUI.Types.Renderer
{
    class GLTextureViewer : GLViewerControl, IGLViewer
    {
        private VrfGuiContext GuiContext;
        private Resource Resource;
        private SKBitmap Bitmap;
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
        private ChannelMapping SelectedChannels = ChannelMapping.RGB;
        private bool WantsSeparateAlpha;
        private TextureCodec decodeFlags;

        private bool FirstPaint = true;
        private Button ResetButton;
        private ComboBox depthComboBox;
        private CheckedListBox decodeFlagsListBox;
        private ComboBox channelsComboBox;
        private CheckBox softwareDecodeCheckBox;

        const int DefaultSelection = 3;
        static readonly (ChannelMapping Channels, bool SplitAlpha, string ChoiceString)[] ChannelsComboBoxOrder = [
            (ChannelMapping.R, false, "Red"),
            (ChannelMapping.G, false, "Green"),
            (ChannelMapping.B, false, "Blue"),
            (ChannelMapping.RGB, false, "Opaque"),
            (ChannelMapping.RGBA, false, "Transparent"),
            (ChannelMapping.A, false, "Alpha"),
            (ChannelMapping.RGBA, true, "Opaque with split Alpha"),
        ];

        private GLTextureViewer(VrfGuiContext guiContext) : base()
        {
            GuiContext = guiContext;

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
                PositionOld = Position;
                Position = Vector2.Zero;

                TextureScaleOld = TextureScale;
                TextureScale = 1f;
                TextureScaleChangeTime = 0f;

                SetZoomLabel();
            };

            AddControl(ResetButton);
        }

        public GLTextureViewer(VrfGuiContext guiContext, SKBitmap bitmap) : this(guiContext)
        {
            Bitmap = bitmap;
        }

        public GLTextureViewer(VrfGuiContext guiContext, Resource resource) : this(guiContext)
        {
            Resource = resource;

            var textureData = (Texture)Resource.DataBlock;

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
                SetInitialDecodeFlagsState,
                checkedItemNames =>
                {
                    decodeFlags = TextureCodec.None;

                    foreach (var itemName in checkedItemNames)
                    {
                        decodeFlags |= (TextureCodec)Enum.Parse(typeof(TextureCodec), itemName);
                    }
                }
            );


            channelsComboBox = AddSelection("Channels", (name, index) =>
            {
                SelectedChannels = ChannelsComboBoxOrder[index].Channels;
                WantsSeparateAlpha = ChannelsComboBoxOrder[index].SplitAlpha;
            });

            for (var i = 0; i < ChannelsComboBoxOrder.Length; i++)
            {
                channelsComboBox.Items.Add(ChannelsComboBoxOrder[i].ChoiceString);
            }

            channelsComboBox.SelectedIndex = DefaultSelection;

            var forceSoftwareDecode = textureData.IsRawJpeg || textureData.IsRawPng;
            softwareDecodeCheckBox = AddCheckBox("Software decode", forceSoftwareDecode, SetupTexture);

            if (forceSoftwareDecode)
            {
                softwareDecodeCheckBox.Enabled = false;
            }
        }

        private void SetInitialDecodeFlagsState(CheckedListBox listBox)
        {
            listBox.Items.Clear();
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GLControl.MouseMove -= OnMouseMove;
                GLPaint -= OnPaint;

                GuiContext = null;
                Resource = null;

                Bitmap?.Dispose();
                Bitmap = null;

                ResetButton.Dispose();
                ResetButton = null;

                depthComboBox?.Dispose();
                depthComboBox = null;

                decodeFlagsListBox?.Dispose();
                decodeFlagsListBox = null;

                channelsComboBox?.Dispose();
                channelsComboBox = null;

                softwareDecodeCheckBox?.Dispose();
                softwareDecodeCheckBox = null;

                texture?.Dispose();
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

            var scaleMinMax = new Vector2(0.1f, 50f);
            scaleMinMax *= 256 / MathF.Max(ActualTextureSize.X, ActualTextureSize.Y);

            TextureScale = Math.Clamp(TextureScale, scaleMinMax.X, scaleMinMax.Y);

            var pos = new Vector2(e.Location.X, e.Location.Y);
            var posPrev = (pos + PositionOld) / TextureScaleOld;
            var posNewScale = posPrev * TextureScale;
            Position = posNewScale - pos;

            ClampPosition();
            SetZoomLabel();
        }

        public Vector2 ActualTextureSize
        {
            get
            {
                if (WantsSeparateAlpha)
                {
                    var isWide = texture.Width > texture.Height;
                    return new Vector2(
                        isWide ? texture.Width : texture.Width * 2,
                        isWide ? texture.Height * 2 : texture.Height
                    );
                }

                return new Vector2(texture.Width, texture.Height);
            }
        }

        public Vector2 ActualTextureSizeScaled => ActualTextureSize * TextureScale;
        public bool IsZoomedIn;
        public bool MovedFromOrigin_Unzoomed;

        private void ClampPosition()
        {
            var width = ActualTextureSizeScaled.X;
            var height = ActualTextureSizeScaled.Y;

            if (ClickPosition != null && !IsZoomedIn)
            {
                MovedFromOrigin_Unzoomed = true;
            }

            IsZoomedIn = GLControl.Height < height && GLControl.Width < width;

            if (IsZoomedIn)
            {
                Position.X = Math.Clamp(Position.X, 0, width - GLControl.Width);
                Position.Y = Math.Clamp(Position.Y, 0, height - GLControl.Height);
                MovedFromOrigin_Unzoomed = false;
                return;
            }

            if (MovedFromOrigin_Unzoomed)
            {
                Position.X = Math.Clamp(Position.X, Math.Min(0, -GLControl.Width + width), 0);
                Position.Y = Math.Clamp(Position.Y, Math.Min(0, -GLControl.Height + height), 0);
            }
            else
            {
                Position = -new Vector2(
                    GLControl.Width / 2f - ActualTextureSizeScaled.X / 2f,
                    GLControl.Height / 2f - ActualTextureSizeScaled.Y / 2f
                );
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

        private void SetupTexture(bool forceSoftwareDecode)
        {
            texture?.Dispose();

            UploadTexture(forceSoftwareDecode);

            if (decodeFlagsListBox != null)
            {
                SetInitialDecodeFlagsState(decodeFlagsListBox);
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
        }

        private void UploadTexture(bool forceSoftwareDecode)
        {
            if (Resource == null)
            {
                Debug.Assert(Bitmap != null);
                Debug.Assert(Bitmap.ColorType == SKColorType.Bgra8888);

                texture = new RenderTexture(TextureTarget.Texture2D, Bitmap.Width, Bitmap.Height, 1, 1);
                decodeFlags = TextureCodec.None;

                using var _ = texture.BindingContext();
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, Bitmap.GetPixels());
                return;
            }

            var textureData = (Texture)Resource.DataBlock;
            var isCpuDecodedFormat = textureData.IsRawJpeg || textureData.IsRawPng;

            if (isCpuDecodedFormat || forceSoftwareDecode)
            {
                SKBitmap bitmap;

                lock (HardwareAcceleratedTextureDecoder.Decoder)
                {
                    // GUI provides hardware decoder for texture decoding, but here we do not want to use it
                    var decoder = HardwareAcceleratedTextureDecoder.Decoder;
                    HardwareAcceleratedTextureDecoder.Decoder = null;

                    try
                    {
                        bitmap = textureData.GenerateBitmap();
                    }
                    finally
                    {
                        HardwareAcceleratedTextureDecoder.Decoder = decoder;
                    }
                }

                using (bitmap)
                {
                    Debug.Assert(bitmap.ColorType == SKColorType.Bgra8888);

                    texture = new RenderTexture(TextureTarget.Texture2D, textureData);
                    decodeFlags = TextureCodec.None;

                    using var _ = texture.BindingContext();
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, bitmap.GetPixels());
                }

                return;
            }

            // TODO: LoadTexture has things like max texture size and anisotrophy, need to ignore these
            texture = GuiContext.MaterialLoader.LoadTexture(Resource);
            decodeFlags = textureData.RetrieveCodecFromResourceEditInfo();
        }

        private void OnLoad(object sender, EventArgs e)
        {
            SetupTexture(false);

            vao = GL.GenVertexArray();

            MainFramebuffer.ClearColor = OpenTK.Graphics.Color4.Green;
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

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

                shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_decode", shader.Parameters);
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

                if (GLControl.Width < ActualTextureSize.X || GLControl.Height < ActualTextureSize.Y)
                {
                    TextureScale = Math.Min(
                        GLControl.Width / ActualTextureSize.X,
                        GLControl.Height / ActualTextureSize.Y
                    );

                    SetZoomLabel();
                }

                Position = -new Vector2(
                    GLControl.Width / 2f - ActualTextureSizeScaled.X / 2f,
                    GLControl.Height / 2f - ActualTextureSizeScaled.Y / 2f
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
            shader.SetUniform1("g_nSelectedChannels", SelectedChannels.PackedValue);
            shader.SetUniform1("g_bWantsSeparateAlpha", WantsSeparateAlpha ? 1u : 0u);
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
