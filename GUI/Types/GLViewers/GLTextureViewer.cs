using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Svg.Skia;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.Shaders;
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

        enum SpriteSheetDisplay
        {
            FullSheet,
            OutlineFrame,
            CropToFrame,
        }

        static readonly string[] SpriteSheetDisplayNames = ["Full sheet", "Outline current frame", "Crop to current frame"];

        /// <summary>
        /// Largest animation rate the speed slider reaches, in passes per second, or in frames per
        /// second when animating in FPS.
        /// </summary>
        const float SpriteRateSliderMax = 4f;
        const float SpriteFpsSliderMax = 60f;

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

        private SpritesheetData? SpriteSheetData;
        private int SelectedSequence;
        private SpriteSheetDisplay SpriteSheetDisplayMode = SpriteSheetDisplay.OutlineFrame;
        private bool IsSpritePlaying = true;
        private bool SpriteLoop = true;
        private float SpriteCyclePosition;
        private float SpriteAnimationRate = 1f;
        private bool SpriteAnimateInFps;
        private float spriteRateSliderValue = 1f / SpriteRateSliderMax;
        private int CurrentSpriteFrame;
        private Label? spriteFrameLabel;
        private GLViewerSliderControl? spriteFrameTrackBar;
        private GLViewerSliderControl? spriteSpeedTrackBar;

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

                if (SpriteSheetDisplayMode == SpriteSheetDisplay.CropToFrame && TryGetCurrentSpriteFrameRect(out var frameRect))
                {
                    size *= new Vector2(frameRect.Z - frameRect.X, frameRect.W - frameRect.Y);
                }

                return size;
            }
        }

        private Vector2 ActualTextureSizeScaled => ActualTextureSize * TextureScale;
        private bool IsZoomedIn;
        private bool MovedFromOrigin_Unzoomed;

        protected int LastRenderHash;
        protected bool RenderUpToDate;

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
            rendererContext.MaxTextureSize = int.MaxValue;

#if DEBUG
            ShaderHotReload.ShadersReloaded += OnHotReload;
#endif
        }

        protected virtual bool ShowResetZoomButton => true;

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            if (GLControl != null)
            {
                GLControl.VisibleChanged += OnVisibleChanged;
            }

            ShowLightBackground = !Application.IsDarkModeEnabled;

            UpdateZoomLabel();

            if (ShowResetZoomButton)
            {
                var resetButton = new ThemedButton
                {
                    Text = "Reset zoom",
                    AutoSize = true,
                };

                resetButton.Click += (_, __) => ResetZoom();

                UiControl.AddControl(resetButton);
            }

            AddSaveButton();

            if (Bitmap != null)
            {
                // Image viewer
                AddChannelsComboBox(HasTranslucentPixels(Bitmap));
            }
            else if (Svg != null)
            {
                // Svg viewer
                AddChannelsComboBox(transparentByDefault: true);
            }
            else if (Resource != null)
            {
                InitializeUIControlsForResource();
            }

            base.AddUiControls();
        }

        /// <summary>The save/copy row, exposed so a viewer can reorder it within its sidebar.</summary>
        protected Control? SaveSection { get; private set; }

        private void AddSaveButton()
        {
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
            SaveSection = saveTable;
        }

        private void InitializeUIControlsForResource()
        {
            Debug.Assert(Resource != null);
            Debug.Assert(UiControl != null);

            if (Resource.ResourceType == ResourceType.PanoramaVectorGraphic)
            {
                AddChannelsComboBox(Svg != null);
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

            ComboBox? cubemapProjectionComboBox = null;
            CheckBox? softwareDecodeCheckBox = null;
            ComboBox? depthComboBox = null;

            using (UiControl.BeginGroup("Texture"))
            {
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
                    SelectedFiltering = Filtering.Linear;
                }

                decodeFlags = textureData.RetrieveCodecFromResourceEditInfo();
            }

            decodeFlagsListBox = UiControl.AddMultiSelection("Texture Conversion",
                SetInitialDecodeFlagsState,
                checkedItemNames =>
                {
                    decodeFlags = TextureCodec.None;

                    foreach (var itemName in checkedItemNames)
                    {
                        decodeFlags |= Enum.Parse<TextureCodec>(itemName);
                    }

                    SetupTextureFromUi(softwareDecodeCheckBox != null && softwareDecodeCheckBox.Checked);
                }
            );

            using (UiControl.BeginGroup("View"))
            {
                AddChannelsComboBox(HasTranslucentPixels(textureData, decodeFlags));

                var forceSoftwareDecode = textureData.IsRawAnyImage;
                var projectionBeforeSoftwareDecode = (int)CubemapProjection.Equirectangular;
                softwareDecodeCheckBox = UiControl.AddCheckBox("Software decode", forceSoftwareDecode, (state) =>
                {
                    if (cubemapProjectionComboBox != null)
                    {
                        if (state)
                        {
                            // Software decode can't project cubemaps; force a single face, remembering the projection.
                            projectionBeforeSoftwareDecode = cubemapProjectionComboBox.SelectedIndex;
                            cubemapProjectionComboBox.SelectedIndex = (int)CubemapProjection.None;
                            cubemapProjectionComboBox.Enabled = false;
                        }
                        else
                        {
                            cubemapProjectionComboBox.SelectedIndex = projectionBeforeSoftwareDecode;
                            cubemapProjectionComboBox.Enabled = true;
                        }
                    }

                    SetupTextureFromUi(state);
                });

                UiControl.AddCheckBox("Show UV Tiling", false, (state) =>
                {
                    var previousSize = ActualTextureSizeScaled;

                    VisualizeTiling = state;

                    TextureDimensionsChanged(previousSize);

                    SetTextureFilteringFromUi();
                });

                if (forceSoftwareDecode)
                {
                    softwareDecodeCheckBox.Enabled = false;
                }
            }

            AddSpriteSheetControls(textureData);
        }

        private void AddSpriteSheetControls(Texture textureData)
        {
            Debug.Assert(UiControl != null);

            var spriteSheetData = textureData.GetSpriteSheetData();

            if (spriteSheetData == null || spriteSheetData.Sequences.Length == 0)
            {
                return;
            }

            SpriteSheetData = spriteSheetData;

            using var _ = UiControl.BeginGroup("Sprite Sheet");

            ComboBox? sequenceComboBox = null;

            if (spriteSheetData.Sequences.Length > 1)
            {
                sequenceComboBox = UiControl.AddSelection("Sequence", (name, index) =>
                {
                    SelectedSequence = index;
                    SpriteCyclePosition = 0f;
                    SetSpriteFrame(spriteSheetData.Sequences[index], 0);

                    if (spriteFrameTrackBar != null)
                    {
                        spriteFrameTrackBar.Slider.Value = 0f;
                    }

                    RecenterTexture();
                });

                sequenceComboBox.Items.AddRange([.. spriteSheetData.Sequences.Select(GetSequenceDisplayName)]);
            }

            spriteFrameLabel = new Label
            {
                AutoSize = true,
            };

            UiControl.AddControl(spriteFrameLabel);

            var displayComboBox = UiControl.AddSelection("Display", (name, index) =>
            {
                SpriteSheetDisplayMode = (SpriteSheetDisplay)index;
                RecenterTexture();
            });

            displayComboBox.Items.AddRange(SpriteSheetDisplayNames);

            UiControl.AddCheckBox("Autoplay", IsSpritePlaying, isChecked => IsSpritePlaying = isChecked);
            UiControl.AddCheckBox("Loop", SpriteLoop, isChecked => SpriteLoop = isChecked);

            spriteFrameTrackBar = UiControl.AddTrackBar(value =>
            {
                var sequence = spriteSheetData.Sequences[SelectedSequence];
                var frameCount = sequence.Frames.Length;

                if (frameCount == 0)
                {
                    return;
                }

                var frame = Math.Clamp((int)(value * frameCount), 0, frameCount - 1);

                SpriteCyclePosition = sequence.GetFrameStartTime(frame) / sequence.EffectiveTotalTime;
                SetSpriteFrame(sequence, frame);
            });

            UiControl.AddCheckBox("Animate in FPS", SpriteAnimateInFps, isChecked =>
            {
                SpriteAnimateInFps = isChecked;
                UpdateSpriteAnimationRate();
            });

            spriteSpeedTrackBar = UiControl.AddTrackBar(value =>
            {
                spriteRateSliderValue = value;
                UpdateSpriteAnimationRate();
            }, spriteRateSliderValue);

            displayComboBox.SelectedIndex = (int)SpriteSheetDisplayMode;

            if (sequenceComboBox != null)
            {
                sequenceComboBox.SelectedIndex = 0;
            }

            SetSpriteFrame(spriteSheetData.Sequences[0], 0);
        }

        /// <summary>
        /// Formats a sequence as its id and frame count, with its name in between when the name is
        /// neither the authoring class name nor a bare number.
        /// </summary>
        private static string GetSequenceDisplayName(SpritesheetData.Sequence sequence)
        {
            var isDescriptive = sequence.IsNamed && !uint.TryParse(sequence.Name, out _);

            return isDescriptive
                ? $"#{sequence.Id} {sequence.Name} ({sequence.Frames.Length} frames)"
                : $"#{sequence.Id} ({sequence.Frames.Length} frames)";
        }

        private void UpdateSpriteAnimationRate()
        {
            SpriteAnimationRate = spriteRateSliderValue * (SpriteAnimateInFps ? SpriteFpsSliderMax : SpriteRateSliderMax);

            if (SpriteSheetData != null)
            {
                SetSpriteFrameLabel(SpriteSheetData.Sequences[SelectedSequence], CurrentSpriteFrame);
            }
        }

        private void SetSpriteFrame(SpritesheetData.Sequence sequence, int frame)
        {
            CurrentSpriteFrame = frame;

            SetSpriteFrameLabel(sequence, frame);
        }

        private void SetSpriteFrameLabel(SpritesheetData.Sequence sequence, int frame)
        {
            if (spriteFrameLabel != null)
            {
                var unit = SpriteAnimateInFps ? "frames/s" : "passes/s";
                spriteFrameLabel.Text = $"Frame: {frame + 1} / {sequence.Frames.Length}    Rate: {SpriteAnimationRate:0.##} {unit}";
            }
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

        private const int TranslucencyScanPixelLimit = 2048 * 2048;
        private const TextureCodec DataInAlphaCodecs = TextureCodec.YCoCg | TextureCodec.RGBM | TextureCodec.HemiOctRB | TextureCodec.NormalizeNormals | TextureCodec.Dxt5nm;

        private static bool HasTranslucentPixels(Texture textureData, TextureCodec codec)
        {
            if (textureData.Format == VTexFormat.UNKNOWN || (codec & DataInAlphaCodecs) != 0)
            {
                return false;
            }

            var mipLevel = Math.Max(textureData.NumMipLevels - 1, 0);
            var width = Math.Max(textureData.Width >> mipLevel, 1);
            var height = Math.Max(textureData.Height >> mipLevel, 1);

            if (width * height > TranslucencyScanPixelLimit)
            {
                return false;
            }

            using var bitmap = textureData.GenerateBitmap(mipLevel: (uint)mipLevel);
            return HasTranslucentPixels(bitmap);
        }

        private static bool HasTranslucentPixels(SKBitmap bitmap)
        {
            if (bitmap.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888))
            {
                return false;
            }

            var pixels = bitmap.GetPixelSpan();

            for (var i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != byte.MaxValue)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddChannelsComboBox(bool transparentByDefault)
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

            channelsComboBox.Items.AddRange([.. ChannelsComboBoxOrder.Select(c => (object)c.ChoiceString)]);

            var defaultChannels = transparentByDefault ? ChannelMapping.RGBA : ChannelMapping.RGB;
            channelsComboBox.SelectedIndex = Array.FindIndex(ChannelsComboBoxOrder, channel => channel.Channels == defaultChannels && channel.ChannelSplitMode == ChannelSplitting.None);

            var samplingComboBox = UiControl.AddSelection("Sampling", (name, index) =>
            {
                SelectedFiltering = (Filtering)index;
                SetTextureFilteringFromUi();
            });

            samplingComboBox.Items.AddRange(Enum.GetNames<Filtering>());
            samplingComboBox.SelectedIndex = (int)SelectedFiltering;
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
                texture.SetWrapMode(VisualizeTiling ? RsTextureAddressMode.Wrap : RsTextureAddressMode.Clamp);
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

        private void OnVisibleChanged(object? sender, EventArgs e)
        {
            if (GLControl?.Visible == true)
            {
                InvalidateRender();
            }
        }

        public override void NotifyVisible()
        {
            if (GLControl?.Visible == true)
            {
                InvalidateRender();
            }
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
            if (GLControl != null)
            {
                GLControl.VisibleChanged -= OnVisibleChanged;
            }

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

            spriteFrameTrackBar?.Dispose();
            spriteFrameTrackBar = null;
            spriteSpeedTrackBar?.Dispose();
            spriteSpeedTrackBar = null;
            spriteFrameLabel?.Dispose();
            spriteFrameLabel = null;
            SpriteSheetData = null;

            base.Dispose();
        }

        /// <summary>
        /// Whether there is anything to write out. Viewers that draw their own content instead of
        /// a texture override this and answer with <see cref="ReadPixelsToBitmap"/>.
        /// </summary>
        protected virtual bool CanSaveVisual => Resource != null || Svg != null || Bitmap != null;

        private void OnSaveButtonClick(object? sender, EventArgs e)
        {
            if (!CanSaveVisual)
            {
                return;
            }

            var fileName = (Resource != null
                ? Path.GetFileNameWithoutExtension(Resource.FileName)
                : Path.GetFileNameWithoutExtension(VrfGuiContext.FileName)) ?? string.Empty;

            // The svg export picks format (and raster resolution) up front, before the file dialog.
            if (Svg?.Picture != null)
            {
                SaveSvg(fileName);
                return;
            }

            var filter = "PNG Image|*.png|JPG Image|*.jpg";
            var alternativeImageFormatIndex = 2;

            var isHdrTexture = Resource?.DataBlock is Texture textureData && textureData.IsHighDynamicRange;

            if (isHdrTexture)
            {
                filter = "EXR Image|*.exr|" + filter;
                alternativeImageFormatIndex++;
            }

            var savePath = AppFileDialogs.SaveFile("Save an Image File", fileName, null, filter, out var selectedFilterIndex);

            if (savePath == null)
            {
                return;
            }

            using var fs = File.Create(savePath);

            if (isHdrTexture && selectedFilterIndex == 1)
            {
                using var hdrBitmap = ReadTexturePixels(hdr: true);
                fs.Write(ValveResourceFormat.IO.TextureExtract.ToExrImage(hdrBitmap));
                return;
            }

            var format = SKEncodedImageFormat.Png;

            switch (selectedFilterIndex - alternativeImageFormatIndex)
            {
                case 0:
                    format = SKEncodedImageFormat.Jpeg;
                    break;
            }

            // TODO: nonpow2 sizes?
            using var bitmap = ReadPixelsToBitmap();
            using var bitmapPixmap = bitmap.PeekPixels();
            bitmapPixmap.Encode(fs, format, 100);
        }

        private void SaveSvg(string fileName)
        {
            Debug.Assert(Svg?.Picture != null);

            using var exportForm = new SvgExportForm(OriginalWidth, OriginalHeight, Resource?.DataBlock is Panorama);
            if (exportForm.ShowDialog(UiControl) != DialogResult.OK)
            {
                return;
            }

            var (filter, extension) = exportForm.SelectedFormat switch
            {
                SvgExportFormat.Svg => ("SVG (Scalable Vector Graphics)|*.svg", "svg"),
                SvgExportFormat.Jpg => ("JPG Image|*.jpg", "jpg"),
                _ => ("PNG Image|*.png", "png"),
            };

            var savePath = AppFileDialogs.SaveFile("Save an Image File", $"{fileName}.{extension}", null, filter);

            if (savePath == null)
            {
                return;
            }

            using var fs = File.Create(savePath);

            if (exportForm.SelectedFormat == SvgExportFormat.Svg && Resource?.DataBlock is Panorama panoramaData)
            {
                fs.Write(panoramaData.Data);
                return;
            }

            var scale = exportForm.SelectedScale;
            var format = exportForm.SelectedFormat == SvgExportFormat.Jpg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png;

            using var svgBitmap = RasterizeSvg(Svg.Picture, OriginalWidth * scale, OriginalHeight * scale);
            using var pixmap = svgBitmap.PeekPixels();
            pixmap.Encode(fs, format, 100);
        }

        protected override SKBitmap ReadPixelsToBitmap()
        {
            if (Svg?.Picture != null)
            {
                var (svgWidth, svgHeight) = GetSvgExportSize();
                return RasterizeSvg(Svg.Picture, svgWidth, svgHeight);
            }

            return ReadTexturePixels(hdr: false);
        }

        private SKBitmap ReadTexturePixels(bool hdr)
        {
            var removeFlags = hdr
                ? (TextureCodec.ColorSpaceLinear | TextureCodec.ColorSpaceSrgb)
                : TextureCodec.None;

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

                var fboFormat = hdr ? GLTextureDecoder.HDRFormat : GLTextureDecoder.LDRFormat;

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

                Draw(SaveAsFbo, captureFullSizeImage: true, removeFlags);

                GL.Flush();
                GL.Finish();

                SaveAsFbo.Bind(FramebufferTarget.ReadFramebuffer);
                GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                var readFormat = MaterialLoader.GetImageExportFormat(hdr);
                GL.ReadPixels(0, 0, bitmap.Width, bitmap.Height, readFormat.ToGLPixelFormat(), readFormat.ToGLPixelType(), pixels);

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

        protected void UpdateZoomLabel() => SetMoveSpeedOrZoomLabel($"Zoom: {TextureScale * 100:0.0}% (scroll to change)");

        protected override void OnKeyDown(Keys keyData)
        {
            base.OnKeyDown(keyData);

            InvalidateRender();

            if (keyData == (Keys.Control | Keys.S))
            {
                OnSaveButtonClick(null, EventArgs.Empty);
                return;
            }

            if (keyData == (Keys.Control | Keys.NumPad0) || keyData == (Keys.Control | Keys.D0))
            {
                ResetZoom();
                return;
            }

            Debug.Assert(GLControl != null);

            if (keyData == (Keys.Control | Keys.Add) || keyData == (Keys.Control | Keys.Oemplus))
            {
                HandleMouseWheel(1, new System.Drawing.Point(GLControl.Width / 2, GLControl.Height / 2), isShiftPressed: false, isCtrlPressed: false);
                return;
            }

            if (keyData == (Keys.Control | Keys.Subtract) || keyData == (Keys.Control | Keys.OemMinus))
            {
                HandleMouseWheel(-1, new System.Drawing.Point(GLControl.Width / 2, GLControl.Height / 2), isShiftPressed: false, isCtrlPressed: false);
                return;
            }
        }

        private void HandleArrowKeyMovement(float frameTime)
        {
            var movementKeys = CurrentlyPressedKeys &
                (TrackedKeys.W | TrackedKeys.S |
                 TrackedKeys.A | TrackedKeys.D);

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

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.W))
            {
                delta.Y -= moveDistance;
            }

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.S))
            {
                delta.Y += moveDistance;
            }

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.A))
            {
                delta.X -= moveDistance;
            }

            if (CurrentlyPressedKeys.HasFlag(TrackedKeys.D))
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

        protected override void OnMouseMove(int x, int y)
        {
            Debug.Assert(GLControl != null);

            GLControl.Focus();

            if (ClickPosition == null)
            {
                return;
            }

            var oldPosition = Position;
            var mousePosition = new Vector2(x, y);

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

        protected override void OnMouseWheel(int delta, System.Drawing.Point location)
        {
            var isShiftPressed = (CurrentlyPressedKeys & TrackedKeys.Shift) > 0;
            var isCtrlPressed = (CurrentlyPressedKeys & TrackedKeys.Control) > 0;

            HandleMouseWheel(delta, location, isShiftPressed, isCtrlPressed);
        }

        private void HandleMouseWheel(int delta, System.Drawing.Point location, bool isShiftPressed, bool isCtrlPressed)
        {
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

                var panDelta = Vector2.Zero;

                if (isShiftPressed)
                {
                    panDelta.Y = delta > 0 ? -panSpeed : panSpeed;
                }
                else if (isCtrlPressed)
                {
                    panDelta.X = delta > 0 ? -panSpeed : panSpeed;
                }

                if (!IsZoomedIn)
                {
                    MovedFromOrigin_Unzoomed = true;
                }

                Position += panDelta;
                ClampPosition();
                InvalidateRender();
                return;
            }

            (TextureScaleOld, PositionOld) = GetCurrentPositionAndScale();
            TextureScaleChangeTime = 0f;
            ClickPosition = null;

            if (delta < 0)
            {
                TextureScale /= 1.25f;
            }
            else
            {
                TextureScale *= 1.25f;
            }

            var scaleMinMax = new Vector2(0.1f, 50f);
            scaleMinMax *= 256 / MathF.Max(ActualTextureSize.X, ActualTextureSize.Y);

            if (this is GLGraphViewer graphViewer)
            {
                scaleMinMax.X = graphViewer.MinTextureScale();
                scaleMinMax.Y = 2f;
            }

            TextureScale = Math.Clamp(TextureScale, scaleMinMax.X, scaleMinMax.Y);

            var pos = new Vector2(location.X, location.Y);
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
                // The far bound is negative on an axis where the texture is smaller than the control
                Position.X = MathUtils.Clamp(Position.X, 0, width - GLControl.Width);
                Position.Y = MathUtils.Clamp(Position.Y, 0, height - GLControl.Height);

                MovedFromOrigin_Unzoomed = false;
            }
            else if (MovedFromOrigin_Unzoomed)
            {
                Position.X = MathUtils.Clamp(Position.X, 0, width - GLControl.Width);
                Position.Y = MathUtils.Clamp(Position.Y, 0, height - GLControl.Height);
            }
            else
            {
                CenterPosition();
            }

            Position.X = MathF.Round(Position.X);
            Position.Y = MathF.Round(Position.Y);
        }

        private void RecenterTexture()
        {
            if (texture == null)
            {
                return;
            }

            MovedFromOrigin_Unzoomed = false;
            ClickPosition = null;
            TextureScaleOld = TextureScale;
            TextureScaleChangeTime = 0f;
            PositionOld = Position;

            CenterPosition();
            ClampPosition();
        }

        private void CenterPosition()
        {
            Debug.Assert(GLControl != null);

            Position = -new Vector2(
                GLControl.Width / 2f - ActualTextureSizeScaled.X / 2f,
                GLControl.Height / 2f - ActualTextureSizeScaled.Y / 2f
            );
        }

        protected override void OnResize(int w, int h)
        {
            base.OnResize(w, h);

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

            shader = RendererContext.ShaderLoader.LoadShader("texture_decode", (textureType, 1));
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

                texture = RenderTexture.Create3D(TextureTarget.Texture3D, resolution, resolution, resolution, ImageFormat.RGBA8888, 1, "ColorCorrectionLUT");

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

            texture = RendererContext.MaterialLoader.LoadTexture(Resource);
            InvalidateRender();
        }

        private void UploadBitmap(SKBitmap bitmap)
        {
            Debug.Assert(bitmap != null);
            texture = MaterialLoader.LoadBitmapTexture(bitmap);
            InvalidateRender();
        }

        /// <summary>
        /// Auto export dimensions for an svg, used when copying to the clipboard: the long edge is rounded
        /// up to the nearest power of two within [1024, 4096], the short edge scales proportionally. Saving
        /// to a file instead asks the user for an explicit multiple of the native resolution.
        /// </summary>
        private (float Width, float Height) GetSvgExportSize()
        {
            // Never export below the svg's native resolution, even when zoomed out to fit the viewport.
            var exportScale = MathF.Max(1f, TextureScale);
            var longEdge = (int)MathF.Ceiling(MathF.Max(OriginalWidth, OriginalHeight) * exportScale);
            longEdge = Math.Clamp(longEdge, 1024, 4096);
            var target = (int)BitOperations.RoundUpToPowerOf2((uint)longEdge);
            var scale = target / MathF.Max(OriginalWidth, OriginalHeight);
            return (MathF.Round(OriginalWidth * scale), MathF.Round(OriginalHeight * scale));
        }

        private static SKBitmap RasterizeSvg(SKPicture picture, float width, float height)
        {
            var imageInfo = new SKImageInfo((int)width, (int)height, SKColorType.Bgra8888, SKAlphaType.Premul, null);
            var bitmap = new SKBitmap(imageInfo);

            using var canvas = new SKCanvas(bitmap);
            canvas.Scale(width / picture.CullRect.Width, height / picture.CullRect.Height);
            canvas.DrawPicture(picture);

            return bitmap;
        }

        private void GenerateNewSvgBitmap()
        {
            Debug.Assert(Svg?.Picture != null);

            var version = NextBitmapVersion;

            var width = Svg.Picture.CullRect.Width * TextureScale;
            var height = Svg.Picture.CullRect.Height * TextureScale;

            var bitmap = RasterizeSvg(Svg.Picture, width, height);

            try
            {
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
            if (Svg == null) // Svg will be setup on OnFirstPaint because it needs to be rescaled
            {
                SetupTexture(false);
            }

            UseDefaultFramebuffer();
        }

        protected void UseDefaultFramebuffer()
        {
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

            // This will call CenterPosition since it could not have been moved by user on first paint yet
            ClampPosition();
        }

        protected override void OnUpdate(float deltaTime)
        {
            HandleArrowKeyMovement(deltaTime);
            TextureScaleChangeTime += deltaTime;

            UpdateSpritePlayback(deltaTime);
        }

        private void UpdateSpritePlayback(float deltaTime)
        {
            if (SpriteSheetData == null || spriteFrameTrackBar == null || !IsSpritePlaying || spriteFrameTrackBar.Slider.Clicked)
            {
                return;
            }

            var sequence = SpriteSheetData.Sequences[SelectedSequence];

            if (sequence.Frames.Length < 2)
            {
                return;
            }

            var passesPerSecond = SpriteAnimateInFps
                ? SpriteAnimationRate / sequence.EffectiveTotalTime
                : SpriteAnimationRate;

            var cycle = SpriteCyclePosition + deltaTime * passesPerSecond;

            SpriteCyclePosition = SpriteLoop
                ? cycle - MathF.Floor(cycle)
                : Math.Clamp(cycle, 0f, 1f);

            var (frame, _, _) = sequence.GetFrameAtPosition(SpriteCyclePosition * sequence.EffectiveTotalTime);

            if (frame == CurrentSpriteFrame)
            {
                return;
            }

            CurrentSpriteFrame = frame;

            var trackBar = spriteFrameTrackBar;

            if (trackBar.InvokeRequired)
            {
                trackBar.BeginInvoke(() => SyncSpriteFrameToPlayback(sequence, frame));
                return;
            }

            SyncSpriteFrameToPlayback(sequence, frame);
        }

        private void SyncSpriteFrameToPlayback(SpritesheetData.Sequence sequence, int frame)
        {
            if (spriteFrameTrackBar != null)
            {
                spriteFrameTrackBar.Slider.Value = (float)frame / sequence.Frames.Length;
            }

            SetSpriteFrameLabel(sequence, frame);
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

            var renderHash = GetRenderHash();

            if (renderHash != LastRenderHash)
            {
                LastRenderHash = renderHash;
                InvalidateRender();
            }

            if (RenderUpToDate)
            {
                SkipBufferSwap = true;
                return;
            }

            RenderUpToDate = true;
            RenderToFramebuffer();
        }

        protected virtual int GetRenderHash()
        {
            Debug.Assert(MainFramebuffer is not null);

            return HashCode.Combine(
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
                MainFramebuffer.Height,
                HashCode.Combine(CurrentSpriteFrame, SelectedSequence, SpriteSheetDisplayMode)
            );
        }

        protected virtual void RenderToFramebuffer()
        {
            Debug.Assert(MainFramebuffer is not null);
            Debug.Assert(GLControl is not null);

            GL.Viewport(0, 0, GLControl.Width, GLControl.Height);
            MainFramebuffer.BindAndClear();
            Draw(MainFramebuffer);
        }

        protected void InvalidateRender()
        {
            RenderUpToDate = false;
            GLControl?.Invalidate();
        }

        protected void Draw(Framebuffer fbo, bool captureFullSizeImage = false, TextureCodec removeFlags = TextureCodec.None)
        {
            GL.DepthMask(false);
            GL.Disable(EnableCap.DepthTest);

            Debug.Assert(shader != null);
            Debug.Assert(texture != null);

            shader.Use();

            shader.SetUniform("g_bTextureViewer", true);
            shader.SetUniform("g_bShowLightBackground", ShowLightBackground);
            shader.SetUniform("g_vViewportSize", new Vector2(fbo.Width, fbo.Height));

            var theme1 = Themer.CurrentTheme == Themer.AppTheme.Dark
                ? Themer.CurrentThemeColors.Border
                : Themer.CurrentThemeColors.AppMiddle;
            shader.SetUniform("g_vCheckerboardTheme", new Vector3(theme1.R, theme1.G, theme1.B) / 255f);

            var (scale, position) = captureFullSizeImage
                ? (1f / (1 << SelectedMip), Vector2.Zero)
                : GetCurrentPositionAndScale();

            shader.SetUniform("g_bCapturingScreenshot", captureFullSizeImage);
            shader.SetUniform("g_vViewportPosition", position);
            shader.SetUniform("g_flScale", scale);

            shader.SetTexture(0, "g_tInputTexture", texture);
            shader.SetUniform("g_vInputTextureSize", new Vector4(OriginalWidth, OriginalHeight, texture.Depth, texture.NumMipLevels));
            shader.SetUniform("g_nSelectedMip", SelectedMip);
            shader.SetUniform("g_nSelectedDepth", SelectedDepth);
            shader.SetUniform("g_nSelectedCubeFace", SelectedCubeFace);
            shader.SetUniform("g_nSelectedChannels", SelectedChannels.PackedValue);
            shader.SetUniform("g_bVisualizeTiling", VisualizeTiling);
            shader.SetUniform("g_nChannelSplitMode", (int)ChannelSplitMode);
            shader.SetUniform("g_nCubemapProjectionType", (int)CubemapProjectionType);
            shader.SetUniform("g_nDecodeFlags", (int)(decodeFlags & ~removeFlags));

            SetSpriteSheetUniforms(captureFullSizeImage);

            GL.BindVertexArray(RendererContext.MeshBufferCache.EmptyVAO);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        private bool TryGetCurrentSpriteFrameRect(out Vector4 frameRect)
        {
            frameRect = new Vector4(0f, 0f, 1f, 1f);

            if (SpriteSheetData == null)
            {
                return false;
            }

            var sequence = SpriteSheetData.Sequences[SelectedSequence];

            if (sequence.Frames.Length == 0)
            {
                return false;
            }

            var frame = sequence.Frames[Math.Clamp(CurrentSpriteFrame, 0, sequence.Frames.Length - 1)];

            if (frame.Images.Length == 0)
            {
                return false;
            }

            var image = frame.Images[0];
            frameRect = new Vector4(image.UncroppedMin.X, image.UncroppedMin.Y, image.UncroppedMax.X, image.UncroppedMax.Y);
            return true;
        }

        private void SetSpriteSheetUniforms(bool captureFullSizeImage)
        {
            Debug.Assert(shader != null);

            var mode = SpriteSheetDisplay.FullSheet;

            if (captureFullSizeImage || !TryGetCurrentSpriteFrameRect(out var frameRect))
            {
                frameRect = new Vector4(0f, 0f, 1f, 1f);
            }
            else
            {
                mode = SpriteSheetDisplayMode;
            }

            shader.SetUniform("g_nSpriteSheetMode", (int)mode);
            shader.SetUniform("g_vSpriteFrameMinMax", frameRect);
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
