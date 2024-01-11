using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat;
using Channels = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace GUI.Forms
{
    partial class Texture : UserControl
    {
        private string name;

        private bool useHardwareDecoding;
        private GLTextureDecoder hardwareDecoder;
        private Resource textureResource;

        private SKBitmap skBitmap;
        private CancellationTokenSource cts;
        private Task channelChangingTask;

        public Texture()
        {
            InitializeComponent();
        }

        public void InitGpuDecoder(VrfGuiContext vrfGuiContext, Resource resource)
        {
            hardwareDecoder = new GLTextureDecoder(vrfGuiContext);
            textureResource = resource;
        }

        public void SetFromGpu()
        {
            if (!useHardwareDecoding)
            {
                return;
            }

            var bitmap = new SKBitmap(skBitmap.Width, skBitmap.Height);
            hardwareDecoder.Decode(bitmap, textureResource, 0, 0, Channels.RGBA);
            SetImage(bitmap, name, skBitmap.Width, skBitmap.Height);
        }

        public void SetImage(SKBitmap skBitmap, string name, int w, int h)
        {
            this.skBitmap = skBitmap;
            viewChannelsTransparent.Enabled = skBitmap.AlphaType != SKAlphaType.Opaque;
            SetImage(skBitmap.ToBitmap(), name, w, h);
        }

        public void SetImage(Bitmap image, string name, int w, int h)
        {
            pictureBox1.Image = image;
            this.name = name;
            pictureBox1.MaximumSize = new Size(w, h);
        }

        /// <summary>
        /// Set the image to a specific channel component. E.g. Red channel.
        /// </summary>
        private void SetChannels(Channels channels)
        {
            if (useHardwareDecoding)
            {
                SetChannelsGpu(channels);
                return;
            }

            if (skBitmap == null)
            {
                return;
            }

            Image image = null;
            if (channels == Channels.RGBA || (skBitmap.AlphaType == SKAlphaType.Opaque && channels == Channels.RGB))
            {
                image = skBitmap.ToBitmap();
            }
            else
            {
                var pngBytes = ValveResourceFormat.IO.TextureExtract.ToPngImageChannels(skBitmap, channels);
                image = Image.FromStream(new MemoryStream(pngBytes));
            }

            try
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }

                var imageRef = image;

                Invoke(() =>
                {
                    pictureBox1.Image.Dispose();
                    pictureBox1.Image = imageRef;
                });

                image = null;
            }
            finally
            {
                image?.Dispose();
            }
        }

        private static void SetChannelsGpu(Channels channels)
        {
            //throw new NotImplementedException();
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
                    format = ImageFormat.Jpeg;
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

        private void HardwareDecodeCheckBox_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;

            if (item.Checked)
            {
                item.Checked = false;
                useHardwareDecoding = false;

                pictureBox1.Image.Dispose();
                pictureBox1.Image = skBitmap.ToBitmap();
                return;
            }

            item.Checked = true;
            useHardwareDecoding = true;
            SetFromGpu();
        }

        private void OnChannelMenuItem_Click(object sender, EventArgs e)
        {
            var item = sender as ToolStripMenuItem;
            if (item.Checked)
            {
                return;
            }

            cts?.Cancel();
            cts?.Dispose();
            cts = new CancellationTokenSource();

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
    }
}
