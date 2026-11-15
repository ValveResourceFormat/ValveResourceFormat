using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Renderer.Utils;

namespace ValveResourceFormat.Renderer.Materials;

/// <summary>Per-node values by name, overriding what materials draw with. Materials stay shared.</summary>
/// <remarks>
/// Each material they touch gets a constant buffer baked with them, rebaked when either or the shader layout changes.
/// </remarks>
public sealed class RenderAttributes
{
    internal MaterialInputs<Variant> Values { get; } = [];

    internal uint Version => Values.Version;

    // Filled by the material, freed with the node
    internal Dictionary<RenderMaterial, BakedGlobals> Baked { get; } = [];

    internal sealed class BakedGlobals(Globals buffer)
    {
        public Globals Buffer { get; } = buffer;
        public GlobalsLayout? Layout { get; set; }
        public uint MaterialVersion { get; set; }
        public uint AttributesVersion { get; set; }
        public float EvaluatedTime { get; set; }
        public bool ExpressionsConstant { get; set; }
    }

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

    /// <summary>Gets an attribute by the token expressions read it with, with scalars in every component.</summary>
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
