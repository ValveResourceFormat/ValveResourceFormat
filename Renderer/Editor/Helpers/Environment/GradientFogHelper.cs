using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Helpers;

/// <summary>
/// The distance and height fog of an <c>env_gradient_fog</c>.
/// </summary>
public sealed class GradientFogHelper : FogHelper
{
    /// <summary>Initializes the fog of an entity.</summary>
    /// <param name="owner">The entity.</param>
    /// <param name="declaration">The helper as the class of the entity declares it.</param>
    public GradientFogHelper(IHelperOwner owner, FgdHelper declaration) : base(owner, declaration)
    {
    }

    /// <inheritdoc/>
    public override void Apply()
    {
        var fogInfo = Owner.Scene.FogInfo;

        fogInfo.GradientFogActive = true;
        fogInfo.GradientFog = SceneGradientFog.FromEntityProperties(Owner.Scene, Owner.KeyValues);
    }
}
