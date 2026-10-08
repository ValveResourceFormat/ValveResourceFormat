using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.IO.ContentFormats.DmxModel;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics;
using ValveResourceFormat.Serialization.KeyValues;
using RnShapes = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;

namespace ValveResourceFormat.IO;

/// <summary>
/// Writes a model's collision shapes out as DMX: the hulls and meshes each physics part carries, and
/// the surface and collision tag markup naming the material they compile against.
/// </summary>
partial class ModelExtract
{
    /// <summary>
    /// Gets the list of physics hulls to be extracted with their output file names.
    /// </summary>
    /// <remarks>
    /// <see cref="Matrix4x4"/> transforms the hull's vertices from body-local space into bind-pose
    /// component space, matching the space a <c>PhysicsHullFile</c> node's DMX geometry is authored in.
    /// </remarks>
    public List<(HullDescriptor Hull, string FileName, string ParentBone, Matrix4x4 BindPose)> PhysHullsToExtract { get; } = [];

    /// <summary>
    /// Gets the list of physics meshes to be extracted with their output file names.
    /// </summary>
    /// <remarks>
    /// <see cref="Matrix4x4"/> transforms the mesh's vertices from body-local space into bind-pose
    /// component space, matching the space a <c>PhysicsMeshFile</c> node's DMX geometry is authored in.
    /// </remarks>
    public List<(MeshDescriptor Mesh, string FileName, string ParentBone, Matrix4x4 BindPose)> PhysMeshesToExtract { get; } = [];

    /// <summary>
    /// The physics surface property names, indexed by surface property index. Filled by
    /// <see cref="EnqueuePhysMeshes"/>, which every constructor runs.
    /// </summary>
    private string[] PhysicsSurfaceNames { get; set; } = [];

    /// <summary>
    /// The physics collision tag sets, indexed by collision attribute index. Filled by
    /// <see cref="EnqueuePhysMeshes"/>, which every constructor runs.
    /// </summary>
    private HashSet<string>[] PhysicsCollisionTags { get; set; } = [];

    /// <summary>
    /// The <c>scripts/collision_properties.txt</c> entry each collision attribute compiles from, by attribute index,
    /// or <see langword="null"/> for the default attribute and for attributes no entry reproduces.
    /// </summary>
    private string?[] PhysicsCollisionProperties { get; set; } = [];

    /// <summary>
    /// The distinct surface and collision tag combinations the enqueued shapes use.
    /// </summary>
    private HashSet<SurfaceTagCombo> SurfaceTagCombos { get; } = [];

    /// <summary>
    /// Gets the function to provide render material names for physics surface tags.
    /// </summary>
    public Func<SurfaceTagCombo, string>? PhysicsToRenderMaterialNameProvider { get; init; }

    private void EnqueuePhysMeshes()
    {
        if (physAggregateData == null)
        {
            return;
        }

        PhysicsSurfaceNames = physAggregateData.SurfacePropertyHashes.Select(StringToken.GetKnownString).ToArray();

        PhysicsCollisionTags = physAggregateData.CollisionAttributes.Select(attributes =>
            PhysAggregateData.GetInteractAsTags(attributes).ToHashSet()
        ).ToArray();

        // Fix index error on some old vphys files
        if (PhysicsSurfaceNames.Length == 0)
        {
            PhysicsSurfaceNames = [string.Empty];
        }

        if (PhysicsCollisionTags.Length == 0)
        {
            PhysicsCollisionTags = [[]];
        }

        PhysicsCollisionProperties = GetCollisionPropertyNames(physAggregateData.CollisionAttributes);

        var bindPoses = physAggregateData.BindPose;

        var numberedSurfaces = new HashSet<int>();
        var meshNames = physAggregateData.Parts
            .SelectMany(part => part.Shape.GetAllMeshes())
            .Select(mesh => mesh.UserFriendlyName)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var i = 0;
        for (var partIndex = 0; partIndex < physAggregateData.Parts.Length; partIndex++)
        {
            var physicsPart = physAggregateData.Parts[partIndex];
            var parentBone = physAggregateData.GetParentBoneName(partIndex);
            var bindPose = partIndex < bindPoses.Length ? bindPoses[partIndex] : Matrix4x4.Identity;

            foreach (var hull in physicsPart.Shape.GetAllHulls())
            {
                PhysHullsToExtract.Add((hull, GetDmxFileName_ForEmbeddedMesh("hull", i++), parentBone, bindPose));
                StoreSurfaceTagCombo(hull);
            }

            numberedSurfaces.UnionWith(physicsPart.Shape.GetAllSpheres().Select(sphere => sphere.SurfacePropertyIndex));
            numberedSurfaces.UnionWith(physicsPart.Shape.GetAllCapsules().Select(capsule => capsule.SurfacePropertyIndex));
            numberedSurfaces.UnionWith(physicsPart.Shape.GetAllHulls().Select(hull => hull.SurfacePropertyIndex));

            foreach (var mesh in GetMeshNodes(physicsPart.Shape.GetAllMeshes(), numberedSurfaces, meshNames))
            {
                PhysMeshesToExtract.Add((mesh, GetDmxFileName_ForEmbeddedMesh("phys", i++), parentBone, bindPose));

                StoreSurfaceTagCombo(mesh);

                foreach (var surfaceIndex in mesh.Shape.Materials)
                {
                    StoreSurfaceTagCombo(mesh.CollisionAttributeIndex, surfaceIndex);
                }
            }
        }
    }

    /// <summary>
    /// The compiler reorders the first eight <c>PhysicsMeshFile</c> nodes of a body by their count and keeps any
    /// further nodes in place. For each count, the node each compiled position takes.
    /// </summary>
    private static readonly int[][] CompiledMeshNodeOrder =
    [
        [],
        [0],
        [1, 0],
        [0, 2, 1],
        [2, 1, 3, 0],
        [1, 3, 0, 4, 2],
        [3, 0, 4, 2, 5, 1],
        [0, 4, 2, 5, 1, 6, 3],
        [4, 2, 5, 1, 6, 3, 7, 0],
    ];

    private static bool HasSeveralSurfaces(RnShapes.Mesh mesh)
        => mesh.Materials.Length > 0 && mesh.Materials.AsSpan().ContainsAnyExcept(mesh.Materials[0]);

    private static int CountMeshNodes(MeshDescriptor mesh)
        => HasSeveralSurfaces(mesh.Shape) ? mesh.Shape.Materials.Distinct().Count() : 1;

    /// <summary>
    /// Gets the <c>PhysicsMeshFile</c> nodes that compile back into a body's meshes, in the order to write them.
    /// </summary>
    /// <remarks>
    /// The compiler merges the mesh nodes of a body that share collision attributes into one mesh, giving each
    /// node's triangles that node's surface property. A mesh with several surfaces is therefore split into one
    /// node per surface. Merged meshes come out in the order of their first node, a mesh's own surface property is
    /// that of its first node, and new surface properties are numbered in node order, so the nodes are arranged to
    /// reproduce the compiled numbering and then permuted by the inverse of the compiler's reordering.
    /// </remarks>
    /// <param name="meshes">The body's compiled meshes.</param>
    /// <param name="numberedSurfaces">The surface properties numbered before these meshes; receives theirs.</param>
    /// <param name="meshNames">The mesh names in use; receives the names given to split nodes.</param>
    private List<MeshDescriptor> GetMeshNodes(IEnumerable<MeshDescriptor> meshes, HashSet<int> numberedSurfaces, HashSet<string> meshNames)
    {
        var groups = meshes.Select(mesh => SplitBySurface(mesh, meshNames)).ToList();
        var nodeCount = groups.Sum(group => group.Count);
        var compiled = new List<MeshDescriptor>(nodeCount);
        var emitted = new int[groups.Count];
        var startedGroups = 0;

        while (compiled.Count < nodeCount)
        {
            var bestGroup = -1;
            var bestKey = int.MaxValue;

            for (var g = 0; g < Math.Min(startedGroups + 1, groups.Count); g++)
            {
                if (emitted[g] == groups[g].Count)
                {
                    continue;
                }

                var surface = groups[g][emitted[g]].SurfacePropertyIndex;
                var key = numberedSurfaces.Contains(surface) ? -1 : surface;

                if (key < bestKey)
                {
                    bestGroup = g;
                    bestKey = key;
                }
            }

            var node = groups[bestGroup][emitted[bestGroup]++];
            numberedSurfaces.Add(node.SurfacePropertyIndex);
            compiled.Add(node);

            if (bestGroup == startedGroups)
            {
                startedGroups++;
            }
        }

        var order = CompiledMeshNodeOrder[Math.Min(compiled.Count, CompiledMeshNodeOrder.Length - 1)];
        var nodes = new MeshDescriptor[compiled.Count];

        for (var position = 0; position < compiled.Count; position++)
        {
            nodes[position < order.Length ? order[position] : position] = compiled[position];
        }

        return [.. nodes];
    }

    /// <summary>
    /// Splits a mesh whose triangles use several surface properties into one descriptor per surface, the mesh's
    /// own surface property first and the rest in index order. The first keeps the mesh's name and each other one is
    /// named after its surface property.
    /// </summary>
    private List<MeshDescriptor> SplitBySurface(MeshDescriptor mesh, HashSet<string> meshNames)
    {
        if (!HasSeveralSurfaces(mesh.Shape))
        {
            return [mesh];
        }

        return [.. mesh.Shape.Materials.Distinct()
            .OrderBy(surface => surface != mesh.SurfacePropertyIndex)
            .ThenBy(surface => surface)
            .Select((surface, index) => new MeshDescriptor
            {
                CollisionAttributeIndex = mesh.CollisionAttributeIndex,
                SurfacePropertyIndex = surface,
                UserFriendlyName = index == 0 || string.IsNullOrEmpty(mesh.UserFriendlyName)
                    ? mesh.UserFriendlyName
                    : ClaimUniqueName($"{mesh.UserFriendlyName}_{PhysicsSurfaceNames[surface]}", meshNames),
                HitGroupName = mesh.HitGroupName,
                Shape = mesh.Shape,
            })];
    }

    private static string ClaimUniqueName(string name, HashSet<string> usedNames)
    {
        var unique = name;

        for (var suffix = 2; !usedNames.Add(unique); suffix++)
        {
            unique = $"{name}_{suffix}";
        }

        return unique;
    }

    private void StoreSurfaceTagCombo<T>(ShapeDescriptor<T> shapeDesc) where T : struct
        => StoreSurfaceTagCombo(shapeDesc.CollisionAttributeIndex, shapeDesc.SurfacePropertyIndex);

    private void StoreSurfaceTagCombo(int collisionAttributeIndex, int surfacePropertyIndex)
    {
        if (PhysicsCollisionTags.Length <= collisionAttributeIndex
        || PhysicsSurfaceNames.Length <= surfacePropertyIndex)
        {
            return;
        }

        SurfaceTagCombos.Add(new SurfaceTagCombo(
            PhysicsSurfaceNames[surfacePropertyIndex],
            PhysicsCollisionTags[collisionAttributeIndex]
        ));
    }

    /// <summary>
    /// Converts a physics hull descriptor to DMX format.
    /// </summary>
    /// <param name="hull">The hull descriptor to convert.</param>
    /// <param name="bindPose">
    /// Transforms the hull's vertices from body-local space into bind-pose component space, the space a
    /// <c>PhysicsHullFile</c> node's DMX geometry is authored in. Defaults to identity.
    /// </param>
    public byte[] ToDmxMesh(HullDescriptor hull, Matrix4x4? bindPose = null)
    {
        var uniformSurface = PhysicsSurfaceNames[hull.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[hull.CollisionAttributeIndex];
        // https://github.com/ValveResourceFormat/ValveResourceFormat/issues/660#issuecomment-1795499191
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        return ToDmxMesh(hull.Shape, hull.UserFriendlyName ?? "hull", uniformSurface, uniformCollisionTags, fixRenderMeshCompileCrash, bindPose ?? Matrix4x4.Identity);
    }

    /// <summary>
    /// Converts a physics mesh descriptor to DMX format.
    /// </summary>
    /// <param name="mesh">The mesh descriptor to convert.</param>
    /// <param name="bindPose">
    /// Transforms the mesh's vertices from body-local space into bind-pose component space, the space a
    /// <c>PhysicsMeshFile</c> node's DMX geometry is authored in. Defaults to identity.
    /// </param>
    public byte[] ToDmxMesh(MeshDescriptor mesh, Matrix4x4? bindPose = null)
    {
        var uniformSurface = PhysicsSurfaceNames[mesh.SurfacePropertyIndex];
        var uniformCollisionTags = PhysicsCollisionTags[mesh.CollisionAttributeIndex];
        var fixRenderMeshCompileCrash = Type == ModelExtractType.Map_PhysicsToRenderMesh;
        int? onlySurface = HasSeveralSurfaces(mesh.Shape) ? mesh.SurfacePropertyIndex : null;
        return ToDmxMesh(mesh.Shape, onlySurface, mesh.UserFriendlyName ?? "mesh", uniformSurface, uniformCollisionTags, PhysicsSurfaceNames, fixRenderMeshCompileCrash, bindPose ?? Matrix4x4.Identity);
    }

    /// <summary>
    /// Converts a Rubikon hull shape to DMX mesh format.
    /// </summary>
    /// <remarks>
    /// <paramref name="bindPose"/> transforms the hull's vertices from body-local space into bind-pose
    /// component space, the space a <c>PhysicsHullFile</c> node's geometry is authored in. The raw
    /// body-local vertices are transformed by the part's <see cref="PhysAggregateData.BindPose"/>.
    /// </remarks>
    public static byte[] ToDmxMesh(RnShapes.Hull hull, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        bool appendVertexNormalStream = false,
        Matrix4x4? bindPose = null)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxScaffolding.BaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        // n-gon face set
        var faceSet = new DmeFaceSet() { Name = "hull faces" };
        faceSet.Material.MaterialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        var edges = hull.GetEdges();
        var faces = hull.GetFaces();
        var vertexPositions = hull.GetVertexPositions().ToArray();

        if (bindPose is Matrix4x4 pose)
        {
            for (var i = 0; i < vertexPositions.Length; i++)
            {
                vertexPositions[i] = Vector3.Transform(vertexPositions[i], pose);
            }
        }

        Debug.Assert(faces.Length + vertexPositions.Length == (edges.Length / 2) + 2);

        foreach (var face in faces)
        {
            foreach (var vertex in RnShapes.Hull.GetFaceVertices(edges, face))
            {
                faceSet.Faces.Add(vertex);
            }

            faceSet.Faces.Add(-1);
        }

        var indices = Enumerable.Range(0, vertexPositions.Length * 3).ToArray();
        vertexData.AddIndexedStream("position$0", vertexPositions, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), vertexPositions.Length).ToArray(), indices);
        }

        DmxScaffolding.TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    /// <summary>
    /// Converts a Rubikon mesh shape to DMX mesh format.
    /// </summary>
    /// <remarks>
    /// <paramref name="bindPose"/> transforms the mesh's vertices from body-local space into bind-pose
    /// component space, matching how
    /// <see cref="ToDmxMesh(RnShapes.Hull, string, string, HashSet{string}, bool, Matrix4x4?)"/> treats
    /// a hull's vertices.
    /// </remarks>
    public static byte[] ToDmxMesh(RnShapes.Mesh mesh, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList,
        bool appendVertexNormalStream = false,
        Matrix4x4? bindPose = null)
        => ToDmxMesh(mesh, null, name, uniformSurface, uniformCollisionTags, surfaceList, appendVertexNormalStream, bindPose);

    /// <summary>
    /// Converts a Rubikon mesh shape to DMX mesh format, keeping only the triangles of surface
    /// <paramref name="onlySurface"/> and the vertices they use when it is set.
    /// </summary>
    private static byte[] ToDmxMesh(RnShapes.Mesh mesh, int? onlySurface, string name,
        string uniformSurface,
        HashSet<string> uniformCollisionTags,
        string[] surfaceList,
        bool appendVertexNormalStream,
        Matrix4x4? bindPose)
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxScaffolding.BaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        var triangles = mesh.GetTriangles();
        var materials = mesh.Materials;
        var vertices = mesh.GetVertices().ToArray();
        int[] indices;

        if (onlySurface is int keptSurface)
        {
            Debug.Assert(materials.Length == triangles.Length);

            var kept = new List<int>(triangles.Length * 3);
            for (var t = 0; t < triangles.Length; t++)
            {
                if (materials[t] == keptSurface)
                {
                    kept.AddRange([triangles[t].X, triangles[t].Y, triangles[t].Z]);
                }
            }

            (vertices, indices) = CompactVertices(vertices, kept);
            materials = [];
        }
        else
        {
            indices = new int[triangles.Length * 3];
            for (var t = 0; t < triangles.Length; t++)
            {
                var triangle = triangles[t];
                indices[t * 3] = triangle.X;
                indices[t * 3 + 1] = triangle.Y;
                indices[t * 3 + 2] = triangle.Z;
            }
        }

        var triangleCount = indices.Length / 3;

        if (materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            DmxScaffolding.TriangleFaceSet(dag, 0, triangleCount, materialName);
        }
        else if (dag.Shape is DmeMesh dmeMesh)
        {
            Debug.Assert(materials.Length == triangleCount);
            Debug.Assert(surfaceList.Length > 0);

            Span<DmeFaceSet> faceSets = new DmeFaceSet[surfaceList.Length];
            for (var t = 0; t < materials.Length; t++)
            {
                var surfaceIndex = materials[t];
                var faceSet = faceSets[surfaceIndex];

                if (faceSet == null)
                {
                    var surface = surfaceList[surfaceIndex];
                    faceSet = faceSets[surfaceIndex] = new DmeFaceSet()
                    {
                        Name = surface + '$' + surfaceIndex
                    };
                    faceSet.Material.MaterialName = new SurfaceTagCombo(surface, uniformCollisionTags).StringMaterial;
                    dmeMesh.FaceSets.Add(faceSet);
                }

                faceSet.Faces.Add(t * 3);
                faceSet.Faces.Add(t * 3 + 1);
                faceSet.Faces.Add(t * 3 + 2);
                faceSet.Faces.Add(-1);
            }
        }

        if (bindPose is Matrix4x4 pose)
        {
            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = Vector3.Transform(vertices[i], pose);
            }
        }

        vertexData.AddIndexedStream("position$0", vertices, indices);

        if (appendVertexNormalStream)
        {
            vertexData.AddIndexedStream("normal$0", Enumerable.Repeat(new Vector3(0, 0, 0), vertices.Length).ToArray(), indices);
        }

        DmxScaffolding.TieElementRoot(dmx, dmeModel);
        using var stream = new MemoryStream();
        dmx.Save(stream, "binary", 9);

        return stream.ToArray();
    }

    /// <summary>
    /// Drops the vertices <paramref name="indices"/> does not reference, keeping the order of the rest.
    /// </summary>
    private static (Vector3[] Vertices, int[] Indices) CompactVertices(Vector3[] vertices, List<int> indices)
    {
        var used = new bool[vertices.Length];
        foreach (var index in indices)
        {
            used[index] = true;
        }

        var remap = new int[vertices.Length];
        var kept = new List<Vector3>();
        for (var i = 0; i < vertices.Length; i++)
        {
            if (used[i])
            {
                remap[i] = kept.Count;
                kept.Add(vertices[i]);
            }
        }

        return ([.. kept], [.. indices.Select(index => remap[index])]);
    }
}
