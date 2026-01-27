using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace ValveResourceFormat.Renderer
{
    public class AnimationGraphController : BaseAnimationController
    {
        public AnimationGraphController(Skeleton skeleton) : base(skeleton)
        {
        }

        public override bool Update(float timeStep)
        {
            // For now, no update - animation graph simulation not implemented yet
            return false;
        }
    }
}
