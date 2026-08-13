using ValveKeyValue;

namespace ValveResourceFormat.ResourceTypes.ParticleUpgrade;

/// <summary>
/// Re-runs the rotation random and random vector rewrites so documents that entered the
/// chain mid-way still lose those legacy classes.
/// </summary>
internal sealed class Vpcf41ToVpcf42 : ParticleUpgradeStep
{
    public override string FromFormat => "vpcf41";
    public override string ToFormat => "vpcf42";

    public override void Apply(KVObject root)
    {
        UpgradeKV.WalkObjects(root, static node =>
        {
            Vpcf39ToVpcf40.RewriteRotationRandom(node);
            Vpcf40ToVpcf41.RewriteRandomVector(node);
        });
    }
}
