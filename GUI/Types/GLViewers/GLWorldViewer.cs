using System.Collections.Frozen;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor;
using ValveResourceFormat.Renderer.Editor.Collision;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Picking;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static GUI.Controls.SavedCameraPositionsControl;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// </summary>
    class GLWorldViewer : GLSceneViewer
    {
        private readonly World? world;
        private readonly WorldNode? worldNode;
        private readonly ResourceExtRefList? mapExternalReferences;
        private CheckedListBox? worldLayersComboBox;
        private GLViewerTreeSelectionControl? collisionTree;
        private ComboBox? cameraComboBox;
        private SavedCameraPositionsControl? savedCameraPositionsControl;
        private EntityInfoForm? entityInfoForm;
        private ThemedButton? playPauseButton;
        private ThemedButton? stopButton;
        private System.Drawing.Image? playImage;
        private System.Drawing.Image? pauseImage;
        private System.Drawing.Image? stopImage;
        private ThemedToggleButton? toolsButton;
        private SimulationState simulationState;

        // Whether tools were shown before playing hid them, which stopping shows again
        private bool showToolsBeforePlay;
        private int simulationRequest;
        private bool ignoreLayersChangeEvents = true;
        private string[] unlistedEnabledLayers = [];
        private List<Matrix4x4> CameraMatrices = [];
        private WorldNodeLoader? LoadedWorldNode;
        public WorldLoader? LoadedWorld;
        private EntityLump.Entity? entityInfoEntity;

        internal enum SimulationState
        {
            /// <summary>The world rests as it loaded.</summary>
            Stopped,

            /// <summary>Entity logic runs.</summary>
            Playing,

            /// <summary>Entity logic is held where it was.</summary>
            Paused,
        }

        internal enum SimulationRequest
        {
            None,
            Play,
            Pause,

            /// <summary>Pause while playing, otherwise play.</summary>
            PlayOrPause,
            Stop,
        }

        /// <summary>Jump from the entity info popup to the entity's node in the I/O graph tab, when the map has one.</summary>
        public Func<EntityLump.Entity, bool>? ShowEntityInGraph { get; set; }

        /// <summary>Whether the I/O graph tab has a node for an entity. Set together with <see cref="ShowEntityInGraph"/>.</summary>
        public Func<EntityLump.Entity, bool>? EntityHasGraphNode { get; set; }

        public GLWorldViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, World world, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext)
        {
            this.world = world;
            mapExternalReferences = externalReferences;
        }

        public GLWorldViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, WorldNode worldNode, ResourceExtRefList? externalReferences = null)
            : base(vrfGuiContext, rendererContext)
        {
            this.worldNode = worldNode;
            mapExternalReferences = externalReferences;
        }

        public override void Dispose()
        {
            base.Dispose();

            worldLayersComboBox?.Dispose();
            collisionTree?.Dispose();
            cameraComboBox?.Dispose();
            savedCameraPositionsControl?.Dispose();
            entityInfoForm?.Dispose();
            playPauseButton?.Dispose();
            stopButton?.Dispose();
            playImage?.Dispose();
            pauseImage?.Dispose();
            stopImage?.Dispose();
            toolsButton?.Dispose();
        }

        private void AddSceneExposureSlider()
        {
            Debug.Assert(UiControl != null);

            var exposureLabel = new Label();
            void UpdateExposureText(float exposure)
            {
                exposureLabel.Text = $"Exposure: {exposure:0.00}";
            }

            UiControl.AddControl(exposureLabel);

            var exposureSlider = UiControl.AddTrackBar("Exposure", (exposureAmount) =>
            {
                var exposure = exposureAmount * 10;
                UpdateExposureText(exposure);
                Renderer.Postprocess.CustomExposure = exposure;

                // also set auto exposure to off for debugging, in case this is slowing things down
                Renderer.Postprocess.State = Renderer.Postprocess.State with { ExposureSettings = Renderer.Postprocess.State.ExposureSettings with { AutoExposureEnabled = false } };
            });

            var sceneExposure = Renderer.Postprocess.CurrentExposure;
            exposureSlider.Slider.Value = sceneExposure / 10;
            UpdateExposureText(sceneExposure);
        }

        private void OnGetOrSetPositionFromClipboardRequest(object? sender, bool isSetRequest)
        {
            if (!isSetRequest)
            {
                var loc = Renderer.Camera.Location;
                var cameraAngles = Renderer.Camera.GetQAngle();

                AppClipboard.SetText(string.Create(CultureInfo.InvariantCulture, $"setpos {loc.X:F6} {loc.Y:F6} {loc.Z:F6}; setang {cameraAngles.X:F6} {cameraAngles.Y:F6} {cameraAngles.Z:F6}"));

                return;
            }

            var text = AppClipboard.GetText();
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

            var viewAngles = ang.Success
                ? new Vector3(
                    float.Parse(ang.Groups["pitch"].Value, CultureInfo.InvariantCulture),
                    float.Parse(ang.Groups["yaw"].Value, CultureInfo.InvariantCulture),
                    0f)
                : Vector3.Zero;

            Input.SaveCameraForTransition(exitWalkMode: false);
            Input.Camera.SetLocation(new Vector3(x, y, z));
            Input.Camera.SetFromQAngle(viewAngles);
        }

        private void OnRestoreCameraRequest(object? sender, RestoreCameraRequestEvent e)
        {
            if (Settings.Config.SavedCameras.TryGetValue(e.Camera, out var savedFloats))
            {
                if (savedFloats.Length == 5)
                {
                    Input.SaveCameraForTransition();

                    // Saved cameras predate the camera holding pitch the engine's way round, and are
                    // stored positive upwards, so already saved ones keep pointing where they did.
                    Input.Camera.SetLocationPitchYaw(
                        new Vector3(savedFloats[0], savedFloats[1], savedFloats[2]),
                        -savedFloats[3],
                        savedFloats[4]);
                }
            }
        }

        private void OnSaveCameraRequest(object? sender, EventArgs e)
        {
            var cam = Renderer.Camera;
            var saveName = $"Camera at {cam.Location.X:F0} {cam.Location.Y:F0} {cam.Location.Z:F0}";
            var originalName = saveName;
            var duplicateCameraIndex = 1;

            while (Settings.Config.SavedCameras.ContainsKey(saveName))
            {
                saveName = $"{originalName} (#{duplicateCameraIndex++})";
            }

            Settings.Config.SavedCameras.Add(saveName, [cam.Location.X, cam.Location.Y, cam.Location.Z, -cam.Pitch, cam.Yaw]);
            Settings.InvokeRefreshCamerasOnSave();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();

            if (worldNode != null)
            {
                base.LoadDefaultLighting();

                // The default cubemap looks wrong on maps, so they keep the flat reflection
                foreach (var envMap in Scene.LightingInfo.EnvMaps)
                {
                    envMap.EnvMapTexture.Delete();
                }

                Scene.LightingInfo.EnvMaps.Clear();
                Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = false;
                Scene.LightingInfo.SunLightShadowCoverageScale = 2f;

                var post = new ScenePostProcessVolume(Scene)
                {
                    HasBloom = true,
                    IsMaster = true,
                };

                Scene.PostProcessInfo.AddPostProcessVolume(post);
            }
        }

        protected override void LoadScene()
        {
            var cameraSet = false;

            InitializeSoundPlayer();

            if (world != null)
            {
                Renderer.EntitySystem.Enabled = false;

                LoadedWorld = new WorldLoader(world, Scene, Renderer.EntitySystem)
                {
                    LoadingProgress = GuiContext.LoadingProgress,
                    DefaultEnabledLayers = { HammerEntityVisuals.MarkerLayerName },
                };

                LoadedWorld.Load(mapExternalReferences);

                NavMeshSceneNode.AddNavNodesToScene(LoadedWorld.NavMesh, Scene, LoadedWorld.NavigationHullNames);
                NavSpaceSceneNode.AddToScene(LoadedWorld.NavSpace, Scene);
                NavMeshSceneNode.AddFlowMapNodesToScene(LoadedWorld.NavFlowMap, LoadedWorld.NavMesh, Scene, LoadedWorld.NavigationHullNames);
                CS2BombDamageSceneNode.AddBakedBombDamageToScene(LoadedWorld.BombDamage, Scene);

                // Prewarm draws before the sidebar picks the layers, so it starts from the same defaults
                SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);

                if (LoadedWorld.CameraMatrices.Count > 0)
                {
                    CameraMatrices = LoadedWorld.CameraMatrices;
                }

                if (LoadedWorld.SpawnCameraMatrix is { } spawn)
                {
                    Input.Camera.SetFromTransformMatrix(spawn);
                    cameraSet = true;
                }

                ReportLoadingStatus("Loading player model…");

                Input.TryLoadViewmodel(Scene, Renderer.EntitySystem);

                var kzMapPrefixes = new[] { "bhop", "surf", "kz", "dr" };
                var mapName = Path.GetFileName(LoadedWorld.MapName);
                var isKzMap = kzMapPrefixes.Any(prefix => mapName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                ShowSpeed = isKzMap;
                Input.PlayerMovement.AutoBunnyHop = isKzMap;

                var gameInfo = Renderer.RendererContext.FileLoader.GameInfo;
                Input.PlayerMovement.CompactHull = gameInfo.Game is Game.Hlvr or Game.Steamtours;
                Input.PlayerMovement.AirAccelerate = isKzMap
                    ? PlayerMovement.AirAccelerateMovementMaps
                    : PlayerMovement.AirAccelerateCompetitive;

                Input.EntitySystem = Renderer.EntitySystem;
                Renderer.EntitySystem.SpawnPlayer(Input.PlayerMovement, Scene);
            }

            if (!cameraSet)
            {
                Input.Camera.SetLocation(new Vector3(256));
                Input.Camera.LookAt(Vector3.Zero);
            }

            if (worldNode != null)
            {
                ReportLoadingStatus("Loading world geometry…");

                LoadedWorldNode = new WorldNodeLoader(Scene.RendererContext, worldNode, mapExternalReferences);
                LoadedWorldNode.Load(Scene);
            }
        }

        protected override bool PrewarmsRenderer => true;

        protected override void OnFirstPaint()
        {
            Input.MoveCamera(new Vector3(0, -150f, 0));

            base.OnFirstPaint();

            Input.MoveCamera(new Vector3(0, 150f, 0), transition: true);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();
            }

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                if (ignoreLayersChangeEvents)
                {
                    return;
                }

                SetEnabledLayers([.. worldLayers, .. unlistedEnabledLayers]);
            });
            AddToolBrushGroupsControl();
            AddCollisionControl();

            using (UiControl.BeginGroup("Camera"))
            {
                savedCameraPositionsControl = new SavedCameraPositionsControl();
                savedCameraPositionsControl.SaveCameraRequest += OnSaveCameraRequest;
                savedCameraPositionsControl.RestoreCameraRequest += OnRestoreCameraRequest;
                savedCameraPositionsControl.GetOrSetPositionFromClipboardRequest += OnGetOrSetPositionFromClipboardRequest;
                UiControl.AddControl(savedCameraPositionsControl);

                if (LoadedWorld != null && LoadedWorld.CameraMatrices.Count > 0)
                {
                    cameraComboBox = UiControl.AddSelection("Map Camera", (cameraName, index) =>
                    {
                        if (index > 0)
                        {
                            Input.SaveCameraForTransition();
                            Input.Camera.SetFromTransformMatrix(CameraMatrices[index - 1]);
                        }
                    });
                    cameraComboBox.BeginUpdate();
                    cameraComboBox.Items.Add("Set view to camera…");
                    cameraComboBox.Items.AddRange([.. LoadedWorld.CameraNames]);
                    cameraComboBox.SelectedIndex = 0;
                    cameraComboBox.EndUpdate();
                }
            }

            if (HasToolsVisibilityControls)
            {
                AddToolsVisibilityToolbar();
            }

            if (world != null)
            {
                AddSimulationToolbar();

                var uniqueWorldLayers = new HashSet<string>(4);

                foreach (var node in Scene.AllNodes)
                {
                    if (node.LayerName != null && Scene.IsInternalLayer(node.LayerName))
                    {
                        continue;
                    }

                    if (node.LayerName != null)
                    {
                        uniqueWorldLayers.Add(node.LayerName);
                    }
                }

                if (uniqueWorldLayers.Count > 0 && LoadedWorld != null)
                {
                    Debug.Assert(worldLayersComboBox != null);

                    worldLayersComboBox.BeginUpdate();

                    // Showing helpers also decides what a selected entity shows, so it can be chosen even while nothing
                    // unselected needs it
                    var listedLayers = uniqueWorldLayers.Append(HammerEntityVisuals.HelpersLayerName).Distinct().ToArray();
                    SetAvailableLayers(listedLayers);

                    // Layers that have no nodes yet, such as particles spawned later, are not listed and stay enabled
                    unlistedEnabledLayers = [.. LoadedWorld.DefaultEnabledLayers.Where(layer => !listedLayers.Contains(layer))];

                    foreach (var worldLayer in LoadedWorld.DefaultEnabledLayers)
                    {
                        var checkboxIndex = worldLayersComboBox.FindStringExact(worldLayer);

                        if (checkboxIndex > -1)
                        {
                            worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                        }
                    }

                    worldLayersComboBox.EndUpdate();

                    SetEnabledLayers(LoadedWorld.DefaultEnabledLayers);
                }

                using (UiControl.BeginGroup("World"))
                {
                    if (Renderer.HasSky)
                    {
                        UiControl.AddCheckBox("Show Skybox", Renderer.Settings.ShowSkybox, (v) => Renderer.Settings.ShowSkybox = v);
                    }

                    UiControl.AddCheckBox("Show Fog", Renderer.Settings.FogEnabled, v => Renderer.Settings.FogEnabled = v);
                    UiControl.AddCheckBox("Show Volumetric Fog", Renderer.Settings.VolumetricFogEnabled, v => Renderer.Settings.VolumetricFogEnabled = v);

                    UiControl.AddCheckBox("Color Correction", Renderer.Postprocess.ColorCorrectionEnabled, v => Renderer.Postprocess.ColorCorrectionEnabled = v);

                    if (Scene.VoxelVisibility != null)
                    {
                        UiControl.AddCheckBox("PVS Culling", Renderer.Settings.EnablePvsCulling, v => Renderer.Settings.EnablePvsCulling = v);
                    }

                    if (Renderer.Scenes.Any(static scene => scene.VisibilityBoxes.Count > 0))
                    {
                        UiControl.AddCheckBox("Visibility Boxes", Renderer.Settings.EnableVisBoxCulling, v => Renderer.Settings.EnableVisBoxCulling = v);
                    }

                    CheckBox? occlusionCullingCheckBox = null;

                    if (GLEnvironment.SlowMultiDrawIndirect)
                    {
                        Renderer.Settings.EnableIndirectDraws = false;
                    }

                    UiControl.AddCheckBox("GPU Culling", Renderer.Settings.EnableIndirectDraws, v =>
                    {
                        Renderer.Settings.EnableIndirectDraws = v;

                        if (occlusionCullingCheckBox != null)
                        {
                            occlusionCullingCheckBox.Enabled = v;
                        }
                    });

                    occlusionCullingCheckBox = UiControl.AddCheckBox("GPU Occlusion Culling", Renderer.Settings.EnableOcclusionCulling, (v) => Renderer.Settings.EnableOcclusionCulling = v);
                    occlusionCullingCheckBox.Enabled = Renderer.Settings.EnableIndirectDraws;

                    UiControl.AddCheckBox("Depth Prepass", Renderer.Settings.EnableDepthPrepass, (v) => Renderer.Settings.EnableDepthPrepass = v);

                    UiControl.AddCheckBox("Barn Lights", Renderer.Settings.EnableBarnLights, v => Renderer.Settings.EnableBarnLights = v);
                    UiControl.AddCheckBox("Tiled Light Culling", Renderer.Settings.EnableTiledLightCulling, v => Renderer.Settings.EnableTiledLightCulling = v);

                    AddSceneExposureSlider();
                }
            }

            if (worldNode != null && worldLayersComboBox != null)
            {
                var worldLayers = Scene.AllNodes
                    .Select(static r => r.LayerName)
                    .OfType<string>()
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

            base.AddUiControls();

            if (world != null)
            {
                AddDOFControls();
            }
        }

        public void AddDOFControls()
        {
            Debug.Assert(UiControl != null);

            var groupBoxPanel = new Panel
            {
                Height = UiControl.AdjustForDPI(230),
                Padding = new(0, 2, 0, 2),
            };

            var groupBox = new ThemedGroupBox
            {
                Text = "Depth Of Field",
                Dock = DockStyle.Fill,
                Padding = new(4, 8, 4, 4),
            };

            var controlsContainer = new Panel
            {
                Height = UiControl.AdjustForDPI(175),
                Dock = DockStyle.Top
            };

            void dofCheckBoxAction(bool v)
            {
                Renderer.Postprocess.DOF.Enabled = v;

                controlsContainer.Enabled = v;
                controlsContainer.Visible = v;

                var height = v ? 230 : 60;
                groupBoxPanel.Height = UiControl.AdjustForDPI(height);
            }

            dofCheckBoxAction(Renderer.Postprocess.DOF.Enabled);

            var checkBox = RendererControl.CreateCheckBox("Enabled", Renderer.Postprocess.DOF.Enabled, dofCheckBoxAction);
            checkBox.Dock = DockStyle.Top;

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Far blurry", val =>
            {
                Renderer.Postprocess.DOF.FarBlurry = val;
            }, Renderer.Postprocess.DOF.FarBlurry, 0, 10000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Far crisp", val =>
            {
                Renderer.Postprocess.DOF.FarCrisp = val;
            }, Renderer.Postprocess.DOF.FarCrisp, 0, 10000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Near blurry", val =>
            {
                Renderer.Postprocess.DOF.NearBlurry = -val;
            }, -Renderer.Postprocess.DOF.NearBlurry, 0, 100));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Near crisp", val =>
            {
                Renderer.Postprocess.DOF.NearCrisp = val;
            }, Renderer.Postprocess.DOF.NearCrisp, 0, 1000));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Max Blur Size", val =>
            {
                Renderer.Postprocess.DOF.MaxBlurSize = val;
            }, Renderer.Postprocess.DOF.MaxBlurSize, 0, 100));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Radius Scale", val =>
            {
                Renderer.Postprocess.DOF.RadScale = val;
            }, Renderer.Postprocess.DOF.RadScale, 0, 1));

            controlsContainer.Controls.Add(RendererControl.CreateFloatInput("Focal Distance", val =>
            {
                Renderer.Postprocess.DOF.FocalDistance = val;
            }, Renderer.Postprocess.DOF.FocalDistance, 0, 10000));

            foreach (Control control in controlsContainer.Controls)
            {
                control.Dock = DockStyle.Top;
                control.Margin = new Padding(4);
            }

            groupBox.Controls.Add(controlsContainer);
            groupBox.Controls.Add(checkBox);
            groupBoxPanel.Controls.Add(groupBox);

            UiControl.AddControl(groupBoxPanel);
        }

        public void SelectAndFocusEntity(EntityLump.Entity entity)
        {
            if (UiControl != null && UiControl.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            var node = Renderer.FindNode(entity);

            if (node == null)
            {
                // Tool entities (logic, sounds, finished particles) have no renderable
                // scene node; fly to the entity origin instead.
                FocusCameraOnBounds(EntityOriginBounds(entity));
                return;
            }

            SelectAndFocusNode(node);
        }

        public void SelectAndFocusEntities(IReadOnlyList<EntityLump.Entity> entities)
        {
            if (entities.Count == 1)
            {
                SelectAndFocusEntity(entities[0]);
                return;
            }

            if (UiControl != null && UiControl.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            var hasBounds = false;
            var bounds = default(AABB);
            var selectedAny = false;

            foreach (var entity in entities)
            {
                var node = Renderer.FindNode(entity);

                AABB entityBounds;

                if (node != null)
                {
                    if (selectedAny)
                    {
                        Selection.Toggle(node);
                    }
                    else
                    {
                        Selection.Select(node);
                        selectedAny = true;
                    }

                    EnsureNodeVisible(node);
                    entityBounds = SelectionBounds(node);
                }
                else
                {
                    entityBounds = EntityOriginBounds(entity);
                }

                bounds = hasBounds ? bounds.Union(entityBounds) : entityBounds;
                hasBounds = true;
            }

            if (hasBounds)
            {
                FocusCameraOnBounds(bounds);
            }
        }

        internal void SelectAndFocusNode(SceneNode node)
        {
            ArgumentNullException.ThrowIfNull(node);

            Selection.Select(node);
            FocusCameraOnBounds(SelectionBounds(node));
            EnsureNodeVisible(node);
        }

        /// <summary>Bounds to focus on for an entity that has no scene node, at its origin.</summary>
        internal AABB EntityOriginBounds(EntityLump.Entity entity)
        {
            // Try to find the entity in the system and fall back to its lump location otherwise
            var spawned = Renderer.EntitySystem.Entities.FirstOrDefault(candidate => !candidate.IsRemoved && candidate.Data == entity);

            if (spawned != null)
            {
                return PointBounds(Vector3.Transform(spawned.WorldOrigin, spawned.Scene.ToViewerWorld));
            }

            var origin = entity.GetVector3Property("origin");

            if (OwnSceneSpawnGroups.FirstOrDefault(group => group.Entities.Contains(entity)) is { } spawnGroup)
            {
                origin = spawnGroup.EntityOriginToWorld(origin);
            }

            return PointBounds(origin);
        }

        private void EnsureNodeVisible(SceneNode node)
        {
            if (node.LayerName != null && ToolsVisibility.IsToolEntityLayer(node.LayerName) && !Tools.ShowTools)
            {
                Tools.ShowTools = true;
                toolsButton?.Checked = true;
                RequestVisibilityUpdate();
            }

            if (!node.LayerEnabled && worldLayersComboBox != null && node.LayerName != null)
            {
                var layerId = worldLayersComboBox.Items.IndexOf(node.LayerName);

                if (layerId >= 0)
                {
                    worldLayersComboBox.SetItemChecked(layerId, true);
                }
            }

            if (node is PhysSceneNode { Enabled: false } collision)
            {
                collisionTree?.Check(collision.CollisionLabels[0]);
            }
        }

        private void ShowSceneNodeDetails(SceneNode sceneNode)
        {
            if (sceneNode.EntityData != null)
            {
                ShowEntityDetails(sceneNode.EntityData);
                return;
            }

            var form = PrepareEntityInfoForm(null);
            var info = form.EntityInfoControl;

            form.Text = $"{sceneNode.GetType().Name}: {sceneNode.Name}";

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
                info.AddProperty("Shader", material.ShaderName);
                info.AddProperty("Material", material.Name, new LinkedFile(material.Name));
                info.AddProperty("Aggregate Model", sceneFragment.Name!, new LinkedFile(sceneFragment.Name!));

                var tris = sceneFragment.DrawCall.IndexCount / 3;
                if (sceneFragment.DrawCall.NumMeshlets > 0)
                {
                    var clusters = sceneFragment.DrawCall.NumMeshlets;
                    var trisPerCluster = tris / clusters;
                    info.AddProperty("Triangles / Clusters / Per Cluster", $"{tris} / {clusters} / {trisPerCluster}");
                }
                else
                {
                    info.AddProperty("Triangles", $"{tris}");
                }

                info.AddProperty("Model Tint", ToRenderColor(sceneFragment.DrawCall.TintColor));
                info.AddProperty("Model Alpha", $"{sceneFragment.DrawCall.TintColor.W:F6}");

                if (sceneFragment.TintAlpha != Vector4.One)
                {
                    info.AddProperty("Instance Tint", ToRenderColor(sceneFragment.TintAlpha));
                    info.AddProperty("Final Tint", ToRenderColor(sceneFragment.DrawCall.TintColor * sceneFragment.TintAlpha));
                }
            }
            else if (sceneNode is SceneClutter clutter)
            {
                var drawCalls = clutter.RenderMesh.DrawCallsOpaque;
                var tris = drawCalls.Sum(static drawCall => drawCall.IndexCount / 3);

                info.AddProperty("Clutter Model", clutter.Name!, new LinkedFile(clutter.Name!));

                foreach (var drawCall in drawCalls)
                {
                    var material = drawCall.Material.Material;
                    info.AddProperty("Shader", material.ShaderName);
                    info.AddProperty("Material", material.Name, new LinkedFile(material.Name));
                }

                info.AddProperty("Instances", $"{clutter.InstanceTransforms.Count}");
                info.AddProperty("Triangles", $"{(long)tris * clutter.InstanceTransforms.Count}");
                info.AddProperty("Triangles Per Instance", $"{tris}");
                info.AddProperty("Instance Tint", ToRenderColor(clutter.TintAlpha));
                info.AddProperty("Begin Cull Size", $"{clutter.BeginCullSize:G6}");
                info.AddProperty("End Cull Size", $"{clutter.EndCullSize:G6}");
            }
            else if (sceneNode is ModelSceneNode modelSceneNode)
            {
                info.AddProperty("Model", modelSceneNode.Name!, new LinkedFile(modelSceneNode.Name!));
                info.AddProperty("Model Tint", ToRenderColor(modelSceneNode.TintAlpha));
                info.AddProperty("Model Alpha", $"{modelSceneNode.Alpha:F6}");

                if (modelSceneNode.LightingOrigin.HasValue)
                {
                    info.AddProperty("Custom Lighting Origin", FormatVector(modelSceneNode.LightingOrigin.Value));
                }
            }
            else if (sceneNode is PhysSceneNode collision)
            {
                PhysicsDetails.AddNodeDetails(info, collision);
            }

            if (sceneNode.CubeMapPrecomputedHandshake > 0)
            {
                info.AddProperty("Cubemap Handshake", $"{sceneNode.CubeMapPrecomputedHandshake}");
            }

            if (sceneNode.LightProbeVolumePrecomputedHandshake > 0)
            {
                info.AddProperty("Light Probe Handshake", $"{sceneNode.LightProbeVolumePrecomputedHandshake}");
            }

            info.AddProperty("Flags", sceneNode.Flags.ToString());
            info.AddProperty("Layer", sceneNode.LayerName ?? string.Empty);

            if (OwnSceneSpawnGroups.FirstOrDefault(group => group.Scene == sceneNode.Scene) is { } spawnGroup)
            {
                form.Text += SpawnGroupSuffix(spawnGroup);
            }

            info.ShowPopulatedTabs();
        }

        private void ShowEntityDetails(EntityLump.Entity entity)
        {
            var form = PrepareEntityInfoForm(entity);

            form.EntityInfoControl.PopulateFromEntity(GetLinkableEntities(), entity);

            var modelEntity = Renderer.EntitySystem.Entities.OfType<BaseModelEntity>().FirstOrDefault(candidate => candidate.Data == entity);

            if (modelEntity?.Physics != null)
            {
                PhysicsDetails.AddEntityDetails(form.EntityInfoControl, modelEntity, modelEntity.Physics);
            }

            var classname = entity.GetStringProperty("classname");
            var targetName = entity.FriendlyTargetName;
            form.Text = string.IsNullOrEmpty(targetName)
                ? $"Entity: {classname}"
                : $"Entity: {classname} ({targetName})";

            if (OwnSceneSpawnGroups.FirstOrDefault(group => group.Entities.Contains(entity)) is { } spawnGroup)
            {
                form.Text += SpawnGroupSuffix(spawnGroup);
            }

            form.EntityInfoControl.ShowPopulatedTabs();
        }

        // Drawn as scenes of their own, such as the 3D sky, with entities the map does not list
        internal IEnumerable<SpawnGroup> OwnSceneSpawnGroups
            => Renderer.EntitySystem.SpawnGroupManager.Groups.Where(static group => group.HasOwnScene);

        private static string SpawnGroupSuffix(SpawnGroup spawnGroup)
            => spawnGroup.WorldGroup != null ? " (in 3D skybox)" : $" (in {spawnGroup.MapName})";

        /// <summary>
        /// Opens the details window if it is not open yet, and empties it for <paramref name="entity"/>,
        /// or for a scene node that is not an entity when it is null.
        /// </summary>
        private EntityInfoForm PrepareEntityInfoForm(EntityLump.Entity? entity)
        {
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm();
                entityInfoForm.EntityInfoControl.LinkedResourceActivated += OnLinkedResourceActivated;
                entityInfoForm.EntityInfoControl.ConnectionFireRequested += OnConnectionFireRequested;
                entityInfoForm.EntityInfoControl.CanFireConnections = true;

                if (ShowEntityInGraph != null)
                {
                    entityInfoForm.AddShowInGraphButton(OnShowInGraphButtonClick);
                }

                entityInfoForm.FormClosed += OnEntityInfoFormClosed;
                entityInfoForm.Activated += RenderLoopThread.OnAppActivated;
                entityInfoForm.Show();
            }

            entityInfoEntity = entity;

            if (entityInfoForm.ShowInGraphButton != null)
            {
                entityInfoForm.ShowInGraphButton.Visible = entity != null && (EntityHasGraphNode?.Invoke(entity) ?? false);
            }

            entityInfoForm.EntityInfoControl.Clear();

            return entityInfoForm;
        }

        private List<EntityLump.Entity> GetLinkableEntities()
        {
            if (LoadedWorld == null)
            {
                return [];
            }

            return [.. LoadedWorld.Entities, .. OwnSceneSpawnGroups.SelectMany(static group => group.Entities)];
        }

        private void OnLinkedResourceActivated(object? sender, LinkedResource link)
        {
            switch (link)
            {
                case LinkedEntities { Entities: [var entity] }:
                    SelectAndFocusEntity(entity);
                    ShowEntityDetails(entity);
                    break;

                case LinkedEntities { Entities: var entities }:
                    SelectAndFocusEntities(entities);
                    break;

                case LinkedFile { Path: var path }:
                    if (Viewers.Resource.OpenExternalReference(GuiContext, path))
                    {
                        entityInfoForm?.Close();
                    }
                    break;
            }
        }

        private void OnConnectionFireRequested(object? sender, EntityLump.Connection connection)
        {
            using var lockedGl = MakeCurrent();
            Renderer.EntitySystem.QueueConnection(connection, Renderer.EntitySystem.Player);
        }

        private void OnShowInGraphButtonClick(object? sender, EventArgs e)
        {
            if (entityInfoEntity != null)
            {
                ShowEntityInGraph?.Invoke(entityInfoEntity);
            }
        }

        private void OnEntityInfoFormClosed(object? sender, FormClosedEventArgs e)
        {
            entityInfoForm = null;
            entityInfoEntity = null;
        }

        protected override void OnPicked(PickResult result)
        {
            var pixelInfo = result.Pixel;

            // Void
            if (result.HitNothing)
            {
                Selection.Clear();
                return;
            }

            var sceneNode = Renderer.FindPickedNode(result);

            if (sceneNode == null)
            {
                return;
            }

            if (result.Intent == PickIntent.Select)
            {
                if (result.Has(PickModifiers.Control))
                {
                    Selection.Toggle(sceneNode);
                }
                else
                {
                    Selection.Select(sceneNode);
                }

                //Update the entity properties window if it was opened
                if (entityInfoForm != null)
                {
                    Program.MainForm.Invoke(() =>
                    {
                        ShowSceneNodeDetails(sceneNode);
                    });
                }
                return;
            }

            if (result.Intent == PickIntent.Details)
            {
                Program.MainForm.Invoke(() =>
                {
                    ShowSceneNodeDetails(sceneNode);

                    if (entityInfoForm == null)
                    {
                        return;
                    }

                    if (entityInfoForm.WindowState == FormWindowState.Minimized)
                    {
                        entityInfoForm.WindowState = FormWindowState.Normal;
                    }

                    entityInfoForm.EntityInfoControl.Focus();
                });
                return;
            }

            Log.Info(nameof(GLWorldViewer), $"Opening {sceneNode.Name} (Id: {pixelInfo.ObjectId})");

            var filename = sceneNode.Name;

            if (sceneNode.EntityData != null)
            {
                // Perhaps this needs to check for correct classname?
                var particle = sceneNode.EntityData.GetStringProperty("effect_name");

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

            if (!Matrix4x4.Invert(sceneNode.Transform * Renderer.Camera.CameraViewMatrix, out var transform))
            {
                throw new InvalidOperationException("Matrix invert failed");
            }

            FullScreenForm?.Close();

            foundFile.Context.GLPostLoadAction = (viewerControl) =>
            {
                // The inverse of a view matrix, so its third row is the camera's backward direction
                var forward = -transform.GetRow(2).AsVector3();

                if (viewerControl is GLSceneViewer sceneViewer && sceneNode is not SceneAggregate { InstanceTransforms.Count: > 0 })
                {
                    sceneViewer.Input.Camera.CopyFrom(Renderer.Camera);
                    sceneViewer.Input.Camera.SetLocation(transform.Translation);
                    sceneViewer.Input.Camera.SetFromQAngle(EntityTransformHelper.ForwardDirectionToEulerAngles(forward));
                }

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

            Program.MainForm.Invoke(() =>
            {
                Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
            });
        }

        private void SetAvailableLayers(IEnumerable<string> worldLayers)
        {
            Debug.Assert(worldLayersComboBox != null);

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

        // Hammer's Auto Vis Groups of tools materials, those the loaded map uses nested under the groups above them,
        // shown until unchecked and folded until opened. A group the brushes of several entity classes use lists
        // each class below it.
        private void AddToolBrushGroupsControl()
        {
            Debug.Assert(UiControl != null);

            var nodes = Renderer.Scenes.SelectMany(static scene => scene.AllNodes).ToArray();
            var meshDraws = nodes
                .OfType<MeshCollectionNode>()
                .SelectMany(static node => node.RenderableMeshes
                    .SelectMany(static mesh => mesh.DrawCalls)
                    .Select(drawCall => (Node: (SceneNode)node, drawCall.Material)));
            var shapeDraws = nodes
                .OfType<ShapeSceneNode>()
                .Where(static node => node.ToolsMaterial != null)
                .Select(static node => (Node: (SceneNode)node, Material: node.ToolsMaterial!));

            var classesByGroup = meshDraws
                .Concat(shapeDraws)
                .Select(static draw => (Group: ToolBrushGroups.GetGroup(draw.Material.Material.Name), EntityClass: ToolBrushGroups.GetEntityClass(draw.Node)))
                .Where(static draw => draw.Group != null)
                .GroupBy(static draw => draw.Group!, static draw => draw.EntityClass, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Distinct(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

            if (classesByGroup.Count == 0)
            {
                return;
            }

            // What each item hides, where a group listing its classes hides nothing of its own
            var items = new Dictionary<string, (string Group, string? EntityClass)>(StringComparer.Ordinal);
            var paths = new List<string>();

            foreach (var (group, entityClasses) in classesByGroup)
            {
                foreach (var parent in ToolBrushGroups.GetGroupAndParents(group).Skip(1))
                {
                    items.TryAdd(parent, (parent, null));
                }

                if (entityClasses is [_])
                {
                    items.Add(group, (group, null));
                    paths.Add(group);
                    continue;
                }

                foreach (var entityClass in entityClasses)
                {
                    var path = $"{group}/{entityClass}";
                    items.Add(path, (group, entityClass));
                    paths.Add(path);
                }
            }

            paths.Sort(StringComparer.Ordinal);

            UiControl.AddTreeSelection("Auto Vis Groups", paths, shown => Tools.HiddenToolBrushGroups = items
                .ExceptBy(shown, static item => item.Key)
                .Select(static item => item.Value)
                .ToFrozenSet(), foldGroups: true);
        }

        // What the loaded map collides as, hidden until checked and folded until opened, and whether it and tools materials are drawn opaque
        private void AddCollisionControl()
        {
            Debug.Assert(UiControl != null);

            var labels = ToolsVisibility.GetCollisionLabels(Scene.AllNodes);

            if (labels.Length > 0)
            {
                collisionTree = UiControl.AddTreeSelection("Collision", labels, shown =>
                {
                    Tools.ShownCollision = shown.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
                    RequestVisibilityUpdate();
                }, initiallyChecked: false, foldGroups: true);
            }
            else if (!Scene.AllNodes.Any(static node => node is ShapeSceneNode { ToolsMaterial: not null }))
            {
                return;
            }

            UiControl.AddCheckBox("Opaque Collision", Tools.OpaqueCollision, v =>
            {
                Tools.OpaqueCollision = v;
                RequestVisibilityUpdate();
            });
        }

        protected override bool HasToolsVisibilityControls => world != null;

        protected override HashSet<string> FilterLayers(HashSet<string> layers)
        {
            if (!HasToolsVisibilityControls)
            {
                return layers;
            }

            var filtered = Tools.FilterLayers(layers);

            // While playing the game hides disabled entities itself, so they come and go with the other entities
            if (simulationState != SimulationState.Stopped && filtered.Contains("Entities"))
            {
                filtered.Add(WorldLoader.DisabledEntitiesLayerName);
            }

            return filtered;
        }

        protected override void ApplyVisibility()
        {
            base.ApplyVisibility();

            Tools.ApplyCollisionVisibility(Scene.AllNodes);
        }

        // Above the viewport rather than in the sidebar: what of the tools is drawn
        private void AddToolsVisibilityToolbar()
        {
            Debug.Assert(UiControl != null);

            toolsButton = UiControl.AddToolbarToggle("Show Tools", Tools.ShowTools, v =>
            {
                Tools.ShowTools = v;
                RequestVisibilityUpdate();
            });

            RequestVisibilityUpdate();
        }

        // Above the viewport rather than in the sidebar: how the world is run
        private void AddSimulationToolbar()
        {
            Debug.Assert(UiControl != null);

            // The image list hands out a copy each time it is asked
            playImage = AppIcons.ImageList.Images[AppIcons.Icons["RendererPlay"]];
            pauseImage = AppIcons.ImageList.Images[AppIcons.Icons["RendererPause"]];
            stopImage = AppIcons.ImageList.Images[AppIcons.Icons["RendererStop"]];

            playPauseButton = UiControl.AddToolbarButton(playImage, "Play", () => RequestSimulation(SimulationRequest.PlayOrPause), centered: true);
            stopButton = UiControl.AddToolbarButton(stopImage, "Stop", () => RequestSimulation(SimulationRequest.Stop), centered: true);

            UpdateSimulationButtons(simulationState);
        }

        private void RequestSimulation(SimulationRequest request)
        {
            Interlocked.Exchange(ref simulationRequest, (int)request);

            // Hands the keyboard back to the viewport, which the click took, so the player can be walked
            GLControl?.Focus();
        }

        /// <summary>Gets whether the world is stopped, playing or paused, as the next frame left it.</summary>
        internal SimulationState Simulation => simulationState;

        /// <summary>
        /// Queues a simulation change for the next frame without taking the keyboard, and leaves the cursor
        /// free in walk mode, as after escape, until the viewport is clicked.
        /// </summary>
        internal void QueueSimulation(SimulationRequest request)
        {
            MouseReleased = true;
            Interlocked.Exchange(ref simulationRequest, (int)request);
        }

        private void UpdateSimulationButtons(SimulationState state)
        {
            var playing = state == SimulationState.Playing;

            toolsButton?.Checked = Tools.ShowTools;

            playPauseButton?.Image = playing ? pauseImage : playImage;
            playPauseButton?.AccessibleName = playing ? "Pause" : "Play";
            stopButton?.Enabled = state != SimulationState.Stopped;
        }

        protected override (string Text, Color32 Color)? ModeLabel => simulationState switch
        {
            SimulationState.Playing => ("PLAY MODE", new Color32(90, 220, 90)),
            SimulationState.Paused => ("PLAY MODE (PAUSED)", new Color32(90, 220, 90)),
            _ => null,
        };

        protected override void OnUpdate(float frameTime)
        {
            ApplySimulationRequest();

            base.OnUpdate(frameTime);
        }

        // Walking into a world at rest plays it
        protected override void OnWalkModeEntered()
        {
            if (simulationState == SimulationState.Stopped)
            {
                ApplySimulation(SimulationRequest.Play);
            }
        }

        private void ApplySimulationRequest()
            => ApplySimulation((SimulationRequest)Interlocked.Exchange(ref simulationRequest, (int)SimulationRequest.None));

        // On the render thread, which owns the entity world and the scene
        private void ApplySimulation(SimulationRequest request)
        {
            if (world == null)
            {
                return;
            }

            if (request == SimulationRequest.PlayOrPause)
            {
                request = simulationState == SimulationState.Playing ? SimulationRequest.Pause : SimulationRequest.Play;
            }

            var entitySystem = Renderer.EntitySystem;

            switch (request, simulationState)
            {
                case (SimulationRequest.Play, SimulationState.Stopped):
                    simulationState = SimulationState.Playing;

                    // Before the respawn, so entities spawn hidden as the game has them
                    entitySystem.Enabled = true;
                    ResetScene();
                    entitySystem.StartRound();
                    Input.SetWalkMode(true);

                    // Plays as the game looks
                    showToolsBeforePlay = Tools.ShowTools;
                    Tools.ShowTools = false;
                    RequestVisibilityUpdate();
                    break;

                case (SimulationRequest.Play, SimulationState.Paused):
                    simulationState = SimulationState.Playing;
                    entitySystem.Enabled = true;
                    break;

                case (SimulationRequest.Pause, SimulationState.Playing):
                    simulationState = SimulationState.Paused;
                    entitySystem.Enabled = false;
                    break;

                case (SimulationRequest.Stop, SimulationState.Playing or SimulationState.Paused):
                    simulationState = SimulationState.Stopped;
                    entitySystem.Enabled = false;
                    Input.SetWalkMode(false);
                    ResetScene();

                    Tools.ShowTools = showToolsBeforePlay;
                    RequestVisibilityUpdate();
                    break;

                default:
                    return;
            }

            var state = simulationState;

            if (UiControl is { IsHandleCreated: true } control)
            {
                control.BeginInvoke(() => UpdateSimulationButtons(state));
            }
        }

        /// <summary>
        /// Puts the world back as it loaded: every entity is removed, along with what they left in the scene,
        /// and spawned again from the map's entity lumps.
        /// </summary>
        private void ResetScene()
        {
            Debug.Assert(LoadedWorld != null);

            // Its nodes are about to go
            Selection.Clear();

            Renderer.RemoveEntities();

            LoadedWorld.LoadingProgress = null;
            LoadedWorld.LoadEntities();

            // Spawning again loaded the spawn groups with scenes of their own again
            foreach (var scene in Renderer.Scenes)
            {
                if (scene != Scene)
                {
                    scene.Initialize();
                }
            }

            Scene.RebindLighting();

            Renderer.EntitySystem.SpawnPlayer(Input.PlayerMovement, Scene);

            ApplyRenderModeToNodes();
            ApplyVisibility();
        }
    }
}
