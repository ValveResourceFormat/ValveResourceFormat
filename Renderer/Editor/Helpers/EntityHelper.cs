using System.Collections.Immutable;
using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// One of the things an entity is drawn with in the editor, such as its model, its icon or the light it casts.
/// An entity gets a helper for each one its class declares.
/// </summary>
public abstract class EntityHelper
{
    /// <summary>Gets the entity the helper belongs to.</summary>
    public IHelperOwner Owner { get; }

    /// <summary>Gets the helper as the class of the entity declares it.</summary>
    public HelperDeclaration Declaration { get; }

    /// <summary>
    /// Gets whether the helper makes the entity visible, which most helpers do. An entity without such a helper is
    /// drawn as a box.
    /// </summary>
    public virtual bool IsVisual => true;

    /// <summary>Gets whether the helper is only drawn while helpers are shown, which they are by default.</summary>
    public virtual bool NeedsShowHelpers => false;

    /// <summary>
    /// Gets whether the helper is hidden while the editor shows only what the game shows, unless its entity is selected.
    /// </summary>
    public virtual bool HiddenInGameObjectsOnly => false;

    /// <summary>Gets whether nothing of the helper is drawn while its entity is not selected.</summary>
    public virtual bool RequiresSelection => false;

    /// <summary>
    /// Gets whether the helper stands in for its entity in the editor. The game draws the entities of a compiled
    /// map, so only these helpers are drawn in their place.
    /// </summary>
    public virtual bool IsStandIn => false;

    /// <summary>
    /// Gets whether the helper only draws shapes of the editor, which the game does not show. An entity of a
    /// compiled map is drawn with these too.
    /// </summary>
    public virtual bool IsToolShape => false;

    /// <summary>
    /// Gets whether the helper of a path is drawn as for a selected path while any node of the path is
    /// selected.
    /// </summary>
    internal virtual bool IsSelectedWithPathNodes => false;

    /// <summary>Default of the editor setting for the color of selected shapes.</summary>
    internal static readonly Color32 SelectionWireframeColor = new(80, 200, 255);

    /// <summary>Default of the editor setting for the color of the selected object.</summary>
    internal static readonly Color32 SelectionColor = new(255, 255, 0);

    /// <summary>Default of the editor setting for the tint over the selected object.</summary>
    internal static readonly Color32 SelectionOverlayColor = new(255, 183, 45);

    /// <summary>The color helpers draw their shapes in while their entity is not selected.</summary>
    internal static readonly Color32 UnselectedEntityColor = new(220, 220, 220);

    private readonly List<ToolSceneNode> shapeNodes = [];

    /// <summary>
    /// Gets the world bounds the helper adds to the bounds of its entity, or <see langword="null"/> if it adds none.
    /// The selection box is made from these. Most helpers add none, since a sphere or a line around an entity does
    /// not make the entity larger. An entity whose helpers add none is boxed at its origin.
    /// </summary>
    public virtual AABB? ObjectBounds => null;

    /// <summary>Gets <see cref="ObjectBounds"/> boxed in another space, where the helper knows them more tightly.</summary>
    /// <param name="worldToSpace">The transform from the world into the space.</param>
    public virtual AABB? GetObjectBounds(in Matrix4x4 worldToSpace) => ObjectBounds?.Transform(worldToSpace);

    /// <summary>
    /// Gets the visibility layers of the nodes the helper adds to the scene, besides those of its entity. They stand
    /// in for the options of the editor: the helpers layer for a helper only drawn while helpers are shown, the
    /// editor-only entities layer for one hidden while only what the game shows is drawn, and none for one neither
    /// option hides.
    /// </summary>
    protected ImmutableArray<WorldLayer> VisibilityLayers => NeedsShowHelpers
        ? [HammerEntityVisuals.HelpersLayer]
        : HiddenInGameObjectsOnly ? [HammerEntityVisuals.MarkerLayer] : [];

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
    /// Draws the shapes of the helper in the world, for its entity selected or not selected.
    /// </summary>
    /// <param name="context">The tool render context.</param>
    /// <param name="state">Whether the entity is selected.</param>
    internal virtual void Render(ToolRenderContext context, in HelperRenderState state)
    {
    }

    /// <summary>The transform the shapes a helper adds to the scene follow as the entity moves.</summary>
    private protected enum ShapePlacement
    {
        /// <summary>Where they were drawn, whatever the entity does.</summary>
        World,

        /// <summary>At the origin of the entity, aligned to the world axes.</summary>
        Origin,

        /// <summary>At the entity, turned and scaled with it.</summary>
        Entity,
    }

    /// <summary>
    /// Adds the shapes the helper draws for an unselected entity to the scene, as part of it. They are hidden
    /// while the entity is selected, since the selection draws them in their selected state.
    /// </summary>
    /// <param name="placement">The transform the shapes follow as the entity moves.</param>
    /// <param name="keepsMarkerSize">
    /// Whether the shapes keep their size as markers do, rather than being magnified with their scene.
    /// </param>
    /// <param name="layer">
    /// The visibility layer of the shapes, besides those of the entity, or <see langword="null"/> for
    /// <see cref="VisibilityLayers"/>.
    /// </param>
    private protected void AddShapes(ShapePlacement placement = ShapePlacement.World, bool keepsMarkerSize = false, WorldLayer? layer = null)
    {
        if (RequiresSelection)
        {
            return;
        }

        var transform = placement switch
        {
            ShapePlacement.Origin => Matrix4x4.CreateTranslation(Owner.Transform.Translation),
            ShapePlacement.Entity => Owner.Transform,
            _ => Matrix4x4.Identity,
        };

        if (!Matrix4x4.Invert(transform, out var worldToPlacement))
        {
            return;
        }

        var node = new ToolSceneNode(Owner.Scene)
        {
            Layers = layer != null ? [layer] : VisibilityLayers,
            KeepsMarkerSize = keepsMarkerSize,
            KeepsWorldAxes = placement == ShapePlacement.Origin,
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
        Owner.AddNode(node, followsEntity: placement != ShapePlacement.World);
    }

    /// <summary>Hides the shapes the helper draws for an unselected entity while the entity is selected.</summary>
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

    /// <summary>Draws a box as lit faces in a color. While its entity is selected, the faces are tinted and outlined.</summary>
    /// <param name="context">The tool render context.</param>
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

        // DrawBoxFaces leaves the draw color as the last face was drawn in
        context.PushRenderMode(ToolRenderMode.Wireframe);
        context.DrawColor = color;
        context.DrawBoxFaceOutlines(mins, maxs);
        context.PopRenderMode();
    }

    /// <summary>Draws a line two pixels wide, dashed over the scene where the scene hides it.</summary>
    /// <param name="context">The tool render context.</param>
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

    /// <summary>Gets a color given as three or four numbers in the metadata of the declaration.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The color, or <see langword="null"/> when the block has no such color.</returns>
    protected Color32? GetMetadataColor(string key)
        => Declaration.Metadata is { } block && block.TryGetValue(key, out var value) ? value.ToColor32() : null;

    /// <summary>Gets a key of the entity as a color of three or four numbers.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The color where the entity has no such key.</param>
    /// <returns>The color, black where the value is not a color.</returns>
    private protected Color32 GetColorKey(string key, Color32 defaultValue)
        => Owner.KeyValues.GetColor32(key, defaultValue);

    /// <summary>Gets a flag from the metadata of the declaration.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the block has no such key.</param>
    /// <returns>The flag.</returns>
    protected bool GetMetadataFlag(string key, bool defaultValue = false) => GetBlockFlag(Declaration.Metadata, key, defaultValue);

    /// <summary>Gets whether any argument of the declaration equals the flag, ignoring case.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>Whether the declaration has it.</returns>
    protected bool HasArgument(string flag)
        => Declaration.Arguments.Contains(flag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the color given by three arguments of the declaration. A channel is full when its argument is
    /// missing. Each argument is read as the integer it starts with and wraps to a byte.
    /// </summary>
    /// <param name="start">Index of the first of the three.</param>
    /// <returns>The color, opaque.</returns>
    protected Color32 GetColorArguments(int start)
    {
        byte GetChannel(int index) => GetArgument(index) is { } argument
            ? unchecked((byte)ValueParsing.ParseLeadingInt(argument))
            : byte.MaxValue;

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

    /// <summary>Gets a key of the metadata of the declaration as text.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, or <see langword="null"/> when the helper has no such key.</returns>
    protected string? GetMetadata(string key)
        => Declaration.Metadata?.GetStringProperty(key);

    /// <summary>Gets the nested block stored under a key of a block.</summary>
    /// <param name="block">The block to look in, or <see langword="null"/> for none.</param>
    /// <param name="key">Name of the key.</param>
    /// <returns>The block, or <see langword="null"/> when there is no block under the key.</returns>
    private protected static KVObject? GetBlock(KVObject? block, string key)
        => block != null && block.ContainsKey(key) && block[key] is { ValueType: KVValueType.Collection } value ? value : null;

    // The key getters below are shorthands for reading the owner entity's keys with the KVObjectExtensions getters.

    /// <summary>Gets a key of the entity as a number.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The value.</returns>
    protected float GetFloatKey(string key, float defaultValue = 0f)
        => Owner.KeyValues.GetFloatProperty(key, defaultValue);

    /// <summary>Gets a key of the entity as a flag.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The flag.</returns>
    protected bool GetBooleanKey(string key, bool defaultValue = false)
        => Owner.KeyValues.GetBooleanProperty(key, defaultValue);

    /// <summary>Gets a key of the entity as an integer.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The value.</returns>
    protected int GetIntegerKey(string key, int defaultValue = 0)
        => Owner.KeyValues.GetInt32Property(key, defaultValue);

    /// <summary>Gets a key of the entity as a two component vector.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, zero where the entity has no such key.</returns>
    protected Vector2 GetVector2Key(string key)
        => Owner.KeyValues.GetVector2Property(key);

    /// <summary>Gets a key of the entity as a vector.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the entity has no such key.</param>
    /// <returns>The value.</returns>
    protected Vector3 GetVector3Key(string key, Vector3 defaultValue = default)
        => Owner.KeyValues.GetVector3Property(key, defaultValue);

    /// <summary>Gets a key of a block as text.</summary>
    /// <param name="block">The block to look in, or <see langword="null"/> for none.</param>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, or <see langword="null"/> when the block has no such key or it is empty.</returns>
    private protected static string? GetBlockString(KVObject? block, string key)
        => block?.GetStringProperty(key) is { Length: > 0 } value ? value : null;

    /// <summary>Gets a flag of a block.</summary>
    /// <param name="block">The block to look in, or <see langword="null"/> for none.</param>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the block has no such key.</param>
    /// <returns>The flag.</returns>
    private protected static bool GetBlockFlag(KVObject? block, string key, bool defaultValue = false)
        => block?.GetBooleanProperty(key, defaultValue) ?? defaultValue;

    /// <summary>Gets an argument of the declaration, or <see langword="null"/> when it has fewer.</summary>
    /// <param name="index">Zero based index of the argument.</param>
    /// <returns>The argument.</returns>
    protected string? GetArgument(int index)
        => index < Declaration.Arguments.Count ? Declaration.Arguments[index] : null;

    /// <summary>
    /// Gets whether a compiled file used to draw an entity exists. A file can be opened without its game, so the
    /// assets of the game may be missing. The entity is then drawn some other way.
    /// </summary>
    /// <param name="owner">The entity.</param>
    /// <param name="name">The file, without the compiled suffix.</param>
    /// <returns>Whether the file exists.</returns>
    private protected static bool CompiledFileExists(IHelperOwner owner, string name)
        => owner.Scene.RendererContext.FileLoader.FindFile(name + GameFileLoader.CompiledFileSuffix, logNotFound: false)
            is not { PathOnDisk: null, PackageEntry: null };

    /// <summary>Gets the entity key an argument of a declaration names. The editor keeps no more than 31 characters of it.</summary>
    /// <param name="argument">The argument.</param>
    /// <returns>The name of the key.</returns>
    private protected static string ToTruncatedKey(string argument) => argument[..Math.Min(argument.Length, 31)].ToLowerInvariant();
}

/// <summary>Whether the entity of a helper is selected, and whether it is the only thing selected.</summary>
/// <param name="IsSelected">Whether the entity is selected.</param>
/// <param name="IsOnlySelected">Whether the entity is the only thing selected.</param>
internal readonly record struct HelperRenderState(bool IsSelected, bool IsOnlySelected)
{
    /// <summary>The state of an entity that is not selected.</summary>
    public static HelperRenderState Unselected => default;

    /// <summary>
    /// Gets the camera the shapes are drawn for, used to size shapes that keep their size on the screen, or
    /// <see langword="null"/> when they are drawn once for every view.
    /// </summary>
    public Camera? Camera { get; init; }
}
