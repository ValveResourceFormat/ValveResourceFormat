using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.IO;
using SharpGLTF.Schema2;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;
using Material = SharpGLTF.Schema2.Material;
using Mesh = SharpGLTF.Schema2.Mesh;

namespace ValveResourceFormat.IO
{
    using static VBIB;
    using VMaterial = ResourceTypes.Material;
    using VMesh = ResourceTypes.Mesh;
    using VModel = ResourceTypes.Model;
    using VWorldNode = ResourceTypes.WorldNode;
    using VWorld = ResourceTypes.World;
    using VEntityLump = ResourceTypes.EntityLump;
    using VAnimation = ResourceTypes.ModelAnimation.Animation;

    public class GltfModelExporter
    {
        private const string GENERATOR = "VRF - https://vrf.steamdb.info/";

        // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
        // Also divides by 100, gltf units are in meters, source engine units are in inches
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#coordinate-system-and-units
        private readonly Matrix4x4 TRANSFORMSOURCETOGLTF = Matrix4x4.CreateScale(0.0254f) * Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / -2f, (float)Math.PI / -2f);

        public IProgress<string> ProgressReporter { get; set; }
        public IFileLoader FileLoader { get; set; }
        public bool ExportMaterials { get; set; } = true;

        private string DstDir;
        private readonly IDictionary<string, Node> LoadedUnskinnedMeshDictionary = new Dictionary<string, Node>();

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
                    var meshes = LoadModelMeshes(Model, Name);
                    for (var i = 0; i < meshes.Length; i++)
                    {
                        var node = AddMeshNode(exportedModel, scene, Model,
                            meshes[i].Name, meshes[i].Mesh, Model.GetSkeleton(i));

                        if (node == null)
                        {
                            continue;
                        }
                        // Swap Rotate upright, scale inches to meters.
                        node.WorldMatrix = Transform * TRANSFORMSOURCETOGLTF;
                    }
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
                var meshes = LoadModelMeshes(model, Path.GetFileNameWithoutExtension(modelName));
                for (var i = 0; i < meshes.Length; i++)
                {
                    var meshName = meshes[i].Name;
                    if (skinName != null)
                    {
                        meshName += "." + skinName;
                    }
                    var node = AddMeshNode(exportedModel, scene, model,
                        meshName, meshes[i].Mesh, model.GetSkeleton(i),
                        skinName != null ? GetSkinPathFromModel(model, skinName) : null);

                    if (node == null)
                    {
                        continue;
                    }
                    // Swap Rotate upright, scale inches to meters.
                    node.WorldMatrix = transform * TRANSFORMSOURCETOGLTF;
                }
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
                var meshes = LoadModelMeshes(Model, Name);
                for (var i = 0; i < meshes.Length; i++)
                {
                    var node = AddMeshNode(exportedModel, scene, Model,
                        meshes[i].Name, meshes[i].Mesh, Model.GetSkeleton(i));

                    if (node == null)
                    {
                        continue;
                    }
                    // Swap Rotate upright, scale inches to meters, after local transform.
                    node.WorldMatrix = Transform * TRANSFORMSOURCETOGLTF;
                }
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
            var meshes = LoadModelMeshes(model, resourceName);
            for (var i = 0; i < meshes.Length; i++)
            {
                var node = AddMeshNode(exportedModel, scene, model,
                    meshes[i].Name, meshes[i].Mesh, model.GetSkeleton(i));

                if (node == null)
                {
                    continue;
                }
                // Swap Rotate upright, scale inches to meters.
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }

            WriteModelFile(exportedModel, fileName);
        }

        /// <summary>
        /// Create a combined list of referenced and embedded meshes. Importantly retains the
        /// refMeshes order so it can be used for getting skeletons.
        /// </summary>
        /// <param name="model">The model to get the meshes from.</param>
        /// <returns>A tuple of meshes and their names.</returns>
        private (VMesh Mesh, string Name)[] LoadModelMeshes(VModel model, string modelName)
        {
            var refMeshes = model.GetRefMeshes().ToArray();
            var meshes = new (VMesh, string)[refMeshes.Length];

            var embeddedMeshIndex = 0;
            var embeddedMeshes = model.GetEmbeddedMeshes().ToArray();

            for (var i = 0; i < meshes.Length; i++)
            {
                var meshReference = refMeshes[i];
                if (string.IsNullOrEmpty(meshReference))
                {
                    // If refmesh is null, take an embedded mesh
                    meshes[i] = (embeddedMeshes[embeddedMeshIndex++], $"{modelName}.Embedded.{embeddedMeshIndex}");
                }
                else
                {
                    // Load mesh from file
                    var meshResource = FileLoader.LoadFile(meshReference + "_c");
                    if (meshResource == null)
                    {
                        continue;
                    }

                    var nodeName = Path.GetFileNameWithoutExtension(meshReference);
                    var mesh = new VMesh(meshResource);
                    meshes[i] = (mesh, nodeName);
                }
            }

            return meshes;
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
            var node = AddMeshNode(exportedModel, scene, null, name, mesh, null);

            if (node != null)
            {
                // Swap Rotate upright, scale inches to meters.
                node.WorldMatrix = TRANSFORMSOURCETOGLTF;
            }

            WriteModelFile(exportedModel, fileName);
        }

        private Node AddMeshNode(ModelRoot exportedModel, Scene scene, VModel model, string name,
            VMesh mesh, Skeleton skeleton, string skinMaterialPath = null)
        {
            if (mesh.GetData().GetArray("m_sceneObjects").Length == 0)
            {
                return null;
            }

            if (LoadedUnskinnedMeshDictionary.TryGetValue(name, out var existingNode))
            {
                // Make a new node that uses the existing mesh
                var newNode = scene.CreateNode(name);
                newNode.Mesh = existingNode.Mesh;
                return newNode;
            }

            var hasJoints = skeleton != null && skeleton.AnimationTextureSize > 0;
            var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, hasJoints, skinMaterialPath);
            var hasVertexJoints = exportedMesh.Primitives.All(primitive => primitive.GetVertexAccessor("JOINTS_0") != null);

            if (hasJoints && hasVertexJoints && model != null)
            {
                var skeletonNode = scene.CreateNode(name);
                var joints = CreateGltfSkeleton(skeleton, skeletonNode);

                scene.CreateNode(name)
                    .WithSkinnedMesh(exportedMesh, Matrix4x4.Identity, joints);

                // Add animations
                var animations = AnimationGroupLoader.GetAllAnimations(model, FileLoader);
                foreach (var animation in animations)
                {
                    var exportedAnimation = exportedModel.CreateAnimation(animation.Name);
                    var rotationDict = new Dictionary<string, Dictionary<float, Quaternion>>();
                    var translationDict = new Dictionary<string, Dictionary<float, Vector3>>();

                    var time = 0f;
                    foreach (var frame in animation.Frames)
                    {
                        foreach (var boneFrame in frame.Bones)
                        {
                            var bone = boneFrame.Key;
                            if (!rotationDict.ContainsKey(bone))
                            {
                                rotationDict[bone] = new Dictionary<float, Quaternion>();
                                translationDict[bone] = new Dictionary<float, Vector3>();
                            }
                            rotationDict[bone].Add(time, boneFrame.Value.Angle);
                            translationDict[bone].Add(time, boneFrame.Value.Position);
                        }
                        time += 1 / animation.Fps;
                    }

                    foreach (var bone in rotationDict.Keys)
                    {
                        var jointNode = joints.FirstOrDefault(n => n.Name == bone);
                        if (jointNode != null)
                        {
                            exportedAnimation.CreateRotationChannel(jointNode, rotationDict[bone], true);
                            exportedAnimation.CreateTranslationChannel(jointNode, translationDict[bone], true);
                        }
                    }
                }
                return skeletonNode;
            }
            var node = scene.CreateNode(name).WithMesh(exportedMesh);
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
            var settings = new WriteSettings();
            settings.ImageWriting = ResourceWriteMode.SatelliteFile;
            settings.ImageWriteCallback = ImageWriteCallback;
            settings.JsonIndented = true;

            // See https://github.com/KhronosGroup/glTF/blob/0bc36d536946b13c4807098f9cf62ddff738e7a5/specification/2.0/README.md#buffers-and-buffer-views
            // Disable merging buffers if the buffer size is over 1GiB, otherwise this will
            // cause SharpGLTF to run past the int32 limitation and crash.
            var totalSize = exportedModel.LogicalBuffers.Sum(buffer => (long)buffer.Content.Length);
            settings.MergeBuffers = totalSize <= 1_074_000_000;

            if (!settings.MergeBuffers)
            {
                throw new NotSupportedException("VRF does not properly support big model (>1GiB) exports yet due to glTF limitations. See https://github.com/SteamDatabase/ValveResourceFormat/issues/379");
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

                    string primitiveType = drawCall.GetProperty<object>("m_nPrimitiveType") switch
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

        private Node[] CreateGltfSkeleton(Skeleton skeleton, Node skeletonNode)
        {
            var joints = new List<(Node Node, List<int> Indices)>();

            foreach (var root in skeleton.Roots)
            {
                joints.AddRange(CreateBonesRecursive(root, skeletonNode));
            }

            var animationJoints = joints.Where(j => j.Indices.Any()).ToList();
            var numJoints = animationJoints.Any()
                ? animationJoints.Where(j => j.Indices.Any()).Max(j => j.Indices.Max())
                : 0;
            var result = new Node[numJoints + 1];

            foreach (var joint in animationJoints)
            {
                foreach (var index in joint.Indices)
                {
                    result[index] = joint.Node;
                }
            }

            // Fill null indices with some dummy node
            for (var i = 0; i < numJoints + 1; i++)
            {
                result[i] ??= skeletonNode.CreateNode();
            }

            return result;
        }

        private IEnumerable<(Node Node, List<int> Indices)> CreateBonesRecursive(Bone bone, Node parent)
        {
            var node = parent.CreateNode(bone.Name)
                .WithLocalTransform(bone.BindPose);

            // Recurse into children
            return bone.Children
                .SelectMany(child => CreateBonesRecursive(child, node))
                .Append((node, bone.SkinIndices));
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

            // assume non-metallic unless prompted
            float metalValue = 0;

            if (renderMaterial.FloatParams.TryGetValue("g_flMetalness", out var flMetalness))
            {
                metalValue = flMetalness;
            }

            Vector4 baseColor = Vector4.One;

            if (renderMaterial.VectorParams.TryGetValue("g_vColorTint", out var vColorTint))
            {
                baseColor = vColorTint;
                baseColor.W = 1; //Tint only affects color
            }

            material.WithPBRMetallicRoughness(baseColor, null, metallicFactor: metalValue);

            //share sampler for all textures
            var sampler = model.UseTextureSampler(TextureWrapMode.REPEAT, TextureWrapMode.REPEAT, TextureMipMapFilter.LINEAR_MIPMAP_LINEAR, TextureInterpolationFilter.LINEAR);

            foreach (var renderTexture in renderMaterial.TextureParams)
            {
                var texturePath = renderTexture.Value;

                var fileName = Path.GetFileNameWithoutExtension(texturePath);

                ProgressReporter?.Report($"Exporting texture: {texturePath}");

                var textureResource = FileLoader.LoadFile(texturePath + "_c");

                if (textureResource == null)
                {
                    continue;
                }

                var exportedTexturePath = Path.Join(DstDir, fileName);
                exportedTexturePath = Path.ChangeExtension(exportedTexturePath, "png");

                using (var bitmap = ((ResourceTypes.Texture)textureResource.DataBlock).GenerateBitmap())
                {
                    if (renderTexture.Key.StartsWith("g_tColor", StringComparison.Ordinal) && material.Alpha == AlphaMode.OPAQUE)
                    {
                        using var pixels = bitmap.PeekPixels();
                        var bitmapSpan = pixels.GetPixelSpan<SKColor>();

                        // expensive transparency workaround for color maps
                        for (var i = 0; i < bitmapSpan.Length; i++)
                        {
                            bitmapSpan[i] = bitmapSpan[i].WithAlpha(255);
                        }
                    }

                    using var fs = File.Open(exportedTexturePath, FileMode.Create);
                    bitmap.PeekPixels().Encode(fs, SKEncodedImageFormat.Png, 100);
                }

                var image = model.UseImage(exportedTexturePath);
                image.Name = fileName + $"_{model.LogicalImages.Count - 1}";

                var tex = model.UseTexture(image);
                tex.Name = fileName + $"_{model.LogicalTextures.Count - 1}";
                tex.Sampler = sampler;

                switch (renderTexture.Key)
                {
                    case "g_tColor":
                    case "g_tColor1":
                    case "g_tColor2":
                    case "g_tColorA":
                    case "g_tColorB":
                    case "g_tColorC":
                        var channel = material.FindChannel("BaseColor");
                        if (channel?.Texture != null && renderTexture.Key != "g_tColor")
                        {
                            break;
                        }

                        channel?.SetTexture(0, tex);

                        material.Extras = JsonContent.CreateFrom(new Dictionary<string, object>
                        {
                            ["baseColorTexture"] = new Dictionary<string, object>
                            {
                                { "index", image.LogicalIndex },
                            },
                        });

                        break;
                    case "g_tNormal":
                        material.FindChannel("Normal")?.SetTexture(0, tex);
                        break;
                    case "g_tAmbientOcclusion":
                        material.FindChannel("Occlusion")?.SetTexture(0, tex);
                        break;
                    case "g_tEmissive":
                        material.FindChannel("Emissive")?.SetTexture(0, tex);
                        break;
                    case "g_tShadowFalloff":
                    // example: tongue_gman, materials/default/default_skin_shadowwarp_tga_f2855b6e.vtex
                    case "g_tCombinedMasks":
                    // example: models/characters/gman/materials/gman_head_mouth_mask_tga_bb35dc38.vtex
                    case "g_tDiffuseFalloff":
                    // example: materials/default/default_skin_diffusewarp_tga_e58a9ed.vtex
                    case "g_tIris":
                    // example:
                    case "g_tIrisMask":
                    // example: models/characters/gman/materials/gman_eye_iris_mask_tga_a5bb4a1e.vtex
                    case "g_tTintColor":
                    // example: models/characters/lazlo/eyemeniscus_vmat_g_ttintcolor_a00ef19e.vtex
                    case "g_tAnisoGloss":
                    // example: gordon_beard, models/characters/gordon/materials/gordon_hair_normal_tga_272a44e9.vtex
                    case "g_tBentNormal":
                    // example: gman_teeth, materials/default/default_skin_shadowwarp_tga_f2855b6e.vtex
                    case "g_tFresnelWarp":
                    // example: brewmaster_color, materials/default/default_fresnelwarprim_tga_d9279d65.vtex
                    case "g_tMasks1":
                    // example: brewmaster_color, materials/models/heroes/brewmaster/brewmaster_base_metalnessmask_psd_58eaa40f.vtex
                    case "g_tMasks2":
                    // example: brewmaster_color,materials/models/heroes/brewmaster/brewmaster_base_specmask_psd_63e9fb90.vtex
                    default:
                        Console.Error.WriteLine($"Warning: Unsupported Texture Type {renderTexture.Key}");
                        break;
                }
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

            float x = inputNormal.X - 128.0f;
            float y = inputNormal.Y - 128.0f;
            float z;

            float zSignBit = x < 0 ? 1.0f : 0.0f;           // z and t negative bits (like slt asm instruction)
            float tSignBit = y < 0 ? 1.0f : 0.0f;
            float zSign = -((2 * zSignBit) - 1);          // z and t signs
            float tSign = -((2 * tSignBit) - 1);

            x = (x * zSign) - zSignBit;                           // 0..127
            y = (y * tSign) - tSignBit;
            x -= 64;                                     // -64..63
            y -= 64;

            float xSignBit = x < 0 ? 1.0f : 0.0f;   // x and y negative bits (like slt asm instruction)
            float ySignBit = y < 0 ? 1.0f : 0.0f;
            float xSign = -((2 * xSignBit) - 1);          // x and y signs
            float ySign = -((2 * ySignBit) - 1);

            x = ((x * xSign) - xSignBit) / 63.0f;             // 0..1 range
            y = ((y * ySign) - ySignBit) / 63.0f;
            z = 1.0f - x - y;

            float oolen = 1.0f / (float)Math.Sqrt((x * x) + (y * y) + (z * z));   // Normalize and
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
