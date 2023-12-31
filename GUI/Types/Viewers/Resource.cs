using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;
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

        public TabPage Create(VrfGuiContext vrfGuiContext, byte[] input)
        {
            var tab = new TabPage();
            var resourceTemp = new ValveResourceFormat.Resource
            {
                FileName = vrfGuiContext.FileName,
            };
            var resource = resourceTemp;

            try
            {
                if (input != null)
                {
                    resource.Read(new MemoryStream(input));
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

            var resTabs = new TabControl
            {
                Dock = DockStyle.Fill,
            };

            var selectData = false;

            switch (resource.ResourceType)
            {
                case ResourceType.Texture:
                    try
                    {
                        AddTexture(vrfGuiContext, resource, resTabs);
                    }
                    catch (Exception e)
                    {
                        var tab2 = new TabPage("TEXTURE")
                        {
                            AutoScroll = true,
                        };
                        var control = new CodeTextBox(e.ToString());

                        tab2.Controls.Add(control);
                        resTabs.TabPages.Add(tab2);
                    }

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

                case ResourceType.Particle:
                    var particleRendererTab = new TabPage("PARTICLE");
                    particleRendererTab.Controls.Add(new GLParticleViewer(vrfGuiContext, (ParticleSystem)resource.DataBlock));
                    resTabs.TabPages.Add(particleRendererTab);
                    break;

                case ResourceType.Sound:
                    var soundTab = new TabPage("SOUND");
                    var ap = new AudioPlayer(resource, soundTab);
                    resTabs.TabPages.Add(soundTab);
                    break;

                case ResourceType.Map:
                    {
                        var mapResource = vrfGuiContext.LoadFile(Path.Join(resource.FileName[..^7], "world.vwrld_c"));
                        if (mapResource != null)
                        {
                            var mapTab = new TabPage("MAP");
                            mapTab.Controls.Add(new GLWorldViewer(vrfGuiContext, (World)mapResource.DataBlock));
                            resTabs.TabPages.Add(mapTab);
                        }
                        break;
                    }

                case ResourceType.World:
                    var worldmeshTab = new TabPage("MAP");
                    worldmeshTab.Controls.Add(new GLWorldViewer(vrfGuiContext, (World)resource.DataBlock));
                    resTabs.TabPages.Add(worldmeshTab);
                    break;

                case ResourceType.WorldNode:
                    var nodemeshTab = new TabPage("WORLD NODE");
                    nodemeshTab.Controls.Add(new GLWorldViewer(vrfGuiContext, (WorldNode)resource.DataBlock));
                    resTabs.TabPages.Add(nodemeshTab);
                    break;

                case ResourceType.Model:
                    var modelRendererTab = new TabPage("MODEL");
                    modelRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, (Model)resource.DataBlock));
                    resTabs.TabPages.Add(modelRendererTab);
                    break;

                case ResourceType.Mesh:
                    var meshRendererTab = new TabPage("MESH");
                    meshRendererTab.Controls.Add(new GLMeshViewer(vrfGuiContext, (Mesh)resource.DataBlock));
                    resTabs.TabPages.Add(meshRendererTab);
                    break;

                case ResourceType.SmartProp:
                    var smartPropRendererTab = new TabPage("SMART PROP");
                    smartPropRendererTab.Controls.Add(new GLSmartPropViewer(vrfGuiContext, (SmartProp)resource.DataBlock));
                    resTabs.TabPages.Add(smartPropRendererTab);
                    break;

                case ResourceType.AnimationGraph:
                    var animGraphModelRendererTab = new TabPage("ANIMATION GRAPH");
                    animGraphModelRendererTab.Controls.Add(new GLAnimGraphViewer(vrfGuiContext, (AnimGraph)resource.DataBlock));
                    resTabs.TabPages.Add(animGraphModelRendererTab);
                    break;

                case ResourceType.Material:
                    if (((Material)resource.DataBlock).ShaderName == "sky.vfx")
                    {
                        var skyboxTab = new TabPage("SKYBOX");
                        var skybox = new GLSkyboxViewer(vrfGuiContext, resource);
                        skyboxTab.Controls.Add(skybox);
                        resTabs.TabPages.Add(skyboxTab);
                        break;
                    }

                    var materialRendererTab = new TabPage("MATERIAL");
                    materialRendererTab.Controls.Add(new GLMaterialViewer(vrfGuiContext, resource, resTabs));
                    resTabs.TabPages.Add(materialRendererTab);

                    break;
                case ResourceType.PhysicsCollisionMesh:
                    var physRendererTab = new TabPage("PHYSICS");
                    physRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, (PhysAggregateData)resource.DataBlock));
                    resTabs.TabPages.Add(physRendererTab);
                    break;

                default:
                    selectData = true;
                    break;
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

                    AddDataGridExternalRefAction(vrfGuiContext.FileLoader, externalRefs, "Name");
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

                var foundFile = guiFileLoader.FindFileWithContext(name + "_c");
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
                    IViewer.AddContentTab(resTabs, "Entities", ((EntityLump)resource.DataBlock).ToEntityDumpString(), true);
                    break;

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

        private static void AddTexture(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, TabControl resTabs)
        {
            var tex = (Texture)resource.DataBlock;

            if ((tex.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                var cubemapContainer = new TabControl
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                };

                var CUBEMAP_OFFSETS = new (float X, float Y)[]
                {
                    (2, 1), // PositiveX, // rt
                    (0, 1), // NegativeX, // lf
                    (1, 0), // PositiveY, // bk
                    (1, 2), // NegativeY, // ft
                    (1, 1), // PositiveZ, // up
                    (3, 1), // NegativeZ, // dn
                };

                for (uint i = 0; i < tex.Depth; i++)
                {
                    var cubemapControl = new Forms.Texture
                    {
                        BackColor = Color.Black,
                    };

                    try
                    {
                        var cubemapBitmap = new SKBitmap(tex.ActualWidth * 4, tex.ActualHeight * 3, SKColorType.Bgra8888, SKAlphaType.Unpremul);

                        try
                        {
                            using var cubemapCanvas = new SKCanvas(cubemapBitmap);

                            for (var face = 0; face < 6; face++)
                            {
                                using var faceBitmap = tex.GenerateBitmap(depth: i, face: (Texture.CubemapFace)face);

                                var offset = CUBEMAP_OFFSETS[face];
                                cubemapCanvas.DrawBitmap(faceBitmap, tex.ActualWidth * offset.X, tex.ActualHeight * offset.Y);
                            }

                            cubemapControl.SetImage(
                                cubemapBitmap,
                                Path.GetFileNameWithoutExtension(vrfGuiContext.FileName),
                                cubemapBitmap.Width,
                                cubemapBitmap.Height
                            );

                            var cubemapTab = new TabPage($"#{i}")
                            {
                                AutoScroll = true,
                            };
                            cubemapTab.Controls.Add(cubemapControl);
                            cubemapContainer.Controls.Add(cubemapTab);

                            cubemapBitmap = null;
                        }
                        finally
                        {
                            cubemapBitmap?.Dispose();
                        }

                        cubemapControl = null;
                    }
                    finally
                    {
                        cubemapControl?.Dispose();
                    }
                }

                var cubemapParentTab = new TabPage("CUBEMAP");
                cubemapParentTab.Controls.Add(cubemapContainer);
                resTabs.TabPages.Add(cubemapParentTab);

                return;
            }
            else if (tex.Depth > 1)
            {
                var depthContainer = new TabControl
                {
                    Dock = DockStyle.Fill,
                    Multiline = true,
                };

                for (uint i = 0; i < tex.Depth; i++)
                {
                    var depthControl = new Forms.Texture
                    {
                        BackColor = Color.Black,
                    };

                    var depthBitmap = tex.GenerateBitmap(depth: i);

                    depthControl.SetImage(
                        depthBitmap,
                        Path.GetFileNameWithoutExtension(vrfGuiContext.FileName),
                        tex.ActualWidth,
                        tex.ActualHeight
                    );

                    var depthTab = new TabPage($"#{i}")
                    {
                        AutoScroll = true,
                    };
                    depthTab.Controls.Add(depthControl);
                    depthContainer.Controls.Add(depthTab);
                }

                var cubemapParentTab = new TabPage("TEXTURE");
                cubemapParentTab.Controls.Add(depthContainer);
                resTabs.TabPages.Add(cubemapParentTab);

                return;
            }

            var sheet = tex.GetSpriteSheetData();
            var bitmap = tex.GenerateBitmap();

            if (sheet != null)
            {
                using var canvas = new SKCanvas(bitmap);
                using var color1 = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(0, 100, 255, 200),
                    StrokeWidth = 1,
                };
                using var color2 = new SKPaint
                {
                    Style = SKPaintStyle.Stroke,
                    Color = new SKColor(255, 100, 0, 200),
                    StrokeWidth = 1,
                };

                foreach (var sequence in sheet.Sequences)
                {
                    foreach (var frame in sequence.Frames)
                    {
                        foreach (var image in frame.Images)
                        {
                            canvas.DrawRect(image.GetCroppedRect(bitmap.Width, bitmap.Height), color1);
                            canvas.DrawRect(image.GetUncroppedRect(bitmap.Width, bitmap.Height), color2);
                        }
                    }
                }
            }

            var control = new Forms.Texture
            {
                BackColor = Color.Black,
            };

            control.InitGpuDecoder(vrfGuiContext, resource);

            control.SetImage(
                bitmap,
                Path.GetFileNameWithoutExtension(vrfGuiContext.FileName),
                tex.ActualWidth,
                tex.ActualHeight
            );

            var tab = new TabPage("TEXTURE")
            {
                AutoScroll = true,
            };
            tab.Controls.Add(control);
            resTabs.TabPages.Add(tab);
        }
    }
}
