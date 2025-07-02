using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

#nullable disable

namespace GUI.Types.Viewers
{
    enum ResourceViewMode
    {
        Default,
        ViewerOnly,
        ResourceBlocksOnly,
    };

    class Resource : IViewer
    {
        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream)
        {
            throw new NotImplementedException();
        }

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream, ResourceViewMode viewMode, bool verifyFileSize)
        {
            var resourceTemp = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            var resource = resourceTemp;

            var isPreview = viewMode == ResourceViewMode.ViewerOnly;

            try
            {
                if (stream != null)
                {
                    resource.Read(stream, verifyFileSize);
                }
                else
                {
                    resource.Read(vrfGuiContext.FileName);
                }

                resourceTemp = null;
            }
            finally
            {
                // Only dispose resource if it throws within Read(), tough luck below this
                resourceTemp?.Dispose();
            }

            var resTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };

            TabPage specialTabPage = null;
            var selectData = true;

            try
            {
                if (viewMode != ResourceViewMode.ResourceBlocksOnly)
                {
                    CreateSpecialViewer(vrfGuiContext, resource, isPreview, resTabs, ref specialTabPage);
                }

                if (specialTabPage != null)
                {
                    selectData = false;
                    resTabs.TabPages.Add(specialTabPage);
                    specialTabPage = null;
                }
            }
            catch (Exception ex)
            {
                var control = CodeTextBox.CreateFromException(ex);

                var tabEx = new TabPage("Error");
                tabEx.Controls.Add(control);
                resTabs.TabPages.Add(tabEx);
            }
            finally
            {
                specialTabPage?.Dispose();
            }

            if (isPreview && !selectData)
            {
                var previewTab = resTabs.TabPages[0];
                resTabs.TabPages.Clear();
                resTabs.Dispose();

                return previewTab;
            }

            foreach (var block in resource.Blocks)
            {
                if (block.Type == BlockType.RERL)
                {
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

                    AddDataGridExternalRefAction(vrfGuiContext.FileLoader, externalRefs, "Name");

                    var externalRefsTab = new TabPage("External Refs");
                    externalRefsTab.Controls.Add(externalRefs);
                    resTabs.TabPages.Add(externalRefsTab);

                    continue;
                }

                if (block.Type == BlockType.NTRO)
                {
                    if (((ResourceIntrospectionManifest)block).ReferencedStructs.Count > 0)
                    {
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

                        var externalRefsTab = new TabPage("Introspection Manifest: Structs");
                        externalRefsTab.Controls.Add(externalRefs);
                        resTabs.TabPages.Add(externalRefsTab);
                    }

                    if (((ResourceIntrospectionManifest)block).ReferencedEnums.Count > 0)
                    {
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

                        var externalRefsTab = new TabPage("Introspection Manifest: Enums");
                        externalRefsTab.Controls.Add(externalRefs2);
                        resTabs.TabPages.Add(externalRefsTab);
                    }
                }

                var blockTab = new TabPage(block.Type.ToString());
                resTabs.TabPages.Add(blockTab);

                try
                {
                    AddTextViewControl(resource, block, blockTab);
                }
                catch (Exception e)
                {
                    Log.Error(nameof(Resource), e.ToString());
                    AddByteViewControl(resource, block, blockTab);
                }

                if (block.Type == BlockType.DATA && selectData)
                {
                    resTabs.SelectTab(blockTab);
                }
            }

            try
            {
                AddReconstructedContentTab(vrfGuiContext, resource, resTabs);
            }
            catch (Exception ex)
            {
                var control = CodeTextBox.CreateFromException(ex);

                var tabEx = new TabPage("Decompile Error");
                tabEx.Controls.Add(control);
                resTabs.TabPages.Add(tabEx);
            }

            var tab = new TabPage();
            tab.Controls.Add(resTabs);

            return tab;
        }

        private static void CreateSpecialViewer(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, bool isPreview, ThemedTabControl resTabs, ref TabPage specialTabPage)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                case ResourceType.PanoramaVectorGraphic:
                    {
                        var textureControl = new GLTextureViewer(vrfGuiContext, resource);
                        specialTabPage = new TabPage("TEXTURE");
                        specialTabPage.Controls.Add(textureControl);
                        break;
                    }

                case ResourceType.Panorama:
                    if (((Panorama)resource.DataBlock).Names.Count > 0)
                    {
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
                        specialTabPage = new TabPage("PANORAMA NAMES");
                        specialTabPage.Controls.Add(nameControl);
                    }

                    break;

                case ResourceType.Particle:
                    {
                        specialTabPage = new TabPage("PARTICLE");
                        specialTabPage.Controls.Add(new GLParticleViewer(vrfGuiContext, (ParticleSystem)resource.DataBlock));
                        break;
                    }

                case ResourceType.Sound:
                    if (resource.ContainsBlockType(BlockType.DATA))
                    {
                        specialTabPage = new TabPage("SOUND");
                        var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
                        var ap = new AudioPlayer(resource, specialTabPage, isPreview && autoPlay);
                    }
                    break;

                case ResourceType.Map:
                    {
                        var mapResource = vrfGuiContext.LoadFile(Path.Join(resource.FileName[..^7], "world.vwrld_c"));
                        if (mapResource != null)
                        {
                            specialTabPage = new TabPage("MAP");
                            specialTabPage.Controls.Add(new GLWorldViewer(vrfGuiContext, (World)mapResource.DataBlock, isFromVmap: true));
                        }
                        break;
                    }

                case ResourceType.World:
                    {
                        specialTabPage = new TabPage("MAP");
                        specialTabPage.Controls.Add(new GLWorldViewer(vrfGuiContext, (World)resource.DataBlock));
                        break;
                    }

                case ResourceType.WorldNode:
                    {
                        specialTabPage = new TabPage("WORLD NODE");
                        specialTabPage.Controls.Add(new GLWorldViewer(vrfGuiContext, (WorldNode)resource.DataBlock));
                        break;
                    }

                case ResourceType.Model:
                    {
                        specialTabPage = new TabPage("MODEL");
                        specialTabPage.Controls.Add(new GLModelViewer(vrfGuiContext, (Model)resource.DataBlock));
                        break;
                    }

                case ResourceType.Mesh:
                    {
                        specialTabPage = new TabPage("MESH");
                        specialTabPage.Controls.Add(new GLMeshViewer(vrfGuiContext, (Mesh)resource.DataBlock));
                        break;
                    }

                case ResourceType.EntityLump:
                    {
                        specialTabPage = new TabPage("Entities");
                        specialTabPage.Controls.Add(new EntityViewer(vrfGuiContext, ((EntityLump)resource.DataBlock).GetEntities()));

                        break;
                    }

                case ResourceType.SmartProp:
                    {
                        specialTabPage = new TabPage("SMART PROP");
                        specialTabPage.Controls.Add(new GLSmartPropViewer(vrfGuiContext, (SmartProp)resource.DataBlock));
                        break;
                    }

                case ResourceType.AnimationGraph:
                    {
                        specialTabPage = new TabPage("ANIMATION GRAPH");
                        specialTabPage.Controls.Add(new GLAnimGraphViewer(vrfGuiContext, (AnimGraph)resource.DataBlock));
                        break;
                    }

                case ResourceType.Material:
                    {
                        if (((Material)resource.DataBlock).ShaderName == "sky.vfx")
                        {
                            var skybox = new GLSkyboxViewer(vrfGuiContext, resource);
                            specialTabPage = new TabPage("SKYBOX");
                            specialTabPage.Controls.Add(skybox);
                            break;
                        }

                        specialTabPage = new TabPage("MATERIAL");
                        specialTabPage.Controls.Add(new GLMaterialViewer(vrfGuiContext, resource, isPreview ? null : resTabs));
                        break;
                    }

                case ResourceType.PhysicsCollisionMesh:
                    {
                        specialTabPage = new TabPage("PHYSICS");
                        specialTabPage.Controls.Add(new GLModelViewer(vrfGuiContext, (PhysAggregateData)resource.DataBlock));
                        break;
                    }

                case ResourceType.ChoreoSceneFileData:
                    {
                        specialTabPage = new TabPage("VCDLIST");
                        specialTabPage.Controls.Add(new ChoreoViewer(resource));
                        break;
                    }
            }
        }

        public static void AddDataGridExternalRefAction(AdvancedGuiFileLoader guiFileLoader, DataGridView dataGrid,
            string columnName, Action<bool> secondAction = null)
        {
            void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0)
                {
                    return;
                }

                var grid = (DataGridView)sender;
                var row = grid.Rows[e.RowIndex];
                var colName = columnName;
                var name = (string)row.Cells[colName].Value;

                Log.Debug(nameof(Resource), $"Opening {name} from external refs");

                var foundFile = guiFileLoader.FindFileWithContext(name + GameFileLoader.CompiledFileSuffix);
                if (foundFile.Context == null)
                {
                    foundFile = guiFileLoader.FindFileWithContext(name);
                }

                var bFound = foundFile.Context != null;
                if (bFound)
                {
                    Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                }

                secondAction?.Invoke(bFound);
            }

            void OnDisposed(object o, EventArgs e)
            {
                dataGrid.CellDoubleClick -= OnCellDoubleClick;
                dataGrid.Disposed -= OnDisposed;
            }

            dataGrid.CellDoubleClick += OnCellDoubleClick;
            dataGrid.Disposed += OnDisposed;
        }

        private static void AddByteViewControl(ValveResourceFormat.Resource resource, Block block, TabPage blockTab)
        {
            resource.Reader.BaseStream.Position = block.Offset;
            var input = resource.Reader.ReadBytes((int)block.Size);

            var textSpan = ByteViewer.GetTextFromBytes(input.AsSpan());

            if (!textSpan.IsEmpty)
            {
                var textBox = CodeTextBox.Create(System.Text.Encoding.UTF8.GetString(textSpan));
                blockTab.Controls.Add(textBox);
                return;
            }

            var bv = new System.ComponentModel.Design.ByteViewer
            {
                Dock = DockStyle.Fill
            };
            blockTab.Controls.Add(bv);

            Program.MainForm.Invoke((MethodInvoker)(() =>
            {
                bv.SetBytes(input);
            }));
        }

        private static void AddTextViewControl(ValveResourceFormat.Resource resource, Block block, TabPage blockTab)
        {
            if (resource.ResourceType == ResourceType.Shader && block is SboxShader shaderBlock)
            {
                var viewer = new CompiledShader();

                try
                {
                    var tabPage = viewer.Create(
                        shaderBlock.Shaders,
                        Path.GetFileNameWithoutExtension(resource.FileName.AsSpan()),
                        ValveResourceFormat.CompiledShader.VcsProgramType.Features
                    );

                    foreach (Control control in tabPage.Controls)
                    {
                        blockTab.Controls.Add(control);
                    }

                    viewer = null;
                    tabPage.Dispose();
                }
                finally
                {
                    viewer?.Dispose();
                }

                return;
            }

            AddTextViewControl(resource.ResourceType, block, blockTab);
        }

        public static void AddTextViewControl(ResourceType resourceType, Block block, TabPage blockTab)
        {
            var text = block.ToString();
            var language = CodeTextBox.HighlightLanguage.KeyValues;

            if (resourceType == ResourceType.PanoramaLayout && block.Type == BlockType.DATA)
            {
                language = CodeTextBox.HighlightLanguage.XML;
            }
            else if (resourceType == ResourceType.PanoramaStyle && block.Type == BlockType.DATA)
            {
                language = CodeTextBox.HighlightLanguage.CSS;
            }
            else if (resourceType == ResourceType.PanoramaScript && block.Type == BlockType.DATA)
            {
                language = CodeTextBox.HighlightLanguage.JS;
            }

            var textBox = CodeTextBox.Create(text, language);
            blockTab.Controls.Add(textBox);
        }

        private static void AddReconstructedContentTab(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, TabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Material:
                    var vmatTab = IViewer.AddContentTab(resTabs, "Reconstructed vmat", new MaterialExtract(resource).ToValveMaterial());
                    var textBox = (CodeTextBox)vmatTab.Controls[0];
                    Task.Run(() => textBox.Text = new MaterialExtract(resource, vrfGuiContext.FileLoader).ToValveMaterial());
                    break;

                case ResourceType.EntityLump:
                    {
                        var entityLump = (EntityLump)resource.DataBlock;
                        IViewer.AddContentTab(resTabs, "FGD", entityLump.ToForgeGameData());
                        IViewer.AddContentTab(resTabs, "Entities-Text", entityLump.ToEntityDumpString(), true);
                        // force select the new entities tab for now
                        resTabs.SelectedTab = resTabs.TabPages[0];
                        break;
                    }

                case ResourceType.PostProcessing:
                    IViewer.AddContentTab(resTabs, "Reconstructed vpost", ((PostProcessing)resource.DataBlock).ToValvePostProcessing());
                    break;

                case ResourceType.Texture:
                    {
                        if (FileExtract.IsChildResource(resource))
                        {
                            break;
                        }

                        var textureExtract = new TextureExtract(resource);
                        IViewer.AddContentTab(resTabs, "Reconstructed vtex", textureExtract.ToValveTexture());

                        if (textureExtract.TryGetMksData(out var _, out var mks))
                        {
                            IViewer.AddContentTab(resTabs, "Reconstructed mks", mks);
                        }

                        break;
                    }

                case ResourceType.Snap:
                    {
                        if (!FileExtract.IsChildResource(resource))
                        {
                            IViewer.AddContentTab(resTabs, "Reconstructed vsnap", new SnapshotExtract(resource).ToValveSnap());
                        }

                        break;
                    }
            }
        }
    }
}
