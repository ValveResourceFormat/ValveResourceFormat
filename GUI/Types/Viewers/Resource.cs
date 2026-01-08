using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.GLViewers;
using GUI.Types.Graphs;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Viewers
{
    enum ResourceViewMode
    {
        Default,
        ViewerOnly,
        ResourceBlocksOnly,
    };

    class Resource(VrfGuiContext vrfGuiContext, ResourceViewMode viewMode, bool verifyFileSize) : IViewer, IDisposable
    {
        private ValveResourceFormat.Resource? resource;
        private RendererContext? rendererContext;
        private GLViewerControl? GLViewer;
        private CodeTextBox? GLViewerError;
        private string? GLViewerTabName;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public async Task LoadAsync(Stream stream)
        {
            var resourceTemp = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            resource = resourceTemp;

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

            if (viewMode != ResourceViewMode.ResourceBlocksOnly)
            {
                try
                {
                    InitializeSpecialViewer(vrfGuiContext, resource);
                }
                catch (Exception ex)
                {
                    GLViewerError = CodeTextBox.CreateFromException(ex);
                }
            }
        }

        private void InitializeSpecialViewer(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource)
        {
            rendererContext = vrfGuiContext.CreateRendererContext();

            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                case ResourceType.PanoramaVectorGraphic:
                    GLViewer = new GLTextureViewer(vrfGuiContext, rendererContext, resource);
                    GLViewerTabName = "TEXTURE";
                    break;

                case ResourceType.Particle:
                    if (resource.DataBlock is ParticleSystem particleData)
                    {
                        GLViewer = new GLParticleViewer(vrfGuiContext, rendererContext, particleData);
                        GLViewerTabName = "PARTICLE";
                    }
                    break;

                case ResourceType.Map:
                    {
                        var mapResource = vrfGuiContext.LoadFile($"{resource.FileName![..^7]}/world.vwrld_c");
                        var mapExternalReferences = resource.ExternalReferences;

                        if (mapResource != null && mapResource.DataBlock is World mapWorldData)
                        {
                            GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, mapWorldData, mapExternalReferences);
                            GLViewerTabName = "MAP";
                        }
                        else
                        {
                            mapResource?.Dispose();
                        }
                        break;
                    }

                case ResourceType.World:
                    if (resource.DataBlock is World worldData)
                    {
                        GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, worldData);
                        GLViewerTabName = "MAP";
                    }
                    break;

                case ResourceType.WorldNode:
                    if (resource.DataBlock is WorldNode worldNodeData)
                    {
                        GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, worldNodeData, resource.ExternalReferences);
                        GLViewerTabName = "WORLD NODE";
                    }
                    break;

                case ResourceType.Model:
                    if (resource.DataBlock is Model modelData)
                    {
                        GLViewer = new GLModelViewer(vrfGuiContext, rendererContext, modelData);
                        GLViewerTabName = "MODEL";
                    }
                    break;

                case ResourceType.Mesh:
                    if (resource.DataBlock is Mesh meshData)
                    {
                        GLViewer = new GLMeshViewer(vrfGuiContext, rendererContext, meshData);
                        GLViewerTabName = "MESH";
                    }
                    break;

                case ResourceType.SmartProp:
                    if (resource.DataBlock is SmartProp smartPropData)
                    {
                        GLViewer = new GLSmartPropViewer(vrfGuiContext, rendererContext, smartPropData);
                        GLViewerTabName = "SMART PROP";
                    }
                    break;

                case ResourceType.AnimationGraph:
                    if (resource.DataBlock is AnimGraph animGraphData)
                    {
                        GLViewer = new GLAnimGraphViewer(vrfGuiContext, rendererContext, animGraphData);
                        GLViewerTabName = "ANIMATION GRAPH";
                    }
                    break;

                case ResourceType.NmClip:
                    GLViewer = new GLAnimationViewer(vrfGuiContext, rendererContext, resource);
                    GLViewerTabName = "ANIMATION CLIP";
                    break;

                case ResourceType.NmSkeleton:
                    GLViewer = new GLAnimationViewer(vrfGuiContext, rendererContext, resource);
                    GLViewerTabName = "SKELETON";
                    break;

                case ResourceType.NmGraph:
                    if (resource.DataBlock is BinaryKV3 binaryKV3)
                    {
                        GLViewer = new AnimationGraphViewer(vrfGuiContext, rendererContext, binaryKV3.Data);
                        GLViewerTabName = "ANIMATION GRAPH";
                    }
                    break;

                case ResourceType.Material:
                    {
                        if (resource.DataBlock is Material { ShaderName: "sky.vfx" })
                        {
                            GLViewer = new GLSkyboxViewer(vrfGuiContext, rendererContext, resource);
                            GLViewerTabName = "SKYBOX";
                        }
                        else
                        {
                            GLViewer = new GLMaterialViewer(vrfGuiContext, rendererContext, resource);
                            GLViewerTabName = "MATERIAL";
                        }
                        break;
                    }

                case ResourceType.PhysicsCollisionMesh:
                    if (resource.DataBlock is PhysAggregateData physAggregateData)
                    {
                        GLViewer = new GLModelViewer(vrfGuiContext, rendererContext, physAggregateData);
                        GLViewerTabName = "PHYSICS";
                    }
                    break;

                case ResourceType.PostProcessing:
                    if (resource.DataBlock is PostProcessing postProcessing && postProcessing.Data.ContainsKey("m_colorCorrectionVolumeData"))
                    {
                        GLViewer = new GLTextureViewer(vrfGuiContext, rendererContext, resource);
                        GLViewerTabName = "LUT";
                    }
                    break;
            }

            GLViewer?.InitializeLoad();
        }

        public void Create(TabPage containerTabPage)
        {
            Debug.Assert(resource is not null);

            var isPreview = viewMode == ResourceViewMode.ViewerOnly;

            var resTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
                Multiline = true,
            };
            containerTabPage.Controls.Add(resTabs);
            //containerTabPage.PerformLayout();

            var ownsResource = true;
            void OnTabDisposed(object? sender, EventArgs e)
            {
                resTabs.Disposed -= OnTabDisposed;

                if (ownsResource)
                {
                    resource?.Dispose();
                }
            }

            resTabs.Disposed += OnTabDisposed;

            var selectData = true;

            if (viewMode != ResourceViewMode.ResourceBlocksOnly)
            {
                if (GLViewerError == null)
                {
                    try
                    {
                        selectData = !AddSpecialViewer(vrfGuiContext, resource, isPreview, resTabs);
                    }
                    catch (Exception ex)
                    {
                        GLViewerError = CodeTextBox.CreateFromException(ex);
                    }
                }

                if (GLViewerError != null)
                {
                    GLViewer?.Dispose();
                    GLViewer = null;
                    var errorTab = new ThemedTabPage("Viewer Error");
                    errorTab.Controls.Add(GLViewerError);
                    resTabs.TabPages.Add(errorTab);
                }
            }

            if (isPreview && !selectData)
            {
                var previewTab = resTabs.TabPages[0];

                // Preview only displays the first tab page, so copy over the contents
                foreach (Control c in previewTab.Controls)
                {
                    containerTabPage.Controls.Add(c);
                }

                resTabs.Dispose();

                ownsResource = false;

                return;
            }

            List<RawBinary>? binaryBuffers = null;

            foreach (var block in resource.Blocks)
            {
                // They are just binary blobs, and the actual layout of them is stored in CTRL, so the tabs are not useful here
                if (block is RawBinary rawBlock && block.Type is BlockType.MVTX or BlockType.MIDX or BlockType.MADJ)
                {
                    binaryBuffers ??= [];
                    binaryBuffers.Add(rawBlock);
                    continue;
                }

                if (block.Type == BlockType.RERL && block is ResourceExtRefList externalReferences)
                {
                    var externalRefsTree = BuildExternalRefTree(vrfGuiContext, externalReferences.ResourceRefInfoList);

                    var externalRefsTab = new ThemedTabPage("References");
                    externalRefsTab.Controls.Add(externalRefsTree);
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
                                        ((ResourceIntrospectionManifest)block).ReferencedStructs), string.Empty),
                        };

                        var externalRefsTab = new ThemedTabPage("Introspection Manifest: Structs");
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
                                        ((ResourceIntrospectionManifest)block).ReferencedEnums), string.Empty),
                        };

                        var externalRefsTab = new ThemedTabPage("Introspection Manifest: Enums");
                        externalRefsTab.Controls.Add(externalRefs2);
                        resTabs.TabPages.Add(externalRefsTab);
                    }
                }

                var blockTab = new ThemedTabPage(block.Type.ToString());
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

            if (binaryBuffers != null)
            {
                var blockTab = new ThemedTabPage("Buffers");
                resTabs.TabPages.Add(blockTab);

                var text = new StringBuilder();

                foreach (var block in binaryBuffers)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"{block.Type} - {block.Size} bytes");
                }

                var textBox = CodeTextBox.Create(text.ToString());
                blockTab.Controls.Add(textBox);
            }

            try
            {
                AddReconstructedContentTab(vrfGuiContext, resource, resTabs);
            }
            catch (Exception ex)
            {
                var control = CodeTextBox.CreateFromException(ex);

                var tabEx = new ThemedTabPage("Decompile Error");
                tabEx.Controls.Add(control);
                resTabs.TabPages.Add(tabEx);
            }
        }

        private bool AddSpecialViewer(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, bool isPreview, TabControl resTabs)
        {
            if (GLViewer != null)
            {
                Debug.Assert(GLViewerTabName != null);

                var glViewerControl = GLViewer.InitializeUiControls(isPreview);

                var specialTabPage = new ThemedTabPage(GLViewerTabName);
                resTabs.TabPages.Add(specialTabPage);
                specialTabPage.Controls.Add(glViewerControl);

                if (!isPreview && GLViewer is GLMaterialViewer glMaterialViewer)
                {
                    glMaterialViewer.SetTabControl(resTabs);
                }

                if (!isPreview && GLViewer is GLWorldViewer glWorldViewer && glWorldViewer.LoadedWorld is { } loadedWorld)
                {
                    if (resource.ResourceType == ResourceType.Map)
                    {
                        var worldTabPage = new ThemedTabPage("World Data");
                        resTabs.TabPages.Add(worldTabPage);
                        AddTextViewControl(ResourceType.WorldNode, loadedWorld.World, worldTabPage);
                    }

                    if (loadedWorld.MainWorldNode != null)
                    {
                        var worldNodeTabPage = new ThemedTabPage("Node Data");
                        resTabs.TabPages.Add(worldNodeTabPage);
                        AddTextViewControl(ResourceType.WorldNode, loadedWorld.MainWorldNode, worldNodeTabPage);
                    }

                    var entitiesTabPage = new ThemedTabPage("Entity List");
                    entitiesTabPage.Controls.Add(new EntityViewer(vrfGuiContext, loadedWorld.Entities, glWorldViewer.SelectAndFocusEntity));
                    resTabs.TabPages.Add(entitiesTabPage);
                }

                GLViewer.InitializeRenderLoop();
                return true;
            }

            switch (resource.ResourceType)
            {
                case ResourceType.Panorama:
                    if (resource.DataBlock is Panorama { Names.Count: > 0 })
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
                                    new BindingList<Panorama.NameEntry>(((Panorama)resource.DataBlock).Names), string.Empty),
                        };
                        var specialTabPage = new ThemedTabPage("PANORAMA NAMES");
                        specialTabPage.Controls.Add(nameControl);
                        resTabs.TabPages.Add(specialTabPage);
                    }
                    break;

                case ResourceType.Sound:
                    if (resource.ContainsBlockType(BlockType.DATA))
                    {
                        var specialTabPage = new ThemedTabPage("SOUND");
                        var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;
                        var ap = new AudioPlayer(resource, specialTabPage, isPreview && autoPlay);
                        resTabs.TabPages.Add(specialTabPage);
                        return true;
                    }
                    break;

                case ResourceType.EntityLump:
                    if (resource.DataBlock is EntityLump entityLumpData)
                    {
                        var specialTabPage = new ThemedTabPage("Entities");
                        specialTabPage.Controls.Add(new EntityViewer(vrfGuiContext, entityLumpData.GetEntities()));
                        resTabs.TabPages.Add(specialTabPage);
                        return true;
                    }
                    break;

                case ResourceType.ChoreoSceneFileData:
                    {
                        var specialTabPage = new ThemedTabPage("VCDLIST");
                        specialTabPage.Controls.Add(new ChoreoViewer(resource));
                        resTabs.TabPages.Add(specialTabPage);
                        return true;
                    }

                case ResourceType.Shader:
                    {
                        var compiledShaderViewer = new CompiledShader(vrfGuiContext);
                        try
                        {
                            var specialTabPage = new ThemedTabPage("SHADER");
                            resTabs.TabPages.Add(specialTabPage);
                            compiledShaderViewer.Create(specialTabPage);
                            compiledShaderViewer = null;
                        }
                        finally
                        {
                            compiledShaderViewer?.Dispose();
                        }
                        return true;
                    }
            }

            return false;
        }

        public static bool OpenExternalReference(VrfGuiContext vrfGuiContext, string name)
        {
            Log.Debug(nameof(Resource), $"Opening {name} from external refs");

            var foundFile = vrfGuiContext.FindFileWithContext(name + GameFileLoader.CompiledFileSuffix);
            if (foundFile.Context == null)
            {
                foundFile = vrfGuiContext.FindFileWithContext(name);
            }

            if (foundFile.Context != null)
            {
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                return true;
            }

            return false;
        }

        private static TreeViewDoubleBuffered BuildExternalRefTree(VrfGuiContext vrfGuiContext, List<ResourceExtRefList.ResourceReferenceInfo> references)
        {
            var treeView = new TreeViewDoubleBuffered
            {
                Dock = DockStyle.Fill,
                ImageList = MainForm.ImageList,
                HideSelection = false,
                ShowRootLines = true,
            };

            treeView.BeginUpdate();

            var rootNodes = new Dictionary<string, TreeNode>();
            var rootLookup = rootNodes.GetAlternateLookup<ReadOnlySpan<char>>();
            var folderIcon = MainForm.Icons["Folder"];

            foreach (var refInfo in references)
            {
                var pathSpan = refInfo.Name.AsSpan();
                var slashIndex = pathSpan.IndexOf('/');

                ReadOnlySpan<char> rootFolderSpan;

                if (slashIndex >= 0)
                {
                    rootFolderSpan = pathSpan[..slashIndex];
                }
                else
                {
                    rootFolderSpan = [];
                }

                var extensionSpan = Path.GetExtension(pathSpan);
                if (extensionSpan.Length > 0)
                {
                    extensionSpan = extensionSpan[1..];
                }

                var fileIcon = MainForm.GetImageIndexForExtension(extensionSpan);
                var fileNode = new TreeNode(refInfo.Name)
                {
                    ImageIndex = fileIcon,
                    SelectedImageIndex = fileIcon,
                    Tag = refInfo,
                };

                TreeNode? rootNode = null;

                if (!rootFolderSpan.IsEmpty && !rootLookup.TryGetValue(rootFolderSpan, out rootNode))
                {
                    var rootFolder = rootFolderSpan.ToString();
                    rootNode = new TreeNode(rootFolder)
                    {
                        ImageIndex = folderIcon,
                        SelectedImageIndex = folderIcon,
                    };
                    rootNodes[rootFolder] = rootNode;
                    treeView.Nodes.Add(rootNode);
                }

                if (rootNode != null)
                {
                    rootNode.Nodes.Add(fileNode);
                }
                else
                {
                    treeView.Nodes.Add(fileNode);
                }
            }

            treeView.ExpandAll();
            treeView.EndUpdate();

            void OnNodeDoubleClick(object? sender, TreeNodeMouseClickEventArgs e)
            {
                if (e.Node?.Tag is ResourceExtRefList.ResourceReferenceInfo refInfo)
                {
                    OpenExternalReference(vrfGuiContext, refInfo.Name);
                }
            }

            void OnDisposed(object? sender, EventArgs e)
            {
                treeView.NodeMouseDoubleClick -= OnNodeDoubleClick;
                treeView.Disposed -= OnDisposed;
            }

            treeView.NodeMouseDoubleClick += OnNodeDoubleClick;
            treeView.Disposed += OnDisposed;

            return treeView;
        }

        private static void AddByteViewControl(ValveResourceFormat.Resource resource, Block block, TabPage blockTab)
        {
            Debug.Assert(resource.Reader != null);

            resource.Reader.BaseStream.Position = block.Offset;
            var input = resource.Reader.ReadBytes((int)block.Size);

            var text = ByteViewer.GetTextFromBytes(input.AsSpan());

            if (!string.IsNullOrEmpty(text))
            {
                var textBox = CodeTextBox.Create(text);
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

        private void AddTextViewControl(ValveResourceFormat.Resource resource, Block block, TabPage blockTab)
        {
            if (resource.ResourceType == ResourceType.SboxShader && block is SboxShader shaderBlock)
            {
                var tabPage = new ThemedTabPage();
                var viewer = new CompiledShader(vrfGuiContext);

                try
                {
                    viewer.Create(
                        tabPage,
                        shaderBlock.Shaders,
                        Path.GetFileNameWithoutExtension(resource.FileName.AsSpan()),
                        ValveResourceFormat.CompiledShader.VcsProgramType.Features
                    );

                    foreach (Control control in tabPage.Controls)
                    {
                        blockTab.Controls.Add(control);
                    }

                    viewer = null;
                }
                finally
                {
                    viewer?.Dispose();
                    tabPage.Dispose();
                }

                return;
            }

            AddTextViewControl(resource.ResourceType, block, blockTab);
        }

        private static void AddTextViewControl(ResourceType resourceType, Block block, TabPage blockTab)
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

        private static void AddReconstructedContentTab(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, ThemedTabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Material:
                    var vmatTab = IViewer.AddContentTab(resTabs, "Reconstructed vmat", new MaterialExtract(resource, vrfGuiContext).ToValveMaterial);
                    break;

                case ResourceType.EntityLump:
                    if (resource.DataBlock is EntityLump entityLump)
                    {
                        IViewer.AddContentTab(resTabs, "FGD", entityLump.ToForgeGameData());
                        IViewer.AddContentTab(resTabs, "Entities-Text", entityLump.ToEntityDumpString(), true);
                        // force select the new entities tab for now
                        resTabs.SelectedTab = resTabs.TabPages[0];
                    }
                    break;

                case ResourceType.PostProcessing:
                    if (resource.DataBlock is PostProcessing postProcessingData)
                    {
                        IViewer.AddContentTab(resTabs, "Reconstructed vpost", postProcessingData.ToValvePostProcessing());
                    }
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

                case ResourceType.ParticleSnapshot:
                    {
                        if (!FileExtract.IsChildResource(resource))
                        {
                            IViewer.AddContentTab(resTabs, "Reconstructed vsnap", new SnapshotExtract(resource).ToValveSnap());
                        }

                        break;
                    }
            }
        }

        public void Dispose()
        {
            resource?.Dispose();
            rendererContext?.Dispose();
            GLViewer?.Dispose();
            GLViewerError?.Dispose();
        }
    }
}
