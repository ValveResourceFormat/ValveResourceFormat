using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.IO;
using SharpGLTF.Schema2;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;
using Material = SharpGLTF.Schema2.Material;
using Mesh = SharpGLTF.Schema2.Mesh;
using static ValveResourceFormat.Blocks.VBIB;
using VMaterial = ValveResourceFormat.ResourceTypes.Material;
using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
using VModel = ValveResourceFormat.ResourceTypes.Model;
using VWorldNode = ValveResourceFormat.ResourceTypes.WorldNode;
using VWorld = ValveResourceFormat.ResourceTypes.World;
using VEntityLump = ValveResourceFormat.ResourceTypes.EntityLump;

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

        private string DstDir;
        private readonly IDictionary<string, Node> LoadedUnskinnedMeshDictionary = new Dictionary<string, Node>();

        public static bool CanExport(Resource resource)
            => ResourceTypesThatAreGltfExportable.Contains(resource.ResourceType);

        public void Export(Resource resource, string targetPath)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Mesh:
                    ExportToFile(resource.FileName, targetPath, new VMesh(resource));
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
        public void ExportToFile(string resourceName, string fileName, VWorld world)
        {
            if (FileLoader == null)
            {
                throw new InvalidOperationException(nameof(FileLoader) + " must be set first.");
            }

            DstDir = Path.GetDirectoryName(fileName);
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
                var worldNodeModels = LoadWorldNodeModels(worldNode);

                foreach (var (Model, Name, Transform) in worldNodeModels)
                {
                    LoadModel(exportedModel, scene, Model, Name, Transform);
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

                LoadEntityMeshes(exportedModel, scene, entityLump);
            }

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
                LoadModel(exportedModel, scene, model, Path.GetFileNameWithoutExtension(modelName), transform, skinName);
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
                LoadEntityMeshes(exportedModel, scene, childEntityLump);
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
        public void ExportToFile(string resourceName, string fileName, VWorldNode worldNode)
        {
            if (FileLoader == null)
            {
                throw new InvalidOperationException(nameof(FileLoader) + " must be set first.");
            }

            DstDir = Path.GetDirectoryName(fileName);
            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var worldNodeModels = LoadWorldNodeModels(worldNode);

            foreach (var (Model, Name, Transform) in worldNodeModels)
            {
                LoadModel(exportedModel, scene, Model, Name, Transform);
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

            return models;
        }

        /// <summary>
        /// Export a Valve VMDL to GLTF.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="model">The model resource to export.</param>
        public void ExportToFile(string resourceName, string fileName, VModel model)
        {
            if (FileLoader == null)
            {
                throw new InvalidOperationException(nameof(FileLoader) + " must be set first.");
            }

            DstDir = Path.GetDirectoryName(fileName);

            var exportedModel = CreateModelRoot(resourceName, out var scene);

            // Add meshes and their skeletons
            LoadModel(exportedModel, scene, model, resourceName, Matrix4x4.Identity);

            WriteModelFile(exportedModel, fileName);
        }

        private void LoadModel(ModelRoot exportedModel, Scene scene, VModel model, string name, Matrix4x4 transform, string skinName = null)
        {
            var meshes = LoadModelMeshes(model);
            if (meshes.Length == 0)
            {
                return;
            }

            var modelNode = scene.CreateNode(name);
            var (boneNodes, skeletonNode) = CreateGltfSkeleton(modelNode, model);

            if (skeletonNode != null)
            {
                var animations = AnimationGroupLoader.GetAllAnimations(model, FileLoader);
                // Add animations
                foreach (var animation in animations)
                {
                    var exportedAnimation = exportedModel.CreateAnimation(animation.Name);
                    var rotationDict = new Dictionary<string, Dictionary<float, Quaternion>>();
                    var lastRotationDict = new Dictionary<string, Quaternion>();
                    var rotationOmittedSet = new HashSet<string>();
                    var translationDict = new Dictionary<string, Dictionary<float, Vector3>>();
                    var lastTranslationDict = new Dictionary<string, Vector3>();
                    var translationOmittedSet = new HashSet<string>();
                    var scaleDict = new Dictionary<string, Dictionary<float, Vector3>>();
                    var lastScaleDict = new Dictionary<string, Vector3>();
                    var scaleOmittedSet = new HashSet<string>();

                    for (var frameIndex = 0; frameIndex < animation.FrameCount; frameIndex++)
                    {
                        var time = frameIndex / (float)animation.Fps;
                        var prevFrameTime = (frameIndex - 1) / (float)animation.Fps;
                        foreach (var boneFrame in animation.Frames[frameIndex].Bones)
                        {
                            var bone = boneFrame.Key;
                            if (!rotationDict.ContainsKey(bone))
                            {
                                rotationDict[bone] = new Dictionary<float, Quaternion>();
                                translationDict[bone] = new Dictionary<float, Vector3>();
                                scaleDict[bone] = new Dictionary<float, Vector3>();
                            }

                            if (!lastRotationDict.TryGetValue(bone, out var lastRotation) || lastRotation != boneFrame.Value.Angle)
                            {
                                if (rotationOmittedSet.Remove(bone))
                                {
                                    // Restore keyframe before current frame, as otherwise interpolation will
                                    // begin from the first instance of identical frame, and not from previous frame
                                    rotationDict[bone].Add(prevFrameTime, lastRotation);
                                }
                                rotationDict[bone].Add(time, boneFrame.Value.Angle);
                                lastRotationDict[bone] = boneFrame.Value.Angle;
                            }
                            else
                            {
                                rotationOmittedSet.Add(bone);
                            }

                            if (!lastTranslationDict.TryGetValue(bone, out var lastTranslation) || lastTranslation != boneFrame.Value.Position)
                            {
                                if (translationOmittedSet.Remove(bone))
                                {
                                    // Restore keyframe before current frame, as otherwise interpolation will
                                    // begin from the first instance of identical frame, and not from previous frame
                                    translationDict[bone].Add(prevFrameTime, lastTranslation);
                                }
                                translationDict[bone].Add(time, boneFrame.Value.Position);
                                lastTranslationDict[bone] = boneFrame.Value.Position;
                            }
                            else
                            {
                                translationOmittedSet.Add(bone);
                            }

                            var scaleVec = new Vector3(boneFrame.Value.Scale, boneFrame.Value.Scale, boneFrame.Value.Scale);
                            if (!lastScaleDict.TryGetValue(bone, out var lastScale) || lastScale != scaleVec)
                            {
                                if (scaleOmittedSet.Remove(bone))
                                {
                                    // Restore keyframe before current frame, as otherwise interpolation will
                                    // begin from the first instance of identical frame, and not from previous frame
                                    scaleDict[bone].Add(prevFrameTime, lastScale);
                                }
                                scaleDict[bone].Add(time, scaleVec);
                                lastScaleDict[bone] = scaleVec;
                            }
                            else
                            {
                                scaleOmittedSet.Add(bone);
                            }
                        }
                    }

                    foreach (var bone in rotationDict.Keys)
                    {
                        var jointNode = boneNodes.GetValueOrDefault(bone);
                        if (jointNode != null)
                        {
                            exportedAnimation.CreateRotationChannel(jointNode, rotationDict[bone], true);
                            exportedAnimation.CreateTranslationChannel(jointNode, translationDict[bone], true);
                            exportedAnimation.CreateScaleChannel(jointNode, scaleDict[bone], true);
                        }
                    }
                }
            }

            var skinMaterialPath = skinName != null ? GetSkinPathFromModel(model, skinName) : null;
            for (var i = 0; i < meshes.Length; i++)
            {
                var meshName = meshes[i].Name;
                if (skinName != null)
                {
                    meshName += "." + skinName;
                }

                AddMeshNode(exportedModel, modelNode,
                    meshName, meshes[i].Mesh, model.GetSkeleton(i),
                    skinMaterialPath, boneNodes, skeletonNode);
            }

            // Even though that's not documented, order matters.
            // WorldMatrix should only be set after everything else.
            // Swap Rotate upright, scale inches to meters.
            modelNode.WorldMatrix = transform * TRANSFORMSOURCETOGLTF;
        }

        /// <summary>
        /// Create a combined list of referenced and embedded meshes. Importantly retains the
        /// refMeshes order so it can be used for getting skeletons.
        /// </summary>
        /// <param name="model">The model to get the meshes from.</param>
        /// <returns>A tuple of meshes and their names.</returns>
        private (VMesh Mesh, string Name)[] LoadModelMeshes(VModel model)
        {
            var embeddedMeshes = model.GetEmbeddedMeshesAndLoD()
                .Where(m => (m.LoDMask & 1) != 0)
                .Select((m, i) => (m.Mesh, $"Embedded.{i}"));

            var refMeshes = model.GetReferenceMeshNamesAndLoD()
                .Where(m => (m.LoDMask & 1) != 0)
                .Select(m =>
                {
                    // Load mesh from file
                    var meshResource = FileLoader.LoadFile(m.MeshName + "_c");
                    var nodeName = Path.GetFileNameWithoutExtension(m.MeshName);
                    if (meshResource == null)
                    {
                        return (null, nodeName);
                    }

                    var mesh = new VMesh(meshResource);
                    return (mesh, nodeName);
                })
                .Where(m => m.mesh != null);

            return embeddedMeshes.Concat(refMeshes).ToArray();
        }

        /// <summary>
        /// Export a Valve VMESH to Gltf.
        /// </summary>
        /// <param name="resourceName">The name of the resource being exported.</param>
        /// <param name="fileName">Target file name.</param>
        /// <param name="mesh">The mesh resource to export.</param>
        public void ExportToFile(string resourceName, string fileName, VMesh mesh)
        {
            DstDir = Path.GetDirectoryName(fileName);

            var exportedModel = CreateModelRoot(resourceName, out var scene);
            var name = Path.GetFileName(resourceName);
            var node = AddMeshNode(exportedModel, scene, name, mesh, null, null);

            if (node != null)
            {
                // Swap Rotate upright, scale inches to meters.
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }

            WriteModelFile(exportedModel, fileName);
        }

        private Node AddMeshNode(ModelRoot exportedModel, IVisualNodeContainer container, string name,
            VMesh mesh, Skeleton skeleton, string skinMaterialPath = null,
            Dictionary<string, Node> boneNodes = null, Node skeletonNode = null)
        {
            if (mesh.GetData().GetArray("m_sceneObjects").Length == 0)
            {
                return null;
            }

            if (LoadedUnskinnedMeshDictionary.TryGetValue(name, out var existingNode))
            {
                // Make a new node that uses the existing mesh
                var newNode = container.CreateNode(name);
                newNode.Mesh = existingNode.Mesh;
                return newNode;
            }

            var hasJoints = skeleton != null && skeleton.AnimationTextureSize > 0;
            var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, hasJoints, skinMaterialPath);
            var hasVertexJoints = exportedMesh.Primitives.All(primitive => primitive.GetVertexAccessor("JOINTS_0") != null);

            if (hasJoints && hasVertexJoints && skeleton != null)
            {
                var joints = GetGltfSkeletonJoints(skeleton, boneNodes, skeletonNode);

                container.CreateNode(name)
                    .WithSkinnedMesh(exportedMesh, Matrix4x4.Identity, joints);

                return null;
            }
            var node = container.CreateNode(name).WithMesh(exportedMesh);
            LoadedUnskinnedMeshDictionary.Add(name, node);
            return node;
        }

        private static ModelRoot CreateModelRoot(string resourceName, out Scene scene)
        {
            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = GENERATOR;
            scene = exportedModel.UseScene(Path.GetFileName(resourceName));

            return exportedModel;
        }

        private static void WriteModelFile(ModelRoot exportedModel, string filePath)
        {
            var settings = new WriteSettings
            {
                ImageWriting = ResourceWriteMode.SatelliteFile,
                ImageWriteCallback = ImageWriteCallback,
                JsonIndented = true,
                MergeBuffers = true
            };

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

        private static string ImageWriteCallback(WriteContext ctx, string uri, SharpGLTF.Memory.MemoryImage image)
        {
            if (File.Exists(image.SourcePath))
            {
                // image.SourcePath is an absolute path, we must make it relative to ctx.CurrentDirectory
                var currDir = ctx.CurrentDirectory.FullName;

                // if the shared texture can be reached by the model in its directory, reuse the texture.
                if (image.SourcePath.StartsWith(currDir, StringComparison.OrdinalIgnoreCase))
                {
                    // we've found the shared texture!, return the uri relative to the model:
                    return Path.GetFileName(image.SourcePath);
                }
            }

            // we were unable to reuse the shared texture,
            // default to write our own texture.
            image.SaveToFile(Path.Combine(ctx.CurrentDirectory.FullName, uri));

            return uri;
        }

        private Mesh CreateGltfMesh(string meshName, VMesh vmesh, ModelRoot model, bool includeJoints, string skinMaterialPath = null)
        {
            ProgressReporter?.Report($"Creating mesh: {meshName}");

            var data = vmesh.GetData();
            var vbib = vmesh.VBIB;

            var mesh = model.CreateMesh(meshName);
            mesh.Name = meshName;

            foreach (var sceneObject in data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                {
                    var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                    var vertexBufferIndex = (int)vertexBufferInfo.GetIntegerProperty("m_hBuffer");
                    var vertexBuffer = vbib.VertexBuffers[vertexBufferIndex];

                    var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                    var indexBufferIndex = (int)indexBufferInfo.GetIntegerProperty("m_hBuffer");
                    var indexBuffer = vbib.IndexBuffers[indexBufferIndex];

                    // Create one primitive per draw call
                    var primitive = mesh.CreatePrimitive();

                    // Avoid duplicate attribute names
                    var attributeCounters = new Dictionary<string, int>();

                    // Set vertex attributes
                    foreach (var attribute in vertexBuffer.InputLayoutFields)
                    {
                        attributeCounters.TryGetValue(attribute.SemanticName, out var attributeCounter);
                        attributeCounters[attribute.SemanticName] = attributeCounter + 1;
                        var accessorName = GetAccessorName(attribute.SemanticName, attributeCounter);

                        var buffer = ReadAttributeBuffer(vertexBuffer, attribute);
                        var numComponents = buffer.Length / vertexBuffer.ElementCount;

                        if (attribute.SemanticName == "BLENDINDICES")
                        {
                            if (!includeJoints)
                            {
                                continue;
                            }

                            var byteBuffer = buffer.Select(f => (byte)f).ToArray();
                            var rawBufferData = new byte[buffer.Length];
                            System.Buffer.BlockCopy(byteBuffer, 0, rawBufferData, 0, rawBufferData.Length);

                            var bufferView = mesh.LogicalParent.UseBufferView(rawBufferData);
                            var accessor = mesh.LogicalParent.CreateAccessor();
                            accessor.SetVertexData(bufferView, 0, buffer.Length / 4, DimensionType.VEC4, EncodingType.UNSIGNED_BYTE);

                            primitive.SetVertexAccessor(accessorName, accessor);

                            continue;
                        }

                        if (attribute.SemanticName == "NORMAL" && VMesh.IsCompressedNormalTangent(drawCall))
                        {
                            var vectors = ToVector4Array(buffer);
                            var (normals, tangents) = DecompressNormalTangents(vectors);
                            primitive.WithVertexAccessor("NORMAL", normals);
                            primitive.WithVertexAccessor("TANGENT", tangents);

                            continue;
                        }

                        if (attribute.SemanticName == "TEXCOORD" && numComponents != 2)
                        {
                            // We are ignoring some data, but non-2-component UVs cause failures in gltf consumers
                            continue;
                        }

                        if (attribute.SemanticName == "BLENDWEIGHT" && numComponents != 4)
                        {
                            Console.Error.WriteLine($"This model has {attribute.SemanticName} with {numComponents} components, which in unsupported.");
                            continue;
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

                                    primitive.WithVertexAccessor(accessorName, vectors);
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

                                    primitive.WithVertexAccessor(accessorName, vectors);
                                    break;
                                }

                            case 2:
                                {
                                    var vectors = ToVector2Array(buffer);
                                    primitive.WithVertexAccessor(accessorName, vectors);
                                    break;
                                }

                            case 1:
                                {
                                    primitive.WithVertexAccessor(accessorName, buffer);
                                    break;
                                }

                            default:
                                throw new NotImplementedException($"Attribute \"{attribute.SemanticName}\" has {numComponents} components");
                        }
                    }

                    // For some reason soruce models can have joints but no weights, check if that is the case
                    var jointAccessor = primitive.GetVertexAccessor("JOINTS_0");
                    if (jointAccessor != null && primitive.GetVertexAccessor("WEIGHTS_0") == null)
                    {
                        // If this occurs, give default weights
                        var defaultWeights = Enumerable.Repeat(Vector4.UnitX, jointAccessor.Count).ToList();
                        primitive.WithVertexAccessor("WEIGHTS_0", defaultWeights);
                    }

                    // Set index buffer
                    var startIndex = (int)drawCall.GetIntegerProperty("m_nStartIndex");
                    var indexCount = (int)drawCall.GetIntegerProperty("m_nIndexCount");
                    var indices = ReadIndices(indexBuffer, startIndex, indexCount);

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

                    // Add material
                    if (!ExportMaterials)
                    {
                        continue;
                    }

                    var materialPath = skinMaterialPath ?? drawCall.GetProperty<string>("m_material") ?? drawCall.GetProperty<string>("m_pMaterial");

                    var materialNameTrimmed = Path.GetFileNameWithoutExtension(materialPath);

                    // Check if material already exists - makes an assumption that if material has the same name it is a duplicate
                    var existingMaterial = model.LogicalMaterials.Where(m => m.Name == materialNameTrimmed).SingleOrDefault();
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
                    var bestMaterial = GenerateGLTFMaterialFromRenderMaterial(renderMaterial, model, materialNameTrimmed);
                    primitive.WithMaterial(bestMaterial);
                }
            }

            return mesh;
        }

        private (Dictionary<string, Node> boneNodes, Node skeletonNode) CreateGltfSkeleton(IVisualNodeContainer container, VModel model)
        {
            var skeleton = model.GetSkeleton(0);
            if (skeleton == null)
            {
                return (null, null);
            }

            var skeletonNode = container.CreateNode("skeleton");
            var boneNodes = new Dictionary<string, Node>();
            foreach (var root in skeleton.Roots)
            {
                CreateBonesRecursive(root, skeletonNode, boneNodes);
            }
            return (boneNodes, skeletonNode);
        }

        private void CreateBonesRecursive(Bone bone, Node parent, Dictionary<string, Node> boneNodes)
        {
            var node = parent.CreateNode(bone.Name)
                .WithLocalTranslation(bone.Position)
                .WithLocalRotation(bone.Angle);
            boneNodes.Add(bone.Name, node);

            // Recurse into children
            foreach (var child in bone.Children)
            {
                CreateBonesRecursive(child, node, boneNodes);
            }
        }

        private static Node[] GetGltfSkeletonJoints(Skeleton skeleton, Dictionary<string, Node> boneNodes, Node skeletonNode)
        {
            var animationJoints = skeleton.Bones
                .Where(bone => bone != null && bone.SkinIndices.Any())
                .Select(bone => (boneNodes.GetValueOrDefault(bone.Name, null), bone.SkinIndices))
                .ToList();

            var numJoints = animationJoints.Any()
                ? animationJoints.Max(j => j.SkinIndices.Max()) + 1
                : 0;
            var result = new Node[numJoints];

            foreach (var joint in animationJoints)
            {
                foreach (var index in joint.SkinIndices)
                {
                    result[index] = joint.Item1;
                }
            }

            // Fill null indices with some dummy node
            for (var i = 0; i < numJoints; i++)
            {
                result[i] ??= skeletonNode.CreateNode();
            }

            return result;
        }

        private Material GenerateGLTFMaterialFromRenderMaterial(VMaterial renderMaterial, ModelRoot model, string materialName)
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
            using var ormTexture = new TextureExtract.TexturePacker { DefaultColor = new SkiaSharp.SKColor(255, 255, 0, 255) };

            var allGltfInputs = MaterialExtract.GltfTextureMappings.Values.SelectMany(x => x);
            var blendNameComparer = new MaterialExtract.LayeredTextureNameComparer(new HashSet<string>(allGltfInputs.Select(x => x.Item2)));
            var blendInputComparer = new MaterialExtract.ChannelMappingComparer(blendNameComparer);

            //share sampler for all textures
            var sampler = model.UseTextureSampler(TextureWrapMode.REPEAT, TextureWrapMode.REPEAT, TextureMipMapFilter.LINEAR_MIPMAP_LINEAR, TextureInterpolationFilter.LINEAR);

            void TrySetupTexture(string textureName, Resource textureResource, List<ValueTuple<MaterialExtract.Channel, string>> renderTextureInputs)
            {
                ProgressReporter?.Report($"Exporting texture: {textureResource.FileName}");
                var fileName = Path.GetFileName(textureResource.FileName);
                var ormFileName = Path.GetFileNameWithoutExtension(fileName) + "_rm.png";
                var exportedTexturePath = Path.ChangeExtension(Path.Join(DstDir, fileName), "png");

                using var bitmap = ((ResourceTypes.Texture)textureResource.DataBlock).GenerateBitmap();
                using var pixels = bitmap.PeekPixels();

                string gltfBestMatch = null;

                void WriteTexture(ReadOnlySpan<byte> pngData, string gltfName, int index, int count)
                {
                    gltfBestMatch = gltfName;
                    renderTextureInputs.RemoveRange(index, count);
                    using var fs = File.Open(exportedTexturePath, FileMode.Create);
                    fs.Write(pngData);
                }

                // Try to find a glTF entry that best matches this texture
                foreach (var (gltfTexture, gltfInputs) in MaterialExtract.GltfTextureMappings)
                {
                    if (!AdaptTextures)
                    {
                        // If we're not adapting textures, blindly match the first input by name.
                        if (blendNameComparer.Equals(renderTextureInputs[0].Item2, gltfInputs[0].Item2))
                        {
                            WriteTexture(TextureExtract.ToPngImage(bitmap), gltfTexture, 0, 1);
                            break;
                        }

                        continue;
                    }

                    // Render texture matches the glTF spec.
                    if (Enumerable.SequenceEqual(renderTextureInputs, gltfInputs, blendInputComparer))
                    {
                        WriteTexture(TextureExtract.ToPngImage(bitmap), gltfTexture, 0, renderTextureInputs.Count);
                        break;
                    }

                    // RGB matches, alpha differs/missing, so write RGB.
                    if (gltfInputs[0].Item1 == MaterialExtract.Channel.RGB && blendInputComparer.Equals(renderTextureInputs[0], gltfInputs[0]))
                    {
                        var png = renderTextureInputs.Count == 1
                            ? TextureExtract.ToPngImage(bitmap)
                            : TextureExtract.ToPngImageChannels(bitmap, renderTextureInputs[0].Item1);

                        WriteTexture(png, gltfTexture, 0, 1);
                        break;
                    }

                    // Render texture likely missing unpack info, otherwise texture types match.
                    if (renderTextureInputs[0].Item1 == MaterialExtract.Channel.RGBA && blendNameComparer.Equals(renderTextureInputs[0].Item2, gltfInputs[0].Item2))
                    {
                        var png = gltfInputs[0].Item1 > MaterialExtract.Channel._Single
                            ? TextureExtract.ToPngImage(bitmap)
                            : TextureExtract.ToPngImageChannels(bitmap, gltfInputs[0].Item1);

                        WriteTexture(png, gltfTexture, 0, 1);
                        break;
                    }
                }

                // Collect any leftover channel/maps to new images
                if (AdaptTextures && gltfBestMatch != "MetallicRoughness")
                {
                    foreach (var (channel, textureType) in renderTextureInputs)
                    {
                        if (blendNameComparer.Equals(textureType, "TextureRoughness"))
                        {
                            ormTexture.Collect(pixels, ormFileName, channel, MaterialExtract.Channel.G);
                        }
                        else if (blendNameComparer.Equals(textureType, "TextureSpecularMask"))
                        {
                            ormTexture.Collect(pixels, ormFileName, channel, MaterialExtract.Channel.G, invert: true);
                        }
                        else if (blendNameComparer.Equals(textureType, "TextureMetalness") || blendNameComparer.Equals(textureType, "TextureMetalnessMask"))
                        {
                            ormTexture.Collect(pixels, ormFileName, channel, MaterialExtract.Channel.B);
                        }
                    }
                }

                if (gltfBestMatch is not null)
                {
                    var image = model.UseImage(exportedTexturePath);
                    image.Name = $"{fileName}_{model.LogicalImages.Count - 1}";

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
            }

            foreach (var renderTexture in renderMaterial.TextureParams)
            {
                var texturePath = renderTexture.Value;
                var textureResource = FileLoader.LoadFile(texturePath + "_c");

                if (textureResource == null)
                {
                    continue;
                }

                // TODO: get the signature directly instead of forming UnpackInfos
                var inputImages = MaterialExtract.GetInputImagesForTexture(renderTexture.Key, texturePath, renderMaterial, false, false).Select(x => x.ToValueTuple()).ToList();

                // Preemptive check so as to not perform any unnecessary GenerateBitmap
                if (inputImages.Count == 0 || !inputImages.Any(input => allGltfInputs.Any(gltfInput => blendNameComparer.Equals(input.Item2, gltfInput.Item2))))
                {
                    continue;
                }

                TrySetupTexture(renderTexture.Key, textureResource, inputImages);
            }

            if (ormTexture.Bitmap is not null)
            {
                var finalDest = Path.Combine(DstDir, ormTexture.FileName);
                using (var fs = File.Open(finalDest, FileMode.Create))
                {
                    fs.Write(TextureExtract.ToPngImage(ormTexture.Bitmap));
                }

                var metallicRoughness = material.FindChannel("MetallicRoughness");
                metallicRoughness?.SetTexture(0, model.UseTexture(model.UseImage(finalDest), sampler));
                metallicRoughness?.SetFactor("MetallicFactor", 1.0f); // Ignore g_flMetalness
            }

            return material;
        }

        public static string GetAccessorName(string name, int index)
        {
            switch (name)
            {
                case "BLENDINDICES":
                    return $"JOINTS_{index}";
                case "BLENDWEIGHT":
                    return $"WEIGHTS_{index}";
                case "TEXCOORD":
                    return $"TEXCOORD_{index}";
                case "COLOR":
                    return $"COLOR_{index}";
            }

            if (index > 0)
            {
                throw new InvalidDataException($"Got attribute \"{name}\" more than once, but that is not supported");
            }

            return name;
        }

        private static float[] ReadAttributeBuffer(OnDiskBufferData buffer, RenderInputLayoutField attribute)
            => Enumerable.Range(0, (int)buffer.ElementCount)
                .SelectMany(i => VBIB.ReadVertexAttribute(i, buffer, attribute))
                .ToArray();

        private static int[] ReadIndices(OnDiskBufferData indexBuffer, int start, int count)
        {
            var indices = new int[count];

            var byteCount = count * (int)indexBuffer.ElementSizeInBytes;
            var byteStart = start * (int)indexBuffer.ElementSizeInBytes;

            if (indexBuffer.ElementSizeInBytes == 4)
            {
                System.Buffer.BlockCopy(indexBuffer.Data, byteStart, indices, 0, byteCount);
            }
            else if (indexBuffer.ElementSizeInBytes == 2)
            {
                var shortIndices = new ushort[count];
                System.Buffer.BlockCopy(indexBuffer.Data, byteStart, shortIndices, 0, byteCount);
                indices = Array.ConvertAll(shortIndices, i => (int)i);
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
                var decompressedNormal = DecompressNormal(new Vector2(compressedNormal.X, compressedNormal.Y));
                var decompressedTangent = DecompressTangent(new Vector2(compressedNormal.Z, compressedNormal.W));

                // Swap Y and Z axes
                normals[i] = new Vector3(decompressedNormal.X, decompressedNormal.Z, decompressedNormal.Y);
                tangents[i] = new Vector4(decompressedTangent.X, decompressedTangent.Z, decompressedTangent.Y, decompressedTangent.W);
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
    }
}
