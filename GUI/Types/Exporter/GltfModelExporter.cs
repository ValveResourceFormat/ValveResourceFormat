using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using GUI.Types.Renderer;
using GUI.Utils;
using SharpGLTF.Schema2;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Exporter
{
    using static ValveResourceFormat.Blocks.VBIB;
    using VMesh = ValveResourceFormat.ResourceTypes.Mesh;

    public class GltfModelExporter
    {
        public void ExportToFile(string fileName, VMesh mesh, VrfGuiContext context)
        {
            var exportedModel = ModelRoot.CreateModel();
            var scene = exportedModel.UseScene("Default");

            var exportedMesh = CreateGltfMesh(mesh, exportedModel);
            scene.CreateNode("Mesh")
                .WithMesh(exportedMesh);

            exportedModel.Save(fileName);
        }

        private Mesh CreateGltfMesh(VMesh vmesh, ModelRoot model)
        {
            var data = vmesh.GetData();
            var vbib = vmesh.VBIB;

            var mesh = model.CreateMesh("Mesh");

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
                                var normals = DecompressNormals(vectors);
                                primitive.WithVertexAccessor(accessorInfo.GltfAccessorName, normals);
                            }
                            else if (accessorInfo.NumComponents == 3)
                            {
                                var vectors = ToVector3Array(buffer, attribute.Type, true, accessorInfo.Resize);
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
                    //var primitive = mesh.CreatePrimitive()
                    //    .WithVertexAccessor("POSITION", positions)
                    //    .WithIndicesAccessor(PrimitiveType.TRIANGLES, indices);
                    //.WithMaterial(material);
                }
            }

            return mesh;
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

        private Vector3[] DecompressNormals(Vector4[] compressedNormalsTangents)
        {
            var normals = new Vector3[compressedNormalsTangents.Length];

            for (var i = 0; i < normals.Length; i++)
            {
                var inputNormal = compressedNormalsTangents[i];

                // Ported from compression.incl shader
                var ztSigns = new Vector2(-(float)Math.Floor((inputNormal.X - 0.5f) / 0.496f), -(float)Math.Floor((inputNormal.Y - 0.5f) / 0.496f));
                var xyAbs = (new Vector2(Math.Abs(inputNormal.X - 0.5f), Math.Abs(inputNormal.Y - 0.5f)) - ztSigns) / 0.496f;
                var xySigns = new Vector2(-(float)Math.Floor((xyAbs.X - 0.25f) / 0.246f), -(float)Math.Floor((xyAbs.Y - 0.25f) / 0.246f));

                var outputXy = (new Vector2(Math.Abs(xyAbs.X - 0.25f), Math.Abs(xyAbs.Y - 0.25f)) - xySigns) / 0.246f;
                var outputZ = 1f - outputXy.X - outputXy.Y;
                var outputNormal = new Vector3(outputXy.X, outputZ, outputXy.Y);
                outputNormal = outputNormal / outputNormal.Length();

                outputNormal *= (new Vector3(xySigns.X, ztSigns.X, xySigns.Y) * -2f) + new Vector3(1);

                normals[i] = outputNormal / outputNormal.Length();

                //float fOne = 1.0f;
                //Vector3 outputNormal = Vector3.Zero;

                //Vector2 ztSigns = -floor((inputNormal.xy - 128.0f) / 127.0f);      // sign bits for zs and binormal (1 or 0)  set-less-than (slt) asm instruction
                //Vector2 xyAbs = abs(inputNormal.xy - 128.0f) - ztSigns;     // 0..127
                //Vector2 xySigns = -floor((xyAbs - 64.0f) / 63.0f);             // sign bits for xs and ys (1 or 0)
                //outputNormal.xy = (abs(xyAbs - 64.0f) - xySigns) / 63.0f;   // abs({nX, nY})

                //outputNormal.z = 1.0f - outputNormal.x - outputNormal.y;       // Project onto x+y+z=1
                //outputNormal.xyz = normalize(outputNormal.xyz);                // Normalize onto unit sphere

                //outputNormal.xy *= mix(vec2(fOne, fOne), vec2(-fOne, -fOne), xySigns);                // Restore x and y signs
                //outputNormal.z *= mix(fOne, -fOne, ztSigns.x);                // Restore z sign

                //return normalize(outputNormal);
            }

            return normals;
        }

        // NOTE: Swaps Y and Z axes - gltf up axis is Y (source engine up is Z)
        // Also divides by 100, gltf units are in meters, source engine units are in inches
        // https://github.com/KhronosGroup/glTF/tree/master/specification/2.0#coordinate-system-and-units
        private Vector3[] ToVector3Array(float[] buffer, DXGI_FORMAT originalType, bool swapAxes = true, bool resize = false)
        {
            var stride = originalType == DXGI_FORMAT.R8G8B8A8_UNORM
                ? 4
                : 3;

            var vectorArray = new Vector3[buffer.Length / stride];

            var yIndex = swapAxes ? 2 : 1;
            var zIndex = swapAxes ? 1 : 2;

            for (var i = 0; i < vectorArray.Length; i++)
            {
                vectorArray[i] = new Vector3(
                    buffer[i * stride],
                    buffer[(i * stride) + yIndex],
                    buffer[(i * stride) + zIndex]);
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
