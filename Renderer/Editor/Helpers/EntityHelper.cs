using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.SceneNodes;
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
    public FgdHelper Declaration { get; }

    /// <summary>
    /// Gets whether the entity is seen by this helper. An entity none of whose helpers is, is drawn as a box.
    /// </summary>
    public virtual bool IsVisual => false;

    /// <summary>Gets the color of the class of the entity, which its helpers draw in while it is not selected.</summary>
    protected Color32 ClassColor => Owner.FgdClass?.Color ?? FgdClass.DefaultColor;

    /// <summary>Initializes a helper of an entity.</summary>
    /// <param name="owner">The entity the helper belongs to.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    protected EntityHelper(IHelperOwner owner, FgdHelper declaration)
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

    /// <summary>Adds the shapes the helper draws while its entity is selected.</summary>
    /// <param name="shapes">Where the shapes go and where the entity is.</param>
    internal virtual void AddSelectionShapes(in SelectionShapes shapes)
    {
    }

    /// <summary>
    /// Adds lines the helper shows while its entity is not selected, with the same primitives as the shapes
    /// it shows while it is. They are drawn as part of the scene, opaque or blended in their alpha.
    /// </summary>
    /// <param name="transform">Where the lines are placed.</param>
    /// <param name="draw">Draws the lines, in the space of <paramref name="transform"/>.</param>
    /// <param name="layerName">The visibility layer of the lines.</param>
    /// <param name="translucent">Whether the lines keep the alpha of their colors.</param>
    private protected void AddLines(in Matrix4x4 transform, Action<EntityHelperLines> draw, string layerName = HammerEntityVisuals.MarkerLayerName, bool translucent = false)
    {
        var vertices = new HelperVertices();

        draw(new EntityHelperLines(vertices, Matrix4x4.Identity, translucent ? HelperPasses.Translucent : HelperPasses.Visible));

        if (vertices.Lines.Count == 0)
        {
            return;
        }

        Owner.AddNode(new LineSceneNode(Owner.Scene, [.. vertices.Lines])
        {
            Transform = transform,
            LayerName = layerName,
            Translucent = translucent,
        });
    }

    /// <summary>Gets a color the block the helper is declared with gives as three or four numbers.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The color, or <see langword="null"/> when the block has no such color.</returns>
    protected Color32? GetMetadataColor(string key)
    {
        if (Declaration.Metadata is not { } block || !block.ContainsKey(key) || block[key] is not { IsArray: true, Count: >= 3 } color)
        {
            return null;
        }

        static byte GetByte(KVObject value) => value.ToByte(CultureInfo.InvariantCulture);

        return new Color32(GetByte(color[0]), GetByte(color[1]), GetByte(color[2]), color.Count > 3 ? GetByte(color[3]) : byte.MaxValue);
    }

    /// <summary>Gets a flag of the block the helper is declared with.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where the block has no such key.</param>
    /// <returns>The flag.</returns>
    protected bool GetMetadataFlag(string key, bool defaultValue)
        => Declaration.Metadata is { } block && block.ContainsKey(key) ? block.GetBooleanProperty(key) : defaultValue;

    /// <summary>Gets whether any argument of the declaration is a flag, without regard to case.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns>Whether the declaration has it.</returns>
    protected bool HasArgument(string flag)
        => Declaration.Arguments.Exists(argument => argument.Equals(flag, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Gets a key of the entity as text, or the default its class gives the key where the entity has none.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, empty when neither has it.</returns>
    protected string GetStringKey(string key)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetStringProperty(key) ?? string.Empty : GetDefault(key) ?? string.Empty;

    /// <summary>Gets a key of the entity as a number, or the default its class gives the key where the entity has none.</summary>
    /// <param name="key">Name of the key.</param>
    /// <param name="defaultValue">The value where neither has it.</param>
    /// <returns>The value.</returns>
    protected float GetFloatKey(string key, float defaultValue = 0f)
    {
        if (Owner.KeyValues.ContainsKey(key))
        {
            return Owner.KeyValues.GetFloatProperty(key);
        }

        return GetDefault(key) is { } value ? ParseComponents(value).X : defaultValue;
    }

    /// <summary>Gets a key of the entity as an integer, or the default its class gives the key where the entity has none.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, zero when neither has it.</returns>
    protected int GetIntegerKey(string key)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetInt32Property(key) : (int)GetDefaultComponents(key).X;

    /// <summary>Gets a key of the entity as a vector, or the default its class gives the key where the entity has none.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, zero when neither has it.</returns>
    protected Vector2 GetVector2Key(string key)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetVector2Property(key) : GetDefaultComponents(key).AsVector2();

    /// <summary>Gets a key of the entity as a vector, or the default its class gives the key where the entity has none.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, zero when neither has it.</returns>
    protected Vector3 GetVector3Key(string key)
        => Owner.KeyValues.ContainsKey(key) ? Owner.KeyValues.GetVector3Property(key) : GetDefaultComponents(key).AsVector3();

    // An entity has every key of its class, set to the default of the class until it is changed
    private string? GetDefault(string key)
        => Owner.FgdClass?.FindVariable(key) is { HasDefault: true } variable ? variable.DefaultString : null;

    private Vector4 GetDefaultComponents(string key) => ParseComponents(GetDefault(key) ?? string.Empty);

    private static Vector4 ParseComponents(string value)
    {
        Span<float> components = stackalloc float[4];
        var index = 0;

        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (index == components.Length)
            {
                break;
            }

            components[index++] = float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var component) ? component : 0f;
        }

        return new Vector4(components);
    }

    /// <summary>Gets a key of the block the helper is declared with as text.</summary>
    /// <param name="key">Name of the key.</param>
    /// <returns>The value, or <see langword="null"/> when the helper has no such key.</returns>
    protected string? GetMetadata(string key)
        => Declaration.Metadata is { } block && block.ContainsKey(key) ? block.GetStringProperty(key) : null;

    /// <summary>Gets an argument of the declaration, or <see langword="null"/> when it has fewer.</summary>
    /// <param name="index">Zero based index of the argument.</param>
    /// <returns>The argument.</returns>
    protected string? GetArgument(int index)
        => index < Declaration.Arguments.Count ? Declaration.Arguments[index] : null;
}
