//#define DEBUG_VALIDATE_GLTF

using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;
using Mesh = SharpGLTF.Schema2.Mesh;
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
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#coordinate-system-and-units
        private readonly Matrix4x4 TRANSFORMSOURCETOGLTF = Matrix4x4.CreateScale(0.0254f) * Matrix4x4.CreateFromYawPitchRoll(0, MathF.PI / -2f, MathF.PI / -2f);

        public IProgress<string> ProgressReporter { get; set; }
        public IFileLoader FileLoader { get; }
        private readonly ShaderDataProvider shaderDataProvider;
        private readonly BasicShaderDataProvider shaderDataProviderFallback = new();
        public bool ExportAnimations { get; set; } = true;
        public bool ExportMaterials { get; set; } = true;
        public bool AdaptTextures { get; set; } = true;
        public bool SatelliteImages { get; set; } = true;
        public bool ExportExtras { get; set; }
        public HashSet<string> AnimationFilter { get; } = [];

        private string DstDir;
        private CancellationToken CancellationToken;
        private readonly Dictionary<string, Mesh> ExportedMeshes = [];
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
            or ResourceType.EntityLump
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
        public void Export(Resource resource, string targetPath, CancellationToken cancellationToken = default)
        {
            if (IsExporting)
            {
                throw new InvalidOperationException($"{nameof(GltfModelExporter)} does not support multi threaded exporting, do not call Export while another export is in progress.");
            }

            IsExporting = true;
            CancellationToken = cancellationToken;
            DstDir = Path.GetDirectoryName(targetPath);

            try
            {
                switch (resource.ResourceType)
                {
                    case ResourceType.Mesh:
                        ExportToFile(resource.FileName, targetPath, (VMesh)resource.DataBlock);
                        break;
                    case ResourceType.Model:
                        ExportToFile(resource.FileName, targetPath, (VModel)resource.DataBlock);
                        break;
                    case ResourceType.WorldNode:
                        ExportToFile(resource.FileName, targetPath, (VWorldNode)resource.DataBlock);
                        break;
                    case ResourceType.World:
                        ExportToFile(resource.FileName, targetPath, (VWorld)resource.DataBlock);
                        break;
                    case ResourceType.Map:
                        {
                            var lumpFolder = MapExtract.GetLumpFolderFromVmapRERL(resource.ExternalReferences);
                            var worldFile = Path.Combine(lumpFolder, "world.vwrld");
                            var mapResource = FileLoader.LoadFileCompiled(worldFile) ?? throw new FileNotFoundException($"Failed to load \"{worldFile}\"");
                            ExportToFile(resource.FileName, targetPath, (VWorld)mapResource.DataBlock);
                            break;
                        }
                    case ResourceType.EntityLump:
                        ExportToFile(resource.FileName, targetPath, (VEntityLump)resource.DataBlock);
                        break;
                    default:
                        throw new ArgumentException($"{resource.ResourceType} not supported for gltf export");
                }
            }
            finally
            {
                ExportedMeshes.Clear();
                TextureExportingTasks.Clear();
                ExportedTextures.Clear();
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
        private void ExportToFile(string resourceName, string fileName, VWorld world)
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

                var worldNode = (VWorldNode)worldResource.DataBlock;
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

                var entityLump = (VEntityLump)entityLumpResource.DataBlock;

                LoadEntityMeshes(exportedModel, scene, entityLump);
            }

            WriteModelFile(exportedModel, fileName);
        }

        /// <summary>
        /// Export a list of entities to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="world">The entity lump resource to export.</param>
        private void ExportToFile(string resourceName, string fileName, VEntityLump entityLump)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            LoadEntityMeshes(exportedModel, scene, entityLump);

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadEntityMeshes(ModelRoot exportedModel, Scene scene, VEntityLump entityLump)
        {
            foreach (var entity in entityLump.GetEntities())
            {
                var modelName = entity.GetProperty<string>("model");
                if (string.IsNullOrEmpty(modelName))
                {
                    // Only worrying about models for now
                    continue;
                    // TODO: Think about adding lights with KHR_lights_punctual
                }

                var modelResource = FileLoader.LoadFileCompiled(modelName);
                if (modelResource == null)
                {
                    continue;
                }

                // TODO: skybox/skydome

                var model = (VModel)modelResource.DataBlock;
                var skinName = entity.GetProperty<string>("skin");
                if (skinName == "0" || skinName == "default")
                {
                    skinName = null;
                }

                var transform = EntityTransformHelper.CalculateTransformationMatrix(entity);
                // Add meshes and their skeletons
                LoadModel(exportedModel, scene, model, Path.GetFileNameWithoutExtension(modelName),
                    transform, skinName, entity);
            }

            foreach (var childEntityName in entityLump.GetChildEntityNames())
            {
                if (childEntityName == null)
                {
                    continue;
                }
                var childEntityLumpResource = FileLoader.LoadFileCompiled(childEntityName);
                if (childEntityLumpResource == null)
                {
                    continue;
                }

                var childEntityLump = (VEntityLump)childEntityLumpResource.DataBlock;
                LoadEntityMeshes(exportedModel, scene, childEntityLump);
            }
        }

        private static string GetSkinPathFromModel(VModel model, string skinName)
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
        private void ExportToFile(string resourceName, string fileName, VWorldNode worldNode)
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
                var model = (VModel)modelResource.DataBlock;
                var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4();

                LoadModel(exportedModel, scene, model, name, matrix);
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
                    var model = (VModel)modelResource.DataBlock;

                    if (!AggregateCreateFragments(exportedModel, scene, model, sceneObject, name))
                    {
                        LoadModel(exportedModel, scene, model, name, Matrix4x4.Identity);
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
        private void ExportToFile(string resourceName, string fileName, VModel model)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            // Add meshes and their skeletons
            LoadModel(exportedModel, scene, model, resourceName, Matrix4x4.Identity);

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadModel(ModelRoot exportedModel, Scene scene, VModel model, string name,
            Matrix4x4 transform, string skinName = null, EntityLump.Entity entity = null)
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
                var animations = model.GetAllAnimations(FileLoader);
                // Add animations
                var frame = new Frame(model.Skeleton, model.FlexControllers);
                var boneCount = model.Skeleton.Bones.Length;

                var rotationDicts = Enumerable.Range(0, boneCount)
                    .Select(_ => new Dictionary<float, Quaternion>()).ToArray();
                var lastRotations = new Quaternion?[boneCount];
                var rotationOmitted = new bool[boneCount];

                var translationDicts = Enumerable.Range(0, boneCount)
                    .Select(_ => new Dictionary<float, Vector3>()).ToArray();
                var lastTranslations = new Vector3?[boneCount];
                var translationOmitted = new bool[boneCount];

                var scaleDicts = Enumerable.Range(0, boneCount)
                    .Select(_ => new Dictionary<float, Vector3>()).ToArray();
                var lastScales = new Vector3?[boneCount];
                var scaleOmitted = new bool[boneCount];

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

                    // Cleanup state
                    frame.Clear(model.Skeleton);
                    for (var i = 0; i < boneCount; i++)
                    {
                        rotationDicts[i].Clear();
                        lastRotations[i] = null;
                        rotationOmitted[i] = false;

                        translationDicts[i].Clear();
                        lastTranslations[i] = null;
                        translationOmitted[i] = false;

                        scaleDicts[i].Clear();
                        lastScales[i] = null;
                        scaleOmitted[i] = false;
                    }

                    var exportedAnimation = exportedModel.UseAnimation(animation.Name);

                    var fps = animation.Fps;

                    // Some models have fps of 0.000, which will make time a NaN
                    if (fps == 0)
                    {
                        fps = 1f;
                    }

                    for (var frameIndex = 0; frameIndex < animation.FrameCount; frameIndex++)
                    {
                        frame.FrameIndex = frameIndex;
                        animation.DecodeFrame(frame);
                        var time = frameIndex / fps;
                        var prevFrameTime = (frameIndex - 1) / fps;

                        for (var boneID = 0; boneID < boneCount; boneID++)
                        {
                            var boneFrame = frame.Bones[boneID];

                            var lastRotation = lastRotations[boneID];
                            if (lastRotation != boneFrame.Angle)
                            {
                                if (lastRotation != null && rotationOmitted[boneID])
                                {
                                    rotationOmitted[boneID] = false;
                                    // Restore keyframe before current frame, as otherwise interpolation will
                                    // begin from the first instance of identical frame, and not from previous frame
                                    rotationDicts[boneID].Add(prevFrameTime, lastRotation.Value);
                                }
                                rotationDicts[boneID].Add(time, boneFrame.Angle);
                                lastRotations[boneID] = boneFrame.Angle;
                            }
                            else
                            {
                                rotationOmitted[boneID] = true;
                            }

                            var lastTranslation = lastTranslations[boneID];
                            if (lastTranslation != boneFrame.Position)
                            {
                                if (lastTranslation != null && translationOmitted[boneID])
                                {
                                    translationOmitted[boneID] = false;
                                    // Restore keyframe before current frame, as otherwise interpolation will
                                    // begin from the first instance of identical frame, and not from previous frame
                                    translationDicts[boneID].Add(prevFrameTime, lastTranslation.Value);
                                }
                                translationDicts[boneID].Add(time, boneFrame.Position);
                                lastTranslations[boneID] = boneFrame.Position;
                            }
                            else
                            {
                                translationOmitted[boneID] = true;
                            }

                            var lastScale = lastScales[boneID];
                            var boneFrameScale = boneFrame.Scale;

                            if (float.IsNaN(boneFrameScale) || float.IsInfinity(boneFrameScale))
                            {
                                // See https://github.com/ValveResourceFormat/ValveResourceFormat/issues/527 (NaN)
                                // and https://github.com/ValveResourceFormat/ValveResourceFormat/issues/570 (inf)
                                boneFrameScale = 0.0f;
                            }

                            var scaleVec = boneFrameScale * Vector3.One;

                            if (lastScale != scaleVec)
                            {
                                if (lastScale != null && scaleOmitted[boneID])
                                {
                                    scaleOmitted[boneID] = false;
                                    // Restore keyframe before current frame, as otherwise interpolation will
                                    // begin from the first instance of identical frame, and not from previous frame
                                    scaleDicts[boneID].Add(prevFrameTime, lastScale.Value);
                                }
                                scaleDicts[boneID].Add(time, scaleVec);
                                lastScales[boneID] = scaleVec;
                            }
                            else
                            {
                                scaleOmitted[boneID] = true;
                            }
                        }
                    }

                    for (var boneID = 0; boneID < boneCount; boneID++)
                    {
                        if (animation.FrameCount == 0)
                        {
                            rotationDicts[boneID].Add(0f, model.Skeleton.Bones[boneID].Angle);
                            translationDicts[boneID].Add(0f, model.Skeleton.Bones[boneID].Position);
                            scaleDicts[boneID].Add(0f, Vector3.One);
                        }

                        var jointNode = joints[boneID];
                        exportedAnimation.CreateRotationChannel(jointNode, rotationDicts[boneID], true);
                        exportedAnimation.CreateTranslationChannel(jointNode, translationDicts[boneID], true);
                        exportedAnimation.CreateScaleChannel(jointNode, scaleDicts[boneID], true);
                    }
                }
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
                var node = AddMeshNode(exportedModel, scene, meshName, m.Mesh, m.Mesh.VBIB, joints, boneRemapTable, skinMaterialPath, entity);
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
            var embeddedMeshes = model.GetEmbeddedMeshesAndLoD()
                .Where(m => (m.LoDMask & 1) != 0)
                .Select(m => (m.Mesh, m.MeshIndex, string.Concat(name, ".", m.Name)));

            var refMeshes = model.GetReferenceMeshNamesAndLoD()
                .Where(m => (m.LoDMask & 1) != 0)
                .Select(m =>
                {
                    // Load mesh from file
                    var meshResource = FileLoader.LoadFileCompiled(m.MeshName);
                    var nodeName = Path.GetFileNameWithoutExtension(m.MeshName);
                    if (meshResource == null)
                    {
                        return (null, 0, nodeName);
                    }

                    var mesh = (VMesh)meshResource.DataBlock;
                    return (mesh, m.MeshIndex, nodeName);
                })
                .Where(m => m.mesh != null);

            return embeddedMeshes.Concat(refMeshes);
        }

        /// <summary>
        /// Export a Valve VMESH to Gltf.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="mesh">The mesh resource to export.</param>
        private void ExportToFile(string resourceName, string fileName, VMesh mesh)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var name = Path.GetFileName(resourceName);
            var node = AddMeshNode(exportedModel, scene, name, mesh, mesh.VBIB, joints: null);

            if (node != null)
            {
                // Swap Rotate upright, scale inches to meters.
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }

            WriteModelFile(exportedModel, fileName);
        }

        private Node AddMeshNode(ModelRoot exportedModel, Scene scene, string name,
            VMesh mesh, Blocks.VBIB vbib, Node[] joints, int[] boneRemapTable = null,
            string skinMaterialPath = null, EntityLump.Entity entity = null)
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

            exportedMesh = CreateGltfMesh(name, mesh, vbib, exportedModel, boneRemapTable, skinMaterialPath);
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

        private void WriteModelFile(ModelRoot exportedModel, string filePath)
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

            exportedModel.Save(filePath, settings);

            if (SatelliteImages)
            {
                WaitForTexturesToExport();
            }
        }

        private static (Node skeletonNode, Node[] joints) CreateGltfSkeleton(Scene scene, Skeleton skeleton, string modelName)
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

        private static string ImageWriteCallback(WriteContext ctx, string uri, MemoryImage memoryImage)
        {
            // Since we've already dumped images to disk, skip glTF image write.
            return uri;
        }
    }
}
