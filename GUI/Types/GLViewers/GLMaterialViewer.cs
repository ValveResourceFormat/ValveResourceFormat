using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with material controls and render mode selection.
    /// </summary>
    class GLMaterialViewer : GLSingleNodeViewer
    {
        private enum ParamType
        {
            Float,
            Int,
            Vector,
            Bool,
            Color,
        }

        private enum ParameterPresence
        {
            MaterialOnly,
            ShaderOnly,
            Both
        }

        private readonly Resource Resource;
        private TabControl? Tabs;
        private Button? openShaderButton;
        private Panel? ParamsPanel;
        private RenderMaterial? renderMat;
        private ComboBox? previewObjectComboBox;

        private enum PreviewObjectType
        {
            Quad,
            Sphere,
            CustomModel,
            Cable,
        }

        // Cable shaders only draw particle cables, so their materials are previewed on this effect instead of a mesh
        private const string CablePreviewEffect = "particles/dev/dev_cables_preview.vpcf";
        private const float CableSettleTime = 1f;

        private PreviewObjectType currentPreviewObject = PreviewObjectType.Quad;
        private readonly Dictionary<PreviewObjectType, SceneNode> previewObjects = [];
        private SceneNode previewNode => previewObjects[currentPreviewObject];
        private float cableFrameDelay = -1f;
        private ShaderCollection? vcsShader;

        public GLMaterialViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Resource resource) : base(vrfGuiContext, rendererContext)
        {
            Resource = resource;
        }

        public void SetTabControl(TabControl tabs)
        {
            Tabs = tabs;
        }

        public override void Dispose()
        {
            ParamsPanel?.Dispose();
            openShaderButton?.Dispose();
            previewObjectComboBox?.Dispose();
            vcsShader?.Dispose();

            base.Dispose();
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            Renderer.Settings.ShowToolsMaterials = true;
            renderMat = Renderer.RendererContext.MaterialLoader.LoadMaterial(Resource, Scene.RenderAttributes);
            renderMat.Shader.EnsureLoaded();
            renderMat.IsOverlay = false; // render without trying to overlay on empty space

            if (renderMat.ShaderName == "cables.vfx" && GuiContext.LoadFileCompiled(CablePreviewEffect)?.DataBlock is ParticleSystem cablePreview)
            {
                // Without the endcap, which would decay the cable as soon as it has been emitted
                var cableNode = new ParticleSceneNode(Scene, cablePreview, preview: true)
                {
                    PlaybackMode = ParticlePlaybackMode.Normal,
                };
                cableNode.SetMaterialOverride(renderMat);
                Scene.Add(cableNode, true);
                previewObjects[PreviewObjectType.Cable] = cableNode;
                currentPreviewObject = PreviewObjectType.Cable;
            }
            else
            {
                AddMeshPreviews(renderMat);
            }

            vcsShader = GuiContext.LoadShader(renderMat.Material.ShaderName);
        }

        private void AddMeshPreviews(RenderMaterial renderMat)
        {
            {
                var planeMesh = MeshSceneNode.CreateMaterialPreviewQuad(Scene, renderMat, new Vector2(32));
                planeMesh.Transform = Matrix4x4.CreateRotationZ(float.DegreesToRadians(90f));

                var isHorizontalPlaneMaterial = renderMat.IsCs2Water;
                if (!isHorizontalPlaneMaterial)
                {
                    planeMesh.Transform *= Matrix4x4.CreateRotationY(float.DegreesToRadians(90f));
                }

                Scene.Add(planeMesh, false);
                previewObjects[PreviewObjectType.Quad] = planeMesh;
            }

            {
                var sphereMesh = MeshSceneNode.CreateMaterialPreviewSphere(Scene, renderMat, 8f);
                Scene.Add(sphereMesh, false);
                previewObjects[PreviewObjectType.Sphere] = sphereMesh;
            }

            if (Resource.DataBlock is Material material && material.StringAttributes.TryGetValue("PreviewModel", out var previewModel))
            {
                var previewModelResource = GuiContext.LoadFileCompiled(previewModel);

                if (previewModelResource != null && previewModelResource.DataBlock is Model modelData)
                {
                    var customModel = new ModelSceneNode(Scene, modelData);

                    foreach (var renderable in customModel.RenderableMeshes)
                    {
                        renderable.SetMaterialForMaterialViewer(Resource);
                    }

                    Scene.Add(customModel, false);
                    previewObjects[PreviewObjectType.CustomModel] = customModel;
                    currentPreviewObject = PreviewObjectType.CustomModel;
                }
            }
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each row adds its editors to its Controls, which dispose them")]
        private void CreateMaterialEditControls(RenderMaterial loadedMaterial)
        {
            Debug.Assert(ParamsPanel != null);

            // Mesh previews may draw with their own copy of the material, so edit that one
            var previewMaterial = loadedMaterial;
            if (previewNode is MeshCollectionNode meshNode)
            {
                var mesh = meshNode.RenderableMeshes[0];
                previewMaterial = mesh.DrawCallsOpaque.Concat(mesh.DrawCallsBlended).First().Material;
            }

            // Collect all parameters with their types and sort them together
            var allParams = new List<(string name, object value, ParamType type, VfxVariableDescription? vfx)>();

            var shaderParams = previewMaterial.Shader.Default;

            var allParameterNames = new HashSet<string>(previewMaterial.FloatParams.Keys);
            allParameterNames.UnionWith(previewMaterial.IntParams.Keys);
            allParameterNames.UnionWith(previewMaterial.VectorParams.Keys);
            allParameterNames.UnionWith(shaderParams.FloatParams.Keys);
            allParameterNames.UnionWith(shaderParams.IntParams.Keys);
            allParameterNames.UnionWith(shaderParams.VectorParams.Keys);

            var vcsDescriptionByName = new Dictionary<string, VfxVariableDescription>();
            if (vcsShader?.Features != null)
            {
                foreach (var varDesc in vcsShader.Features.VariableDescriptions)
                {
                    vcsDescriptionByName[varDesc.Name] = varDesc;
                }
            }

            foreach (var paramName in allParameterNames)
            {
                var inMaterial = previewMaterial.FloatParams.ContainsKey(paramName) ||
                    previewMaterial.IntParams.ContainsKey(paramName) ||
                    previewMaterial.VectorParams.ContainsKey(paramName);
                var inShader = shaderParams.FloatParams.ContainsKey(paramName) ||
                    shaderParams.IntParams.ContainsKey(paramName) ||
                    shaderParams.VectorParams.ContainsKey(paramName);
                var parameterPresence = (inShader, inMaterial) switch
                {
                    (false, true) => ParameterPresence.MaterialOnly,
                    (true, false) => ParameterPresence.ShaderOnly,
                    _ => ParameterPresence.Both,
                };

                var vfxDescription = vcsDescriptionByName.GetValueOrDefault(paramName);

                if (parameterPresence == ParameterPresence.ShaderOnly && vcsDescriptionByName.Count > 0 && vfxDescription == null)
                {
                    continue;
                }

                // Handle float parameters
                if (previewMaterial.FloatParams.TryGetValue(paramName, out var floatValue) || shaderParams.FloatParams.TryGetValue(paramName, out floatValue))
                {
                    allParams.Add((paramName, (floatValue, parameterPresence), ParamType.Float, vfxDescription));
                    continue;
                }

                // Handle int/bool parameters
                if (previewMaterial.IntParams.TryGetValue(paramName, out var intValue) || shaderParams.IntParams.TryGetValue(paramName, out intValue))
                {
                    if (previewMaterial.Shader.IsBooleanParameter(paramName)
                        || paramName.StartsWith("F_", StringComparison.OrdinalIgnoreCase) && intValue is 0 or 1)
                    {
                        var boolValue = Convert.ToBoolean(intValue);
                        allParams.Add((paramName, (boolValue, parameterPresence), ParamType.Bool, vfxDescription));
                    }
                    else
                    {
                        var int32Value = Convert.ToInt32(intValue);
                        allParams.Add((paramName, (int32Value, parameterPresence), ParamType.Int, vfxDescription));
                    }
                    continue;
                }

                // Handle vector parameters
                if (previewMaterial.VectorParams.TryGetValue(paramName, out var vectorValue) || shaderParams.VectorParams.TryGetValue(paramName, out vectorValue))
                {
                    var componentCount = previewMaterial.Shader.GetRegisterSize(paramName);

                    if (vfxDescription?.UiType == UiType.Color)
                    {
                        vectorValue.W = 1f;
                        allParams.Add((paramName, (vectorValue, parameterPresence), ParamType.Color, vfxDescription));
                    }
                    else
                    {
                        allParams.Add((paramName, (vectorValue, componentCount, parameterPresence), ParamType.Vector, vfxDescription));
                    }
                }
            }

            // Sort and group parameters
            var sortedParams = allParams
                .OrderBy(p => p.vfx?.UiGroup.Heading)
                .ThenBy(p => p.vfx?.UiGroup.HeadingOrder)
                .ThenBy(p => p.vfx?.UiGroup.Group)
                .ThenBy(p => p.vfx?.UiGroup.GroupOrder)
                .ThenBy(p => p.vfx?.UiGroup.VariableOrder)
                .ThenBy(p => p.name)
                .ToList();

            var currentHeading = string.Empty;
            ParamsPanel.SuspendLayout();

            // Add parameters to UI with layer headers
            foreach (var (paramName, value, type, vfxDescription) in sortedParams)
            {
                if (vfxDescription != null && vfxDescription.UiGroup.Heading != currentHeading)
                {
                    currentHeading = vfxDescription.UiGroup.Heading;
                    AddParameterRow(CreateParameterHeader(currentHeading));
                }

                var displayName = NormalizeParameterName(paramName);

                switch (type)
                {
                    case ParamType.Float:
                        var (floatVal, floatPresence) = ((float, ParameterPresence))value;
                        var (floatMin, floatMax) = vfxDescription != null
                            ? (vfxDescription.FloatMins[0], vfxDescription.FloatMaxs[0])
                            : (0f, 1f);

                        // The slider widens to include the value, so an unusable range only needs a starting point
                        if (!(floatMin < floatMax) || !float.IsFinite(floatMax - floatMin))
                        {
                            (floatMin, floatMax) = (0f, 1f);
                        }

                        var (slider, field) = RendererControl.CreateRangedFloatEditor(floatVal, floatMin, floatMax, wholeNumbers: false,
                            v => previewMaterial.FloatParams[paramName] = v);
                        AddParameterRow(CreateParameterRow(displayName, [slider], field), floatPresence != ParameterPresence.MaterialOnly);
                        break;
                    case ParamType.Int:
                        var (intVal, intPresence) = ((int, ParameterPresence))value;
                        var (intMin, intMax) = vfxDescription != null
                            ? (vfxDescription.IntMins[0], vfxDescription.IntMaxs[0])
                            : (Math.Min(intVal, 0), Math.Max(intVal, 1));

                        if (intMin > intMax)
                        {
                            (intMin, intMax) = (Math.Min(intVal, 0), Math.Max(intVal, 1));
                        }

                        var intInput = new ThemedIntNumeric
                        {
                            MinValue = intMin,
                            MaxValue = intMax,
                            Value = Math.Clamp(intVal, intMin, intMax),
                        };

                        intInput.ValueChanged += (_, _) =>
                        {
                            previewMaterial.IntParams[paramName] = intInput.Value;
                            previewMaterial.LoadRenderState();
                        };

                        AddParameterRow(CreateParameterRow(displayName, [intInput]), intPresence != ParameterPresence.MaterialOnly);
                        break;
                    case ParamType.Bool:
                        var (boolVal, boolPresence) = ((bool, ParameterPresence))value;
                        var checkBox = RendererControl.CreateRowCheckBox(boolVal, v =>
                        {
                            previewMaterial.IntParams[paramName] = v ? 1 : 0;
                            previewMaterial.LoadRenderState();
                        });
                        AddParameterRow(CreateParameterRow(displayName, [checkBox]), boolPresence != ParameterPresence.MaterialOnly);
                        break;
                    case ParamType.Vector:
                        var (vector, count, vectorPresence) = ((Vector4, int, ParameterPresence))value;
                        var vectorFields = CreateVectorFields(vector, Math.Clamp(count, 1, 4), v => previewMaterial.VectorParams[paramName] = v);
                        AddParameterRow(CreateParameterRow(displayName, vectorFields), vectorPresence != ParameterPresence.MaterialOnly);
                        break;

                    case ParamType.Color:
                        var (colorVec, colorPresence) = ((Vector4, ParameterPresence))value;
                        var colorButton = CreateColorButton(Vector4ToColor(colorVec), c => previewMaterial.VectorParams[paramName] = ColorToVector4(c));
                        AddParameterRow(CreateParameterRow(displayName, [colorButton]), colorPresence != ParameterPresence.MaterialOnly);
                        break;
                }
            }

            ParamsPanel.ResumeLayout();
        }

        private static PropertyRow CreateParameterRow(string name, Control[] editors, params Control[] trailing)
            => new(name, editors, trailing) { NameAlign = ContentAlignment.MiddleRight };

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The header adds the label to its Controls, which dispose it")]
        private static Panel CreateParameterHeader(string heading)
        {
            var label = RendererControl.CreateSectionHeader(heading);
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.BottomRight;

            var header = new Panel
            {
                Margin = Padding.Empty,
            };

            header.Height = header.AdjustForDPI(28);

            // The label takes its font from the sidebar, which the header only knows once it is in it
            header.ParentChanged += (_, _) => header.Controls.Add(label);

            // Ends where the names of the rows below end
            header.Layout += (_, _) => label.SetBounds(0, 0, header.Width / 2 - header.AdjustForDPI(PropertyRow.Gap), header.Height);

            return header;
        }

        private void AddParameterRow(Control row, bool isEnabled = true)
        {
            Debug.Assert(ParamsPanel != null);

            row.Enabled = isEnabled;
            row.Dock = DockStyle.Top;
            ParamsPanel.Controls.Add(row);
            row.BringToFront();
        }

        private static ThemedFloatNumeric[] CreateVectorFields(Vector4 value, int componentCount, Action<Vector4> onValueChanged)
        {
            var fields = new ThemedFloatNumeric[componentCount];

            for (var i = 0; i < fields.Length; i++)
            {
                fields[i] = RendererControl.CreateFloatField(value[i], 3, _ =>
                {
                    // Components past the register size keep their value
                    var newVector = value;

                    for (var component = 0; component < fields.Length; component++)
                    {
                        newVector[component] = fields[component].Value;
                    }

                    onValueChanged(newVector);
                });
            }

            return fields;
        }

        private static string NormalizeParameterName(string paramNameString)
        {
            // Handle feature flags (F_ prefix) - all uppercase, split by underscores
            if (paramNameString.StartsWith("F_", StringComparison.Ordinal))
            {
                return paramNameString[2..].Replace('_', ' ');
            }

            var paramName = paramNameString.AsSpan();

            foreach (var prefix in VfxVariableDescription.TypePrefixes)
            {
                if (paramName.StartsWith(prefix))
                {
                    paramName = paramName[prefix.Length..];
                    break;
                }
            }

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

        private static ThemedButton CreateColorButton(Color initialColor, Action<Color> onValueChanged)
        {
            var colorButton = new ThemedButton
            {
                BackColor = initialColor,
                FlatStyle = FlatStyle.Flat,
                Style = false,
            };

            colorButton.Height = colorButton.AdjustForDPI(20);

            colorButton.Click += (sender, e) =>
            {
                using var picker = new BetterColorPicker(colorButton.BackColor, (pickedColor) =>
                {
                    colorButton.BackColor = pickedColor;
                    onValueChanged(pickedColor);
                });

                picker.ShowDialog();
            };

            return colorButton;
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();
            sunAngles = new Vector2(19, 196);

            Input.OrbitModeAlways = true;
            Input.OrbitTarget = Vector3.Zero;

            UpdateSunAngles();
            Scene.UpdateBuffers();
        }

        protected override void OnUpdate(float frameTime)
        {
            base.OnUpdate(frameTime);

            // Framed once the cable has sagged, it starts out straight
            if (cableFrameDelay < 0f)
            {
                return;
            }

            cableFrameDelay -= frameTime;

            if (cableFrameDelay > 0f)
            {
                return;
            }

            cableFrameDelay = -1f;

            // The preview cable runs along X, so look at it from the side
            var bounds = previewNode.BoundingBox;
            Input.Camera.FrameObjectFromAngle(bounds.Center, bounds.Size.X, bounds.Size.Y, bounds.Size.Z, float.DegreesToRadians(-90f), float.DegreesToRadians(15f));
            Input.OrbitTarget = bounds.Center;
        }

        protected override void OnFirstPaint()
        {
            Input.Camera.FrameObjectFromAngle(Vector3.Zero, 0, 32, 32, float.DegreesToRadians(180f), 0);
            if (currentPreviewObject == PreviewObjectType.Cable)
            {
                cableFrameDelay = CableSettleTime;
            }
            else if (currentPreviewObject == PreviewObjectType.CustomModel)
            {
                var bounds = previewNode.BoundingBox;
                var direction = CameraPlacement.PreferredDirection(bounds.Size);
                Input.Camera.SetLocation(bounds.Center + direction * Input.Camera.GetFramingDistance(bounds.Size, -direction));
                Input.Camera.LookAt(bounds.Center);
            }
            else if (renderMat != null && renderMat.IsCs2Water)
            {
                Input.Camera.FrameObjectFromAngle(Vector3.Zero, 32, 32, 0, 0, float.DegreesToRadians(90f));
            }

            if (previewNode != null)
            {
                Input.OrbitTarget = previewNode.BoundingBox.Center;
            }

            Input.ForceUpdate = true;
        }

        private void OnShadersButtonClick(object? s, EventArgs e)
        {
            if (Resource.DataBlock is not Material material)
            {
                return;
            }

            var featureState = ShaderDataProvider.GetMaterialFeatureState(material);

            if (Tabs == null || vcsShader == null)
            {
                // todo: open in new tab when we're in preview
                return;
            }

            var loadingTabPage = new ThemedTabPage(material.ShaderName);
            var loadingFile = new LoadingFile();
            loadingTabPage.Controls.Add(loadingFile);
            Tabs.TabPages.Add(loadingTabPage);
            Tabs.SelectTab(loadingTabPage);

            var viewer = new CompiledShader(GuiContext);

            try
            {
                var tabPage = new ThemedTabPage(material.ShaderName);
                Tabs.TabPages.Add(tabPage);
                viewer.Create(
                    tabPage,
                    vcsShader,
                    Path.GetFileNameWithoutExtension(material.ShaderName.AsSpan()),
                    ValveResourceFormat.CompiledShader.VcsProgramType.Features,
                    featureState
                );
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
            Debug.Assert(UiControl != null);

            openShaderButton = new ThemedButton
            {
                Text = $"Open Shader",
                AutoSize = true,
            };

            openShaderButton.Click += OnShadersButtonClick;

            UiControl.AddControl(openShaderButton);
        }

        static Color Vector4ToColor(Vector4 v)
        {
            return Color.FromArgb(
                (int)(v.W * 255),
                (int)(v.X * 255),
                (int)(v.Y * 255),
                (int)(v.Z * 255));
        }

        private void RenderMeshPreview_SelectionChanged(object? sender, EventArgs e)
        {
            Debug.Assert(previewObjectComboBox != null);

            foreach (var node in previewObjects.Values)
            {
                node.LayerEnabled = false;
            }

            if (Scene != null && previewObjectComboBox.SelectedIndex >= 0)
            {
                currentPreviewObject = Enum.Parse<PreviewObjectType>(previewObjectComboBox.SelectedItem?.ToString() ?? string.Empty);
                previewNode.LayerEnabled = true;
                OnFirstPaint();
            }
        }

        static Vector4 ColorToVector4(Color c)
        {
            return Vector4.Create(c.R, c.G, c.B, c.A) / 255f;
        }

        [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Each row adds its editors to its Controls, which dispose them")]
        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            // Make controls panel wider for material parameters
            UiControl.UseWideSplitter();

            AddRenderModeSelectionControl();

            AddShaderButton();
            UiControl.AddDivider();

            var colorButton = CreateColorButton(Color.White, pickedColor =>
            {
                if (previewNode != null)
                {
                    previewNode.TintAlpha = ColorToVector4(pickedColor);
                }
            });

            previewObjectComboBox = new ThemedComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            previewObjectComboBox.SelectedIndexChanged += RenderMeshPreview_SelectionChanged;

            UiControl.AddControl(CreateParameterRow("Render Color", [colorButton]));
            UiControl.AddControl(CreateParameterRow("Render Mesh", [previewObjectComboBox]));

            ParamsPanel = new Panel
            {
                AutoSize = true,
                Margin = Padding.Empty,
            };

            UiControl.AddDivider();
            UiControl.AddControl(ParamsPanel);
            UiControl.AddDivider();

            // Populate UI controls with scene data
            if (renderMat != null)
            {
                if (openShaderButton != null)
                {
                    openShaderButton.Text = renderMat.Material.ShaderName;
                }

                previewObjectComboBox.Items.AddRange([.. Enum.GetNames<PreviewObjectType>().Where(n => previewObjects.ContainsKey(Enum.Parse<PreviewObjectType>(n)))]);
                previewObjectComboBox.SelectedItem = currentPreviewObject.ToString();

                CreateMaterialEditControls(renderMat);
            }

            base.AddUiControls();
        }
    }
}
