//#define DEBUG_VALIDATE_GLTF

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using SharpGLTF.IO;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.Blocks.VBIB;
using Material = SharpGLTF.Schema2.Material;
using Mesh = SharpGLTF.Schema2.Mesh;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VModel = ValveResourceFormat.ResourceTypes.Model;
using VWorld = ValveResourceFormat.ResourceTypes.World;
using VWorldNode = ValveResourceFormat.ResourceTypes.WorldNode;

namespace ValveResourceFormat.IO
{
    public class GltfModelExporter
    {
        private const string GENERATOR = "VRF - https://vrf.steamdb.info/";

        private static readonly ISet<ResourceType> ResourceTypesThatAreGltfExportable = new HashSet<ResourceType>()
        {
            ResourceType.Mesh,
            ResourceType.Model,
            ResourceType.WorldNode,
            ResourceType.World
        };

        // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
        // Also divides by 100, gltf units are in meters, source engine units are in inches
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#coordinate-system-and-units
        private readonly Matrix4x4 TRANSFORMSOURCETOGLTF = Matrix4x4.CreateScale(0.0254f) * Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / -2f, (float)Math.PI / -2f);

        public IProgress<string> ProgressReporter { get; set; }
        public IFileLoader FileLoader { get; set; }
        public bool ExportMaterials { get; set; } = true;
        public bool AdaptTextures { get; set; } = true;
        public bool SatelliteImages { get; set; } = true;

        private string DstDir;
        private CancellationToken? CancellationToken;

        // In SatelliteImages mode, SharpGLTF will still load and validate images.
        // To save memory, we initiate MemoryImage with a a dummy image instead.
        private readonly byte[] dummyPng = new byte[] { 137, 80, 78, 71, 0, 0, 0, 0, 0, 0, 0, 0 };

        public static bool CanExport(Resource resource)
            => ResourceTypesThatAreGltfExportable.Contains(resource.ResourceType);

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
        public void Export(Resource resource, string targetPath, CancellationToken? cancellationToken)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Mesh:
                    ExportToFile(resource.FileName, targetPath, (VMesh)resource.DataBlock, cancellationToken);
                    break;
                case ResourceType.Model:
                    ExportToFile(resource.FileName, targetPath, (VModel)resource.DataBlock, cancellationToken);
                    break;
                case ResourceType.WorldNode:
                    ExportToFile(resource.FileName, targetPath, (VWorldNode)resource.DataBlock, cancellationToken);
                    break;
                case ResourceType.World:
                    ExportToFile(resource.FileName, targetPath, (VWorld)resource.DataBlock, cancellationToken);
                    break;
                default:
                    throw new ArgumentException($"{resource.ResourceType} not supported for gltf export");
            }
        }

        /// <summary>
        /// Export a Valve VWRLD to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="world">The world resource to export.</param>
        /// <param name="cancellationToken">Optional task cancellation token</param>
        public void ExportToFile(string resourceName, string fileName, VWorld world, CancellationToken? cancellationToken)
        {
            CancellationToken = cancellationToken;
            if (FileLoader == null)
            {
                throw new InvalidOperationException(nameof(FileLoader) + " must be set first.");
            }

            DstDir = Path.GetDirectoryName(fileName);
            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var loadedMeshDictionary = new Dictionary<string, Mesh>();

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
                var worldNodeModels = LoadWorldNodeModels(worldNode);

                foreach (var (Model, Name, Transform) in worldNodeModels)
                {
                    LoadModel(exportedModel, scene, Model, Name, Transform, loadedMeshDictionary);
                }
            }

            // Then the Entities
            foreach (var lumpName in world.GetEntityLumpNames())
            {
                if (lumpName == null)
                {
                    continue;
                }
                var entityLumpResource = FileLoader.LoadFile(lumpName + "_c");
                if (entityLumpResource == null)
                {
                    continue;
                }

                var entityLump = (VEntityLump)entityLumpResource.DataBlock;

                LoadEntityMeshes(exportedModel, scene, entityLump, loadedMeshDictionary);
            }

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadEntityMeshes(ModelRoot exportedModel, Scene scene, VEntityLump entityLump,
            Dictionary<string, Mesh> loadedMeshDictionary)
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

                var modelResource = FileLoader.LoadFile(modelName + "_c");
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
                    transform, loadedMeshDictionary, skinName);
            }

            foreach (var childEntityName in entityLump.GetChildEntityNames())
            {
                if (childEntityName == null)
                {
                    continue;
                }
                var childEntityLumpResource = FileLoader.LoadFile(childEntityName + "_c");
                if (childEntityLumpResource == null)
                {
                    continue;
                }

                var childEntityLump = (VEntityLump)childEntityLumpResource.DataBlock;
                LoadEntityMeshes(exportedModel, scene, childEntityLump, loadedMeshDictionary);
            }
        }

        private static string GetSkinPathFromModel(VModel model, string skinName)
        {
            var materialGroupForSkin = model.Data.GetArray<IKeyValueCollection>("m_materialGroups")
                .ToList()
                .SingleOrDefault(m => m.GetProperty<string>("m_name") == skinName);

            if (materialGroupForSkin == null)
            {
                return null;
            }

            // Given these are at the model level, and otherwise pull materials from drawcalls
            // on the mesh, not sure how they correlate if there's more than one here
            // So just take the first one and hope for the best
            return materialGroupForSkin.GetArray<string>("m_materials")[0];
        }

        /// <summary>
        /// Export a Valve VWNOD to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="worldNode">The worldNode resource to export.</param>
        /// <param name="cancellationToken">Optional task cancellation token</param>
        public void ExportToFile(string resourceName, string fileName, VWorldNode worldNode, CancellationToken? cancellationToken)
        {
            CancellationToken = cancellationToken;
            if (FileLoader == null)
            {
                throw new InvalidOperationException(nameof(FileLoader) + " must be set first.");
            }

            DstDir = Path.GetDirectoryName(fileName);
            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var worldNodeModels = LoadWorldNodeModels(worldNode);
            var loadedMeshDictionary = new Dictionary<string, Mesh>();

            foreach (var (Model, Name, Transform) in worldNodeModels)
            {
                LoadModel(exportedModel, scene, Model, Name, Transform, loadedMeshDictionary);
            }

            WriteModelFile(exportedModel, fileName);
        }

        private IList<(VModel Model, string ModelName, Matrix4x4 Transform)> LoadWorldNodeModels(VWorldNode worldNode)
        {
            var sceneObjects = worldNode.Data.GetArray("m_sceneObjects");
            var models = new List<(VModel, string, Matrix4x4)>();
            foreach (var sceneObject in sceneObjects)
            {
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");
                if (renderableModel == null)
                {
                    continue;
                }

                var modelResource = FileLoader.LoadFile(renderableModel + "_c");
                if (modelResource == null)
                {
                    continue;
                }

                var model = (VModel)modelResource.DataBlock;
                var matrix = sceneObject.GetArray("m_vTransform").ToMatrix4x4();

                models.Add((model, Path.GetFileNameWithoutExtension(renderableModel), matrix));
            }

            if (!worldNode.Data.ContainsKey("m_aggregateSceneObjects"))
            {
                return models;
            }

            var aggregateSceneObjects = worldNode.Data.GetArray("m_aggregateSceneObjects");
            foreach (var sceneObject in aggregateSceneObjects)
            {
                var renderableModel = sceneObject.GetProperty<string>("m_renderableModel");

                if (renderableModel != null)
                {
                    var modelResource = FileLoader.LoadFile(renderableModel + "_c");

                    if (modelResource == null)
                    {
                        continue;
                    }

                    var model = (VModel)modelResource.DataBlock;
                    models.Add((model, Path.GetFileNameWithoutExtension(renderableModel), Matrix4x4.Identity));
                }
            }

            return models;
        }

        /// <summary>
        /// Export a Valve VMDL to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="model">The model resource to export.</param>
        /// <param name="cancellationToken">Optional task cancellation token</param>
        public void ExportToFile(string resourceName, string fileName, VModel model, CancellationToken? cancellationToken)
        {
            CancellationToken = cancellationToken;
            if (FileLoader == null)
            {
                throw new InvalidOperationException(nameof(FileLoader) + " must be set first.");
            }

            DstDir = Path.GetDirectoryName(fileName);

            var exportedModel = CreateModelRoot(resourceName, out var scene);

            // Add meshes and their skeletons
            var loadedMeshDictionary = new Dictionary<string, Mesh>();
            LoadModel(exportedModel, scene, model, resourceName, Matrix4x4.Identity, loadedMeshDictionary);

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadModel(ModelRoot exportedModel, Scene scene, VModel model, string name,
            Matrix4x4 transform, IDictionary<string, Mesh> loadedMeshDictionary, string skinName = null)
        {
            CancellationToken?.ThrowIfCancellationRequested();
            var (skeletonNode, joints) = CreateGltfSkeleton(scene, model.Skeleton, name);

            if (skeletonNode != null)
            {
                var animations = model.GetAllAnimations(FileLoader);
                // Add animations
                var frame = new Frame(model.Skeleton);
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

                foreach (var animation in animations)
                {
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
                        animation.DecodeFrame(frameIndex, frame);
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
                            var scaleVec = boneFrame.Scale * Vector3.One;
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

                var node = AddMeshNode(exportedModel, scene, meshName,
                    m.Mesh, joints, loadedMeshDictionary, skinMaterialPath,
                    model, m.MeshIndex);
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
                    var meshResource = FileLoader.LoadFile(m.MeshName + "_c");
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
        /// <param name="cancellationToken">Optional task cancellation token</param>
        public void ExportToFile(string resourceName, string fileName, VMesh mesh, CancellationToken? cancellationToken)
        {
            CancellationToken = cancellationToken;
            DstDir = Path.GetDirectoryName(fileName);

            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var name = Path.GetFileName(resourceName);
            var loadedMeshDictionary = new Dictionary<string, Mesh>();
            var node = AddMeshNode(exportedModel, scene, name, mesh, null, loadedMeshDictionary);

            if (node != null)
            {
                // Swap Rotate upright, scale inches to meters.
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }

            WriteModelFile(exportedModel, fileName);
        }

        private Node AddMeshNode(ModelRoot exportedModel, Scene scene, string name,
            VMesh mesh, Node[] joints, IDictionary<string, Mesh> loadedMeshDictionary,
            string skinMaterialPath = null, VModel model = null, int meshIndex = 0)
        {
            if (mesh.Data.GetArray("m_sceneObjects").Length == 0)
            {
                return null;
            }

            var newNode = scene.CreateNode(name);
            if (loadedMeshDictionary.TryGetValue(name, out var existingMesh))
            {
                // Make a new node that uses the existing mesh
                newNode.Mesh = existingMesh;
                return newNode;
            }

            var hasJoints = joints != null;
            var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, hasJoints, skinMaterialPath, model, meshIndex);
            loadedMeshDictionary.Add(name, exportedMesh);
            var hasVertexJoints = exportedMesh.Primitives.All(primitive => primitive.GetVertexAccessor("JOINTS_0") != null);

            if (!hasJoints || !hasVertexJoints)
            {
                return newNode.WithMesh(exportedMesh);
            }

            newNode.WithSkinnedMesh(exportedMesh, Matrix4x4.Identity, joints);
            // WorldMatrix is set only once on skeletonNode
            return null;
        }

        private static ModelRoot CreateModelRoot(string resourceName, out Scene scene)
        {
            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = GENERATOR;
            scene = exportedModel.UseScene(Path.GetFileName(resourceName));

#if DEBUG_VALIDATE_GLTF
            debugCurrentExportedModel = exportedModel;
#endif

            return exportedModel;
        }

        private void WriteModelFile(ModelRoot exportedModel, string filePath)
        {
            ProgressReporter?.Report("Writing model to file...");

            var settings = new WriteSettings
            {
                ImageWriting = SatelliteImages ? ResourceWriteMode.SatelliteFile : ResourceWriteMode.BufferView,
                ImageWriteCallback = ImageWriteCallback,
                JsonIndented = true,
                MergeBuffers = true
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
            if (totalSize > int.MaxValue)
            {
                throw new NotSupportedException("VRF does not properly support big model (>=2GiB) exports yet due to glTF limitations. See https://github.com/SteamDatabase/ValveResourceFormat/issues/379");
            }

            exportedModel.Save(filePath, settings);
        }

        private Mesh CreateGltfMesh(string meshName, VMesh vmesh, ModelRoot exportedModel, bool includeJoints,
            string skinMaterialPath, VModel model, int meshIndex)
        {
            ProgressReporter?.Report($"Creating mesh: {meshName}");

            var data = vmesh.Data;
            var vbib = vmesh.VBIB;
            if (model != null)
            {
                vbib = model.RemapBoneIndices(vbib, meshIndex);
            }

            var mesh = exportedModel.CreateMesh(meshName);
            mesh.Name = meshName;

            vmesh.LoadExternalMorphData(FileLoader);

            var vertexBufferAccessors = vbib.VertexBuffers.Select((vertexBuffer, vertexBufferIndex) =>
            {
                var accessors = new Dictionary<string, Accessor>();

                if (vertexBuffer.ElementCount == 0)
                {
                    return accessors;
                }

                // Avoid duplicate attribute names
                var attributeCounters = new Dictionary<string, int>();

                // Set vertex attributes
                var actualJointsCount = 0;
                foreach (var attribute in vertexBuffer.InputLayoutFields)
                {
                    attributeCounters.TryGetValue(attribute.SemanticName, out var attributeCounter);
                    attributeCounters[attribute.SemanticName] = attributeCounter + 1;
                    var accessorName = GetAccessorName(attribute.SemanticName, attributeCounter);

                    if (accessorName == null)
                    {
                        continue;
                    }

                    var buffer = ReadAttributeBuffer(vertexBuffer, attribute);
                    var numComponents = buffer.Length / (int)vertexBuffer.ElementCount;

                    if (accessorName == "JOINTS_0")
                    {
                        actualJointsCount = numComponents;
                    }

                    if (attribute.SemanticName == "BLENDINDICES")
                    {
                        if (!includeJoints)
                        {
                            continue;
                        }

                        var ushortBuffer = buffer.Select(f => (ushort)f).ToArray();
                        if (numComponents != 4)
                        {
                            ushortBuffer = ChangeBufferStride(ushortBuffer, numComponents, 4);
                        }

                        BufferView bufferView = exportedModel.CreateBufferView(2 * ushortBuffer.Length, 0, BufferMode.ARRAY_BUFFER);
                        ushortBuffer.CopyTo(MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)bufferView.Content).Span));
                        var accessor = mesh.LogicalParent.CreateAccessor();
                        accessor.SetVertexData(bufferView, 0, ushortBuffer.Length / 4, DimensionType.VEC4, EncodingType.UNSIGNED_SHORT);
                        accessors[accessorName] = accessor;

                        continue;
                    }

                    if (attribute.SemanticName == "NORMAL")
                    {
                        var isCompressedNormalTangent = data.GetArray("m_sceneObjects").Any(sceneObject =>
                        {
                            return sceneObject.GetArray("m_drawCalls").Any(drawCall =>
                            {
                                var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0];
                                return vertexBufferInfo.GetInt32Property("m_hBuffer") == vertexBufferIndex
                                    && VMesh.IsCompressedNormalTangent(drawCall);
                            });
                        });

                        if (isCompressedNormalTangent)
                        {
                            var vectors = ToVector4Array(buffer);
                            var (normals, tangents) = DecompressNormalTangents(vectors);

                            {
                                BufferView bufferView = exportedModel.CreateBufferView(12 * normals.Length, 0, BufferMode.ARRAY_BUFFER);
                                new Vector3Array(bufferView.Content).Fill(normals);
                                Accessor accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, normals.Length, DimensionType.VEC3);
                                accessors["NORMAL"] = accessor;
                            }

                            {
                                BufferView bufferView = exportedModel.CreateBufferView(16 * tangents.Length, 0, BufferMode.ARRAY_BUFFER);
                                new Vector4Array(bufferView.Content).Fill(tangents);
                                Accessor accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, tangents.Length, DimensionType.VEC4);
                                accessors["TANGENT"] = accessor;
                            }

                            continue;
                        }
                    }

                    if (attribute.SemanticName == "TEXCOORD" && numComponents != 2)
                    {
                        // We are ignoring some data, but non-2-component UVs cause failures in gltf consumers
                        continue;
                    }

                    if (attribute.SemanticName == "BLENDWEIGHT" && numComponents != 4)
                    {
                        buffer = ChangeBufferStride(buffer, numComponents, 4);
                        numComponents = 4;
                    }

                    switch (numComponents)
                    {
                        case 4:
                            {
                                var vectors = ToVector4Array(buffer);

                                // dropship.vmdl in HL:A has a tanget with value of <0, -0, 0>
                                if (attribute.SemanticName == "NORMAL" || attribute.SemanticName == "TANGENT")
                                {
                                    vectors = FixZeroLengthVectors(vectors);
                                }

                                BufferView bufferView = exportedModel.CreateBufferView(16 * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
                                new Vector4Array(bufferView.Content).Fill(vectors);
                                Accessor accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, vectors.Length, DimensionType.VEC4);
                                accessors[accessorName] = accessor;
                                break;
                            }

                        case 3:
                            {
                                var vectors = ToVector3Array(buffer);

                                // dropship.vmdl in HL:A has a normal with value of <0, 0, 0>
                                if (attribute.SemanticName == "NORMAL" || attribute.SemanticName == "TANGENT")
                                {
                                    vectors = FixZeroLengthVectors(vectors);
                                }

                                BufferView bufferView = exportedModel.CreateBufferView(12 * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
                                new Vector3Array(bufferView.Content).Fill(vectors);
                                Accessor accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, vectors.Length, DimensionType.VEC3);
                                accessors[accessorName] = accessor;
                                break;
                            }

                        case 2:
                            {
                                var vectors = ToVector2Array(buffer);
                                BufferView bufferView = exportedModel.CreateBufferView(8 * vectors.Length, 0, BufferMode.ARRAY_BUFFER);
                                new Vector2Array(bufferView.Content).Fill(vectors);
                                Accessor accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, vectors.Length, DimensionType.VEC2);
                                accessors[accessorName] = accessor;
                                break;
                            }

                        case 1:
                            {
                                BufferView bufferView = exportedModel.CreateBufferView(4 * buffer.Length, 0, BufferMode.ARRAY_BUFFER);
                                new ScalarArray(bufferView.Content).Fill(buffer);
                                Accessor accessor = exportedModel.CreateAccessor();
                                accessor.SetVertexData(bufferView, 0, buffer.Length, DimensionType.SCALAR);
                                accessors[accessorName] = accessor;
                                break;
                            }

                        default:
                            throw new NotImplementedException($"Attribute \"{attribute.SemanticName}\" has {numComponents} components");
                    }
                }

                // For some reason soruce models can have joints but no weights, check if that is the case
                if (accessors.TryGetValue("JOINTS_0", out var jointAccessor))
                {
                    if (!accessors.TryGetValue("WEIGHTS_0", out var weightsAccessor))
                    {
                        // If this occurs, give default weights
                        var baseWeight = 1f / actualJointsCount;
                        var baseWeights = new Vector4(
                            actualJointsCount > 0 ? baseWeight : 0,
                            actualJointsCount > 1 ? baseWeight : 0,
                            actualJointsCount > 2 ? baseWeight : 0,
                            actualJointsCount > 3 ? baseWeight : 0
                        );
                        var defaultWeights = Enumerable.Repeat(baseWeights, jointAccessor.Count).ToList();

                        BufferView bufferView = exportedModel.CreateBufferView(16 * defaultWeights.Count, 0, BufferMode.ARRAY_BUFFER);
                        new Vector4Array(bufferView.Content).Fill(defaultWeights);
                        weightsAccessor = exportedModel.CreateAccessor();
                        weightsAccessor.SetVertexData(bufferView, 0, defaultWeights.Count, DimensionType.VEC4);
                        accessors["WEIGHTS_0"] = weightsAccessor;
                    }

                    var joints = MemoryMarshal.Cast<byte, ushort>(((Memory<byte>)jointAccessor.SourceBufferView.Content).Span);
                    var weights = MemoryMarshal.Cast<byte, float>(((Memory<byte>)weightsAccessor.SourceBufferView.Content).Span);

                    for (var i = 0; i < joints.Length; i += 4)
                    {
                        // remove joints without weights
                        for (var j = 0; j < 4; j++)
                        {
                            if (weights[i + j] == 0)
                            {
                                joints[i + j] = 0;
                            }
                        }

                        // remove duplicate joints
                        for (var j = 2; j >= 0; j--)
                        {
                            for (var k = 3; k > j; k--)
                            {
                                if (joints[i + j] == joints[i + k])
                                {
                                    for (var l = k; l < 3; l++)
                                    {
                                        joints[i + l] = joints[i + l + 1];
                                    }
                                    joints[i + 3] = 0;

                                    weights[i + j] += weights[i + k];
                                    for (var l = k; l < 3; l++)
                                    {
                                        weights[i + l] = weights[i + l + 1];
                                    }
                                    weights[i + 3] = 0;
                                }
                            }
                        }
                    }

                    jointAccessor.UpdateBounds();
                    weightsAccessor.UpdateBounds();
                }

                return accessors;
            }).ToArray();

            foreach (var sceneObject in data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                {
                    CancellationToken?.ThrowIfCancellationRequested();
                    var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                    var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

                    var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                    var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
                    var indexBuffer = vbib.IndexBuffers[indexBufferIndex];

                    // Create one primitive per draw call
                    var primitive = mesh.CreatePrimitive();

                    foreach (var (attributeKey, accessor) in vertexBufferAccessors[vertexBufferIndex])
                    {
                        primitive.SetVertexAccessor(attributeKey, accessor);

                        DebugValidateGLTF();
                    }

                    // Set index buffer
                    var baseVertex = drawCall.GetInt32Property("m_nBaseVertex");
                    var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                    var indexCount = drawCall.GetInt32Property("m_nIndexCount");
                    var indices = ReadIndices(indexBuffer, startIndex, indexCount, baseVertex);

                    var primitiveType = drawCall.GetProperty<object>("m_nPrimitiveType") switch
                    {
                        string primitiveTypeString => primitiveTypeString,
                        byte primitiveTypeByte =>
                        (primitiveTypeByte == 5) ? "RENDER_PRIM_TRIANGLES" : ("UNKNOWN_" + primitiveTypeByte),
                        _ => throw new NotImplementedException("Unknown PrimitiveType in drawCall!")
                    };

                    switch (primitiveType)
                    {
                        case "RENDER_PRIM_TRIANGLES":
                            primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, indices);
                            break;
                        default:
                            throw new NotImplementedException("Unknown PrimitiveType in drawCall! (" + primitiveType + ")");
                    }

                    if (vmesh.MorphData != null && vmesh.MorphData.FlexData != null)
                    {
                        var vertexCount = drawCall.GetInt32Property("m_nVertexCount");
                        AddMorphTargetsToPrimitive(vmesh.MorphData, primitive, exportedModel, baseVertex, vertexCount);
                    }

                    DebugValidateGLTF();

                    // Add material
                    if (!ExportMaterials)
                    {
                        continue;
                    }

                    var materialPath = skinMaterialPath ?? drawCall.GetProperty<string>("m_material") ?? drawCall.GetProperty<string>("m_pMaterial");

                    var materialNameTrimmed = Path.GetFileNameWithoutExtension(materialPath);

                    // Check if material already exists - makes an assumption that if material has the same name it is a duplicate
                    var existingMaterial = exportedModel.LogicalMaterials.SingleOrDefault(m => m.Name == materialNameTrimmed);
                    if (existingMaterial != null)
                    {
                        ProgressReporter?.Report($"Found existing material: {materialNameTrimmed}");
                        primitive.Material = existingMaterial;
                        continue;
                    }

                    ProgressReporter?.Report($"Loading material: {materialPath}");

                    var materialResource = FileLoader.LoadFile(materialPath + "_c");

                    if (materialResource == null)
                    {
                        continue;
                    }

                    var renderMaterial = (VMaterial)materialResource.DataBlock;
                    var bestMaterial = GenerateGLTFMaterialFromRenderMaterial(renderMaterial, exportedModel,
                        materialNameTrimmed);
                    primitive.WithMaterial(bestMaterial);
                }
            }

            return mesh;
        }

        private (Node skeletonNode, Node[] joints) CreateGltfSkeleton(Scene scene, Skeleton skeleton, string modelName)
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

        private void CreateBonesRecursive(Bone bone, Node parent, ref Node[] joints)
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

        private static void AddMorphTargetsToPrimitive(Morph morph, MeshPrimitive primitive, ModelRoot model, int vertexIndex, int vertexCount)
        {
            var morphIndex = 0;

            foreach (var pair in morph.FlexData)
            {
                var dict = new Dictionary<string, Accessor>();

                var acc = model.CreateAccessor();
                acc.Name = pair.Key;

                var buffer = new List<byte>(vertexCount * sizeof(float) * 3);
                for (var i = vertexIndex; i < vertexCount + vertexIndex; i++)
                {
                    var position = pair.Value[i];
                    buffer.AddRange(BitConverter.GetBytes(position.X));
                    buffer.AddRange(BitConverter.GetBytes(position.Y));
                    buffer.AddRange(BitConverter.GetBytes(position.Z));
                }

                var buff = model.UseBufferView(buffer.ToArray(), 0, buffer.Count);
                acc.SetData(buff, 0, vertexCount, DimensionType.VEC3, EncodingType.FLOAT, false);
                dict.Add("POSITION", acc);

                if (dict.Any())
                {
                    primitive.SetMorphTargetAccessors(morphIndex++, dict);
                }
            }
        }

        private Material GenerateGLTFMaterialFromRenderMaterial(VMaterial renderMaterial, ModelRoot model,
            string materialName)
        {
            var material = model
                    .CreateMaterial(materialName)
                    .WithDefault();

            renderMaterial.IntParams.TryGetValue("F_TRANSLUCENT", out var isTranslucent);
            renderMaterial.IntParams.TryGetValue("F_ALPHA_TEST", out var isAlphaTest);

            if (renderMaterial.ShaderName == "vr_glass.vfx")
            {
                isTranslucent = 1;
            }

            material.Alpha = isTranslucent > 0 ? AlphaMode.BLEND : (isAlphaTest > 0 ? AlphaMode.MASK : AlphaMode.OPAQUE);
            if (isAlphaTest > 0 && renderMaterial.FloatParams.ContainsKey("g_flAlphaTestReference"))
            {
                material.AlphaCutoff = renderMaterial.FloatParams["g_flAlphaTestReference"];
            }

            if (renderMaterial.IntParams.TryGetValue("F_RENDER_BACKFACES", out var doubleSided)
                && doubleSided > 0)
            {
                material.DoubleSided = true;
            }

            if (renderMaterial.IntParams.GetValueOrDefault("F_UNLIT") > 0)
            {
                material.WithUnlit();
            }

            // assume non-metallic unless prompted
            float metalValue = 0;

            if (renderMaterial.FloatParams.TryGetValue("g_flMetalness", out var flMetalness))
            {
                metalValue = flMetalness;
            }

            var baseColor = Vector4.One;

            if (renderMaterial.VectorParams.TryGetValue("g_vColorTint", out var vColorTint))
            {
                baseColor = vColorTint;
                baseColor.W = 1; //Tint only affects color
            }

            material.WithPBRMetallicRoughness(baseColor, null, metallicFactor: metalValue);

            using var occlusionRoughnessMetal = new TextureExtract.TexturePacker { DefaultColor = new SkiaSharp.SKColor(255, 255, 0, 255) };
            var ormHasOcclusion = false;
            Image ormImage = null;

            var allGltfInputs = MaterialExtract.GltfTextureMappings.Values.SelectMany(x => x);
            var blendNameComparer = new MaterialExtract.LayeredTextureNameComparer(new HashSet<string>(allGltfInputs.Select(x => x.Item2)));
            var blendInputComparer = new MaterialExtract.ChannelMappingComparer(blendNameComparer);

            //share sampler for all textures
            var sampler = model.UseTextureSampler(TextureWrapMode.REPEAT, TextureWrapMode.REPEAT, TextureMipMapFilter.LINEAR_MIPMAP_LINEAR, TextureInterpolationFilter.LINEAR);

            void TrySetupTexture(string textureName, Resource textureResource, List<ValueTuple<MaterialExtract.Channel, string>> renderTextureInputs)
            {
                var ormFileName = Path.GetFileNameWithoutExtension(textureResource.FileName) + "_orm.png";
                ormImage = model.LogicalImages.SingleOrDefault(i => i.Name == ormFileName);

                // Pack occlusion into the ORM if possible
                if (AdaptTextures && blendInputComparer.Equals(renderTextureInputs[0], (MaterialExtract.Channel.R, "TextureAmbientOcclusion")))
                {
                    if (ormImage is null)
                    {
                        using var bitmap = ((ResourceTypes.Texture)textureResource.DataBlock).GenerateBitmap();
                        bitmap.SetImmutable();
                        using var pixels = bitmap.PeekPixels();
                        occlusionRoughnessMetal.Collect(pixels, ormFileName, MaterialExtract.Channel.R, MaterialExtract.Channel.R);
                        ormHasOcclusion = true;
                    }

                    return;
                }

                // Try to find a glTF entry that best matches this texture
                foreach (var (gltfTexture, gltfInputs) in MaterialExtract.GltfTextureMappings)
                {
                    if (!AdaptTextures)
                    {
                        if (!blendNameComparer.Equals(renderTextureInputs[0].Item2, gltfInputs[0].Item2))
                        {
                            continue;
                        }

                        WriteTexture(MaterialExtract.Channel.RGBA, gltfTexture, 0, 1);
                        break;
                    }

                    // Render texture matches the glTF spec.
                    if (Enumerable.SequenceEqual(renderTextureInputs, gltfInputs, blendInputComparer))
                    {
                        WriteTexture(MaterialExtract.Channel.RGBA, gltfTexture, 0, renderTextureInputs.Count);
                        break;
                    }

                    // RGB matches, alpha differs or missing, so write RGB.
                    if (gltfInputs[0].Item1 == MaterialExtract.Channel.RGB && blendInputComparer.Equals(renderTextureInputs[0], gltfInputs[0]))
                    {
                        var channel = renderTextureInputs.Count == 1
                            ? MaterialExtract.Channel.RGBA
                            : renderTextureInputs[0].Item1;

                        WriteTexture(channel, gltfTexture, 0, 1);
                        break;
                    }

                    // Render texture likely missing unpack info, otherwise texture types match.
                    if (renderTextureInputs[0].Item1 == MaterialExtract.Channel.RGBA && blendNameComparer.Equals(renderTextureInputs[0].Item2, gltfInputs[0].Item2))
                    {
                        var channel = gltfInputs[0].Item1 > MaterialExtract.Channel._Single
                            ? MaterialExtract.Channel.RGBA
                            : gltfInputs[0].Item1;

                        WriteTexture(channel, gltfTexture, 0, 1);
                        break;
                    }
                }

                void WriteTexture(MaterialExtract.Channel channel, string gltfBestMatch, int index, int count)
                {
                    renderTextureInputs.RemoveRange(index, count);
                    var fileName = Path.GetFileName(textureResource.FileName);
                    var image = model.LogicalImages.SingleOrDefault(i => i.Name == fileName);
                    if (image is null)
                    {
                        using var bitmap = ((ResourceTypes.Texture)textureResource.DataBlock).GenerateBitmap();
                        bitmap.SetImmutable();
                        image = LinkAndStoreImage(channel, bitmap, model, fileName);
                        CollectRemainingChannels(bitmap);
                    }

                    var tex = model.UseTexture(image, sampler);
                    tex.Name = $"{fileName}_{model.LogicalTextures.Count - 1}";

                    material.FindChannel(gltfBestMatch)?.SetTexture(0, tex);

                    if (gltfBestMatch == "BaseColor")
                    {
                        material.Extras = JsonContent.CreateFrom(new Dictionary<string, object>
                        {
                            ["baseColorTexture"] = new Dictionary<string, object>
                            {
                                { "index", image.LogicalIndex },
                            },
                        });
                    }
                }

                void CollectRemainingChannels(SkiaSharp.SKBitmap bitmap)
                {
                    if (!AdaptTextures || ormImage is not null)
                    {
                        return;
                    }

                    // Collect any leftover channel/maps to new images
                    using var pixels = bitmap.PeekPixels();
                    foreach (var (leftoverChannel, textureType) in renderTextureInputs)
                    {
                        if (blendNameComparer.Equals(textureType, "TextureRoughness"))
                        {
                            occlusionRoughnessMetal.Collect(pixels, ormFileName, leftoverChannel, MaterialExtract.Channel.G);
                        }
                        else if (blendNameComparer.Equals(textureType, "TextureSpecularMask"))
                        {
                            occlusionRoughnessMetal.Collect(pixels, ormFileName, leftoverChannel, MaterialExtract.Channel.G, invert: true);
                        }
                        else if (blendNameComparer.Equals(textureType, "TextureMetalness") || blendNameComparer.Equals(textureType, "TextureMetalnessMask"))
                        {
                            occlusionRoughnessMetal.Collect(pixels, ormFileName, leftoverChannel, MaterialExtract.Channel.B);
                        }
                    }
                }
            }

            foreach (var renderTexture in renderMaterial.TextureParams)
            {
                CancellationToken?.ThrowIfCancellationRequested();
                var texturePath = renderTexture.Value;
                var textureResource = FileLoader.LoadFile(texturePath + "_c");

                if (textureResource == null)
                {
                    continue;
                }

                var inputImages = MaterialExtract.GetTextureInputs(renderMaterial.ShaderName, renderTexture.Key, renderMaterial.IntParams).ToList();

                // Preemptive check so as to not perform any unnecessary GenerateBitmap
                if (inputImages.Count == 0 || !inputImages.Any(input => allGltfInputs.Any(gltfInput => blendNameComparer.Equals(input.Item2, gltfInput.Item2))))
                {
                    continue;
                }

                TrySetupTexture(renderTexture.Key, textureResource, inputImages);
            }

            if (ormImage is null && occlusionRoughnessMetal.FileName is not null)
            {
                ormImage = LinkAndStoreImage(MaterialExtract.Channel.RGBA, occlusionRoughnessMetal.Bitmap, model, occlusionRoughnessMetal.FileName);
            }

            if (ormImage is not null)
            {
                var metallicRoughness = material.FindChannel("MetallicRoughness");
                var tex = model.UseTexture(ormImage, sampler);
                metallicRoughness?.SetTexture(0, tex);
                metallicRoughness?.SetFactor("MetallicFactor", 1.0f); // Ignore g_flMetalness

                if (ormHasOcclusion)
                {
                    material.FindChannel("Occlusion")?.SetTexture(0, tex);
                }
            }

            return material;
        }

        private Image LinkAndStoreImage(MaterialExtract.Channel channel, SkiaSharp.SKBitmap bitmap, ModelRoot model, string fileName)
        {
            Image image;
            ProgressReporter?.Report($"Exporting texture: {fileName}");

            if (!SatelliteImages)
            {
                image = model.CreateImage(fileName);
                image.Content = TextureExtract.ToPngImageChannels(bitmap, channel);
                return image;
            }

            image = model.CreateImage(fileName);
            image.Content = new SharpGLTF.Memory.MemoryImage(dummyPng);
            fileName = Path.ChangeExtension(fileName, "png");
            image.AlternateWriteFileName = fileName;

            var exportedTexturePath = Path.Join(DstDir, fileName);
            using (var fs = File.Open(exportedTexturePath, FileMode.Create))
            {
                fs.Write(TextureExtract.ToPngImageChannels(bitmap, channel));
            }

            return image;
        }

        private static string ImageWriteCallback(WriteContext ctx, string uri, SharpGLTF.Memory.MemoryImage memoryImage)
        {
            // Since we've already dumped images to disk, skip glTF image write.
            return uri;
        }

        public static string GetAccessorName(string name, int index)
        {
            if (index > 0 && name != "TEXCOORD" && name != "COLOR")
            {
                throw new NotImplementedException($"Got attribute \"{name}\" more than once, but that is not supported.");
            }

            switch (name)
            {
                case "TEXCOORD": return $"TEXCOORD_{index}";
                case "COLOR": return $"COLOR_{index}";
                case "POSITION": return "POSITION";
                case "NORMAL": return "NORMAL";
                case "TANGENT": return "TANGENT";
                case "BLENDINDICES": return "JOINTS_0";
                case "BLENDWEIGHT":
                case "BLENDWEIGHTS": return "WEIGHTS_0";
            };

            Console.Error.WriteLine($"Got unknown attribute \"{name}\" which was skipped.");

            return null;
        }

        private static float[] ReadAttributeBuffer(OnDiskBufferData buffer, RenderInputLayoutField attribute)
            => Enumerable.Range(0, (int)buffer.ElementCount)
                .SelectMany(i => VBIB.ReadVertexAttribute(i, buffer, attribute))
                .ToArray();

        private static int[] ReadIndices(OnDiskBufferData indexBuffer, int start, int count, int baseVertex)
        {
            var indices = new int[count];

            var byteCount = count * (int)indexBuffer.ElementSizeInBytes;
            var byteStart = start * (int)indexBuffer.ElementSizeInBytes;

            if (indexBuffer.ElementSizeInBytes == 4)
            {
                System.Buffer.BlockCopy(indexBuffer.Data, byteStart, indices, 0, byteCount);
                for (var i = 0; i < count; i++)
                {
                    indices[i] += baseVertex;
                }
            }
            else if (indexBuffer.ElementSizeInBytes == 2)
            {
                var shortIndices = new ushort[count];
                System.Buffer.BlockCopy(indexBuffer.Data, byteStart, shortIndices, 0, byteCount);
                indices = Array.ConvertAll(shortIndices, i => baseVertex + i);
            }

            return indices;
        }

        private static (Vector3[] Normals, Vector4[] Tangents) DecompressNormalTangents(Vector4[] compressedNormalsTangents)
        {
            var normals = new Vector3[compressedNormalsTangents.Length];
            var tangents = new Vector4[compressedNormalsTangents.Length];

            for (var i = 0; i < normals.Length; i++)
            {
                // Undo-normalization
                var compressedNormal = compressedNormalsTangents[i] * 255f;
                normals[i] = DecompressNormal(new Vector2(compressedNormal.X, compressedNormal.Y));
                tangents[i] = DecompressTangent(new Vector2(compressedNormal.Z, compressedNormal.W));
            }

            return (normals, tangents);
        }

        private static Vector3 DecompressNormal(Vector2 compressedNormal)
        {
            var inputNormal = compressedNormal;
            var outputNormal = Vector3.Zero;

            var x = inputNormal.X - 128.0f;
            var y = inputNormal.Y - 128.0f;
            float z;

            var zSignBit = x < 0 ? 1.0f : 0.0f;           // z and t negative bits (like slt asm instruction)
            var tSignBit = y < 0 ? 1.0f : 0.0f;
            var zSign = -((2 * zSignBit) - 1);          // z and t signs
            var tSign = -((2 * tSignBit) - 1);

            x = (x * zSign) - zSignBit;                           // 0..127
            y = (y * tSign) - tSignBit;
            x -= 64;                                     // -64..63
            y -= 64;

            var xSignBit = x < 0 ? 1.0f : 0.0f;   // x and y negative bits (like slt asm instruction)
            var ySignBit = y < 0 ? 1.0f : 0.0f;
            var xSign = -((2 * xSignBit) - 1);          // x and y signs
            var ySign = -((2 * ySignBit) - 1);

            x = ((x * xSign) - xSignBit) / 63.0f;             // 0..1 range
            y = ((y * ySign) - ySignBit) / 63.0f;
            z = 1.0f - x - y;

            var oolen = 1.0f / (float)Math.Sqrt((x * x) + (y * y) + (z * z));   // Normalize and
            x *= oolen * xSign;                 // Recover signs
            y *= oolen * ySign;
            z *= oolen * zSign;

            outputNormal.X = x;
            outputNormal.Y = y;
            outputNormal.Z = z;

            return outputNormal;
        }

        private static Vector4 DecompressTangent(Vector2 compressedTangent)
        {
            var outputNormal = DecompressNormal(compressedTangent);
            var tSign = compressedTangent.Y - 128.0f < 0 ? -1.0f : 1.0f;

            return new Vector4(outputNormal.X, outputNormal.Y, outputNormal.Z, tSign);
        }

        private static Vector3[] ToVector3Array(float[] buffer)
        {
            var vectorArray = new Vector3[buffer.Length / 3];

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector3(buffer[i * 3], buffer[(i * 3) + 1], buffer[(i * 3) + 2]);
            }

            return vectorArray;
        }

        private static Vector2[] ToVector2Array(float[] buffer)
        {
            var vectorArray = new Vector2[buffer.Length / 2];

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector2(buffer[i * 2], buffer[(i * 2) + 1]);
            }

            return vectorArray;
        }

        private static Vector4[] ToVector4Array(float[] buffer)
        {
            var vectorArray = new Vector4[buffer.Length / 4];

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector4(buffer[i * 4], buffer[(i * 4) + 1], buffer[(i * 4) + 2], buffer[(i * 4) + 3]);
            }

            return vectorArray;
        }

        // https://github.com/KhronosGroup/glTF-Validator/blob/master/lib/src/errors.dart
        private const float UnitLengthThresholdVec3 = 0.00674f;

        private static Vector4[] FixZeroLengthVectors(Vector4[] vectorArray)
        {
            for (var i = 0; i < vectorArray.Length; i++)
            {
                var vec = vectorArray[i];

                if (Math.Abs(new Vector3(vec.X, vec.Y, vec.Z).Length() - 1.0f) > UnitLengthThresholdVec3)
                {
                    vectorArray[i] = -Vector4.UnitZ;
                    vectorArray[i].W = vec.W;

                    Console.Error.WriteLine($"The exported model contains a non-zero unit vector which was replaced with {vectorArray[i]} for exporting purposes.");
                }
            }

            return vectorArray;
        }

        private static Vector3[] FixZeroLengthVectors(Vector3[] vectorArray)
        {
            for (var i = 0; i < vectorArray.Length; i++)
            {
                if (Math.Abs(vectorArray[i].Length() - 1.0f) > UnitLengthThresholdVec3)
                {
                    vectorArray[i] = -Vector3.UnitZ;

                    Console.Error.WriteLine($"The exported model contains a non-zero unit vector which was replaced with {vectorArray[i]} for exporting purposes.");
                }
            }

            return vectorArray;
        }

        private static T[] ChangeBufferStride<T>(T[] oldBuffer, int oldStride, int newStride)
        {
            return Enumerable.Range(0, (oldBuffer.Length / oldStride) * newStride)
                .Select(i =>
                {
                    var index = i % newStride;
                    if (index >= oldStride)
                    {
                        return default;
                    }
                    return oldBuffer[(i / newStride) * oldStride + index];
                })
                .ToArray();
        }
    }
}
