using System;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace GUI
{
    public partial class MainForm : Form
    {
        public MainForm()
        {
            InitializeComponent();
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OpenFileDialog openDialog = new OpenFileDialog();
            openDialog.Filter = "Valve Resource Format (*.*_c)|*.*_c|Valve Package Format (*.vpk)|*.vpk";
            openDialog.FilterIndex = 1;
            var userOK = openDialog.ShowDialog();
            if (userOK == DialogResult.OK)
            {
                mainTabs.TabPages.Clear();
                if (openDialog.FileName.EndsWith(".vpk")) {
                    var package = new Package();

                    try
                    {
                        package.Read(openDialog.FileName);
                        var tab = new TabPage("Files");
                        var control = new TreeView();
                        control.Dock = DockStyle.Fill;

                        //http://stackoverflow.com/a/24591871
                        TreeNode currentnode;
                        foreach (var filetype in package.Entries)
                        {
                            foreach (var file in filetype.Value)
                            {
                                currentnode = null;
                                foreach (string subPath in (file.DirectoryName + Path.DirectorySeparatorChar + file.FileName + "." + filetype.Key).Split(Path.DirectorySeparatorChar))
                                {
                                    if (currentnode == null) //Root directory
                                    {
                                        if (subPath == " ")
                                            continue; //root files
                                        if (null == control.Nodes[subPath])
                                            currentnode = control.Nodes.Add(subPath, subPath);
                                        else
                                            currentnode = control.Nodes[subPath];
                                    }
                                    else //Not root directory
                                    {
                                        if (null == currentnode.Nodes[subPath])
                                            currentnode = currentnode.Nodes.Add(subPath, subPath);
                                        else
                                            currentnode = currentnode.Nodes[subPath];
                                    }
                                }
                            }
                        }
                        control.Sort();
                        control.ExpandAll();
                        tab.Controls.Add(control);
                        mainTabs.TabPages.Add(tab);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.Message);
                    }
                } else {
                    var resource = new Resource();
                    resource.Read(openDialog.FileName);
                    this.Text = "Valve Resource Format - " + openDialog.FileName;

                    switch (resource.ResourceType)
                    {
                        case ResourceType.Texture:
                            var tab = new TabPage("TEXTURE");
                            var control = new PictureBox();
                            control.Image = ((Texture)resource.Blocks[BlockType.DATA]).GenerateBitmap();
                            control.Dock = DockStyle.Fill;
                            tab.Controls.Add(control);
                            mainTabs.TabPages.Add(tab);
                            break;
                    }
                    foreach (var block in resource.Blocks)
                    {
                        var tab = new TabPage(block.Key.ToString());
                        var control = new TextBox();
                        control.Text = block.Value.ToString().Replace("\n", Environment.NewLine); //make sure panorama is new lines
                        control.Dock = DockStyle.Fill;
                        control.Multiline = true;
                        control.ReadOnly = true;
                        control.ScrollBars = ScrollBars.Both;
                        tab.Controls.Add(control);
                        mainTabs.TabPages.Add(tab);
                    }
                }
            }
        }
    }
}
