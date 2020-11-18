using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using SharpGLTF.IO;
using SharpGLTF.Schema2;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.IO
{
    using static VBIB;
    using VMaterial = ResourceTypes.Material;
    using VMesh = ResourceTypes.Mesh;
    using VModel = ResourceTypes.Model;
    using VAnimation = ResourceTypes.ModelAnimation.Animation;

    public class GltfModelExporter
    {
        private const string GENERATOR = "VRF - https://vrf.steamdb.info/";

        // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
        // Also divides by 100, gltf units are in meters, source engine units are in inches
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#coordinate-system-and-units
        private readonly Matrix4x4 TRANSFORMSOURCETOGLTF = Matrix4x4.CreateScale(0.0254f) * Matrix4x4.CreateFromYawPitchRoll(0, (float)Math.PI / -2f, 0);

        public IProgressReporter ProgressReporter { get; set; }
        public IFileLoader FileLoader { get; set; }
        public bool ExportMaterials { get; set; } = true;

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
                throw new NullReferenceException(nameof(FileLoader));
            }

            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = GENERATOR;
            var scene = exportedModel.UseScene(Path.GetFileName(resourceName));

            void AddMeshNode(string name, VMesh mesh, Skeleton skeleton)
            {
                if (mesh.GetData().GetArray("m_sceneObjects").Length == 0)
                {
                    return;
                }

                var hasJoints = skeleton.AnimationTextureSize > 0;
                var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, hasJoints);
                var hasVertexJoints = exportedMesh.Primitives.All(primitive => primitive.GetVertexAccessor("JOINTS_0") != null);
                
                if (hasJoints && hasVertexJoints)
                {
                    var skeletonNode = scene.CreateNode(name);
                    var joints = CreateGltfSkeleton(skeleton, skeletonNode);

                    scene.CreateNode(name)
                        .WithSkinnedMesh(exportedMesh, Matrix4x4.Identity, joints);

                    // Rotate upright, scale inches to meters.
                    skeletonNode.WorldMatrix = TRANSFORMSOURCETOGLTF;

                    // Add animations
                    var animations = GetAllAnimations(model);
                    foreach (var animation in animations)
                    {
                        var exportedAnimation = exportedModel.CreateAnimation(animation.Name);
                        var rotationDict = new Dictionary<string, Dictionary<float, Quaternion>>();
                        var translationDict = new Dictionary<string, Dictionary<float, Vector3>>();

                        var time = (float)0;
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
                            var node = joints.FirstOrDefault(n => n.Name == bone);
                            if (node != null)
                            {
                                exportedAnimation.CreateRotationChannel(node, rotationDict[bone], true);
                                exportedAnimation.CreateTranslationChannel(node, translationDict[bone], true);
                            }
                        }
                    }
                }
                else
                {
                    var meshNode = scene.CreateNode(name)
                        .WithMesh(exportedMesh);

                    // Rotate upright, scale inches to meters.
                    meshNode.WorldMatrix = TRANSFORMSOURCETOGLTF;
                }
            }

            // Add meshes and their skeletons
            var meshes = LoadModelMeshes(model);
            for (var i = 0; i < meshes.Length; i++)
            {
                AddMeshNode(meshes[i].Name, meshes[i].Mesh, model.GetSkeleton(i));
            }



            exportedModel.Save(fileName);
        }

        /// <summary>
        /// Create a combined list of referenced and embedded meshes. Importantly retains the
        /// refMeshes order so it can be used for getting skeletons.
        /// </summary>
        /// <param name="model">The model to get the meshes from.</param>
        /// <returns>A tuple of meshes and their names.</returns>
        private (VMesh Mesh, string Name)[] LoadModelMeshes(VModel model)
        {
            var refMeshes = model.GetRefMeshes().ToArray();
            var meshes = new (VMesh, string)[refMeshes.Length];

            var embeddedMeshIndex = 0;
            var embeddedMeshes = model.GetEmbeddedMeshes().ToArray();

            for (var i = 0; i < meshes.Length; i++)
            {
                var meshReference = refMeshes[i];
                if (meshReference == null)
                {
                    // If refmesh is null, take an embedded mesh
                    meshes[i] = (embeddedMeshes[embeddedMeshIndex++], $"Embedded Mesh {embeddedMeshIndex}");
                } else
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
            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = GENERATOR;
            var name = Path.GetFileName(resourceName);
            var scene = exportedModel.UseScene(name);

            var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, false);
            var meshNode = scene.CreateNode(name)
                .WithMesh(exportedMesh);

            // Swap Rotate upright, scale inches to meters.
            meshNode.WorldMatrix = TRANSFORMSOURCETOGLTF;

            exportedModel.Save(fileName);
        }

        private Mesh CreateGltfMesh(string meshName, VMesh vmesh, ModelRoot model, bool includeJoints)
        {
            ProgressReporter?.SetProgress($"Creating mesh: {meshName}");

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
                    foreach (var attribute in vertexBuffer.Attributes)
                    {
                        attributeCounters.TryGetValue(attribute.Name, out var attributeCounter);
                        attributeCounters[attribute.Name] = attributeCounter + 1;
                        var accessorName = GetAccessorName(attribute.Name, attributeCounter);

                        var buffer = ReadAttributeBuffer(vertexBuffer, attribute);
                        var numComponents = buffer.Length / vertexBuffer.Count;

                        if (attribute.Name == "BLENDINDICES")
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

                        if (attribute.Name == "NORMAL" && VMesh.IsCompressedNormalTangent(drawCall))
                        {
                            var vectors = ToVector4Array(buffer);
                            var (normals, tangents) = DecompressNormalTangents(vectors);
                            primitive.WithVertexAccessor("NORMAL", normals);
                            primitive.WithVertexAccessor("TANGENT", tangents);

                            continue;
                        }

                        if (attribute.Name == "TEXCOORD" && numComponents != 2)
                        {
                            // We are ignoring some data, but non-2-component UVs cause failures in gltf consumers
                            continue;
                        }

                        switch (numComponents)
                        {
                            case 4:
                                {
                                    var vectors = ToVector4Array(buffer);

                                    // dropship.vmdl in HL:A has a tanget with value of <0, -0, 0>
                                    if (attribute.Name == "NORMAL" || attribute.Name == "TANGENT")
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
                                    if (attribute.Name == "NORMAL" || attribute.Name == "TANGENT")
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
                                throw new NotImplementedException($"Attribute \"{attribute.Name}\" has {numComponents} components");
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
                    primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, indices);

                    // Add material
                    if (!ExportMaterials)
                    {
                        continue;
                    }

                    var materialPath = drawCall.GetProperty<string>("m_material");

                    ProgressReporter?.SetProgress($"Loading material: {materialPath}");

                    var materialResource = FileLoader.LoadFile(materialPath + "_c");

                    if (materialResource == null)
                    {
                        continue;
                    }

                    var renderMaterial = (VMaterial)materialResource.DataBlock;

                    var materialNameTrimmed = Path.GetFileNameWithoutExtension(materialPath);
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
            material.Alpha = isTranslucent > 0 ? AlphaMode.BLEND : AlphaMode.OPAQUE;

            float metalValue = 0;

            foreach (var floatParam in renderMaterial.FloatParams)
            {
                if (floatParam.Key == "g_flMetalness")
                {
                    metalValue = floatParam.Value;
                }
            }

            // assume non-metallic unless prompted
            material.WithPBRMetallicRoughness(Vector4.One, null, metallicFactor: metalValue);

            foreach (var renderTexture in renderMaterial.TextureParams)
            {
                var texturePath = renderTexture.Value;

                var fileName = Path.GetFileNameWithoutExtension(texturePath);

                ProgressReporter?.SetProgress($"Exporting texture: {texturePath}");

                var textureResource = FileLoader.LoadFile(texturePath + "_c");

                if (textureResource == null)
                {
                    continue;
                }

                var bitmap = ((ResourceTypes.Texture)textureResource.DataBlock).GenerateBitmap();

                if (renderTexture.Key == "g_tColor" && material.Alpha == AlphaMode.OPAQUE)
                {
                    var bitmapSpan = bitmap.PeekPixels().GetPixelSpan<SKColor>();

                    // expensive transparency workaround for color maps
                    for (var i = 0; i < bitmapSpan.Length; i++)
                    {
                        bitmapSpan[i] = bitmapSpan[i].WithAlpha(255);
                    }
                }

                var textureImage = SKImage.FromBitmap(bitmap);
                using var data = textureImage.Encode(SKEncodedImageFormat.Png, 100);

                var image = model.UseImageWithContent(data.ToArray());
                // TODO find a way to change the image's URI to be the image name, right now it turns into (model)_0, (model)_1....
                image.Name = fileName + $"_{model.LogicalImages.Count - 1}";

                var sampler = model.UseTextureSampler(TextureWrapMode.REPEAT, TextureWrapMode.REPEAT, TextureMipMapFilter.NEAREST, TextureInterpolationFilter.DEFAULT);
                sampler.Name = fileName;

                var tex = model.UseTexture(image);
                tex.Name = fileName + $"_{model.LogicalTextures.Count - 1}";
                tex.Sampler = sampler;

                switch (renderTexture.Key)
                {
                    case "g_tColor":

                        material.FindChannel("BaseColor")?.SetTexture(0, tex);

                        var indexTexture = new JsonDictionary() { ["index"] = image.LogicalIndex };
                        var dict = material.TryUseExtrasAsDictionary(true);
                        dict["baseColorTexture"] = indexTexture;

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
                        Console.WriteLine($"Warning: Unsupported Texture Type {renderTexture.Key}");
                        break;
                }
            }

            return material;
        }

        private List<VAnimation> GetAllAnimations(VModel model)
        {
            var animGroupPaths = model.GetReferencedAnimationGroupNames();
            var animations = model.GetEmbeddedAnimations().ToList();

            // Load animations from referenced animation groups
            foreach (var animGroupPath in animGroupPaths)
            {
                var animGroup = FileLoader.LoadFile(animGroupPath + "_c");
                if (animGroup != default)
                {
                    var data = animGroup.DataBlock is ResourceTypes.NTRO ntro
                        ? ntro.Output as IKeyValueCollection
                        : ((ResourceTypes.BinaryKV3)animGroup.DataBlock).Data;

                    // Get the list of animation files
                    var animArray = data.GetArray<string>("m_localHAnimArray").Where(a => a != null);
                    // Get the key to decode the animations
                    var decodeKey = data.GetSubCollection("m_decodeKey");

                    // Load animation files
                    foreach (var animationFile in animArray)
                    {
                        var animResource = FileLoader.LoadFile(animationFile + "_c");

                        // Build animation classes
                        animations.AddRange(VAnimation.FromResource(animResource, decodeKey));
                    }
                }
            }

            return animations.ToList();
        }

        public static string GetAccessorName(string name, int index)
        {
            switch (name)
            {
                case "BLENDINDICES": return $"JOINTS_{index}";
                case "BLENDWEIGHT": return $"WEIGHTS_{index}";
                case "TEXCOORD": return $"TEXCOORD_{index}";
                case "COLOR": return $"COLOR_{index}";
            }

            if (index > 0)
            {
                throw new InvalidDataException($"Got attribute \"{name}\" more than once, but that is not supported");
            }

            return name;
        }

        private static float[] ReadAttributeBuffer(VertexBuffer buffer, VertexAttribute attribute)
            => Enumerable.Range(0, (int)buffer.Count)
                .SelectMany(i => VBIB.ReadVertexAttribute(i, buffer, attribute))
                .ToArray();

        private static int[] ReadIndices(IndexBuffer indexBuffer, int start, int count)
        {
            var indices = new int[count];

            var byteCount = count * (int)indexBuffer.Size;
            var byteStart = start * (int)indexBuffer.Size;

            if (indexBuffer.Size == 4)
            {
                System.Buffer.BlockCopy(indexBuffer.Buffer, byteStart, indices, 0, byteCount);
            }
            else if (indexBuffer.Size == 2)
            {
                var shortIndices = new ushort[count];
                System.Buffer.BlockCopy(indexBuffer.Buffer, byteStart, shortIndices, 0, byteCount);
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
            x = x - 64;                                     // -64..63
            y = y - 64;

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
