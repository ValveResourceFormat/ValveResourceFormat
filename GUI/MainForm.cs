using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;

namespace GUI
{
    public partial class MainForm : Form
    {
        private ImageList ImageList;

        public MainForm()
        {
            LoadAssetTypes();
            InitializeComponent();
        }

        private void LoadAssetTypes()
        {
            ImageList = new ImageList();

            var images = Directory.GetFiles("AssetTypes\\", "*.png");

            foreach (var image in images)
            {
                ImageList.Images.Add(Path.GetFileNameWithoutExtension(image), Image.FromFile(image));
            }
        }

        private void OnTabClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle)
            {
                var tabControl = sender as TabControl;
                var tabs = tabControl.TabPages;

                tabs.Remove(tabs.Cast<TabPage>()
                    .Where((t, i) => tabControl.GetTabRect(i).Contains(e.Location))
                    .First()
                );
            }
        }

        private void OnAboutItemClick(object sender, EventArgs e)
        {
            var form = new Forms.AboutForm();
            form.ShowDialog(this);
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var openDialog = new OpenFileDialog();
            openDialog.Filter = "Valve Resource Format (*.*_c, *.vpk)|*.*_c;*.vpk|All files (*.*)|*.*";
            openDialog.Multiselect = true;
            var userOK = openDialog.ShowDialog();

            if (userOK == DialogResult.OK)
            {
                foreach (var file in openDialog.FileNames)
                {
                    OpenFile(file);
                }
            }
        }

        private void OpenFile(string fileName)
        {
            var tab = new TabPage(Path.GetFileName(fileName));
            tab.Controls.Add(new Forms.LoadingFile());

            mainTabs.TabPages.Add(tab);
            mainTabs.SelectTab(tab);

            var task = Task.Factory.StartNew(() => ProcessFile(fileName));

            task.ContinueWith(t =>
            {
                t.Exception.Flatten().Handle(ex =>
                {
                    mainTabs.TabPages.Remove(tab);

                    MessageBox.Show(ex.Message, "Failed to read package", MessageBoxButtons.OK, MessageBoxIcon.Error);

                    return true;
                });
            }, TaskContinuationOptions.OnlyOnFaulted);

            task.ContinueWith(t =>
            {
                tab.Controls.Clear();

                foreach (Control c in t.Result.Controls)
                {
                    tab.Controls.Add(c);
                }
            }, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private TabPage ProcessFile(string fileName)
        {
            var tab = new TabPage();

            if (fileName.EndsWith(".vpk", StringComparison.Ordinal))
            {
                var package = new Package();
                package.Read(fileName);

                var control = new TreeView();
                control.Dock = DockStyle.Fill;
                control.ImageList = ImageList;

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
                                {
                                    continue; //root files
                                }

                                if (null == control.Nodes[subPath])
                                {
                                    currentnode = control.Nodes.Add(subPath, subPath);
                                }
                                else
                                {
                                    currentnode = control.Nodes[subPath];
                                }
                            }
                            else //Not root directory
                            {
                                if (null == currentnode.Nodes[subPath])
                                {
                                    currentnode = currentnode.Nodes.Add(subPath, subPath);
                                }
                                else
                                {
                                    currentnode = currentnode.Nodes[subPath];
                                }
                            }

                            var ext = Path.GetExtension(currentnode.Name);

                            if (ext.Length == 0)
                            {
                                ext = "_folder";
                            }
                            else
                            {
                                ext = ext.Substring(1);

                                if (ext.EndsWith("_c", StringComparison.Ordinal))
                                {
                                    ext = ext.Substring(0, ext.Length - 2);
                                }

                                if (!ImageList.Images.ContainsKey(ext))
                                {
                                    if (ext[0] == 'v')
                                    {
                                        ext = ext.Substring(1);

                                        if (!ImageList.Images.ContainsKey(ext))
                                        {
                                            ext = "_default";
                                        }
                                    }
                                    else
                                    {
                                        ext = "_default";
                                    }
                                }
                            }

                            currentnode.ImageKey = ext;
                            currentnode.SelectedImageKey = ext;
                        }
                    }
                }

                control.Sort();
                control.ExpandAll();
                tab.Controls.Add(control);
            }
            else
            {
                var resource = new Resource();
                resource.Read(fileName);

                var resTabs = new TabControl();
                resTabs.Dock = DockStyle.Fill;

                switch (resource.ResourceType)
                {
                    case ResourceType.Texture:
                        var tab2 = new TabPage("TEXTURE");
                        tab2.AutoScroll = true;

                        try
                        {
                            var tex = (Texture)resource.Blocks[BlockType.DATA];

                            var control = new Forms.Texture();
                            control.SetImage(tex.GenerateBitmap(), tex.Width, tex.Height);

                            tab2.Controls.Add(control);
                        }
                        catch (Exception e)
                        {
                            var control = new TextBox
                            {
                                Dock = DockStyle.Fill,
                                Font = new Font(FontFamily.GenericMonospace, 8),
                                Multiline = true,
                                ReadOnly = true,
                                Text = e.ToString()
                            };

                            tab2.Controls.Add(control);
                        }

                        resTabs.TabPages.Add(tab2);
                        break;
                }

                foreach (var block in resource.Blocks)
                {
                    if (block.Key == BlockType.RERL)
                    {
                        var externalRefsTab = new TabPage("External Refs");

                        var externalRefs = new DataGridView();
                        externalRefs.Dock = DockStyle.Fill;
                        externalRefs.AutoGenerateColumns = true;
                        externalRefs.AutoSize = true;
                        externalRefs.ReadOnly = true;
                        externalRefs.AllowUserToAddRows = false;
                        externalRefs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                        externalRefs.DataSource = new BindingSource(new BindingList<ResourceExtRefList.ResourceReferenceInfo>(resource.ExternalReferences.ResourceRefInfoList), null);

                        externalRefsTab.Controls.Add(externalRefs);

                        resTabs.TabPages.Add(externalRefsTab);

                        continue;
                    }

                    if (block.Key == BlockType.NTRO)
                    {
                        if (((ResourceIntrospectionManifest)block.Value).ReferencedStructs.Count > 0)
                        {
                            var externalRefsTab = new TabPage("Introspection Manifest: Structs");

                            var externalRefs = new DataGridView();
                            externalRefs.Dock = DockStyle.Fill;
                            externalRefs.AutoGenerateColumns = true;
                            externalRefs.AutoSize = true;
                            externalRefs.ReadOnly = true;
                            externalRefs.AllowUserToAddRows = false;
                            externalRefs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                            externalRefs.DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskStruct>(((ResourceIntrospectionManifest)block.Value).ReferencedStructs), null);
                            
                            externalRefsTab.Controls.Add(externalRefs);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        if (((ResourceIntrospectionManifest)block.Value).ReferencedEnums.Count > 0)
                        {
                            var externalRefsTab = new TabPage("Introspection Manifest: Enums");
                            var externalRefs2 = new DataGridView();
                            externalRefs2.Dock = DockStyle.Fill;
                            externalRefs2.AutoGenerateColumns = true;
                            externalRefs2.AutoSize = true;
                            externalRefs2.ReadOnly = true;
                            externalRefs2.AllowUserToAddRows = false;
                            externalRefs2.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
                            externalRefs2.DataSource = new BindingSource(new BindingList<ResourceIntrospectionManifest.ResourceDiskEnum>(((ResourceIntrospectionManifest)block.Value).ReferencedEnums), null);

                            externalRefsTab.Controls.Add(externalRefs2);
                            resTabs.TabPages.Add(externalRefsTab);
                        }

                        //continue;
                    }

                    var tab2 = new TabPage(block.Key.ToString());
                    var control = new TextBox();
                    control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);

                    try
                    {
                        control.Text = block.Value.ToString().Replace("\n", Environment.NewLine); //make sure panorama is new lines
                    }
                    catch (Exception e)
                    {
                        control.Text = e.ToString();
                    }

                    control.Dock = DockStyle.Fill;
                    control.Multiline = true;
                    control.ReadOnly = true;
                    control.ScrollBars = ScrollBars.Both;
                    tab2.Controls.Add(control);
                    resTabs.TabPages.Add(tab2);
                }

                tab.Controls.Add(resTabs);
            }

            return tab;
        }

        private void MainForm_DragDrop(object sender, DragEventArgs e)
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

            foreach (string fileName in files)
            {
                OpenFile(fileName);
            }
        }

        private void MainForm_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Move;
            }
        }
    }
}
