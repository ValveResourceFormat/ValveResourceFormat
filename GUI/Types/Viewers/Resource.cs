using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GUI.Types.Audio;
using GUI.Types.Renderer;
using GUI.Utils;
using SkiaSharp;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.CompiledShader;
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
                        var control = new MonospaceTextBox
                        {
                            Text = e.ToString(),
                        };

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
                    meshRendererTab.Controls.Add(new GLModelViewer(vrfGuiContext, (Mesh)resource.DataBlock).ViewerControl);
                    resTabs.TabPages.Add(meshRendererTab);
                    break;

                case ResourceType.Material:
                    if (((Material)resource.DataBlock).ShaderName == "sky.vfx")
                    {
                        var skyboxTab = new TabPage("SKYBOX");
                        var skybox = new GLSkyboxViewer(vrfGuiContext, resource);
                        skyboxTab.Controls.Add(skybox.ViewerControl);
                        resTabs.TabPages.Add(skyboxTab);
                        break;
                    }

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

                    void OnCellDoubleClick(object sender, DataGridViewCellEventArgs e)
                    {
                        if (e.RowIndex < 0)
                        {
                            return;
                        }

                        var grid = (DataGridView)sender;
                        var row = grid.Rows[e.RowIndex];
                        var name = (string)row.Cells["Name"].Value;

                        Console.WriteLine($"Opening {name} from external refs");

                        var foundFile = vrfGuiContext.FileLoader.FindFileWithContext(name);
                        if (foundFile.Context == null)
                        {
                            foundFile = vrfGuiContext.FileLoader.FindFileWithContext(name + "_c");
                        }

                        if (foundFile.Context != null)
                        {
                            Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
                        }
                    }

                    void OnDisposed(object o, EventArgs e)
                    {
                        externalRefs.CellDoubleClick -= OnCellDoubleClick;
                        externalRefs.Disposed -= OnDisposed;
                    }

                    externalRefs.CellDoubleClick += OnCellDoubleClick;
                    externalRefs.Disposed += OnDisposed;
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
                    var control = new MonospaceTextBox();

                    if (block.Type == BlockType.DATA)
                    {
                        if (block is BinaryKV3 blockKeyvalues)
                        {
                            // Wrap it around a KV3File object to get the header.
                            control.Text = blockKeyvalues.GetKV3File().ToString().ReplaceLineEndings();
                        }
                        else
                        {
                            if (resource.ResourceType == ResourceType.Sound)
                            {
                                control.Text = ((Sound)block).ToString().ReplaceLineEndings();
                            }
                            else
                            {
                                control.Text = block.ToString().ReplaceLineEndings();
                            }
                        }
                    }
                    else
                    {
                        control.Text = block.ToString().ReplaceLineEndings();
                    }

                    tab2.Controls.Add(control);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);

                    var bv = new System.ComponentModel.Design.ByteViewer
                    {
                        Dock = DockStyle.Fill
                    };
                    tab2.Controls.Add(bv);

                    Program.MainForm.Invoke((MethodInvoker)(() =>
                    {
                        resource.Reader.BaseStream.Position = block.Offset;
                        bv.SetBytes(resource.Reader.ReadBytes((int)block.Size));
                    }));
                }

                resTabs.TabPages.Add(tab2);

                static void VcsShaderResourceBridge(TabControl resTabs, SboxShader sboxShader)
                {
                    var shaderTab = new TabPage("Embedded Shader");
                    var shaderTabControl = new TabControl
                    {
                        Dock = DockStyle.Fill,
                    };

                    shaderTab.Controls.Add(shaderTabControl);
                    resTabs.TabPages.Add(shaderTab);

                    foreach (var shader in sboxShader.Shaders)
                    {
                        if (shader is null)
                        {
                            continue;
                        }

                        var sb = new StringBuilder();
                        shader.PrintSummary((x) => sb.Append(x), true);
                        IViewer.AddContentTab(shaderTabControl, shader.VcsProgramType.ToString(), sb.ToString());

                        if (shader.GetZFrameCount() > 0)
                        {
                            for (var i = 0; i < Math.Min(4, shader.GetZFrameCount()); i++)
                            {
                                var zframeFile = shader.GetZFrameFileByIndex(i);
                                using var sw = new StringWriter();
                                var zframeSummary = new PrintZFrameSummary(shader, zframeFile, sw.Write, true);
                                IViewer.AddContentTab(shaderTabControl, $"Z{i}", sw.ToString());
                            }
                        }
                    }
                }

                if (block.Type != BlockType.DATA)
                {
                    continue;
                }

                if (selectData)
                {
                    resTabs.SelectTab(tab2);
                }

                try
                {
                    switch (resource.ResourceType)
                    {
                        case ResourceType.Material:
                            IViewer.AddContentTab(resTabs, "Reconstructed vmat", new MaterialExtract(resource).ToValveMaterial());
                            break;

                        case ResourceType.EntityLump:
                            IViewer.AddContentTab(resTabs, "Entities", ((EntityLump)block).ToEntityDumpString());
                            break;

                        case ResourceType.PostProcessing:
                            IViewer.AddContentTab(resTabs, "Reconstructed vpost", ((PostProcessing)block).ToValvePostProcessing());
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
                            var shaderFileContainer = (SboxShader)block;
                            var extract = new ShaderExtract(resource);
                            VcsShaderResourceBridge(resTabs, shaderFileContainer);
                            IViewer.AddContentTab<Func<string>>(resTabs, extract.GetVfxFileName(), extract.ToVFX, true);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    var control = new MonospaceTextBox
                    {
                        Text = ex.ToString(),
                    };

                    var tabEx = new TabPage("Exception");
                    tabEx.Controls.Add(control);
                    resTabs.TabPages.Add(tabEx);
                }
            }

            if (resource.ResourceType == ResourceType.EntityLump)
            {
                resTabs.SelectTab(resTabs.TabCount - 1);
            }

            tab.Controls.Add(resTabs);

            return tab;
        }

        private static void AddTexture(VrfGuiContext vrfGuiContext, ValveResourceFormat.Resource resource, TabControl resTabs)
        {
            var tex = (Texture)resource.DataBlock;

            // TODO: Generate depth tabs for non cubemaps
            if ((tex.Flags & VTexFlags.CUBE_TEXTURE) != 0)
            {
                var cubemapContainer = new TabControl
                {
                    Dock = DockStyle.Fill,
                };
                cubemapContainer.MouseWheel += (sender, args) =>
                {
                    var count = args.Delta > 0 ? -1 : 1;
                    cubemapContainer.SelectedIndex = Math.Clamp(cubemapContainer.SelectedIndex + count,
                        0,
                        cubemapContainer.TabCount - 1
                    );
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
                    var cubemapBitmap = new SKBitmap(tex.ActualWidth * 4, tex.ActualHeight * 3, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                    using var cubemapCanvas = new SKCanvas(cubemapBitmap);

                    for (int face = 0; face < 6; face++)
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
