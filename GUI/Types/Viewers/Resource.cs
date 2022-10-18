using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GUI.Types.Audio;
using GUI.Types.Exporter;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    public class Resource : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var resource = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };

            if (input != null)
            {
                resource.Read(new MemoryStream(input));
            }
            else
            {
                resource.Read(vrfGuiContext.FileName);
            }

            var resTabs = new TabControl
            {
                Dock = DockStyle.Fill,
            };

            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                    var tab2 = new TabPage("TEXTURE")
                    {
                        AutoScroll = true,
                    };

                    try
                    {
                        var tex = (Texture)resource.DataBlock;
                        var sheet = tex.GetSpriteSheetData();
                        using var bitmap = tex.GenerateBitmap();

                        if (sheet != null)
                        {
                            using var canvas = new SKCanvas(bitmap);

                            foreach (var sequence in sheet.Sequences)
                            {
                                foreach (var frame in sequence.Frames)
                                {
                                    foreach (var image in frame.Images)
                                    {
                                        canvas.DrawRect(image.GetCroppedRect(bitmap.Width, bitmap.Height), new SKPaint
                                        {
                                            Style = SKPaintStyle.Stroke,
                                            Color = new SKColor(0, 100, 255, 200),
                                            StrokeWidth = 1,
                                        });

                                        canvas.DrawRect(image.GetUncroppedRect(bitmap.Width, bitmap.Height), new SKPaint
                                        {
                                            Style = SKPaintStyle.Stroke,
                                            Color = new SKColor(255, 100, 0, 200),
                                            StrokeWidth = 1,
                                        });
                                    }
                                }
                            }
                        }

                        var control = new Forms.Texture
                        {
                            BackColor = Color.Black,
                        };
                        control.SetImage(bitmap.ToBitmap(), Path.GetFileNameWithoutExtension(vrfGuiContext.FileName),
                            tex.ActualWidth, tex.ActualHeight);

                        tab2.Controls.Add(control);
                        Program.MainForm.Invoke(new ExportDel(AddToExport), resTabs,
                            $"Export {Path.GetFileName(vrfGuiContext.FileName)} as an image", vrfGuiContext.FileName,
                            new ExportData { Resource = resource });
                    }
                    catch (Exception e)
                    {
                        var control = new TextBox
                        {
                            Dock = DockStyle.Fill,
                            Font = new Font(FontFamily.GenericMonospace, 8),
                            Multiline = true,
                            ReadOnly = true,
                            Text = e.ToString(),
                        };

                        tab2.Controls.Add(control);
                    }

                    resTabs.TabPages.Add(tab2);
                    break;

                case ResourceType.Panorama:
                    if (((Panorama)resource.DataBlock).Names.Count > 0)
                    {
                        var nameTab = new TabPage("PANORAMA NAMES");
                        var nameControl = new DataGridView
                        {
                            Dock = DockStyle.Fill,
                            AutoSize = true,
                            ReadOnly = true,
                            AllowUserToAddRows = false,
                            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                            DataSource =
                                new BindingSource(
                                    new BindingList<Panorama.NameEntry>(((Panorama)resource.DataBlock).Names), null),
                        };
                        nameTab.Controls.Add(nameControl);
                        resTabs.TabPages.Add(nameTab);
                    }

                    break;

                case ResourceType.PanoramaLayout:
                    Program.MainForm.Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(vrfGuiContext.FileName)} as XML",
                        vrfGuiContext.FileName, new ExportData { Resource = resource });
                    break;

                case ResourceType.PanoramaScript:
                    Program.MainForm.Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(vrfGuiContext.FileName)} as JS",
                        vrfGuiContext.FileName, new ExportData { Resource = resource });
                    break;

                case ResourceType.PanoramaStyle:
                    Program.MainForm.Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(vrfGuiContext.FileName)} as CSS",
                        vrfGuiContext.FileName, new ExportData { Resource = resource });
                    break;

                case ResourceType.Particle:
                    var viewerControl = new GLParticleViewer(vrfGuiContext);
                    viewerControl.Load += (_, __) =>
                    {
                        var particleSystem = (ParticleSystem)resource.DataBlock;
                        var particleRenderer = new ParticleRenderer.ParticleRenderer(particleSystem, vrfGuiContext);

                        viewerControl.AddRenderer(particleRenderer);
                    };

                    var particleRendererTab = new TabPage("PARTICLE");
                    particleRendererTab.Controls.Add(viewerControl.Control);
                    resTabs.TabPages.Add(particleRendererTab);
                    break;

                case ResourceType.Sound:
                    var soundTab = new TabPage("SOUND");
                    var ap = new AudioPlayer(resource, soundTab);
                    resTabs.TabPages.Add(soundTab);

                    Program.MainForm.Invoke(new ExportDel(AddToExport), resTabs,
                        $"Export {Path.GetFileName(vrfGuiContext.FileName)} as {((Sound)resource.DataBlock).SoundType}", vrfGuiContext.FileName,
                        new ExportData { Resource = resource });

                    break;

                case ResourceType.World:
                    var worldmeshTab = new TabPage("MAP");
                    worldmeshTab.Controls.Add(
                        new GLWorldViewer(vrfGuiContext, (World)resource.DataBlock).ViewerControl);
                    resTabs.TabPages.Add(worldmeshTab);
                    break;

                case ResourceType.WorldNode:
                    var nodemeshTab = new TabPage("WORLD NODE");
                    nodemeshTab.Controls.Add(new GLWorldViewer(vrfGuiContext, (WorldNode)resource.DataBlock)
                        .ViewerControl);
                    resTabs.TabPages.Add(nodemeshTab);
                    break;

                case ResourceType.Model:
                    var modelRendererTab = new TabPage("MODEL");
                    modelRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, (Model)resource.DataBlock)
                        .ViewerControl);
                    resTabs.TabPages.Add(modelRendererTab);
                    break;

                case ResourceType.Mesh:
                    var meshRendererTab = new TabPage("MESH");
                    meshRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, new Mesh(resource)).ViewerControl);
                    resTabs.TabPages.Add(meshRendererTab);
                    break;

                case ResourceType.Material:
                    var materialViewerControl = new GLMaterialViewer();

                    materialViewerControl.Load += (_, __) =>
                    {
                        var materialRenderer = new MaterialRenderer(vrfGuiContext, resource);

                        materialViewerControl.AddRenderer(materialRenderer);

                    };

                    var materialRendererTab = new TabPage("MATERIAL");
                    materialRendererTab.Controls.Add(materialViewerControl.Control);
                    resTabs.TabPages.Add(materialRendererTab);

                    break;
                case ResourceType.PhysicsCollisionMesh:
                    var physRendererTab = new TabPage("PHYSICS");
                    physRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, (PhysAggregateData)resource.DataBlock).ViewerControl);
                    resTabs.TabPages.Add(physRendererTab);
                    break;
            }

            if (GltfModelExporter.CanExport(resource))
            {
                Program.MainForm.Invoke(new ExportDel(AddToExport), resTabs, $"Export {Path.GetFileName(vrfGuiContext.FileName)} as glTF / GLB",
                    vrfGuiContext.FileName, new ExportData { Resource = resource, VrfGuiContext = vrfGuiContext });
            }

            foreach (var block in resource.Blocks)
            {
                if (block.Type == BlockType.RERL)
                {
                    var externalRefsTab = new TabPage("External Refs");

                    var externalRefs = new DataGridView
                    {
                        Dock = DockStyle.Fill,
                        AutoGenerateColumns = true,
                        AutoSize = true,
                        ReadOnly = true,
                        AllowUserToAddRows = false,
                        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                        DataSource =
                            new BindingSource(
                                new BindingList<ResourceExtRefList.ResourceReferenceInfo>(resource.ExternalReferences
                                    .ResourceRefInfoList), null),
                    };
                    externalRefs.CellDoubleClick += (object sender, DataGridViewCellEventArgs e) =>
                    {
                        if (e.RowIndex < 0)
                        {
                            return;
                        }

                        var grid = (DataGridView)sender;
                        var row = grid.Rows[e.RowIndex];
                        var name = (string)row.Cells["Name"].Value;

                        Console.WriteLine($"Opening {name} from external refs");

                        var file = vrfGuiContext.CurrentPackage?.FindEntry(name);

                        if (file == null)
                        {
                            file = vrfGuiContext.CurrentPackage?.FindEntry(name + "_c");
                        }

                        if (file != null)
                        {
                            vrfGuiContext.CurrentPackage.ReadEntry(file, out var fileContents);

                            // TODO: Creating TreeViewPackageTag here is subpar
                            var newVrfGuiContext = new VrfGuiContext(file.GetFileName(), new Controls.TreeViewWithSearchResults.TreeViewPackageTag()
                            {
                                Package = vrfGuiContext.CurrentPackage,
                                ParentFileLoader = vrfGuiContext.FileLoader,
                            });

                            Program.MainForm.OpenFile(fileContents, newVrfGuiContext);
                        }
                    };
                    externalRefsTab.Controls.Add(externalRefs);

                    resTabs.TabPages.Add(externalRefsTab);

                    continue;
                }

                if (block.Type == BlockType.NTRO)
                {
                    if (((ResourceIntrospectionManifest)block).ReferencedStructs.Count > 0)
                    {
                        var externalRefsTab = new TabPage("Introspection Manifest: Structs");

                        var externalRefs = new DataGridView
                        {
                            Dock = DockStyle.Fill,
                            AutoGenerateColumns = true,
                            AutoSize = true,
                            ReadOnly = true,
                            AllowUserToAddRows = false,
                            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                            DataSource =
                                new BindingSource(
                                    new BindingList<ResourceIntrospectionManifest.ResourceDiskStruct>(
                                        ((ResourceIntrospectionManifest)block).ReferencedStructs), null),
                        };

                        externalRefsTab.Controls.Add(externalRefs);
                        resTabs.TabPages.Add(externalRefsTab);
                    }

                    if (((ResourceIntrospectionManifest)block).ReferencedEnums.Count > 0)
                    {
                        var externalRefsTab = new TabPage("Introspection Manifest: Enums");
                        var externalRefs2 = new DataGridView
                        {
                            Dock = DockStyle.Fill,
                            AutoGenerateColumns = true,
                            AutoSize = true,
                            ReadOnly = true,
                            AllowUserToAddRows = false,
                            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                            DataSource =
                                new BindingSource(
                                    new BindingList<ResourceIntrospectionManifest.ResourceDiskEnum>(
                                        ((ResourceIntrospectionManifest)block).ReferencedEnums), null),
                        };

                        externalRefsTab.Controls.Add(externalRefs2);
                        resTabs.TabPages.Add(externalRefsTab);
                    }
                }

                var tab2 = new TabPage(block.Type.ToString());
                try
                {
                    var control = new TextBox();
                    control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);

                    if (block.Type == BlockType.DATA)
                    {
                        if (block is BinaryKV3 blockKeyvalues)
                        {
                            // Wrap it around a KV3File object to get the header.
                            control.Text =
                                Utils.Utils.NormalizeLineEndings(blockKeyvalues.GetKV3File().ToString());
                        }
                        else
                        {
                            if (resource.ResourceType == ResourceType.Sound)
                            {
                                control.Text = Utils.Utils.NormalizeLineEndings(((Sound)block).ToString());
                            }
                            else
                            {
                                control.Text = Utils.Utils.NormalizeLineEndings(block.ToString());
                            }
                        }
                    }
                    else
                    {
                        control.Text = Utils.Utils.NormalizeLineEndings(block.ToString());
                    }

                    control.Dock = DockStyle.Fill;
                    control.Multiline = true;
                    control.ReadOnly = true;
                    control.ScrollBars = ScrollBars.Both;
                    tab2.Controls.Add(control);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);

                    var bv = new System.ComponentModel.Design.ByteViewer();
                    bv.Dock = DockStyle.Fill;
                    tab2.Controls.Add(bv);

                    Program.MainForm.Invoke((MethodInvoker)(() =>
                    {
                        resource.Reader.BaseStream.Position = block.Offset;
                        bv.SetBytes(resource.Reader.ReadBytes((int)block.Size));
                    }));
                }

                resTabs.TabPages.Add(tab2);

                static void AddContentTab(TabControl resTabs, string name, string text)
                {
                    var control = new TextBox();
                    control.Font = new Font(FontFamily.GenericMonospace, control.Font.Size);
                    control.Text = Utils.Utils.NormalizeLineEndings(text);
                    control.Dock = DockStyle.Fill;
                    control.Multiline = true;
                    control.ReadOnly = true;
                    control.ScrollBars = ScrollBars.Both;
                    var tab = new TabPage(name);
                    tab.Controls.Add(control);
                    resTabs.TabPages.Add(tab);
                }

                if (block.Type != BlockType.DATA)
                {
                    continue;
                }

                switch (resource.ResourceType)
                {
                    case ResourceType.Material:
                        AddContentTab(resTabs, "Reconstructed vmat", ((Material)block).ToValveMaterial());
                        break;

                    case ResourceType.EntityLump:
                        AddContentTab(resTabs, "Entities", ((EntityLump)block).ToEntityDumpString());
                        break;

                    case ResourceType.PostProcessing:
                        AddContentTab(resTabs, "Reconstructed vpost", ((PostProcessing)block).ToValvePostProcessing());
                        break;

                    case ResourceType.Texture:
                        {
                            if (FileExtract.IsChildResource(resource))
                            {
                                break;
                            }

                            var textureExtract = new TextureExtract(resource.FileName, (Texture)resource.DataBlock);
                            AddContentTab(resTabs, "Reconstructed vtex", textureExtract.ToValveTexture());

                            if (textureExtract.TryGetMksData(out var _, out var mks))
                            {
                                AddContentTab(resTabs, "Reconstructed mks", mks);
                            }

                            break;
                        }
                }
            }

            if (resource.ResourceType == ResourceType.PanoramaLayout
                || resource.ResourceType == ResourceType.PanoramaScript
                || resource.ResourceType == ResourceType.PanoramaStyle
                || resource.ResourceType == ResourceType.SoundEventScript
                || resource.ResourceType == ResourceType.SoundStackScript)
            {
                foreach (TabPage tab2 in resTabs.TabPages)
                {
                    if (tab2.Text == "DATA")
                    {
                        resTabs.SelectTab(tab2);
                        break;
                    }
                }
            }

            if (resource.ResourceType == ResourceType.EntityLump)
            {
                resTabs.SelectTab(resTabs.TabCount - 1);
            }

            tab.Controls.Add(resTabs);

            return tab;
        }

        private delegate void ExportDel(Control control, string name, string filename, ExportData data);

        private void AddToExport(Control control, string name, string filename, ExportData data)
        {
            Program.MainForm.ExportToolStripButton.Enabled = true;

            var ts = new ToolStripMenuItem
            {
                Size = new Size(150, 20),
                Text = name,
                ToolTipText = filename,
                Tag = data,
            };
            //This is required for the dialog to know the default name and path.
            //This makes it trivial to dump without exploring our nested TabPages.
            ts.Click += ExportToolStripMenuItem_Click;

            Program.MainForm.ExportToolStripButton.DropDownItems.Add(ts);

            void ControlExposed(object sender, EventArgs e)
            {
                control.Disposed -= ControlExposed;
                ts.Click -= ExportToolStripMenuItem_Click;
                Program.MainForm.ExportToolStripButton.DropDownItems.Remove(ts);

                if (Program.MainForm.ExportToolStripButton.DropDownItems.Count == 0)
                {
                    Program.MainForm.ExportToolStripButton.Enabled = false;
                }
            }

            control.Disposed += ControlExposed;
        }

        private void ExportToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //ToolTipText is the full filename
            var menuItem = (ToolStripMenuItem)sender;
            var fileName = menuItem.ToolTipText;

            ExportFile.Export(fileName, menuItem.Tag as ExportData);
        }
    }
}
