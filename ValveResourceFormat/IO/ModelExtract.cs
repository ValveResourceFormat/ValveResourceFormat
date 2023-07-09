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
using System.Diagnostics;
using System.Globalization;

namespace ValveResourceFormat.IO;

public class ModelExtract
{
    private readonly Model model;
    private readonly PhysAggregateData physAggregateData;
    private readonly IFileLoader fileLoader;
    private readonly string fileName;

    public List<(Mesh Mesh, string FileName)> RenderMeshesToExtract { get; } = new();
    public List<(MeshDescriptor Mesh, string FileName)> PhysMeshesToExtract { get; } = new();

    public string[] PhysicsSurfaceNames { get; private set; }
    public HashSet<string>[] PhysicsCollisionTags { get; private set; }

    public record SurfaceTagCombo(string SurfacePropName, HashSet<string> InteractAsStrings)
    {
        public SurfaceTagCombo(string surfacePropName, string[] collisionTags)
            : this(surfacePropName, new HashSet<string>(collisionTags))
        { }

        public string StringMaterial => string.Join('+', InteractAsStrings) + '$' + SurfacePropName;
        public override int GetHashCode() => StringMaterial.GetHashCode(StringComparison.OrdinalIgnoreCase);
    }

    public HashSet<SurfaceTagCombo> SurfaceTagCombos { get; } = new();

    public enum ModelExtractType
    {
        Default,
        Map_PhysicsToRenderMesh,
        Map_AggregateSplit,
    }

    public ModelExtractType Type { get; init; } = ModelExtractType.Default;
    public Func<SurfaceTagCombo, string> PhysicsToRenderMaterialNameProvider { get; init; }

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

        if (physAggregateData != null)
        {
            PhysicsSurfaceNames = physAggregateData.SurfacePropertyHashes.Select(hash =>
            {
                StringToken.InvertedTable.TryGetValue(hash, out var name);
                return name ?? hash.ToString(CultureInfo.InvariantCulture);
            }).ToArray();


            PhysicsCollisionTags = physAggregateData.CollisionAttributes.Select(attributes =>
                attributes.GetArray<string>("m_InteractAsStrings").ToHashSet()
                ?? attributes.GetArray<string>("m_PhysicsTagStrings").ToHashSet()
            ).ToArray();

            foreach (var physMesh in PhysMeshesToExtract)
            {
                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[physMesh.Mesh.SurfacePropertyIndex],
                    PhysicsCollisionTags[physMesh.Mesh.CollisionAttributeIndex]
                ));

                foreach (var surfaceIndex in physMesh.Mesh.Shape.Materials)
                {
                    SurfaceTagCombos.Add(new SurfaceTagCombo(
                        PhysicsSurfaceNames[surfaceIndex],
                        PhysicsCollisionTags[physMesh.Mesh.CollisionAttributeIndex]
                    ));
                }
            }
        }
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
                () => ToDmxMesh(physMesh.Mesh)
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

        Lazy<KVObject> MakeLazyList(string className)
        {
            return new Lazy<KVObject>(() =>
            {
                var list = MakeListNode(className);
                root.Children.AddProperty(null, new KVValue(KVType.OBJECT, list.Node));
                return list.Children;
            });
        }

        var materialGroupList = MakeLazyList("MaterialGroupList");
        var renderMeshList = MakeLazyList("RenderMeshList");
        var physicsShapeList = MakeLazyList("PhysicsShapeList");

        if (RenderMeshesToExtract.Count != 0)
        {
            foreach (var renderMesh in RenderMeshesToExtract)
            {
                renderMeshList.Value.AddProperty(null, new KVValue(KVType.OBJECT,
                    MakeNode(
                        "RenderMeshFile",
                        ("filename", new KVValue(KVType.STRING, renderMesh.FileName))
                    )
                ));
            }
        }

        if (PhysMeshesToExtract.Count != 0)
        {
            if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
            {
                var globalReplace = PhysicsToRenderMaterialNameProvider == null;
                var remapTable = globalReplace ? null
                    : SurfaceTagCombos.ToDictionary(
                        combo => combo.StringMaterial,
                        combo => PhysicsToRenderMaterialNameProvider(combo)
                    );

                RemapMaterials(remapTable, globalReplace);
            }

            foreach (var (physMesh, fileName) in PhysMeshesToExtract)
            {
                var surfacePropName = PhysicsSurfaceNames[physMesh.SurfacePropertyIndex];
                var collisionTags = PhysicsCollisionTags[physMesh.CollisionAttributeIndex];

                if (Type == ModelExtractType.Map_PhysicsToRenderMesh)
                {
                    renderMeshList.Value.AddProperty(null, new KVValue(KVType.OBJECT,
                        MakeNode(
                            "RenderMeshFile",
                            ("filename", new KVValue(KVType.STRING, fileName))
                        )
                    ));

                    continue;
                }

                // TODO: per faceSet surface_prop
                physicsShapeList.Value.AddProperty(null, new KVValue(KVType.OBJECT,
                    MakeNode(
                        "PhysicsMeshFile",
                        ("filename", new KVValue(KVType.STRING, fileName)),
                        ("surface_prop", new KVValue(KVType.STRING, surfacePropName)),
                        ("collision_tags", new KVValue(KVType.STRING, string.Join(" ", collisionTags))),
                        ("name", new KVValue(KVType.STRING, physMesh.UserFriendlyName))
                    )
                ));
            }
        }

        return new KV3File(kv, format: "modeldoc32:version{c5dcef98-b629-46ab-88e3-a17c005c935e}").ToString();

        void RemapMaterials(
            IReadOnlyDictionary<string, string> remapTable = null,
            bool globalReplace = false,
            string globalDefault = "materials/tools/toolsnodraw.vmat")
        {
            var remaps = new KVObject(null, isArray: true);
            materialGroupList.Value.AddProperty(null, new KVValue(KVType.OBJECT,
                MakeNode(
                    "DefaultMaterialGroup",
                    ("remaps", new KVValue(KVType.ARRAY, remaps)),
                    ("use_global_default", new KVValue(KVType.BOOLEAN, globalReplace)),
                    ("global_default_material", new KVValue(KVType.STRING, globalDefault))
                )
            ));

            if (globalReplace || remapTable == null)
            {
                return;
            }

            foreach (var (from, to) in remapTable)
            {
                var remap = new KVObject(null);
                remap.AddProperty("from", new KVValue(KVType.STRING, from));
                remap.AddProperty("to", new KVValue(KVType.STRING, to));
                remaps.AddProperty(null, new KVValue(KVType.OBJECT, remap));
            }
        }
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

    public byte[] ToDmxMesh(MeshDescriptor mesh)
    {
        var uniformSurface = PhysicsSurfaceNames[mesh.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[mesh.CollisionAttributeIndex];
        return ToDmxMesh(mesh.Shape, mesh.UserFriendlyName, uniformSurface, uniformCollisionTags, PhysicsSurfaceNames);
    }

    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList)
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

        if (mesh.Materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            GenerateTriangleFaceSet(dag, 0, mesh.Triangles.Length, materialName);
        }
        else
        {
            Debug.Assert(mesh.Materials.Length == mesh.Triangles.Length);
            Debug.Assert(surfaceList.Length > 0);

            Span<DmeFaceSet> faceSets = new DmeFaceSet[surfaceList.Length];
            for (var t = 0; t < mesh.Materials.Length; t++)
            {
                var surfaceIndex = mesh.Materials[t];
                var faceSet = faceSets[surfaceIndex];

                if (faceSet == null)
                {
                    var surface = surfaceList[surfaceIndex];
                    faceSet = faceSets[surfaceIndex] = new DmeFaceSet()
                    {
                        Name = surface + '$' + surfaceIndex
                    };
                    faceSet.Material.MaterialName = new SurfaceTagCombo(surface, uniformCollisionTags).StringMaterial;
                    dag.Shape.FaceSets.Add(faceSet);
                }

                faceSet.Faces.Add(t * 3);
                faceSet.Faces.Add(t * 3 + 1);
                faceSet.Faces.Add(t * 3 + 2);
                faceSet.Faces.Add(-1);
            }
        }

        var indices = new int[mesh.Triangles.Length * 3];
        for (var t = 0; t < mesh.Triangles.Length; t++)
        {
            for (var i = 0; i < 3; i++)
            {
                indices[t * 3 + i] = mesh.Triangles[t].Indices[i];
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
