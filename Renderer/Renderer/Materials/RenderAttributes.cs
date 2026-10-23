using ValveResourceFormat.Renderer.Buffers;
using ValveResourceFormat.Renderer.Shaders;
using ValveResourceFormat.Renderer.Utils;

namespace ValveResourceFormat.Renderer.Materials;

/// <summary>Per-node values by name, overriding what materials draw with. Materials stay shared.</summary>
/// <remarks>
/// Each material and shader variant they touch gets a constant buffer baked with them, rebaked when either changes.
/// </remarks>
public sealed class RenderAttributes
{
    internal MaterialInputs<Variant> Values { get; } = [];

    internal uint Version => Values.Version;

    // Filled by the material, freed with the node
    internal Dictionary<(RenderMaterial Material, GlobalsLayout Layout), BakedGlobals> Baked { get; } = [];

    internal sealed class BakedGlobals(Globals buffer)
    {
        public Globals Buffer { get; } = buffer;
        public uint MaterialVersion { get; set; }
        public uint AttributesVersion { get; set; }
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

    /// <summary>Gets whether any attribute changes what a shader with <paramref name="layout"/> draws.</summary>
    internal bool Overrides(GlobalsLayout layout)
    {
        foreach (var name in Values.Keys)
        {
            if (layout.Members.ContainsKey(name))
            {
                return true;
            }
        }

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
