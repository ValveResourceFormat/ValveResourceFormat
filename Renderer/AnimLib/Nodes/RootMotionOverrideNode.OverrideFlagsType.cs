namespace ValveResourceFormat.Renderer.AnimLib;

partial class RootMotionOverrideNode
{
    internal enum OverrideFlagsType : byte
    {
        AllowMoveX = 0,
        AllowMoveY = 1,
        AllowMoveZ = 2,
        AllowFacingPitch = 3,
        ListenForEvents = 4,
    }
}
