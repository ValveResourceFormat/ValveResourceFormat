using System;
using System.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.IO
{
    public static class MeshWriter
    {
        public static void WriteObject(StreamWriter objStream, StreamWriter mtlStream, string mtlFilename, Resource resource)
        {
            var mesh = resource.VBIB;

            const string header = "# Written by VRF - https://opensource.steamdb.info/ValveResourceFormat/";

            mtlStream.WriteLine(header);
            objStream.WriteLine(header);
            objStream.WriteLine($"# Vertex buffers: {mesh.VertexBuffers.Count}");
            objStream.WriteLine($"# Index buffers: {mesh.IndexBuffers.Count}");
            objStream.WriteLine($"mtllib {mtlFilename}.mtl");

            if (mesh.VertexBuffers.Count != mesh.IndexBuffers.Count)
            {
                throw new InvalidDataException("VertexBuffers.Count != IndexBuffers.Count");
            }

            var data = (BinaryKV3)resource.DataBlock;
            var sceneObjects = (KVObject)data.Data.Properties["m_sceneObjects"].Value;

            var indexCount = 1;

            for (var b = 0; b < sceneObjects.Properties.Count; b++)
            {
                var drawCalls = (KVObject)((KVObject)sceneObjects.Properties[b.ToString()].Value).Properties["m_drawCalls"].Value;

                for (var i = 0; i < drawCalls.Properties.Count; i++)
                {
                    var d = (KVObject)drawCalls.Properties[i.ToString()].Value;
                    var materialName = d.Properties["m_material"].Value.ToString();

                    var groupName = Path.GetFileNameWithoutExtension(materialName);
                    mtlStream.WriteLine($"newmtl {groupName}");
                    mtlStream.WriteLine("illum 2");
                    mtlStream.WriteLine($"map_Ka {groupName}.png");
                    mtlStream.WriteLine($"map_Kd {groupName}.png");

                    var f = (KVObject)d.Properties["m_indexBuffer"].Value;
                    var indexBufferId = Convert.ToUInt32(f.Properties["m_hBuffer"].Value);

                    var g = (KVObject)d.Properties["m_vertexBuffers"].Value;
                    var h = (KVObject)g.Properties["0"].Value; // TODO: Not just 0
                    var vertexBufferId = Convert.ToUInt32(h.Properties["m_hBuffer"].Value);

                    var vertexBuffer = mesh.VertexBuffers[(int)vertexBufferId];
                    objStream.WriteLine($"# Vertex Buffer {i}. Count: {vertexBuffer.Count}, Size: {vertexBuffer.Size}");
                    for (var j = 0; j < vertexBuffer.Count; j++)
                    {
                        foreach (var attribute in vertexBuffer.Attributes)
                        {
                            var result = mesh.ReadVertexAttribute(j, vertexBuffer, attribute);

                            switch (attribute.Name)
                            {
#pragma warning disable SA1011 // ClosingSquareBracketsMustBeSpacedCorrectly, rule kinda breaks in strings
                                case "POSITION":
                                    objStream.WriteLine($"v {result[0]:F6} {result[1]:F6} {result[2]:F6}");
                                    break;

                                case "NORMAL":
                                    objStream.WriteLine($"vn {result[0]:F6} {result[1]:F6} {result[2]:F6}");
                                    break;

                                case "TEXCOORD":
                                    objStream.WriteLine($"vt {result[0]:F6} {result[1]:F6}");
                                    break;
#pragma warning restore SA1011 // ClosingSquareBracketsMustBeSpacedCorrectly
                            }
                        }
                    }

                    var indexBuffer = mesh.IndexBuffers[(int)indexBufferId];

                    objStream.WriteLine($"# Index Buffer {i}. Count: {indexBuffer.Count}, Size: {indexBuffer.Size}");

                    objStream.WriteLine($"g {groupName}");
                    objStream.WriteLine($"usemtl {groupName}");

                    if (indexBuffer.Size == 2)
                    {
                        var indexArray = new ushort[indexBuffer.Count];
                        System.Buffer.BlockCopy(indexBuffer.Buffer, 0, indexArray, 0, indexBuffer.Buffer.Length);

                        for (var j = 0; j < indexBuffer.Count; j += 3)
                        {
                            objStream.WriteLine($"f {indexArray[j] + indexCount}/{indexArray[j] + indexCount}/{indexArray[j] + indexCount} {indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount} {indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}");
                        }
                    }
                    else if (indexBuffer.Size == 4)
                    {
                        var indexArray = new uint[indexBuffer.Count];
                        System.Buffer.BlockCopy(indexBuffer.Buffer, 0, indexArray, 0, indexBuffer.Buffer.Length);

                        for (var j = 0; j < indexBuffer.Count; j += 3)
                        {
                            objStream.WriteLine($"f {indexArray[j] + indexCount}/{indexArray[j] + indexCount}/{indexArray[j] + indexCount} {indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount}/{indexArray[j + 1] + indexCount} {indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}/{indexArray[j + 2] + indexCount}");
                        }
                    }
                    else
                    {
                        throw new InvalidDataException("Index size isn't 2 or 4, dafuq.");
                    }

                    indexCount += (int)vertexBuffer.Count;
                }
            }
        }
    }
}
