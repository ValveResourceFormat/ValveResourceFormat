namespace ValveResourceFormat.Renderer.AnimLib;

partial class TransitionNode
{
    internal enum TransitionOptionsType : byte
    {
        None = 0,
        ClampDuration = 1,
        Synchronized = 2,
        MatchSourceTime = 3,
        MatchSyncEventIndex = 4,
        MatchSyncEventID = 5,
        MatchSyncEventPercentage = 6,
        PreferClosestSyncEventID = 7,
        MatchTimeInSeconds = 8,
        OffsetTimeInSeconds = 9,
    }
}
