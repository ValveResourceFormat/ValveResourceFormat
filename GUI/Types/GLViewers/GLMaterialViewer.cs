using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.Renderer;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

#nullable disable

namespace GUI.Types.GLViewers
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

            var node = CreatePreviewModel();

            Scene.ShowToolsMaterials = true;
            Scene.Add(node, false);

#if DEBUG
            // Assume cubemap model only has one opaque draw call
            var mesh = node.RenderableMeshes[0];
            var drawCall = mesh.DrawCallsOpaque.Concat(mesh.DrawCallsBlended).First();

            drawCall.Material.Shader.EnsureLoaded();

            foreach (var (paramName, initialValue) in drawCall.Material.Shader.Default.Material.FloatParams.OrderBy(x => x.Key))
            {
                var currentvalue = drawCall.Material.Material.FloatParams.GetValueOrDefault(paramName, initialValue);

                var row = ParamsTable.RowCount;
                ParamsTable.RowCount = row + 2;
                ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
                ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

                ParamsTable.Controls.Add(new Label()
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Text = paramName
                }, 0, row);

                var currentParamName = paramName;
                var input = new NumericUpDown
                {
                    Width = ParamsTable.Width / 2,
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                    DecimalPlaces = 6,
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
                ParamsTable.Controls.Add(input, 0, row + 1);
            }
#endif
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            // todo: adjust position based on selected fov.
            Camera.SetLocationPitchYaw(new Vector3(21.5f, 0, 0), 0, MathUtils.ToRadians(180f));
            sunAngles = new Vector2(19, 196);
            UpdateSunAngles();
            Scene.UpdateBuffers();
        }

        private MeshCollectionNode CreatePreviewModel()
        {
            var material = (Material)Resource.DataBlock;
            MeshCollectionNode node = null;

            if (material.StringAttributes.TryGetValue("PreviewModel", out var previewModel))
            {
                var previewModelResource = GuiContext.LoadFileCompiled(previewModel);

                if (previewModelResource != null)
                {
                    node = new ModelSceneNode(Scene, (Model)previewModelResource.DataBlock);
                }
            }

            node ??= MeshSceneNode.CreateMaterialPreviewQuad(Scene, GuiContext.MaterialLoader.LoadMaterial(Resource), new Vector2(32));
            node.Transform = Matrix4x4.CreateRotationZ(MathUtils.ToRadians(90f))
                * Matrix4x4.CreateRotationY(MathUtils.ToRadians(90f));

            //node ??= CreateEnvCubemapSphere(Scene);

            foreach (var renderable in node.RenderableMeshes)
            {
                renderable.SetMaterialForMaterialViewer(Resource);
            }

            return node;
        }

        public static ModelSceneNode CreateEnvCubemapSphere(Scene scene)
        {
            var node = new ModelSceneNode(scene, (Model)ShapeSceneNode.CubemapResource.Value.DataBlock);
            return node;
        }

        private void OnShadersButtonClick(object s, EventArgs e)
        {
            var material = (Material)Resource.DataBlock;
            var featureState = ShaderDataProvider.GetMaterialFeatureState(material);

            var loadingTabPage = new TabPage(material.ShaderName);
            var loadingFile = new LoadingFile();
            loadingTabPage.Controls.Add(loadingFile);
            Tabs.TabPages.Add(loadingTabPage);
            Tabs.SelectTab(loadingTabPage);

            var viewer = new CompiledShader(GuiContext);

            try
            {
                var shaders = GuiContext.LoadShader(material.ShaderName);

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
            AddRenderModeSelectionControl();

            ParamsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoScroll = true,
                Width = 220,
                Height = 300,
            };
            AddControl(ParamsTable);

            ParamsTable.ColumnCount = 1;
            ParamsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize, 1));
        }
    }
}
