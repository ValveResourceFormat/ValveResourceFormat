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
    /// model and none of its morph sets has morph targets.
    /// </summary>
    private Morph? MorphSetForControlNodes => morphSetForControlNodes?.Value;

    private Lazy<Morph?>? morphSetForControlNodes;

    private Morph? FindMorphSetForControlNodes()
    {
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
    /// Writes the controllers and rules of the morph set <see cref="MorphSetForControlNodes"/> picks as
    /// <c>MorphControl</c> and <c>MorphRule</c> nodes.
    /// </summary>
    private void AddMorphControlNodes(ModelDocLists lists)
    {
        if (MorphSetForControlNodes is not { } emptyMorph)
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

        foreach (var (target, expression) in recovery.Expressions)
        {
            lists.MorphRules.Add(MakeNode("MorphRule",
                ("target", target),
                ("expression", expression),
                ("implicit", false)
            ));
        }
    }
}
