using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>Which bounds of its model a model helper draws while its entity is selected.</summary>
internal enum ModelBoundsMode
{
    /// <summary>The bounds of the model as it is built, which turn with the entity but follow neither its scale nor the animation.</summary>
    Fixed,

    /// <summary>The bounds of the model as it is drawn.</summary>
    Animated,

    /// <summary>None.</summary>
    None,
}

/// <summary>
/// The model an entity is drawn as: the one its class names, or the one its <c>model</c> key does. A class
/// declares it as <c>editormodel</c> where the model only stands in for the entity in the editor. Words after
/// the name of the model, or in its place, and the block the class declares it with change how it is drawn.
/// </summary>
public class ModelHelper : EntityHelper
{
    /// <inheritdoc/>
    public override bool IsStandIn => IsNamedByClass;

    /// <inheritdoc/>
    public override bool HiddenInGameObjectsOnly => IsEditorModel;

    // The bounds of the model while the entity is selected
    private static readonly Color32 BoundsColor = new(140, 215, 250);

    // What a class can put where the name of the model goes, to draw the model the entity names differently
    private static readonly string[] Options = ["static", "fixedbounds", "castshadows", "nobounds", "posable", "postocclusionquery"];

    // The keys of a class that name the animation shown, and how much each matters where several are set
    private const string PreviewAnimationKey = "is_preview_animation";
    private const string PreviewAnimationPriorityKey = "preview_animation_priority";

    // A light without a directlight key is drawn as one that is lit as it moves
    private const int DefaultDirectLight = 2;

    private readonly Model? model;
    private readonly Settings settings;
    private AABB fixedBounds;

    /// <summary>Gets whether the model only stands in for the entity in the editor.</summary>
    public bool IsEditorModel => settings.IsEditorModel;

    /// <summary>Gets the model, or <see langword="null"/> when the entity names none that loads.</summary>
    public Model? Model => model;

    /// <summary>Gets the model, or <see langword="null"/> when neither the class nor the entity names one.</summary>
    internal string? ModelName => settings.ModelName;

    /// <summary>
    /// Gets whether the class names the model, which then stands in for every entity of the class, rather
    /// than each entity naming its own with its <c>model</c> key.
    /// </summary>
    private bool IsNamedByClass => settings.IsNamedByClass;

    /// <summary>Gets which bounds of the model are drawn while the entity is selected.</summary>
    private ModelBoundsMode BoundsMode => settings.BoundsMode;

    /// <summary>Gets the bounds of the model in the space of the entity, or <see langword="null"/> before it is added.</summary>
    public AABB? ModelBounds => ModelNode?.LocalBoundingBox;

    /// <inheritdoc/>
    public override AABB? ObjectBounds => BoundsMode != ModelBoundsMode.None ? ModelNode?.BoundingBox : null;

    /// <summary>Gets the model as it is drawn, once it is in the scene.</summary>
    public ModelSceneNode? ModelNode { get; private set; }

    /// <summary>Gets whether the model moves along with the entity, which it does where it is placed at it.</summary>
    private protected virtual bool FollowsEntity => true;

    /// <summary>
    /// How a model helper draws its model, as the declaration of the class and the keys of the entity say
    /// before the model is loaded.
    /// </summary>
    private protected sealed record Settings
    {
        public string? ModelName { get; init; }

        public bool IsEditorModel { get; init; }

        public bool IsNamedByClass { get; init; }

        public ModelBoundsMode BoundsMode { get; init; } = ModelBoundsMode.Animated;

        public bool CastsShadows { get; init; } = true;

        // A static model does not animate
        public bool IsStatic { get; init; }

        // What is played where no animation is named, or the one named is not found
        public DefaultSequence DefaultSequence { get; init; } = DefaultSequence.Search;

        // The animation the class names
        public string? DefaultAnimation { get; init; }

        // The animation the key of the entity the class names gives, played over the rest
        public string? PreviewClip { get; init; }

        // The colors the model of a light is drawn in, by how the light is lit, in place of the color of the entity
        public (Color32 Baked, Color32 Stationary, Color32 Dynamic)? LightModeTints { get; init; }

        // The choice the first body group of the model is drawn with, whatever the entity says
        public int? ForcedFirstBodyGroup { get; init; }
    }

    /// <summary>What a model plays where nothing names an animation that it has.</summary>
    private protected enum DefaultSequence
    {
        /// <summary>The one of its sequences that looks meant for it: a reference pose, or else an idle.</summary>
        Search,

        /// <summary>Its first sequence.</summary>
        First,

        /// <summary>None.</summary>
        None,
    }

    private protected ModelHelper(IHelperOwner owner, HelperDeclaration declaration, Settings settings, Model? model) : base(owner, declaration)
    {
        this.settings = settings;
        this.model = model;
    }

    /// <summary>Creates the model a class declares.</summary>
    /// <returns>
    /// The model, or <see langword="null"/> when neither the class nor the entity names one or it does not
    /// load, so that the entity is drawn some other way.
    /// </returns>
    internal static ModelHelper? Create(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(declaration);

        var settings = ReadDeclaration(owner, declaration);

        return LoadModel(owner, settings.ModelName) is { } model ? new ModelHelper(owner, declaration, settings, model) : null;
    }

    /// <summary>Gets the model a class names in the declaration of a model helper.</summary>
    /// <param name="declaration">The helper as a class declares it.</param>
    /// <returns>The name as it is declared, or <see langword="null"/> when the model is the one each entity names.</returns>
    public static string? GetDeclaredModelName(HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);

        return declaration.Arguments.Count > 0 && declaration.Arguments[0] is { Length: > 0 } argument && !IsOption(argument)
            ? argument
            : null;
    }

    /// <summary>Gets the model a class or an entity names, as a vmdl.</summary>
    /// <param name="name">The name as it is declared.</param>
    /// <returns>The name of the model.</returns>
    public static string GetModelName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        name = name.Replace('\\', '/');

        if (name.EndsWith(".mdl", StringComparison.OrdinalIgnoreCase))
        {
            return name[..^4] + ".vmdl";
        }

        return name.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase) ? name : name + ".vmdl";
    }

    private static bool IsOption(string argument) => Array.Exists(Options, option => option.Equals(argument, StringComparison.OrdinalIgnoreCase));

    private static Settings ReadDeclaration(IHelperOwner owner, HelperDeclaration declaration)
    {
        var block = declaration.Metadata;
        var isEditorModelName = declaration.Name.Equals("editormodel", StringComparison.OrdinalIgnoreCase);
        var isEditorModel = isEditorModelName || GetBlockFlag(block, "editorobject");

        string? name;
        bool isNamedByClass;
        int firstOption;

        // A class can name a key of the entity that holds the model, rather than the model itself
        if (GetBlockString(block, "preview_modelname") is { } previewKey)
        {
            name = GetStringKey(owner, previewKey);
            isNamedByClass = true;
            firstOption = 0;
        }
        else if (GetDeclaredModelName(declaration) is { } declared)
        {
            name = declared;
            isNamedByClass = true;
            firstOption = 1;
        }
        else
        {
            name = GetStringKey(owner, "model");
            isNamedByClass = false;
            firstOption = 0;
        }

        // A class can pick the model by the value of a key of the entity, and a value it does not list leaves it without one
        if (GetBlock(block, "model_from_key") is { } fromKey && GetBlockString(fromKey, "source_key") is { } sourceKey
            && GetBlock(fromKey, "value_to_vmdl") is { } models)
        {
            var value = GetStringKey(owner, sourceKey);

            name = value.Length > 0 && models.ContainsKey(value) ? models.GetStringProperty(value) : null;
        }

        var arguments = declaration.Arguments;

        int FindOption(string option)
        {
            for (var i = firstOption; i < arguments.Count; i++)
            {
                if (arguments[i].Equals(option, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        Color32 GetColorArgument(int index) => (index < arguments.Count ? ParseClampedColor(arguments[index]) : null) ?? Color32.White;

        // The word static is only looked for where neither of the other bounds is chosen
        var boundsMode = FindOption("fixedbounds") >= 0 ? ModelBoundsMode.Fixed
            : FindOption("nobounds") >= 0 ? ModelBoundsMode.None
            : ModelBoundsMode.Animated;

        // The model of a light is drawn in the color of the way it is lit. The first form gives one color for
        // baked and stationary lights and another for the rest, the second one color for each.
        (Color32, Color32, Color32)? lightModeTints = null;

        if (FindOption("lightModeTint") is >= 0 and var tint)
        {
            var lit = GetColorArgument(tint + 1);

            lightModeTints = (lit, lit, GetColorArgument(tint + 2));
        }

        if (FindOption("lightModeTint2") is >= 0 and var tint2)
        {
            lightModeTints = (GetColorArgument(tint2 + 1), GetColorArgument(tint2 + 2), GetColorArgument(tint2 + 3));
        }

        var defaultAnimation = isEditorModel && FindOption("defaultAnimation") is >= 0 and var animation && animation + 1 < arguments.Count
            ? arguments[animation + 1]
            : null;

        var previewClip = isEditorModelName && GetBlockString(block, "preview_clipname") is { } clipKey ? GetStringKey(owner, clipKey) : null;

        // The model of an entity is drawn the way the game draws it before it is told to play anything
        var defaultSequence = isEditorModel ? DefaultSequence.Search
            : owner.Scene.RendererContext.FileLoader.GetGameInfoBool("Hammer/UseFirstAnimationAsDefault", defaultValue: false) ? DefaultSequence.First
            : DefaultSequence.None;

        return new Settings
        {
            ModelName = string.IsNullOrEmpty(name) ? null : GetModelName(name),
            IsEditorModel = isEditorModel,
            IsNamedByClass = isNamedByClass,
            BoundsMode = boundsMode,

            // A model that stands in for the entity casts no shadow unless the class says it does
            CastsShadows = !isEditorModel || FindOption("castshadows") >= 0,
            IsStatic = (boundsMode == ModelBoundsMode.Animated && FindOption("static") >= 0) || GetBlockFlag(block, "static"),
            DefaultSequence = defaultSequence,
            DefaultAnimation = defaultAnimation,
            PreviewClip = previewClip,
            LightModeTints = lightModeTints,
        };
    }

    /// <summary>Loads a model, saying so where it does not exist.</summary>
    /// <param name="owner">The entity the model is drawn for.</param>
    /// <param name="name">The model, or <see langword="null"/> for none.</param>
    /// <returns>The model, or <see langword="null"/> when there is none or it does not load.</returns>
    private protected static Model? LoadModel(IHelperOwner owner, string? name)
    {
        if (name == null)
        {
            return null;
        }

        var rendererContext = owner.Scene.RendererContext;

        if (rendererContext.FileLoader.LoadFileCompiled(name)?.DataBlock is Model model)
        {
            return model;
        }

        rendererContext.Logger.LogWarning("Model \"{Model}\" of a '{Classname}' does not exist", name, owner.Classname);
        return null;
    }

    /// <summary>Gets where the model is placed, which is where the entity is.</summary>
    /// <returns>The transform of the model.</returns>
    private protected virtual Matrix4x4 GetModelTransform() => Owner.Transform;

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (model == null)
        {
            return;
        }

        var node = new ModelSceneNode(Owner.Scene, model, null, isWorldPreview: IsEditorModel)
        {
            Name = ModelName,
            Transform = GetModelTransform(),
            TintAlpha = GetTint(),
        };

        if (HiddenInGameObjectsOnly)
        {
            node.LayerName = HammerEntityVisuals.MarkerLayerName;

            // A stand in is as large in a 3D sky as in the map of the sky
            node.KeepsMarkerSize = true;
        }

        if (!settings.CastsShadows)
        {
            node.Flags |= ObjectTypeFlags.NoShadows;
        }

        fixedBounds = node.LocalBoundingBox;

        ApplyKeys(node);
        PlayAnimation(node);

        Owner.AddNode(node, FollowsEntity);
        ModelNode = node;
    }

    /// <inheritdoc/>
    public override void Link()
    {
        // The model is lit as if it stood at the entity the key names
        if (ModelNode is not { } node || GetStringKey("lightingorigin") is not { Length: > 0 } name || Owner.FindByName(name) is not [var target, ..])
        {
            return;
        }

        node.LightingOrigin = target.Transform.Translation;
        Owner.Scene.UpdateNodeEnvironmentMaps(node);
    }

    internal override void Render(ToolRenderContext context, in HelperRenderState state)
    {
        if (!state.IsSelected || BoundsMode == ModelBoundsMode.None || ModelNode is not { } node)
        {
            return;
        }

        var bounds = node.LocalBoundingBox;
        var transform = node.Transform;

        if (BoundsMode == ModelBoundsMode.Fixed)
        {
            var origin = transform.Translation;

            bounds = fixedBounds;
            transform = Owner.RigidTransform;
            transform.Translation = origin;
        }

        context.PushTransform(transform);
        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = BoundsColor;
        context.DrawWireBox(bounds.Min, bounds.Max);
        context.PopRenderMode();
        context.PopTransform();
    }

    // The color of a light picked by how it is lit, or else the color of the entity
    private Vector4 GetTint()
    {
        if (settings.LightModeTints is not { } tints)
        {
            return GetEntityColor();
        }

        var directLight = HasKey("directlight") ? GetIntegerKey("directlight") : DefaultDirectLight;
        var color = directLight switch
        {
            1 => tints.Baked,
            2 => tints.Dynamic,
            3 => tints.Stationary,
            _ => Color32.White,
        };

        return new Vector4(color.R, color.G, color.B, color.A) / byte.MaxValue;
    }

    /// <summary>Gets the color the entity is drawn in: its <c>rendercolor</c>, with its <c>renderamt</c> as alpha.</summary>
    /// <returns>The color, each channel from 0 to 1.</returns>
    private protected Vector4 GetEntityColor()
    {
        var color = HasKey("rendercolor") ? GetVector3Key("rendercolor") : new Vector3(byte.MaxValue);

        // The amount is a byte, so one meant as a fraction leaves the model all but transparent
        var alpha = HasKey("renderamt") ? unchecked((byte)GetIntegerKey("renderamt")) : byte.MaxValue;

        return new Vector4(color, alpha) / byte.MaxValue;
    }

    private bool HasKey(string key) => Owner.KeyValues.ContainsKey(key) || Owner.EntityClass?.FindKey(key) is { Default: not null };

    private void ApplyKeys(ModelSceneNode node)
    {
        if (GetStringKey("skin") is { Length: > 0 } skin)
        {
            SetSkin(node, skin);
        }

        var choices = node.GetBodyGroupChoices().ToList();

        if (ParseBlock(GetStringKey("bodygroups")) is { } bodyGroups)
        {
            SetBodyGroups(node, choices, bodyGroups);
        }

        if (settings.ForcedFirstBodyGroup is { } forced && choices.Count > 0)
        {
            SetBodyGroup(node, choices, choices[0].BodyGroup, forced);
        }

        // A model state gives the skin and the body groups at once
        if (ParseBlock(GetStringKey("model_state")) is { } modelState)
        {
            if (GetBlockString(modelState, "material_group") is { } materialGroup)
            {
                SetSkin(node, materialGroup);
            }

            if (GetBlock(modelState, "body_groups") is { } stateBodyGroups)
            {
                SetBodyGroups(node, choices, stateBodyGroups);
            }
        }

        if (GetStringKey("materialoverride") is { Length: > 0 } materialOverride)
        {
            var materialName = Path.ChangeExtension(materialOverride.Replace('\\', '/'), ".vmat");

            foreach (var mesh in node.AllRenderableMeshes)
            {
                foreach (var drawCall in mesh.DrawCalls.ToList())
                {
                    mesh.ReplaceMaterial(drawCall, materialName);
                }
            }
        }

        if (HasKey("lodlevel") && GetIntegerKey("lodlevel") is >= 0 and var level)
        {
            node.SetOverrideLod(level);
        }
    }

    // A skin is named, or numbered in the order of the material groups
    private static void SetSkin(ModelSceneNode node, string skin)
    {
        var groups = node.GetMaterialGroups().ToList();

        if (groups.Find(group => group.Equals(skin, StringComparison.OrdinalIgnoreCase)) is { } name)
        {
            node.SetMaterialGroup(name);
        }
        else if (int.TryParse(skin, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < groups.Count)
        {
            node.SetMaterialGroup(groups[index]);
        }
    }

    // Body groups by name, each with the number of the choice drawn of it
    private static void SetBodyGroups(ModelSceneNode node, List<BodyGroupChoice> choices, KVObject bodyGroups)
    {
        foreach (var (bodyGroup, value) in bodyGroups)
        {
            var choice = value.ValueType switch
            {
                KVValueType.String => int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0,
                KVValueType.Collection or KVValueType.Array or KVValueType.Null => 0,
                _ => value.ToInt32(CultureInfo.InvariantCulture),
            };

            SetBodyGroup(node, choices, bodyGroup, choice);
        }
    }

    private static void SetBodyGroup(ModelSceneNode node, List<BodyGroupChoice> choices, string bodyGroup, int index)
    {
        var match = choices.FindIndex(choice => choice.Index == index && choice.BodyGroup.Equals(bodyGroup, StringComparison.OrdinalIgnoreCase));

        if (match >= 0)
        {
            node.SetBodyGroupChoice(choices[match]);
        }
    }

    // A key that holds a block of keys writes it without the header of a file
    private static KVObject? ParseBlock(string text)
        => !string.IsNullOrWhiteSpace(text) && KVDocumentExtensions.TryParseKV3WithoutHeader(text, out var block, out _)
            && block.ValueType == KVValueType.Collection ? block : null;

    private void PlayAnimation(ModelSceneNode node)
    {
        if (settings.IsStatic)
        {
            return;
        }

        var name = IsEditorModel ? settings.DefaultAnimation : GetPreviewAnimation() ?? NonEmpty(GetStringKey("defaultanim"));
        var animation = (name == null ? null : FindAnimation(node, name)) ?? settings.DefaultSequence switch
        {
            DefaultSequence.Search => FindDefaultSequence(node),
            DefaultSequence.First => node.Animations.Values.OfType<SequenceAnimation>().FirstOrDefault(),
            _ => null,
        };

        if (settings.PreviewClip is { Length: > 0 } clip && FindAnimation(node, clip) is { } previewClip)
        {
            animation = previewClip;
        }

        if (animation != null)
        {
            node.SetAnimation(animation);
        }
    }

    // Of the keys of the class that name the animation shown, the set one that matters most
    private string? GetPreviewAnimation()
    {
        if (Owner.EntityClass is not { } entityClass)
        {
            return null;
        }

        string? animation = null;
        var highestPriority = -1;

        foreach (var key in entityClass.Keys)
        {
            if (key.Metadata is not { } metadata || !GetBlockFlag(metadata, PreviewAnimationKey) || GetStringKey(key.Name) is not { Length: > 0 } value)
            {
                continue;
            }

            var priority = metadata.ContainsKey(PreviewAnimationPriorityKey) ? metadata.GetInt32Property(PreviewAnimationPriorityKey) : 0;

            if (priority > highestPriority)
            {
                animation = value;
                highestPriority = priority;
            }
        }

        return animation;
    }

    // An animation by its name, or else the first sequence with an activity of that name
    private static AnimationBase? FindAnimation(ModelSceneNode node, string name)
        => node.Animations.GetValueOrDefault(name)
            ?? node.Animations.Values.OfType<SequenceAnimation>().FirstOrDefault(sequence
                => Array.Exists(sequence.Activities, activity => activity.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    // A reference pose or tools preview first, then an idle activity, a sequence named idle, one with idle in
    // its name, and the first sequence where there is none of those. Hidden sequences are passed over.
    private static SequenceAnimation? FindDefaultSequence(ModelSceneNode node)
    {
        var sequences = node.Animations.Values.OfType<SequenceAnimation>().ToList();
        var visible = sequences.FindAll(static sequence => !sequence.Hidden);

        static bool HasFirstActivity(SequenceAnimation sequence, string activity)
            => sequence.Activities is [var first, ..] && first.Name.Equals(activity, StringComparison.OrdinalIgnoreCase);

        return visible.Find(static sequence => HasFirstActivity(sequence, "ACT_NEUTRAL_REF_POSE") || sequence.Name.Equals("tools_preview", StringComparison.OrdinalIgnoreCase))
            ?? visible.Find(static sequence => HasFirstActivity(sequence, "ACT_IDLE"))
            ?? visible.FindLast(static sequence => sequence.Name.Equals("idle", StringComparison.OrdinalIgnoreCase))
            ?? visible.Find(static sequence => sequence.Name.Contains("idle", StringComparison.OrdinalIgnoreCase))
            ?? sequences.FirstOrDefault();
    }

    private static string? NonEmpty(string value) => value.Length > 0 ? value : null;

    // Three or four numbers, each clamped to a byte, the alpha full where it is left out
    private static Color32? ParseClampedColor(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length is < 3 or > 4)
        {
            return null;
        }

        Span<byte> channels = [byte.MaxValue, byte.MaxValue, byte.MaxValue, byte.MaxValue];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel))
            {
                return null;
            }

            channels[i] = (byte)Math.Clamp(channel, 0, byte.MaxValue);
        }

        return new Color32(channels[0], channels[1], channels[2], channels[3]);
    }
}
