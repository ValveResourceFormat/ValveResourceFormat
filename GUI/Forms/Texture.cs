using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Blocks.ResourceEditInfoStructs;
using TextureData = ValveResourceFormat.ResourceTypes.Texture;
using Channels = ValveResourceFormat.CompiledShader.ChannelMapping;
using System.Diagnostics;
using ValveResourceFormat.TextureDecoders;

namespace GUI.Forms
{
    partial class Texture : UserControl
    {
        private string name;

        private readonly GLTextureDecoder hardwareDecoder;
        private Resource textureResource;
        private TextureData texture;

        private SKBitmap skBitmap;
        private CancellationTokenSource cts;
        private Task channelChangingTask;

        public Texture()
        {
            InitializeComponent();
        }

        public Texture(VrfGuiContext vrfGuiContext) : this()
        {
            var separateContext = new VrfGuiContext(null, vrfGuiContext); // Needs its own shader cache
            hardwareDecoder = new GLTextureDecoder(separateContext);
        }

        public void SetTexture(Resource resource, bool hardwareDecode = false)
        {
            textureResource = resource;
            name = resource.FileName;
            texture = (TextureData)resource.DataBlock;

            hardwareDecodeCheckBox.Checked = hardwareDecode;
            CancelPreviousChannelChange();
            SetChannels(Channels.RGBA);
        }

        public void SetImage(SKBitmap skBitmap, string name, int w, int h)
        {
            this.skBitmap = skBitmap;
            viewChannelsTransparent.Enabled = skBitmap.AlphaType != SKAlphaType.Opaque;
            SetImage(skBitmap.ToBitmap(), name, w, h);
        }

        public void SetImage(Bitmap image, string name, int w, int h)
        {
            var previous = pictureBox1.Image;
            pictureBox1.Image = image;
            this.name = name;
            pictureBox1.MaximumSize = new Size(w, h);
            previous?.Dispose();
        }

        /// <summary>
        /// Set the image to a specific channel component. E.g. Red channel.
        /// </summary>
        private void SetChannels(Channels channels)
        {
            if (hardwareDecodeCheckBox.Checked)
            {
                DecodeTextureGpu(channels);
                return;
            }

            var sw = Stopwatch.StartNew();
            var decodeTime = 0f;
            var totalTime = 0f;

            if (skBitmap == null)
            {
                if (texture == null)
                {
                    return;
                }

                name ??= textureResource.FileName;
                skBitmap = texture.GenerateBitmap();

                decodeTime = sw.ElapsedMilliseconds;

                if (cts.IsCancellationRequested)
                {
                    return;
                }
            }

            var useOriginal = channels == Channels.RGBA || (skBitmap.AlphaType == SKAlphaType.Opaque && channels == Channels.RGB);
            if (useOriginal)
            {
                Invoke(() => SetImage(skBitmap, name, skBitmap.Width, skBitmap.Height));
                totalTime = sw.ElapsedMilliseconds;
                Log.Debug(nameof(Texture), $"Software decode succeeded in {decodeTime}ms (ToBitmap overhead: {totalTime - decodeTime}ms)");
                return;
            }

            using var newSkiaBitmap = ValveResourceFormat.IO.TextureExtract.ToBitmapChannels(skBitmap, channels);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            Invoke(() => SetImage(newSkiaBitmap.ToBitmap(), name, newSkiaBitmap.Width, newSkiaBitmap.Height));

            totalTime = sw.ElapsedMilliseconds;
            Log.Debug(nameof(Texture), $"Software decode succeeded in {totalTime}ms (channel processing)");
        }

        private bool DecodeTextureGpu(Channels channels)
        {
            if (texture is null)
            {
                return false;
            }

            var decodeFlags = TextureCodec.None;

            if (textureResource is not null)
            {
                decodeFlags = TextureData.RetrieveCodecFromResourceEditInfo(textureResource);
            }

            // using?
            using var bitmap = new SKBitmap(texture.Width, texture.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);

            using var request = new GLTextureDecoder.DecodeRequest(bitmap, texture, 0, 0, channels, decodeFlags);
            var success = hardwareDecoder.Decode(request);

            if (!success)
            {
                return false;
            }

            DrawSpriteSheetOverlay(bitmap);
            SetImage(bitmap.ToBitmap(), name, texture.Width, texture.Height);
            return true;
        }

        private void DrawSpriteSheetOverlay(SKBitmap bitmap)
        {
            var sheet = texture?.GetSpriteSheetData();
            if (sheet == null)
            {
                return;
            }

            using var canvas = new SKCanvas(bitmap);
            using var color1 = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(0, 100, 255, 200),
                StrokeWidth = 1,
            };
            using var color2 = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = new SKColor(255, 100, 0, 200),
                StrokeWidth = 1,
            };

            foreach (var sequence in sheet.Sequences)
            {
                foreach (var frame in sequence.Frames)
                {
                    foreach (var image in frame.Images)
                    {
                        canvas.DrawRect(image.GetCroppedRect(bitmap.Width, bitmap.Height), color1);
                        canvas.DrawRect(image.GetUncroppedRect(bitmap.Width, bitmap.Height), color2);
                    }
                }
            }
        }

        private Channels GetSelectedChannelMode()
        {
            foreach (var item in viewChannelsToolStripMenuItem.DropDownItems)
            {
                if (item is ToolStripMenuItem menuItem && menuItem.Checked)
                {
                    return (Channels)menuItem.Tag;
                }
            }

            return Channels.RGBA;
        }

        private void HardwareDecodeCheckBox_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            item.Checked = !item.Checked;

            var channels = GetSelectedChannelMode();
            CancelPreviousChannelChange();
            SetChannels(channels);
        }

        private void OnChannelMenuItem_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            if (item.Checked)
            {
                return;
            }

            CancelPreviousChannelChange();

            channelChangingTask = Task.Run(() => SetChannels((Channels)item.Tag), cts.Token);
            channelChangingTask.ContinueWith((t) =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    foreach (var i in item.GetCurrentParent().Items)
                    {
                        if (i is not ToolStripMenuItem menuItem)
                        {
                            continue;
                        }

                        Invoke(() => menuItem.Checked = menuItem == item);
                    }
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void CancelPreviousChannelChange()
        {
            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();
        }

        private void ContextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem.Name != "saveAsToolStripMenuItem")
            {
                return;
            }

            var menuStrip = sender as ContextMenuStrip;
            menuStrip.Visible = false; //Hide it as we have pressed the button now!

            using var saveFileDialog = new SaveFileDialog
            {
                InitialDirectory = Settings.Config.SaveDirectory,
                Filter = "PNG Image|*.png|JPG Image|*.jpg|Tiff Image|*.tiff|Bitmap Image|*.bmp",
                Title = "Save an Image File",
                FileName = name,
                AddToRecent = true,
            };

            if (saveFileDialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            Settings.Config.SaveDirectory = Path.GetDirectoryName(saveFileDialog.FileName);

            var format = ImageFormat.Png;

            switch (saveFileDialog.FilterIndex)
            {
                case 2:
                    format = ImageFormat.Exif;
                    break;

                case 3:
                    format = ImageFormat.Tiff;
                    break;
                case 4:
                    format = ImageFormat.Bmp;
                    break;
            }

            using var fs = (FileStream)saveFileDialog.OpenFile();
            pictureBox1.Image.Save(fs, format);
        }
    }
}
