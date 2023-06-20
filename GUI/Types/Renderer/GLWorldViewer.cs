using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Utils;
using static GUI.Controls.SavedCameraPositionsControl;
using static GUI.Types.Renderer.PickingTexture;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
#pragma warning disable CA1001 // Types that own disposable fields should be disposable
    class GLWorldViewer : GLSceneViewer
#pragma warning restore CA1001 // Types that own disposable fields should be disposable
    {
        private readonly World world;
        private readonly WorldNode worldNode;
        private CheckedListBox worldLayersComboBox;
        private CheckedListBox physicsGroupsComboBox;
        private ComboBox cameraComboBox;
        private SavedCameraPositionsControl savedCameraPositionsControl;

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

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();

            worldLayersComboBox = ViewerControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                SetEnabledLayers(new HashSet<string>(worldLayers));
            });
            physicsGroupsComboBox = ViewerControl.AddMultiSelection("Physics Groups", null, (physicsGroups) =>
            {
                SetEnabledPhysicsGroups(new HashSet<string>(physicsGroups));
            });

            savedCameraPositionsControl = new SavedCameraPositionsControl();
            savedCameraPositionsControl.SaveCameraRequest += OnSaveCameraRequest;
            savedCameraPositionsControl.RestoreCameraRequest += OnRestoreCameraRequest;
            ViewerControl.AddControl(savedCameraPositionsControl);
        }

        private void OnRestoreCameraRequest(object sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
                    Scene.MainCamera.SetLocationPitchYaw(
                        new Vector3(savedFloats[0], savedFloats[1], savedFloats[2]),
                        savedFloats[3],
                        savedFloats[4]);
                }
            }
        }

        private void OnSaveCameraRequest(object sender, EventArgs e)
        {
            var cam = Scene.MainCamera;
            var saveName = $"Camera at {cam.Location.X:F0} {cam.Location.Y:F0} {cam.Location.Z:F0}";
            var originalName = saveName;
            var duplicateCameraIndex = 1;

            while (Settings.Config.SavedCameras.ContainsKey(saveName))
            {
                saveName = $"{originalName} (#{duplicateCameraIndex++})";
            }

            Settings.Config.SavedCameras.Add(saveName, new[] { cam.Location.X, cam.Location.Y, cam.Location.Z, cam.Pitch, cam.Yaw });
            Settings.Save();
            Settings.InvokeRefreshCamerasOnSave();
        }

        protected override void LoadScene()
        {
            ShowBaseGrid = false;

            // TODO: This method iterates over Scene.AllNodes multiple types with linq, do one big loop instead

            if (world != null)
            {
                var loader = new WorldLoader(GuiContext, world);
                var result = loader.Load(Scene);

                if (result.Skybox != null)
                {
                    SkyboxScene = new Scene(GuiContext);
                    var skyboxLoader = new WorldLoader(GuiContext, result.Skybox);
                    var skyboxResult = skyboxLoader.Load(SkyboxScene);

                    SkyboxScale = skyboxResult.SkyboxScale;
                    SkyboxOrigin = skyboxResult.SkyboxOrigin;

                    ViewerControl.AddCheckBox("Show Skybox", ShowSkybox, (v) => ShowSkybox = v);
                }

                var worldLayers = Scene.AllNodes
                    .Select(r => r.LayerName)
                    .Distinct();
                SetAvailableLayers(worldLayers);

                if (worldLayers.Any())
                {
                    foreach (var worldLayer in result.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(worldLayersComboBox.FindStringExact(worldLayer), CheckState.Checked);
                        }
                    }
                }

                if (result.CameraMatrices.Any())
                {
                    if (cameraComboBox == default)
                    {
                        cameraComboBox = ViewerControl.AddSelection("Camera", (cameraName, index) =>
                        {
                            if (index > 0)
                            {
                                if (result.CameraMatrices.TryGetValue(cameraName, out var cameraMatrix))
                                {
                                    Scene.MainCamera.SetFromTransformMatrix(cameraMatrix);
                                }
                            }
                        });

                        cameraComboBox.Items.Add("Set view to camera...");
                        cameraComboBox.SelectedIndex = 0;
                    }

                    cameraComboBox.Items.AddRange(result.CameraMatrices.Keys.ToArray<object>());
                }
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

            // Physics groups
            {
                var physGroups = Scene.AllNodes
                    .OfType<PhysSceneNode>()
                    .Select(r => r.PhysGroupName)
                    .Distinct();
                SetAvailablPhysicsGroups(physGroups);
            }

            ViewerControl.Invoke(savedCameraPositionsControl.RefreshSavedPositions);
        }

        protected override void OnPicked(object sender, PickingResponse pickingResponse)
        {
            var pixelInfo = pickingResponse.PixelInfo;

            // Void
            if (pixelInfo.ObjectId == 0)
            {
                selectedNodeRenderer.SelectNode(null);
                return;
            }

            var sceneNode = Scene.Find(pixelInfo.ObjectId);

            if (pickingResponse.Intent == PickingIntent.Select)
            {
                selectedNodeRenderer.SelectNode(sceneNode);
                return;
            }

            if (pickingResponse.Intent == PickingIntent.Details)
            {
                if (sceneNode.EntityData == null)
                {
                    return;
                }

                Dictionary<uint, string> knownKeys = null;

                using var entityDialog = new EntityInfoForm();

                foreach (var property in sceneNode.EntityData.Properties)
                {
                    var name = property.Value.Name;

                    if (name == null)
                    {
                        if (knownKeys == null)
                        {
                            knownKeys = StringToken.InvertedTable;
                        }

                        if (knownKeys.TryGetValue(property.Key, out var knownKey))
                        {
                            name = knownKey;
                        }
                        else
                        {
                            name = $"key={property.Key}";
                        }
                    }

                    var value = property.Value.Data;

                    if (value.GetType() == typeof(byte[]))
                    {
                        var tmp = value as byte[];
                        value = string.Join(' ', tmp.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray());
                    }

                    entityDialog.AddColumn(name, value.ToString());
                }

                var classname = sceneNode.EntityData.GetProperty<string>("classname");
                entityDialog.Text = $"Object: {classname}";

                entityDialog.ShowDialog();

                return;
            }

            Console.WriteLine($"Opening {sceneNode.Name} (Id: {pixelInfo.ObjectId})");

            var foundFile = GuiContext.FileLoader.FindFileWithContext(sceneNode.Name + "_c");

            if (foundFile.Context == null)
            {
                return;
            }

            Matrix4x4.Invert(sceneNode.Transform * Scene.MainCamera.CameraViewMatrix, out var transform);

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

                            if (glViewer.GLViewer is GLModelViewer glModelViewer)
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
                                if (glModelViewer.materialGroupListBox != null && worldModel.ActiveSkin != null)
                                {
                                    var skinId = glModelViewer.materialGroupListBox.FindStringExact(worldModel.ActiveSkin);

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

        private void SetAvailablPhysicsGroups(IEnumerable<string> physicsGroups)
        {
            physicsGroupsComboBox.Items.Clear();

            var physicsGroupsArray = physicsGroups.ToArray();

            if (physicsGroupsArray.Length > 0)
            {
                physicsGroupsComboBox.Enabled = true;
                physicsGroupsComboBox.Items.AddRange(physicsGroupsArray);
            }
            else
            {
                physicsGroupsComboBox.Enabled = false;
            }
        }

        private void SetEnabledPhysicsGroups(HashSet<string> physicsGroups)
        {
            foreach (var physNode in Scene.AllNodes.OfType<PhysSceneNode>())
            {
                physNode.Enabled = physicsGroups.Contains(physNode.PhysGroupName);
            }
        }
    }
}
