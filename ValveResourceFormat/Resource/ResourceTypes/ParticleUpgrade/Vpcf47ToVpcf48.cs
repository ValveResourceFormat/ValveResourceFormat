using System.Text;
using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Renames five CP remap operator classes by replacing every case-insensitive "cp" in the
/// class name with "Transform", with the velocity class hardcoded to keep its capital T, and
/// folds the per-family legacy control point key into m_TransformInput.
/// </summary>
internal sealed class Vpcf47ToVpcf48 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf47";
    public override string ToFormat => "vpcf48";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            var cls = node.GetString("_class", "");
            var isVisibility = cls is "C_OP_RemapCPVisibilityToScalar" or "C_OP_RemapCPVisibilityToVector";
            var isYaw = cls == "C_OP_RemapCPOrientationToYaw";
            var isOrientation = isYaw || cls == "C_OP_RemapCPOrientationToRotations";
            var isVelocity = cls == "C_OP_RemapCPtoVelocity";

            if (!isVisibility && !isOrientation && !isVelocity)
            {
                return;
            }

            node.SetString("_class", isVelocity ? "C_OP_RemapTransformToVelocity" : ReplaceCaseInsensitive(cls, "cp", "Transform"));

            var key = isOrientation ? "m_nCP" : isVelocity ? "m_nCPInput" : "m_nControlPoint";
            var cp = unchecked((int)node.TakeInt(key, isYaw ? 1 : 0));
            Vpcf45ToVpcf46.FillTransformInput(node.MergeObject("m_TransformInput"), cp);
        });
    }

    /// <summary>
    /// Replaces every case-insensitive occurrence of the needle in a single left-to-right scan
    /// without rescanning replacement text.
    /// </summary>
    internal static string ReplaceCaseInsensitive(string value, string needle, string replacement)
    {
        var builder = new StringBuilder(value.Length);
        var position = 0;

        while (position < value.Length)
        {
            if (position + needle.Length <= value.Length
                && string.Compare(value, position, needle, 0, needle.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                builder.Append(replacement);
                position += needle.Length;
            }
            else
            {
                builder.Append(value[position]);
                position++;
            }
        }

        return builder.ToString();
    }
}
