using System.Globalization;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// One of the things an entity is drawn with in the editor. An entity gets a helper for each one its class
/// declares, and everything seen of it comes from them: its model, its icon, the light it casts.
/// </summary>
public abstract class EntityHelper
{
    /// <summary>Gets the entity the helper belongs to.</summary>
    public IHelperOwner Owner { get; }

    /// <summary>Gets the helper as the class of the entity declares it.</summary>
    public HelperDeclaration Declaration { get; }

    /// <summary>
    /// Gets whether the entity is seen by this helper, which most are. An entity none of whose helpers is, is
    /// drawn as a box.
    /// </summary>
    public virtual bool IsVisual => true;

    /// <summary>Gets whether the helper is only drawn while helpers are shown, which they are by default.</summary>
    public virtual bool NeedsShowHelpers => false;

    /// <summary>
    /// Gets whether the helper is hidden while only what the game shows is, unless its entity is selected.
    /// </summary>
    public virtual bool HiddenInGameObjectsOnly => false;

    /// <summary>Gets whether nothing of the helper is drawn while its entity is not selected.</summary>
    public virtual bool RequiresSelection => false;

    /// <summary>
    /// Gets whether the helper stands in for its entity in the editor, rather than drawing what the game shows of
    /// it. The game draws an entity of a compiled map, so of its helpers these are what is drawn in its place.
    /// </summary>
    public virtual bool IsStandIn => false;

    /// <summary>
    /// Gets whether the helper only draws shapes of the editor, which the game does not show. An entity of a
    /// compiled map is drawn with these too.
    /// </summary>
    public virtual bool IsToolShape => false;

    /// <summary>
    /// Gets whether the helper of a path is drawn as it is while the path is selected also while any node of the
    /// path is.
    /// </summary>
    internal virtual bool IsSelectedWithPathNodes => false;

    /// <summary>The color selected shapes are drawn in, which is a setting of the editor; this is its default.</summary>
    internal static readonly Color32 SelectionWireframeColor = new(80, 200, 255);

    /// <summary>The color of what is selected itself, which is a setting of the editor; this is its default.</summary>
    internal static readonly Color32 SelectionColor = new(255, 255, 0);

    /// <summary>The tint over what is selected, which is a setting of the editor; this is its default.</summary>
    internal static readonly Color32 SelectionOverlayColor = new(255, 183, 45);

    /// <summary>The color helpers draw their shapes in while their entity is not selected.</summary>
    internal static readonly Color32 UnselectedEntityColor = new(220, 220, 220);

    private readonly List<ToolSceneNode> shapeNodes = [];

    /// <summary>
    /// Gets the bounds the helper adds to those of its entity in the world, which a selection is boxed by, or
    /// <see langword="null"/> where it adds none. Most helpers add none: a sphere or a line around an entity
    /// does not make the entity any larger. An entity none of whose helpers adds any is boxed at its origin.
    /// </summary>
    public virtual AABB? ObjectBounds => null;

    /// <summary>Gets <see cref="ObjectBounds"/> boxed in another space, where the helper knows them more tightly.</summary>
    /// <param name="worldToSpace">The transform from the world into the space.</param>
    public virtual AABB? GetObjectBounds(in Matrix4x4 worldToSpace) => ObjectBounds?.Transform(worldToSpace);

    /// <summary>
    /// Gets which ways a gizmo may move the entity, which every helper of the entity has to allow. The helpers
    /// Hammer does not let scale only allow moving and turning.
    /// </summary>
    public virtual GizmoTransformModes SupportedTransformModes => GizmoTransformModes.All;

    /// <summary>
    /// Gets whether the helper is drawn for its entity at all. Of the entities of a 3D sky placed into the map,
    /// only the helpers that show what the game does are, never the ones the editor's options hide.
    /// </summary>
    public bool IsAvailable => !Owner.IsInSkybox || !(NeedsShowHelpers || HiddenInGameObjectsOnly);

    /// <summary>
    /// Gets the visibility layer of what the helper adds to the scene, which stands in for the options of the
    /// editor: the helpers layer for a helper only drawn while helpers are shown, the editor-only entities layer
    /// for one hidden while only what the game shows is, and <see langword="null"/>, the layer of the entity,
    /// for one neither option hides.
    /// </summary>
    protected string? VisibilityLayerName => NeedsShowHelpers
        ? HammerEntityVisuals.HelpersLayerName
        : HiddenInGameObjectsOnly ? HammerEntityVisuals.MarkerLayerName : null;

    /// <summary>Initializes a helper of an entity.</summary>
    /// <param name="owner">The entity the helper belongs to.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    protected EntityHelper(IHelperOwner owner, HelperDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(declaration);

        Owner = owner;
        Declaration = declaration;
    }

    /// <summary>Adds what the helper draws to the scene of the entity.</summary>
    public virtual void AddToScene()
    {
    }

    /// <summary>
    /// Draws the shapes of the helper the way the editor draws them, in the world, while its entity is or is not
    /// selected.
    /// </summary>
    /// <param name="context">What the shapes are drawn with.</param>
    /// <param name="state">Whether the entity is selected.</param>
    internal virtual void Render(ToolRenderContext context, in HelperRenderState state)
    {
    }

    /// <summary>
    /// Adds the shapes the helper draws while its entity is not selected to the scene, as part of it. They are
    /// hidden while the entity is selected, when the selection draws them as they are then.
    /// </summary>
    /// <param name="placement">
    /// Where the shapes are placed, for shapes that keep their size as markers do, or <see langword="null"/> to
    /// draw them in the world.
    /// </param>
    /// <param name="layerName">
    /// The visibility layer of the shapes, or <see langword="null"/> for the one the options that hide the
    /// helper give.
    /// </param>
    private protected void AddShapes(Matrix4x4? placement = null, string? layerName = null)
    {
        if (RequiresSelection || !IsAvailable)
        {
            return;
        }

        var transform = placement ?? Matrix4x4.Identity;
        Matrix4x4.Invert(transform, out var worldToPlacement);

        var node = new ToolSceneNode(Owner.Scene)
        {
            LayerName = layerName ?? VisibilityLayerName,
            KeepsMarkerSize = placement != null,
        };

        node.Draw(context =>
        {
            context.PushAbsoluteTransform(worldToPlacement);
            Render(context, HelperRenderState.Unselected);
            context.PopTransform();
        });

        if (node.Geometry.IsEmpty)
        {
            node.Delete();
            return;
        }

        node.Transform = transform;
        shapeNodes.Add(node);
        Owner.AddNode(node);
    }

    /// <summary>Hides the shapes the helper draws while its entity is not selected, while it is.</summary>
    internal void SetSelected(bool isSelected)
    {
        foreach (var node in shapeNodes)
        {
            node.IsHiddenWhileSelected = isSelected;
        }

        OnSelectionChanged(isSelected);
    }

    /// <summary>Called when the entity of the helper is selected or deselected.</summary>
    /// <param name="isSelected">Whether the entity is now selected.</param>
    private protected virtual void OnSelectionChanged(bool isSelected)
    {
    }

    /// <summary>
    /// Gets the color the editor draws the shapes of the entity in: the selection wireframe color while it is
    /// selected, a light grey otherwise.
    /// </summary>
    private protected static Color32 GetDrawColor(in HelperRenderState state) => state.IsSelected ? SelectionWireframeColor : UnselectedEntityColor;

    /// <summary>Draws a box as lit faces in a color, or tinted while its entity is selected and outlined then too.</summary>
    /// <param name="context">What the box is drawn with.</param>
    /// <param name="mins">The smallest corner.</param>
    /// <param name="maxs">The largest corner.</param>
    /// <param name="color">The color of the faces while the entity is not selected.</param>
    /// <param name="state">Whether the entity is selected.</param>
    private protected static void DrawLitBox(ToolRenderContext context, Vector3 mins, Vector3 maxs, Color32 color, in HelperRenderState state)
    {
        if (state.IsSelected)
        {
            color = SelectionOverlayColor;
        }

        context.PushRenderMode(ToolRenderMode.Flat);
        context.DrawBoxFaces(mins, maxs, color);
        context.PopRenderMode();

        if (!state.IsSelected)
        {
            return;
        }

        // The faces leave the draw color shaded
        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = color;
        context.DrawBoxFaceOutlines(mins, maxs);
        context.PopRenderMode();
    }

    /// <summary>Draws a line two pixels wide, dashed over the scene where the scene hides it.</summary>
    /// <param name="context">What the line is drawn with.</param>
    /// <param name="start">Where the line starts.</param>
    /// <param name="end">Where the line ends.</param>
    /// <param name="color">The color of the line.</param>
    private protected static void DrawLineOverHidden(ToolRenderContext context, Vector3 start, Vector3 end, Color32 color)
    {
        context.LineWidth = 2f;

        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawLine(start, end, color);
        context.PopRenderMode();

        context.PushRenderMode(ToolRenderMode.DottedNoZ);
        context.DrawLine(start, end, color);
        context.PopRenderMode();

        context.LineWidth = 0f;
    }

    /// <summary>Gets a color the block the helper is declared with gives as three or four numbers.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The color, or <see langword="null"/> when the block has no such color.</returns>
    protected Color32? GetMetadataColor(string key)
        => Declaration.Metadata is { } block && block.ContainsKey(key) ? ReadColorArray(block[key]) : null;

    /// <summary>
    /// Gets a color key of the entity as three or four numbers, opaque where there are three. A value that is not
    /// a color is transparent black.
    /// </summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The color where the entity has no such key.</param>
    /// <returns>The color.</returns>
    private protected Color32 GetColorKey(string key, Color32 defaultValue)
    {
        if (!Owner.KeyValues.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        if (value.IsArray)
        {
            return ReadColorArray(value) ?? default;
        }

        return Owner.KeyValues.GetStringProperty(key) is { } text ? ParseColor(text) : default;
    }

    /// <summary>Parses a color as three or four numbers, opaque where there are three, and transparent black where it is not a color.</summary>
    /// <param name="text">The color as text.</param>
    /// <returns>The color.</returns>
    private protected static Color32 ParseColor(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Span<byte> channels = [0, 0, 0, byte.MaxValue];

        if (parts.Length < 3)
        {
            return default;
        }

        for (var i = 0; i < Math.Min(parts.Length, channels.Length); i++)
        {
            if (!byte.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out channels[i]))
            {
                return default;
            }
        }

        return new Color32(channels[0], channels[1], channels[2], channels[3]);
    }

    // Three or four numbers, opaque where the fourth is left out
    private static Color32? ReadColorArray(KVObject? value)
    {
        if (value is not { IsArray: true, Count: >= 3 })
        {
            return null;
        }

        byte GetChannel(int index) => index < value.Count ? value[index].ToByte(CultureInfo.InvariantCulture) : byte.MaxValue;

        return new Color32(GetChannel(0), GetChannel(1), GetChannel(2), GetChannel(3));
    }

    /// <summary>Gets a flag of the block the helper is declared with.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the block has no such key.</param>
    /// <returns>The flag.</returns>
    protected bool GetMetadataFlag(string key, bool defaultValue) => GetBlockFlag(Declaration.Metadata, key, defaultValue);

    /// <summary>Gets whether any argument of the declaration is a flag, without regard to case.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>Whether the declaration has it.</returns>
    protected bool HasArgument(string flag)
        => Declaration.Arguments.Contains(flag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the color three arguments of the declaration give, each channel on its own: full where its
    /// argument is left out, and none where it is not a number.
    /// </summary>
    /// <param name="start">Index of the first of the three.</param>
    /// <returns>The color, opaque.</returns>
    protected Color32 GetColorArguments(int start)
    {
        byte GetChannel(int index) => GetArgument(index) is not { } argument
            ? byte.MaxValue
            : byte.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var channel) ? channel : (byte)0;

        return new Color32(GetChannel(start), GetChannel(start + 1), GetChannel(start + 2));
    }

    /// <summary>
    /// Finds the entities the helper names. Called once every entity of the map exists, and again whenever
    /// one of them changes.
    /// </summary>
    public virtual void Link()
    {
    }

    /// <summary>Gets a key of the entity as text.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, empty when the entity has no such key.</returns>
    protected string GetStringKey(string key) => GetStringKey(Owner, key);

    /// <summary>Gets a key of an entity as text.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, empty when the entity has no such key.</returns>
    private protected static string GetStringKey(IHelperOwner owner, string key)
        => owner.KeyValues.GetStringProperty(key) ?? string.Empty;

    /// <summary>Gets a key of the entity as a number.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The value.</returns>
    protected float GetFloatKey(string key, float defaultValue = 0f)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetFloatProperty(key) : defaultValue;

    /// <summary>Gets a key of the entity as a flag.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key, or where it is neither a number nor a boolean.</param>
    /// <returns>Whether the value is a number other than zero, or true.</returns>
    protected bool GetBooleanKey(string key, bool defaultValue)
    {
        if (!Owner.KeyValues.TryGetValue(key, out var stored))
        {
            return defaultValue;
        }

        // A compiled map keeps flags as booleans rather than as text
        if (stored.ValueType == KVValueType.Boolean)
        {
            return (bool)stored;
        }

        var value = GetStringKey(key);

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number != 0f;
        }

        return bool.TryParse(value, out var flag) ? flag : defaultValue;
    }

    /// <summary>Gets a key of the entity as an integer.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The value.</returns>
    protected int GetIntegerKey(string key, int defaultValue = 0)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetInt32Property(key) : defaultValue;

    /// <summary>Gets a key of the entity as a vector.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, zero when the entity has no such key.</returns>
    protected Vector2 GetVector2Key(string key) => Owner.KeyValues.GetVector2Property(key);

    /// <summary>Gets a key of the entity as a vector.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The value.</returns>
    protected Vector3 GetVector3Key(string key, Vector3 defaultValue = default)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetVector3Property(key) : defaultValue;

    /// <summary>Gets a key of the block the helper is declared with as text.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, or <see langword="null"/> when the helper has no such key.</returns>
    protected string? GetMetadata(string key)
        => Declaration.Metadata is { } block && block.ContainsKey(key) ? block.GetStringProperty(key) : null;

    /// <summary>Gets a block of keys a block holds under a key.</summary>
    /// <param name="block">The block to look in, or <see langword="null"/> for none.</param>
    /// <param name="key">Name of the key.</param>
    /// <returns>The block, or <see langword="null"/> when there is no block under the key.</returns>
    private protected static KVObject? GetBlock(KVObject? block, string key)
        => block != null && block.ContainsKey(key) && block[key] is { ValueType: KVValueType.Collection } value ? value : null;

    /// <summary>Gets a key of a block as text.</summary>
    /// <param name="block">The block to look in, or <see langword="null"/> for none.</param>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, or <see langword="null"/> when the block has no such key or it is empty.</returns>
    private protected static string? GetBlockString(KVObject? block, string key)
        => block != null && block.ContainsKey(key) && block.GetStringProperty(key) is { Length: > 0 } value ? value : null;

    /// <summary>Gets a flag of a block.</summary>
    /// <param name="block">The block to look in, or <see langword="null"/> for none.</param>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the block has no such key.</param>
    /// <returns>The flag.</returns>
    private protected static bool GetBlockFlag(KVObject? block, string key, bool defaultValue = false)
        => block != null && block.ContainsKey(key) ? block.GetBooleanProperty(key) : defaultValue;

    /// <summary>Gets an argument of the declaration, or <see langword="null"/> when it has fewer.</summary>
    /// <param name="index">Zero based index of the argument.</param>
    /// <returns>The argument.</returns>
    protected string? GetArgument(int index)
        => index < Declaration.Arguments.Count ? Declaration.Arguments[index] : null;

    /// <summary>Gets the key of the entity an argument of a declaration names, of which the editor keeps no more than 31 characters.</summary>
    /// <param name="argument">The argument.</param>
    /// <returns>The name of the key.</returns>
    private protected static string ToTruncatedKey(string argument) => argument[..Math.Min(argument.Length, 31)].ToLowerInvariant();
}

/// <summary>Whether the entity a helper draws for is selected, and whether it is all that is.</summary>
/// <param name="IsSelected">Whether the entity is selected.</param>
/// <param name="IsOnlySelected">Whether the entity is the only thing selected.</param>
internal readonly record struct HelperRenderState(bool IsSelected, bool IsOnlySelected)
{
    /// <summary>The state of an entity that is not selected.</summary>
    public static HelperRenderState Unselected => default;

    /// <summary>
    /// Gets the camera the shapes are drawn for, which shapes that keep their size on the screen are sized by, or
    /// <see langword="null"/> when they are drawn once for every view.
    /// </summary>
    public Camera? Camera { get; init; }
}
