using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes for a morph set that has flex controllers and rules but no morph targets.
/// </summary>
partial class ModelExtract
{
    /// <summary>
    /// Gets the morph set whose controllers and rules the model writes as nodes: the last one, when the extract has a
    /// model and none of its morph sets has morph targets. The compiler puts such nodes on the last mesh when no mesh
    /// carries morph targets, and the meshes then leave their empty morph sets out.
    /// </summary>
    private Morph? GetMorphSetForControlNodes()
    {
        if (model == null)
        {
            return null;
        }

        Morph? emptyMorph = null;

        foreach (var renderMesh in RenderMeshesToExtract)
        {
            if (fileLoader != null)
            {
                renderMesh.Mesh.LoadExternalMorphData(fileLoader);
            }

            if (renderMesh.Mesh.MorphData is not { } morph)
            {
                continue;
            }

            if (morph.HasMorphTargets)
            {
                return null;
            }

            emptyMorph = morph;
        }

        return emptyMorph;
    }

    /// <summary>
    /// Writes the controllers and rules of the morph set <see cref="GetMorphSetForControlNodes"/> picks as
    /// <c>MorphControl</c> and <c>MorphRule</c> nodes.
    /// </summary>
    private void AddMorphControlNodes(ModelDocLists lists)
    {
        if (GetMorphSetForControlNodes() is not { } emptyMorph)
        {
            return;
        }

        var recovery = new FlexRecovery(emptyMorph);

        foreach (var control in recovery.Controls)
        {
            lists.MorphControls.Add(MakeNode("MorphControl",
                ("name", control.Name),
                ("stereo", false),
                ("min_value", control.Min),
                ("max_value", control.Max),
                ("implicit", false)
            ));
        }

        var flexNames = emptyMorph.GetFlexDescriptors();
        var written = new HashSet<string>();

        foreach (var rule in emptyMorph.Data.GetArray("m_FlexRules") ?? [])
        {
            var flexId = rule.GetInt32Property("m_nFlex");

            if (flexId < 0 || flexId >= flexNames.Count
                || !recovery.Expressions.TryGetValue(flexNames[flexId], out var expression)
                || !written.Add(flexNames[flexId]))
            {
                continue;
            }

            lists.MorphRules.Add(MakeNode("MorphRule",
                ("target", flexNames[flexId]),
                ("expression", expression),
                ("implicit", false)
            ));
        }
    }
}
