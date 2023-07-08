using System;
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using Datamodel;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using System.IO;
using System.Text;
using System.Numerics;
using System.Collections.Generic;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.IO;

public class ModelExtract
{
    private readonly Model model;
    private readonly PhysAggregateData physAggregateData;
    private readonly IFileLoader fileLoader;
    private readonly string fileName;

    public List<(Mesh Mesh, string FileName)> RenderMeshesToExtract { get; } = new();
    public List<(MeshDescriptor Mesh, string FileName)> PhysMeshesToExtract { get; } = new();

    public ModelExtract(Model model, IFileLoader fileLoader)
    {
        this.model = model;
        this.fileLoader = fileLoader;

        var refPhysics = model.GetReferencedPhysNames().FirstOrDefault();
        if (refPhysics != null)
        {
            using var physResource = fileLoader.LoadFile(refPhysics + "_c");
            physAggregateData = (PhysAggregateData)physResource.DataBlock;
        }
        else
        {
            physAggregateData = model.GetEmbeddedPhys();
        }

        LoadMeshes();
    }

    /// <summary>
    /// Extract a single mesh to vmdl+dmx.
    /// </summary>
    /// <param name="mesh">Mesh data</param>
    /// <param name="fileName">File name of the mesh e.g "models/my_mesh.vmesh"</param>
    public ModelExtract(Mesh mesh, string fileName)
    {
        RenderMeshesToExtract.Add((mesh, GetDmxFileName_ForReferenceMesh(fileName)));
        this.fileName = Path.ChangeExtension(fileName, ".vmdl");
    }

    public ModelExtract(PhysAggregateData physAggregateData, string fileName)
    {
        this.physAggregateData = physAggregateData;
        this.fileName = fileName;
        LoadMeshes();
    }

    private void LoadMeshes()
    {
        RenderMeshesToExtract.AddRange(GetExportableRenderMeshes());
        PhysMeshesToExtract.AddRange(GetExportablePhysMeshes());
    }

    public ContentFile ToContentFile()
    {
        var vmdl = new ContentFile
        {
            Data = Encoding.UTF8.GetBytes(ToValveModel()),
            FileName = GetFileName(),
        };

        foreach (var renderMesh in RenderMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(renderMesh.FileName),
                () => ToDmxMesh(renderMesh.Mesh, Path.GetFileNameWithoutExtension(renderMesh.FileName))
            );
        }

        foreach (var physMesh in PhysMeshesToExtract)
        {
            vmdl.AddSubFile(
                Path.GetFileName(physMesh.FileName),
                () => ToDmxMesh(physMesh.Mesh.Shape, Path.GetFileNameWithoutExtension(physMesh.FileName))
            );
        }

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

        if (RenderMeshesToExtract.Count != 0)
        {
            var (renderMeshListSingleton, renderMeshList) = MakeListNode("RenderMeshList");
            root.Children.AddProperty(null, new KVValue(KVType.OBJECT, renderMeshListSingleton));

            foreach (var renderMesh in RenderMeshesToExtract)
            {
                renderMeshList.AddProperty(null, new KVValue(KVType.OBJECT,
                    MakeNode(
                        "RenderMeshFile",
                        ("filename", new KVValue(KVType.STRING, renderMesh.FileName))
                    )
                ));
            }
        }

        if (PhysMeshesToExtract.Count != 0)
        {
            var (physicsShapeListSingleton, physicsShapeList) = MakeListNode("PhysicsShapeList");
            root.Children.AddProperty(null, new KVValue(KVType.OBJECT, physicsShapeListSingleton));

            foreach (var (physMesh, fileName) in PhysMeshesToExtract)
            {
                var surfacePropHash = physAggregateData.SurfacePropertyHashes[physMesh.SurfacePropertyIndex];
                StringToken.InvertedTable.TryGetValue(surfacePropHash, out var surfacePropName);

                physicsShapeList.AddProperty(null, new KVValue(KVType.OBJECT,
                    MakeNode(
                        "PhysicsMeshFile",
                        ("filename", new KVValue(KVType.STRING, fileName)),
                        ("surface_prop", new KVValue(KVType.STRING, surfacePropName ?? "default")),
                        ("name", new KVValue(KVType.STRING, physMesh.UserFriendlyName))
                    )
                ));
            }
        }

        return new KV3File(kv, format: "modeldoc32:version{c5dcef98-b629-46ab-88e3-a17c005c935e}").ToString();
    }

    public string GetFileName()
        => model?.Data.GetProperty<string>("m_name")
            ?? fileName;

    string GetDmxFileName_ForEmbeddedMesh(string subString, int number = 0)
    {
        var fileName = GetFileName();
        return (Path.GetDirectoryName(fileName)
            + Path.DirectorySeparatorChar
            + Path.GetFileNameWithoutExtension(fileName)
            + "_"
            + subString
            + (number > 0 ? number : string.Empty)
            + ".dmx")
            .Replace('\\', '/');
    }

    static string GetDmxFileName_ForReferenceMesh(string fileName)
        => Path.ChangeExtension(fileName, ".dmx").Replace('\\', '/');

    private IEnumerable<(Mesh Mesh, string FileName)> GetExportableRenderMeshes()
    {
        if (model == null)
        {
            yield break;
        }

        var i = 0;
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            yield return (embedded.Mesh, GetDmxFileName_ForEmbeddedMesh(embedded.Name, i++));
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            using var resource = fileLoader.LoadFile(reference.MeshName + "_c");
            yield return ((Mesh)resource.DataBlock, GetDmxFileName_ForReferenceMesh(reference.MeshName));
        }
    }

    private IEnumerable<(MeshDescriptor Mesh, string FileName)> GetExportablePhysMeshes()
    {
        if (physAggregateData == null)
        {
            yield break;
        }

        var i = 0;
        foreach (var physicsPart in physAggregateData.Parts)
        {
            foreach (var mesh in physicsPart.Shape.Meshes)
            {
                yield return (mesh, GetDmxFileName_ForEmbeddedMesh("phys", i++));
            }
        }
    }

    public static byte[] ToDmxMesh(Mesh mesh, string name)
    {
        var vertexBuffer = mesh.VBIB.VertexBuffers.First();
        var ib = mesh.VBIB.IndexBuffers.First();

        using var dmx = new Datamodel.Datamodel("model", 22);
        var dmeModel = new DmeModel() { Name = name };

        var dag = new DmeDag() { Name = name };
        dmeModel.Children.Add(dag);
        dmeModel.JointList.Add(dag);

        var transformList = new DmeTransformsList();
        transformList.Transforms.Add(new DmeTransform());
        dmeModel.BaseStates.Add(transformList);

        var vertexData = new DmeVertexData { Name = "bind" };
        dag.Shape.CurrentState = vertexData;
        dag.Shape.BaseStates.Add(vertexData);

        foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
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

            var semantic = attribute.SemanticName.ToLowerInvariant() + "$" + attribute.SemanticIndex;

            if (attribute.SemanticName == "NORMAL")
            {
                var vectors = GltfModelExporter.ToVector4Array(buffer);
                var decompressed = GltfModelExporter.DecompressNormalTangents(vectors);

                vertexData.AddStream<Vector3Array, Vector3>(semantic, decompressed.Normals, indices);
                vertexData.AddStream<Vector4Array, Vector4>("tangent$" + attribute.SemanticIndex, decompressed.Tangents, indices);
                continue;
            }

            if (numComponents == 4)
            {
                vertexData.AddStream<Vector4Array, Vector4>(semantic, GltfModelExporter.ToVector4Array(buffer), indices);
            }
            else if (numComponents == 3)
            {
                vertexData.AddStream<Vector3Array, Vector3>(semantic, GltfModelExporter.ToVector3Array(buffer), indices);
            }
            else if (numComponents == 2)
            {
                vertexData.AddStream<Vector2Array, Vector2>(semantic, GltfModelExporter.ToVector2Array(buffer), indices);
            }
            else if (numComponents == 1)
            {
                vertexData.AddStream<FloatArray, float>(semantic, buffer, indices);
            }
            else
            {
                throw new NotImplementedException($"Stream {semantic} has an unexpected number of components: {numComponents}.");
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

    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        var dmeModel = new DmeModel() { Name = name };

        var dag = new DmeDag() { Name = name };
        dmeModel.Children.Add(dag);
        dmeModel.JointList.Add(dag);

        var transformList = new DmeTransformsList();
        transformList.Transforms.Add(new DmeTransform());
        dmeModel.BaseStates.Add(transformList);

        var vertexData = new DmeVertexData { Name = "bind" };
        dag.Shape.CurrentState = vertexData;
        dag.Shape.BaseStates.Add(vertexData);

        GenerateTriangleFaceSet(dag, 0, mesh.Triangles.Length, "materials/dev/reflectivity_60.vmat");

        var indices = new int[mesh.Triangles.Length * 3];
        for (var i = 0; i < mesh.Triangles.Length; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                indices[i * 3 + j] = mesh.Triangles[i].Indices[j];
            }
        }

        vertexData.AddStream<Vector3Array, Vector3>("position$0", mesh.Vertices, indices);

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

    private static void GenerateTriangleFaceSet(DmeDag dag, int triangleStart, int triangleEnd, string material)
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
}
