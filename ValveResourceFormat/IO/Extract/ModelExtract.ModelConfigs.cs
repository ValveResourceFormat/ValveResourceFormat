using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the spawn and dynamic model config nodes behind a model's <c>m_pModelConfigList</c>.
/// </summary>
/// <remarks>
/// The spawn config compiles to the config named <c>@spawn</c> and every dynamic config to another top level config.
/// Each choice of a pick compiles to a config of its own, which is written back as a group the first time a pick names
/// it and as a reference after that.
/// </remarks>
partial class ModelExtract
{
    private const string CompiledElementPrefix = "CModelConfigElement_";

    private static void AddModelConfigNodes(Model model, KVObject rootChildren)
    {
        var configList = model.Configs;

        if (configList.Configs.Count == 0)
        {
            return;
        }

        var writer = new ModelConfigWriter(configList);

        var spawnConfig = MakeNode("SpawnConfig",
            ("hide_materialgroup_in_tools", configList.HideMaterialGroupInTools),
            ("hide_rendercolor_in_tools", configList.HideRenderColorInTools)
        );

        if (configList.Find(ModelConfigList.SpawnConfigName) is { } spawn)
        {
            spawnConfig.Add("children", writer.ConvertElements(spawn.Elements));
        }

        rootChildren.Add(spawnConfig);

        var dynamicConfigs = KVObject.Array();

        foreach (var config in configList.Configs)
        {
            if (!config.IsTopLevel || config.Name == ModelConfigList.SpawnConfigName)
            {
                continue;
            }

            dynamicConfigs.Add(MakeNode("DynamicConfig",
                ("name", config.Name),
                ("active_in_editor_by_default", config.IsActiveInEditorByDefault),
                ("children", writer.ConvertElements(config.Elements))
            ));
        }

        if (dynamicConfigs.Count > 0)
        {
            rootChildren.Add(MakeNode("DynamicConfigList", ("children", dynamicConfigs)));
        }
    }

    /// <summary>
    /// Converts compiled config elements to nodes. Node names are unique in a model doc, so a config or element that was
    /// already written is referenced by name the next time it appears.
    /// </summary>
    private sealed class ModelConfigWriter(ModelConfigList configList)
    {
        private readonly HashSet<string> writtenNames = [];

        public KVObject ConvertElements(IEnumerable<ModelConfigElement> elements)
            => MakeArray(elements.Select(ConvertElement));

        private KVObject ConvertElement(ModelConfigElement element)
        {
            if (element.Name.Length > 0 && !writtenNames.Add(element.Name))
            {
                return MakeNode("ConfigReference", ("config_reference", element.Name));
            }

            var className = element.ClassName.StartsWith(CompiledElementPrefix, StringComparison.Ordinal)
                ? element.ClassName[CompiledElementPrefix.Length..]
                : element.ClassName;

            var node = MakeNode($"Config{className}", ("name", element.Name));

            switch (element)
            {
                case ModelConfigAttachedModel attachedModel:
                    AddAttachedModelKeys(attachedModel, node);
                    break;
                case ModelConfigUserPick userPick:
                    node.Add("children", ConvertPickChoices(userPick.Choices, []));
                    return node;
                case ModelConfigRandomPick randomPick:
                    node.Add("children", ConvertPickChoices(randomPick.Choices, randomPick.ChoiceWeights));
                    return node;
                case ModelConfigSetMaterialGroup setMaterialGroup:
                    node.Add("material_group", setMaterialGroup.MaterialGroup);
                    break;
                case ModelConfigSetMaterialGroupOnAttachedModels setMaterialGroup:
                    node.Add("material_group", setMaterialGroup.MaterialGroup);
                    break;
                case ModelConfigSetRenderColor setRenderColor:
                    var color = setRenderColor.Color;
                    node.Add("color", MakeArray((long)color.R, (long)color.G, (long)color.B, (long)color.A));
                    break;
                case ModelConfigSetBodyGroup setBodyGroup:
                    node.Add("group_name", setBodyGroup.GroupName);
                    node.Add("choice", setBodyGroup.Choice);
                    break;
                case ModelConfigSetBodyGroupOnAttachedModels setBodyGroup:
                    node.Add("group_name", setBodyGroup.GroupName);
                    node.Add("choice", setBodyGroup.Choice);
                    break;
            }

            if (element.NestedElements.Count > 0)
            {
                node.Add("children", ConvertElements(element.NestedElements));
            }

            return node;
        }

        /// <summary>
        /// Writes a pick's choices. A choice config holding a single element of its own name is that element, any other
        /// is a group of its elements.
        /// </summary>
        private KVObject ConvertPickChoices(IReadOnlyList<string> choices, IReadOnlyList<float> weights)
        {
            var children = KVObject.Array();

            for (var i = 0; i < choices.Count; i++)
            {
                KVObject choice;

                if (writtenNames.Contains(choices[i]) || configList.Find(choices[i]) is not { IsTopLevel: false } config)
                {
                    choice = MakeNode("ConfigReference", ("config_reference", choices[i]));
                }
                else if (config.Elements is [var element] && element.Name == choices[i])
                {
                    choice = ConvertElement(element);
                }
                else
                {
                    writtenNames.Add(choices[i]);
                    choice = MakeNode("ConfigGroup", ("name", choices[i]), ("children", ConvertElements(config.Elements)));
                }

                if (i < weights.Count)
                {
                    choice.Add("choice_weight", weights[i]);
                }

                children.Add(choice);
            }

            return children;
        }
    }

    /// <summary>
    /// Writes the attachment keys of an attached model. The compiler keeps the offsets only for root relative
    /// attachments, or for bone and attachment ones that turn them on.
    /// </summary>
    private static void AddAttachedModelKeys(ModelConfigAttachedModel element, KVObject node)
    {
        var type = element.AttachmentType switch
        {
            ModelConfigAttachmentType.BoneOrAttachment => "attach",
            ModelConfigAttachmentType.Bonemerge => element.BoneMergeFlex ? "bonemergeflex" : "bonemerge",
            _ => "root",
        };

        node.Add("model_name", element.ModelName);
        node.Add("entity_class", element.EntityClass);
        node.Add("attachment_type", type);
        node.Add("attach_point", element.AttachmentName);

        if (type is "root" or "attach")
        {
            node.Add("use_local_attach_point_offset", element.LocalAttachmentOffsetName.Length > 0);
            node.Add("local_attach_point_offset", element.LocalAttachmentOffsetName);
            node.Add("use_additional_offset", element.Offset != Vector3.Zero || element.AngleOffset != Vector3.Zero);
            node.Add("relative_origin", ToKVArray(element.Offset));
            node.Add("relative_angles", ToKVArray(element.AngleOffset));
        }

        node.Add("user_specified_color", element.UserSpecifiedColor);
        node.Add("user_specified_materialgroup", element.UserSpecifiedMaterialGroup);
        node.Add("set_bodygroup_on_other_models", element.BodyGroupOnOtherModels);
        node.Add("set_materialgroup_on_other_models", element.MaterialGroupOnOtherModels);
        node.Add("collide_with_hierarchy", element.CollideWithHierarchy);
        node.Add("collide_outside_hierarchy", element.CollideOutsideHierarchy);
    }
}
