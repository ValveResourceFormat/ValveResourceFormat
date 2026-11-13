using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.IO.KVHelpers;

namespace ValveResourceFormat.IO;

/// <summary>
/// Rebuilds the model doc config nodes from the compiled config list. The compiler makes one config per
/// node it reaches, in the order it walks them from the spawn config, and names a nameless one after a
/// counter; the nodes are written in that same order so the names come out the same.
/// </summary>
partial class ModelExtract
{
    private static void AddModelConfigNodes(Model model, ModelDocLists lists)
    {
        if (model.Data.GetSubCollection("m_pModelConfigList") is not { ValueType: KVValueType.Collection } configList)
        {
            return;
        }

        new ModelConfigNodeWriter(configList, lists).Write();
    }

    private sealed class ModelConfigNodeWriter(KVObject configList, ModelDocLists lists)
    {
        private readonly Dictionary<string, KVObject> configs = [];
        private readonly HashSet<string> createdConfigs = [];
        private readonly HashSet<string> nodeNames = [];

        public void Write()
        {
            foreach (var config in configList.GetArray("m_Configs"))
            {
                configs[config.GetStringProperty("m_ConfigName")] = config;
            }

            var spawnConfig = MakeListNode("SpawnConfig");
            lists.RootChildren.Add(spawnConfig.Node);

            if (configList.GetBooleanProperty("m_bHideMaterialGroupInTools"))
            {
                spawnConfig.Node.Add("hide_materialgroup_in_tools", true);
            }

            if (configList.GetBooleanProperty("m_bHideRenderColorInTools"))
            {
                spawnConfig.Node.Add("hide_rendercolor_in_tools", true);
            }

            createdConfigs.Add(ModelConfigList.SpawnConfigName);
            AddElements(spawnConfig.Children, configs[ModelConfigList.SpawnConfigName]);

            foreach (var config in configList.GetArray("m_Configs"))
            {
                if (config.GetBooleanProperty("m_bTopLevel"))
                {
                    CreateTopLevelConfig(config);
                }
            }
        }

        // A top level config referred to by a pick is created when the compiler reaches that pick
        private void CreateTopLevelConfig(KVObject config)
        {
            var name = config.GetStringProperty("m_ConfigName");

            if (!createdConfigs.Add(name))
            {
                return;
            }

            var dynamicConfig = MakeListNode("DynamicConfig");
            lists.DynamicConfigs.Add(dynamicConfig.Node);

            if (!IsUnnamed(name))
            {
                dynamicConfig.Node.Add("name", name);
                nodeNames.Add(name);
            }

            if (config.GetBooleanProperty("m_bActiveInEditorByDefault"))
            {
                dynamicConfig.Node.Add("active_in_editor_by_default", true);
            }

            AddElements(dynamicConfig.Children, config);
        }

        private void AddElements(KVObject children, KVObject config)
        {
            foreach (var element in config.GetArray("m_Elements"))
            {
                children.Add(MakeElementNode(element));
            }
        }

        private KVObject MakeElementNode(KVObject element)
        {
            var name = element.GetStringProperty("m_ElementName", string.Empty);

            // The same element in several configs is one node the others refer to
            if (name.Length > 0 && !nodeNames.Add(name))
            {
                return MakeReference(name);
            }

            var className = element.GetStringProperty("_class");
            var node = className switch
            {
                "CModelConfigElement_UserPick" => MakePickNode("ConfigUserPick", element),
                "CModelConfigElement_RandomPick" => MakePickNode("ConfigRandomPick", element),
                "CModelConfigElement_AttachedModel" => MakeAttachedModelNode(element),
                "CModelConfigElement_SetRenderColor" => MakeNode("ConfigSetRenderColor", ("color", MakeColor(element.GetArray<int>("m_Color")))),
                "CModelConfigElement_RandomColor" => MakeNode("ConfigRandomColor", ("gradient", element.GetSubCollection("m_Gradient"))),
                "CModelConfigElement_SetBodygroup" => MakeBodyGroupNode("ConfigSetBodygroup", element),
                "CModelConfigElement_SetBodygroupOnAttachedModels" => MakeBodyGroupNode("ConfigSetBodygroupOnAttachedModels", element),
                "CModelConfigElement_SetMaterialGroup" => MakeNode("ConfigSetMaterialGroup", ("material_group", element.GetStringProperty("m_MaterialGroupName"))),
                "CModelConfigElement_SetMaterialGroupOnAttachedModels" => MakeNode("ConfigSetMaterialGroupOnAttachedModels", ("material_group", element.GetStringProperty("m_MaterialGroupName"))),
                "CModelConfigElement_Command" => MakeCommandNode(element),
                _ => throw new UnexpectedMagicException("Unknown model config element", className, nameof(className)),
            };

            if (name.Length > 0)
            {
                node.Add("name", name);
            }

            return node;
        }

        private KVObject MakePickNode(string className, KVObject element)
        {
            var pick = MakeListNode(className);
            var weights = element.GetFloatArray("m_ChoiceWeights");
            var choices = element.GetArray<string>("m_Choices");

            for (var i = 0; i < choices.Length; i++)
            {
                var choice = MakeChoiceNode(choices[i]);

                if (i < weights.Length && weights[i] != 1f)
                {
                    choice.Add("choice_weight", weights[i]);
                }

                pick.Children.Add(choice);
            }

            return pick.Node;
        }

        private KVObject MakeChoiceNode(string configName)
        {
            var config = configs[configName];

            if (config.GetBooleanProperty("m_bTopLevel"))
            {
                CreateTopLevelConfig(config);
                return MakeReference(configName);
            }

            if (createdConfigs.Contains(configName) || nodeNames.Contains(configName))
            {
                return MakeReference(configName);
            }

            createdConfigs.Add(configName);

            var unnamed = IsUnnamed(configName);
            var elements = config.GetArray("m_Elements");

            // A choice that is a single element takes the config name from the element
            if (elements.Count == 1 && elements[0].GetStringProperty("m_ElementName", string.Empty) == (unnamed ? string.Empty : configName))
            {
                return MakeElementNode(elements[0]);
            }

            var group = MakeListNode("ConfigGroup");

            if (!unnamed)
            {
                group.Node.Add("name", configName);
                nodeNames.Add(configName);
            }

            AddElements(group.Children, config);
            return group.Node;
        }

        private KVObject MakeAttachedModelNode(KVObject element)
        {
            var isAttached = element.GetStringProperty("m_AttachmentType") == "MODEL_CONFIG_ATTACHMENT_BONE_OR_ATTACHMENT";
            var attachmentType = element.GetStringProperty("m_AttachmentType") switch
            {
                "MODEL_CONFIG_ATTACHMENT_BONEMERGE" => element.GetBooleanProperty("m_bBoneMergeFlex") ? "bonemergeflex" : "bonemerge",
                "MODEL_CONFIG_ATTACHMENT_BONE_OR_ATTACHMENT" => "attach",
                _ => "root",
            };

            var node = MakeNode("ConfigAttachedModel",
                ("model_name", element.GetStringProperty("m_hModel", string.Empty)),
                ("attachment_type", attachmentType)
            );

            AddStringIfSet(node, "entity_class", element, "m_EntityClass");
            AddStringIfSet(node, "attach_point", element, "m_AttachmentName");

            var origin = element.GetSubCollection("m_vOffset").ToVector3();
            var angles = element.GetSubCollection("m_aAngOffset").ToVector3();

            if (origin != Vector3.Zero || angles != Vector3.Zero)
            {
                node.Add("relative_origin", ToKVArray(origin));
                node.Add("relative_angles", ToKVArray(angles));

                if (isAttached)
                {
                    node.Add("use_additional_offset", true);
                }
            }

            if (AddStringIfSet(node, "local_attach_point_offset", element, "m_LocalAttachmentOffsetName") && isAttached)
            {
                node.Add("use_local_attach_point_offset", true);
            }

            AddStringIfSet(node, "set_bodygroup_on_other_models", element, "m_BodygroupOnOtherModels");
            AddStringIfSet(node, "set_materialgroup_on_other_models", element, "m_MaterialGroupOnOtherModels");

            foreach (var (key, compiledKey) in (ReadOnlySpan<(string, string)>)[
                ("user_specified_color", "m_bUserSpecifiedColor"),
                ("user_specified_materialgroup", "m_bUserSpecifiedMaterialGroup"),
                ("accept_parent_materialdriven_decals", "m_bAcceptParentMaterialDrivenDecals"),
                ("collide_with_hierarchy", "m_bCollideWithHierarchy"),
            ])
            {
                if (element.GetBooleanProperty(compiledKey))
                {
                    node.Add(key, true);
                }
            }

            if (!element.GetBooleanProperty("m_bCollideOutsideHierarchy", true))
            {
                node.Add("collide_outside_hierarchy", false);
            }

            var children = KVObject.Array();
            node.Add("children", children);

            foreach (var nested in element.GetArray("m_NestedElements"))
            {
                children.Add(MakeElementNode(nested));
            }

            return node;
        }

        private static KVObject MakeBodyGroupNode(string className, KVObject element) => MakeNode(className,
            ("group_name", element.GetStringProperty("m_GroupName")),
            ("choice", element.GetInt32Property("m_nChoice"))
        );

        private static KVObject MakeCommandNode(KVObject element)
        {
            var node = MakeNode("ConfigCommand", ("command", element.GetStringProperty("m_Command")));

            if (element.GetSubCollection("m_Args") is { ValueType: KVValueType.Collection } args)
            {
                // The compiler parses the args from text without a header
                var text = args.ToKV3String();
                node.Add("args", text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..]);
            }

            return node;
        }

        // A compiled color may leave out an opaque alpha, which the compiler reads as 0 when it is authored that way
        private static KVObject MakeColor(int[] color) => MakeArray(color[0], color[1], color[2], color.Length > 3 ? color[3] : 255);

        private static bool AddStringIfSet(KVObject node, string key, KVObject element, string compiledKey)
        {
            if (element.GetStringProperty(compiledKey, string.Empty) is not { Length: > 0 } value)
            {
                return false;
            }

            node.Add(key, value);
            return true;
        }

        private static KVObject MakeReference(string name) => MakeNode("ConfigReference", ("config_reference", name));

        private static bool IsUnnamed(string configName) => configName.StartsWith("@unnamed_", StringComparison.Ordinal);
    }
}
