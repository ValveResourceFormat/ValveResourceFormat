using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An entity with something to draw.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBaseModelEntity">CBaseModelEntity</seealso>
public abstract class BaseModelEntity : BaseEntity
{
    /// <summary>How an entity collides.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/client/SolidType_t">SolidType_t</seealso>
    public enum SolidType
    {
        /// <summary>Not solid.</summary>
        SOLID_NONE = 0,
        /// <summary>Brush model.</summary>
        SOLID_BSP = 1,
        /// <summary>Axis-aligned bounding box.</summary>
        SOLID_BBOX = 2,
        /// <summary>Oriented bounding box.</summary>
        SOLID_OBB = 3,
        /// <summary>Sphere.</summary>
        SOLID_SPHERE = 4,
        /// <summary>Point.</summary>
        SOLID_POINT = 5,
        /// <summary>The model's physics collision.</summary>
        SOLID_VPHYSICS = 6,
        /// <summary>Capsule.</summary>
        SOLID_CAPSULE = 7,
        /// <summary>Cylinder.</summary>
        SOLID_CYLINDER = 8,
    }

    /// <summary>Gets the solid type.</summary>
    public SolidType Solid { get; protected set; }

    /// <summary>
    /// Gets the node this entity draws as, or <see langword="null"/> when its model has no meshes,
    /// as with a brush compiled for collision alone.
    /// </summary>
    public ModelSceneNode? ModelNode { get; private set; }

    /// <summary>Gets the physics of the model, or <see langword="null"/> when it has none.</summary>
    public PhysAggregateData? Physics { get; private set; }

    /// <summary>Gets the model this entity loaded, or <see langword="null"/> when it names none or it failed to load.</summary>
    public Model? LoadedModel { get; private set; }

    /// <summary>Initializes a model entity from its keyvalues.</summary>
    protected BaseModelEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        Solid = KeyValues.ContainsKey("solid") ? KeyValues.GetEnumValue<SolidType>("solid") : SolidType.SOLID_NONE;
    }

    /// <summary>
    /// Loads the model and makes it this entity's own node. Brush entities carry their geometry
    /// this way, in a model compiled next to the map.
    /// </summary>
    /// <returns>
    /// The model node, or the editor box from <see cref="BaseEntity.CreateRootNode"/> when there is
    /// nothing to draw, so a collision-only entity can still be seen and picked.
    /// </returns>
    protected override SceneNode? CreateRootNode()
    {
        var modelName = ModelName;

        if (string.IsNullOrEmpty(modelName))
        {
            return base.CreateRootNode();
        }

        var fileLoader = EntitySystem.FileLoader;

        if (fileLoader.LoadFileCompiled(modelName)?.DataBlock is not Model model)
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to load model \"{Model}\"", Classname, TargetName, modelName);

            // Stands in for the missing model
            if (fileLoader.LoadFile("models/dev/error.vmdl_c")?.DataBlock is Model errorModel)
            {
                return new ModelSceneNode(Scene, errorModel, Data?.GetStringProperty("skin"))
                {
                    Name = "error",
                };
            }

            return base.CreateRootNode();
        }

        LoadedModel = model;

        var tint = Data?.GetRenderTint() ?? Vector4.One;

        // A model that spawns fully transparent is faded in by the map later, so an editor shows it opaque
        if (tint.W == 0f && !EntitySystem.Enabled)
        {
            tint.W = 1f;
        }

        var modelNode = new ModelSceneNode(Scene, model, Data?.GetStringProperty("skin"))
        {
            Name = modelName,
            TintAlpha = tint,
        };

        if (Data != null && DisablesShadows(Data))
        {
            modelNode.Flags |= ObjectTypeFlags.NoShadows;
        }

        modelNode.FixupLightingOriginToViewer = Data?.GetBooleanProperty("fixup_lighting_origin_to_player_side") == true;

        // Model-referenced particles spawn regardless of meshes
        var particleNodes = ParticleSceneNode.CreateModelParticles(Scene, model, modelNode);

        foreach (var particleNode in particleNodes)
        {
            particleNode.Layers = [WorldLayer.Particles];

            // Owned, so they leave the scene with the entity
            AddNode(particleNode, followsEntity: false);
        }

        // Meshes are only known once the node is built, so a collision-only model's node is dropped.
        // A particle-only model keeps its node for the follow attachments.
        if (modelNode.HasMeshes || particleNodes.Count > 0)
        {
            // Not added here: the caller takes the returned node as the entity's own
            ModelNode = modelNode;
        }

        if (modelNode.HasMeshes)
        {
            // Physics is baked in the model's posed frame, but the raw mesh can sit in modeldoc's
            // working frame (de_nuke's doors differ by 90 degrees). Props are always posed by a
            // sequence, so apply the authored animation or the modeldoc ref.
            var animation = Data?.GetStringProperty("startinganim")
                ?? Data?.GetStringProperty("defaultanim")
                ?? Data?.GetStringProperty("idleanim");

            if (animation != null && modelNode.SetAnimationForWorldPreview(animation))
            {
                if (Data?.GetBooleanProperty("holdanimation") == true)
                {
                    modelNode.AnimationController.PauseLastFrame();
                }
            }
            else
            {
                modelNode.SetAnimationForWorldPreview("ref");
            }

            var body = Data?.GetIntegerProperty("body", -1L) ?? -1L;

            if (body != -1L)
            {
                modelNode.SetActiveMeshGroups(modelNode.GetMeshGroups().Skip((int)body).Take(1));
            }

            ApplyModelState(model, modelNode);
        }

        if (EntityCollider.LoadPhysics(model, fileLoader) is { } physics)
        {
            Physics = physics;

            if (BuildsCollider)
            {
                Collider = new EntityCollider(physics);
                UpdateColliderTransform();
            }

            // Owned by the entity, not the model node: a collision-only brush has no model node,
            // and what the tool draws of its collision is all there is to show.
            foreach (var node in EntitySystem.ToolVisuals?.CreateCollisionVisuals(Scene, physics, modelName, this) ?? [])
            {
                AddNode(node);
            }

            // intentionally skip default scene node if phys exists
            return ModelNode;
        }

        return ModelNode ?? base.CreateRootNode();
    }

    // Older games store a boolean, newer ones a cast shadows mode where 1 is no shadows. The modes that keep
    // only realtime (2) or only baked (3) shadows are not modelled.
    private static bool DisablesShadows(EntityLump.Entity data)
    {
        if (!data.TryGetValue("disableshadows", out var value))
        {
            return false;
        }

        return value.ValueType switch
        {
            KVValueType.Boolean => (bool)value,
            KVValueType.String => ParseDisable((string)value),
            _ => (int)value == 1,
        };
    }

    private static bool ParseDisable(string text)
    {
        if (bool.TryParse(text, out var disabled))
        {
            return disabled;
        }

        return text == "1";
    }

    // The model state sets the material group and the body groups, then picks between the configs of the
    // model, which a model with a spawn config is built from even when the entity has no model state
    private void ApplyModelState(Model model, ModelSceneNode node)
    {
        var modelState = ParseCollectionProperty("model_state");

        if (modelState?.GetStringProperty("material_group") is { Length: > 0 } materialGroup)
        {
            node.SetMaterialGroup(materialGroup);
        }

        var bodyGroups = modelState?.GetSubCollection("body_groups") is { ValueType: KVValueType.Collection } stateBodyGroups
            ? stateBodyGroups
            : ParseCollectionProperty("bodygroups");

        if (bodyGroups != null)
        {
            foreach (var (name, value) in bodyGroups)
            {
                SetBodyGroup(node, name, ReadBodyGroupChoice(value));
            }
        }

        var configState = modelState?.GetSubCollection("config_state") is { ValueType: KVValueType.Collection } state ? state : null;

        if (model.Configs.Evaluate(ModelConfigList.SpawnConfigName, configState, CreateConfigRandom()) is { } result)
        {
            AddConfigModels(node, result);
        }
    }

    private KVObject? ParseCollectionProperty(string key)
        => Data?.GetStringProperty(key) is { Length: > 0 } text
            && KVDocumentExtensions.TryParseKV3WithoutHeader(text, out var block, out _) && block.ValueType == KVValueType.Collection
                ? block
                : null;

    // The game picks at random on every spawn, while a viewer that picks the same for an entity on every load
    // shows a map the same way each time it is opened
    private Random CreateConfigRandom()
    {
        var seed = Data?.GetStringProperty("hammeruniqueid") is { Length: > 0 } id
            ? id
            : string.Create(CultureInfo.InvariantCulture, $"{Classname} {WorldOrigin}");

        return new Random(unchecked((int)StringToken.Get(seed)));
    }

    private void AddConfigModels(ModelSceneNode root, ModelConfigResult result)
    {
        foreach (var className in result.UnsupportedElements)
        {
            EntitySystem.Logger.LogDebug("{Classname} '{TargetName}' skipped model config element {Element}", Classname, TargetName, className);
        }

        ApplyConfigAppearance(root, result);

        var nodes = new ModelSceneNode?[result.AttachedModels.Count];
        var scale = Matrix4x4.CreateScale(EntityScale);

        for (var i = 0; i < nodes.Length; i++)
        {
            var attached = result.AttachedModels[i];
            var element = attached.Element;
            var parent = attached.ParentIndex >= 0 ? nodes[attached.ParentIndex] : root;

            if (parent == null || element.AttachmentType == ModelConfigAttachmentType.Invalid || element.ModelName.Length == 0)
            {
                continue;
            }

            if (EntitySystem.FileLoader.LoadFileCompiled(element.ModelName)?.DataBlock is not Model model)
            {
                EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' failed to load attached model \"{Model}\"", Classname, TargetName, element.ModelName);
                continue;
            }

            var node = new ModelSceneNode(Scene, model)
            {
                Name = element.ModelName,
                Alpha = root.Alpha,
            };

            node.Flags |= root.Flags & ObjectTypeFlags.NoShadows;

            // Frames of a parent model drop its scale, so the scale of the entity is applied again here
            var offset = scale * EntityTransformHelper.ToRigidTransformationMatrix(element.AngleOffset, element.Offset * EntityScale);

            // The node is still at the origin, so this is the attachment in the space of its own model
            if (element.LocalAttachmentOffsetName.Length > 0
                && node.Attachments.ContainsKey(element.LocalAttachmentOffsetName)
                && Matrix4x4.Invert(node.GetAttachmentTransform(element.LocalAttachmentOffsetName), out var toLocalAttachment))
            {
                offset = toLocalAttachment * offset;
            }

            switch (element.AttachmentType)
            {
                case ModelConfigAttachmentType.Bonemerge:
                    node.SetParent(parent, localTransform: scale);
                    node.BoneMerge(parent);
                    break;

                case ModelConfigAttachmentType.BoneOrAttachment when element.AttachmentName.Length > 0:
                    node.SetParent(parent, element.AttachmentName, offset);
                    break;

                default:
                    node.SetParent(parent, localTransform: offset);
                    break;
            }

            ApplyConfigAppearance(node, attached);

            // Placed by the model it is attached to
            AddNode(node, followsEntity: false);
            nodes[i] = node;
        }
    }

    private static void ApplyConfigAppearance(ModelSceneNode node, ModelConfigAppearance appearance)
    {
        if (appearance.MaterialGroup is { } materialGroup)
        {
            node.SetMaterialGroup(materialGroup);
        }

        if (appearance.RenderColor is { } color)
        {
            // Multiplies the color of the entity as Half-Life: Alyx does. Unproven: that rendercolor is applied
            // before this there, and that other games multiply too
            node.Tint *= new Vector3(color.R, color.G, color.B) / byte.MaxValue;
        }

        foreach (var (name, choice) in appearance.BodyGroups)
        {
            SetBodyGroup(node, name, choice);
        }
    }

    private static int ReadBodyGroupChoice(KVObject value) => value.ValueType switch
    {
        KVValueType.String => int.TryParse((string)value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
        KVValueType.Collection or KVValueType.Array or KVValueType.Null => 0,
        _ => value.ToInt32(CultureInfo.InvariantCulture),
    };

    private static void SetBodyGroup(ModelSceneNode node, string bodyGroup, int index)
    {
        foreach (var choice in node.GetBodyGroupChoices())
        {
            if (choice.Index == index && choice.BodyGroup.Equals(bodyGroup, StringComparison.OrdinalIgnoreCase))
            {
                node.SetBodyGroupChoice(choice);
                return;
            }
        }
    }

    /// <summary>
    /// Gets whether the model's physics becomes a <see cref="BaseEntity.Collider"/>; its hulls are drawn
    /// either way. Read during construction, so overrides must not depend on instance state.
    /// </summary>
    protected virtual bool BuildsCollider => true;

    /// <summary>Tints the model with <c>"R G B"</c> in 0-255.</summary>
    [EntityInput("Color")]
    protected void InputColor(EntityInputData data)
    {
        if (ModelNode is not { } node)
        {
            return;
        }

        if (data.Parameter == null || !EntityTransformHelper.TryParseVector3(data.Parameter.Trim(), out var color))
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' cannot take Color \"{Value}\", which is not \"R G B\"", Classname, TargetName, data.Parameter);
            return;
        }

        node.Tint = Vector3.Clamp(color / 255f, Vector3.Zero, Vector3.One);
    }

    /// <summary>Sets the model's alpha from 0-255.</summary>
    [EntityInput("Alpha")]
    protected void InputAlpha(EntityInputData data)
    {
        if (ModelNode is not { } node)
        {
            return;
        }

        var alpha = data.Float(float.NaN);

        if (float.IsNaN(alpha))
        {
            EntitySystem.Logger.LogWarning("{Classname} '{TargetName}' cannot take Alpha \"{Value}\", which is not a number", Classname, TargetName, data.Parameter);
            return;
        }

        node.Alpha = MathUtils.Saturate(alpha / 255f);
    }

    /// <summary>
    /// Lights the model as if it stood at the named entity. The position is sampled once and does not follow it.
    /// </summary>
    [EntityInput("LightingOrigin")]
    protected void InputLightingOrigin(EntityInputData data)
    {
        if (ModelNode is not { } node || string.IsNullOrEmpty(data.Parameter))
        {
            return;
        }

        if (EntitySystem.FindByTargetName(data.Parameter) is not { } origin)
        {
            return;
        }

        node.LightingOriginAttachment = null;
        node.LightingOrigin = origin.WorldOrigin;
        Scene.UpdateNodeEnvironmentMaps(node);
    }
}
