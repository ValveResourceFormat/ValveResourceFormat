using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// What an entity gets baked for it when the map is built. Of those, the light probe of a light probe
/// volume is what a scene is drawn with.
/// </summary>
public sealed class BakeResourceHelper : EntityHelper
{
    private const string LightProbeVolume = "ToolObjects/LightProbeVolume";

    /// <summary>Gets whether what is baked is a light probe volume.</summary>
    public bool IsLightProbeVolume { get; }

    /// <summary>Initializes a baked resource of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public BakeResourceHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
        IsLightProbeVolume = LightProbeVolume.Equals(GetArgument(3), StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        if (IsLightProbeVolume)
        {
            SceneLightingVolume.AddLightProbe(Owner.Scene, Owner.KeyValues, Owner.RigidTransform, Owner.LayerName, isSphere: false);
        }
    }
}
