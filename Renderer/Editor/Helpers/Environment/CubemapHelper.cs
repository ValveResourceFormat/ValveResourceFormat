using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The environment map of a cubemap entity, which lights what is inside its volume.
/// </summary>
public sealed class CubemapHelper : EntityHelper
{
    /// <summary>Initializes the cubemap of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public CubemapHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void AddToScene()
    {
        // Only env_cubemap has a sphere of influence, the other classes have a box
        var isSphere = Owner.Classname.Equals("env_cubemap", StringComparison.OrdinalIgnoreCase);

        SceneLightingVolume.AddEnvironmentMap(Owner.Scene, Owner.KeyValues, Owner.RigidTransform, Owner.LayerName, isSphere);
    }
}
