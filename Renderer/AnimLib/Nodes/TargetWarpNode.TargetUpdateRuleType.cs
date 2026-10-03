namespace ValveResourceFormat.Renderer.AnimLib;

partial class TargetWarpNode
{
    internal enum TargetUpdateRuleType : byte
    {
        None = 0,
        Recalculate = 1,
        Offset = 2,
        RecalculateOrOffset = 3,
    }
}
