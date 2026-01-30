using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib
{
    sealed partial class StateNode
    {
        enum TransitionState : byte
        {
            None,
            TransitioningIn,
            TransitioningOut,
        };

        PoseNode? ChildNode;
        BoneMaskValueNode? BoneMaskValueNode;
        FloatValueNode? LayerWeightNode;
        FloatValueNode? LayerRootMotionWeightNode;

        public TimeSpan ElapsedTimeInState;
        TransitionState Transition;
        public bool IsFirstStateUpdate;

        public bool TransitioningIn => Transition == TransitionState.TransitioningIn;
        public bool TransitioningOut => Transition == TransitionState.TransitioningOut;


        public void Initialize(GraphContext ctx)
        {
            ctx.SetOptionalNodeFromIndex(ChildNodeIdx, ref ChildNode);
            ctx.SetOptionalNodeFromIndex(LayerBoneMaskNodeIdx, ref BoneMaskValueNode);
            ctx.SetOptionalNodeFromIndex(LayerWeightNodeIdx, ref LayerWeightNode);
            ctx.SetOptionalNodeFromIndex(LayerRootMotionWeightNodeIdx, ref LayerRootMotionWeightNode);
        }
    }
}
