using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.CompiledShader;
using ValveResourceFormat.IO;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace GUI.Types.Viewers
{
    class CompiledShader : IDisposable, IViewer
    {
        private TextControl control;
        private TreeView fileListView;
        private readonly Container components;
        private ThemedContextMenuStrip shaderFileContextMenu;
        private readonly VrfGuiContext vrfGuiContext;

        public static bool IsAccepted(uint magic)
        {
            return magic == VfxProgramData.MAGIC;
        }

        public CompiledShader(VrfGuiContext vrfGuiContext)
        {
            this.vrfGuiContext = vrfGuiContext;
            fileListView = new TreeViewDoubleBuffered
            {
                FullRowSelect = true,
                HideSelection = false,
                ShowRootLines = false,
                ShowNodeToolTips = true,
                Dock = DockStyle.Fill,
                ImageList = MainForm.ImageList,
            };
            fileListView.NodeMouseClick += OnNodeMouseClick;
            Themer.ThemeControl(fileListView);

            components = new Container();
            shaderFileContextMenu = new ThemedContextMenuStrip(components)
            {
                ImageScalingSize = new System.Drawing.Size(24, 24),
            };
            var exportBytecodeMenuItem = new ThemedToolStripMenuItem
            {
                Text = "Export bytecode",
                SVGImageResourceName = "GUI.Icons.Export.svg",
            };
            exportBytecodeMenuItem.Click += OnExportBytecodeClick;
            shaderFileContextMenu.Items.Add(exportBytecodeMenuItem);

            control = new TextControl(CodeTextBox.HighlightLanguage.Shaders);
            control.AddControl(fileListView);
        }

        public async Task LoadAsync(Stream? stream)
        {
            // Creating shader collection doesn't actually use the provided stream which is kind of a waste
            if (stream is not null)
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
        }

        public void Create(TabPage tab)
        {
            var filename = Path.GetFileName(vrfGuiContext.FileName);
            var leadProgramType = ComputeVCSFileName(filename).ProgramType;
            var vcsCollectionName = filename.AsSpan(0, filename.LastIndexOf('_')); // in the form water_dota_pcgl_40

            var shaderCollection = ShaderCollection.GetShaderCollection(vrfGuiContext.FileName, vrfGuiContext.CurrentPackage);

            Create(tab, shaderCollection, vcsCollectionName, leadProgramType);
        }

        public void Create(TabPage tab, ShaderCollection shaderCollection, ReadOnlySpan<char> vcsCollectionName, VcsProgramType leadProgramType, IDictionary<string, byte>? leadFeatureParams = null)
        {
            tab.Controls.Add(control);

            var vfxImage = MainForm.Icons["FolderShaders"];
            var programImage = MainForm.GetImageIndexForExtension("vcs");
            var comboImage = MainForm.GetImageIndexForExtension("pdi");

            var materialCollectionIndex = 0;
            var collectionNode = new TreeNode($"{vcsCollectionName}.vfx")
            {
                ImageIndex = vfxImage,
                SelectedImageIndex = vfxImage,
            };
            fileListView.Nodes.Add(collectionNode);

            List<string> sfNamesAbbrev = [];
            List<string> sfNames = [];

            Debug.Assert(shaderCollection.Features != null);

            foreach (var program in shaderCollection.OrderBy(static x => x.VcsProgramType))
            {
                var truncatedProgramName = program.FilenamePath.AsSpan();

                if (truncatedProgramName.StartsWith(vcsCollectionName))
                {
                    truncatedProgramName = truncatedProgramName[(vcsCollectionName.Length + 1)..];
                }

                var programNode = new TreeNode(truncatedProgramName.ToString())
                {
                    Tag = program,
                    ImageIndex = programImage,
                    SelectedImageIndex = programImage,
                };
                collectionNode.Nodes.Add(programNode);

                if (program.StaticComboEntries.Count > 0)
                {
                    var configGen = new ConfigMappingParams(program);
                    var leadStaticComboId = -1L; // Shader file to be displayed for a particular material

                    if (leadFeatureParams != null)
                    {
                        leadStaticComboId = ShaderDataProvider.GetStaticConfiguration_ForFeatureState(shaderCollection.Features, program, leadFeatureParams).StaticComboId;
                    }

                    foreach (var staticComboEntry in program.StaticComboEntries)
                    {
                        var config = configGen.GetConfigState(staticComboEntry.Key);

                        sfNames.Clear();
                        sfNamesAbbrev.Clear();

                        for (var i = 0; i < program.StaticComboArray.Length; i++)
                        {
                            if (config[i] == 0)
                            {
                                continue;
                            }

                            var sfBlock = program.StaticComboArray[i];
                            var sfShortName = ShortenShaderParam(sfBlock.Name).ToLowerInvariant();

                            if (config[i] > 1)
                            {
                                sfNames.Add($"{sfBlock.Name}={config[i]}");
                                sfNamesAbbrev.Add($"{sfShortName}={config[i]}");
                            }
                            else
                            {
                                sfNames.Add(sfBlock.Name);
                                sfNamesAbbrev.Add(sfShortName);
                            }
                        }

                        var variantsAbbrev = sfNamesAbbrev.Count > 0 ? $" ({string.Join(", ", sfNamesAbbrev)})" : string.Empty;
                        var variantsTooltip = string.Join(Environment.NewLine, sfNames);

                        var comboNode = new TreeNode($"{staticComboEntry.Key:x08}{variantsAbbrev}")
                        {
                            ToolTipText = variantsTooltip,
                            Tag = staticComboEntry.Value,
                            ImageIndex = comboImage,
                            SelectedImageIndex = comboImage,
                        };
                        programNode.Nodes.Add(comboNode);

                        if (staticComboEntry.Key == leadStaticComboId)
                        {
                            // When viewing from a material, unserialize the correct static combo straight away
                            var combo = staticComboEntry.Value.Unserialize();
                            comboNode.Tag = combo;
                            CreateStaticComboNodes(combo, comboNode);

                            // Program files that do not store any ShaderFiles
                            if (program.VcsProgramType == VcsProgramType.PixelShaderRenderState)
                            {
                                continue;
                            }

                            var shaderFile = combo.ShaderFiles[0];

                            if (shaderFile.Bytecode.Length > 0)
                            {
                                var matImage = MainForm.GetImageIndexForExtension("vmat");
                                var matNode = new TreeNode($"Material {program.VcsProgramType}{variantsAbbrev}")
                                {
                                    ToolTipText = variantsTooltip,
                                    Tag = combo.ShaderFiles[0],
                                    ImageIndex = matImage,
                                    SelectedImageIndex = matImage,
                                };
                                collectionNode.Nodes.Insert(materialCollectionIndex++, matNode);
                            }
                        }
                    }

                    if (program.VcsProgramType == leadProgramType)
                    {
                        programNode.Expand();
                    }
                }
            }

            collectionNode.Expand();

            // ShaderExtract cannot continue without the features file present
            if (shaderCollection.Features is not null)
            {
                var shaderExtract = new ShaderExtract(shaderCollection);

                collectionNode.Tag = shaderExtract;
                DisplayExtractedVfx(shaderExtract);
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                fileListView?.Dispose();
                control?.Dispose();
                components?.Dispose();
                shaderFileContextMenu?.Dispose();
            }
        }

        private void OnNodeMouseClick(object? sender, TreeNodeMouseClickEventArgs e)
        {
            if (e.Node == null)
            {
                return;
            }

            if (e.Button == MouseButtons.Right && e.Node.Tag is VfxShaderFile clickedShader)
            {
                if (clickedShader.Bytecode.Length > 0)
                {
                    shaderFileContextMenu.Tag = clickedShader;
                    shaderFileContextMenu.Show(fileListView, e.Location);
                }

                return;
            }

            if (e.Node.Tag is ShaderExtract shaderExtract)
            {
                DisplayExtractedVfx(shaderExtract);
            }
            else if (e.Node.Tag is VfxProgramData program)
            {
                using var output = new IndentedTextWriter();
                program.PrintSummary(output);
                control.TextBox.Text = output.ToString();
            }
            else if (e.Node.Tag is VfxStaticComboVcsEntry comboEntry)
            {
                var combo = comboEntry.Unserialize();
                e.Node.Tag = combo; // Replace the entry with unserialized combo data

                DisplayStaticCombo(combo);

                fileListView.BeginUpdate();
                CreateStaticComboNodes(combo, e.Node);
                fileListView.EndUpdate();
            }
            else if (e.Node.Tag is VfxStaticComboData combo)
            {
                DisplayStaticCombo(combo);
            }
            else if (e.Node.Tag is VfxShaderFile shaderFile)
            {
                // TODO: Perhaps these tabs can also be removed, but currently required for the byte viewer
                control.TextBox.Text = shaderFile.GetDecompiledFile();

                /*
                TODO: We need to display the bytecode somehow

                tabs = new TabControl
                {
                    Dock = DockStyle.Fill,
                };

                // source
                var sourceBvTab = new TabPage("Source");
                var sourceBv = new System.ComponentModel.Design.ByteViewer
                {
                    Dock = DockStyle.Fill,
                };
                sourceBvTab.Controls.Add(sourceBv);
                resTabs.TabPages.Add(sourceBvTab);

                // text
                var reflectedSource = AttemptSpirvReflection(vulkanSource, Backend.GLSL);

                var textTab = new TabPage("SPIR-V");
                var textBox = new CodeTextBox(reflectedSource, CodeTextBox.HighlightLanguage.Shaders);
                textTab.Controls.Add(textBox);
                resTabs.TabPages.Add(textTab);
                resTabs.SelectedTab = textTab;

                if (!vulkanSource.IsEmpty())
                {
                    Program.MainForm.Invoke((MethodInvoker)(() =>
                    {
                        sourceBv.SetBytes(vulkanSource.Bytecode);
                    }));
                }
                */
            }
        }

        private static void CreateStaticComboNodes(VfxStaticComboData combo, TreeNode treeNode)
        {
            var sourceFileImage = MainForm.GetImageIndexForExtension("ini");

            List<string> dfNamesAbbrev = [];
            List<string> dfNames = [];
            var sourceIdToRenderStateInfo = new Dictionary<int, VfxRenderStateInfo>(combo.ShaderFiles.Length);

            // We are only taking the first render state info currently
            foreach (var renderStateInfo in combo.DynamicCombos)
            {
                sourceIdToRenderStateInfo.TryAdd(renderStateInfo.ShaderFileId, renderStateInfo);
            }

            Debug.Assert(combo.ParentProgramData != null);

            foreach (var source in combo.ShaderFiles)
            {
                if (source.Size == 0)
                {
                    continue;
                }

                var config = combo.ParentProgramData.GetDBlockConfig(sourceIdToRenderStateInfo[source.ShaderFileId].DynamicComboId);

                dfNames.Clear();
                dfNamesAbbrev.Clear();

                for (var i = 0; i < combo.ParentProgramData.DynamicComboArray.Length; i++)
                {
                    if (config[i] == 0)
                    {
                        continue;
                    }

                    var dfBlock = combo.ParentProgramData.DynamicComboArray[i];
                    var dfShortName = ShortenShaderParam(dfBlock.Name).ToLowerInvariant();

                    if (config[i] > 1)
                    {
                        dfNames.Add($"{dfBlock.Name}={config[i]}");
                        dfNamesAbbrev.Add($"{dfShortName}={config[i]}");
                    }
                    else
                    {
                        dfNames.Add(dfBlock.Name);
                        dfNamesAbbrev.Add(dfShortName);
                    }
                }

                var node = new TreeNode($"{source.ShaderFileId:X2}{(dfNamesAbbrev.Count > 0 ? $" ({string.Join(", ", dfNamesAbbrev)})" : string.Empty)}")
                {
                    ToolTipText = string.Join(Environment.NewLine, dfNames),
                    Tag = source,
                    ImageIndex = sourceFileImage,
                    SelectedImageIndex = sourceFileImage,
                };
                treeNode.Nodes.Add(node);
            }

            treeNode.Expand();
        }

        private void DisplayExtractedVfx(ShaderExtract shaderExtract)
        {
            try
            {
                control.TextBox.Text = shaderExtract.ToVFX();
            }
            catch (Exception ex)
            {
                control.TextBox.Text = ex.ToString();
            }
        }

        private void DisplayStaticCombo(VfxStaticComboData combo)
        {
            using var output = new IndentedTextWriter();
            var zframeSummary = new PrintZFrameSummary(combo, output);
            control.TextBox.Text = output.ToString();
        }

        private void OnExportBytecodeClick(object? sender, EventArgs e)
        {
            if (shaderFileContextMenu.Tag is not VfxShaderFile shaderFile)
            {
                return;
            }

            var combo = shaderFile.ParentCombo;
            Debug.Assert(combo.ParentProgramData != null);

            var extension = shaderFile.BlockName == "VULKAN" ? "spv" : shaderFile.BlockName.ToLowerInvariant();
            using var dialog = new SaveFileDialog
            {
                Title = "Export bytecode",
                FileName = $"{combo.ParentProgramData.ShaderName}_{combo.StaticComboId:x08}_{shaderFile.ShaderFileId:x02}",
                InitialDirectory = Settings.Config.SaveDirectory,
                DefaultExt = extension,
                Filter = $"{shaderFile.BlockName} bytecode (*.{extension})|*.{extension}|All files (*.*)|*.*",
                AddToRecent = true,
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            if (Path.GetDirectoryName(dialog.FileName) is { } directory)
            {
                Settings.Config.SaveDirectory = directory;
            }

            File.WriteAllBytes(dialog.FileName, shaderFile.Bytecode);
        }
    }
}
