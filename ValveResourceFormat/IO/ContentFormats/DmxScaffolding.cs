using Datamodel;

namespace ValveResourceFormat.IO.ContentFormats.DmxModel;

/// <summary>
/// The DMX scaffolding every mesh export shares: the dag and vertex data a datamodel mesh hangs off,
/// the face sets its geometry is written into, and the root element tying them to a datamodel.
/// </summary>
internal static class DmxScaffolding
{
    /// <summary>
    /// Creates a model with one dag and one vertex data element hanging off it.
    /// </summary>
    public static void BaseLayout(string name, out DmeModel dmeModel, out DmeDag dag, out DmeVertexData vertexData)
    {
        dmeModel = new DmeModel() { Name = name };
        (dag, vertexData) = CreateDagVertexData(dmeModel, name);
    }

    /// <summary>
    /// Adds a dag drawing the given vertex data to a model, and registers it in the model's joint list.
    /// </summary>
    public static DmeDag CreateDag(DmeModel dmeModel, DmeVertexData vertexData, string name)
    {
        var shape = new DmeMesh
        {
            Name = name,
            CurrentState = vertexData
        };
        shape.BaseStates.Add(vertexData);

        var dag = new DmeDag() { Name = name, Shape = shape };
        dmeModel.Children.Add(dag);
        dmeModel.JointList.Add(dag);

        var transformList = new DmeTransformsList();
        transformList.Transforms.Add(new DmeTransform());
        dmeModel.BaseStates.Add(transformList);

        return dag;
    }

    /// <summary>
    /// Adds a dag together with the vertex data it draws. dmx requires one dag per vertex buffer.
    /// </summary>
    public static (DmeDag Dag, DmeVertexData VertexData) CreateDagVertexData(DmeModel dmeModel, string name)
    {
        var vertexData = new DmeVertexData { Name = "bind" };
        var dag = CreateDag(dmeModel, vertexData, name);

        return (dag, vertexData);
    }

    /// <summary>
    /// Writes a run of consecutive triangles as one face set of the dag's mesh.
    /// </summary>
    public static void TriangleFaceSet(DmeDag dag, int triangleStart, int triangleEnd, string material)
    {
        var faceSet = new DmeFaceSet() { Name = triangleStart + "-" + triangleEnd };
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        for (var i = triangleStart; i < triangleEnd; i++)
        {
            faceSet.Faces.Add(i * 3);
            faceSet.Faces.Add(i * 3 + 1);
            faceSet.Faces.Add(i * 3 + 2);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    /// <summary>
    /// Writes an index buffer range as one face set of the dag's mesh, offset by a base vertex.
    /// </summary>
    public static void TriangleFaceSetFromIndexBuffer(DmeDag dag, ReadOnlySpan<int> indices, int baseVertex,
        string material, string name)
    {
        var faceSet = new DmeFaceSet() { Name = name };
        if (dag.Shape is DmeMesh dmeMesh)
        {
            dmeMesh.FaceSets.Add(faceSet);
        }

        for (var i = 0; i < indices.Length; i += 3)
        {
            faceSet.Faces.Add(baseVertex + indices[i]);
            faceSet.Faces.Add(baseVertex + indices[i + 1]);
            faceSet.Faces.Add(baseVertex + indices[i + 2]);
            faceSet.Faces.Add(-1);
        }

        faceSet.Material.MaterialName = material;
    }

    /// <summary>
    /// Makes the given model both the skeleton and the model of a datamodel's root element.
    /// </summary>
    public static void TieElementRoot(Datamodel.Datamodel dmx, DmeModel dmeModel)
    {
        dmx.Root = new Element(dmx, "root", null, "DmElement")
        {
            ["skeleton"] = dmeModel,
            ["model"] = dmeModel,
            ["exportTags"] = new Element(dmx, "exportTags", null, "DmeExportTags")
            {
                ["source"] = $"Generated with {StringToken.VRF_GENERATOR}",
            }
        };
    }
}
