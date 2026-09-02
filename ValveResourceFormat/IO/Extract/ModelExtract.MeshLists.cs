using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc nodes that say what the model is made of: its render meshes, the body groups
/// and LOD groups that select between them, its attachment points and its material groups.
/// </summary>
partial class ModelExtract
{
    private void AddRenderMeshNodes(ModelDocLists lists)
    {
        if (RenderMeshesToExtract.Count != 0)
        {
            foreach (var renderMesh in RenderMeshesToExtract)
            {
                var renderMeshFile = MakeNode(
                    "RenderMeshFile",
                    ("name", renderMesh.Name),
                    ("filename", renderMesh.FileName)
                );

                if (renderMesh.ImportFilter != default)
                {
                    var importFilter = KVObject.Collection();
                    {
                        importFilter.Add("exclude_by_default", renderMesh.ImportFilter.ExcludeByDefault);
                        importFilter.Add("exception_list", MakeArray([.. renderMesh.ImportFilter.Filter.Select(s => (KVObject)s)]));
                    }

                    renderMeshFile.Add("import_filter", importFilter);
                }

                lists.RenderMeshes.Add(renderMeshFile);
            }

            if (model != null)
            {
                var meshGroups = model.MeshGroups;

                foreach (var bodyGroupInfo in meshGroups.BodyGroups)
                {
                    var choiceList = KVObject.Array();
                    var bodyGroup = MakeNode("BodyGroup",
                        ("name", bodyGroupInfo.Name),
                        ("children", choiceList)
                    );

                    if (meshGroups.IsHiddenInTools(bodyGroupInfo.Name))
                    {
                        bodyGroup.Add("hidden_in_tools", true);
                    }

                    // A single choice compiled without its index is what this flag asks for.
                    if (bodyGroupInfo.Choices is [{ Indexed: false }])
                    {
                        bodyGroup.Add("non_bodygroup_single_choice", true);
                    }

                    var choiceIndex = 0;

                    foreach (var choice in bodyGroupInfo.Choices)
                    {
                        var meshGroupChoice = MakeNode("BodyGroupChoice");

                        // Every choice needs a name to recompile, even one that only repeats its index.
                        meshGroupChoice.Add("name", string.IsNullOrEmpty(choice.Name)
                            ? choiceIndex.ToString(CultureInfo.InvariantCulture)
                            : choice.Name);

                        if (meshGroups.IsHiddenInTools(choice.FullName))
                        {
                            meshGroupChoice.Add("hide_in_tools", true);
                        }

                        var meshes = KVObject.Array();
                        meshGroupChoice.Add("meshes", meshes);

                        foreach (var renderMesh in RenderMeshesToExtract)
                        {
                            // No mask will show up as 'Empty' in editor
                            if (meshGroups.IsMeshInGroup(renderMesh.Index, choice.GroupIndex))
                            {
                                meshes.Add(renderMesh.Name);
                            }
                        }

                        choiceList.Add(meshGroupChoice);
                        choiceIndex++;
                    }

                    lists.BodyGroups.Add(bodyGroup);
                }
            }

            if (model != null)
            {
                // LOD groups. m_refLODGroupMasks says which level each mesh belongs to (bit N => level N) and
                // m_lodGroupSwitchDistances gives each level's switch value. Emit one LODGroup per declared
                // level so a recompile rebuilds the original switch distances, and collect meshes that live in
                // every level into a single LODGroupAll rather than repeating them in each group. A level
                // whose meshes all moved to LODGroupAll is still written, as an empty group.
                var lodInfo = model.LodInfo;

                for (var lodLevel = 0; lodLevel < lodInfo.SwitchDistances.Count; lodLevel++)
                {
                    var meshReferences = KVObject.Array();

                    foreach (var renderMesh in RenderMeshesToExtract)
                    {
                        if (!lodInfo.IsMeshInLevel(renderMesh.Index, lodLevel) || lodInfo.IsMeshInAllLevels(renderMesh.Index))
                        {
                            continue;
                        }

                        var meshReference = KVObject.Collection();
                        meshReference.Add("mesh_name", renderMesh.Name);
                        meshReferences.Add(meshReference);
                    }

                    lists.LodGroups.Add(MakeNode("LODGroup",
                        ("switch_threshold", lodInfo.SwitchDistances[lodLevel]),
                        ("mesh_references", meshReferences)
                    ));
                }

                if (lodInfo.SwitchDistances.Count > 0)
                {
                    var allLevelReferences = KVObject.Array();

                    foreach (var renderMesh in RenderMeshesToExtract)
                    {
                        if (!lodInfo.IsMeshInAllLevels(renderMesh.Index))
                        {
                            continue;
                        }

                        var meshReference = KVObject.Collection();
                        meshReference.Add("mesh_name", renderMesh.Name);
                        allLevelReferences.Add(meshReference);
                    }

                    if (allLevelReferences.Count > 0)
                    {
                        lists.LodGroups.Add(MakeNode("LODGroupAll",
                            ("mesh_references", allLevelReferences)
                        ));
                    }
                }
            }

            var mesh = RenderMeshesToExtract.First();
            var attachments = mesh.Mesh.Attachments;

            foreach (var attachment in attachments.Values)
            {
                var mainInfluence = attachment[^1];

                var node = MakeNode("Attachment",
                    ("name", attachment.Name),
                    ("ignore_rotation", attachment.IgnoreRotation),
                    ("parent_bone", mainInfluence.Name),
                    ("relative_origin", ToKVArray(mainInfluence.Offset)),
                    ("relative_angles", ToKVArray(EntityTransformHelper.ToEulerAngles(mainInfluence.Rotation))),
                    ("weight", mainInfluence.Weight)
                );

                if (attachment.Length > 1)
                {
                    var children = KVObject.Array();
                    for (var i = 0; i < attachment.Length - 1; i++)
                    {
                        var influence = attachment[i];
                        var childNode = MakeNode("AttachmentInfluence",
                            ("parent_bone", influence.Name),
                            ("relative_origin", ToKVArray(influence.Offset)),
                            ("relative_angles", ToKVArray(EntityTransformHelper.ToEulerAngles(influence.Rotation))),
                            ("weight", influence.Weight)
                        );

                        children.Add(childNode);
                    }
                    node.Add("children", children);
                }

                lists.Attachments.Add(node);
            }
        }
    }

    private void AddMaterialGroupNodes(ModelDocLists lists)
    {
        if (model?.GetMaterialGroups().ToList() is { Count: > 0 } materialGroups)
        {
            var defaultMaterials = materialGroups[0].Materials;

            lists.MaterialGroups.Add(MakeNode("DefaultMaterialGroup",
                ("name", materialGroups[0].Name ?? "default"),
                ("remaps", KVObject.Array())
            ));

            for (var groupIndex = 1; groupIndex < materialGroups.Count; groupIndex++)
            {
                var variantMaterials = materialGroups[groupIndex].Materials;
                if (variantMaterials.Length == 0)
                {
                    continue;
                }

                var remaps = KVObject.Array();
                var pairCount = Math.Min(defaultMaterials.Length, variantMaterials.Length);
                for (var i = 0; i < pairCount; i++)
                {
                    var fromMaterial = defaultMaterials[i];
                    var toMaterial = variantMaterials[i];

                    // A null slot carries no remap for that material.
                    if (fromMaterial == null || toMaterial == null
                        || string.Equals(fromMaterial, toMaterial, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    remaps.Add(MakeNode("BaseMaterialRemap",
                        ("from", fromMaterial),
                        ("to", toMaterial)
                    ));
                }

                lists.MaterialGroups.Add(MakeNode("MaterialGroup",
                    ("name", materialGroups[groupIndex].Name ?? groupIndex.ToString(CultureInfo.InvariantCulture)),
                    ("remaps", remaps)
                ));
            }
        }
    }
}
