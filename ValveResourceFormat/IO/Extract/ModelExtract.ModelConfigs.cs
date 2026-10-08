using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
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
    private const string SpawnConfigName = "@spawn";
    private const string CompiledElementPrefix = "CModelConfigElement_";

    private static void AddModelConfigNodes(Model model, KVObject rootChildren)
    {
        if (model.Data.GetSubCollection("m_pModelConfigList") is not { } configList
            || configList.GetArray("m_Configs") is not { Count: > 0 } configs)
        {
            return;
        }

        var writer = new ModelConfigWriter(configs);

        var spawnConfig = MakeNode("SpawnConfig",
            ("hide_materialgroup_in_tools", configList.GetBooleanProperty("m_bHideMaterialGroupInTools")),
            ("hide_rendercolor_in_tools", configList.GetBooleanProperty("m_bHideRenderColorInTools"))
        );

        if (writer.TryGetConfig(SpawnConfigName, out var spawn))
        {
            spawnConfig.Add("children", writer.ConvertElements(spawn));
        }

        rootChildren.Add(spawnConfig);

        var dynamicConfigs = KVObject.Array();

        foreach (var config in configs)
        {
            var name = config.GetStringProperty("m_ConfigName", string.Empty);

            if (!config.GetBooleanProperty("m_bTopLevel") || name == SpawnConfigName)
            {
                continue;
            }

            dynamicConfigs.Add(MakeNode("DynamicConfig",
                ("name", name),
                ("active_in_editor_by_default", config.GetBooleanProperty("m_bActiveInEditorByDefault")),
                ("children", writer.ConvertElements(config))
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
    private sealed class ModelConfigWriter
    {
        private readonly Dictionary<string, KVObject> configsByName = [];
        private readonly HashSet<string> writtenNames = [];

        public ModelConfigWriter(IEnumerable<KVObject> configs)
        {
            foreach (var config in configs)
            {
                configsByName.TryAdd(config.GetStringProperty("m_ConfigName", string.Empty), config);
            }
        }

        public bool TryGetConfig(string name, [MaybeNullWhen(false)] out KVObject config)
            => configsByName.TryGetValue(name, out config);

        public KVObject ConvertElements(KVObject config)
            => MakeArray((config.GetArray("m_Elements") ?? []).Select(ConvertElement));

        private KVObject ConvertElement(KVObject element)
        {
            var name = element.GetStringProperty("m_ElementName", string.Empty);

            if (name.Length > 0 && !writtenNames.Add(name))
            {
                return MakeNode("ConfigReference", ("config_reference", name));
            }

            var compiledClass = element.GetStringProperty("_class", string.Empty);
            var className = compiledClass.StartsWith(CompiledElementPrefix, StringComparison.Ordinal)
                ? compiledClass[CompiledElementPrefix.Length..]
                : compiledClass;

            var node = MakeNode($"Config{className}", ("name", name));

            switch (className)
            {
                case "AttachedModel":
                    AddAttachedModelKeys(element, node);
                    break;
                case "UserPick" or "RandomPick":
                    node.Add("children", ConvertPickChoices(element));
                    return node;
                case "SetMaterialGroup" or "SetMaterialGroupOnAttachedModels":
                    node.Add("material_group", element.GetStringProperty("m_MaterialGroupName", string.Empty));
                    break;
                case "SetRenderColor":
                    var color = element.GetIntegerArray("m_Color") ?? [];
                    node.Add("color", MakeArray(
                        color.ElementAtOrDefault(0), color.ElementAtOrDefault(1), color.ElementAtOrDefault(2),
                        color.Length > 3 ? color[3] : 255));
                    break;
                case "RandomColor":
                    if (element.ContainsKey("m_Gradient"))
                    {
                        node.Add("gradient", element["m_Gradient"]);
                    }

                    break;
                case "SetBodygroup" or "SetBodygroupOnAttachedModels":
                    node.Add("group_name", element.GetStringProperty("m_GroupName", string.Empty));
                    node.Add("choice", element.GetInt32Property("m_nChoice"));
                    break;
                case "Command":
                    node.Add("command", element.GetStringProperty("m_Command", string.Empty));

                    if (element.GetSubCollection("m_Args") is { Count: > 0 } args)
                    {
                        node.Add("args", ToHeaderlessKV3(args));
                    }

                    break;
            }

            if (element.GetArray("m_NestedElements") is { Count: > 0 } nested)
            {
                node.Add("children", MakeArray(nested.Select(ConvertElement)));
            }

            return node;
        }

        /// <summary>
        /// Writes a pick's choices. A choice config holding a single element of its own name is that element, any other
        /// is a group of its elements.
        /// </summary>
        private KVObject ConvertPickChoices(KVObject pick)
        {
            var choices = pick.GetArray<string>("m_Choices") ?? [];
            var weights = pick.GetFloatArray("m_ChoiceWeights");
            var children = KVObject.Array();

            for (var i = 0; i < choices.Length; i++)
            {
                var weight = i < weights.Length ? weights[i] : 1f;
                KVObject choice;

                if (writtenNames.Contains(choices[i]) || !configsByName.TryGetValue(choices[i], out var config)
                    || config.GetBooleanProperty("m_bTopLevel"))
                {
                    choice = MakeNode("ConfigReference", ("config_reference", choices[i]));
                }
                else if (config.GetArray("m_Elements") is [var element] && element.GetStringProperty("m_ElementName") == choices[i])
                {
                    choice = ConvertElement(element);
                }
                else
                {
                    writtenNames.Add(choices[i]);
                    choice = MakeNode("ConfigGroup", ("name", choices[i]), ("children", ConvertElements(config)));
                }

                choice.Add("choice_weight", weight);
                children.Add(choice);
            }

            return children;
        }
    }

    /// <summary>
    /// Writes the attachment keys of an attached model. The compiler keeps the offsets only for root relative
    /// attachments, or for bone and attachment ones that turn them on.
    /// </summary>
    private static void AddAttachedModelKeys(KVObject element, KVObject node)
    {
        var attachmentType = element.ContainsKey("m_AttachmentType") ? element["m_AttachmentType"].ToString() : "0";
        var type = attachmentType switch
        {
            "0" or "MODEL_CONFIG_ATTACHMENT_BONE_OR_ATTACHMENT" => "attach",
            "2" or "MODEL_CONFIG_ATTACHMENT_BONEMERGE" => element.GetBooleanProperty("m_bBoneMergeFlex") ? "bonemergeflex" : "bonemerge",
            _ => "root",
        };

        var offset = GetVector3OrZero(element, "m_vOffset");
        var angles = GetVector3OrZero(element, "m_aAngOffset");
        var localAttachment = element.GetStringProperty("m_LocalAttachmentOffsetName", string.Empty);

        node.Add("model_name", element.GetStringProperty("m_hModel", string.Empty));
        node.Add("entity_class", element.GetStringProperty("m_EntityClass", string.Empty));
        node.Add("attachment_type", type);
        node.Add("attach_point", element.GetStringProperty("m_AttachmentName", string.Empty));

        if (type is "root" or "attach")
        {
            node.Add("use_local_attach_point_offset", localAttachment.Length > 0);
            node.Add("local_attach_point_offset", localAttachment);
            node.Add("use_additional_offset", offset != Vector3.Zero || angles != Vector3.Zero);
            node.Add("relative_origin", ToKVArray(offset));
            node.Add("relative_angles", ToKVArray(angles));
        }

        node.Add("user_specified_color", element.GetBooleanProperty("m_bUserSpecifiedColor"));
        node.Add("user_specified_materialgroup", element.GetBooleanProperty("m_bUserSpecifiedMaterialGroup"));
        node.Add("set_bodygroup_on_other_models", element.GetStringProperty("m_BodygroupOnOtherModels", string.Empty));
        node.Add("set_materialgroup_on_other_models", element.GetStringProperty("m_MaterialGroupOnOtherModels", string.Empty));
        node.Add("collide_with_hierarchy", element.GetBooleanProperty("m_bCollideWithHierarchy"));
        node.Add("collide_outside_hierarchy", element.GetBooleanProperty("m_bCollideOutsideHierarchy", true));

        static Vector3 GetVector3OrZero(KVObject element, string name)
            => element.GetFloatArray(name) is [var x, var y, var z, ..] ? new Vector3(x, y, z) : Vector3.Zero;
    }

    /// <summary>
    /// Writes command arguments as the KV3 text without its header that the <c>args</c> key takes.
    /// </summary>
    private static string ToHeaderlessKV3(KVObject args)
    {
        var text = args.ToKV3String();
        var headerEnd = text.StartsWith("<!--", StringComparison.Ordinal) ? text.IndexOf('\n', StringComparison.Ordinal) : -1;

        return text[(headerEnd + 1)..].Trim();
    }
}
