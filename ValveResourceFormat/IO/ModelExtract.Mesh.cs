using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Datamodel;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Utils;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;

namespace ValveResourceFormat.IO;

partial class ModelExtract
{
    public List<(HullDescriptor Hull, string FileName)> PhysHullsToExtract { get; } = [];
    public List<(MeshDescriptor Mesh, string FileName)> PhysMeshesToExtract { get; } = [];
    public List<RenderMeshExtractConfiguration> RenderMeshesToExtract { get; } = [];
    public Dictionary<string, Material.VsInputSignature> MaterialInputSignatures { get; } = [];

    public string[] PhysicsSurfaceNames { get; private set; }
    public HashSet<string>[] PhysicsCollisionTags { get; private set; }

    public HashSet<SurfaceTagCombo> SurfaceTagCombos { get; } = [];
    public Func<SurfaceTagCombo, string> PhysicsToRenderMaterialNameProvider { get; init; }
    public Vector3 Translation { get; set; }

    public readonly struct DatamodelRenderMeshExtractOptions
    {
        /// <summary>
        /// Split draw calls into sub-meshes named draw0, draw1, draw2...
        /// </summary>
        public bool SplitDrawCallsIntoSeparateSubmeshes { get; init; }

        /// <summary>
        /// Pre-parsed input signatures used to map DirectX semantic names to engine semantic names.
        /// </summary>
        public Dictionary<string, Material.VsInputSignature> MaterialInputSignatures { get; init; }
    }

    public record struct RenderMeshExtractConfiguration(Mesh Mesh, string Name, int Index, string FileName, ImportFilter ImportFilter = default);

    public sealed record SurfaceTagCombo(string SurfacePropName, HashSet<string> InteractAsStrings)
    {
        public SurfaceTagCombo(string surfacePropName, string[] collisionTags)
            : this(surfacePropName, new HashSet<string>(collisionTags))
        { }

        public string StringMaterial => string.Join('+', InteractAsStrings) + '$' + SurfacePropName;
        public override int GetHashCode() => StringMaterial.GetHashCode(StringComparison.OrdinalIgnoreCase);
        public bool Equals(SurfaceTagCombo other) => GetHashCode() == other.GetHashCode();
    }

    string GetDmxFileName_ForEmbeddedMesh(string subString, int number = 0)
    {
        var fileName = GetModelName();
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

    private void EnqueueMeshes()
    {
        EnqueueRenderMeshes();
        EnqueuePhysMeshes();
    }

    private void EnqueueRenderMeshes()
    {
        if (model == null)
        {
            return;
        }

        GrabMaterialInputSignatures(modelResource);

        var i = 0;
        foreach (var embedded in model.GetEmbeddedMeshes())
        {
            embedded.Mesh.VBIB = model.RemapBoneIndices(embedded.Mesh.VBIB, embedded.MeshIndex);
            RenderMeshesToExtract.Add(new(embedded.Mesh, embedded.Name, embedded.MeshIndex, GetDmxFileName_ForEmbeddedMesh(embedded.Name, i++)));
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            using var resource = fileLoader.LoadFileCompiled(reference.MeshName);

            if (resource is null)
            {
                continue;
            }

            GrabMaterialInputSignatures(resource);

            var mesh = (Mesh)resource.DataBlock;
            mesh.VBIB = model.RemapBoneIndices(mesh.VBIB, reference.MeshIndex);
            model.SetExternalMeshData(mesh);

            RenderMeshesToExtract.Add(new(mesh, reference.MeshName, reference.MeshIndex, GetDmxFileName_ForReferenceMesh(reference.MeshName)));
        }
    }

    internal void GrabMaterialInputSignatures(Resource resource)
    {
        var materialReferences = resource?.ExternalReferences?.ResourceRefInfoList.Where(static r => r.Name[^4..] == "vmat");
        foreach (var material in materialReferences ?? [])
        {
            using var materialResource = fileLoader.LoadFileCompiled(material.Name);
            MaterialInputSignatures[material.Name] = (materialResource?.DataBlock as Material)?.InputSignature ?? Material.VsInputSignature.Empty;
        }
    }

    private void EnqueuePhysMeshes()
    {
        if (physAggregateData == null)
        {
            return;
        }

        var knownKeys = StringToken.InvertedTable;

        PhysicsSurfaceNames = physAggregateData.SurfacePropertyHashes.Select(hash =>
        {
            knownKeys.TryGetValue(hash, out var name);
            return name ?? hash.ToString(CultureInfo.InvariantCulture);
        }).ToArray();


        PhysicsCollisionTags = physAggregateData.CollisionAttributes.Select(attributes =>
            (attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings")).ToHashSet()
        ).ToArray();

        var i = 0;
        foreach (var physicsPart in physAggregateData.Parts)
        {
            foreach (var hull in physicsPart.Shape.Hulls)
            {
                PhysHullsToExtract.Add((hull, GetDmxFileName_ForEmbeddedMesh("hull", i++)));

                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[hull.SurfacePropertyIndex],
                    PhysicsCollisionTags[hull.CollisionAttributeIndex]
                ));
            }

            foreach (var mesh in physicsPart.Shape.Meshes)
            {
                PhysMeshesToExtract.Add((mesh, GetDmxFileName_ForEmbeddedMesh("phys", i++)));

                SurfaceTagCombos.Add(new SurfaceTagCombo(
                    PhysicsSurfaceNames[mesh.SurfacePropertyIndex],
                    PhysicsCollisionTags[mesh.CollisionAttributeIndex]
                ));

                foreach (var surfaceIndex in mesh.Shape.Materials)
                {
                    SurfaceTagCombos.Add(new SurfaceTagCombo(
                        PhysicsSurfaceNames[surfaceIndex],
                        PhysicsCollisionTags[mesh.CollisionAttributeIndex]
                    ));
                }
            }
        }
    }

    public static IEnumerable<ContentFile> GetContentFiles_DrawCallSplit(Resource aggregateModelResource, IFileLoader fileLoader, Vector3[] drawOrigins, int drawCallCount)
    {
        var extract = new ModelExtract(aggregateModelResource, fileLoader) { Type = ModelExtractType.Map_AggregateSplit };
        Debug.Assert(extract.RenderMeshesToExtract.Count == 1);

        if (extract.RenderMeshesToExtract.Count == 0)
        {
            yield break;
        }

        var (mesh, name, index, fileName, _) = extract.RenderMeshesToExtract[0];

        var options = new DatamodelRenderMeshExtractOptions
        {
            MaterialInputSignatures = extract.MaterialInputSignatures,
            SplitDrawCallsIntoSeparateSubmeshes = true
        };

        byte[] sharedDmxExtractMethod() => ToDmxMesh(
            mesh,
            Path.GetFileNameWithoutExtension(fileName),
            options
        );

        var sharedMeshExtractConfiguration = new RenderMeshExtractConfiguration(mesh, name, index, fileName, new(true, new(1)));
        extract.RenderMeshesToExtract.Clear();
        extract.RenderMeshesToExtract.Add(sharedMeshExtractConfiguration);

        for (var i = 0; i < drawCallCount; i++)
        {
            sharedMeshExtractConfiguration.ImportFilter.Filter.Clear();
            sharedMeshExtractConfiguration.ImportFilter.Filter.Add("draw" + i);

            extract.Translation = drawOrigins.Length > i
                ? -1 * drawOrigins[i]
                : Vector3.Zero;

            var vmdl = new ContentFile
            {
                Data = Encoding.UTF8.GetBytes(extract.ToValveModel()),
                FileName = GetFragmentModelName(extract.GetModelName(), i),
            };

            if (i == 0)
            {
                vmdl.AddSubFile(Path.GetFileName(fileName), sharedDmxExtractMethod);
            }

            yield return vmdl;
        }
    }

    public static string GetFragmentModelName(string aggModelName, int drawCallIndex)
    {
        const string vmdlExt = ".vmdl";
        return aggModelName[..^vmdlExt.Length] + "_draw" + drawCallIndex + vmdlExt;
    }

    private static void FillDatamodelVertexData(VBIB.OnDiskBufferData vertexBuffer, DmeVertexData vertexData, Material.VsInputSignature materialInputSignature)
    {
        var indices = Enumerable.Range(0, (int)vertexBuffer.ElementCount).ToArray(); // May break with non-unit strides, non-tri faces

        foreach (var attribute in vertexBuffer.InputLayoutFields)
        {
            var attributeFormat = VBIB.GetFormatInfo(attribute);
            var semantic = attribute.SemanticName.ToLowerInvariant() + "$" + attribute.SemanticIndex;

            if (attribute.SemanticName is "NORMAL")
            {
                var (normals, tangents) = VBIB.GetNormalTangentArray(vertexBuffer, attribute);
                vertexData.AddIndexedStream(semantic, normals, indices);

                if (tangents.Length > 0)
                {
                    vertexData.AddIndexedStream("tangent$" + attribute.SemanticIndex, tangents, indices);
                }

                continue;
            }
            else if (attribute.SemanticName is "BLENDINDICES")
            {
                vertexData.JointCount = 4;

                var blendIndices = VBIB.GetBlendIndicesArray(vertexBuffer, attribute);
                vertexData.AddStream(semantic, Array.ConvertAll(blendIndices, i => (int)i));
                continue;
            }
            else if (attribute.SemanticName is "BLENDWEIGHT" or "BLENDWEIGHTS")
            {
                var vectorWeights = VBIB.GetBlendWeightsArray(vertexBuffer, attribute);
                var flatWeights = MemoryMarshal.Cast<Vector4, float>(vectorWeights).ToArray();

                vertexData.AddStream("blendweights$" + attribute.SemanticIndex, flatWeights);
                continue;
            }

            if (materialInputSignature.Elements.Length > 0)
            {
                var insgElement = Material.FindD3DInputSignatureElement(materialInputSignature, attribute.SemanticName, attribute.SemanticIndex);

                // Use engine semantics for attributes that need them
                if (insgElement.Semantic is "VertexPaintBlendParams" or "VertexPaintTintColor")
                {
                    semantic = insgElement.Semantic + "$0";
                }
            }

            switch (attributeFormat.ElementCount)
            {
                case 1:
                    var scalar = VBIB.GetScalarAttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, scalar, indices);
                    break;
                case 2:
                    var vec2 = VBIB.GetVector2AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec2, indices);
                    break;
                case 3:
                    var vec3 = VBIB.GetVector3AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec3, indices);
                    break;
                case 4:
                    var vec4 = VBIB.GetVector4AttributeArray(vertexBuffer, attribute);
                    vertexData.AddIndexedStream(semantic, vec4, indices);
                    break;
                default:
                    throw new NotImplementedException($"Stream {semantic} has an unexpected number of components: {attributeFormat.ElementCount}.");
            }
        }

        if (vertexData.VertexFormat.Contains("blendindices$0") && !vertexData.VertexFormat.Contains("blendweights$0"))
        {
            var blendIndicesLength = vertexData.TryGetValue("blendindices$0", out var blendIndices)
                ? ((ICollection<int>)blendIndices).Count
                : throw new InvalidOperationException("blendindices$0 stream not found");
            vertexData.AddStream("blendweights$0", Enumerable.Repeat(1f, blendIndicesLength).ToArray());
        }
    }

    public static byte[] ToDmxMesh(Mesh mesh, string name, DatamodelRenderMeshExtractOptions options = default)
    {
        using var dmx = ConvertMeshToDatamodelMesh(mesh, name, options);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    public static Datamodel.Datamodel ConvertMeshToDatamodelMesh(Mesh mesh, string name, DatamodelRenderMeshExtractOptions options)
    {
        var mdat = mesh.Data;
        var mbuf = mesh.VBIB;
        var indexBuffers = mbuf.IndexBuffers.Select(ib => new Lazy<int[]>(() => GltfModelExporter.ReadIndices(ib, 0, (int)ib.ElementCount, 0))).ToArray();

        var datamodel = new Datamodel.Datamodel("model", 22);
        DmxModelMultiVertexBufferLayout(name, mbuf.VertexBuffers.Count, out var dmeModel, out var dags, out var dmeVertexBuffers);

        var materialInputSignature = Material.VsInputSignature.Empty;
        var drawCallIndex = 0;

        foreach (var sceneObject in mdat.GetArray("m_sceneObjects"))
        {
            foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
            {
                var vertexBufferInfo = drawCall.GetArray("m_vertexBuffers")[0]; // In what situation can we have more than 1 vertex buffer per draw call?
                var vertexBufferIndex = vertexBufferInfo.GetInt32Property("m_hBuffer");

                var indexBufferInfo = drawCall.GetSubCollection("m_indexBuffer");
                var indexBufferIndex = indexBufferInfo.GetInt32Property("m_hBuffer");
                ReadOnlySpan<int> indexBuffer = indexBuffers[indexBufferIndex].Value;

                var material = drawCall.GetProperty<string>("m_material") ?? drawCall.GetProperty<string>("m_pMaterial");

                if (material != null && options.MaterialInputSignatures != null && (materialInputSignature.Elements == null || materialInputSignature.Elements.Length == 0))
                {
                    materialInputSignature = options.MaterialInputSignatures.GetValueOrDefault(material);
                }

                if (material == null && Mesh.IsOccluder(drawCall))
                {
                    material = "materials/tools/toolsoccluder.vmat";
                }

                var baseVertex = drawCall.GetInt32Property("m_nBaseVertex");
                var startIndex = drawCall.GetInt32Property("m_nStartIndex");
                var indexCount = drawCall.GetInt32Property("m_nIndexCount");

                var dag = dags[vertexBufferIndex];

                if (options.SplitDrawCallsIntoSeparateSubmeshes)
                {
                    if (drawCallIndex > 0)
                    {
                        // new submesh with same vertex buffer as first submesh
                        dag = [];
                        dmeModel.Children.Add(dag);
                        dmeModel.JointList.Add(dag);
                        dag.Shape.CurrentState = dmeVertexBuffers[vertexBufferIndex];
                        dag.Shape.BaseStates.Add(dmeVertexBuffers[vertexBufferIndex]);
                    }

                    dag.Shape.Name = "draw" + drawCallIndex;
                }

                GenerateTriangleFaceSetFromIndexBuffer(
                    dag,
                    indexBuffer[startIndex..(startIndex + indexCount)],
                    baseVertex,
                    material,
                    $"{startIndex}..{startIndex + indexCount}"
                );

                drawCallIndex++;
            }
        }

        for (var i = 0; i < mbuf.VertexBuffers.Count; i++)
        {
            FillDatamodelVertexData(mbuf.VertexBuffers[i], dmeVertexBuffers[i], materialInputSignature);
        }

        TieElementRoot(datamodel, dmeModel);
        return datamodel;
    }

    public byte[] ToDmxMesh(HullDescriptor hull)
    {
        var uniformSurface = PhysicsSurfaceNames[hull.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[hull.CollisionAttributeIndex];
        // https://github.com/ValveResourceFormat/ValveResourceFormat/issues/660#issuecomment-1795499191
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(hull.Shape, hull.UserFriendlyName, uniformSurface, uniformCollisionTags, fixRenderMeshCompileCrash);
    }

    public byte[] ToDmxMesh(MeshDescriptor mesh)
    {
        var uniformSurface = PhysicsSurfaceNames[mesh.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[mesh.CollisionAttributeIndex];
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(mesh.Shape, mesh.UserFriendlyName, uniformSurface, uniformCollisionTags, PhysicsSurfaceNames, fixRenderMeshCompileCrash);
    }

    public static byte[] ToDmxMesh(RnShapes.Hull hull, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        bool appendVertexNormalStream = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        // n-gon face set
        var faceSet = new DmeFaceSet() { Name = "hull faces" };
        faceSet.Material.MaterialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
        dag.Shape.FaceSets.Add(faceSet);

        var edges = hull.GetEdges();
        var faces = hull.GetFaces();
        var vertexPositions = hull.GetVertexPositions().ToArray();

        Debug.Assert(faces.Length + vertexPositions.Length == (edges.Length / 2) + 2);

        foreach (var face in faces)
        {
            var startEdge = face.Edge;
            var currentEdge = startEdge;
            do
            {
                var e = edges[currentEdge];
                faceSet.Faces.Add(e.Origin);
                currentEdge = e.Next;
            }
            while (currentEdge != startEdge);

            faceSet.Faces.Add(-1);
        }

        var indices = Enumerable.Range(0, vertexPositions.Length * 3).ToArray();
        vertexData.AddIndexedStream("position$0", vertexPositions, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), vertexPositions.Length).ToArray(), indices);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList,
        bool appendVertexNormalStream = false)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxModelBaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        var triangles = mesh.GetTriangles();

        if (mesh.Materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            GenerateTriangleFaceSet(dag, 0, triangles.Length, materialName);
        }
        else
        {
            Debug.Assert(mesh.Materials.Length == triangles.Length);
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

        var indices = new int[triangles.Length * 3];
        for (var t = 0; t < triangles.Length; t++)
        {
            var triangle = triangles[t];
            indices[t * 3] = triangle.X;
            indices[t * 3 + 1] = triangle.Y;
            indices[t * 3 + 2] = triangle.Z;
        }

        var vertices = mesh.GetVertices().ToArray();

        vertexData.AddIndexedStream("position$0", vertices, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), vertices.Length).ToArray(), indices);
        }

        TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "keyvalues2", 4);

        return stream.ToArray();
    }

    private static void DmxModelBaseLayout(string name, out DmeModel dmeModel, out DmeDag dag, out DmeVertexData vertexData)
    {
        DmxModelMultiVertexBufferLayout(name, 1, out dmeModel, out var dags, out var dmeVertexBuffers);
        dag = dags[0];
        vertexData = dmeVertexBuffers[0];
    }

    private static void DmxModelMultiVertexBufferLayout(string name, int vertexBufferCount,
        out DmeModel dmeModel, out DmeDag[] dags, out DmeVertexData[] dmeVertexBuffers)
    {
        dmeModel = new DmeModel() { Name = name };
        dags = new DmeDag[vertexBufferCount];
        dmeVertexBuffers = new DmeVertexData[vertexBufferCount];

        for (var i = 0; i < vertexBufferCount; i++)
        {
            // dmx requires one dag per vertex buffer
            var dag = dags[i] = new DmeDag() { Name = name };
            dmeModel.Children.Add(dag);
            dmeModel.JointList.Add(dag);

            var transformList = new DmeTransformsList();
            transformList.Transforms.Add(new DmeTransform());
            dmeModel.BaseStates.Add(transformList);

            var vertexData = dmeVertexBuffers[i] = new DmeVertexData { Name = "bind" };
            dag.Shape.Name = name;
            dag.Shape.CurrentState = vertexData;
            dag.Shape.BaseStates.Add(vertexData);
        }
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

    private static void GenerateTriangleFaceSetFromIndexBuffer(DmeDag dag, ReadOnlySpan<int> indices, int baseVertex,
        string material, string name)
    {
        var faceSet = new DmeFaceSet() { Name = name };
        dag.Shape.FaceSets.Add(faceSet);

        for (var i = 0; i < indices.Length; i += 3)
        {
            faceSet.Faces.Add(baseVertex + indices[i]);
            faceSet.Faces.Add(baseVertex + indices[i + 1]);
            faceSet.Faces.Add(baseVertex + indices[i + 2]);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    private static void TieElementRoot(Datamodel.Datamodel dmx, DmeModel dmeModel)
    {
        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeModel,
            ["model"] = dmeModel,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["source"] = $"Generated with {ValveResourceFormat.Utils.StringToken.VRF_GENERATOR}",
            }
        };
    }
}
