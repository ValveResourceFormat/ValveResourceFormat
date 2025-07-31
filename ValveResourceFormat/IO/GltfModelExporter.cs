//#define DEBUG_VALIDATE_GLTF

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;
using Mesh = SharpGLTF.Schema2.Mesh;
using VAnimationClip = ValveResourceFormat.ResourceTypes.ModelAnimation2.AnimationClip;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VModel = ValveResourceFormat.ResourceTypes.Model;
using VWorld = ValveResourceFormat.ResourceTypes.World;
using VWorldNode = ValveResourceFormat.ResourceTypes.WorldNode;

namespace ValveResourceFormat.IO
{
    public partial class GltfModelExporter
    {
        // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
        // Also divides by 100, gltf units are in meters, source engine units are in inches
        // https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#coordinate-system-and-units
        private readonly static Matrix4x4 TRANSFORMSOURCETOGLTF = Matrix4x4.CreateScale(0.0254f) * Matrix4x4.CreateFromYawPitchRoll(0, MathF.PI / -2f, MathF.PI / -2f);

        // https://github.com/KhronosGroup/glTF-Blender-IO/blob/6b29ca135d5255dbfe1dd72424ce7243be73c0be/addons/io_scene_gltf2/blender/com/conversion.py#L20
        private const float PbrWattsTolumens = 683;

        public required IProgress<string> ProgressReporter { get; set; }
        public IFileLoader FileLoader { get; }
        private readonly ShaderDataProvider shaderDataProvider;
        private readonly BasicShaderDataProvider shaderDataProviderFallback = new();
        public bool ExportAnimations { get; set; } = true;
        public bool ExportMaterials { get; set; } = true;
        public bool AdaptTextures { get; set; } = true;
        public bool SatelliteImages { get; set; } = true;
        public bool ExportExtras { get; set; }
        public HashSet<string> AnimationFilter { get; } = [];

        private string DstDir = string.Empty;
        private CancellationToken CancellationToken;
        private readonly Dictionary<string, Mesh> ExportedMeshes = [];
        private readonly List<(PhysAggregateData Phys, string Classname, Matrix4x4 Transform)> PhysicsToExport = [];
        private bool IsExporting;

        public GltfModelExporter(IFileLoader fileLoader)
        {
            ArgumentNullException.ThrowIfNull(fileLoader, nameof(fileLoader));
            FileLoader = fileLoader;
            shaderDataProvider = new ShaderDataProvider(fileLoader);
        }

        public static bool CanExport(Resource resource) => resource.ResourceType
            is ResourceType.Mesh
            or ResourceType.Model
            or ResourceType.NmClip
            or ResourceType.EntityLump
            or ResourceType.PhysicsCollisionMesh
            or ResourceType.WorldNode
            or ResourceType.World
            or ResourceType.Map;

#if DEBUG_VALIDATE_GLTF
#pragma warning disable CS0168 // Variable is declared but never used
        private static ModelRoot debugCurrentExportedModel;
        private static void DebugValidateGLTF()
        {
            try
            {
                debugCurrentExportedModel.WriteGLB(Stream.Null);
            }
            catch (Exception validationException)
            {
                System.Diagnostics.Debugger.Break();
                throw;
            }
        }
#else
        private static void DebugValidateGLTF()
        {
            // noop
        }
#endif

        /// <summary>
        /// Export a Valve resource to Gltf.
        /// </summary>
        /// <param name="resource">The resource being exported.</param>
        /// <param name="targetPath">Target file name.</param>
        /// <param name="cancellationToken">Optional task cancellation token</param>
        public void Export(Resource resource, string? targetPath, CancellationToken cancellationToken = default)
        {
            if (IsExporting)
            {
                throw new InvalidOperationException($"{nameof(GltfModelExporter)} does not support multi threaded exporting, do not call Export while another export is in progress.");
            }

            Debug.Assert(resource.FileName != null);

            IsExporting = true;
            CancellationToken = cancellationToken;

            if (targetPath != null)
            {
                var targetDir = Path.GetDirectoryName(targetPath);
                ArgumentNullException.ThrowIfNull(targetDir);
                DstDir = targetDir;
            }

            try
            {
                switch (resource.ResourceType)
                {
                    case ResourceType.Mesh:
                        ExportToFile(resource.FileName, targetPath, (VMesh)resource.DataBlock!);
                        break;
                    case ResourceType.Model:
                        ExportToFile(resource.FileName, targetPath, (VModel)resource.DataBlock!);
                        break;
                    case ResourceType.WorldNode:
                        ExportToFile(resource.FileName, targetPath, (VWorldNode)resource.DataBlock!);
                        break;
                    case ResourceType.World:
                        ExportToFile(resource.FileName, targetPath, (VWorld)resource.DataBlock!);
                        break;
                    case ResourceType.Map:
                        {
                            var lumpFolder = MapExtract.GetLumpFolderFromVmapRERL(resource.ExternalReferences);
                            var worldFile = Path.Combine(lumpFolder, "world.vwrld");
                            var mapResource = FileLoader.LoadFileCompiled(worldFile) ?? throw new FileNotFoundException($"Failed to load \"{worldFile}\"");
                            ExportToFile(resource.FileName, targetPath, (VWorld)mapResource.DataBlock!);
                            break;
                        }
                    case ResourceType.EntityLump:
                        ExportToFile(resource.FileName, targetPath, (VEntityLump)resource.DataBlock!);
                        break;
                    case ResourceType.PhysicsCollisionMesh:
                        ExportToFile(resource.FileName, targetPath, (PhysAggregateData)resource.DataBlock!);
                        break;
                    case ResourceType.NmClip:
                        ExportToFile(resource.FileName, targetPath, (VAnimationClip)resource.DataBlock!);
                        break;
                    default:
                        throw new ArgumentException($"{resource.ResourceType} not supported for gltf export");
                }
            }
            finally
            {
                ExportedMeshes.Clear();
                PhysicsToExport.Clear();
                TextureExportingTasks.Clear();
                ExportedTextures.Clear();
                ExportedMaterials.Clear();
                TextureSampler = null;
                TexturesExportedSoFar = 0;
                IsExporting = false;
            }
        }

        /// <summary>
        /// Export a Valve VWRLD to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="world">The world resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, VWorld world)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            // First the WorldNodes
            foreach (var worldNodeName in world.GetWorldNodeNames())
            {
                if (worldNodeName == null)
                {
                    continue;
                }

                var worldResource = FileLoader.LoadFile(worldNodeName + ".vwnod_c");
                if (worldResource == null)
                {
                    continue;
                }

                var worldNode = (VWorldNode)worldResource.DataBlock!;
                LoadWorldNodeModels(exportedModel, scene, worldNode);
            }

            // Then the Entities
            foreach (var lumpName in world.GetEntityLumpNames())
            {
                if (lumpName == null)
                {
                    continue;
                }
                var entityLumpResource = FileLoader.LoadFileCompiled(lumpName);
                if (entityLumpResource == null)
                {
                    continue;
                }

                var entityLump = (VEntityLump)entityLumpResource.DataBlock!;

                LoadEntityMeshes(exportedModel, scene, entityLump, Matrix4x4.Identity);
            }

            WriteModelFile(exportedModel, fileName);

            ExportPhysicsIfAny(resourceName, fileName);
        }

        private void ExportPhysicsIfAny(string resourceName, string? fileName)
        {
            if (PhysicsToExport.Count == 0)
            {
                return;
            }

            ProgressReporter?.Report("Exporting physics...");

            ExportedTextures.Clear(); // gltf images can not be shared between gltf files

            var exportedPhysics = CreateModelRoot(resourceName, out var scenePhysics);

            foreach (var (phys, className, transform) in PhysicsToExport)
            {
                LoadPhysicsMeshes(exportedPhysics, scenePhysics, phys, transform, className);
            }

            string? physFileName = null;

            if (fileName != null)
            {
                var lastDot = fileName.LastIndexOf('.');
                Debug.Assert(lastDot >= 0);
                physFileName = $"{fileName.AsSpan(0, lastDot)}_physics{fileName.AsSpan(lastDot)}";
            }

            WriteModelFile(exportedPhysics, physFileName);
        }

        /// <summary>
        /// Export a list of entities to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="world">The entity lump resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, VEntityLump entityLump)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            LoadEntityMeshes(exportedModel, scene, entityLump, Matrix4x4.Identity);

            WriteModelFile(exportedModel, fileName);

            ExportPhysicsIfAny(resourceName, fileName);
        }

        private void LoadEntityMeshes(ModelRoot exportedModel, Scene scene, VEntityLump entityLump, Matrix4x4 parentTransform = default(Matrix4x4))
        {
            var childEntities = entityLump.GetChildEntityNames();
            var childEntityLumps = new Dictionary<string, VEntityLump>(childEntities.Length);

            foreach (var childEntityName in childEntities)
            {
                var newResource = FileLoader.LoadFileCompiled(childEntityName);

                if (newResource == null)
                {
                    continue;
                }

                var childLump = (VEntityLump)newResource.DataBlock!;
                var childName = childLump.Data.GetProperty<string>("m_name");

                childEntityLumps.Add(childName, childLump);
            }

            foreach (var entity in entityLump.GetEntities())
            {
                var transform = EntityTransformHelper.CalculateTransformationMatrix(entity) * parentTransform;
                var modelName = entity.GetProperty<string>("model");
                var className = entity.GetProperty<string>("classname");

                if (string.IsNullOrEmpty(modelName))
                {
                    // Add environment lights with KHR_lights_punctual
                    // https://github.com/KhronosGroup/glTF/blob/main/extensions/2.0/Khronos/KHR_lights_punctual/README.md
                    // TODO: Add point and spot lights
                    if (className == "light_environment")
                    {
                        if (!Matrix4x4.Decompose(transform, out var scale, out var _, out var positionVector))
                        {
                            throw new InvalidOperationException("Matrix decompose failed");
                        }

                        var pitchYawRoll = entity.GetVector3Property("angles");
                        var rollMatrix = Matrix4x4.CreateRotationX(pitchYawRoll.Z * MathF.PI / 180f);
                        var pitchMatrix = Matrix4x4.CreateRotationY((pitchYawRoll.X - 90) * MathF.PI / 180f); // copypasta because of this
                        var yawMatrix = Matrix4x4.CreateRotationZ(pitchYawRoll.Y * MathF.PI / 180f);
                        var rotationMatrix = rollMatrix * pitchMatrix * yawMatrix;

                        var scaleMatrix = Matrix4x4.CreateScale(scale);
                        var positionMatrix = Matrix4x4.CreateTranslation(positionVector);
                        var lightMatrix = scaleMatrix * rotationMatrix * positionMatrix;

                        var node = scene.CreateNode(className);
                        node.PunctualLight = CreateGltfLightEnvironment(exportedModel, entity);
                        node.LocalMatrix = lightMatrix * TRANSFORMSOURCETOGLTF;
                    }
                    else if (className == "point_template")
                    {
                        var entityLumpName = entity.GetProperty<string>("entitylumpname");

                        if (entityLumpName != null && childEntityLumps.TryGetValue(entityLumpName, out var childLump))
                        {
                            LoadEntityMeshes(exportedModel, scene, childLump, transform);
                        }
                        else
                        {
                            ProgressReporter?.Report($"Failed to find child entity lump with name {entityLumpName}.");
                        }
                    }

                    continue;
                }

                if (className == "csgo_player_previewmodel")
                {
                    continue;
                }

                var modelResource = FileLoader.LoadFileCompiled(modelName);
                if (modelResource == null)
                {
                    continue;
                }

                // TODO: skybox/skydome

                var model = (VModel)modelResource.DataBlock!;
                var skinName = entity.GetProperty<string>("skin");
                if (skinName == "0" || skinName == "default")
                {
                    skinName = null;
                }

                // todo: rendercolor might sometimes be vec4, which holds renderamt
                var rendercolor = entity.GetColor32Property("rendercolor");
                var renderamt = entity.GetPropertyUnchecked("renderamt", 1.0f);

                if (renderamt > 1f)
                {
                    renderamt /= 255f;
                }

                rendercolor.X = MathF.Pow(rendercolor.X, 2.2f);
                rendercolor.Y = MathF.Pow(rendercolor.Y, 2.2f);
                rendercolor.Z = MathF.Pow(rendercolor.Z, 2.2f);
                var tintColor = new Vector4(rendercolor, renderamt);

                // Add meshes and their skeletons
                LoadModel(exportedModel, scene, model, Path.GetFileNameWithoutExtension(modelName),
                    transform, tintColor, skinName, entity);

                // Load model physics
                var phys = model.GetEmbeddedPhys();
                if (phys == null)
                {
                    var refPhysicsPaths = model.GetReferencedPhysNames().ToArray();
                    if (refPhysicsPaths.Length != 0)
                    {
                        var newResource = FileLoader.LoadFileCompiled(refPhysicsPaths.First());
                        if (newResource?.DataBlock is PhysAggregateData physFile)
                        {
                            phys = physFile;
                        }
                    }
                }

                if (phys != null)
                {
                    PhysicsToExport.Add((phys, className, transform));
                }
            }

        }

        private static string? GetSkinPathFromModel(VModel model, string skinName)
        {
            var materialGroupForSkin = model.GetMaterialGroups()
                .SingleOrDefault(group => group.Name == skinName);

            // Given these are at the model level, and otherwise pull materials from drawcalls
            // on the mesh, not sure how they correlate if there's more than one here
            // So just take the first one and hope for the best
            return materialGroupForSkin.Materials?[0];
        }

        /// <summary>
        /// Export a Valve VWNOD to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="worldNode">The worldNode resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, VWorldNode worldNode)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);
            LoadWorldNodeModels(exportedModel, scene, worldNode);

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadWorldNodeModels(ModelRoot exportedModel, Scene scene, VWorldNode worldNode)
        {
            foreach (var sceneObject in worldNode.SceneObjects)
            {
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");
                if (renderableModel == null)
                {
                    continue;
                }

                var modelResource = FileLoader.LoadFileCompiled(renderableModel);
                if (modelResource == null)
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(renderableModel);
                var model = (VModel)modelResource.DataBlock!;
                var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4();
                var tintColor = sceneObject.GetSubCollection("m_vTintColor").ToVector4();

                if (tintColor == Vector4.Zero)
                {
                    tintColor = Vector4.One;
                }

                LoadModel(exportedModel, scene, model, name, matrix, tintColor);
            }

            foreach (var sceneObject in worldNode.AggregateSceneObjects)
            {
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");

                if (renderableModel != null)
                {
                    var modelResource = FileLoader.LoadFileCompiled(renderableModel);

                    if (modelResource == null)
                    {
                        continue;
                    }

                    var name = Path.GetFileNameWithoutExtension(renderableModel);
                    var model = (VModel)modelResource.DataBlock!;

                    if (!AggregateCreateFragments(exportedModel, scene, model, sceneObject, name))
                    {
                        LoadModel(exportedModel, scene, model, name, Matrix4x4.Identity, Vector4.One);
                    }
                }
            }
        }

        /// <summary>
        /// Export a Valve VMDL to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="model">The model resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, VModel model)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            // Add meshes and their skeletons
            LoadModel(exportedModel, scene, model, resourceName, Matrix4x4.Identity, Vector4.One);

            WriteModelFile(exportedModel, fileName);

            // Add embedded phys
            var phys = model.GetEmbeddedPhys();
            if (phys != null)
            {
                string? physFileName = null;

                if (fileName != null)
                {
                    var lastDot = fileName.LastIndexOf('.');
                    Debug.Assert(lastDot >= 0);
                    physFileName = $"{fileName.AsSpan(0, lastDot)}_physics{fileName.AsSpan(lastDot)}";
                }

                ExportToFile(resourceName, physFileName, phys);
            }
        }

        /// <summary>
        /// Export a Valve VPHYS to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="physAggregateData">The physics aggregate data resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, PhysAggregateData physAggregateData)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            LoadPhysicsMeshes(exportedModel, scene, physAggregateData, Matrix4x4.Identity);

            WriteModelFile(exportedModel, fileName);
        }

        /// <summary>
        /// Export a Valve Animation Clip to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="animationClip">The animation clip resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, VAnimationClip animationClip)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            var skeletonResource = FileLoader.LoadFileCompiled(animationClip.SkeletonName)
                ?? throw new InvalidOperationException($"Unable to load skeleton data '{animationClip.SkeletonName}'.");

            var skeletonData = Skeleton.FromSkeletonData(((BinaryKV3)skeletonResource.DataBlock!).Data);

            var (skeletonNode, joints) = CreateGltfSkeleton(scene, skeletonData, animationClip.SkeletonName);
            if (joints == null)
            {
                throw new InvalidDataException($"Failure creating glTF skeleton for '{animationClip.SkeletonName}'.");
            }

            //if (ExportAnimations)
            {
                var animation = new ResourceTypes.ModelAnimation.Animation(animationClip);
                var animationWriter = new AnimationWriter(skeletonData, []);
                animationWriter.WriteAnimation(exportedModel, joints, animation);
            }

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadModel(ModelRoot exportedModel, Scene scene, VModel model, string name,
            Matrix4x4 transform, Vector4 tintColor, string? skinName = null, EntityLump.Entity? entity = null)
        {
#if DEBUG
            ProgressReporter?.Report($"Loading model {name}");
#endif

            CancellationToken.ThrowIfCancellationRequested();

            var (skeletonNode, joints) = ExportAnimations
                ? CreateGltfSkeleton(scene, model.Skeleton, name)
                : (null, null);

            if (skeletonNode != null)
            {
                Debug.Assert(joints != null);

                var animations = model.GetAllAnimations(FileLoader);
                var animationWriter = new AnimationWriter(model.Skeleton, model.FlexControllers);
                var animationFilter = AnimationFilter;

                // When exporting map entities, only export the default animation
                if (entity != null)
                {
                    var entityAnimation = entity.GetProperty<string>("defaultanim") ?? entity.GetProperty<string>("idleanim");
                    animationFilter = [
                        entityAnimation,
                        $"@{entityAnimation}"
                    ];
                }

                foreach (var animation in animations)
                {
                    if (animationFilter.Count > 0 && !animationFilter.Contains(animation.Name))
                    {
                        continue;
                    }

                    animationWriter.WriteAnimation(exportedModel, joints, animation);
                    CancellationToken.ThrowIfCancellationRequested();
                }
            }
            else
            {
                Debug.Assert(joints == null);
            }

            // Swap Rotate upright, scale inches to meters.
            transform *= TRANSFORMSOURCETOGLTF;

            var skinMaterialPath = skinName != null ? GetSkinPathFromModel(model, skinName) : null;

            foreach (var m in LoadModelMeshes(model, name))
            {
                var meshName = m.Name;
                if (skinName != null)
                {
                    meshName = string.Concat(meshName, ".", skinName);
                }

                var boneRemapTable = model.GetRemapTable(m.MeshIndex);
                var node = AddMeshNode(exportedModel, scene, meshName, tintColor, m.Mesh, m.Mesh.VBIB, joints, boneRemapTable, skinMaterialPath, entity);
                if (node != null)
                {
                    node.WorldMatrix = transform;

                    DebugValidateGLTF();
                }
            }

            // Even though that's not documented, order matters.
            // WorldMatrix should only be set after everything else.
            if (skeletonNode != null)
            {
                skeletonNode.WorldMatrix = transform;
            }
        }

        /// <summary>
        /// Create a combined list of referenced and embedded meshes. Importantly retains the
        /// refMeshes order so it can be used for getting skeletons.
        /// </summary>
        /// <param name="model">The model to get the meshes from.</param>
        /// <returns>A tuple of meshes and their names.</returns>
        private IEnumerable<(VMesh Mesh, int MeshIndex, string Name)> LoadModelMeshes(VModel model, string name)
        {
            foreach (var m in model.GetEmbeddedMeshesAndLoD().Where(static m => (m.LoDMask & 1) != 0))
            {
                yield return (m.Mesh, m.MeshIndex, string.Concat(name, ".", m.Name));
            }

            foreach (var m in model.GetReferenceMeshNamesAndLoD().Where(static m => (m.LoDMask & 1) != 0))
            {
                var meshResource = FileLoader.LoadFileCompiled(m.MeshName);
                var nodeName = Path.GetFileNameWithoutExtension(m.MeshName);
                if (meshResource == null)
                {
                    continue;
                }

                var mesh = (VMesh)meshResource.DataBlock!;
                yield return (mesh, m.MeshIndex, nodeName);
            }
        }

        /// <summary>
        /// Export a Valve VMESH to Gltf.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="mesh">The mesh resource to export.</param>
        private void ExportToFile(string resourceName, string? fileName, VMesh mesh)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var name = Path.GetFileName(resourceName);
            var node = AddMeshNode(exportedModel, scene, name, Vector4.One, mesh, mesh.VBIB, joints: null);

            if (node != null)
            {
                // Swap Rotate upright, scale inches to meters.
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }

            WriteModelFile(exportedModel, fileName);
        }

        private Node? AddMeshNode(ModelRoot exportedModel, Scene scene, string name, Vector4 tintColor,
            VMesh mesh, Blocks.VBIB vbib, Node[]? joints, int[]? boneRemapTable = null,
            string? skinMaterialPath = null, EntityLump.Entity? entity = null)
        {
            if (mesh.Data.GetArray("m_sceneObjects").Length == 0)
            {
                return null;
            }

            var newNode = scene.CreateNode(name);
            if (ExportedMeshes.TryGetValue(name, out var exportedMesh))
            {
                // Make a new node that uses the existing mesh
                newNode.Mesh = exportedMesh;
                return newNode;
            }

            exportedMesh = CreateGltfMesh(name, mesh, vbib, exportedModel, boneRemapTable, skinMaterialPath, tintColor);
            ExportedMeshes.Add(name, exportedMesh);

            if (entity != null && ExportExtras)
            {
                foreach (var (key, value) in entity.Properties)
                {
                    exportedMesh.Extras[key] = value as string;
                }
            }

            var hasVertexJoints = exportedMesh.Primitives.All(primitive => primitive.GetVertexAccessor("JOINTS_0") != null);

            if (joints == null || !hasVertexJoints)
            {
                return newNode.WithMesh(exportedMesh);
            }

            newNode.WithSkinnedMesh(exportedMesh, Matrix4x4.Identity, joints);
            // WorldMatrix is set only once on skeletonNode
            return null;
        }

        private ModelRoot CreateModelRoot(string resourceName, out Scene scene)
        {
            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = StringToken.VRF_GENERATOR;
            scene = exportedModel.UseScene(Path.GetFileName(resourceName));

#if DEBUG_VALIDATE_GLTF
            debugCurrentExportedModel = exportedModel;
#endif

            TextureSampler = exportedModel.UseTextureSampler(TextureWrapMode.REPEAT, TextureWrapMode.REPEAT, TextureMipMapFilter.LINEAR_MIPMAP_LINEAR, TextureInterpolationFilter.LINEAR);

            return exportedModel;
        }

        private void WriteModelFile(ModelRoot exportedModel, string? filePath)
        {
            if (!SatelliteImages)
            {
                WaitForTexturesToExport();
            }

            ProgressReporter?.Report($"Writing model to file '{Path.GetFileName(filePath)}'...");

            var settings = new WriteSettings
            {
                ImageWriting = SatelliteImages ? ResourceWriteMode.SatelliteFile : ResourceWriteMode.BufferView,
                ImageWriteCallback = ImageWriteCallback,
                JsonIndented = false,
                MergeBuffers = true,
                BuffersMaxSize = 1_074_000_000,
            };

            // If no file path is provided, validate the schema without writing a file
            if (filePath == null)
            {
                exportedModel.WriteGLB(Stream.Null, settings);
                return;
            }

            // See https://github.com/KhronosGroup/glTF/blob/0bc36d536946b13c4807098f9cf62ddff738e7a5/specification/2.0/README.md#buffers-and-buffer-views
            // Disable merging buffers if the buffer size is >=2GiB, otherwise this will
            // cause SharpGLTF to run past the int32 limitation and crash.
            var totalSize = exportedModel.LogicalBuffers.Sum(buffer => (long)buffer.Content.Length);
            if (totalSize >= int.MaxValue && filePath.EndsWith(".glb", StringComparison.InvariantCultureIgnoreCase))
            {
                throw new NotSupportedException("VRF does not properly support big model (>=2GiB) exports yet due to glTF limitations. Try exporting as .gltf, not .glb.");
            }

#if DEBUG
            settings.JsonIndented = true;
#endif

            exportedModel.Save(filePath, settings);

            if (SatelliteImages)
            {
                WaitForTexturesToExport();
            }
        }

        private static (Node? skeletonNode, Node[]? joints) CreateGltfSkeleton(Scene scene, Skeleton skeleton, string modelName)
        {
            if (skeleton.Bones.Length == 0)
            {
                return (null, null);
            }

            var skeletonNode = scene.CreateNode(modelName);
            var boneNodes = new Dictionary<string, Node>();
            var joints = new Node[skeleton.Bones.Length];
            foreach (var root in skeleton.Roots)
            {
                CreateBonesRecursive(root, skeletonNode, ref joints);
            }
            return (skeletonNode, joints);
        }

        private static void CreateBonesRecursive(Bone bone, Node parent, ref Node[] joints)
        {
            var node = parent.CreateNode(bone.Name)
                .WithLocalTranslation(bone.Position)
                .WithLocalRotation(bone.Angle);
            joints[bone.Index] = node;

            // Recurse into children
            foreach (var child in bone.Children)
            {
                CreateBonesRecursive(child, node, ref joints);
            }
        }

        private static PunctualLight CreateGltfLightEnvironment(ModelRoot exportedModel, VEntityLump.Entity entity)
        {
            var intensity = entity.GetPropertyUnchecked("brightness", 1f);
            var color = entity.GetColor32Property("color");

            var envLight = exportedModel
                .CreatePunctualLight(PunctualLightType.Directional)
                .WithColor(color, intensity * PbrWattsTolumens);

            return envLight;
        }

        private static string ImageWriteCallback(WriteContext ctx, string uri, MemoryImage memoryImage)
        {
            // Since we've already dumped images to disk, skip glTF image write.
            return uri;
        }
    }
}
