using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

[KV3Transfer]
partial class Clip
{
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public uint NumFrames { get; }
    public float Duration { get; }
    public byte[] CompressedPoseData { get; } = [];
    public CompressionSettings[] TrackCompressionSettings { get; } = [];
    public uint[] CompressedPoseOffsets { get; } = [];
    public SyncTrack SyncTrack { get; }
    public RootMotionData RootMotion { get; }
    public bool IsAdditive { get; }
    public Clip.ModelSpaceSamplingChainLink[] ModelSpaceSamplingChain { get; } = [];
    public int[] ModelSpaceBoneSamplingIndices { get; } = [];
}
