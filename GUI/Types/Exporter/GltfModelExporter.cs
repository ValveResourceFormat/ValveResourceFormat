using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using GUI.Types.Renderer;
using GUI.Utils;
using NAudio.SoundFont;
using SharpGLTF.IO;
using SharpGLTF.Schema2;
using SkiaSharp;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Exporter
{
    using static ValveResourceFormat.Blocks.VBIB;
    using VMesh = ValveResourceFormat.ResourceTypes.Mesh;
    using VModel = ValveResourceFormat.ResourceTypes.Model;

    public class GltfModelExporter
    {
        private const string GENERATOR = "VRF - https://vrf.steamdb.info/";

        public void ExportToFile(string resourceName, string fileName, VModel model, VrfGuiContext context)
        {
            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = GENERATOR;
            var scene = exportedModel.UseScene(Path.GetFileName(resourceName));
            var embeddedMeshIndex = 0;


            foreach (var mesh in model.GetEmbeddedMeshes())
            {
                var name = $"Embedded Mesh {++embeddedMeshIndex}";
                var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, context);
                scene.CreateNode(name)
                    .WithMesh(exportedMesh);
            }

            foreach (var meshReference in model.GetReferencedMeshNames())
            {
                var meshResource = context.LoadFileByAnyMeansNecessary(meshReference + "_c");

                if (meshResource == null)
                {
                    continue;
                }
                var nodeName = Path.GetFileName(meshReference);
                var mesh = new VMesh(meshResource);
                var exportedMesh = CreateGltfMesh(nodeName, mesh, exportedModel, context);
                var materials = exportedModel.LogicalMaterials;

                scene.CreateNode(nodeName)
                    .WithMesh(exportedMesh);
            }

            exportedModel.Save(fileName);
        }

        public void ExportToFile(string resourceName, string fileName, VMesh mesh, VrfGuiContext context)
        {
            var exportedModel = ModelRoot.CreateModel();
            exportedModel.Asset.Generator = GENERATOR;
            var name = Path.GetFileName(resourceName);
            var scene = exportedModel.UseScene(name);

            var exportedMesh = CreateGltfMesh(name, mesh, exportedModel, context);
            scene.CreateNode(name)
                .WithMesh(exportedMesh);

            exportedModel.Save(fileName);
        }

        private Mesh CreateGltfMesh(string meshName, VMesh vmesh, ModelRoot model, VrfGuiContext context)
        {
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
                    var uniqueAttributes = vertexBuffer.Attributes.GroupBy(a => a.Name).Select(g => g.First());

                    // Set vertex attributes
                    foreach (var attribute in uniqueAttributes)
                    {
                        if (AccessorInfo.TryGetValue(attribute.Name, out var accessorInfo))
                        {
                            var buffer = ReadAttributeBuffer(vbib, vertexBuffer, attribute);

                            if (accessorInfo.NumComponents == 4)
                            {
                                var vectors = ToVector4Array(buffer);
                                primitive.WithVertexAccessor(accessorInfo.GltfAccessorName, vectors);
                            }
                            else if (attribute.Name == "NORMAL" && DrawCall.IsCompressedNormalTangent(drawCall))
                            {
                                var vectors = ToVector4Array(buffer);
                                var (normals, tangents) = DecompressNormalTangents(vectors);
                                primitive.WithVertexAccessor("NORMAL", normals);
                                primitive.WithVertexAccessor("TANGENT", tangents);
                            }
                            else if (accessorInfo.NumComponents == 3)
                            {
                                var vectors = ToVector3Array(buffer, true, accessorInfo.Resize);
                                primitive.WithVertexAccessor(accessorInfo.GltfAccessorName, vectors);
                            }
                            else if (accessorInfo.NumComponents == 2)
                            {
                                var vectors = ToVector2Array(buffer);
                                primitive.WithVertexAccessor(accessorInfo.GltfAccessorName, vectors);
                            }
                        }
                    }

                    // Set index buffer
                    var indices = ReadIndices(indexBuffer);
                    primitive.WithIndicesAccessor(PrimitiveType.TRIANGLES, indices);

                    // Add material
                    var materialPath = drawCall.GetProperty<string>("m_material");

                    var renderMaterial = context.MaterialLoader.GetMaterial(materialPath);

                    var materialNameTrimmed = materialPath[(materialPath.LastIndexOf("/") + 1)..materialPath.IndexOf(".vmat")];
                    var bestMaterial = GenerateGLTFMaterialFromRenderMaterial(renderMaterial, model, context, materialNameTrimmed);
                    primitive.WithMaterial(bestMaterial);
                }
            }

            return mesh;
        }

        private Material GenerateGLTFMaterialFromRenderMaterial(RenderMaterial renderMaterial, ModelRoot model, VrfGuiContext context, string materialName)
        {
            //context.MaterialLoader.LoadedTextures

            var material = model
                    .CreateMaterial(materialName + "_material")
                    .WithPBRMetallicRoughness();

            foreach (var renderTexture in renderMaterial.Material.TextureParams)
            {
                var texturePath = renderTexture.Value;
                var textureNameTrimmed = texturePath[(texturePath.LastIndexOf("/") + 1)..texturePath.IndexOf(".vtex")];

                Console.WriteLine($"Exporting texture for mesh: {texturePath}");
                var textureResource = context.LoadFileByAnyMeansNecessary(texturePath + "_c");

                var textureImage = SKImage.FromBitmap(((ValveResourceFormat.ResourceTypes.Texture)textureResource.DataBlock).GenerateBitmap());

                using var data = textureImage.Encode(SKEncodedImageFormat.Png, 100);

                var image = model.UseImageWithContent(data.ToArray());
                // TODO find a way to change the image's URI to be the image name, right now it turns into (model)_0, (model)_1....
                image.Name = textureNameTrimmed + "_image";

                var sampler = model.UseTextureSampler(TextureWrapMode.CLAMP_TO_EDGE, TextureWrapMode.CLAMP_TO_EDGE, TextureMipMapFilter.NEAREST, TextureInterpolationFilter.DEFAULT);
                sampler.Name = textureNameTrimmed + "_sampler";

                var tex = model.UseTexture(image);
                tex.Name = textureNameTrimmed + "_texture";
                tex.Sampler = sampler;

                if (textureNameTrimmed.Contains("color"))
                {
                    material.FindChannel("BaseColor")?.SetTexture(0, tex);

                    var indexTexture = new JsonDictionary() { ["index"] = image.LogicalIndex };
                    var dict = material.TryUseExtrasAsDictionary(true);
                    dict["baseColorTexture"] = indexTexture;
                }

                if (textureNameTrimmed.ToLower().Contains("normal"))
                {
                    material.FindChannel("Normal")?.SetTexture(0, tex);
                }

                if (textureNameTrimmed.ToLower().Contains("ao"))
                {
                    material.FindChannel("Occlusion")?.SetTexture(0, tex);
                }

                if (textureNameTrimmed.ToLower().Contains("emissive"))
                {
                    material.FindChannel("Emissive")?.SetTexture(0, tex);
                }
            }

            return material;
        }


        private class AttributeExportInfo
        {
            public string GltfAccessorName { get; set; }

            public int NumComponents { get; set; }

            public bool Resize { get; set; }
        }

        private static IDictionary<string, AttributeExportInfo> AccessorInfo = new Dictionary<string, AttributeExportInfo>
        {
            ["POSITION"] = new AttributeExportInfo
            {
                GltfAccessorName = "POSITION",
                NumComponents = 3,
                Resize = true,
            },
            ["NORMAL"] = new AttributeExportInfo
            {
                GltfAccessorName = "NORMAL",
                NumComponents = 3,
                Resize = false,
            },
            ["TEXCOORD"] = new AttributeExportInfo
            {
                GltfAccessorName = "TEXCOORD_0",
                NumComponents = 2,
            },
        };

        private float[] ReadAttributeBuffer(VBIB vbib, VertexBuffer buffer, VertexAttribute attribute)
            => Enumerable.Range(0, (int)buffer.Count)
                .SelectMany(i => vbib.ReadVertexAttribute(i, buffer, attribute))
                .ToArray();

        private int[] ReadIndices(IndexBuffer indexBuffer)
        {
            var indices = new int[indexBuffer.Count];

            if (indexBuffer.Size == 4)
            {
                System.Buffer.BlockCopy(indexBuffer.Buffer, 0, indices, 0, indexBuffer.Buffer.Length);
            }
            else if (indexBuffer.Size == 2)
            {
                var shortIndices = new short[indexBuffer.Count];
                System.Buffer.BlockCopy(indexBuffer.Buffer, 0, shortIndices, 0, indexBuffer.Buffer.Length);
                indices = Array.ConvertAll(shortIndices, i => (int)i);
            }

            return indices;
        }

        private (Vector3[] Normals, Vector4[] Tangents) DecompressNormalTangents(Vector4[] compressedNormalsTangents)
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

        private Vector3 DecompressNormal(Vector2 compressedNormal)
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

        private Vector4 DecompressTangent(Vector2 compressedTangent)
        {
            var outputNormal = DecompressNormal(compressedTangent);
            var tSign = compressedTangent.Y - 128.0f < 0 ? -1.0f : 1.0f;

            return new Vector4(outputNormal.X, outputNormal.Y, outputNormal.Z, tSign);
        }

        // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
        // Also divides by 100, gltf units are in meters, source engine units are in inches
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#coordinate-system-and-units
        private Vector3[] ToVector3Array(float[] buffer, bool swapAxes = true, bool resize = false)
        {
            var vectorArray = new Vector3[buffer.Length / 3];

            var yIndex = swapAxes ? 2 : 1;
            var zIndex = swapAxes ? 1 : 2;

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector3(buffer[i * 3], buffer[(i * 3) + yIndex], buffer[(i * 3) + zIndex]);
            }

            if (resize)
            {
                for (var i = 0; i < vectorArray.Length; i++)
                {
                    vectorArray[i] = vectorArray[i] * 0.0254f;
                }
            }

            return vectorArray;
        }

        private Vector2[] ToVector2Array(float[] buffer)
        {
            var vectorArray = new Vector2[buffer.Length / 2];

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector2(buffer[i * 2], buffer[(i * 2) + 1]);
            }

            return vectorArray;
        }

        private Vector4[] ToVector4Array(float[] buffer)
        {
            var vectorArray = new Vector4[buffer.Length / 4];

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector4(buffer[i * 4], buffer[(i * 4) + 1], buffer[(i * 4) + 2], buffer[(i * 4) + 3]);
            }

            return vectorArray;
        }
    }
}
