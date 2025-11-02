using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

#nullable disable

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with material controls (render modes maybe at some point?).
    /// Renders a list of MatarialRenderers.
    /// </summary>
    class GLMaterialViewer : GLSingleNodeViewer
    {
        private readonly ValveResourceFormat.Resource Resource;
        private readonly TabControl Tabs;
        private TableLayoutPanel ParamsTable;
        private RenderMaterial renderMat;
        private MeshSceneNode previewNode;

        public GLMaterialViewer(VrfGuiContext guiContext, ValveResourceFormat.Resource resource, TabControl tabs) : base(guiContext)
        {
            Resource = resource;
            Tabs = tabs;

            AddShaderButton();

            Camera.ModifySpeed(0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ParamsTable?.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            previewNode = CreatePreviewModel();

            Scene.ShowToolsMaterials = true;
            Scene.Add(previewNode, false);

            // Assume cubemap model only has one opaque draw call
            var mesh = previewNode.RenderableMeshes[0];
            var drawCall = mesh.DrawCallsOpaque.Concat(mesh.DrawCallsBlended).First();

            drawCall.Material.Shader.EnsureLoaded();

            foreach (var (paramName, initialValue) in drawCall.Material.Shader.Default.Material.FloatParams.OrderBy(x => x.Key))
            {
                var currentvalue = drawCall.Material.Material.FloatParams.GetValueOrDefault(paramName, initialValue);

                var row = ParamsTable.RowCount;
                ParamsTable.RowCount = row + 1;
                ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

                ParamsTable.Controls.Add(new Label()
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Text = paramName
                }, 0, row);

                var currentParamName = paramName;
                var input = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                    DecimalPlaces = 3,
                    Increment = 0.1M,
                    Value = (decimal)initialValue
                };
                input.ValueChanged += (sender, e) =>
                {
                    drawCall.Material.Material.FloatParams[currentParamName] = (float)input.Value;
                };
                input.MouseWheel += (sender, e) =>
                {
                    // Fix bug where one scroll causes increments more than once, https://stackoverflow.com/a/16338022
                    (e as HandledMouseEventArgs).Handled = true;

                    if (e.Delta > 0)
                    {
                        input.Value += input.Increment;
                    }
                    else if (e.Delta < 0)
                    {
                        input.Value -= input.Increment;
                    }
                };
                ParamsTable.Controls.Add(input, 1, row);
            }
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            // todo: adjust position based on selected fov.
            Camera.SetLocationPitchYaw(new Vector3(21.5f, 0, 0), 0, MathUtils.ToRadians(180f));
            sunAngles = new Vector2(19, 196);

            if (renderMat.IsCs2Water)
            {
                Camera.SetLocationPitchYaw(new Vector3(27.720367f, -0.7101173f, 22.564936f), -0.59182787f, 9.421663f);
                sunAngles = new Vector2(54, -9);
            }

            UpdateSunAngles();
            Scene.UpdateBuffers();
        }

        private MeshSceneNode CreatePreviewModel()
        {
            var material = (Material)Resource.DataBlock;
            MeshSceneNode node = null;

            if (material.StringAttributes.TryGetValue("PreviewModel", out var previewModel))
            {
                var previewModelResource = GuiContext.FileLoader.LoadFileCompiled(previewModel);

                if (previewModelResource != null)
                {
                    // For preview models, we can't directly use ModelSceneNode, so fall back to the quad
                    // node = new ModelSceneNode(Scene, (Model)previewModelResource.DataBlock);
                }
            }

            renderMat = GuiContext.MaterialLoader.LoadMaterial(Resource, Scene.RenderAttributes);
            node ??= MeshSceneNode.CreateMaterialPreviewQuad(Scene, renderMat, new Vector2(32));
            node.Transform = Matrix4x4.CreateRotationZ(MathUtils.ToRadians(90f));

            var isHorizontalPlaneMaterial = renderMat.IsCs2Water;
            if (!isHorizontalPlaneMaterial)
            {
                node.Transform *= Matrix4x4.CreateRotationY(MathUtils.ToRadians(90f));
            }

            //node ??= CreateEnvCubemapSphere(Scene);
            //foreach (var renderable in node.RenderableMeshes)
            //{
            //    renderable.SetMaterialForMaterialViewer(Resource);
            //}

            return node;
        }

        public static ModelSceneNode CreateEnvCubemapSphere(Scene scene)
        {
            var node = new ModelSceneNode(scene, (Model)CubemapResource.Value.DataBlock);
            return node;
        }

        public static Lazy<ValveResourceFormat.Resource> CubemapResource = new(() =>
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream($"GUI.Utils.env_cubemap.vmdl_c");
            var resource = new ValveResourceFormat.Resource()
            {
                FileName = "env_cubemap.vmdl_c"
            };

            resource.Read(stream);
            return resource;
        });

        private void OnShadersButtonClick(object s, EventArgs e)
        {
            var material = (Material)Resource.DataBlock;
            var featureState = ShaderDataProvider.GetMaterialFeatureState(material);

            var loadingTabPage = new TabPage(material.ShaderName);
            var loadingFile = new LoadingFile();
            loadingTabPage.Controls.Add(loadingFile);
            Tabs.TabPages.Add(loadingTabPage);
            Tabs.SelectTab(loadingTabPage);

            var viewer = new CompiledShader();

            try
            {
                var shaders = GuiContext.FileLoader.LoadShader(material.ShaderName);

                var tabPage = viewer.Create(
                    shaders,
                    Path.GetFileNameWithoutExtension(material.ShaderName.AsSpan()),
                    ValveResourceFormat.CompiledShader.VcsProgramType.Features,
                    featureState
                );
                tabPage.Text = material.ShaderName;
                Tabs.TabPages.Add(tabPage);
                viewer = null;

                Tabs.SelectTab(tabPage);
            }
            finally
            {
                viewer?.Dispose();

                Tabs.TabPages.Remove(loadingTabPage);
                loadingTabPage.Dispose();
            }
        }

        private void AddShaderButton()
        {
            if (Tabs == null)
            {
                return; // Will be null when previewing a file
            }

            var button = new Button
            {
                Text = "Decompile shader",
                AutoSize = true,
            };

            button.Click += OnShadersButtonClick;

            AddControl(button);
        }

        protected override void InitializeControl()
        {
            // Make controls panel wider for material parameters
            ViewerSplitContainer.SplitterDistance = 400;

            AddRenderModeSelectionControl();

            AddDivider();

            var renderColorRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Height = 30,
            };
            renderColorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            renderColorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

            renderColorRow.Controls.Add(new Label()
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "Render Color"
            }, 0, 0);

            var colorButton = new Button
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            colorButton.Click += (sender, e) =>
            {
                using var colorDialog = new ColorDialog
                {
                    Color = colorButton.BackColor,
                    FullOpen = true
                };

                if (colorDialog.ShowDialog() == DialogResult.OK)
                {
                    colorButton.BackColor = colorDialog.Color;
                    if (previewNode != null)
                    {
                        var color = colorDialog.Color;
                        previewNode.Tint = new Vector4(
                            color.R / 255f,
                            color.G / 255f,
                            color.B / 255f,
                            color.A / 255f
                        );
                    }
                }
            };
            renderColorRow.Controls.Add(colorButton, 1, 0);

            AddControl(renderColorRow);

            ParamsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
            };

            AddDivider();
            AddControl(ParamsTable);
            AddDivider();

            ParamsTable.ColumnCount = 2;
            ParamsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            ParamsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        }
    }
}
