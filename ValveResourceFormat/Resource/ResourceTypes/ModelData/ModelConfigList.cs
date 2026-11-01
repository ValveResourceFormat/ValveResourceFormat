using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.ResourceTypes.ModelData
{
    /// <summary>
    /// The configs of a model: named sets of elements that attach other models to it and set its material
    /// group, color and body groups. An entity picks between them with the <c>config_state</c> block of its
    /// <c>model_state</c> key, starting from <see cref="SpawnConfigName"/>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigList">CModelConfigList</seealso>
    public sealed class ModelConfigList
    {
        /// <summary>The config an entity is built with when it spawns.</summary>
        public const string SpawnConfigName = "@spawn";

        // Each pick leads into another config, so a cycle of them stops at this depth
        private const int MaxDepth = 10;

        /// <summary>Gets the configs, in compiled order.</summary>
        public IReadOnlyList<ModelConfig> Configs { get; }

        /// <summary>Initializes the configs from the <c>m_pModelConfigList</c> of a model.</summary>
        /// <param name="data">The config list, or <see langword="null"/> for a model without one.</param>
        public ModelConfigList(KVObject? data)
        {
            Configs = data?.GetArray("m_Configs") is { } configs ? [.. configs.Select(ModelConfig.Read)] : [];
        }

        /// <summary>Gets a config by its name.</summary>
        /// <returns>The config, or <see langword="null"/> when the model has none of that name.</returns>
        public ModelConfig? Find(string name)
        {
            foreach (var config in Configs)
            {
                if (config.Name == name)
                {
                    return config;
                }
            }

            return null;
        }

        /// <summary>
        /// Builds the model the way a config and the configs its picks lead to assemble it.
        /// </summary>
        /// <param name="configName">The config to start from, usually <see cref="SpawnConfigName"/>.</param>
        /// <param name="state">
        /// The <c>config_state</c> of the entity: the choice of each pick by the path of its element, and the
        /// <c>_color</c> and <c>_matgrp</c> of attached models that leave those to the entity.
        /// </param>
        /// <param name="random">Picks the choice of a random pick the state does not settle.</param>
        /// <returns>The result, or <see langword="null"/> when the model has no config of that name.</returns>
        public ModelConfigResult? Evaluate(string configName, KVObject? state, Random random)
        {
            ArgumentNullException.ThrowIfNull(random);

            if (Find(configName) is not { } config)
            {
                return null;
            }

            var result = new ModelConfigResult();
            var evaluation = new Evaluation(this, state, random, result);

            evaluation.EvaluateElements(config.Elements, result, parentIndex: -1, depth: 1);

            // Only known once every element ran, as a model can be attached before what applies to it
            foreach (var model in result.AttachedModels)
            {
                model.InheritFrom(model.ParentIndex >= 0 ? result.AttachedModels[model.ParentIndex] : result);
            }

            return result;
        }

        private sealed class Evaluation(ModelConfigList list, KVObject? state, Random random, ModelConfigResult result)
        {
            public void EvaluateElements(IReadOnlyList<ModelConfigElement> elements, ModelConfigAppearance target, int parentIndex, int depth)
            {
                foreach (var element in elements)
                {
                    switch (element)
                    {
                        case ModelConfigUserPick { Choices.Count: > 0 } pick:
                        {
                            // A choice the state does not name, or names wrongly, falls back to the first
                            var picked = state?.GetStringProperty(pick.StateKey) is { } name && pick.Choices.Contains(name) ? name : pick.Choices[0];

                            EvaluatePicked(picked, target, parentIndex, depth);
                            break;
                        }

                        case ModelConfigRandomPick { Choices.Count: > 0 } pick:
                        {
                            var index = state?.GetInt32Property(pick.StateKey, -1) ?? -1;

                            if (index < 0 || index >= pick.Choices.Count)
                            {
                                index = PickRandom(pick);
                            }

                            EvaluatePicked(pick.Choices[index], target, parentIndex, depth);
                            break;
                        }

                        case ModelConfigAttachedModel attached:
                        {
                            var model = new ModelConfigAttachedModelResult(attached, parentIndex);

                            if (attached.UserSpecifiedColor)
                            {
                                model.RenderColor = state?.GetArray<byte>($"{attached.Name}_color") is { } color
                                    ? new Color32(color[0], color[1], color[2])
                                    : Color32.White;
                            }

                            if (attached.UserSpecifiedMaterialGroup && state?.GetStringProperty($"{attached.Name}_matgrp") is { Length: > 0 } materialGroup)
                            {
                                model.MaterialGroup = materialGroup;
                            }

                            result.AddAttachedModel(model);

                            // What is nested in an attached model sets that model, not the one it is attached to
                            EvaluateElements(attached.NestedElements, model, result.AttachedModels.Count - 1, depth);
                            break;
                        }

                        case ModelConfigSetMaterialGroup setMaterialGroup:
                            target.MaterialGroup = setMaterialGroup.MaterialGroup;
                            break;

                        case ModelConfigSetRenderColor setRenderColor:
                            target.RenderColor = setRenderColor.Color;
                            break;

                        case ModelConfigSetBodyGroup setBodyGroup:
                            target.SetBodyGroup(setBodyGroup.GroupName, setBodyGroup.Choice);
                            break;

                        case ModelConfigSetMaterialGroupOnAttachedModels setMaterialGroup:
                            target.AttachedModelsMaterialGroup = setMaterialGroup.MaterialGroup;
                            break;

                        case ModelConfigSetBodyGroupOnAttachedModels setBodyGroup:
                            target.SetAttachedModelsBodyGroup(setBodyGroup.GroupName, setBodyGroup.Choice);
                            break;

                        case ModelConfigUnsupportedElement unsupported:
                            result.AddUnsupportedElement(unsupported.ClassName);
                            break;
                    }
                }
            }

            private void EvaluatePicked(string configName, ModelConfigAppearance target, int parentIndex, int depth)
            {
                if (depth >= MaxDepth || list.Find(configName) is not { } config)
                {
                    return;
                }

                EvaluateElements(config.Elements, target, parentIndex, depth + 1);
            }

            private int PickRandom(ModelConfigRandomPick pick)
            {
                var weights = pick.ChoiceWeights;
                var total = weights.Count == pick.Choices.Count ? weights.Sum(static weight => MathF.Max(weight, 0f)) : 0f;

                if (total <= 0f)
                {
                    return random.Next(pick.Choices.Count);
                }

                var roll = random.NextSingle() * total;

                for (var i = 0; i < weights.Count; i++)
                {
                    roll -= MathF.Max(weights[i], 0f);

                    if (roll < 0f)
                    {
                        return i;
                    }
                }

                return weights.Count - 1;
            }
        }
    }

    /// <summary>A named set of elements of a model config list.</summary>
    /// <param name="Name">The name picks refer to it by. A config the compiler names starts with <c>@</c>.</param>
    /// <param name="Elements">The elements, in compiled order.</param>
    /// <param name="IsTopLevel">Whether the config is offered on its own rather than only through picks.</param>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfig">CModelConfig</seealso>
    public sealed record ModelConfig(string Name, IReadOnlyList<ModelConfigElement> Elements, bool IsTopLevel)
    {
        internal static ModelConfig Read(KVObject data)
        {
            var name = data.GetStringProperty("m_ConfigName", string.Empty);
            var elements = ModelConfigElement.ReadList(data.GetArray("m_Elements"), string.Empty);

            return new ModelConfig(name, elements, data.GetBooleanProperty("m_bTopLevel"));
        }
    }

    /// <summary>
    /// What a model config does to the model it is evaluated for, or to the model it attaches.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement">CModelConfigElement</seealso>
    public abstract class ModelConfigElement
    {
        /// <summary>Gets the name of the element, which may be empty.</summary>
        public string Name { get; }

        /// <summary>
        /// Gets the key the element is found by in the <c>config_state</c> of an entity: its name, or
        /// <c>#</c> and its index when it has none, after the key of the element it is nested in and a dot.
        /// </summary>
        public string StateKey { get; }

        /// <summary>Gets the elements nested in this one.</summary>
        public IReadOnlyList<ModelConfigElement> NestedElements { get; }

        private protected ModelConfigElement(KVObject data, string stateKey)
        {
            Name = data.GetStringProperty("m_ElementName", string.Empty);
            StateKey = stateKey;
            NestedElements = ReadList(data.GetArray("m_NestedElements"), stateKey);
        }

        internal static ModelConfigElement[] ReadList(IReadOnlyList<KVObject>? elements, string parentKey)
        {
            if (elements == null)
            {
                return [];
            }

            var list = new ModelConfigElement[elements.Count];

            for (var i = 0; i < elements.Count; i++)
            {
                var data = elements[i];
                var name = data.GetStringProperty("m_ElementName", string.Empty);
                var key = name.Length > 0 ? name : string.Create(CultureInfo.InvariantCulture, $"#{i}");

                if (parentKey.Length > 0)
                {
                    key = $"{parentKey}.{key}";
                }

                list[i] = data.GetStringProperty("_class") switch
                {
                    "CModelConfigElement_AttachedModel" => new ModelConfigAttachedModel(data, key),
                    "CModelConfigElement_UserPick" => new ModelConfigUserPick(data, key),
                    "CModelConfigElement_RandomPick" => new ModelConfigRandomPick(data, key),
                    "CModelConfigElement_SetMaterialGroup" => new ModelConfigSetMaterialGroup(data, key),
                    "CModelConfigElement_SetRenderColor" => new ModelConfigSetRenderColor(data, key),
                    "CModelConfigElement_SetBodygroup" => new ModelConfigSetBodyGroup(data, key),
                    "CModelConfigElement_SetMaterialGroupOnAttachedModels" => new ModelConfigSetMaterialGroupOnAttachedModels(data, key),
                    "CModelConfigElement_SetBodygroupOnAttachedModels" => new ModelConfigSetBodyGroupOnAttachedModels(data, key),
                    var className => new ModelConfigUnsupportedElement(data, key, className ?? string.Empty),
                };
            }

            return list;
        }
    }

    /// <summary>Attaches another model to the one the config is evaluated for.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_AttachedModel">CModelConfigElement_AttachedModel</seealso>
    public sealed class ModelConfigAttachedModel : ModelConfigElement
    {
        /// <summary>Gets the name the attached model is known by, which may be empty.</summary>
        public string InstanceName { get; }

        /// <summary>Gets the entity class the attached model is spawned as, or an empty string for the default.</summary>
        public string EntityClass { get; }

        /// <summary>Gets the model that is attached.</summary>
        public string ModelName { get; }

        /// <summary>Gets the position of the attached model in the frame it is attached at.</summary>
        public Vector3 Offset { get; }

        /// <summary>Gets the rotation of the attached model in the frame it is attached at, as a QAngle.</summary>
        public Vector3 AngleOffset { get; }

        /// <summary>Gets the attachment point or bone the model is attached at, for <see cref="ModelConfigAttachmentType.BoneOrAttachment"/>.</summary>
        public string AttachmentName { get; }

        /// <summary>Gets the attachment point of the attached model that is placed at the frame, or an empty string for its origin.</summary>
        public string LocalAttachmentOffsetName { get; }

        /// <summary>Gets how the model is placed on the one it is attached to.</summary>
        public ModelConfigAttachmentType AttachmentType { get; }

        /// <summary>Gets whether the entity gives the color of the attached model, as the <c>_color</c> of the element's name.</summary>
        public bool UserSpecifiedColor { get; }

        /// <summary>Gets whether the entity gives the material group of the attached model, as the <c>_matgrp</c> of the element's name.</summary>
        public bool UserSpecifiedMaterialGroup { get; }

        internal ModelConfigAttachedModel(KVObject data, string stateKey) : base(data, stateKey)
        {
            InstanceName = data.GetStringProperty("m_InstanceName", string.Empty);
            EntityClass = data.GetStringProperty("m_EntityClass", string.Empty);
            ModelName = data.GetStringProperty("m_hModel", string.Empty);
            Offset = data.GetSubCollection("m_vOffset").ToVector3();
            AngleOffset = data.GetSubCollection("m_aAngOffset").ToVector3();
            AttachmentName = data.GetStringProperty("m_AttachmentName", string.Empty);
            LocalAttachmentOffsetName = data.GetStringProperty("m_LocalAttachmentOffsetName", string.Empty);
            AttachmentType = data.GetEnumValue<ModelConfigAttachmentType>("m_AttachmentType", normalize: true, stripExtension: "Type");
            UserSpecifiedColor = data.GetBooleanProperty("m_bUserSpecifiedColor");
            UserSpecifiedMaterialGroup = data.GetBooleanProperty("m_bUserSpecifiedMaterialGroup");
        }
    }

    /// <summary>
    /// Follows the config the entity picks, by the value of <see cref="ModelConfigElement.StateKey"/> in its
    /// <c>config_state</c>, or else the first choice.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_UserPick">CModelConfigElement_UserPick</seealso>
    public sealed class ModelConfigUserPick : ModelConfigElement
    {
        /// <summary>Gets the names of the configs to pick from.</summary>
        public IReadOnlyList<string> Choices { get; }

        internal ModelConfigUserPick(KVObject data, string stateKey) : base(data, stateKey)
        {
            Choices = data.GetArray<string>("m_Choices") ?? [];
        }
    }

    /// <summary>
    /// Follows a config picked at random, unless the <c>config_state</c> of the entity holds the index of
    /// the choice at <see cref="ModelConfigElement.StateKey"/>.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_RandomPick">CModelConfigElement_RandomPick</seealso>
    public sealed class ModelConfigRandomPick : ModelConfigElement
    {
        /// <summary>Gets the names of the configs to pick from.</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>Gets how likely each choice is, or nothing when they are equally likely.</summary>
        public IReadOnlyList<float> ChoiceWeights { get; }

        internal ModelConfigRandomPick(KVObject data, string stateKey) : base(data, stateKey)
        {
            Choices = data.GetArray<string>("m_Choices") ?? [];
            ChoiceWeights = data.GetFloatArray("m_ChoiceWeights");
        }
    }

    /// <summary>Sets the material group of the model it applies to.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_SetMaterialGroup">CModelConfigElement_SetMaterialGroup</seealso>
    public sealed class ModelConfigSetMaterialGroup : ModelConfigElement
    {
        /// <summary>Gets the name of the material group.</summary>
        public string MaterialGroup { get; }

        internal ModelConfigSetMaterialGroup(KVObject data, string stateKey) : base(data, stateKey)
        {
            MaterialGroup = data.GetStringProperty("m_MaterialGroupName", string.Empty);
        }
    }

    /// <summary>Sets the color of the model it applies to.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_SetRenderColor">CModelConfigElement_SetRenderColor</seealso>
    public sealed class ModelConfigSetRenderColor : ModelConfigElement
    {
        /// <summary>Gets the color.</summary>
        public Color32 Color { get; }

        internal ModelConfigSetRenderColor(KVObject data, string stateKey) : base(data, stateKey)
        {
            var color = data.GetArray<byte>("m_Color");

            if (color is not { Length: >= 3 })
            {
                Color = Color32.White;
                return;
            }

            var alpha = color.Length > 3 ? color[3] : (byte)255;

            Color = new Color32(color[0], color[1], color[2], alpha);
        }
    }

    /// <summary>Sets the choice of a body group of the model it applies to.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_SetBodygroup">CModelConfigElement_SetBodygroup</seealso>
    public sealed class ModelConfigSetBodyGroup : ModelConfigElement
    {
        /// <summary>Gets the name of the body group.</summary>
        public string GroupName { get; }

        /// <summary>Gets the number of the choice.</summary>
        public int Choice { get; }

        internal ModelConfigSetBodyGroup(KVObject data, string stateKey) : base(data, stateKey)
        {
            GroupName = data.GetStringProperty("m_GroupName", string.Empty);
            Choice = data.GetInt32Property("m_nChoice");
        }
    }

    /// <summary>Sets the material group of the models attached to the model it applies to.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_SetMaterialGroupOnAttachedModels">CModelConfigElement_SetMaterialGroupOnAttachedModels</seealso>
    public sealed class ModelConfigSetMaterialGroupOnAttachedModels : ModelConfigElement
    {
        /// <summary>Gets the name of the material group.</summary>
        public string MaterialGroup { get; }

        internal ModelConfigSetMaterialGroupOnAttachedModels(KVObject data, string stateKey) : base(data, stateKey)
        {
            MaterialGroup = data.GetStringProperty("m_MaterialGroupName", string.Empty);
        }
    }

    /// <summary>Sets the choice of a body group of the models attached to the model it applies to.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/CModelConfigElement_SetBodygroupOnAttachedModels">CModelConfigElement_SetBodygroupOnAttachedModels</seealso>
    public sealed class ModelConfigSetBodyGroupOnAttachedModels : ModelConfigElement
    {
        /// <summary>Gets the name of the body group.</summary>
        public string GroupName { get; }

        /// <summary>Gets the number of the choice.</summary>
        public int Choice { get; }

        internal ModelConfigSetBodyGroupOnAttachedModels(KVObject data, string stateKey) : base(data, stateKey)
        {
            GroupName = data.GetStringProperty("m_GroupName", string.Empty);
            Choice = data.GetInt32Property("m_nChoice");
        }
    }

    /// <summary>An element <see cref="ModelConfigList.Evaluate"/> does not apply, such as a random color or a command.</summary>
    public sealed class ModelConfigUnsupportedElement : ModelConfigElement
    {
        /// <summary>Gets the compiled class of the element.</summary>
        public string ClassName { get; }

        internal ModelConfigUnsupportedElement(KVObject data, string stateKey, string className) : base(data, stateKey)
        {
            ClassName = className;
        }
    }

    /// <summary>What a model config sets on one model: its material group, color and body groups.</summary>
    public abstract class ModelConfigAppearance
    {
        private readonly Dictionary<string, int> bodyGroups = [];
        private readonly Dictionary<string, int> attachedModelsBodyGroups = [];

        /// <summary>Gets the material group, or <see langword="null"/> when the config leaves it.</summary>
        public string? MaterialGroup { get; internal set; }

        /// <summary>Gets the color, or <see langword="null"/> when the config leaves it.</summary>
        public Color32? RenderColor { get; internal set; }

        /// <summary>Gets the choice of each body group the config sets, by body group name.</summary>
        public IReadOnlyDictionary<string, int> BodyGroups => bodyGroups;

        private protected ModelConfigAppearance()
        {
        }

        internal string? AttachedModelsMaterialGroup { get; set; }

        internal void SetBodyGroup(string name, int choice) => bodyGroups[name] = choice;

        internal void SetAttachedModelsBodyGroup(string name, int choice) => attachedModelsBodyGroups[name] = choice;

        // What the config sets on the model itself wins over what its parent sets on all it attaches
        internal void InheritFrom(ModelConfigAppearance parent)
        {
            MaterialGroup ??= parent.AttachedModelsMaterialGroup;

            foreach (var (name, choice) in parent.attachedModelsBodyGroups)
            {
                bodyGroups.TryAdd(name, choice);
            }
        }
    }

    /// <summary>What a model config makes of the model it is evaluated for.</summary>
    public sealed class ModelConfigResult : ModelConfigAppearance
    {
        private readonly List<ModelConfigAttachedModelResult> attachedModels = [];
        private readonly HashSet<string> unsupportedElements = [];

        /// <summary>Gets the models attached to it, a model always after the one it is attached to.</summary>
        public IReadOnlyList<ModelConfigAttachedModelResult> AttachedModels => attachedModels;

        /// <summary>Gets the compiled classes of the elements the evaluation reached but did not apply.</summary>
        public IReadOnlyCollection<string> UnsupportedElements => unsupportedElements;

        internal ModelConfigResult()
        {
        }

        internal void AddAttachedModel(ModelConfigAttachedModelResult model) => attachedModels.Add(model);

        internal void AddUnsupportedElement(string className) => unsupportedElements.Add(className);
    }

    /// <summary>A model a config attaches, and what the config sets on it.</summary>
    public sealed class ModelConfigAttachedModelResult : ModelConfigAppearance
    {
        /// <summary>Gets the element that attaches the model.</summary>
        public ModelConfigAttachedModel Element { get; }

        /// <summary>
        /// Gets the index in <see cref="ModelConfigResult.AttachedModels"/> of the model this one is attached to,
        /// or -1 for the model the config is evaluated for.
        /// </summary>
        public int ParentIndex { get; }

        internal ModelConfigAttachedModelResult(ModelConfigAttachedModel element, int parentIndex)
        {
            Element = element;
            ParentIndex = parentIndex;
        }
    }
}
