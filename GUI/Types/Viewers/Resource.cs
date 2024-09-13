using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using DarkModeForms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
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

        public TabPage Create(VrfGuiContext vrfGuiContext, Stream stream, bool isPreview)
        {
            var resourceTemp = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            var resource = resourceTemp;

            try
            {
                if (stream != null)
                {
                    resource.Read(stream);
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

            var resTabs = new FlatTabControl
            {
                Dock = DockStyle.Fill,
            };

            TabPage specialTabPage = null;
            var selectData = false;

            try
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
                        {
                            specialTabPage = new TabPage("SOUND");
                            var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
                            var ap = new AudioPlayer(resource, specialTabPage, isPreview && autoPlay);
                            break;
                        }

                    case ResourceType.Map:
                        {
                            var mapResource = vrfGuiContext.LoadFile(Path.Join(resource.FileName[..^7], "world.vwrld_c"));
                            if (mapResource != null)
                            {
                                specialTabPage = new TabPage("MAP");
                                specialTabPage.Controls.Add(new GLWorldViewer(vrfGuiContext, (World)mapResource.DataBlock));
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

                    default:
                        selectData = true;
                        break;
                }

                if (specialTabPage != null)
                {
                    resTabs.TabPages.Add(specialTabPage);
                    specialTabPage = null;
                }
            }
            catch (Exception ex)
            {
                var control = new CodeTextBox(ex.ToString());

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
                var control = new CodeTextBox(ex.ToString());

                var tabEx = new TabPage("Decompile Error");
                tabEx.Controls.Add(control);
                resTabs.TabPages.Add(tabEx);
            }

            var tab = new TabPage();
            tab.Controls.Add(resTabs);

            return tab;
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
            var bv = new System.ComponentModel.Design.ByteViewer
            {
                Dock = DockStyle.Fill
            };
            blockTab.Controls.Add(bv);

            Program.MainForm.Invoke((MethodInvoker)(() =>
            {
                resource.Reader.BaseStream.Position = block.Offset;
                bv.SetBytes(resource.Reader.ReadBytes((int)block.Size));
            }));
        }

        private static void AddTextViewControl(ValveResourceFormat.Resource resource, Block block, TabPage blockTab)
        {
            if (resource.ResourceType == ResourceType.Shader && block is SboxShader shaderBlock)
            {
                var viewer = new CompiledShader();

                try
                {
                    var shaderTabs = viewer.SetResourceBlockTabControl(blockTab, shaderBlock.Shaders);

                    foreach (var shaderFile in shaderBlock.Shaders)
                    {
                        shaderTabs.CreateShaderFileTab(shaderBlock.Shaders, shaderFile.VcsProgramType);
                    }

                    viewer = null;
                }
                finally
                {
                    viewer?.Dispose();
                }

                return;
            }

            var textBox = new CodeTextBox(block.ToString());
            blockTab.Controls.Add(textBox);
        }

        private static void AddReconstructedContentTab(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, TabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Material:
                    var vmatTab = IViewer.AddContentTab(resTabs, "Reconstructed vmat", new MaterialExtract(resource).ToValveMaterial());
                    var textBox = (CodeTextBox)vmatTab.Controls[0];
                    Task.Run(() => textBox.Text = new MaterialExtract(resource, vrfGuiContext.FileLoader).ToValveMaterial().ReplaceLineEndings());
                    break;

                case ResourceType.EntityLump:
                    {
                        var entityLump = (EntityLump)resource.DataBlock;
                        IViewer.AddContentTab(resTabs, "FGD", entityLump.ToForgeGameData());
                        IViewer.AddContentTab(resTabs, "Entities", entityLump.ToEntityDumpString(), true);
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

                case ResourceType.Shader:
                    {
                        var collectionBlock = resource.GetBlockByType(BlockType.SPRV)
                            ?? resource.GetBlockByType(BlockType.DXBC)
                            ?? resource.GetBlockByType(BlockType.DATA);

                        var extract = new ShaderExtract((SboxShader)collectionBlock)
                        {
                            SpirvCompiler = CompiledShader.SpvToHlsl
                        };

                        IViewer.AddContentTab<Func<string>>(resTabs, extract.GetVfxFileName(), extract.ToVFX, true);
                        break;
                    }
            }
        }
    }
}
