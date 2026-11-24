using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Renderer.Utils;

namespace ValveResourceFormat.Renderer.Materials;

/// <summary>Per-node values by name that override what shared materials draw with.</summary>
public sealed class RenderAttributes
{
    internal MaterialInputs<Variant> Values { get; } = [];

    internal uint Version => Values.Version;

    internal bool IsEmpty => Values.Count == 0;

    // Filled by the material, freed with the node
    internal Dictionary<RenderMaterial, RenderMaterial.GlobalsState> Baked { get; } = [];

    /// <summary>Gets or sets an attribute.</summary>
    /// <exception cref="KeyNotFoundException">The attribute is not set.</exception>
    public Variant this[string name]
    {
        get => Values[name];
        set => Values[name] = value;
    }

    /// <summary>Removes an attribute.</summary>
    /// <returns>Whether it was set.</returns>
    public bool Remove(string name) => Values.Remove(name);

    /// <summary>Gets whether any attribute changes what <paramref name="material"/> draws with a shader of <paramref name="layout"/>.</summary>
    internal bool Overrides(RenderMaterial material, GlobalsLayout layout)
    {
        foreach (var name in Values.Keys)
        {
            if (layout.Members.ContainsKey(name) || ReadByExpressions(material, name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ReadByExpressions(RenderMaterial material, string name)
    {
        foreach (var used in material.Material.RenderAttributesUsed)
        {
            if (string.Equals(used, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gets an attribute by token, with scalars in every component.</summary>
    internal bool TryGetValue(StringToken name, out Vector4 value)
    {
        foreach (var (key, variant) in Values)
        {
            if (StringToken.Get(key) == name.Token)
            {
                value = variant.ToVector4();
                return true;
            }
        }

        value = default;
        return false;
    }

    internal void Delete()
    {
        foreach (var baked in Baked.Values)
        {
            baked.Buffer.Delete();
        }

        Baked.Clear();
    }
}
