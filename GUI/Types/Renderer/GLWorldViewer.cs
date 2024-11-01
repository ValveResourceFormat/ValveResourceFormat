using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static GUI.Controls.SavedCameraPositionsControl;
using static GUI.Types.Renderer.PickingTexture;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    class GLWorldViewer : GLSceneViewer
    {
        private readonly World world;
        private readonly WorldNode worldNode;
        private CheckedListBox worldLayersComboBox;
        private CheckedListBox physicsGroupsComboBox;
        private ComboBox cameraComboBox;
        private SavedCameraPositionsControl savedCameraPositionsControl;
        private EntityInfoForm entityInfoForm;
        private bool ignoreLayersChangeEvents = true;
        private List<Matrix4x4> CameraMatrices;

        public GLWorldViewer(VrfGuiContext guiContext, World world)
            : base(guiContext)
        {
            this.world = world;
        }

        public GLWorldViewer(VrfGuiContext guiContext, WorldNode worldNode)
            : base(guiContext)
        {
            this.worldNode = worldNode;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                worldLayersComboBox?.Dispose();
                physicsGroupsComboBox?.Dispose();
                cameraComboBox?.Dispose();
                savedCameraPositionsControl?.Dispose();
                entityInfoForm?.Dispose();
            }
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();

            worldLayersComboBox = AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers(new HashSet<string>(worldLayers));
            });
            physicsGroupsComboBox = AddMultiSelection("Physics Groups", null, (physicsGroups) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledPhysicsGroups(new HashSet<string>(physicsGroups));
            });

            savedCameraPositionsControl = new SavedCameraPositionsControl();
            savedCameraPositionsControl.SaveCameraRequest += OnSaveCameraRequest;
            savedCameraPositionsControl.RestoreCameraRequest += OnRestoreCameraRequest;
            savedCameraPositionsControl.GetOrSetPositionFromClipboardRequest += OnGetOrSetPositionFromClipboardRequest;
            AddControl(savedCameraPositionsControl);

            cameraComboBox = AddSelection("Map Camera", (cameraName, index) =>
            {
                if (index > 0)
                {
                    Camera.SetFromTransformMatrix(CameraMatrices[index - 1]);
                }
            });

            AddDivider();
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

            Camera.SetLocationPitchYaw(new Vector3(x, y, z), pitch, yaw);
        }

        private void OnRestoreCameraRequest(object sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
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

        protected override void LoadScene()
        {
            var cameraSet = false;

            if (world != null)
            {
                var result = new WorldLoader(world, Scene);

                AddCheckBox("Show Fog", Scene.FogEnabled, v => Scene.FogEnabled = v);
                AddCheckBox("Color Correction", postProcessRenderer.ColorCorrectionEnabled, v => postProcessRenderer.ColorCorrectionEnabled = v);

                if (result.SkyboxScene != null)
                {
                    SkyboxScene = result.SkyboxScene;

                    AddCheckBox("Show Skybox", ShowSkybox, (v) => ShowSkybox = v);
                }

                if (result.Skybox2D != null)
                {
                    Skybox2D = result.Skybox2D;
                }

                var uniqueWorldLayers = new HashSet<string>(4);
                var uniquePhysicsGroups = new HashSet<string>();

                foreach (var node in Scene.AllNodes)
                {
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

                    foreach (var worldLayer in result.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    worldLayersComboBox.EndUpdate();

                    Scene.SetEnabledLayers(result.DefaultEnabledLayers, skipUpdate: true);
                    SkyboxScene?.SetEnabledLayers(result.DefaultEnabledLayers, skipUpdate: true);
                }

                if (uniquePhysicsGroups.Count > 0)
                {
                    SetAvailablPhysicsGroups(uniquePhysicsGroups);
                }

                if (result.CameraMatrices.Count > 0)
                {
                    CameraMatrices = result.CameraMatrices;

                    cameraComboBox.BeginUpdate();
                    cameraComboBox.Items.Add("Set view to camera…");
                    cameraComboBox.Items.AddRange([.. result.CameraNames]);
                    cameraComboBox.SelectedIndex = 0;
                    cameraComboBox.EndUpdate();

                    Camera.SetFromTransformMatrix(result.CameraMatrices[0]);
                    Camera.SetLocation(Camera.Location + Camera.GetForwardVector() * 10f); // Escape the camera model
                    cameraSet = true;
                }
            }

            if (!cameraSet)
            {
                cameraComboBox.Parent.Dispose();

                Camera.SetLocation(new Vector3(256));
                Camera.LookAt(Vector3.Zero);
            }

            if (worldNode != null)
            {
                var loader = new WorldNodeLoader(GuiContext, worldNode);
                loader.Load(Scene);

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

            Invoke(savedCameraPositionsControl.RefreshSavedPositions);

            ignoreLayersChangeEvents = false;
        }

        private void ShowSceneNodeDetails(SceneNode sceneNode, bool isInSkybox)
        {
            var isEntity = sceneNode.EntityData != null;
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm(GuiContext.FileLoader);
                entityInfoForm.Disposed += OnEntityInfoFormDisposed;
            }
            entityInfoForm.Clear();
            entityInfoForm.SetEntityLayout(isEntity);

            if (isEntity)
            {
                ShowEntityProperties(sceneNode);
            }
            else
            {
                entityInfoForm.Text = $"{sceneNode.GetType().Name}: {sceneNode.Name}";

                static string ToRenderColor(Vector4 tint)
                {
                    tint *= 255.0f;
                    return $"{tint.X:F0} {tint.Y:F0} {tint.Z:F0}";
                }

                if (sceneNode is SceneAggregate.Fragment sceneFragment)
                {
                    var material = sceneFragment.DrawCall.Material.Material;
                    entityInfoForm.AddProperty("Shader", material.ShaderName);
                    entityInfoForm.AddProperty("Material", material.Name);

                    var tris = sceneFragment.DrawCall.IndexCount / 3;
                    if (sceneFragment.DrawCall.NumMeshlets > 0)
                    {
                        var clusters = sceneFragment.DrawCall.NumMeshlets;
                        var trisPerCluster = tris / clusters;
                        entityInfoForm.AddProperty("Triangles / Clusters / Per Cluster", $"{tris} / {clusters} / {trisPerCluster}");
                    }
                    else
                    {
                        entityInfoForm.AddProperty("Triangles", $"{tris}");
                    }

                    entityInfoForm.AddProperty("Model Tint", ToRenderColor(sceneFragment.DrawCall.TintColor));
                    entityInfoForm.AddProperty("Model Alpha", $"{sceneFragment.DrawCall.TintColor.W:F6}");

                    if (sceneFragment.Tint != Vector4.One)
                    {
                        entityInfoForm.AddProperty("Instance Tint", ToRenderColor(sceneFragment.Tint));
                        entityInfoForm.AddProperty("Final Tint", ToRenderColor(sceneFragment.DrawCall.TintColor * sceneFragment.Tint));
                    }
                }
                else if (sceneNode is ModelSceneNode modelSceneNode)
                {
                    entityInfoForm.AddProperty("Model Tint", ToRenderColor(modelSceneNode.Tint));
                    entityInfoForm.AddProperty("Model Alpha", $"{modelSceneNode.Tint.W:F6}");
                }

                if (sceneNode.CubeMapPrecomputedHandshake > 0)
                {
                    entityInfoForm.AddProperty("Cubemap Handshake", $"{sceneNode.CubeMapPrecomputedHandshake}");
                }

                if (sceneNode.LightProbeVolumePrecomputedHandshake > 0)
                {
                    entityInfoForm.AddProperty("Light Probe Handshake", $"{sceneNode.LightProbeVolumePrecomputedHandshake}");
                }

                entityInfoForm.AddProperty("Flags", sceneNode.Flags.ToString());
                entityInfoForm.AddProperty("Layer", sceneNode.LayerName);
            }

            if (isInSkybox)
            {
                entityInfoForm.Text += " (in 3D skybox)";
            }

            entityInfoForm.Show();
        }

        private void OnEntityInfoFormDisposed(object sender, EventArgs e)
        {
            entityInfoForm.Disposed -= OnEntityInfoFormDisposed;
            entityInfoForm = null;
        }

        protected override void OnPicked(object sender, PickingResponse pickingResponse)
        {
            var pixelInfo = pickingResponse.PixelInfo;

            // Void
            if (pixelInfo.ObjectId == 0 || pixelInfo.Unused2 != 0)
            {
                selectedNodeRenderer.SelectNode(null);
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
                if ((ModifierKeys & Keys.Control) > 0)
                {
                    selectedNodeRenderer.ToggleNode(sceneNode);
                }
                else
                {
                    selectedNodeRenderer.SelectNode(sceneNode);
                }

                //Update the entity properties window if it was opened
                if (entityInfoForm != null)
                {
                    ShowSceneNodeDetails(sceneNode, isInSkybox);
                }
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Details)
            {
                ShowSceneNodeDetails(sceneNode, isInSkybox);
                entityInfoForm.Focus();
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

            var foundFile = GuiContext.FileLoader.FindFileWithContext(filename + GameFileLoader.CompiledFileSuffix);

            if (foundFile.Context == null)
            {
                return;
            }

            Matrix4x4.Invert(sceneNode.Transform * Camera.CameraViewMatrix, out var transform);

            FullScreenForm?.Close();

            Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry).ContinueWith(
                t =>
                {
                    var glViewer = t.Result.Controls.OfType<TabControl>().FirstOrDefault()?
                        .Controls.OfType<TabPage>().First(tab => tab.Controls.OfType<GLViewerControl>() is not null)?
                        .Controls.OfType<GLViewerControl>().First();
                    if (glViewer is not null)
                    {
                        glViewer.GLPostLoad = (viewerControl) =>
                        {
                            var yaw = MathF.Atan2(-transform.M32, -transform.M31);
                            var scaleZ = MathF.Sqrt(transform.M31 * transform.M31 + transform.M32 * transform.M32 + transform.M33 * transform.M33);
                            var unscaledZ = transform.M33 / scaleZ;
                            var pitch = MathF.Asin(-unscaledZ);

                            viewerControl.Camera.SetLocationPitchYaw(transform.Translation, pitch, yaw);

                            if (sceneNode is not ModelSceneNode worldModel)
                            {
                                return;
                            }

                            if (glViewer is GLModelViewer glModelViewer)
                            {
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
                            }
                        };
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
        }

        private void ShowEntityProperties(SceneNode sceneNode)
        {
            foreach (var (key, value) in sceneNode.EntityData.Properties)
            {
                entityInfoForm.AddProperty(key, value switch
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
                    entityInfoForm.AddConnection(connection);
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

        private void SetAvailablPhysicsGroups(IEnumerable<string> physicsGroups)
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
