using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class Texture : UserControl
    {
        private string name;

        public Texture()
        {
            InitializeComponent();
        }

        public void SetImage(Bitmap image, string name, int w, int h)
        {
            pictureBox1.Image = image;
            this.name = name;
            pictureBox1.MaximumSize = new Size(w, h);
        }

        private void ContextMenuStrip1_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            if (e.ClickedItem.Name != "saveAsToolStripMenuItem")
            {
                return;
            }

            var menuStrip = sender as ContextMenuStrip;
            menuStrip.Visible = false; //Hide it as we have pressed the button now!

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PNG Image|*.png|JPG Image|*.jpg|Tiff Image|*.tiff|Bitmap Image|*.bmp",
                Title = "Save an Image File",
                FileName = name,
            };
            saveFileDialog.ShowDialog(this);

            if (saveFileDialog.FileName != string.Empty)
            {
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

                using (var fs = (FileStream)saveFileDialog.OpenFile())
                {
                    pictureBox1.Image.Save(fs, format);
                }
            }
        }
    }
}
