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
using Channels = ValveResourceFormat.CompiledShader.ChannelMapping;

namespace GUI.Forms
{
    partial class Texture : UserControl
    {
        private string name;

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

            if (skBitmap == null)
            {
                return;
            }

            Bitmap bitmap = null;
            var useOriginal = channels == Channels.RGBA || (skBitmap.AlphaType == SKAlphaType.Opaque && channels == Channels.RGB);
            if (useOriginal)
            {
                bitmap = skBitmap.ToBitmap();
            }
            else
            {
                var newSkiaBitmap = ValveResourceFormat.IO.TextureExtract.ToBitmapChannels(skBitmap, channels);
                bitmap = newSkiaBitmap.ToBitmap();
            }

            try
            {
                if (cts is null || cts.IsCancellationRequested)
                {
                    return;
                }

                var bitmapRef = bitmap;

                Invoke(() =>
                {
                    SetImage(bitmap, name, skBitmap.Width, skBitmap.Height);
                });

                bitmap = null;
            }
            finally
            {
                bitmap?.Dispose();
            }
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

            var channels = GetSelectedChannelMode();

            if (item.Checked)
            {
                item.Checked = false;
                SetChannels(channels);
                return;
            }

            item.Checked = true;
            DecodeTextureGpu(GetSelectedChannelMode());
        }

        private void DecodeTextureGpu(Channels channels)
        {
            if (textureResource is null)
            {
                return;
            }

            var hemiOctRB = false;

            if (textureResource.EditInfo.Structs.TryGetValue(ResourceEditInfo.REDIStruct.SpecialDependencies, out var specialDepsRedi))
            {
                var specialDeps = (SpecialDependencies)specialDepsRedi;
                hemiOctRB = specialDeps.List.Any(dependancy => dependancy.CompilerIdentifier == "CompileTexture" && dependancy.String == "Texture Compiler Version Mip HemiOctIsoRoughness_RG_B");
            }

            // using?
            using var bitmap = new SKBitmap(skBitmap.Width, skBitmap.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            hardwareDecoder.Decode(new GLTextureDecoder.DecodeRequest(bitmap, textureResource, 0, 0, channels)
            {
                HemiOctRB = hemiOctRB,
            });

            var previous = pictureBox1.Image;
            SetImage(bitmap.ToBitmap(), name, skBitmap.Width, skBitmap.Height);
            previous.Dispose();
        }

        private Channels GetSelectedChannelMode()
        {
            foreach (var item in viewChannelsToolStripMenuItem.DropDownItems)
            {
                if (item is not ToolStripMenuItem menuItem)
                {
                    continue;
                }

                if (menuItem.Checked)
                {
                    return (Channels)menuItem.Tag;
                }
            }

            return Channels.RGBA;
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
