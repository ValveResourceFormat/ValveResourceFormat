using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Types.Renderer;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static GUI.Controls.SavedCameraPositionsControl;
using static GUI.Types.Renderer.PickingTexture;

#nullable disable

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    class GLWorldViewer : GLSceneViewer
    {
        private readonly World world;
        private readonly WorldNode worldNode;
        private readonly bool isFromVmap;
        private CheckedListBox worldLayersComboBox;
        private CheckedListBox physicsGroupsComboBox;
        private ComboBox cameraComboBox;
        private SavedCameraPositionsControl savedCameraPositionsControl;
        private EntityInfoForm entityInfoForm;
        private bool ignoreLayersChangeEvents = true;
        private List<Matrix4x4> CameraMatrices;

        public GLWorldViewer(VrfGuiContext guiContext, World world, bool isFromVmap = false)
            : base(guiContext)
        {
            this.world = world;
            this.isFromVmap = isFromVmap;
            Scene.EnableOcclusionCulling = isFromVmap;
        }

        public GLWorldViewer(VrfGuiContext guiContext, WorldNode worldNode)
            : base(guiContext)
        {
            this.worldNode = worldNode;
        }

        public override void Dispose()
        {
            base.Dispose();

            worldLayersComboBox?.Dispose();
            physicsGroupsComboBox?.Dispose();
            cameraComboBox?.Dispose();
            savedCameraPositionsControl?.Dispose();
            entityInfoForm?.Dispose();
        }

        private void AddSceneExposureSlider()
        {
            var exposureLabel = new Label();
            void UpdateExposureText(float exposure)
            {
                exposureLabel.Text = $"Exposure: {exposure:0.00}";
            }

            UiControl.AddControl(exposureLabel);

            var exposureSlider = UiControl.AddTrackBar((exposureAmountInt) =>
            {
                var exposure = exposureAmountInt / 10f;
                UpdateExposureText(exposure);
                Scene.PostProcessInfo.CustomExposure = exposure;
            });

            exposureSlider.TrackBar.Minimum = 1;
            exposureSlider.TrackBar.Maximum = 80;
            var sceneExposure = Scene.PostProcessInfo.CalculateTonemapScalar();
            exposureSlider.TrackBar.Value = (int)(sceneExposure * 10);
            UpdateExposureText(sceneExposure);
        }

        private void OnGetOrSetPositionFromClipboardRequest(object sender, bool isSetRequest)
        {
            var pitch = 0.0f;
            var yaw = 0.0f;

            if (!isSetRequest)
            {
                var loc = Camera.Location;
                pitch = -1.0f * Camera.Pitch * 180.0f / MathF.PI;
                yaw = Camera.Yaw * 180.0f / MathF.PI;

                Clipboard.SetText($"setpos {loc.X:F6} {loc.Y:F6} {loc.Z:F6}; setang {pitch:F6} {yaw:F6} 0.0");

                return;
            }

            var text = Clipboard.GetText();
            var pos = Regexes.SetPos().Match(text);
            var ang = Regexes.SetAng().Match(text);

            if (!pos.Success)
            {
                Log.Error(nameof(GLWorldViewer), "Failed to find setpos in clipboard text.");
                return;
            }

            var x = float.Parse(pos.Groups["x"].Value, CultureInfo.InvariantCulture);
            var y = float.Parse(pos.Groups["y"].Value, CultureInfo.InvariantCulture);
            var z = float.Parse(pos.Groups["z"].Value, CultureInfo.InvariantCulture);

            if (ang.Success)
            {
                pitch = -1f * float.Parse(ang.Groups["pitch"].Value, CultureInfo.InvariantCulture) * MathF.PI / 180f;
                yaw = float.Parse(ang.Groups["yaw"].Value, CultureInfo.InvariantCulture) * MathF.PI / 180f;
            }

            Camera.SaveCurrentForTransition();
            Camera.SetLocationPitchYaw(new Vector3(x, y, z), pitch, yaw);
        }

        private void OnRestoreCameraRequest(object sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
                    Camera.SaveCurrentForTransition();
                    Camera.SetLocationPitchYaw(
                        new Vector3(savedFloats[0], savedFloats[1], savedFloats[2]),
                        savedFloats[3],
                        savedFloats[4]);
                }
            }
        }

        private void OnSaveCameraRequest(object sender, EventArgs e)
        {
            var cam = Camera;
            var saveName = $"Camera at {cam.Location.X:F0} {cam.Location.Y:F0} {cam.Location.Z:F0}";
            var originalName = saveName;
            var duplicateCameraIndex = 1;

            while (Settings.Config.SavedCameras.ContainsKey(saveName))
            {
                saveName = $"{originalName} (#{duplicateCameraIndex++})";
            }

            Settings.Config.SavedCameras.Add(saveName, [cam.Location.X, cam.Location.Y, cam.Location.Z, cam.Pitch, cam.Yaw]);
            Settings.InvokeRefreshCamerasOnSave();
        }

        private WorldLoader LoadedWorld;
        private WorldNodeLoader LoadedWorldNode;

        protected override void LoadScene()
        {
            var cameraSet = false;

            if (world != null)
            {
                LoadedWorld = new WorldLoader(world, Scene);

                if (LoadedWorld.SkyboxScene != null)
                {
                    SkyboxScene = LoadedWorld.SkyboxScene;
                }

                if (LoadedWorld.Skybox2D != null)
                {
                    Skybox2D = LoadedWorld.Skybox2D;
                }

                NavMeshSceneNode.AddNavNodesToScene(LoadedWorld.NavMesh, Scene);

                if (LoadedWorld.CameraMatrices.Count > 0)
                {
                    CameraMatrices = LoadedWorld.CameraMatrices;

                    Camera.SetFromTransformMatrix(CameraMatrices[0]);
                    Camera.SetLocation(Camera.Location + Camera.GetForwardVector() * 10f); // Escape the camera model
                    cameraSet = true;
                }
            }

            if (!cameraSet)
            {
                Camera.SetLocation(new Vector3(256));
                Camera.LookAt(Vector3.Zero);
            }

            if (worldNode != null)
            {
                LoadedWorldNode = new WorldNodeLoader(GuiContext, worldNode);
                LoadedWorldNode.Load(Scene);
            }
        }

        public override Control InitializeUiControls()
        {
            base.InitializeUiControls();

            AddRenderModeSelectionControl();

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers([.. worldLayers]);
            });
            physicsGroupsComboBox = UiControl.AddMultiSelection("Physics Groups", null, (physicsGroups) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledPhysicsGroups([.. physicsGroups]);
            });

            savedCameraPositionsControl = new SavedCameraPositionsControl();
            savedCameraPositionsControl.SaveCameraRequest += OnSaveCameraRequest;
            savedCameraPositionsControl.RestoreCameraRequest += OnRestoreCameraRequest;
            savedCameraPositionsControl.GetOrSetPositionFromClipboardRequest += OnGetOrSetPositionFromClipboardRequest;
            UiControl.AddControl(savedCameraPositionsControl);

            cameraComboBox = UiControl.AddSelection("Map Camera", (cameraName, index) =>
            {
                if (index > 0)
                {
                    Camera.SaveCurrentForTransition();
                    Camera.SetFromTransformMatrix(CameraMatrices[index - 1]);
                }
            });

            UiControl.AddDivider();

            if (world != null)
            {
                var uniqueWorldLayers = new HashSet<string>(4);
                var uniquePhysicsGroups = new HashSet<string>();

                foreach (var node in Scene.AllNodes)
                {
                    if (node.LayerName.StartsWith("LightProbeGrid", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    uniqueWorldLayers.Add(node.LayerName);

                    if (node is PhysSceneNode physSceneNode)
                    {
                        uniquePhysicsGroups.Add(physSceneNode.PhysGroupName);
                    }
                }

                if (uniqueWorldLayers.Count > 0)
                {
                    worldLayersComboBox.BeginUpdate();

                    SetAvailableLayers(uniqueWorldLayers);

                    foreach (var worldLayer in LoadedWorld.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    worldLayersComboBox.EndUpdate();

                    Scene.SetEnabledLayers(LoadedWorld.DefaultEnabledLayers, skipUpdate: true);
                    SkyboxScene?.SetEnabledLayers(LoadedWorld.DefaultEnabledLayers, skipUpdate: true);
                }

                if (uniquePhysicsGroups.Count > 0)
                {
                    SetAvailablePhysicsGroups(uniquePhysicsGroups);
                }

                if (LoadedWorld.CameraMatrices.Count > 0)
                {
                    cameraComboBox.BeginUpdate();
                    cameraComboBox.Items.Add("Set view to camera…");
                    cameraComboBox.Items.AddRange([.. LoadedWorld.CameraNames]);
                    cameraComboBox.SelectedIndex = 0;
                    cameraComboBox.EndUpdate();
                }

                AddSceneExposureSlider();
            }

            if (worldNode != null)
            {
                var worldLayers = Scene.AllNodes
                    .Select(r => r.LayerName)
                    .Distinct()
                    .ToList();
                SetAvailableLayers(worldLayers);

                for (var i = 0; i < worldLayersComboBox.Items.Count; i++)
                {
                    worldLayersComboBox.SetItemChecked(i, true);
                }
            }

            savedCameraPositionsControl.RefreshSavedPositions();

            ignoreLayersChangeEvents = false;

            return UiControl;
        }

        private void SelectAndFocusEntity(EntityLump.Entity entity)
        {
            if (UiControl.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            var node = Scene.Find(entity);

            if (node == null && SkyboxScene != null)
            {
                node = SkyboxScene.Find(entity);
            }

            SelectAndFocusNode(node);
        }

        private void SelectAndFocusNode(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            SelectedNodeRenderer.SelectNode(node, forceDisableDepth: true);

            var bbox = node.BoundingBox;
            var size = bbox.Size;
            var maxDimension = Math.Max(Math.Max(size.X, size.Y), size.Z);
            var distance = maxDimension * 1.2f;
            var cameraHeight = bbox.Center.Y + size.Y * 2f;

            var location = new Vector3(bbox.Center.X + distance, cameraHeight, bbox.Center.Z + distance);
            Camera.SaveCurrentForTransition();
            Camera.SetLocation(location);
            Camera.LookAt(bbox.Center);

            // Ensure the node is visible
            if (!node.LayerEnabled)
            {
                var layerId = worldLayersComboBox.Items.IndexOf(node.LayerName);

                if (layerId >= 0)
                {
                    worldLayersComboBox.SetItemChecked(layerId, true);
                }
            }

            if (node is PhysSceneNode physNode && !physNode.Enabled)
            {
                var physId = physicsGroupsComboBox.Items.IndexOf(physNode.PhysGroupName);

                if (physId >= 0)
                {
                    physicsGroupsComboBox.SetItemChecked(physId, true);
                }
            }
        }

        private void ShowSceneNodeDetails(SceneNode sceneNode)
        {
            var isEntity = sceneNode.EntityData != null;
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm(GuiContext);
                entityInfoForm.Show();
                entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick += OnEntityInfoOutputsCellDoubleClick;
                entityInfoForm.EntityInfoControl.Disposed += OnEntityInfoFormDisposed;
            }
            entityInfoForm.EntityInfoControl.Clear();

            if (isEntity)
            {
                ShowEntityProperties(sceneNode);
            }
            else
            {
                entityInfoForm.Text = $"{sceneNode.GetType().Name}: {sceneNode.Name}";

                static string FormatVector(Vector3 vector)
                {
                    return $"{vector.X:F2} {vector.Y:F2} {vector.Z:F2}";
                }

                static string ToRenderColor(Vector4 tint)
                {
                    tint *= 255.0f;
                    return $"{tint.X:F0} {tint.Y:F0} {tint.Z:F0}";
                }

                if (sceneNode is SceneAggregate.Fragment sceneFragment)
                {
                    var material = sceneFragment.DrawCall.Material.Material;
                    entityInfoForm.EntityInfoControl.AddProperty("Shader", material.ShaderName);
                    entityInfoForm.EntityInfoControl.AddProperty("Material", material.Name);

                    var tris = sceneFragment.DrawCall.IndexCount / 3;
                    if (sceneFragment.DrawCall.NumMeshlets > 0)
                    {
                        var clusters = sceneFragment.DrawCall.NumMeshlets;
                        var trisPerCluster = tris / clusters;
                        entityInfoForm.EntityInfoControl.AddProperty("Triangles / Clusters / Per Cluster", $"{tris} / {clusters} / {trisPerCluster}");
                    }
                    else
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Triangles", $"{tris}");
                    }

                    entityInfoForm.EntityInfoControl.AddProperty("Model Tint", ToRenderColor(sceneFragment.DrawCall.TintColor));
                    entityInfoForm.EntityInfoControl.AddProperty("Model Alpha", $"{sceneFragment.DrawCall.TintColor.W:F6}");

                    if (sceneFragment.Tint != Vector4.One)
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Instance Tint", ToRenderColor(sceneFragment.Tint));
                        entityInfoForm.EntityInfoControl.AddProperty("Final Tint", ToRenderColor(sceneFragment.DrawCall.TintColor * sceneFragment.Tint));
                    }
                }
                else if (sceneNode is ModelSceneNode modelSceneNode)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Model Tint", ToRenderColor(modelSceneNode.Tint));
                    entityInfoForm.EntityInfoControl.AddProperty("Model Alpha", $"{modelSceneNode.Tint.W:F6}");

                    if (modelSceneNode.LightingOrigin.HasValue)
                    {
                        entityInfoForm.EntityInfoControl.AddProperty("Custom Lighting Origin", FormatVector(modelSceneNode.LightingOrigin.Value));
                    }
                }

                if (sceneNode.CubeMapPrecomputedHandshake > 0)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Cubemap Handshake", $"{sceneNode.CubeMapPrecomputedHandshake}");
                }

                if (sceneNode.LightProbeVolumePrecomputedHandshake > 0)
                {
                    entityInfoForm.EntityInfoControl.AddProperty("Light Probe Handshake", $"{sceneNode.LightProbeVolumePrecomputedHandshake}");
                }

                entityInfoForm.EntityInfoControl.AddProperty("Flags", sceneNode.Flags.ToString());
                entityInfoForm.EntityInfoControl.AddProperty("Layer", sceneNode.LayerName);
            }

            if (SkyboxScene != null && sceneNode.Scene == SkyboxScene)
            {
                entityInfoForm.Text += " (in 3D skybox)";
            }

            entityInfoForm.EntityInfoControl.ShowOutputsTabIfAnyData();
            entityInfoForm.EntityInfoControl.Show();
        }

        private void OnEntityInfoOutputsCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 1)
            {
                return;
            }

            var entityName = (string)(entityInfoForm.EntityInfoControl.OutputsGrid[e.ColumnIndex, e.RowIndex].Value ?? string.Empty);

            if (string.IsNullOrEmpty(entityName))
            {
                return;
            }

            var node = Scene.FindNodeByKeyValue("targetname", entityName);

            if (node == null && SkyboxScene != null)
            {
                node = SkyboxScene.FindNodeByKeyValue("targetname", entityName);
            }

            SelectAndFocusNode(node);
            ShowSceneNodeDetails(node);
        }

        private void OnEntityInfoFormDisposed(object sender, EventArgs e)
        {
            entityInfoForm.EntityInfoControl.OutputsGrid.CellDoubleClick -= OnEntityInfoOutputsCellDoubleClick;
            entityInfoForm.EntityInfoControl.Disposed -= OnEntityInfoFormDisposed;
            entityInfoForm = null;
        }

        protected override void OnPicked(object sender, PickingResponse pickingResponse)
        {
            var pixelInfo = pickingResponse.PixelInfo;

            // Void
            if (pixelInfo.ObjectId == 0 || pixelInfo.Unused2 != 0)
            {
                SelectedNodeRenderer.SelectNode(null);
                return;
            }

            var isInSkybox = pixelInfo.IsSkybox > 0;
            var sceneNode = isInSkybox ? SkyboxScene.Find(pixelInfo.ObjectId) : Scene.Find(pixelInfo.ObjectId);

            if (sceneNode == null)
            {
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Select)
            {
                if ((Control.ModifierKeys & Keys.Control) > 0)
                {
                    SelectedNodeRenderer.ToggleNode(sceneNode);
                }
                else
                {
                    SelectedNodeRenderer.SelectNode(sceneNode);
                }

                //Update the entity properties window if it was opened
                if (entityInfoForm != null)
                {
                    ShowSceneNodeDetails(sceneNode);
                }
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Details)
            {
                ShowSceneNodeDetails(sceneNode);
                entityInfoForm.EntityInfoControl.Focus();
                return;
            }

            Log.Info(nameof(GLWorldViewer), $"Opening {sceneNode.Name} (Id: {pixelInfo.ObjectId})");

            var filename = sceneNode.Name;

            if (sceneNode.EntityData != null)
            {
                // Perhaps this needs to check for correct classname?
                var particle = sceneNode.EntityData.GetProperty<string>("effect_name");

                if (particle != null)
                {
                    filename = particle;
                }
            }

            var foundFile = GuiContext.FindFileWithContext(filename + GameFileLoader.CompiledFileSuffix);

            if (foundFile.Context == null)
            {
                return;
            }

            if (!Matrix4x4.Invert(sceneNode.Transform * Camera.CameraViewMatrix, out var transform))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            FullScreenForm?.Close();

            foundFile.Context.GLPostLoadAction = (viewerControl) =>
            {
                var yaw = MathF.Atan2(-transform.M32, -transform.M31);
                var scaleZ = MathF.Sqrt(transform.M31 * transform.M31 + transform.M32 * transform.M32 + transform.M33 * transform.M33);
                var unscaledZ = transform.M33 / scaleZ;
                var pitch = MathF.Asin(-unscaledZ);

                viewerControl.Camera.CopyFrom(Camera);
                viewerControl.Camera.SetLocationPitchYaw(transform.Translation, pitch, yaw);

                if (viewerControl is not GLModelViewer glModelViewer || sceneNode is not ModelSceneNode worldModel)
                {
                    return;
                }

                // Set same mesh groups
                if (glModelViewer.meshGroupListBox != null)
                {
                    foreach (int checkedItemIndex in glModelViewer.meshGroupListBox.CheckedIndices)
                    {
                        glModelViewer.meshGroupListBox.SetItemChecked(checkedItemIndex, false);
                    }

                    foreach (var group in worldModel.GetActiveMeshGroups())
                    {
                        var item = glModelViewer.meshGroupListBox.FindStringExact(group);

                        if (item != ListBox.NoMatches)
                        {
                            glModelViewer.meshGroupListBox.SetItemChecked(item, true);
                        }
                    }
                }

                // Set same material group
                if (glModelViewer.materialGroupListBox != null && worldModel.ActiveMaterialGroup != null)
                {
                    var skinId = glModelViewer.materialGroupListBox.FindStringExact(worldModel.ActiveMaterialGroup);

                    if (skinId != -1)
                    {
                        glModelViewer.materialGroupListBox.SelectedIndex = skinId;
                    }
                }

                // Set animation
                if (glModelViewer.animationComboBox != null && worldModel.AnimationController.ActiveAnimation != null)
                {
                    var animationId = glModelViewer.animationComboBox.FindStringExact(worldModel.AnimationController.ActiveAnimation.Name);

                    if (animationId != -1)
                    {
                        glModelViewer.animationComboBox.SelectedIndex = animationId;
                    }
                }
            };

            Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
        }

        private void ShowEntityProperties(SceneNode sceneNode)
        {
            foreach (var (key, value) in sceneNode.EntityData.Properties)
            {
                entityInfoForm.EntityInfoControl.AddProperty(key, value switch
                {
                    null => string.Empty,
                    KVObject { IsArray: true } kvArray => string.Join(' ', kvArray.Select(p => p.Value.ToString())),
                    _ => value.ToString(),
                });
            }

            if (sceneNode.EntityData.Connections != null)
            {
                foreach (var connection in sceneNode.EntityData.Connections)
                {
                    entityInfoForm.EntityInfoControl.AddConnection(connection);
                }
            }

            var classname = sceneNode.EntityData.GetProperty<string>("classname");
            entityInfoForm.Text = $"Entity: {classname}";
        }

        private void SetAvailableLayers(IEnumerable<string> worldLayers)
        {
            worldLayersComboBox.Items.Clear();

            var worldLayersArray = worldLayers.ToArray();

            if (worldLayersArray.Length > 0)
            {
                worldLayersComboBox.Enabled = true;
                worldLayersComboBox.Items.AddRange(worldLayersArray);
            }
            else
            {
                worldLayersComboBox.Enabled = false;
            }
        }

        private const string PhysicsRenderAsOpaque = "S2V: Render as opaque";

        private void SetAvailablePhysicsGroups(IEnumerable<string> physicsGroups)
        {
            physicsGroupsComboBox.Items.Clear();

            var physicsGroupsArray = physicsGroups
                .OrderByDescending(static s => s.StartsWith('-'))
                .ToArray();

            if (physicsGroupsArray.Length > 0)
            {
                physicsGroupsComboBox.Enabled = true;
                physicsGroupsComboBox.Items.AddRange(physicsGroupsArray);
                physicsGroupsComboBox.Items.Add(PhysicsRenderAsOpaque);
            }
            else
            {
                physicsGroupsComboBox.Enabled = false;
            }
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            var renderTranslucent = !physicsGroups.Contains(PhysicsRenderAsOpaque);

            if (!renderTranslucent)
            {
                physicsGroups.Remove(PhysicsRenderAsOpaque);
            }

            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
                physNode.IsTranslucentRenderMode = renderTranslucent;
            }

            Scene.UpdateOctrees();
            SkyboxScene?.UpdateOctrees();
        }
    }
}
