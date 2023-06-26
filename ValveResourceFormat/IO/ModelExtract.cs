using System;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using System.IO;
using System.Text;
using System.Numerics;

namespace ValveResourceFormat.IO;

public class ModelExtract
{
    private readonly Model model;
    private readonly IFileLoader fileLoader;

    public ModelExtract(Model model, IFileLoader fileLoader)
    {
        this.model = model;
        this.fileLoader = fileLoader;
    }

    public ContentFile ToContentFile()
    {
        throw new NotImplementedException();
    }

    public ContentFile ToBakedMapModel()
    {
        var vmdl = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveModel())
        };

        vmdl.AddSubFile(GetDmxFileName(), ToDMXMesh);
        return vmdl;
    }

    public string ToValveModel()
    {
        var kv = new KVObject(null);

        static KVObject MakeNode(string className, params (string Name, KVValue Value)[] properties)
        {
            var node = new KVObject(className);
            node.AddProperty("_class", new KVValue(KVType.STRING, className));
            foreach (var (name, value) in properties)
            {
                node.AddProperty(name, value);
            }
            return node;
        }

        static (KVObject Node, KVObject Children) MakeListNode(string className)
        {
            var children = new KVObject(null, isArray: true);
            var node = MakeNode(className, ("children", new KVValue(KVType.ARRAY, children)));
            return (node, children);
        }

        var root = MakeListNode("RootNode");
        kv.AddProperty("rootNode", new KVValue(KVType.OBJECT, root.Node));

        var (renderMeshListSingleton, renderMeshList) = MakeListNode("RenderMeshList");
        root.Children.AddProperty(null, new KVValue(KVType.OBJECT, renderMeshListSingleton));

        renderMeshList.AddProperty(null, new KVValue(KVType.OBJECT,
            MakeNode(
                "RenderMeshFile",
                ("filename", new KVValue(KVType.STRING, GetDmxFileName()))
            )
        ));

        return new KV3File(kv, format: "modeldoc32:version{c5dcef98-b629-46ab-88e3-a17c005c935e}").ToString();
    }

    public string GetDmxFileName()
    {
        return Path.ChangeExtension(Path.GetFileName(model.Data.GetProperty<string>("m_name")), ".dmx");
    }

    public byte[] ToDMXMesh()
    {
        var mesh = model.GetEmbeddedMeshes().FirstOrDefault();
        if (mesh.Mesh is null)
        {
            var referenceDesc = model.GetReferenceMeshNamesAndLoD().First();
            var referenceMesh = fileLoader.LoadFile(referenceDesc.MeshName + "_c");
            mesh = ((Mesh)referenceMesh.DataBlock, referenceDesc.MeshIndex, referenceDesc.MeshName);
        }
        var vertexBuffer = mesh.Mesh.VBIB.VertexBuffers.First();
        var ib = mesh.Mesh.VBIB.IndexBuffers.First();

        using var dmx = new Datamodel.Datamodel("model", 22);
        var dmeModel = new DmeModel() { Name = mesh.Name };

        var dag = new DmeDag() { Name = mesh.Name };
        dmeModel.Children.Add(dag);
        dmeModel.JointList.Add(dag);

        var transformList = new DmeTransformsList();
        transformList.Transforms.Add(new DmeTransform());
        dmeModel.BaseStates.Add(transformList);

        var vertexData = new DmeVertexData { Name = "bind" };
        dag.Shape.CurrentState = vertexData;
        dag.Shape.BaseStates.Add(vertexData);

        static void GenerateTriangleFaceSet(DmeDag dag, int triangleStart, int triangleEnd, string material)
        {
            var faceSet = new DmeFaceSet() { Name = triangleStart + "-" + triangleEnd };
            dag.Shape.FaceSets.Add(faceSet);

            for (var i = triangleStart; i < triangleEnd; i++)
            {
                faceSet.Faces.Add(i * 3);
                faceSet.Faces.Add(i * 3 + 1);
                faceSet.Faces.Add(i * 3 + 2);
                faceSet.Faces.Add(-1);
            }

            faceSet.Material.MaterialName = material;
        }

        foreach (var sceneObject in mesh.Mesh.Data.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");

                if (vertexBufferIndex != 0 || indexBufferIndex != 0)
                {
                    continue; // Skip this TODO
                }

                var material = drawCall.GetProperty<string>("m_material");
                var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                var indexCount = drawCall.GetInt32Property("m_nIndexCount");

                GenerateTriangleFaceSet(dag, startIndex / 3, (startIndex + indexCount) / 3, material);
            }
        }

        var indices = GltfModelExporter.ReadIndices(ib, 0, (int)ib.ElementCount, 0);

        foreach (var attribute in vertexBuffer.InputLayoutFields)
        {
            var buffer = GltfModelExporter.ReadAttributeBuffer(vertexBuffer, attribute);
            var numComponents = buffer.Length / (int)vertexBuffer.ElementCount;

            var name = attribute.SemanticName.ToLowerInvariant() + "$" + attribute.SemanticIndex;

            if (attribute.SemanticName == "NORMAL")
            {
                var vectors = GltfModelExporter.ToVector4Array(buffer);
                var decompressed = GltfModelExporter.DecompressNormalTangents(vectors);

                vertexData.AddStream<Vector3Array, Vector3>(name, decompressed.Normals, indices);
                vertexData.AddStream<Vector4Array, Vector4>("tangent$" + attribute.SemanticIndex, decompressed.Tangents, indices);
                continue;
            }

            if (numComponents == 4)
            {
                vertexData.AddStream<Vector4Array, Vector4>(name, GltfModelExporter.ToVector4Array(buffer), indices);
            }
            else if (numComponents == 3)
            {
                vertexData.AddStream<Vector3Array, Vector3>(name, GltfModelExporter.ToVector3Array(buffer), indices);
            }
            else if (numComponents == 2)
            {
                vertexData.AddStream<Vector2Array, Vector2>(name, GltfModelExporter.ToVector2Array(buffer), indices);
            }
            else if (numComponents == 1)
            {
                vertexData.AddStream<FloatArray, float>(name, buffer, indices);
            }
            else
            {
                throw new NotImplementedException($"Stream {name} has an unexpected number of components: {numComponents}.");
            }
        }

        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeModel,
            ["model"] = dmeModel,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["source"] = "Generated with VRF - https://vrf.steamdb.info/",
            }
        };

        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }
}
