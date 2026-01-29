using ValveResourceFormat.Serialization.KeyValues;
namespace ValveResourceFormat.Renderer.AnimLib;

class Clip
{
    public string Skeleton { get; } // InfoForResourceTypeCNmSkeleton
    public uint NumFrames { get; }
    public float Duration { get; }
    public byte[] CompressedPoseData { get; }
    public CompressionSettings[] TrackCompressionSettings { get; }
    public uint[] CompressedPoseOffsets { get; }
    public GlobalSymbol[] FloatCurveIDs { get; }
    public FloatCurveCompressionSettings[] FloatCurveDefs { get; }
    public ushort[] CompressedFloatCurveData { get; }
    public uint[] CompressedFloatCurveOffsets { get; }
    public SyncTrack SyncTrack { get; }
    public RootMotionData RootMotion { get; }
    public bool IsAdditive { get; }
    public Clip__ModelSpaceSamplingChainLink[] ModelSpaceSamplingChain { get; }
    public int[] ModelSpaceBoneSamplingIndices { get; }

    public Clip(KVObject data)
    {
        Skeleton = data.GetProperty<string>("m_skeleton");
        NumFrames = data.GetUInt32Property("m_nNumFrames");
        Duration = data.GetFloatProperty("m_flDuration");
        CompressedPoseData = data.GetArray<byte>("m_compressedPoseData");
        TrackCompressionSettings = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_trackCompressionSettings"), kv => new CompressionSettings(kv))];
        CompressedPoseOffsets = data.GetArray<uint>("m_compressedPoseOffsets");
        FloatCurveIDs = data.GetArray<GlobalSymbol>("m_floatCurveIDs");
        FloatCurveDefs = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_floatCurveDefs"), kv => new FloatCurveCompressionSettings(kv))];
        CompressedFloatCurveData = data.GetArray<ushort>("m_compressedFloatCurveData");
        CompressedFloatCurveOffsets = data.GetArray<uint>("m_compressedFloatCurveOffsets");
        SyncTrack = new(data.GetProperty<KVObject>("m_syncTrack"));
        RootMotion = new(data.GetProperty<KVObject>("m_rootMotion"));
        IsAdditive = data.GetProperty<bool>("m_bIsAdditive");
        ModelSpaceSamplingChain = [.. System.Linq.Enumerable.Select(data.GetArray<KVObject>("m_modelSpaceSamplingChain"), kv => new Clip__ModelSpaceSamplingChainLink(kv))];
        ModelSpaceBoneSamplingIndices = data.GetArray<int>("m_modelSpaceBoneSamplingIndices");
    }
}
