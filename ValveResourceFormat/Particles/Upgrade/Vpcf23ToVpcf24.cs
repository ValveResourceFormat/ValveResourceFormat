using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles.Upgrade;

/// <summary>
/// Converts legacy root renderer colors into vector inputs: m_ColorScale and m_Color both
/// become the opaque m_vecColorScale literal color (m_Color wins when both are present), and
/// m_vEndTrailTintFactor fans out into m_vecTailColorScale plus m_flTailAlphaScale.
/// </summary>
internal sealed class Vpcf23ToVpcf24 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf23";
    public override string ToFormat => "vpcf24";

    public override void Apply(KVObject root)
    {
        var renderers = root.ElementsOf("m_Renderers");

        foreach (var element in renderers)
        {
            ConvertColorScale(element, "m_ColorScale");
        }

        foreach (var element in renderers)
        {
            ConvertColorScale(element, "m_Color");
        }

        foreach (var element in renderers)
        {
            ConvertTailTint(element);
        }
    }

    private static void ConvertColorScale(KVObject element, string name)
    {
        if (!UpgradeKV.IsObject(element) || !element.ContainsKey(name))
        {
            return;
        }

        var (r, g, b) = GetColorRgb(element, name);
        element.Remove(name);

        var input = element.SetObject("m_vecColorScale");
        input.SetString("m_nType", "PVEC_TYPE_LITERAL_COLOR");
        input.SetMember("m_LiteralColor", IntArray(r, g, b));
    }

    private static void ConvertTailTint(KVObject element)
    {
        if (!UpgradeKV.IsObject(element) || !element.ContainsKey("m_vEndTrailTintFactor"))
        {
            return;
        }

        var tint = GetFloat4(element, "m_vEndTrailTintFactor", new Vector4(1f, 1f, 1f, 1f));
        element.Remove("m_vEndTrailTintFactor");

        var tail = element.SetObject("m_vecTailColorScale");

        if (tint.X == 1f && tint.Y == 1f && tint.Z == 1f)
        {
            tail.SetString("m_nType", "PVEC_TYPE_LITERAL_COLOR");
            tail.SetMember("m_LiteralColor", IntArray(255, 255, 255));
        }
        else
        {
            tail.SetString("m_nType", "PVEC_TYPE_LITERAL");
            tail.SetFloatArray("m_vLiteralValue", tint.X, tint.Y, tint.Z);
        }

        var alpha = element.SetObject("m_flTailAlphaScale");
        alpha.SetString("m_nType", "PF_TYPE_LITERAL");
        alpha.SetFloat("m_flLiteralValue", tint.W);
    }

    private static (int R, int G, int B) GetColorRgb(KVObject element, string name)
    {
        var value = element.Find(name);

        if (value is not { IsArray: true } || value.Count is not (3 or 4))
        {
            return (255, 255, 255);
        }

        return (value[0].ToInt32OrDefault(255), value[1].ToInt32OrDefault(255), value[2].ToInt32OrDefault(255));
    }

    // Only an array of exactly four numbers counts, anything else gives the default
    private static Vector4 GetFloat4(KVObject element, string name, Vector4 defaultValue)
    {
        var value = element.Find(name);

        if (value is not { IsArray: true } || value.Count != 4)
        {
            return defaultValue;
        }

        return value.ToVector4();
    }

    private static KVObject IntArray(params int[] values) => KVObject.PackedArray(values);
}
