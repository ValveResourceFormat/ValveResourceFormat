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
using ContentAlignment = System.Drawing.ContentAlignment;
using System.Drawing;

#nullable disable

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with material controls (render modes maybe at some point?).
    /// Renders a list of MatarialRenderers.
    /// </summary>
    class GLMaterialViewer : GLSingleNodeViewer
    {
        private enum ParamType
        {
            Float,
            Int,
            Vector,
            Bool
        }
        private readonly ValveResourceFormat.Resource Resource;
        private readonly TabControl Tabs;
        private TableLayoutPanel ParamsTable;
        private RenderMaterial renderMat;
        private MeshSceneNode previewNode;

        public GLMaterialViewer(VrfGuiContext guiContext, ValveResourceFormat.Resource resource, TabControl tabs) : base(guiContext)
        {
            Resource = resource;
            Tabs = tabs;

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

            // Collect all parameters with their types and sort them together
            var allParams = new List<(string name, object value, ParamType type)>();

            // Add float parameters
            foreach (var (paramName, initialValue) in drawCall.Material.Shader.Default.Material.FloatParams)
            {
                var currentValue = drawCall.Material.Material.FloatParams.GetValueOrDefault(paramName, initialValue);
                allParams.Add((paramName, currentValue, ParamType.Float));
            }

            // Add int parameters
            foreach (var (paramName, initialValue) in drawCall.Material.Shader.Default.Material.IntParams)
            {
                var currentValue = drawCall.Material.Material.IntParams.GetValueOrDefault(paramName, initialValue);
                if (drawCall.Material.Shader.IsBooleanParameter(paramName)
                    || paramName.StartsWith("F_", StringComparison.OrdinalIgnoreCase) && initialValue is 0 or 1)
                {
                    var boolValue = Convert.ToBoolean(currentValue);
                    allParams.Add((paramName, boolValue, ParamType.Bool));
                }
                else
                {
                    var int32Value = Convert.ToInt32(currentValue);
                    allParams.Add((paramName, int32Value, ParamType.Int));
                }
            }

            // Add vector parameters
            foreach (var (paramName, initialValue) in drawCall.Material.Shader.Default.Material.VectorParams)
            {
                var currentValue = drawCall.Material.Material.VectorParams.GetValueOrDefault(paramName, initialValue);
                var componentCount = drawCall.Material.Shader.GetRegisterSize(paramName);
                allParams.Add((paramName, (currentValue, componentCount), ParamType.Vector));
            }

            // Helper function to extract number from parameter name end
            static int GetParamNumber(string name)
            {
                var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+$");
                return match.Success ? int.Parse(match.Value) : -1;
            }

            // Sort and group parameters
            var sortedParams = allParams
                .OrderBy(p => GetParamNumber(p.name))
                .ThenBy(p => p.name)
                .ToList();

            var currentLayer = -1;

            // Add parameters to UI with layer headers
            foreach (var (paramName, value, type) in sortedParams)
            {
                var paramNumber = GetParamNumber(paramName);

                // Add layer header if we're starting a new numbered group
                if (paramNumber != -1 && paramNumber != currentLayer)
                {
                    currentLayer = paramNumber;

                    var headerRow = ParamsTable.RowCount;
                    ParamsTable.RowCount = headerRow + 1;
                    ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

                    var header = new Label
                    {
                        Text = $"Layer {paramNumber}",
                        Dock = DockStyle.Fill,
                        Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = System.Drawing.SystemColors.ControlLight,
                        ForeColor = System.Drawing.SystemColors.ControlText
                    };

                    var headerCell = new TableLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        Margin = new Padding(0),
                        Padding = new Padding(10, 5, 0, 5),
                        BackColor = System.Drawing.SystemColors.ControlLight
                    };
                    headerCell.Controls.Add(header);

                    ParamsTable.Controls.Add(headerCell, 0, headerRow);
                    ParamsTable.SetColumnSpan(headerCell, 2);
                }

                switch (type)
                {
                    case ParamType.Float:
                        AddNumericParameter(
                            paramName,
                            (decimal)(float)value,
                            ParamType.Float,
                            v => drawCall.Material.Material.FloatParams[paramName] = (float)v);
                        break;
                    case ParamType.Int:
                        AddNumericParameter(
                            paramName,
                            (int)value,
                            ParamType.Int,
                            v => drawCall.Material.Material.IntParams[paramName] = (int)v);
                        break;
                    case ParamType.Bool:
                        AddBooleanParameter(
                            paramName,
                            (bool)value,
                            v => drawCall.Material.Material.IntParams[paramName] = v ? 1 : 0);
                        break;
                    case ParamType.Vector:
                        var (vector, count) = ((Vector4, int))value;
                        AddVectorParameter(
                            paramName,
                            count,
                            vector,
                            v => drawCall.Material.Material.VectorParams[paramName] = v);
                        break;
                }
            }
        }

        private void AddBooleanParameter(string paramName, bool initialValue, Action<bool> onValueChanged)
        {
            var row = ParamsTable.RowCount;
            ParamsTable.RowCount = row + 1;
            ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            var displayName = NormalizeParameterName(paramName);

            ParamsTable.Controls.Add(new Label()
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = displayName,
                TextAlign = ContentAlignment.MiddleRight
            }, 0, row);

            var checkbox = new CheckBox
            {
                Dock = DockStyle.Fill,
                Checked = initialValue,
                AutoSize = true,
                Margin = new Padding(10, 0, 0, 0),
            };

            checkbox.CheckedChanged += (sender, e) =>
            {
                onValueChanged(checkbox.Checked);
            };

            ParamsTable.Controls.Add(checkbox, 1, row);
        }

        private void AddVectorParameter(string paramName, int componentCount, Vector4 value, Action<Vector4> onValueChanged)
        {
            var row = ParamsTable.RowCount;
            ParamsTable.RowCount = row + 1;
            ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            var displayName = NormalizeParameterName(paramName);

            ParamsTable.Controls.Add(new Label()
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = displayName,
                TextAlign = ContentAlignment.MiddleRight
            }, 0, row);

            var inputRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = componentCount,
                Margin = new Padding(0),
            };

            // Set equal width for each component
            var columnWidth = 100f / componentCount;
            for (var i = 0; i < componentCount; i++)
            {
                inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, columnWidth));
            }

            // Add NumericUpDown controls for vector components
            var inputs = new NumericUpDown[componentCount];
            for (var i = 0; i < componentCount; i++)
            {
                var index = i; // Capture for lambda
                var input = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                    DecimalPlaces = 3,
                    Increment = 0.1M,
                    Value = (decimal)(index == 0 ? value.X : index == 1 ? value.Y : index == 2 ? value.Z : value.W),
                };

                input.ValueChanged += (sender, e) =>
                {
                    // Keep existing W value for vec2/vec3, or use 1.0 as default
                    var w = componentCount < 4 ? (componentCount == 3 ? 1.0f : value.W) : (float)inputs[3].Value;
                    var newVector = new Vector4(
                        index == 0 ? (float)input.Value : (float)inputs[0].Value,
                        index == 1 ? (float)input.Value : (float)inputs[1].Value,
                        index == 2 && componentCount > 2 ? (float)input.Value : value.Z,
                        w
                    );
                    onValueChanged(newVector);
                };

                input.MouseWheel += (sender, e) =>
                {
                    // Fix bug where one scroll causes increments more than once, https://stackoverflow.com/a/16338022
                    (e as HandledMouseEventArgs).Handled = true;

                    if (e.Delta > 0)
                    {
                        input.Value -= input.Increment;
                    }
                    else if (e.Delta < 0)
                    {
                        input.Value += input.Increment;
                    }
                };

                inputs[i] = input;
                inputRow.Controls.Add(input, i, 0);
            }

            ParamsTable.Controls.Add(inputRow, 1, row);
        }

        static readonly string[] GlobalShaderPrefixes =
        [
            "g_fl",
            "g_f",
            "g_v",
            "g_b",
            "g_n",
        ];

        private static string NormalizeParameterName(string paramNameString)
        {
            // Handle feature flags (F_ prefix) - all uppercase, split by underscores
            if (paramNameString.StartsWith("F_"))
            {
                return paramNameString[2..].Replace('_', ' ');
            }

            var paramName = paramNameString.AsSpan();

            foreach (var prefix in GlobalShaderPrefixes)
            {
                if (paramName.StartsWith(prefix))
                {
                    paramName = paramName[prefix.Length..];
                    break;
                }
            }

            // Start with empty result
            var result = new System.Text.StringBuilder();

            for (var i = 0; i < paramName.Length; i++)
            {
                var c = paramName[i];

                // Replace underscores with spaces
                if (c == '_')
                {
                    result.Append(' ');
                }
                // Add space before capital letters only if preceded by lowercase
                else if (char.IsUpper(c) && i > 0 && result.Length > 0 && char.IsLower(paramName[i - 1]))
                {
                    result.Append(' ');
                    result.Append(c);
                }
                else
                {
                    result.Append(c);
                }
            }

            return result.ToString().Trim();
        }

        private void AddNumericParameter(string paramName, decimal initialValue, ParamType paramType, Action<decimal> onValueChanged)
        {
            var row = ParamsTable.RowCount;
            ParamsTable.RowCount = row + 1;
            ParamsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30F));

            var displayName = NormalizeParameterName(paramName);

            ParamsTable.Controls.Add(new Label()
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = displayName,
                TextAlign = ContentAlignment.MiddleRight
            }, 0, row);

            var inputRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = paramType == ParamType.Float ? 2 : 1,
                Margin = new Padding(0),
            };

            if (paramType == ParamType.Float)
            {
                inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
                inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

                var sliderMax = initialValue > 1 ? (int)(initialValue * 1000) : 1000;
                var sliderValue = (int)(Math.Clamp((double)initialValue, 0.0, (double)(sliderMax / 1000.0)) * 1000);

                var slider = new TrackBar
                {
                    Dock = DockStyle.Fill,
                    Minimum = 0,
                    Maximum = sliderMax,
                    TickStyle = TickStyle.None,
                    Value = sliderValue,
                };

                var input = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                    DecimalPlaces = 3,
                    Increment = 0.1M,
                    Value = initialValue,
                };

                var updatingFromSlider = false;

                slider.ValueChanged += (sender, e) =>
                {
                    if (!updatingFromSlider)
                    {
                        updatingFromSlider = true;
                        var newValue = slider.Value / 1000.0m;
                        input.Value = newValue;
                        updatingFromSlider = false;
                    }
                };

                input.ValueChanged += (sender, e) =>
                {
                    onValueChanged(input.Value);

                    // Update slider if value is in valid range
                    if (input.Value >= 0 && input.Value <= (decimal)(slider.Maximum / 1000.0) && !updatingFromSlider)
                    {
                        updatingFromSlider = true;
                        slider.Value = (int)(input.Value * 1000);
                        updatingFromSlider = false;
                    }
                };

                input.MouseWheel += (sender, e) =>
                {
                    // Fix bug where one scroll causes increments more than once, https://stackoverflow.com/a/16338022
                    (e as HandledMouseEventArgs).Handled = true;

                    if (e.Delta > 0)
                    {
                        input.Value -= input.Increment;
                    }
                    else if (e.Delta < 0)
                    {
                        input.Value += input.Increment;
                    }
                };

                inputRow.Controls.Add(slider, 0, 0);
                inputRow.Controls.Add(input, 1, 0);
            }
            else // Int parameter
            {
                inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                var input = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                    DecimalPlaces = 0,
                    Increment = 1M,
                    Value = initialValue,
                };

                input.ValueChanged += (sender, e) => onValueChanged(input.Value);

                input.MouseWheel += (sender, e) =>
                {
                    // Fix bug where one scroll causes increments more than once, https://stackoverflow.com/a/16338022
                    (e as HandledMouseEventArgs).Handled = true;

                    if (e.Delta > 0)
                    {
                        input.Value -= input.Increment;
                    }
                    else if (e.Delta < 0)
                    {
                        input.Value += input.Increment;
                    }
                };

                inputRow.Controls.Add(input, 0, 0);
            }

            ParamsTable.Controls.Add(inputRow, 1, row);
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            sunAngles = new Vector2(19, 196);
            Camera.FrameObjectFromAngle(Vector3.Zero, 0, 32, 32, MathUtils.ToRadians(180f), 0);

            if (renderMat.IsCs2Water)
            {
                Camera.FrameObjectFromAngle(Vector3.Zero, 32, 32, 0, 0, MathUtils.ToRadians(-90f));
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

            if (Tabs == null)
            {
                // todo: open in new tab when we're in preivew
                return;
            }

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
            ViewerSplitContainer.SplitterDistance = 450;

            SuspendLayout();
            AddRenderModeSelectionControl();
            AddDivider();

            AddShaderButton();
            AddDivider();

            var renderColorRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                Height = 30,
            };
            renderColorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            renderColorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            renderColorRow.Controls.Add(new Label()
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Text = "Render Color",
                TextAlign = ContentAlignment.MiddleRight
            }, 0, 0);

            var colorButton = new Button
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.White,
                FlatStyle = FlatStyle.Flat,
            };
            colorButton.Click += (sender, e) =>
            {
                var oldColor = colorButton.BackColor;

                using (var picker = new BetterColorPicker(colorButton.BackColor))
                {
                    picker.ColorChanged += (sender, args) =>
                    {
                        colorButton.BackColor = args.Color;
                        if (previewNode != null)
                        {
                            previewNode.Tint = new Vector4(
                                args.Color.R / 255f,
                                args.Color.G / 255f,
                                args.Color.B / 255f,
                                args.Color.A / 255f
                            );
                        }
                    };

                    var result = picker.ShowDialog();

                    var outColor = (result == DialogResult.OK) ? picker.PickedColor : oldColor;

                    colorButton.BackColor = outColor;
                    if (previewNode != null)
                    {
                        previewNode.Tint = new Vector4(
                            outColor.R / 255f,
                            outColor.G / 255f,
                            outColor.B / 255f,
                            outColor.A / 255f
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
            ParamsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            ParamsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            ResumeLayout();
        }
    }
}
