using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Picking;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.Source1;
using ValveResourceFormat.Renderer.Source1.Entities;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Source1.Bsp;
using ValveResourceFormat.Source1.Entities;
using ValveResourceFormat.Source1.FileSystem;
using ValveResourceFormat.Source1.Model;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Scene of a Source 1 map: world and brush models with the baked lightmap, static props and the 2D sky.
    /// The pakfile of the map is mounted in <see cref="Source1FileSystem"/> so its materials win over the game's.
    /// </summary>
    class GLSource1MapViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Source1FileSystem? fileSystem, string mapPath)
        : GLSceneViewer(vrfGuiContext, rendererContext), IEntityMapViewer
    {
        private readonly Dictionary<string, Source1Model?> models = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<EntityLump.Entity> entities = [];
        private readonly Dictionary<SceneNode, BspStaticProp> staticProps = [];
        private BspFile? bsp;
        private Source1MapLighting? lighting;
        private Scene? skyScene;
        private Vector3? startLocation;
        private Vector3 startAngles;
        private string? mapName;
        private EntityInfoForm? entityInfoForm;

        public string? MapName => mapName;

        public List<EntityLump.Entity>? MapEntities => entities;

        // The 2D sky is not an entity scene, so nothing is drawn as a scene of its own
        public IEnumerable<SpawnGroup> OwnSceneSpawnGroups => [];

        public override void Dispose()
        {
            base.Dispose();

            skyScene?.Dispose();
            entityInfoForm?.Dispose();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();
            LoadDefaultLighting();
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Render"))
            {
                AddRenderModeSelectionControl();
                AddWireframeToggleControl();
            }

            base.AddUiControls();
        }

        protected override void OnPicked(PickResult result)
        {
            if (result.HitNothing)
            {
                Selection.Clear();
                return;
            }

            if (result.Intent is not (PickIntent.Select or PickIntent.Details))
            {
                return;
            }

            var sceneNode = Renderer.FindPickedNode(result);

            if (sceneNode == null)
            {
                return;
            }

            if (result.Intent == PickIntent.Details)
            {
                Program.MainForm.Invoke(() => ShowDetails(sceneNode, focus: true));
                return;
            }

            if (result.Has(PickModifiers.Control))
            {
                Selection.Toggle(sceneNode);
            }
            else
            {
                Selection.Select(sceneNode);
            }

            if (entityInfoForm != null)
            {
                Program.MainForm.Invoke(() => ShowDetails(sceneNode, focus: false));
            }
        }

        public void SelectAndFocusEntity(EntityLump.Entity entity)
        {
            if (UiControl?.Parent is TabPage tabPage && tabPage.Parent is TabControl tabControl)
            {
                tabControl.SelectTab(tabPage);
            }

            var node = Renderer.FindNode(entity);

            if (node == null)
            {
                FocusCameraOnBounds(EntityOriginBounds(entity));
                return;
            }

            SelectAndFocusNode(node);
        }

        public void SelectAndFocusNode(SceneNode node)
        {
            Selection.Select(node);
            FocusCameraOnBounds(SelectionBounds(node));
        }

        public AABB EntityOriginBounds(EntityLump.Entity entity)
        {
            var spawned = Renderer.EntitySystem.Entities.FirstOrDefault(candidate => !candidate.IsRemoved && candidate.Data == entity);

            if (spawned != null)
            {
                return PointBounds(Vector3.Transform(spawned.WorldOrigin, spawned.Scene.ToViewerWorld));
            }

            return PointBounds(entity.GetVector3Property("origin"));
        }

        private void ShowDetails(SceneNode node, bool focus)
        {
            if (node.EntityData is { } entity)
            {
                ShowEntityDetails(entity);
            }
            else if (staticProps.TryGetValue(node, out var prop))
            {
                ShowStaticPropDetails(prop);
            }
            else
            {
                ShowNodeDetails(node);
            }

            if (!focus || entityInfoForm == null)
            {
                return;
            }

            if (entityInfoForm.WindowState == FormWindowState.Minimized)
            {
                entityInfoForm.WindowState = FormWindowState.Normal;
            }

            entityInfoForm.EntityInfoControl.Focus();
        }

        private void ShowEntityDetails(EntityLump.Entity entity)
        {
            var form = PrepareEntityInfoForm();

            form.EntityInfoControl.PopulateFromEntity(entities, entity);

            var classname = entity.GetStringProperty("classname");
            var targetName = entity.FriendlyTargetName;
            form.Text = string.IsNullOrEmpty(targetName)
                ? $"Entity: {classname}"
                : $"Entity: {classname} ({targetName})";

            form.EntityInfoControl.ShowPopulatedTabs();
        }

        private void ShowStaticPropDetails(BspStaticProp prop)
        {
            var form = PrepareEntityInfoForm();
            var info = form.EntityInfoControl;

            form.Text = $"Static prop: {prop.ModelName}";

            info.AddProperty("Model", prop.ModelName);
            info.AddProperty("Skin", prop.Skin.ToString(CultureInfo.InvariantCulture));
            info.AddProperty("Origin", FormatVector(prop.Origin));
            info.AddProperty("Angles", FormatVector(prop.Angles));
            info.AddProperty("Scale", prop.Scale.ToString("G6", CultureInfo.InvariantCulture));
            info.AddProperty("Solid", prop.Solid.ToString(CultureInfo.InvariantCulture));
            info.AddProperty("Flags", prop.Flags.ToString());
            info.AddProperty("Fade Min Distance", prop.FadeMinDist.ToString("G6", CultureInfo.InvariantCulture));
            info.AddProperty("Fade Max Distance", prop.FadeMaxDist.ToString("G6", CultureInfo.InvariantCulture));
            info.AddProperty("Forced Fade Scale", prop.ForcedFadeScale.ToString("G6", CultureInfo.InvariantCulture));

            if ((prop.Flags & StaticPropFlags.UseLightingOrigin) != 0)
            {
                info.AddProperty("Lighting Origin", FormatVector(prop.LightingOrigin));
            }

            var color = new Color32(prop.ColorModulation);
            info.AddProperty("Color Modulation", string.Create(CultureInfo.InvariantCulture, $"{color.R} {color.G} {color.B} {color.A}"));
            info.AddProperty("Model Index", prop.PropType.ToString(CultureInfo.InvariantCulture));
            info.AddProperty("Leaf Count", prop.LeafCount.ToString(CultureInfo.InvariantCulture));

            info.ShowPopulatedTabs();
        }

        private void ShowNodeDetails(SceneNode node)
        {
            var form = PrepareEntityInfoForm();
            var info = form.EntityInfoControl;

            form.Text = $"{node.GetType().Name}: {node.Name}";

            if (node is MeshCollectionNode meshes)
            {
                foreach (var drawCall in meshes.RenderableMeshes.SelectMany(static mesh => mesh.DrawCalls))
                {
                    var material = drawCall.Material.Material;
                    info.AddProperty("Shader", material.ShaderName);
                    info.AddProperty("Material", material.Name);
                }
            }

            info.AddProperty("Flags", node.Flags.ToString());
            info.AddProperty("Layer", node.LayerName ?? string.Empty);

            info.ShowPopulatedTabs();
        }

        private static string FormatVector(Vector3 vector)
            => string.Create(CultureInfo.InvariantCulture, $"{vector.X:F2} {vector.Y:F2} {vector.Z:F2}");

        /// <summary>Opens the details window if it is not open yet, and empties it.</summary>
        private EntityInfoForm PrepareEntityInfoForm()
        {
            if (entityInfoForm == null)
            {
                entityInfoForm = new EntityInfoForm();
                entityInfoForm.EntityInfoControl.LinkedResourceActivated += OnLinkedResourceActivated;
                entityInfoForm.FormClosed += OnEntityInfoFormClosed;
                entityInfoForm.Activated += RenderLoopThread.OnAppActivated;
                entityInfoForm.Show();
            }

            entityInfoForm.EntityInfoControl.Clear();

            return entityInfoForm;
        }

        private void OnLinkedResourceActivated(object? sender, LinkedResource link)
        {
            if (link is not LinkedEntities { Entities: [var entity, ..] } linked)
            {
                return;
            }

            SelectAndFocusEntity(entity);

            if (linked.Entities.Count == 1)
            {
                ShowEntityDetails(entity);
            }
        }

        private void OnEntityInfoFormClosed(object? sender, FormClosedEventArgs e)
        {
            entityInfoForm = null;
        }

        protected override void LoadScene()
        {
            bsp = BspFile.Open(mapPath);
            mapName = bsp.Name;

#pragma warning disable CA2000 // Disposed with the file system that owns the archive
            if (fileSystem != null && BspPakFileReader.Open(bsp) is { } pakFile)
#pragma warning restore CA2000
            {
                fileSystem.AddArchive(pakFile, ownsArchive: true);
            }

            Scene.RendererContext.Source1HighDynamicRange = bsp.HasHdrLighting;

            var materialLoader = Scene.RendererContext.MaterialLoader;
            RenderMaterial ResolveMaterial(string name) => materialLoader.GetMaterial(Source1Paths.ToMaterialPath(name), null);

            lighting = new Source1MapLighting(Scene, bsp, fileSystem);

            if (lighting.Sky != null)
            {
                skyScene = new Scene(Scene.RendererContext) { WorldGroup = "skybox" };
            }

            var entityBySource = ConvertEntities(bsp);

            var worldBounds = Source1MapSceneBuilder.Build(
                Scene,
                bsp,
                ResolveMaterial,
                CreateStaticProp,
                lighting,
                skyScene,
                (source, node) => AttachEntity(entityBySource, source, node));

            Source1DetailPropBuilder.Build(
                Scene,
                bsp,
                ResolveMaterial,
                (modelPath, transform) => CreateStaticModel(Scene, modelPath, transform),
                lighting.SkyBounds);

            Source1SpriteBuilder.Build(
                Scene,
                skyScene,
                bsp,
                ResolveMaterial,
                lighting.SkyBounds);

            Source1RopeBuilder.Build(
                Scene,
                skyScene,
                bsp,
                ResolveMaterial,
                lighting,
                lighting.SkyBounds);

            AddModelEntities(bsp, entityBySource);

            var worldspawn = bsp.Entities.FirstOrDefault(static entity => entity.ClassName == "worldspawn");

            if (worldspawn != null && fileSystem != null)
            {
                Scene.Skybox2D = Source1MapSceneBuilder.CreateSkybox(Scene, fileSystem, worldspawn.GetString("skyname"), bsp.HasHdrLighting);
            }

            var spawn = bsp.Entities.FirstOrDefault(static entity => entity.ClassName == "info_player_teamspawn")
                ?? bsp.Entities.FirstOrDefault(static entity => entity.ClassName.StartsWith("info_player", StringComparison.Ordinal));

            if (spawn != null)
            {
                startLocation = spawn.Origin + new Vector3(0, 0, 64);
                startAngles = spawn.Angles;
            }
            else
            {
                startLocation = worldBounds.Center + new Vector3(0, 0, worldBounds.Size.Z * 0.25f);
                startAngles = new Vector3(15, 45, 0);
            }

            SpawnEntities(bsp);

            var post = new ScenePostProcessVolume(Scene)
            {
                HasBloom = true,
                IsMaster = true,
            };

            Scene.PostProcessInfo.AddPostProcessVolume(post);
        }

        private Dictionary<BspEntity, EntityLump.Entity> ConvertEntities(BspFile bsp)
        {
            var lump = Source1EntityConverter.CreateLump(bsp.Name);
            var options = new Source1EntityConverterOptions { HdrLighting = bsp.HasHdrLighting };
            var entityBySource = new Dictionary<BspEntity, EntityLump.Entity>();

            foreach (var source in bsp.Entities)
            {
                if (Source1EntityConverter.Convert(source, lump, options) is not { } entity)
                {
                    continue;
                }

                entities.Add(entity);
                entityBySource.Add(source, entity);
            }

            return entityBySource;
        }

        private static void AttachEntity(Dictionary<BspEntity, EntityLump.Entity> entityBySource, BspEntity source, SceneNode node)
        {
            if (entityBySource.TryGetValue(source, out var entity))
            {
                node.EntityData = entity;
            }
        }

        // The prop models are drawn here, so the entity classes only get the entities for their tool visuals and I/O
        private void SpawnEntities(BspFile bsp)
        {
            Scene.LightingInfo.LightmapVersionNumber = 8;
            Scene.LightingInfo.LightmapGameVersionNumber = 1;

            if (skyScene != null && lighting?.Sky is { } sky)
            {
                skyScene.LightingInfo.LightmapVersionNumber = 8;
                skyScene.LightingInfo.LightmapGameVersionNumber = 1;
                Source1EntitySpawner.SetSkyFog(skyScene, entities);
                Renderer.SetFixedSky(skyScene, sky);
            }

            Renderer.EntitySystem.Enabled = false;

            Source1EntitySpawner.Spawn(Renderer.EntitySystem, Scene, bsp.Name, entities, lightFocus: startLocation ?? Vector3.Zero);

            if (Scene.FogInfo.LinearFog is { MaxDensity: > 0f } fog && fog.End > fog.Start)
            {
                Scene.FogInfo.ObjectsUseLinearFog = true;
            }
        }

        private Source1Model? GetModel(string path)
        {
            if (models.TryGetValue(path, out var cached))
            {
                return cached;
            }

            Source1Model? model = null;

            if (fileSystem != null && fileSystem.Exists(path))
            {
                try
                {
                    model = Source1Model.Load(path, fileSystem.OpenFile);
                }
                catch (Exception e) when (e is InvalidDataException or IOException or NotSupportedException)
                {
                    Log.Warn(nameof(GLSource1MapViewer), $"Failed to load model {path}: {e.Message}");
                }
            }

            models[path] = model;
            return model;
        }

        private VhvFile? LoadVertexLighting(int propIndex)
        {
            if (fileSystem == null || bsp == null)
            {
                return null;
            }

            if ((bsp.StaticProps.Props[propIndex].Flags & StaticPropFlags.NoPerVertexLighting) != 0)
            {
                return null;
            }

            var hdrName = string.Create(CultureInfo.InvariantCulture, $"sp_hdr_{propIndex}.vhv");
            var ldrName = string.Create(CultureInfo.InvariantCulture, $"sp_{propIndex}.vhv");
            using var stream = (bsp.HasHdrLighting ? fileSystem.OpenFile(hdrName) : null) ?? fileSystem.OpenFile(ldrName);

            if (stream == null)
            {
                return null;
            }

            return VhvFile.Parse(stream);
        }

        private SceneNode? CreateStaticProp(Scene target, BspStaticProp prop, int propIndex, Matrix4x4 transform)
        {
            var node = CreateStaticModel(target, prop.ModelName, transform, prop.Skin, LoadVertexLighting(propIndex));

            if (node != null)
            {
                staticProps.Add(node, prop);
            }

            return node;
        }

        private Source1ModelSceneNode? CreateStaticModel(Scene target, string modelPath, Matrix4x4 transform, int skin = 0, VhvFile? vertexLighting = null)
        {
            var model = GetModel(modelPath);

            if (model == null)
            {
                return null;
            }

            var node = new Source1ModelSceneNode(target, model, CreateMaterialResolver(transform.Translation), skin: skin, vertexLighting: vertexLighting);
            node.Transform = transform;
            return node;
        }

        private Func<string, Dictionary<string, byte>?, RenderMaterial> CreateMaterialResolver(Vector3 position)
        {
            var materialLoader = Scene.RendererContext.MaterialLoader;

            return (path, arguments) =>
            {
                var material = materialLoader.GetMaterial(path, arguments);

                return lighting?.GetInstance(material, arguments, position, useAmbientCube: true) ?? material;
            };
        }

        private void AddModelEntities(BspFile bsp, Dictionary<BspEntity, EntityLump.Entity> entityBySource)
        {
            var entityModels = new Source1EntityModels(fileSystem, bsp.Name, bsp.Entities);

            foreach (var entity in bsp.Entities)
            {
                if (Source1MapSceneBuilder.IsHiddenAtSpawn(entity) || entityModels.IsSpawnedLater(entity))
                {
                    continue;
                }

                if (entityModels.GetModel(entity) is not { } entityModel)
                {
                    continue;
                }

                var target = Source1MapLighting.SelectScene(lighting?.SkyBounds, Scene, skyScene, entity.Origin);

                if (target == null)
                {
                    continue;
                }

                var model = GetModel(entityModel.Path);

                if (model == null)
                {
                    continue;
                }

                var node = new Source1ModelSceneNode(target, model, CreateMaterialResolver(entity.Origin), skin: entityModel.Skin, animated: true, flexes: true);
                AttachEntity(entityBySource, entity, node);

                var scale = entity.GetFloat("modelscale", 1f);
                var rigid = EntityTransformHelper.ToRigidTransformationMatrix(entity.Angles, entity.Origin);
                node.Transform = scale > 0f && scale != 1f ? Matrix4x4.CreateScale(scale) * rigid : rigid;

                if (entity.Properties.ContainsKey("rendercolor"))
                {
                    node.Tint = entity.GetVector3("rendercolor") / 255f;
                }

                // Without an animation the node holds sequence 0, whose pose can differ from the reference pose
                var animated = PlayEntitySequence(node, entity.GetString("DefaultAnim"), entityModel);

                if (animated)
                {
                    node.Loop = true;
                }
                else
                {
                    node.Paused = true;
                }

                target.Add(node, animated);

                node.SetBody(entityModel.Body);
                SetBodyGroups(node, entityModel.BodyGroups);
            }
        }

        private static bool PlayEntitySequence(Source1ModelSceneNode node, string defaultAnimation, Source1EntityModel entityModel)
        {
            if (defaultAnimation.Length > 0 && node.PlaySequence(defaultAnimation))
            {
                return true;
            }

            if (entityModel.Sequence != null && node.PlaySequence(entityModel.Sequence))
            {
                return true;
            }

            return entityModel.Activity != null && node.PlayActivity(entityModel.Activity);
        }

        // A sub-model the model does not have is left at the first one
        private static void SetBodyGroups(Source1ModelSceneNode node, (int Group, int SubModel)[] bodyGroups)
        {
            var bodyParts = node.Model.BodyParts;

            foreach (var (group, subModel) in bodyGroups)
            {
                if (group < bodyParts.Length && subModel < bodyParts[group].Models.Length)
                {
                    node.SetBodyGroup(group, subModel);
                }
            }
        }

        public override void PostSceneLoad()
        {
            base.PostSceneLoad();

            if (startLocation is { } location)
            {
                Input.Camera.SetLocation(location);
                Input.Camera.SetFromQAngle(startAngles);
            }
        }
    }
}
