namespace ValveResourceFormat.IO.Fgd;

/// <summary>
/// The type of a variable of an entity class, by the number the editor knows it under.
/// </summary>
public enum FgdVariableType
{
    /// <summary>Not a type the editor knows.</summary>
    Invalid = -1,

    /// <summary><c>angle</c></summary>
    Angle = 0,

    /// <summary><c>target_destination</c></summary>
    TargetDestination = 1,

    /// <summary><c>target_name_or_class</c></summary>
    TargetNameOrClass = 2,

    /// <summary><c>target_source</c></summary>
    TargetSource = 3,

    /// <summary><c>integer</c></summary>
    Integer = 4,

    /// <summary><c>string</c></summary>
    String = 5,

    /// <summary><c>string_instanced</c></summary>
    StringInstanced = 6,

    /// <summary><c>choices</c></summary>
    Choices = 7,

    /// <summary><c>intchoices</c></summary>
    IntChoices = 8,

    /// <summary><c>floatchoices</c></summary>
    FloatChoices = 9,

    /// <summary><c>flags</c></summary>
    Flags = 10,

    /// <summary><c>color255</c></summary>
    Color255 = 11,

    /// <summary><c>color255alpha</c></summary>
    Color255Alpha = 12,

    /// <summary><c>sound</c></summary>
    Sound = 13,

    /// <summary><c>vector</c></summary>
    Vector = 14,

    /// <summary><c>transform</c></summary>
    Transform = 15,

    /// <summary><c>npcclass</c></summary>
    NpcClass = 16,

    /// <summary><c>filterclass</c></summary>
    FilterClass = 17,

    /// <summary><c>float</c></summary>
    Float = 18,

    /// <summary><c>side</c></summary>
    Side = 19,

    /// <summary><c>sidelist</c></summary>
    SideList = 20,

    /// <summary><c>vecline</c></summary>
    VecLine = 21,

    /// <summary><c>axis</c></summary>
    Axis = 22,

    /// <summary><c>pointentityclass</c></summary>
    PointEntityClass = 23,

    /// <summary><c>node_dest</c></summary>
    NodeDest = 24,

    /// <summary><c>script</c></summary>
    Script = 25,

    /// <summary><c>scriptlist</c></summary>
    ScriptList = 26,

    /// <summary><c>instance_file</c></summary>
    InstanceFile = 27,

    /// <summary><c>instance_variable</c></summary>
    InstanceVariable = 28,

    /// <summary><c>instance_parm</c></summary>
    InstanceParm = 29,

    /// <summary><c>boolean</c></summary>
    Boolean = 30,

    /// <summary><c>node_id</c></summary>
    NodeId = 31,

    /// <summary><c>resource</c></summary>
    Resource = 32,

    /// <summary><c>resource_name</c></summary>
    ResourceName = 33,

    /// <summary><c>tag_list</c></summary>
    TagList = 34,

    /// <summary><c>tag_list_dynamic</c></summary>
    TagListDynamic = 35,

    /// <summary><c>world_point</c></summary>
    WorldPoint = 36,

    /// <summary><c>world_angle</c></summary>
    WorldAngle = 37,

    /// <summary><c>world_transform</c></summary>
    WorldTransform = 38,

    /// <summary><c>local_point</c></summary>
    LocalPoint = 39,

    /// <summary><c>localaxis</c></summary>
    LocalAxis = 40,

    /// <summary><c>node_id_List</c></summary>
    NodeIdList = 41,

    /// <summary><c>gameunitclass</c></summary>
    GameUnitClass = 42,

    /// <summary><c>gameitemclass</c></summary>
    GameItemClass = 43,

    /// <summary><c>sequence</c></summary>
    Sequence = 44,

    /// <summary><c>bodygroupchoices</c></summary>
    BodyGroupChoices = 45,

    /// <summary><c>modelstatechoices</c></summary>
    ModelStateChoices = 46,

    /// <summary><c>lod_level</c></summary>
    LodLevel = 47,

    /// <summary><c>text_block</c></summary>
    TextBlock = 48,

    /// <summary><c>parentAttachment</c></summary>
    ParentAttachment = 49,

    /// <summary><c>materialgroup</c></summary>
    MaterialGroup = 50,

    /// <summary><c>collision_property</c></summary>
    CollisionProperty = 51,

    /// <summary><c>surface_properties</c></summary>
    SurfaceProperties = 52,

    /// <summary><c>vdata_choice</c></summary>
    VdataChoice = 53,

    /// <summary><c>subclass_choice</c></summary>
    SubclassChoice = 54,

    /// <summary><c>model_bone</c></summary>
    ModelBone = 55,

    /// <summary><c>model_attachment</c></summary>
    ModelAttachment = 56,

    /// <summary><c>propdataname</c></summary>
    PropDataName = 57,

    /// <summary><c>model_bodygroup</c></summary>
    ModelBodyGroup = 58,

    /// <summary><c>model_breakpiece</c></summary>
    ModelBreakPiece = 59,

    /// <summary><c>model_morphchannel</c></summary>
    ModelMorphChannel = 60,

    /// <summary><c>model_cloth_effect</c></summary>
    ModelClothEffect = 61,

    /// <summary><c>model_cloth_vertex_map</c></summary>
    ModelClothVertexMap = 62,

    /// <summary><c>model_cloth_node</c></summary>
    ModelClothNode = 63,

    /// <summary><c>model_runtime_cloth_node</c></summary>
    ModelRuntimeClothNode = 64,

    /// <summary><c>model_physics_shape</c></summary>
    ModelPhysicsShape = 65,

    /// <summary><c>remove_key</c></summary>
    RemoveKey = 66,

    /// <summary><c>curve</c></summary>
    Curve = 67,

    /// <summary><c>kv3</c></summary>
    KV3 = 68,

    /// <summary><c>array</c></summary>
    Array = 69,

    /// <summary><c>struct</c></summary>
    Struct = 70,

    /// <summary><c>particle_cfg</c></summary>
    ParticleCfg = 71,

    /// <summary><c>suggestions</c></summary>
    Suggestions = 72,

    /// <summary><c>flagchoices</c></summary>
    FlagChoices = 73,

    /// <summary><c>stringmap</c></summary>
    StringMap = 74,

    /// <summary><c>intmap</c></summary>
    IntMap = 75,

    /// <summary><c>polymorphic_class</c></summary>
    PolymorphicClass = 76,

    /// <summary><c>vector2d</c></summary>
    Vector2D = 77,

    /// <summary><c>vector4d</c></summary>
    Vector4D = 78,

    /// <summary><c>panorama_image</c></summary>
    PanoramaImage = 79,

    /// <summary><c>panorama_layout</c></summary>
    PanoramaLayout = 80,

    /// <summary><c>panorama_style</c></summary>
    PanoramaStyle = 81,

    /// <summary><c>panorama_video</c></summary>
    PanoramaVideo = 82,

    /// <summary><c>animgraph_enum</c></summary>
    AnimGraphEnum = 83,

    /// <summary><c>animgraph</c></summary>
    AnimGraph = 84,

    /// <summary><c>npc_ability_name</c></summary>
    NpcAbilityName = 85,

    /// <summary><c>animgraph2_identifier</c></summary>
    AnimGraph2Identifier = 86,

    /// <summary><c>localization_required</c></summary>
    LocalizationRequired = 87,

    /// <summary><c>animgraph2_clip</c></summary>
    AnimGraph2Clip = 88,

    /// <summary><c>animgraph2_graph</c></summary>
    AnimGraph2Graph = 89,

    /// <summary><c>choice_group</c></summary>
    ChoiceGroup = 90,

    /// <summary><c>resource_choices</c></summary>
    ResourceChoices = 91,

    /// <summary><c>scene</c></summary>
    Scene = 92,

    /// <summary><c>origin</c></summary>
    Origin = 93,

    /// <summary><c>angle_negative_pitch</c></summary>
    AngleNegativePitch = 94,

    /// <summary><c>color1</c></summary>
    Color1 = 95,
}

/// <summary>
/// How the value of a variable is held.
/// </summary>
public enum FgdStorage
{
    /// <summary>A type made of other types, which has no value of its own.</summary>
    Composite = -2,

    /// <summary>A whole number.</summary>
    Integer = 1,

    /// <summary>Text.</summary>
    String = 2,
}

/// <summary>
/// A type name a variable can be declared with.
/// </summary>
/// <param name="Name">The name, matched without regard to case.</param>
/// <param name="Type">The type it names.</param>
/// <param name="Storage">How a value of the type is held.</param>
/// <param name="TakesSubType">Whether the name is followed by <c>:</c> and text that narrows the type down.</param>
/// <param name="TakesElementType">Whether the name is followed by <c>:</c> and the type of what it is made of.</param>
public readonly record struct FgdTypeName(string Name, FgdVariableType Type, FgdStorage Storage, bool TakesSubType, bool TakesElementType)
{
    /// <summary>Gets every type name, in the order they are looked up.</summary>
    public static IReadOnlyList<FgdTypeName> All { get; } =
    [
        new("angle", FgdVariableType.Angle, FgdStorage.String, false, false),
        new("choices", FgdVariableType.Choices, FgdStorage.String, false, false),
        new("intchoices", FgdVariableType.IntChoices, FgdStorage.Integer, false, false),
        new("floatchoices", FgdVariableType.FloatChoices, FgdStorage.String, false, false),
        new("suggestions", FgdVariableType.Suggestions, FgdStorage.String, false, false),
        new("flagchoices", FgdVariableType.FlagChoices, FgdStorage.String, false, false),
        new("color255", FgdVariableType.Color255, FgdStorage.String, false, false),
        new("color255alpha", FgdVariableType.Color255Alpha, FgdStorage.String, false, false),
        new("flags", FgdVariableType.Flags, FgdStorage.Integer, false, false),
        new("integer", FgdVariableType.Integer, FgdStorage.Integer, false, false),
        new("int", FgdVariableType.Integer, FgdStorage.Integer, false, false),
        new("sound", FgdVariableType.Sound, FgdStorage.String, false, false),
        new("string", FgdVariableType.String, FgdStorage.String, false, false),
        new("string_instanced", FgdVariableType.StringInstanced, FgdStorage.String, false, false),
        new("text_block", FgdVariableType.TextBlock, FgdStorage.String, false, false),
        new("target_destination", FgdVariableType.TargetDestination, FgdStorage.String, false, false),
        new("target_source", FgdVariableType.TargetSource, FgdStorage.String, false, false),
        new("target_name_or_class", FgdVariableType.TargetNameOrClass, FgdStorage.String, false, false),
        new("vector2d", FgdVariableType.Vector2D, FgdStorage.String, false, false),
        new("vector", FgdVariableType.Vector, FgdStorage.String, false, false),
        new("vector4d", FgdVariableType.Vector4D, FgdStorage.String, false, false),
        new("transform", FgdVariableType.Transform, FgdStorage.String, false, false),
        new("npcclass", FgdVariableType.NpcClass, FgdStorage.String, false, false),
        new("filterclass", FgdVariableType.FilterClass, FgdStorage.String, false, false),
        new("float", FgdVariableType.Float, FgdStorage.String, false, false),
        new("side", FgdVariableType.Side, FgdStorage.String, false, false),
        new("sidelist", FgdVariableType.SideList, FgdStorage.String, false, false),
        new("axis", FgdVariableType.Axis, FgdStorage.String, false, false),
        new("vecline", FgdVariableType.VecLine, FgdStorage.String, false, false),
        new("pointentityclass", FgdVariableType.PointEntityClass, FgdStorage.String, false, false),
        new("node_dest", FgdVariableType.NodeDest, FgdStorage.Integer, false, false),
        new("script", FgdVariableType.Script, FgdStorage.String, false, false),
        new("scriptlist", FgdVariableType.ScriptList, FgdStorage.String, false, false),
        new("instance_file", FgdVariableType.InstanceFile, FgdStorage.String, false, false),
        new("instance_variable", FgdVariableType.InstanceVariable, FgdStorage.String, false, false),
        new("instance_parm", FgdVariableType.InstanceParm, FgdStorage.String, false, false),
        new("boolean", FgdVariableType.Boolean, FgdStorage.String, false, false),
        new("bool", FgdVariableType.Boolean, FgdStorage.String, false, false),
        new("node_id", FgdVariableType.NodeId, FgdStorage.Integer, false, false),
        new("resource", FgdVariableType.Resource, FgdStorage.String, true, false),
        new("resource_name", FgdVariableType.ResourceName, FgdStorage.String, true, false),
        new("kv3", FgdVariableType.KV3, FgdStorage.String, false, false),
        new("tag_list", FgdVariableType.TagList, FgdStorage.String, false, false),
        new("tag_list_dynamic", FgdVariableType.TagListDynamic, FgdStorage.String, false, false),
        new("world_point", FgdVariableType.WorldPoint, FgdStorage.String, false, false),
        new("world_angle", FgdVariableType.WorldAngle, FgdStorage.String, false, false),
        new("world_transform", FgdVariableType.WorldTransform, FgdStorage.String, false, false),
        new("local_point", FgdVariableType.LocalPoint, FgdStorage.String, false, false),
        new("localaxis", FgdVariableType.LocalAxis, FgdStorage.String, false, false),
        new("node_id_List", FgdVariableType.NodeIdList, FgdStorage.String, false, false),
        new("gameunitclass", FgdVariableType.GameUnitClass, FgdStorage.String, false, false),
        new("gameitemclass", FgdVariableType.GameItemClass, FgdStorage.String, false, false),
        new("sequence", FgdVariableType.Sequence, FgdStorage.String, false, false),
        new("bodygroupchoices", FgdVariableType.BodyGroupChoices, FgdStorage.String, false, false),
        new("modelstatechoices", FgdVariableType.ModelStateChoices, FgdStorage.String, false, false),
        new("lod_level", FgdVariableType.LodLevel, FgdStorage.Integer, false, false),
        new("parentAttachment", FgdVariableType.ParentAttachment, FgdStorage.String, false, false),
        new("materialgroup", FgdVariableType.MaterialGroup, FgdStorage.String, false, false),
        new("model_bone", FgdVariableType.ModelBone, FgdStorage.String, false, false),
        new("model_attachment", FgdVariableType.ModelAttachment, FgdStorage.String, false, false),
        new("propdataname", FgdVariableType.PropDataName, FgdStorage.String, false, false),
        new("model_bodygroup", FgdVariableType.ModelBodyGroup, FgdStorage.String, false, false),
        new("model_breakpiece", FgdVariableType.ModelBreakPiece, FgdStorage.String, false, false),
        new("model_morphchannel", FgdVariableType.ModelMorphChannel, FgdStorage.String, false, false),
        new("remove_key", FgdVariableType.RemoveKey, FgdStorage.String, false, false),
        new("curve", FgdVariableType.Curve, FgdStorage.String, false, false),
        new("collision_property", FgdVariableType.CollisionProperty, FgdStorage.String, false, false),
        new("surface_properties", FgdVariableType.SurfaceProperties, FgdStorage.String, false, false),
        new("array", FgdVariableType.Array, FgdStorage.Composite, false, true),
        new("struct", FgdVariableType.Struct, FgdStorage.Composite, true, false),
        new("model_cloth_effect", FgdVariableType.ModelClothEffect, FgdStorage.String, false, false),
        new("model_cloth_vertex_map", FgdVariableType.ModelClothVertexMap, FgdStorage.String, false, false),
        new("model_cloth_node", FgdVariableType.ModelClothNode, FgdStorage.String, false, false),
        new("model_runtime_cloth_node", FgdVariableType.ModelRuntimeClothNode, FgdStorage.String, false, false),
        new("model_physics_shape", FgdVariableType.ModelPhysicsShape, FgdStorage.String, false, false),
        new("vdata_choice", FgdVariableType.VdataChoice, FgdStorage.String, true, false),
        new("subclass_choice", FgdVariableType.SubclassChoice, FgdStorage.String, true, false),
        new("particle_cfg", FgdVariableType.ParticleCfg, FgdStorage.String, false, false),
        new("stringmap", FgdVariableType.StringMap, FgdStorage.String, false, true),
        new("intmap", FgdVariableType.IntMap, FgdStorage.String, false, true),
        new("polymorphic_class", FgdVariableType.PolymorphicClass, FgdStorage.String, true, false),
        new("panorama_image", FgdVariableType.PanoramaImage, FgdStorage.String, false, false),
        new("panorama_layout", FgdVariableType.PanoramaLayout, FgdStorage.String, false, false),
        new("panorama_style", FgdVariableType.PanoramaStyle, FgdStorage.String, false, false),
        new("panorama_video", FgdVariableType.PanoramaVideo, FgdStorage.String, false, false),
        new("animgraph_enum", FgdVariableType.AnimGraphEnum, FgdStorage.String, false, false),
        new("animgraph", FgdVariableType.AnimGraph, FgdStorage.String, false, false),
        new("npc_ability_name", FgdVariableType.NpcAbilityName, FgdStorage.String, false, false),
        new("animgraph2_identifier", FgdVariableType.AnimGraph2Identifier, FgdStorage.String, false, false),
        new("localization_required", FgdVariableType.LocalizationRequired, FgdStorage.String, false, false),
        new("animgraph2_clip", FgdVariableType.AnimGraph2Clip, FgdStorage.String, false, false),
        new("animgraph2_graph", FgdVariableType.AnimGraph2Graph, FgdStorage.String, false, false),
        new("choice_group", FgdVariableType.ChoiceGroup, FgdStorage.String, false, false),
        new("resource_choices", FgdVariableType.ResourceChoices, FgdStorage.String, true, false),
        new("scene", FgdVariableType.Scene, FgdStorage.String, false, false),
        new("origin", FgdVariableType.Origin, FgdStorage.String, false, false),
        new("angle_negative_pitch", FgdVariableType.AngleNegativePitch, FgdStorage.String, false, false),
        new("color1", FgdVariableType.Color1, FgdStorage.String, false, false),
    ];
}
