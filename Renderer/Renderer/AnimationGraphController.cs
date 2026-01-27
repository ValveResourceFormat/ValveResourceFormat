using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace ValveResourceFormat.Renderer
{
    public class AnimationGraphController : AnimationController
    {
        public AnimationGraphController(Skeleton skeleton, NmGraphDefinition graphDefinition) : base(skeleton, [])
        {
        }

        public override bool Update(float timeStep)
        {
            // For now, no update - animation graph simulation not implemented yet
            return false;
        }
    }
}
