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

        var bindPoses = physAggregateData.BindPose;

        var i = 0;
        for (var partIndex = 0; partIndex < physAggregateData.Parts.Length; partIndex++)
        {
            var physicsPart = physAggregateData.Parts[partIndex];
            var parentBone = physAggregateData.GetParentBoneName(partIndex);
            var bindPose = partIndex < bindPoses.Length ? bindPoses[partIndex] : Matrix4x4.Identity;

            foreach (var hull in physicsPart.Shape.Hulls)
            {
                PhysHullsToExtract.Add((hull, GetDmxFileName_ForEmbeddedMesh("hull", i++), parentBone, bindPose));
                StoreSurfaceTagCombo(hull);
            }

            foreach (var mesh in physicsPart.Shape.Meshes)
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
        return ToDmxMesh(mesh.Shape, mesh.UserFriendlyName ?? "mesh", uniformSurface, uniformCollisionTags, PhysicsSurfaceNames, fixRenderMeshCompileCrash, bindPose ?? Matrix4x4.Identity);
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
    {
        using var dmx = new Datamodel.Datamodel("model", 22);
        DmxScaffolding.BaseLayout(name, out var dmeModel, out var dag, out var vertexData);

        var triangles = mesh.GetTriangles();

        if (mesh.Materials.Length == 0)
        {
            var materialName = new SurfaceTagCombo(uniformSurface, uniformCollisionTags).StringMaterial;
            DmxScaffolding.TriangleFaceSet(dag, 0, triangles.Length, materialName);
        }
        else if (dag.Shape is DmeMesh dmeMesh)
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
                    dmeMesh.FaceSets.Add(faceSet);
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
}
