using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.GLViewers;
using GUI.Types.Graphs;
using GUI.Utils;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Viewers
{
    enum ResourceViewMode
    {
        Default,
        ViewerOnly,
        ResourceBlocksOnly,
    };

    class Resource(VrfGuiContext vrfGuiContext, ResourceViewMode viewMode) : IViewer, IDisposable
    {
        /// <summary>
        /// Keyvalues blocks larger than this on disk are not turned into text for display, their text
        /// would come close to or exceed the largest string .NET can hold.
        /// </summary>
        private const long MaxKeyValuesBlockSizeForText = 64 * 1024 * 1024;

        private ValveResourceFormat.Resource? resource;
        private RendererContext? rendererContext;
        private readonly List<(Func<GLGraphViewer> Create, string TabName)> preparedGraphViewers = [];
        private readonly List<GLGraphViewer> loadedGraphViewers = [];
        private const string EntityIOGraphTabName = "ENTITY I/O GRAPH";
        public GLBaseControl? GLViewer { get; private set; }
        private CodeTextBox? GLViewerError;
        private string? GLViewerTabName;

        public static bool IsAccepted(uint magic)
        {
            return magic == ValveResourceFormat.Resource.KnownHeaderVersion;
        }

        public async Task LoadAsync(Stream? stream)
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

            if (viewMode != ResourceViewMode.ResourceBlocksOnly)
            {
                try
                {
                    InitializeSpecialViewer(vrfGuiContext, resource);
                }
                catch (Exception ex)
                {
                    GLViewerError = CodeTextBox.CreateFromException(ex, vrfGuiContext.FullPath);
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

                case ResourceType.ParticleSnapshot:
                    if (resource.GetBlockByType(BlockType.SNAP) is ParticleSnapshot snapshot && SnapshotParticleSystem.CanPreview(snapshot))
                    {
                        GLViewer = new GLParticleViewer(vrfGuiContext, rendererContext, SnapshotParticleSystem.Create(snapshot), snapshot);
                        GLViewerTabName = "SNAPSHOT";
                    }
                    break;

                case ResourceType.Map:
                {
                    var worldResource = vrfGuiContext.LoadFileCompiled(WorldLoader.GetWorldNameFromMap(resource.FileName!));
                    var mapExternalReferences = resource.ExternalReferences;

                    if (worldResource != null && worldResource.DataBlock is World mapWorldData)
                    {
                        GLViewer = new GLWorldViewer(vrfGuiContext, rendererContext, mapWorldData, mapExternalReferences);
                        GLViewerTabName = "MAP";
                    }
                    else
                    {
                        worldResource?.Dispose();
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
                        GLViewer = new AG1GraphViewer(vrfGuiContext, rendererContext, animGraphData.Data);
                        GLViewerTabName = "AG1 ANIMATION GRAPH";
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
                        GLViewer = new AG2GraphViewer(vrfGuiContext, rendererContext, binaryKV3.Data);
                        GLViewerTabName = "AG2 ANIMATION GRAPH";
                    }
                    break;

                case ResourceType.PulseGraphDef:
                    if (resource.DataBlock is BinaryKV3 graphDefKV3)
                    {
                        GLViewer = new PulseGraphViewer(vrfGuiContext, rendererContext, graphDefKV3.Data);
                        GLViewerTabName = "PULSE GRAPH";
                    }
                    break;

                case ResourceType.EntityLump:
                    if (resource.DataBlock is EntityLump entityLumpData)
                    {
                        GLViewer = new EntityIOGraphViewer(vrfGuiContext, rendererContext, entityLumpData);
                        GLViewerTabName = "ENTITY I/O GRAPH";
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

                case ResourceType.WorldVisibility:
                    if (VoxelVisibility.GetWorldVisibility(resource) is { } vxvs)
                    {
                        GLViewer = new GLVoxelVisibilityViewer(vrfGuiContext, rendererContext, vxvs);
                        GLViewerTabName = "VISIBILITY";
                    }
                    break;

                case ResourceType.PostProcessing:
                    if (resource.DataBlock is PostProcessing postProcessing && postProcessing.Data.ContainsKey("m_colorCorrectionVolumeData"))
                    {
                        GLViewer = new GLTextureViewer(vrfGuiContext, rendererContext, resource);
                        GLViewerTabName = "LUT";
                    }
                    break;

                case ResourceType.VData:
                    if (resource.DataBlock is BombDamage bombDamage)
                    {
                        GLViewer = new GLBombDamageViewer(vrfGuiContext, rendererContext, bombDamage);
                        GLViewerTabName = "BOMB DAMAGE";
                    }
                    break;
            }

            GLViewer?.InitializeLoad();

            // Preview only ever shows the first tab, so the extra graph tabs would be built and thrown away.
            if (viewMode != ResourceViewMode.ViewerOnly)
            {
                PrepareExtraGraphViewers(vrfGuiContext, resource);
            }
        }

        public void NotifyVisible() => GLViewer?.NotifyVisible();

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
                        GLViewerError = CodeTextBox.CreateFromException(ex, vrfGuiContext.FullPath);
                    }
                }

                // Creating the GL viewer waits on other threads, and the tab can be closed while it does
                if (resTabs.IsDisposed)
                {
                    GLViewerError?.Dispose();
                    GLViewerError = null;
                    return;
                }

                if (GLViewerError != null)
                {
                    GLViewer?.Dispose();
                    GLViewer = null;
                    DisposeExtraGraphViewers();
                    var errorTab = new ThemedTabPage("Viewer Error");
                    errorTab.Controls.Add(GLViewerError);
                    resTabs.TabPages.Add(errorTab);
                }

                // Entity lumps get the same browsable grid a map's world viewer provides, with or
                // without the graph tab the GL viewer adds.
                if (!isPreview && resource.DataBlock is EntityLump standaloneLump)
                {
                    var entitiesTabPage = new ThemedTabPage("Entity List");
                    entitiesTabPage.Controls.Add(new EntityViewer(vrfGuiContext, standaloneLump.GetEntities()));
                    resTabs.TabPages.Add(entitiesTabPage);
                }
            }

            if (isPreview && !selectData)
            {
                var previewTab = resTabs.TabPages[0];

                while (previewTab.Controls.Count > 0)
                {
                    containerTabPage.Controls.Add(previewTab.Controls[0]);
                }

                resTabs.Dispose();

                return;
            }

            // Strictly speaking this event should not be needed, but we have it for safety.
            // Handled by OpenFile -> OnTabDisposed
            void OnTabDisposed(object? sender, EventArgs e)
            {
                resTabs.Disposed -= OnTabDisposed;

                resource?.Dispose();
            }

            resTabs.Disposed += OnTabDisposed;

            var loadedResource = resource;
            var blocksByType = new Dictionary<BlockType, List<(int Index, Block Block)>>();

            for (var blockIndex = 0; blockIndex < resource.Blocks.Count; blockIndex++)
            {
                var block = resource.Blocks[blockIndex];

                if (!blocksByType.TryGetValue(block.Type, out var blocksOfType))
                {
                    blocksOfType = [];
                    blocksByType[block.Type] = blocksOfType;
                }

                blocksOfType.Add((blockIndex, block));
            }

            for (var blockIndex = 0; blockIndex < resource.Blocks.Count; blockIndex++)
            {
                var block = resource.Blocks[blockIndex];
                var blocksOfType = blocksByType[block.Type];

                // Models repeat their per mesh blocks, so these get a single tab listing them all
                if (blocksOfType.Count > 1)
                {
                    if (blocksOfType[0].Index == blockIndex)
                    {
                        var listTab = new ThemedTabPage($"{block.Type} ({blocksOfType.Count})");
                        resTabs.TabPages.Add(listTab);

                        PopulateWhenShown(listTab, () =>
                        {
                            listTab.Controls.Add(new RepeatedBlocksViewer(blocksOfType, GetBlockNames(loadedResource), (selectedBlock, container) => PopulateBlockView(loadedResource, selectedBlock, container)));
                        });
                    }

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

                PopulateWhenShown(blockTab, () => PopulateBlockView(loadedResource, block, blockTab));

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
                var control = CodeTextBox.CreateFromException(ex, vrfGuiContext.FullPath);

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

                if (isPreview && glViewerControl is RendererControl rendererControl)
                {
                    // No tab header in preview, so show the file name (and icon) at the top of the side control panel.
                    var iconExtension = Path.GetExtension(vrfGuiContext.FileName.AsSpan());
                    if (iconExtension.Length > 0)
                    {
                        iconExtension = iconExtension[1..];
                    }

                    rendererControl.AddPreviewFileName(Path.GetFileName(vrfGuiContext.FileName), AppIcons.GetImageIndexForExtension(iconExtension));
                }

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
                        PopulateWhenShown(worldTabPage, () => AddTextViewControl(ResourceType.WorldNode, loadedWorld.World, worldTabPage));
                    }

                    if (loadedWorld.MainWorldNode != null)
                    {
                        var worldNodeTabPage = new ThemedTabPage("Node Data");
                        resTabs.TabPages.Add(worldNodeTabPage);
                        PopulateWhenShown(worldNodeTabPage, () => AddTextViewControl(ResourceType.WorldNode, loadedWorld.MainWorldNode, worldNodeTabPage));
                    }

                    var entitiesTabPage = new ThemedTabPage("Entity List");
                    entitiesTabPage.Controls.Add(new EntityViewer(vrfGuiContext, loadedWorld.Entities, glWorldViewer.SelectAndFocusEntity));
                    resTabs.TabPages.Add(entitiesTabPage);
                }

                if (!isPreview)
                {
                    foreach (var (create, tabName) in preparedGraphViewers)
                    {
                        var (tabPage, graph) = AddGraphViewerTab(create, tabName, resTabs);

                        if (GLViewer is GLWorldViewer worldViewerWithGraph && tabName == EntityIOGraphTabName)
                        {
                            worldViewerWithGraph.ShowEntityInGraph = entity => ShowEntityInGraph(entity, graph, tabPage);
                            worldViewerWithGraph.EntityHasGraphNode = entity => !graph.IsCompleted || (graph.Result as EntityIOGraphViewer)?.HasEntity(entity) == true;
                        }
                    }
                }

                return true;
            }

            return AddSpecialViewerData(resource, isPreview, resTabs);
        }

        private (TabPage TabPage, Task<GLGraphViewer?> Graph) AddGraphViewerTab(Func<GLGraphViewer> create, string tabName, TabControl resTabs)
        {
            var tabPage = new ThemedTabPage(tabName);
#pragma warning disable CA2000 // Ownership is transferred to the tab, which disposes it
            var loadingFile = new LoadingFile();
#pragma warning restore CA2000
            tabPage.Controls.Add(loadingFile);
            resTabs.TabPages.Add(tabPage);

            return (tabPage, LoadGraphViewerAsync(create, tabPage, loadingFile));
        }

        // Built and loaded off the UI thread, each in its own renderer context as the viewers share no GL objects
        private async Task<GLGraphViewer?> LoadGraphViewerAsync(Func<GLGraphViewer> create, TabPage tabPage, LoadingFile loadingFile)
        {
            GLGraphViewer? viewer = null;

            try
            {
                viewer = await Task.Run(create).ConfigureAwait(true);
                await Task.Run(viewer.InitializeLoad).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                viewer?.Dispose();

                if (!tabPage.IsDisposed)
                {
                    loadingFile.Dispose();
                    tabPage.Controls.Add(CodeTextBox.CreateFromException(e, vrfGuiContext.FullPath));
                }

                return null;
            }

            if (tabPage.IsDisposed)
            {
                viewer.Dispose();
                return null;
            }

            loadedGraphViewers.Add(viewer);
            loadingFile.Dispose();
            tabPage.Controls.Add(viewer.InitializeUiControls(isPreview: false));

            return viewer;
        }

        private static bool ShowEntityInGraph(EntityLump.Entity entity, Task<GLGraphViewer?> graph, TabPage tabPage)
        {
            if (graph.IsCompleted)
            {
                return (graph.Result as EntityIOGraphViewer)?.ShowEntity(entity) == true;
            }

            // Its tab shows that it is loading until it can jump to the entity
            if (tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            _ = ShowEntityWhenLoadedAsync(entity, graph);
            return true;
        }

        private static async Task ShowEntityWhenLoadedAsync(EntityLump.Entity entity, Task<GLGraphViewer?> graph)
        {
            if (await graph.ConfigureAwait(true) is EntityIOGraphViewer entityGraph)
            {
                entityGraph.ShowEntity(entity);
            }
        }

        // Runs on the background load thread, but only reads the graph resources. Building the viewers is left to
        // their tabs, so it does not hold up the main viewer.
        private void PrepareExtraGraphViewers(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource)
        {
            if (rendererContext == null)
            {
                return;
            }

            if (GLViewer is GLWorldViewer { LoadedWorld: { } loadedWorld } glWorldViewer)
            {
                var hasConnections = false;

                foreach (var entity in loadedWorld.Entities)
                {
                    if (entity.Connections is { Count: > 0 })
                    {
                        hasConnections = true;
                        break;
                    }
                }

                if (hasConnections)
                {
                    preparedGraphViewers.Add((() => new EntityIOGraphViewer(vrfGuiContext, vrfGuiContext.CreateRendererContext(), loadedWorld.Entities, glWorldViewer.SelectAndFocusEntities), EntityIOGraphTabName));
                }

                PrepareMapPulseGraphViewers(vrfGuiContext, loadedWorld.Entities);
            }

            if (GLViewer is GLModelViewer && resource.DataBlock is Model model)
            {
                PrepareModelAnimGraphViewers(vrfGuiContext, model);
            }
        }

        // Maps bind pulse scripts through point_pulse entities referencing the graph resource.
        private void PrepareMapPulseGraphViewers(VrfGuiContext vrfGuiContext, List<EntityLump.Entity> entities)
        {
            Debug.Assert(rendererContext != null);

            var scripts = new List<string>();

            foreach (var entity in entities)
            {
                if (entity.GetStringProperty("classname") != "point_pulse")
                {
                    continue;
                }

                var graphDef = entity.GetStringProperty("graph_def");

                if (!string.IsNullOrEmpty(graphDef) && !scripts.Contains(graphDef))
                {
                    scripts.Add(graphDef);
                }
            }

            foreach (var script in scripts)
            {
                if (rendererContext.FileLoader.LoadFileCompiled(script)?.DataBlock is BinaryKV3 pulseData)
                {
                    var tabName = scripts.Count > 1 ? $"PULSE GRAPH ({Path.GetFileNameWithoutExtension(script)})" : "PULSE GRAPH";
                    preparedGraphViewers.Add((() => new PulseGraphViewer(vrfGuiContext, vrfGuiContext.CreateRendererContext(), pulseData.Data), tabName));
                }
            }
        }

        private void PrepareModelAnimGraphViewers(VrfGuiContext vrfGuiContext, Model model)
        {
            Debug.Assert(rendererContext != null);

            var graphPaths = new List<string>();

            void AddGraphPath(string? path)
            {
                if (!string.IsNullOrEmpty(path) && !graphPaths.Contains(path))
                {
                    graphPaths.Add(path);
                }
            }

            if (model.Data.GetArray("m_animGraph2Refs") is { } animGraph2Refs)
            {
                foreach (var graphRef in animGraph2Refs)
                {
                    AddGraphPath(graphRef.GetStringProperty("m_hGraph"));
                }
            }
            else if (model.Data.ContainsKey("m_animGraph2Refs"))
            {
                Log.Warn(nameof(Resource), "Model has a non-array m_animGraph2Refs value, skipping its animation graph tabs.");
            }

            if (model.Data.ContainsKey("m_refAnimGraph"))
            {
                AddGraphPath(model.Data.GetStringProperty("m_refAnimGraph"));
            }

            // HLA/SteamVR-era models and compiled Deadlock AG1 bind their graph through the keyvalues block.
            AddGraphPath(model.KeyValues.GetStringProperty("anim_graph_resource"));

            foreach (var path in graphPaths)
            {
                Func<GLGraphViewer> create;
                string baseName;

                switch (rendererContext.FileLoader.LoadFileCompiled(path)?.DataBlock)
                {
                    case AnimGraph ag1Data:
                        create = () => new AG1GraphViewer(vrfGuiContext, vrfGuiContext.CreateRendererContext(), ag1Data.Data);
                        baseName = "AG1 ANIMATION GRAPH";
                        break;
                    case BinaryKV3 nmGraphData:
                        create = () => new AG2GraphViewer(vrfGuiContext, vrfGuiContext.CreateRendererContext(), nmGraphData.Data);
                        baseName = "AG2 ANIMATION GRAPH";
                        break;
                    default:
                        continue;
                }

                var tabName = graphPaths.Count > 1 ? $"{baseName} ({Path.GetFileNameWithoutExtension(path)})" : baseName;
                preparedGraphViewers.Add((create, tabName));
            }
        }

        private bool AddSpecialViewerData(ValveResourceFormat.Resource resource, bool isPreview, TabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Panorama:
                    if (resource.DataBlock is Panorama { Images.Count: > 0 })
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
                                    new BindingList<Panorama.ImageEntry>(((Panorama)resource.DataBlock).Images), string.Empty),
                        };
                        var specialTabPage = new ThemedTabPage("PANORAMA IMAGES");
                        specialTabPage.Controls.Add(nameControl);
                        resTabs.TabPages.Add(specialTabPage);
                    }
                    break;

                case ResourceType.Sound:
                    if (resource.DataBlock is Sound { StreamingDataSize: > 0 } soundData)
                    {
                        var specialTabPage = new ThemedTabPage("SOUND");
                        var autoPlay = ((Settings.QuickPreviewFlags)Settings.Config.QuickFilePreview & Settings.QuickPreviewFlags.AutoPlaySounds) != 0;

                        specialTabPage.Controls.Add(CreateSoundInfoLabel(soundData));

                        try
                        {
                            if (AudioPlayer.CreateWaveStream(resource) is var (waveStream, loopMarkers))
                            {
                                var ownedStream = waveStream;

                                try
                                {
                                    var audio = new AudioPlaybackPanel(ownedStream, isPreview && autoPlay, loopMarkers);
                                    specialTabPage.Controls.Add(audio);
                                    ownedStream = null;
                                }
                                finally
                                {
                                    ownedStream?.Dispose();
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Log.Error(nameof(AudioPlayer), e.ToString());

                            var msg = new Label
                            {
                                Text = $"NAudio Exception: {e}",
                                Dock = DockStyle.Fill,
                            };

                            specialTabPage.Controls.Add(msg);
                        }

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

        private static Label CreateSoundInfoLabel(Sound sound)
        {
            var text = new StringBuilder();

            foreach (var (label, value) in sound.GetInfoRows())
            {
                if (text.Length > 0)
                {
                    text.Append("    ");
                }

                text.Append(label).Append(": ").Append(value);
            }

            return new Label
            {
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(6, 6, 6, 2),
                Text = text.ToString(),
            };
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

        public static TreeViewDoubleBuffered BuildExternalRefTree(VrfGuiContext vrfGuiContext, List<ResourceExtRefList.ResourceReferenceInfo> references)
        {
            var treeView = new TreeViewDoubleBuffered
            {
                Dock = DockStyle.Fill,
                ImageList = AppIcons.ImageList,
                HideSelection = false,
                ShowRootLines = true,
            };

            treeView.BeginUpdate();

            var rootNodes = new Dictionary<string, TreeNode>();
            var rootLookup = rootNodes.GetAlternateLookup<ReadOnlySpan<char>>();
            var folderIcon = AppIcons.Icons["Folder"];

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

                var fileIcon = AppIcons.GetImageIndexForExtension(extensionSpan);
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

        // Both before and after the MVTX MIDX update
        private static readonly string[] EmbeddedMeshBlockKeys =
        [
            "data_block", "vbib_block", "morph_block", "tools_vb_block",
            "m_nDataBlock", "m_nMorphBlock", "m_nVBIBBlock", "m_nToolsVBBlock",
        ];

        private static readonly string[] EmbeddedMeshBufferKeys = ["m_vertexBuffers", "m_indexBuffers", "m_toolsBuffers"];

        /// <summary>
        /// Names the blocks a model's embedded meshes are stored in after the mesh, so the repeated
        /// blocks can be told apart.
        /// </summary>
        private static Dictionary<int, string> GetBlockNames(ValveResourceFormat.Resource resource)
        {
            var names = new Dictionary<int, string>();

            if (resource.GetBlockByType(BlockType.CTRL) is not BinaryKV3 ctrl || ctrl.Data.Root.GetArray("embedded_meshes") is not { } embeddedMeshes)
            {
                return names;
            }

            foreach (var embeddedMesh in embeddedMeshes)
            {
                var name = embeddedMesh.GetStringProperty("m_Name") ?? embeddedMesh.GetStringProperty("name");

                foreach (var key in EmbeddedMeshBlockKeys)
                {
                    AddName(embeddedMesh.GetIntegerProperty(key, -1), name);
                }

                foreach (var key in EmbeddedMeshBufferKeys)
                {
                    foreach (var buffer in embeddedMesh.GetArray(key) ?? [])
                    {
                        AddName(buffer.GetIntegerProperty("m_nBlockIndex", -1), name);
                    }
                }
            }

            return names;

            void AddName(long blockIndex, string name)
            {
                if (blockIndex >= 0)
                {
                    names.TryAdd((int)blockIndex, name);
                }
            }
        }

        private void PopulateBlockView(ValveResourceFormat.Resource resource, Block block, Control container)
        {
            // Mesh buffers are compressed binary blobs whose layout is described in CTRL
            if (block.Type is BlockType.MVTX or BlockType.MIDX or BlockType.MADJ or BlockType.MSLT)
            {
                AddByteViewControl(resource, block, container);
                return;
            }

            try
            {
                AddTextViewControl(resource, block, container);
            }
            catch (Exception e)
            {
                Log.Error(nameof(Resource), e.ToString());
                AddByteViewControl(resource, block, container);
            }
        }

        private static void AddByteViewControl(ValveResourceFormat.Resource resource, Block block, Control blockTab)
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

        private void AddTextViewControl(ValveResourceFormat.Resource resource, Block block, Control blockTab)
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

        private static void AddTextViewControl(ResourceType resourceType, Block block, Control blockTab)
        {
            if (block.Size > MaxKeyValuesBlockSizeForText && TryGetKvDataBlock(block, out var root, out var header))
            {
                AddTooLargeForTextControl(block, new KVDocument(header, name: null, root), blockTab);
                return;
            }

            ViewerContent.Text content;

            try
            {
                content = GetTextViewContent(resourceType, block);
            }
            catch (OutOfMemoryException) when (TryGetKvDataBlock(block, out var kvRoot, out var kvHeader))
            {
                AddTooLargeForTextControl(block, new KVDocument(kvHeader, name: null, kvRoot), blockTab);
                return;
            }

            ViewerContentPresenter.Present(blockTab, content);
        }

        private static void AddTooLargeForTextControl(Block block, KVDocument document, Control blockTab)
        {
            var message = CodeTextBox.Create(
                $"The {block.Type} block is {block.Size:N0} bytes, which is too large to display as text.{Environment.NewLine}Save it as a text file instead.",
                HighlightLanguage.None);

            var saveButton = new ThemedButton
            {
                Text = "Save as text...",
                Dock = DockStyle.Top,
                Height = 32,
            };
            saveButton.Click += OnSaveClick;

            blockTab.Controls.Add(message);
            blockTab.Controls.Add(saveButton);

            async void OnSaveClick(object? sender, EventArgs e)
            {
                var defaultName = $"{Path.GetFileNameWithoutExtension(block.Resource.FileName)}_{block.Type}.kv3";
                var fileName = AppFileDialogs.SaveFile("Save block as text", defaultName, "kv3", "KeyValues3 (*.kv3)|*.kv3|All files (*.*)|*.*");

                if (fileName == null)
                {
                    return;
                }

                saveButton.Enabled = false;

                try
                {
                    await Task.Run(() =>
                    {
                        using var stream = File.Create(fileName);
                        KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Serialize(stream, document);
                    }).ConfigureAwait(true);

                    Log.Info(nameof(Resource), $"Saved {block.Type} block to \"{fileName}\"");
                }
                catch (Exception ex)
                {
                    Log.Error(nameof(Resource), $"Failed to save {block.Type} block to \"{fileName}\": {ex}");
                }
                finally
                {
                    if (!saveButton.IsDisposed)
                    {
                        saveButton.Enabled = true;
                    }
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="populate"/> the first time <paramref name="page"/> is shown, so the
        /// contents of tabs that are never opened are not built.
        /// </summary>
        private static void PopulateWhenShown(TabPage page, Action populate)
        {
            if (page.Visible)
            {
                populate();
                return;
            }

            void OnVisibleChanged(object? sender, EventArgs e)
            {
                if (!page.Visible || page.IsDisposed || page.Disposing)
                {
                    return;
                }

                page.VisibleChanged -= OnVisibleChanged;
                populate();
            }

            page.VisibleChanged += OnVisibleChanged;
        }

        private static ViewerContent.Text GetTextViewContent(ResourceType resourceType, Block block)
        {
            if (TryGetKvDataBlock(block, out var kvRoot, out var kvHeader))
            {
                var doc = new KVDocument(kvHeader, name: null, kvRoot);
                var (kv3Text, sourceMap) = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).SerializeWithSourceMap(doc);
                return new ViewerContent.Text(kv3Text, SourceMap: sourceMap);
            }

            var text = block.ToString();
            var language = HighlightLanguage.KeyValues;

            if (resourceType == ResourceType.PanoramaLayout && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.XML;
            }
            else if (resourceType == ResourceType.PanoramaVectorGraphic && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.XML;
            }
            else if (resourceType == ResourceType.PanoramaStyle && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.CSS;
            }
            else if ((resourceType == ResourceType.PanoramaScript || resourceType == ResourceType.PanoramaTypescript) && block.Type == BlockType.DATA)
            {
                language = HighlightLanguage.JS;
            }

            return new ViewerContent.Text(text, language);
        }

        private static bool TryGetKvDataBlock(Block block, [MaybeNullWhen(false)] out KVObject root, out KVHeader? header)
        {
            switch (block)
            {
                case BinaryKV3 kv3:
                    root = kv3.Data.Root;
                    header = kv3.Data.Header;
                    return true;

                case KeyValuesOrNTRO kvOrNtro:
                    root = kvOrNtro.Data;
                    header = null;
                    return true;

                case NTRO ntro:
                    root = ntro.Output;
                    header = null;
                    return true;

                case ResourceEditInfo2 red2 when red2.Data is not null:
                    root = red2.Data.Root;
                    header = red2.Data.Header;
                    return true;

                default:
                    root = null;
                    header = null;
                    return false;
            }
        }

        private static void AddReconstructedContentTab(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, ThemedTabControl resTabs)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Sound when resource.DataBlock is Sound { Sentence: { } sentence }:
                    ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed phonemes", new ViewerContent.Text(sentence.ToValveSentence()));
                    break;

                case ResourceType.Material:
                    ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vmat", new ViewerContent.LazyText(new MaterialExtract(resource, vrfGuiContext.FileLoaderNoCache).ToValveMaterial));
                    break;

                case ResourceType.EntityLump:
                    if (resource.DataBlock is EntityLump entityLump)
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "FGD", new ViewerContent.Text(entityLump.ToForgeGameData()));
                        ViewerContentPresenter.AddContentTab(resTabs, "Entities-Text", new ViewerContent.Text(entityLump.ToEntityDumpString()), select: true);
                        // force select the new entities tab for now
                        resTabs.SelectedTab = resTabs.TabPages[0];
                    }
                    break;

                case ResourceType.PostProcessing:
                    if (resource.DataBlock is PostProcessing postProcessingData)
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vpost", new ViewerContent.Text(postProcessingData.ToValvePostProcessing()));
                    }
                    break;

                case ResourceType.Texture:
                {
                    if (FileExtract.IsChildResource(resource))
                    {
                        break;
                    }

                    var textureExtract = new TextureExtract(resource);
                    ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vtex", new ViewerContent.Text(textureExtract.ToValveTexture()));

                    if (textureExtract.TryGetMksData(out var _, out var mks))
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed mks", new ViewerContent.Text(mks));
                    }

                    break;
                }

                case ResourceType.ParticleSnapshot:
                {
                    if (!FileExtract.IsChildResource(resource))
                    {
                        ViewerContentPresenter.AddContentTab(resTabs, "Reconstructed vsnap", new ViewerContent.Text(new SnapshotExtract(resource).ToValveSnap()));
                    }

                    break;
                }
            }
        }

        private void DisposeExtraGraphViewers()
        {
            foreach (var viewer in loadedGraphViewers)
            {
                viewer.Dispose();
            }

            loadedGraphViewers.Clear();
            preparedGraphViewers.Clear();
        }

        public void Dispose()
        {
            // Order matters: nothing may dispose a resource until every thread that could still be
            // reading it has stopped
            GLViewer?.Dispose();
            rendererContext?.Dispose();
            resource?.Dispose();

            DisposeExtraGraphViewers();
            GLViewerError?.Dispose();
        }
    }
}
