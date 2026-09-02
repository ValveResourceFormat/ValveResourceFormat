//#define DEBUG_VALIDATE_GLTF

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using ValveKeyValue;
using ValveResourceFormat.NavMesh;
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
    /// <summary>
    /// Exports Valve resources to glTF 2.0 format.
    /// </summary>
    public partial class GltfModelExporter
    {
        // https://github.com/KhronosGroup/glTF-Blender-IO/blob/6b29ca135d5255dbfe1dd72424ce7243be73c0be/addons/io_scene_gltf2/blender/com/conversion.py#L20
        private const float PbrWattsTolumens = 683;

        /// <summary>
        /// Gets or sets the progress reporter for export operations.
        /// </summary>
        public required IProgress<string> ProgressReporter { get; set; }

        /// <summary>
        /// Gets the file loader for loading referenced resources.
        /// </summary>
        public IFileLoader FileLoader { get; }
        private readonly ShaderDataProvider shaderDataProvider;
        private readonly BasicShaderDataProvider shaderDataProviderFallback = new();

        /// <summary>
        /// Gets or sets a value indicating whether to export animations.
        /// </summary>
        public bool ExportAnimations { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to export materials.
        /// </summary>
        public bool ExportMaterials { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to adapt textures for glTF compatibility.
        /// </summary>
        public bool AdaptTextures { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to save satellite images separately.
        /// </summary>
        public bool SatelliteImages { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether to export extra data.
        /// </summary>
        public bool ExportExtras { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether additive animations are composed over the bind pose.
        /// When false they are exported as the delta tracks they are, flagged in the animation's extras
        /// for the consumer to compose.
        /// </summary>
        public bool ComposeAdditiveAnimations { get; set; }

        /// <summary>
        /// Gets the set of animation names to filter during export. An entry matches an animation by its
        /// full name or, for animation graph clips named by resource path, by its leaf name (e.g. "idle_knife"
        /// matches "animation/anims/.../idle_knife"). Empty means export every animation.
        /// </summary>
        public HashSet<string> AnimationFilter { get; } = [];

        // Filter entries that matched at least one animation, so unmatched ones can be reported after export.
        private readonly HashSet<string> matchedAnimationFilter = [];

        /// <summary>
        /// Gets the set of mesh names to filter during export.
        /// The filter does not apply when exporting a vmesh resource.
        /// </summary>
        public HashSet<string> MeshFilter { get; } = [];

        private string DstDir = string.Empty;
        private CancellationToken CancellationToken;
        private Vector2 LightmapUvScale = Vector2.One;
        private readonly Dictionary<string, Mesh> ExportedMeshes = [];
        private readonly List<(PhysAggregateData Phys, string? Classname, Matrix4x4 Transform)> PhysicsToExport = [];
        private bool IsExporting;

        /// <summary>
        /// Initializes a new instance of the <see cref="GltfModelExporter"/> class.
        /// </summary>
        public GltfModelExporter(IFileLoader fileLoader)
        {
            ArgumentNullException.ThrowIfNull(fileLoader, nameof(fileLoader));
            FileLoader = fileLoader;
            shaderDataProvider = new ShaderDataProvider(fileLoader);
        }

        /// <summary>
        /// Determines whether the specified resource can be exported to glTF.
        /// </summary>
        public static bool CanExport(Resource resource) => resource.ResourceType
            is ResourceType.Mesh
            or ResourceType.Model
            or ResourceType.NmClip
            or ResourceType.NmSkeleton
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

        private void RunExport(Action exportAction, string? targetPath, CancellationToken cancellationToken)
        {
            if (IsExporting)
            {
                throw new InvalidOperationException($"{nameof(GltfModelExporter)} does not support multi threaded exporting, do not call Export while another export is in progress.");
            }

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
                exportAction();
                ReportUnmatchedAnimationFilter();
            }
            finally
            {
                matchedAnimationFilter.Clear();
                ExportedMeshes.Clear();
                LightmapUvScale = Vector2.One;
                MaterialInputSignatures.Clear();
                ScaledLightmapUvAccessors.Clear();
                PhysicsToExport.Clear();
                TextureExportingTasks.Clear();
                ExportedTextures.Clear();
                ExportedMaterials.Clear();
                TextureSampler = null;
                TexturesExportedSoFar = 0;
                DstDir = string.Empty;
                IsExporting = false;
            }
        }

        // Whether an animation passes AnimationFilter, matching on its full name or, for path-named graph
        // clips, its leaf name. Records which filter entry matched so misses can be reported afterwards.
        private bool IncludeAnimation(HashSet<string> filter, string name)
        {
            if (filter.Count == 0)
            {
                return true;
            }

            if (filter.Contains(name))
            {
                matchedAnimationFilter.Add(name);
                return true;
            }

            var leafName = Path.GetFileName(name);
            if (leafName.Length != name.Length && filter.Contains(leafName))
            {
                matchedAnimationFilter.Add(leafName);
                return true;
            }

            return false;
        }

        private void ReportUnmatchedAnimationFilter()
        {
            if (AnimationFilter.Count == 0)
            {
                return;
            }

            var unmatched = AnimationFilter.Where(name => !matchedAnimationFilter.Contains(name)).ToList();
            if (unmatched.Count > 0)
            {
                ProgressReporter?.Report($"glTF animation filter matched no animations for: {string.Join(", ", unmatched)}");
            }
        }

        /// <summary>
        /// Export a Valve resource to glTF.
        /// </summary>
        /// <param name="resource">The resource being exported.</param>
        /// <param name="targetPath">Target file name.</param>
        /// <param name="cancellationToken">Optional task cancellation token</param>
        public void Export(Resource resource, string? targetPath, CancellationToken cancellationToken = default)
        {
            Debug.Assert(resource.FileName != null);

            RunExport(() =>
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
                    case ResourceType.NmSkeleton:
                        ExportSkeletonToFile(resource.FileName, targetPath, Skeleton.FromSkeletonData(((BinaryKV3)resource.DataBlock!).Data));
                        break;
                    default:
                        throw new ArgumentException($"{resource.ResourceType} not supported for gltf export");
                }
            }, targetPath, cancellationToken);
        }

        /// <summary>
        /// Export a navigation mesh to glTF.
        /// </summary>
        /// <param name="navMesh">The navigation mesh to export.</param>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="targetPath">Target file name.</param>
        /// <param name="cancellationToken">Optional task cancellation token.</param>
        public void Export(NavMeshFile navMesh, string resourceName, string? targetPath, CancellationToken cancellationToken = default)
        {
            RunExport(() =>
            {
                var exportedModel = BuildNavMeshModel(resourceName, navMesh);
                WriteModelFile(exportedModel, targetPath);
            }, targetPath, cancellationToken);
        }

        /// <summary>
        /// Export a navigation mesh to a GLB stream.
        /// </summary>
        /// <param name="navMesh">The navigation mesh to export.</param>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="stream">Target stream to write GLB data to.</param>
        /// <param name="cancellationToken">Optional task cancellation token.</param>
        public void Export(NavMeshFile navMesh, string resourceName, Stream stream, CancellationToken cancellationToken = default)
        {
            RunExport(() =>
            {
                var exportedModel = BuildNavMeshModel(resourceName, navMesh);
                WriteModelFile(exportedModel, null, stream);
            }, null, cancellationToken);
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

            void ExportAnimationClip(VAnimationClip clip)
            {
                var skeletonData = Skeleton.FromSkeletonResource(FileLoader, clip.SkeletonName)
                    ?? throw new InvalidOperationException($"Unable to load skeleton data '{clip.SkeletonName}'.");

                var (skeletonNode, joints) = CreateGltfSkeleton(scene, skeletonData, clip.SkeletonName);
                if (skeletonNode == null || joints == null)
                {
                    throw new InvalidDataException($"Failure creating glTF skeleton for '{clip.SkeletonName}'.");
                }

                // Create a skeleton visualization mesh so importers recognize this as a proper skeleton
                var meshNode = CreateSkeletonVisualizationMesh(exportedModel, scene, skeletonData, joints);
                meshNode.Name = $"{clip.SkeletonName}.empty_mesh_reference";

                //if (ExportAnimations)
                {
                    var animation = new ResourceTypes.ModelAnimation.ClipAnimation(clip);
                    var animationWriter = new AnimationWriter(skeletonData, []) { ComposeAdditive = ComposeAdditiveAnimations };
                    animationWriter.WriteAnimation(exportedModel, joints, animation, ClipAnimationName(clip.Name));
                }
            }

            ExportAnimationClip(animationClip);

            foreach (var secondaryClip in animationClip.SecondaryAnimations)
            {
                ExportAnimationClip(secondaryClip);
            }

            WriteModelFile(exportedModel, fileName);
        }

        /// <summary>
        /// Export an Animgraph 2 skeleton to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="skeleton">The skeleton to export.</param>
        private void ExportSkeletonToFile(string resourceName, string? fileName, Skeleton skeleton)
        {
            var exportedModel = CreateModelRoot(resourceName, out var scene);

            var (skeletonNode, joints) = CreateGltfSkeleton(scene, skeleton, resourceName);
            if (skeletonNode == null || joints == null)
            {
                throw new InvalidDataException($"Failure creating glTF skeleton for '{resourceName}'.");
            }

            // Create a skeleton visualization mesh so importers recognize this as a proper skeleton
            var meshNode = CreateSkeletonVisualizationMesh(exportedModel, scene, skeleton, joints);
            meshNode.Name = $"{resourceName}.empty_mesh_reference";

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadModel(ModelRoot exportedModel, Scene scene, VModel model, string name,
            Matrix4x4 transform, Vector4 tintColor, string? skinName = null, EntityLump.Entity? entity = null)
        {
#if DEBUG
            ProgressReporter?.Report($"Loading model {name}");
#endif

            CancellationToken.ThrowIfCancellationRequested();

            var animationFilter = AnimationFilter;

            // When exporting map entities, only export the default animation
            if (entity != null)
            {
                var entityAnimation = entity.GetStringProperty("startinganim") ?? entity.GetStringProperty("defaultanim") ?? entity.GetStringProperty("idleanim");
                if (entityAnimation != null)
                {
                    animationFilter = [
                        entityAnimation,
                        $"@{entityAnimation}"
                    ];
                }
            }

            var meshes = LoadModelMeshes(model, name).ToList();

            // Animation frames are sized from the flex controllers, so they have to be known before the
            // animations are written. Reading them here lets the model's own morph block win; only a
            // model whose morph set sits in a separate vmorf falls back to the one its meshes carry.
            if (model.FlexControllers.Length == 0)
            {
                foreach (var m in meshes)
                {
                    m.Mesh.LoadExternalMorphData(FileLoader);
                    model.SetExternalMorphData(m.Mesh.MorphData);
                }
            }

            var (skeletonNode, joints) = ExportAnimations
                ? CreateGltfSkeleton(scene, model.Skeleton, name)
                : (null, null);

            if (skeletonNode != null)
            {
                Debug.Assert(joints != null);

                var animations = model.GetAllAnimations(FileLoader);
                var animationWriter = new AnimationWriter(model.Skeleton, model.FlexControllers) { ComposeAdditive = ComposeAdditiveAnimations };

                foreach (var animation in animations)
                {
                    // Clips authored on another skeleton are written retargeted by WriteAnimationGraphClips.
                    if (animation is ClipAnimation || !IncludeAnimation(animationFilter, animation.Name))
                    {
                        continue;
                    }

                    animationWriter.WriteAnimation(exportedModel, joints, animation);
                    CancellationToken.ThrowIfCancellationRequested();
                }

                WriteAnimationGraphClips(exportedModel, scene, model, joints!, animationFilter);
            }
            else
            {
                Debug.Assert(joints == null);
            }

            var nodeTransform = GetPlacementTransform(transform);

            var skinMaterialPath = skinName != null ? GetSkinPathFromModel(model, skinName) : null;

            var morphedMeshNodes = new List<(Node Node, VMesh Mesh)>();

            foreach (var m in meshes)
            {
                var meshName = m.Name;

                if (MeshFilter.Count > 0 && !MeshFilter.Contains(meshName.Split('.')[^1]))
                {
                    continue;
                }

                if (skinName != null)
                {
                    meshName = string.Concat(meshName, ".", skinName);
                }

                var boneRemapTable = model.GetRemapTable(m.MeshIndex);
                var node = AddMeshNode(exportedModel, scene, meshName, tintColor, m.Mesh, m.Mesh.VBIB, joints, out var meshNode, boneRemapTable, skinMaterialPath, entity);
                if (node != null)
                {
                    node.WorldMatrix = nodeTransform;

                    DebugValidateGLTF();
                }

                // meshNode is set even for skinned meshes, where the returned node is null.
                if (skeletonNode != null && meshNode != null && m.Mesh.MorphData?.FlexRules.Length > 0)
                {
                    morphedMeshNodes.Add((meshNode, m.Mesh));
                }
            }

            // Morph weights are written as a second pass over the animations once the mesh nodes exist.
            if (skeletonNode != null && morphedMeshNodes.Count > 0)
            {
                WriteMorphAnimations(exportedModel, model, morphedMeshNodes, animationFilter);
            }

            // Even though that's not documented, order matters.
            // WorldMatrix should only be set after everything else.
            if (skeletonNode != null)
            {
                skeletonNode.WorldMatrix = nodeTransform;
            }
        }

        /// <summary>
        /// Create a combined list of referenced and embedded meshes. Importantly retains the
        /// refMeshes order so it can be used for getting skeletons.
        /// </summary>
        /// <param name="model">The model to get the meshes from.</param>
        /// <param name="name">The base name used when generating mesh names.</param>
        /// <returns>An enumerable of tuples of mesh, mesh index, and name.</returns>
        private IEnumerable<(VMesh Mesh, int MeshIndex, string Name)> LoadModelMeshes(VModel model, string name)
        {
            // Export the lowest LoD level that actually has meshes. Usually LoD0, but some models leave it empty.
            var lowestLod = model.LodInfo.LowestLevel;

            foreach (var m in model.GetEmbeddedMeshesForLod(lowestLod))
            {
                yield return (m.Mesh, m.MeshIndex, string.Concat(name, ".", m.Name));
            }

            foreach (var m in model.GetReferenceMeshNamesForLod(lowestLod))
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

            var renderSkeleton = mesh.Data.GetSubCollection("m_skeleton");
            var skeleton = renderSkeleton != null ? Skeleton.FromRenderSkeleton(renderSkeleton) : null;
            var (_, joints) = skeleton != null ? CreateGltfSkeleton(scene, skeleton, name) : (null, null);

            // The mesh's own blend indices already address its render skeleton directly, so an identity
            // remap (rather than null) is what tells CreateGltfMesh to emit joint/weight vertex data.
            var identityRemapTable = joints != null ? Enumerable.Range(0, skeleton!.Bones.Length).ToArray() : null;

            AddMeshNode(exportedModel, scene, name, Vector4.One, mesh, mesh.VBIB, joints, meshNode: out _, identityRemapTable);

            WriteModelFile(exportedModel, fileName);
        }

        private Node? AddMeshNode(ModelRoot exportedModel, Scene scene, string name, Vector4 tintColor,
            VMesh mesh, Blocks.VBIB vbib, Node[]? joints, out Node? meshNode, int[]? boneRemapTable = null,
            string? skinMaterialPath = null, EntityLump.Entity? entity = null)
        {
            meshNode = null;

            if (mesh.Data.GetArray("m_sceneObjects").Count == 0)
            {
                return null;
            }

            var newNode = scene.CreateNode(name);
            meshNode = newNode;
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
                foreach (var (key, value) in entity.Children)
                {
                    exportedMesh.Extras[key] = value.ValueType == KVValueType.String ? (string)value : value.ToString();
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

        private void WriteModelFile(ModelRoot exportedModel, string? filePath, Stream? stream = null)
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
                MergeBuffers = false,
            };

            // Write GLB to a provided stream
            if (stream != null)
            {
                exportedModel.MergeBuffers();
                exportedModel.WriteGLB(stream, settings);
                return;
            }

            // If no file path is provided, validate the schema without writing a file
            if (filePath == null)
            {
                exportedModel.WriteGLB(Stream.Null, settings);
                return;
            }

            var isGLB = filePath.EndsWith(".glb", StringComparison.OrdinalIgnoreCase);

            // See https://github.com/KhronosGroup/glTF/blob/0bc36d536946b13c4807098f9cf62ddff738e7a5/specification/2.0/README.md#buffers-and-buffer-views
            // Disable merging buffers if the buffer size is >=2GiB, otherwise this will
            // cause SharpGLTF to run past the int32 limitation and crash.
            if (isGLB)
            {
                var totalSize = exportedModel.LogicalBuffers.Sum(buffer => (long)buffer.Content.Length);
                if (totalSize >= int.MaxValue)
                {
                    throw new NotSupportedException("VRF does not properly support big model (>=2GiB) exports yet due to glTF limitations. Try exporting as .gltf, not .glb.");
                }

                // binary glb must be a single buffer, which is limited to 2gib
                exportedModel.MergeBuffers();
            }
            else
            {
                // Split into 1gb buffer chunks for text gltf
                exportedModel.MergeBuffers(1_074_000_000);
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

        private static string ImageWriteCallback(WriteContext ctx, string uri, MemoryImage memoryImage)
        {
            // Since we've already dumped images to disk, skip glTF image write.
            return uri;
        }
    }
}
