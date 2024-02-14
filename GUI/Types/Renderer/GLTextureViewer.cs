using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Svg.Skia;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.Texture;

namespace GUI.Types.Renderer
{
    class GLTextureViewer : GLViewerControl
    {
        enum CubemapProjection
        {
            None,
            Equirectangular,
            Cubic,
        }

        private VrfGuiContext GuiContext;
        private Resource Resource;
        private SKBitmap Bitmap;
        private SKSvg Svg;
        private RenderTexture texture;
        private Shader shader;
        private int vao;

        private SKBitmap NextBitmapToSet;
        private int NextBitmapVersion;

        private Vector2? ClickPosition;
        private Vector2 Position;
        private Vector2 PositionOld;
        private float TextureScale = 1f;
        private float TextureScaleOld = 1f;
        private float TextureScaleChangeTime = 10f;
        private float OriginalWidth;
        private float OriginalHeight;

        private int SelectedMip;
        private int SelectedDepth;
        private int SelectedCubeFace;
        private ChannelMapping SelectedChannels = ChannelMapping.RGB;
        private bool WantsSeparateAlpha;
        private CubemapProjection CubemapProjectionType;
        private TextureCodec decodeFlags;
        private Framebuffer SaveAsFbo;

        private CheckedListBox decodeFlagsListBox;

        private Vector2 ActualTextureSize
        {
            get
            {
                var size = new Vector2(OriginalWidth, OriginalHeight);

                size *= CubemapProjectionType switch
                {
                    CubemapProjection.Equirectangular => new Vector2(4, 2),
                    CubemapProjection.Cubic => new Vector2(4, 3),
                    _ => new Vector2(1, 1),
                };

                if (WantsSeparateAlpha && CubemapProjectionType == CubemapProjection.None)
                {
                    var mult = OriginalWidth > OriginalHeight
                        ? new Vector2(1, 2)
                        : new Vector2(2, 1);

                    size *= mult;
                }

                return size;
            }
        }

        private Vector2 ActualTextureSizeScaled => ActualTextureSize * TextureScale;
        private bool IsZoomedIn;
        private bool MovedFromOrigin_Unzoomed;

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

        private GLTextureViewer(VrfGuiContext guiContext) : base(guiContext)
        {
            GuiContext = guiContext;

            GLLoad += OnLoad;
            GLControl.PreviewKeyDown += OnPreviewKeyDown;

            SetZoomLabel();

            var resetButton = new Button
            {
                Text = "Reset zoom",
                AutoSize = true,
            };

            resetButton.Click += (_, __) => ResetZoom();

            AddControl(resetButton);
        }

        public GLTextureViewer(VrfGuiContext guiContext, SKBitmap bitmap) : this(guiContext)
        {
            Bitmap = bitmap;

            AddChannelsComboBox();
        }

        public GLTextureViewer(VrfGuiContext guiContext, Resource resource) : this(guiContext)
        {
            Resource = resource;

            var saveButton = new Button
            {
                Text = "Save to disk…",
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            saveButton.Click += OnSaveButtonClick;
            var copyLabel = new Label
            {
                Text = "or Ctrl-C to copy",
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };

            var saveTable = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Top,
                Size = new System.Drawing.Size(100, 64),
                Padding = new Padding(0, 15, 0, 15),
            };
            saveTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            saveTable.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            saveTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            saveTable.Controls.Add(saveButton, 0, 0);
            saveTable.Controls.Add(copyLabel, 1, 0);
            AddControl(saveTable);

            if (Resource.ResourceType == ResourceType.PanoramaVectorGraphic)
            {
                AddChannelsComboBox();

                using var ms = new MemoryStream(((Panorama)resource.DataBlock).Data);
                Svg = new SKSvg();
                Svg.Load(ms);

                OriginalWidth = Svg.Picture.CullRect.Width;
                OriginalHeight = Svg.Picture.CullRect.Height;

                return;
            }

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

            ComboBox cubemapProjectionComboBox = null;
            CheckBox softwareDecodeCheckBox = null;

            if (textureData.NumMipLevels > 1)
            {
                var mipComboBox = AddSelection("Mip level", (name, index) =>
                {
                    SelectedMip = index;

                    if (softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked)
                    {
                        SetupTexture(true);
                    }
                });

                mipComboBox.Items.AddRange(Enumerable.Range(0, textureData.NumMipLevels).Select(x => $"#{x}").ToArray());
                mipComboBox.SelectedIndex = 0;
            }

            if (textureData.Depth > 1)
            {
                var depthComboBox = AddSelection("Depth", (name, index) =>
                {
                    SelectedDepth = index;

                    if (softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked)
                    {
                        SetupTexture(true);
                    }
                });

                depthComboBox.Items.AddRange(Enumerable.Range(0, textureData.Depth).Select(x => $"#{x}").ToArray());
                depthComboBox.SelectedIndex = 0;
            }

            if ((textureData.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                ComboBox cubeFaceComboBox = null;

                cubemapProjectionComboBox = AddSelection("Projection type", (name, index) =>
                {
                    cubeFaceComboBox.Enabled = index == 0;

                    if (softwareDecodeCheckBox == null)
                    {
                        CubemapProjectionType = (CubemapProjection)index;
                        return;
                    }

                    var oldTextureSize = ActualTextureSizeScaled;

                    CubemapProjectionType = (CubemapProjection)index;

                    TextureScaleChangeTime = 0f;
                    TextureScaleOld = TextureScale;

                    PositionOld = Position;
                    CenterPosition();
                });

                cubeFaceComboBox = AddSelection("Cube face", (name, index) =>
                {
                    SelectedCubeFace = index;

                    if (softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked)
                    {
                        SetupTexture(true);
                    }
                });

                cubeFaceComboBox.Items.AddRange(Enum.GetNames<CubemapFace>());
                cubeFaceComboBox.SelectedIndex = 0;

                cubemapProjectionComboBox.Items.AddRange(Enum.GetNames<CubemapProjection>());
                cubemapProjectionComboBox.SelectedIndex = (int)CubemapProjection.Equirectangular;
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

            AddChannelsComboBox();

            var forceSoftwareDecode = textureData.IsRawJpeg || textureData.IsRawPng;
            softwareDecodeCheckBox = AddCheckBox("Software decode", forceSoftwareDecode, (state) =>
            {
                if ((textureData.Flags & VTexFlags.CUBE_TEXTURE) != 0)
                {
                    if (state)
                    {
                        cubemapProjectionComboBox.SelectedIndex = (int)CubemapProjection.None;
                        cubemapProjectionComboBox.Enabled = false;
                    }
                    else
                    {
                        cubemapProjectionComboBox.Enabled = true;
                    }
                }

                SetupTexture(state);
            });

            if (forceSoftwareDecode)
            {
                softwareDecodeCheckBox.Enabled = false;
            }
        }

        private void AddChannelsComboBox()
        {
            var channelsComboBox = AddSelection("Channels", (name, index) =>
            {
                if (texture == null)
                {
                    return;
                }

                var wasSeparateAlpha = WantsSeparateAlpha;
                var oldTextureSize = ActualTextureSizeScaled;

                SelectedChannels = ChannelsComboBoxOrder[index].Channels;
                WantsSeparateAlpha = ChannelsComboBoxOrder[index].SplitAlpha;

                if (wasSeparateAlpha || WantsSeparateAlpha)
                {
                    TextureScaleChangeTime = 0f;
                    TextureScaleOld = TextureScale;

                    PositionOld = Position;
                    Position -= oldTextureSize / 2f;
                    Position += ActualTextureSizeScaled / 2f;

                    ClampPosition();
                }
            });

            for (var i = 0; i < ChannelsComboBoxOrder.Length; i++)
            {
                channelsComboBox.Items.Add(ChannelsComboBoxOrder[i].ChoiceString);
            }

            channelsComboBox.SelectedIndex = DefaultSelection;
        }

        private void SetInitialDecodeFlagsState(CheckedListBox listBox)
        {
            listBox.Items.Clear();
            var values = Enum.GetValues<TextureCodec>();

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
            base.Dispose(disposing);

            if (disposing)
            {
                GLControl.PreviewKeyDown -= OnPreviewKeyDown;
                GLPaint -= OnPaint;

                GuiContext = null;
                Resource = null;

                Bitmap?.Dispose();
                Bitmap = null;

                Interlocked.Increment(ref NextBitmapVersion);
                NextBitmapToSet?.Dispose();
                NextBitmapToSet = null;

                Svg?.Dispose();
                Svg = null;

                decodeFlagsListBox?.Dispose();
                decodeFlagsListBox = null;

                texture?.Dispose();
                SaveAsFbo?.Dispose();
            }
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            using var saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = Settings.Config.SaveDirectory,
                Filter = "PNG Image|*.png|JPG Image|*.jpg", // Bitmap Image|*.bmp doesn't work in skia
                Title = "Save an Image File",
                FileName = Path.GetFileNameWithoutExtension(Resource.FileName),
                AddToRecent = true,
            };

            if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Settings.Config.SaveDirectory = Path.GetDirectoryName(saveFileDialog.FileName);

            // TODO: nonpow2 sizes?
            using var bitmap = ReadPixelsToBitmap();
            var format = SKEncodedImageFormat.Png;

            switch (saveFileDialog.FilterIndex)
            {
                case 2:
                    format = SKEncodedImageFormat.Jpeg;
                    break;
                case 3:
                    format = SKEncodedImageFormat.Bmp;
                    break;
            }

            var test = bitmap.GetPixelSpan();

            using var pixmap = bitmap.PeekPixels();
            using var fs = saveFileDialog.OpenFile();
            var t = pixmap.Encode(fs, format, 100);
        }

        protected override SKBitmap ReadPixelsToBitmap()
        {
            var size = ActualTextureSize;
            var bitmap = new SKBitmap((int)size.X, (int)size.Y, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            try
            {
                var pixels = bitmap.GetPixels(out var length);

                // extract pixels from framebuffer
                GL.Viewport(0, 0, bitmap.Width, bitmap.Height);

                if (SaveAsFbo == null)
                {
                    SaveAsFbo = Framebuffer.Prepare(bitmap.Width, bitmap.Height, 0, new(PixelInternalFormat.Rgba8, PixelFormat.Bgra, PixelType.UnsignedByte), null);
                    SaveAsFbo.Initialize();
                }
                else
                {
                    SaveAsFbo.Resize(bitmap.Width, bitmap.Height);
                }

                SaveAsFbo.Clear();

                Draw(SaveAsFbo, captureFullSizeImage: true);

                GL.Flush();
                GL.Finish();

                SaveAsFbo.Bind(FramebufferTarget.ReadFramebuffer);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                GL.ReadPixels(0, 0, bitmap.Width, bitmap.Height, PixelFormat.Bgra, PixelType.UnsignedByte, pixels);

                var bitmapToReturn = bitmap;
                bitmap = null;
                return bitmapToReturn;
            }
            finally
            {
                MainFramebuffer.Bind(FramebufferTarget.Framebuffer);
                bitmap?.Dispose();
            }
        }

        private void ResetZoom()
        {
            MovedFromOrigin_Unzoomed = false;
            ClickPosition = null;
            TextureScaleOld = TextureScale;
            TextureScale = 1f;
            TextureScaleChangeTime = 0f;

            PositionOld = Position;
            ClampPosition();

            SetZoomLabel();

            if (Svg != null)
            {
                Interlocked.Increment(ref NextBitmapVersion);
                Task.Run(GenerateNewSvgBitmap);
            }
        }

        private void SetZoomLabel() => SetMoveSpeedOrZoomLabel($"Zoom: {TextureScale * 100:0.0}% (scroll to change)");

        private void OnPreviewKeyDown(object sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right)
            {
                e.IsInputKey = true;
            }
        }

        protected override void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == (Keys.Control | Keys.S))
            {
                OnSaveButtonClick(null, null);
                return;
            }

            if (e.KeyData == (Keys.Control | Keys.NumPad0) || e.KeyData == (Keys.Control | Keys.D0))
            {
                ResetZoom();
                return;
            }

            if (e.KeyData == (Keys.Control | Keys.Add) || e.KeyData == (Keys.Control | Keys.Oemplus))
            {
                OnMouseWheel(null, new MouseEventArgs(MouseButtons.None, 0, GLControl.Width / 2, GLControl.Height / 2, 1));
                return;
            }

            if (e.KeyData == (Keys.Control | Keys.Subtract) || e.KeyData == (Keys.Control | Keys.OemMinus))
            {
                OnMouseWheel(null, new MouseEventArgs(MouseButtons.None, 0, GLControl.Width / 2, GLControl.Height / 2, -1));
                return;
            }

            if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right)
            {
                var move = 10f * TextureScale;
                var delta = e.KeyCode switch
                {
                    Keys.Up => new Vector2(0, -move),
                    Keys.Down => new Vector2(0, move),
                    Keys.Left => new Vector2(-move, 0),
                    Keys.Right => new Vector2(move, 0),
                    _ => throw new NotImplementedException(),
                };

                if (!IsZoomedIn)
                {
                    MovedFromOrigin_Unzoomed = true;
                }

                (TextureScaleOld, PositionOld) = GetCurrentPositionAndScale();
                TextureScaleChangeTime = 0f;
                Position += delta;
                ClampPosition();
            }

            base.OnKeyDown(sender, e);
        }

        protected override void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (ClickPosition == null)
            {
                return;
            }

            var oldPosition = Position;
            var mousePosition = new Vector2(e.Location.X, e.Location.Y);

            Position = ClickPosition.Value - mousePosition;

            ClampPosition();

            // When cursor moves past the edge, but the picture does not move, update click position
            // so that moving mouse in opposite direction instantly moves the picture, instead of waiting to move to the initial click position
            if (oldPosition == Position)
            {
                ClickPosition = Position + mousePosition;
            }
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

            if (Svg != null && TextureScaleOld != TextureScale)
            {
                // Reupload image with new scale
                Interlocked.Increment(ref NextBitmapVersion);
                Task.Run(GenerateNewSvgBitmap);
            }
        }

        private void ClampPosition()
        {
            var width = ActualTextureSizeScaled.X;
            var height = ActualTextureSizeScaled.Y;

            if (ClickPosition != null && !IsZoomedIn)
            {
                MovedFromOrigin_Unzoomed = true;
            }

            IsZoomedIn = GLControl.Height < height || GLControl.Width < width;

            if (IsZoomedIn)
            {
                if (GLControl.Width < width)
                {
                    Position.X = Math.Clamp(Position.X, 0, width - GLControl.Width);
                }
                else
                {
                    Position.X = Math.Clamp(Position.X, Math.Min(0, -GLControl.Width + width), 0);
                }

                if (GLControl.Height < height)
                {
                    Position.Y = Math.Clamp(Position.Y, 0, height - GLControl.Height);
                }
                else
                {
                    Position.Y = Math.Clamp(Position.Y, Math.Min(0, -GLControl.Height + height), 0);
                }

                MovedFromOrigin_Unzoomed = false;
            }
            else if (MovedFromOrigin_Unzoomed)
            {
                Position.X = Math.Clamp(Position.X, Math.Min(0, -GLControl.Width + width), 0);
                Position.Y = Math.Clamp(Position.Y, Math.Min(0, -GLControl.Height + height), 0);
            }
            else
            {
                CenterPosition();
            }

            Position.X = MathF.Round(Position.X);
            Position.Y = MathF.Round(Position.Y);
        }

        private void CenterPosition()
        {
            Position = -new Vector2(
                GLControl.Width / 2f - ActualTextureSizeScaled.X / 2f,
                GLControl.Height / 2f - ActualTextureSizeScaled.Y / 2f
            );
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

            Debug.Assert(texture != null);

            if (decodeFlagsListBox != null)
            {
                SetInitialDecodeFlagsState(decodeFlagsListBox);
            }

            texture.SetWrapMode(TextureWrapMode.ClampToEdge);
            texture.SetFiltering(TextureMinFilter.LinearMipmapLinear, TextureMagFilter.Nearest);

            if (Svg == null)
            {
                OriginalWidth = texture.Width;
                OriginalHeight = texture.Height;
            }

            var textureType = GLTextureDecoder.GetTextureTypeDefine(texture.Target);

            if (shader != null && shader.Parameters.ContainsKey(textureType))
            {
                return;
            }

            var arguments = new Dictionary<string, byte>
            {
                [textureType] = 1,
            };

            shader = GuiContext.ShaderLoader.LoadShader("vrf.texture_decode", arguments);
        }

        private void UploadTexture(bool forceSoftwareDecode)
        {
            if (Bitmap != null)
            {
                UploadBitmap(Bitmap);

                return;
            }

            if (Svg != null)
            {
                GenerateNewSvgBitmap();

                using (NextBitmapToSet)
                {
                    UploadBitmap(NextBitmapToSet);
                }

                NextBitmapToSet = null;

                return;
            }

            var textureData = (Texture)Resource.DataBlock;
            var isCpuDecodedFormat = textureData.IsRawJpeg || textureData.IsRawPng;

            if (isCpuDecodedFormat || forceSoftwareDecode)
            {
                SKBitmap bitmap;

                // GUI provides hardware decoder for texture decoding, but here we do not want to use it
                var decoder = HardwareAcceleratedTextureDecoder.Decoder;
                HardwareAcceleratedTextureDecoder.Decoder = null;

                try
                {
                    bitmap = textureData.GenerateBitmap((uint)SelectedDepth, (CubemapFace)SelectedCubeFace, (uint)SelectedMip);
                }
                finally
                {
                    HardwareAcceleratedTextureDecoder.Decoder = decoder;
                }

                using (bitmap)
                {
                    UploadBitmap(bitmap);
                }

                return;
            }

            texture = GuiContext.MaterialLoader.LoadTexture(Resource, isViewerRequest: true);
            decodeFlags = textureData.RetrieveCodecFromResourceEditInfo();
        }

        private void UploadBitmap(SKBitmap bitmap)
        {
            Debug.Assert(bitmap != null);

            texture = new RenderTexture(TextureTarget.Texture2D, bitmap.Width, bitmap.Height, 1, 1);
            decodeFlags = TextureCodec.None;

            var store = GLTextureDecoder.GetImageExportFormat(hdr: bitmap.ColorType == SKColorType.RgbaF32);
            GL.TextureStorage2D(texture.Handle, 1, store.SizedInternalFormat, texture.Width, texture.Height);
            GL.TextureSubImage2D(texture.Handle, 0, 0, 0, texture.Width, texture.Height, store.PixelFormat, store.PixelType, bitmap.GetPixels());
        }

        private void GenerateNewSvgBitmap()
        {
            var version = NextBitmapVersion;

            var width = Svg.Picture.CullRect.Width * TextureScale;
            var height = Svg.Picture.CullRect.Height * TextureScale;
            var imageInfo = new SKImageInfo((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Premul, null);

            var bitmap = new SKBitmap(imageInfo);

            try
            {
                using var canvas = new SKCanvas(bitmap);
                canvas.Scale(TextureScale, TextureScale);
                canvas.DrawPicture(Svg.Picture);

                if (version == NextBitmapVersion)
                {
                    NextBitmapToSet = bitmap;
                    bitmap = null;
                }
            }
            finally
            {
                bitmap?.Dispose();
            }
        }

        private void OnLoad(object sender, EventArgs e)
        {
            if (Svg == null) /// Svg will be setup on <see cref="FirstPaint"/> because it needs to be rescaled
            {
                SetupTexture(false);
            }

            // An empty VAO is required for some GPUs to not crash
            GL.CreateVertexArrays(1, out vao);

            // Use non-msaa framebuffer for texture viewer
            if (MainFramebuffer != GLDefaultFramebuffer)
            {
                MainFramebuffer.Dispose();
                MainFramebuffer = GLDefaultFramebuffer;
            }

            MainFramebuffer.ClearColor = OpenTK.Graphics.Color4.Green;
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;

            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);
            GL.Disable(EnableCap.CullFace);

            GLLoad -= OnLoad;

            // Bind paint event at the end of the processing loop so that first paint event has correctly sized gl control
            BeginInvoke(FirstPaint);
        }

        private void FirstPaint()
        {
            if (GLControl.Width < ActualTextureSize.X || GLControl.Height < ActualTextureSize.Y || Svg != null)
            {
                // Initially scale image to fit if it's bigger than the viewport
                TextureScale = Math.Min(
                    GLControl.Width / ActualTextureSize.X,
                    GLControl.Height / ActualTextureSize.Y
                );

                if (Svg != null)
                {
                    SetupTexture(false);
                }
            }
            else
            {
                // Initially scale image to the minimum scale if it's very small
                TextureScale = Math.Max(
                    1f,
                    0.1f * 256f / MathF.Max(ActualTextureSize.X, ActualTextureSize.Y)
                );
            }

            SetZoomLabel();

            /// This will call <see cref="CenterPosition"/> since it could not have been moved by user on first paint yet
            ClampPosition();

            GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            if (NextBitmapToSet != null)
            {
                texture?.Dispose();

                using (NextBitmapToSet)
                {
                    UploadBitmap(NextBitmapToSet);
                }

                NextBitmapToSet = null;
            }

            TextureScaleChangeTime += e.FrameTime;

            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.Clear();
            Draw(MainFramebuffer);
        }

        private void Draw(Framebuffer fbo, bool captureFullSizeImage = false)
        {
            GL.UseProgram(shader.Program);

            shader.SetUniform1("g_bTextureViewer", 1u);
            shader.SetUniform2("g_vViewportSize", new Vector2(fbo.Width, fbo.Height));

            var (scale, position) = captureFullSizeImage
                ? (1f, Vector2.Zero)
                : GetCurrentPositionAndScale();

            shader.SetUniform1("g_bCapturingScreenshot", captureFullSizeImage ? 1u : 0u);
            shader.SetUniform2("g_vViewportPosition", position);
            shader.SetUniform1("g_flScale", scale);

            shader.SetTexture(0, "g_tInputTexture", texture);
            shader.SetUniform4("g_vInputTextureSize", new Vector4(OriginalWidth, OriginalHeight, texture.Depth, texture.NumMipLevels));
            shader.SetUniform1("g_nSelectedMip", SelectedMip);
            shader.SetUniform1("g_nSelectedDepth", SelectedDepth);
            shader.SetUniform1("g_nSelectedCubeFace", SelectedCubeFace);
            shader.SetUniform1("g_nSelectedChannels", SelectedChannels.PackedValue);
            shader.SetUniform1("g_bWantsSeparateAlpha", WantsSeparateAlpha && CubemapProjectionType == CubemapProjection.None ? 1u : 0u);
            shader.SetUniform1("g_nCubemapProjectionType", (int)CubemapProjectionType);
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
