namespace ValveResourceFormat.Renderer.AnimLib;

partial class TimeConditionNode
{
    internal enum ComparisonType : byte
    {
        PercentageThroughState = 0,
        PercentageThroughSyncEvent = 1,
        ElapsedTime = 2,
    }
}
