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
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.TextureDecoders;
using static ValveResourceFormat.ResourceTypes.Texture;

namespace GUI.Types.GLViewers
{
    class GLTextureViewer : GLBaseControl, IDisposable
    {
        enum CubemapProjection
        {
            None,
            Equirectangular,
            Cubic,
        }

        enum ChannelSplitting
        {
            None,
            Alpha,
            FourChannels,
        }

        enum Filtering
        {
            Point,
            Linear,
        }

        protected VrfGuiContext VrfGuiContext;
        private Resource? Resource;
        private SKBitmap? Bitmap;
        private SKSvg? Svg;
        private RenderTexture? texture;
        private Shader? shader;

        private SKBitmap? NextBitmapToSet;
        private int NextBitmapVersion;

        protected Vector2? ClickPosition;
        protected Vector2 Position;
        private Vector2 PositionOld;
        protected float TextureScale = 1f;
        private float TextureScaleOld = 1f;
        protected float TextureScaleChangeTime = 10f;
        protected float OriginalWidth;
        protected float OriginalHeight;

        private int SelectedMip;
        private int SelectedDepth;
        private int SelectedCubeFace;
        private bool VisualizeTiling;
        private ChannelMapping SelectedChannels = ChannelMapping.RGB;
        private Filtering SelectedFiltering = Filtering.Point;
        private ChannelSplitting ChannelSplitMode;
        private CubemapProjection CubemapProjectionType;
        private TextureCodec decodeFlags;
        private const TextureCodec softwareDecodeOnlyOptions = TextureCodec.ForceLDR;
        private Framebuffer? SaveAsFbo;

        private CheckedListBox? decodeFlagsListBox;
        private bool ShowLightBackground;
        private bool WasMovingLastFrame;

        private int DisplayedImageCount => Math.Max(1 << (int)ChannelSplitMode, VisualizeTiling ? 2 : 1);

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

                if (VisualizeTiling)
                {
                    size *= 2;
                }

                if (ChannelSplitMode > 0)
                {
                    var mult = OriginalWidth > OriginalHeight
                        ? new Vector2(1, DisplayedImageCount)
                        : new Vector2(DisplayedImageCount, 1);

                    size *= mult;
                }

                return size;
            }
        }

        private Vector2 ActualTextureSizeScaled => ActualTextureSize * TextureScale;
        private bool IsZoomedIn;
        private bool MovedFromOrigin_Unzoomed;

        private int LastRenderHash;
        private int NumRendersLastHash;

        static readonly (ChannelMapping Channels, ChannelSplitting ChannelSplitMode, string ChoiceString)[] ChannelsComboBoxOrder = [
            (ChannelMapping.R, ChannelSplitting.None, "Red"),
            (ChannelMapping.G, ChannelSplitting.None, "Green"),
            (ChannelMapping.B, ChannelSplitting.None, "Blue"),
            (ChannelMapping.RGB, ChannelSplitting.None, "RGB (Opaque)"),
            (ChannelMapping.RGBA, ChannelSplitting.None, "RGBA (Transparent)"),
            (ChannelMapping.A, ChannelSplitting.None, "Alpha"),
            (ChannelMapping.RGBA, ChannelSplitting.Alpha, "RGB | A (Separate channels)"),
            (ChannelMapping.RGBA, ChannelSplitting.FourChannels, "R | G | B | A (Separate channels)"),
        ];

        private GLTextureViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext) : base(rendererContext)
        {
            VrfGuiContext = vrfGuiContext;
            RendererContext = new(vrfGuiContext, VrfGuiContext.Logger);

#if DEBUG
            ShaderHotReload.ShadersReloaded += OnHotReload;
#endif
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            GLControl?.PreviewKeyDown += OnPreviewKeyDown;

            ShowLightBackground = !Application.IsDarkModeEnabled;

            UpdateZoomLabel();

            var resetButton = new ThemedButton
            {
                Text = "Reset zoom",
                AutoSize = true,
            };

            resetButton.Click += (_, __) => ResetZoom();

            UiControl.AddControl(resetButton);

            if (Bitmap != null)
            {
                // Image viewer
                AddChannelsComboBox();
            }
            else if (Svg != null)
            {
                // Svg viewer
                AddChannelsComboBox();
            }
            else if (Resource != null)
            {
                InitializeUIControlsForResource();
            }

            base.AddUiControls();
        }

        private void InitializeUIControlsForResource()
        {
            Debug.Assert(Resource != null);
            Debug.Assert(UiControl != null);

            var saveButton = new ThemedButton
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
            UiControl.AddControl(saveTable);

            if (Resource.ResourceType == ResourceType.PanoramaVectorGraphic)
            {
                AddChannelsComboBox();
                return;
            }
            else if (Resource.ResourceType == ResourceType.PostProcessing && Resource.DataBlock is PostProcessing postProcessingData)
            {
                var resolution = postProcessingData.GetColorCorrectionLUTDimension();

                UiControl.AddControl(new Label
                {
                    Text = $"Color correction: {(postProcessingData.HasColorCorrection() ? "Yes" : "No")}",
                    Width = 200,
                });
                UiControl.AddControl(new Label
                {
                    Text = $"Resolution: {resolution}",
                    Width = 200,
                });

                // TODO: Kind of crappy.
                var depthComboBox2 = UiControl.AddSelection("Depth", (name, index) =>
                {
                    SelectedDepth = index;
                });

                depthComboBox2.Items.AddRange(Enumerable.Range(0, resolution).Select(x => $"#{x}").ToArray());
                depthComboBox2.SelectedIndex = 0;

                return;
            }

            if (Resource.DataBlock is not Texture textureData)
            {
                return;
            }

            UiControl.AddControl(new Label
            {
                Text = $"Size: {textureData.Width}x{textureData.Height}",
                Width = 200,
            });
            UiControl.AddControl(new Label
            {
                Text = $"Format: {textureData.Format}",
                Width = 200,
            });

            ComboBox? cubemapProjectionComboBox = null;
            CheckBox? softwareDecodeCheckBox = null;
            ComboBox? depthComboBox = null;

            if (textureData.NumMipLevels > 1)
            {
                string GetMipLevelSizeString(int mipLevel)
                {
                    var mipWidth = Math.Max(1, textureData.Width >> mipLevel);
                    var mipHeight = Math.Max(1, textureData.Height >> mipLevel);

                    if ((textureData.Flags & VTexFlags.VOLUME_TEXTURE) != 0)
                    {
                        var mipDepth = Math.Max(1, textureData.Depth >> mipLevel);
                        return $"(#{mipLevel}) {mipWidth}x{mipHeight}x{mipDepth}";
                    }

                    return $"(#{mipLevel}) {mipWidth}x{mipHeight}";
                }

                var mipComboBox = UiControl.AddSelection("Mip level", (name, index) =>
                {
                    SelectedMip = index;

                    // Depth levels are also mip mapped, so we have to remove incorrect levels
                    if (depthComboBox != null && (textureData.Flags & VTexFlags.VOLUME_TEXTURE) != 0)
                    {
                        var depthMip = textureData.Depth >> SelectedMip;
                        var newSelectedDepth = Math.Min(SelectedDepth, depthMip - 1);

                        depthComboBox.BeginUpdate();
                        depthComboBox.Items.Clear();
                        depthComboBox.Items.AddRange(Enumerable.Range(0, depthMip).Select(x => $"#{x}").ToArray());
                        depthComboBox.SelectedIndex = newSelectedDepth;
                        depthComboBox.EndUpdate();
                    }

                    if (softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked)
                    {
                        SetupTextureFromUi(true);
                    }
                });

                mipComboBox.Items.AddRange(
                    [.. Enumerable.Range(0, textureData.NumMipLevels).Select(GetMipLevelSizeString)]);
                mipComboBox.SelectedIndex = 0;
            }

            if (textureData.Depth > 1)
            {
                depthComboBox = UiControl.AddSelection("Depth", (name, index) =>
                {
                    SelectedDepth = index;

                    if (softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked)
                    {
                        SetupTextureFromUi(true);
                    }
                });

                depthComboBox.Items.AddRange(Enumerable.Range(0, textureData.Depth).Select(x => $"#{x}").ToArray());
                depthComboBox.SelectedIndex = 0;
            }

            if ((textureData.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                ComboBox? cubeFaceComboBox = null;

                cubemapProjectionComboBox = UiControl.AddSelection("Projection type", (name, index) =>
                {
                    cubeFaceComboBox!.Enabled = index == 0;

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

                cubeFaceComboBox = UiControl.AddSelection("Cube face", (name, index) =>
                {
                    SelectedCubeFace = index;

                    if (softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked)
                    {
                        SetupTextureFromUi(true);
                    }
                });

                cubeFaceComboBox.Items.AddRange(Enum.GetNames<CubemapFace>());
                cubeFaceComboBox.SelectedIndex = 0;

                cubemapProjectionComboBox.Items.AddRange(Enum.GetNames<CubemapProjection>());
                cubemapProjectionComboBox.SelectedIndex = (int)CubemapProjection.Equirectangular;
            }

            decodeFlags = textureData.RetrieveCodecFromResourceEditInfo();

            decodeFlagsListBox = UiControl.AddMultiSelection("Texture Conversion",
                SetInitialDecodeFlagsState,
                checkedItemNames =>
                {
                    decodeFlags = TextureCodec.None;

                    foreach (var itemName in checkedItemNames)
                    {
                        decodeFlags |= Enum.Parse<TextureCodec>(itemName);
                    }
                }
            );

            AddChannelsComboBox();

            var forceSoftwareDecode = textureData.IsRawAnyImage;
            softwareDecodeCheckBox = UiControl.AddCheckBox("Software decode", forceSoftwareDecode, (state) =>
            {
                if (cubemapProjectionComboBox != null)
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

                SetupTextureFromUi(state);
            });

            UiControl.AddCheckBox("Show UV Tiling", false, (state) =>
            {
                var previousSize = ActualTextureSizeScaled;

                VisualizeTiling = state;
                SetTextureFilteringFromUi();

                TextureDimensionsChanged(previousSize);
            });

            if (forceSoftwareDecode)
            {
                softwareDecodeCheckBox.Enabled = false;
            }

            return;
        }

        public GLTextureViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, SKBitmap? bitmap) : this(vrfGuiContext, rendererContext)
        {
            Bitmap = bitmap;
        }

        public GLTextureViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, SKSvg svg) : this(vrfGuiContext, rendererContext)
        {
            SetSvg(svg);
        }

        public GLTextureViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Resource resource) : this(vrfGuiContext, rendererContext)
        {
            Resource = resource;

            if (resource.ResourceType == ResourceType.PanoramaVectorGraphic && resource.DataBlock is Panorama panoramaData)
            {
                using var ms = new MemoryStream(panoramaData.Data);
                var svg = new SKSvg();
                svg.Load(ms);

                SetSvg(svg);
            }
        }

        private void SetSvg(SKSvg svg)
        {
            ArgumentNullException.ThrowIfNull(svg.Picture);

            Svg = svg;
            OriginalWidth = Svg.Picture.CullRect.Width;
            OriginalHeight = Svg.Picture.CullRect.Height;
        }

        private void AddChannelsComboBox()
        {
            Debug.Assert(UiControl != null);

            var channelsComboBox = UiControl.AddSelection("Channels", (name, index) =>
            {
                SelectedChannels = ChannelsComboBoxOrder[index].Channels;
                var splitMode = ChannelsComboBoxOrder[index].ChannelSplitMode;

                // do not split channels under these conditions
                if (CubemapProjectionType != CubemapProjection.None || VisualizeTiling)
                {
                    splitMode = 0;
                }

                if (splitMode != ChannelSplitMode)
                {
                    var previousSize = ActualTextureSizeScaled;

                    ChannelSplitMode = splitMode;
                    TextureDimensionsChanged(previousSize);
                }
            });

            for (var i = 0; i < ChannelsComboBoxOrder.Length; i++)
            {
                channelsComboBox.Items.Add(ChannelsComboBoxOrder[i].ChoiceString);
            }

            channelsComboBox.SelectedIndex = Svg != null
                ? Array.FindIndex(ChannelsComboBoxOrder, channel => channel.Channels == ChannelMapping.RGBA)
                : Array.FindIndex(ChannelsComboBoxOrder, channel => channel.Channels == ChannelMapping.RGB);

            var samplingComboBox = UiControl.AddSelection("Sampling", (name, index) =>
            {
                SelectedFiltering = (Filtering)index;
                SetTextureFilteringFromUi();
            });

            samplingComboBox.Items.AddRange(Enum.GetNames<Filtering>());
            samplingComboBox.SelectedIndex = 0;
        }

        private void SetTextureFiltering()
        {
            if (texture != null)
            {
                var (min, mag) = SelectedFiltering switch
                {
                    Filtering.Point => (TextureMinFilter.NearestMipmapNearest, TextureMagFilter.Nearest),
                    Filtering.Linear => (TextureMinFilter.LinearMipmapNearest, TextureMagFilter.Linear),
                    _ => throw new UnreachableException(),
                };

                texture.SetFiltering(min, mag);
                texture.SetWrapMode(VisualizeTiling ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge);
            }
        }

        /// <param name="oldTextureSize">The texture size before changing viewer state.</param>
        private void TextureDimensionsChanged(Vector2 oldTextureSize)
        {
            if (texture == null)
            {
                return;
            }

            TextureScaleChangeTime = 0f;
            TextureScaleOld = TextureScale;

            PositionOld = Position;

            var imageCount = (float)DisplayedImageCount;
            Position -= oldTextureSize / imageCount;
            Position += ActualTextureSizeScaled / imageCount;

            ClampPosition();
        }

        private void SetInitialDecodeFlagsState(CheckedListBox listBox)
        {
            listBox.Items.Clear();
            var values = Enum.GetValues<TextureCodec>();

            var i = 0;
            for (var flag = 0; flag < values.Length; flag++)
            {
                var value = (TextureCodec)values.GetValue(flag)!;
                var name = Enum.GetName(value)!;

                var isCombinedFlag = (value & value - 1) != 0;
                var skipFlags = TextureCodec.None | TextureCodec.Auto;

                if (isCombinedFlag || skipFlags.HasFlag(value))
                {
                    continue;
                }

                listBox.Items.Add(name);
                var setCheckedState = decodeFlags.HasFlag(value);
                listBox.SetItemChecked(i, setCheckedState);
                i++;
            }
        }

        public override void Dispose()
        {
            base.Dispose();

            GLControl?.PreviewKeyDown -= OnPreviewKeyDown;

#if DEBUG
            ShaderHotReload.ShadersReloaded -= OnHotReload;
#endif
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
        }

        private void OnSaveButtonClick(object? sender, EventArgs e)
        {
            if (Resource == null)
            {
                return;
            }

            var filter = "PNG Image|*.png|JPG Image|*.jpg";
            var alternativeImageFormatIndex = 2;

            var isHdrTexture = Resource.DataBlock is Texture textureData && textureData.IsHighDynamicRange;

            if (Svg != null)
            {
                filter = $"SVG (Scalable Vector Graphics)|*.svg|{filter}";
                alternativeImageFormatIndex++;
            }
            else if (isHdrTexture)
            {
                filter = "EXR Image|*.exr|" + filter;
                alternativeImageFormatIndex++;
            }

            using var saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = Settings.Config.SaveDirectory,
                Filter = filter,
                Title = "Save an Image File",
                FileName = Path.GetFileNameWithoutExtension(Resource.FileName),
                AddToRecent = true,
            };

            if (saveFileDialog.ShowDialog(UiControl) != DialogResult.OK)
            {
                return;
            }

            var directory = saveFileDialog.FileName;
            if (directory != null)
            {
                Settings.Config.SaveDirectory = directory;
            }

            using var fs = saveFileDialog.OpenFile();

            if (Svg != null && saveFileDialog.FilterIndex == 1 && Resource.DataBlock is Panorama panoramaData)
            {
                fs.Write(panoramaData.Data);
                return;
            }

            if (isHdrTexture && saveFileDialog.FilterIndex == 1)
            {
                using var hdrBitmap = ReadPixelsToBitmap(hdr: true);
                fs.Write(ValveResourceFormat.IO.TextureExtract.ToExrImage(hdrBitmap));
                return;
            }

            // TODO: nonpow2 sizes?
            using var bitmap = ReadPixelsToBitmap();
            var format = SKEncodedImageFormat.Png;

            switch (saveFileDialog.FilterIndex - alternativeImageFormatIndex)
            {
                case 0:
                    format = SKEncodedImageFormat.Jpeg;
                    break;
            }

            var test = bitmap.GetPixelSpan();

            using var pixmap = bitmap.PeekPixels();
            var t = pixmap.Encode(fs, format, 100);
        }

        protected override SKBitmap ReadPixelsToBitmap()
        {
            return ReadPixelsToBitmap(hdr: false);
        }

        protected SKBitmap ReadPixelsToBitmap(bool hdr = false)
        {
            var size = ActualTextureSize;

            if (SelectedMip > 0)
            {
                size /= 1 << SelectedMip;
            }

            var bitmapFormat = hdr ? HdrBitmapColorType : DefaultBitmapColorType;
            var bitmap = new SKBitmap((int)size.X, (int)size.Y, bitmapFormat, SKAlphaType.Unpremul);

            try
            {
                var pixels = bitmap.GetPixels(out var length);

                using var lockedGl = MakeCurrent();

                // extract pixels from framebuffer
                GL.Viewport(0, 0, bitmap.Width, bitmap.Height);

                var fboFormat = GLTextureDecoder.GetPreferredFramebufferFormat(hdr);

                if (SaveAsFbo is not null)
                {
                    if (SaveAsFbo.ColorFormat != fboFormat)
                    {
                        SaveAsFbo.Delete();
                        SaveAsFbo = null;
                    }
                    else
                    {
                        SaveAsFbo.Resize(bitmap.Width, bitmap.Height);
                    }
                }

                if (SaveAsFbo is null)
                {
                    SaveAsFbo = Framebuffer.Prepare(nameof(SaveAsFbo), bitmap.Width, bitmap.Height, 0, fboFormat, null);
                    SaveAsFbo.Initialize();
                }

                SaveAsFbo.BindAndClear(FramebufferTarget.DrawFramebuffer);

                Draw(SaveAsFbo, captureFullSizeImage: true);

                GL.Flush();
                GL.Finish();

                SaveAsFbo.Bind(FramebufferTarget.ReadFramebuffer);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                GL.ReadPixels(0, 0, bitmap.Width, bitmap.Height, SaveAsFbo.ColorFormat!.PixelFormat, SaveAsFbo.ColorFormat.PixelType, pixels);

                Debug.Assert(MainFramebuffer is not null);
                MainFramebuffer.Bind(FramebufferTarget.Framebuffer);

                var bitmapToReturn = bitmap;
                bitmap = null;
                return bitmapToReturn;
            }
            finally
            {
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

            UpdateZoomLabel();

            if (Svg != null)
            {
                Interlocked.Increment(ref NextBitmapVersion);
                Task.Run(GenerateNewSvgBitmap);
            }
        }

        private void UpdateZoomLabel() => SetMoveSpeedOrZoomLabel($"Zoom: {TextureScale * 100:0.0}% (scroll to change)");

        private void OnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            if (e.KeyCode is Keys.Up or Keys.Down or Keys.Left or Keys.Right)
            {
                e.IsInputKey = true;
            }
        }

        protected override void OnKeyDown(object? sender, KeyEventArgs e)
        {
            base.OnKeyDown(sender, e);

            InvalidateRender();

            if (e.KeyData == (Keys.Control | Keys.S))
            {
                OnSaveButtonClick(null, EventArgs.Empty);
                return;
            }

            if (e.KeyData == (Keys.Control | Keys.NumPad0) || e.KeyData == (Keys.Control | Keys.D0))
            {
                ResetZoom();
                return;
            }

            Debug.Assert(GLControl != null);

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
        }

        private void HandleArrowKeyMovement(float frameTime)
        {
            var movementKeys = CurrentlyPressedKeys &
                (TrackedKeys.Forward | TrackedKeys.Back |
                 TrackedKeys.Left | TrackedKeys.Right);

            var isMovingThisFrame = movementKeys != TrackedKeys.None;

            if (!isMovingThisFrame)
            {
                WasMovingLastFrame = false;
                return;
            }

            var baseSpeed = 300f;
            var speedMultiplier = 1f;

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.Shift))
            {
                speedMultiplier = 4f;
            }
            else if (CurrentlyPressedKeys.HasFlag(TrackedKeys.Control))
            {
                speedMultiplier = 2f;
            }

            var moveDistance = baseSpeed * speedMultiplier * frameTime;

            var delta = Vector2.Zero;

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.Forward))
            {
                delta.Y -= moveDistance;
            }

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.Back))
            {
                delta.Y += moveDistance;
            }

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.Left))
            {
                delta.X -= moveDistance;
            }

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.Right))
            {
                delta.X += moveDistance;
            }

            if (delta != Vector2.Zero)
            {
                if (!IsZoomedIn)
                {
                    MovedFromOrigin_Unzoomed = true;
                }

                if (!WasMovingLastFrame)
                {
                    (TextureScaleOld, PositionOld) = GetCurrentPositionAndScale();
                    TextureScaleChangeTime = 0f;
                }

                WasMovingLastFrame = true;

                Position += delta;
                ClampPosition();
            }
        }

        protected override void OnMouseMove(object? sender, MouseEventArgs e)
        {
            Debug.Assert(GLControl != null);

            GLControl.Focus();

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

            InvalidateRender();
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            ClickPosition = Position + new Vector2(e.Location.X, e.Location.Y);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs mouseEventArgs)
        {
            ClickPosition = null;
        }

        protected override void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            var isShiftPressed = (CurrentlyPressedKeys & TrackedKeys.Shift) > 0;
            var isCtrlPressed = (CurrentlyPressedKeys & TrackedKeys.Control) > 0;

            if (isShiftPressed || isCtrlPressed)
            {
                (TextureScaleOld, PositionOld) = GetCurrentPositionAndScale();
                TextureScaleChangeTime = 0f;
                ClickPosition = null;

                var panSpeed = 50f;
                if ((CurrentlyPressedKeys & TrackedKeys.Alt) > 0)
                {
                    panSpeed *= 2f;
                }

                var delta = Vector2.Zero;

                if (isShiftPressed)
                {
                    delta.Y = e.Delta > 0 ? -panSpeed : panSpeed;
                }
                else if (isCtrlPressed)
                {
                    delta.X = e.Delta > 0 ? -panSpeed : panSpeed;
                }

                if (!IsZoomedIn)
                {
                    MovedFromOrigin_Unzoomed = true;
                }

                Position += delta;
                ClampPosition();
                InvalidateRender();
                return;
            }

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

            if (this is GLNodeGraphViewer)
            {
                scaleMinMax.Y = 2f;
            }

            TextureScale = Math.Clamp(TextureScale, scaleMinMax.X, scaleMinMax.Y);

            var pos = new Vector2(e.Location.X, e.Location.Y);
            var posPrev = (pos + PositionOld) / TextureScaleOld;
            var posNewScale = posPrev * TextureScale;
            Position = posNewScale - pos;

            ClampPosition();
            UpdateZoomLabel();

            if (Svg != null && TextureScaleOld != TextureScale)
            {
                // Reupload image with new scale
                Interlocked.Increment(ref NextBitmapVersion);
                Task.Run(GenerateNewSvgBitmap);
            }

            InvalidateRender();
        }

        private void ClampPosition()
        {
            Debug.Assert(GLControl != null);

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
            Debug.Assert(GLControl != null);

            Position = -new Vector2(
                GLControl.Width / 2f - ActualTextureSizeScaled.X / 2f,
                GLControl.Height / 2f - ActualTextureSizeScaled.Y / 2f
            );
        }

        protected override void OnResize()
        {
            base.OnResize();

            if (texture != null)
            {
                ClampPosition();
            }
        }

        private void SetupTextureFromUi(bool softwareDecode)
        {
            using var lockedGl = MakeCurrent();
            SetupTexture(softwareDecode);
        }

        private void SetTextureFilteringFromUi()
        {
            using var lockedGl = MakeCurrent();
            SetTextureFiltering();
        }

        private void SetupTexture(bool forceSoftwareDecode)
        {
            texture?.Delete();

            UploadTexture(forceSoftwareDecode);

            Debug.Assert(texture != null);

            SetTextureFiltering();

            if (Svg == null)
            {
                OriginalWidth = texture.Width;
                OriginalHeight = texture.Height;

                // Render software mips at full size
                if (forceSoftwareDecode && SelectedMip > 0 && Resource?.DataBlock is Texture textureData)
                {
                    OriginalWidth = textureData.Width;
                    OriginalHeight = textureData.Height;
                }
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

            shader = RendererContext.ShaderLoader.LoadShader("vrf.texture_decode", arguments);
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

                if (NextBitmapToSet != null)
                {
                    using (NextBitmapToSet)
                    {
                        UploadBitmap(NextBitmapToSet);
                    }

                    NextBitmapToSet = null;
                }

                return;
            }

            if (Resource == null)
            {
                return;
            }

            if (Resource.ResourceType == ResourceType.PostProcessing && Resource.DataBlock is PostProcessing postProcessingData)
            {
                var resolution = postProcessingData.GetColorCorrectionLUTDimension();
                var data = postProcessingData.GetColorCorrectionLUT();

                texture = new RenderTexture(TextureTarget.Texture3D, resolution, resolution, resolution, 1);

                GL.TextureStorage3D(texture.Handle, 1, SizedInternalFormat.Rgba8, resolution, resolution, resolution);
                GL.TextureSubImage3D(texture.Handle, 0, 0, 0, 0, resolution, resolution, resolution, PixelFormat.Rgba, PixelType.UnsignedByte, data);

                return;
            }

            if (Resource.DataBlock is not Texture textureData)
            {
                return;
            }

            var isCpuDecodedFormat = textureData.IsRawAnyImage;
            var swDecodeFlags = decodeFlags & softwareDecodeOnlyOptions;

            if (isCpuDecodedFormat || forceSoftwareDecode)
            {
                SKBitmap bitmap;

                // GUI provides hardware decoder for texture decoding, but here we do not want to use it
                var decoder = HardwareAcceleratedTextureDecoder.Decoder;
                HardwareAcceleratedTextureDecoder.Decoder = null;

                try
                {
                    bitmap = textureData.GenerateBitmap((uint)SelectedDepth, (CubemapFace)SelectedCubeFace, (uint)SelectedMip, swDecodeFlags);
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

            texture = RendererContext.MaterialLoader.LoadTexture(Resource, isViewerRequest: true);
            InvalidateRender();
        }

        private void UploadBitmap(SKBitmap bitmap)
        {
            Debug.Assert(bitmap != null);
            texture = MaterialLoader.LoadBitmapTexture(bitmap);
            InvalidateRender();
        }

        private void GenerateNewSvgBitmap()
        {
            Debug.Assert(Svg?.Picture != null);

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

        protected override void OnGLLoad()
        {
            if (Svg == null) /// Svg will be setup on <see cref="OnFirstPaint"/> because it needs to be rescaled
            {
                SetupTexture(false);
            }

            // Use non-msaa framebuffer for texture viewer
            if (MainFramebuffer != GLDefaultFramebuffer)
            {
                MainFramebuffer?.Delete();
                MainFramebuffer = GLDefaultFramebuffer;
            }

            Debug.Assert(MainFramebuffer != null);
            MainFramebuffer.ClearColor = OpenTK.Mathematics.Color4.White;
            MainFramebuffer.ClearMask = ClearBufferMask.ColorBufferBit;
        }

        protected override void OnFirstPaint()
        {
            Debug.Assert(GLControl != null);
            Debug.Assert(UiControl != null);

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

            UiControl.BeginInvoke(UpdateZoomLabel);

            /// This will call <see cref="CenterPosition"/> since it could not have been moved by user on first paint yet
            ClampPosition();
        }

        protected override void OnUpdate(float deltaTime)
        {
            HandleArrowKeyMovement(deltaTime);
            TextureScaleChangeTime += deltaTime;
        }

        protected override void OnPaint(float frameTime)
        {
            Debug.Assert(MainFramebuffer is not null);
            Debug.Assert(GLControl is not null);

            base.OnPaint(frameTime);

            if (NextBitmapToSet != null)
            {
                texture?.Delete();

                using (NextBitmapToSet)
                {
                    UploadBitmap(NextBitmapToSet);
                }

                NextBitmapToSet = null;
            }

            var renderHash = HashCode.Combine(
                HashCode.Combine(
                    GetCurrentPositionAndScale(),
                    SelectedMip,
                    SelectedDepth,
                    SelectedCubeFace,
                    SelectedChannels.PackedValue,
                    ChannelSplitMode
                ),
                decodeFlags,
                SelectedFiltering,
                VisualizeTiling,
                ShowLightBackground,
                MainFramebuffer.Width,
                MainFramebuffer.Height
            );

            if (renderHash != LastRenderHash)
            {
                InvalidateRender();
            }

            const int NumBackBuffers = 2;
            if (NumRendersLastHash < NumBackBuffers)
            {
                GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
                MainFramebuffer.BindAndClear();
                Draw(MainFramebuffer);

                LastRenderHash = renderHash;
                NumRendersLastHash++;
            }
        }

        protected void InvalidateRender()
        {
            NumRendersLastHash = 0;
            GLControl?.Invalidate();
        }

        protected void Draw(Framebuffer fbo, bool captureFullSizeImage = false)
        {
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);

            Debug.Assert(shader != null);
            Debug.Assert(texture != null);

            shader.Use();

            shader.SetUniform1("g_bTextureViewer", true);
            shader.SetUniform1("g_bShowLightBackground", ShowLightBackground);
            shader.SetUniform2("g_vViewportSize", new Vector2(fbo.Width, fbo.Height));

            var theme1 = Themer.CurrentTheme == Themer.AppTheme.Dark
                ? Themer.CurrentThemeColors.Border
                : Themer.CurrentThemeColors.AppMiddle;
            shader.SetUniform3("g_vCheckerboardTheme", new Vector3(theme1.R, theme1.G, theme1.B) / 255f);

            var (scale, position) = captureFullSizeImage
                ? (1f / (1 << SelectedMip), Vector2.Zero)
                : GetCurrentPositionAndScale();

            shader.SetUniform1("g_bCapturingScreenshot", captureFullSizeImage);
            shader.SetUniform2("g_vViewportPosition", position);
            shader.SetUniform1("g_flScale", scale);

            shader.SetTexture("g_tInputTexture", texture);
            shader.SetUniform4("g_vInputTextureSize", new Vector4(OriginalWidth, OriginalHeight, texture.Depth, texture.NumMipLevels));
            shader.SetUniform1("g_nSelectedMip", SelectedMip);
            shader.SetUniform1("g_nSelectedDepth", SelectedDepth);
            shader.SetUniform1("g_nSelectedCubeFace", SelectedCubeFace);
            shader.SetUniform1("g_nSelectedChannels", SelectedChannels.PackedValue);
            shader.SetUniform1("g_bVisualizeTiling", VisualizeTiling);
            shader.SetUniform1("g_nChannelSplitMode", (int)ChannelSplitMode);
            shader.SetUniform1("g_nCubemapProjectionType", (int)CubemapProjectionType);
            shader.SetUniform1("g_nDecodeFlags", (int)decodeFlags);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        protected (float Scale, Vector2 Position) GetCurrentPositionAndScale()
        {
            var time = Math.Min(TextureScaleChangeTime / 0.4f, 1.0f);
            time = 1f - MathF.Pow(1f - time, 5f); // easeOutQuint

            var position = Vector2.Lerp(PositionOld, Position, time);
            var scale = float.Lerp(TextureScaleOld, TextureScale, time);

            return (scale, position);
        }

#if DEBUG
        private void OnHotReload(object? sender, string? e)
        {
            InvalidateRender();
        }
#endif
    }
}
