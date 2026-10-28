using System.Linq;

namespace ValveResourceFormat.IO;

public sealed partial class VMixGraphExtract
{
    /// <summary>
    /// A vmixtool node type: its editor property class, its plugs, and its slot in the node counters
    /// that the editors before 2026 save with each graph.
    /// </summary>
    /// <param name="Name">The node type, also the prefix of the internal names of its nodes.</param>
    /// <param name="ClassName">The class of the node properties.</param>
    /// <param name="Inputs">The input plugs.</param>
    /// <param name="Outputs">The output plugs.</param>
    /// <param name="LegacyPlugs">Plugs only older editors have, used to resolve hashed names.</param>
    /// <param name="CounterIndex2024">The node counter slot of 2024 editors, or -1.</param>
    /// <param name="CounterIndex2025">The node counter slot of 2025 editors, or -1.</param>
    private sealed record NodeType(string Name, string ClassName, string[] Inputs, string[] Outputs, string[] LegacyPlugs, int CounterIndex2024, int CounterIndex2025)
    {
        public IEnumerable<string> Plugs => Inputs.Concat(Outputs).Concat(LegacyPlugs);
    }

    private static readonly NodeType[] NodeTypes =
    [
        new("audio.Amp", "CMixAmp", ["audio:In", "volume"], ["audio:Out"], [], 19, 19),
        new("audio.AutoFilter", "CMixAutoFilter", ["audio:In", "audio:sidechain", "enable", "mix"], ["audio:Out"], [], 46, 48),
        new("audio.Blend", "CMixBlendAudio", ["audio:In1", "audio:In2", "audio:In3", "audio:In4", "audio:In5", "audio:In6", "blend"], ["audio:Out"], [], 39, 41),
        new("audio.Compressor", "CMixDynamicsCompressor", ["audio:In", "audio.SideChain", "enable", "mix"], ["audio:Out", "dBLevelOut", "LevelOut"], [], 37, 39),
        new("audio.Convolution", "CMixConvolution", ["audio:In", "vsnd:ImpulseResponse", "enable", "mix"], ["audio:Out"], [], 35, 36),
        new("audio.Delay", "CMixDelay", ["audio:In", "Frequency", "Delay", "enable", "mix"], ["audio:Out"], [], 23, 23),
        new("audio.Diffusor", "CMixDiffusor", ["audio:In", "mix"], ["audio:Out"], [], 25, 25),
        new("audio.DualCompressor", "CMixDualCompressor", [], [], [], -1, 37),
        new("audio.Dynamics", "CMixDynamics", ["audio:In", "audio.SideChain", "enable", "mix"], ["audio:Out", "dBLevelOut", "LevelOut"], ["Gain"], 13, 13),
        new("audio.Dynamics3Band", "CMixDynamics3Band", ["audio:In", "enable", "mix"], ["audio:Out"], [], 36, 38),
        new("audio.EQ8", "CMixEQ8", ["audio:In", "dynamicEQScale", "enable", "mix"], ["audio:Out"], [], 30, 31),
        new("audio.EffectChain", "CMixEffectChain", ["audio:In", "enable", "mix", "effect"], ["audio:Out"], [], 51, 53),
        new("audio.Envelope", "CMixEnvelope", ["audio:In", "enable"], ["audio:Out", "dBLevelOut", "LevelOut"], [], 31, 32),
        new("audio.Filter", "CMixFilter", ["audio:In", "Frequency", "Q", "enable", "mix"], ["audio:Out"], [], 1, 1),
        new("audio.Flanger", "CMixFlanger", ["audio:In", "Frequency", "Delay", "ModRate", "ModDepth", "mix"], ["audio:Out"], [], -1, -1),
        new("audio.Freeverb", "CMixFreeverb", ["audio:In", "mix"], ["audio:Out"], [], 27, 28),
        new("audio.Limiter", "CMixDynamicsCompressor", ["audio:In", "audio.SideChain", "enable", "mix"], ["audio:Out", "dBLevelOut", "LevelOut"], [], 38, 40),
        new("audio.Meter", "CMixAudioMeter", ["audio:In"], ["audio:Out"], [], 17, 17),
        new("audio.Mix", "CMixSum", ["audio:In1", "volume1", "audio:In2", "volume2", "audio:In3", "volume3", "audio:In4", "volume4", "audio:In5", "volume5", "audio:In6", "volume6", "audio:In7", "volume7", "audio:In8", "volume8"], ["audio:Out"], [], 2, 2),
        new("audio.ModDelay", "CMixModDelay", ["audio:In", "Frequency", "Delay", "ModRate", "ModDepth", "mix"], ["audio:Out"], [], 24, 24),
        new("audio.Osc", "CMixOsc", ["Frequency", "Phase"], ["audio:Out"], [], 47, 49),
        new("audio.Output", "CMixOutput", ["audio:In1", "volume1", "audio:In2", "volume2"], [], [], 7, 7),
        new("audio.Panner", "CMixPanner", ["audio:In", "Pan"], ["audio:Out"], [], 45, 47),
        new("audio.PitchShift", "CMixPitchShift", ["audio:In", "pitchScale", "enable", "mix"], ["audio:Out"], [], 16, 16),
        new("audio.Plateverb", "CMixPlateverb", ["audio:In", "mix"], ["audio:Out"], [], 28, 29),
        new("audio.Processor", "CMixPresetDSP", ["audio:In", "enable", "mix", "effect"], ["audio:Out"], [], 0, 0),
        new("audio.Shaper", "CMixShaper", ["audio:In", "enable", "drive", "mix"], ["audio:Out"], [], 42, 44),
        new("audio.ShoeboxReverb", "CMixBoxverb", ["audio:In", "mix"], ["audio:Out"], [], 26, 26),
        new("audio.ShoeboxReverb2", "CMixBoxverb2", ["audio:In", "mix"], ["audio:Out"], [], -1, 27),
        new("audio.Source", "CMixAudioSource", [], ["audio:Out"], [], -1, -1),
        new("audio.Splitter", "CMixSplitter", ["audio:In", "volume1", "volume2", "volume3", "volume4", "volume5", "volume6", "volume7", "volume8"], ["audio:Out1", "audio.Out2", "audio:Out3", "audio:Out4", "audio:Out5", "audio:Out6", "audio:Out7", "audio:Out8"], [], 14, 14),
        new("audio.SplitterBlend", "CMixSplitterBlend", ["audio:In", "blend"], ["audio:Out", "audio:Out2", "audio:Out3", "audio:Out4", "audio:Out5", "audio:Out6"], [], 40, 42),
        new("audio.SteamAudioDirect", "CMixSteamAudioDirect", ["audio:In", "position.x", "position.y", "position.z", "right.x", "right.y", "right.z", "up.x", "up.y", "up.z", "ahead.x", "ahead.y", "ahead.z", "applyDistanceAttenuation", "applyAirAbsorption", "applyDirectivity", "applyOcclusion", "applyTransmission", "dipoleWeight", "dipolePower", "occlusion", "transmissionLow", "transmissionMid", "transmissionHigh", "transmission"], ["audio:Out"], [], 52, 54),
        new("audio.SteamAudioHybridReverb", "CMixSteamAudioHybridReverb", ["audio:In", "impulseresponse", "reverbTime.low", "reverbTime.mid", "reverbTime.high", "reverbTime", "enable", "mix"], ["audio:Out"], ["eq.low", "eq.mid", "eq.high", "delay"], 53, 55),
        new("audio.SteamAudioPathing", "CMixSteamAudioPathing", ["audio:In", "position.x", "position.y", "position.z", "pathingmixlevel", "pathingeq", "pathingcoeffs", "enable", "mix"], ["audio:Out"], [], 29, 30),
        new("audio.SteamAudioReverb", "CMixSteamAudioReverb", ["audio:In", "indirectbinaural", "usereverboverrides", "reverbscale", "reverbscalesperband", "enable", "mix"], ["audio:Out"], [], -1, -1),
        new("audio.SteamAudioSource", "CMixSteamAudioSource", ["audio:In", "position.x", "position.y", "position.z", "interpolation", "directmixlevel", "perspectivecorrection", "relativeposition", "enable", "mix"], ["audio:Out", "delayLeft", "delayRight"], ["enablespatialblend"], 15, 15),
        new("audio.StereoDelay", "CMixStereoDelay", ["audio:In", "delayLeft", "delayRight"], ["audio:Out"], [], 50, 52),
        new("audio.Track", "CMixTrack", [], ["audio:Source"], [], 3, 3),
        new("audio.Utility", "CMixUtility", ["audio:In", "enable"], ["audio:Out"], [], 44, 46),
        new("audio.Vocoder", "CMixVocoder", ["audio:ModulatorIn", "audio:CarrierIn", "enable", "mix"], ["audio:Out"], [], 32, 33),
        new("blend.vsnd.impulseresponse", "CMixBlendVsndsToImpulseResponse", ["vsnd:0", "weight:0", "predelay:0", "vsnd:1", "weight:1", "predelay:1", "vsnd:2", "weight:2", "predelay:2", "vsnd:3", "weight:3", "predelay:3", "vsnd:4", "weight:4", "predelay:4", "vsnd:5", "weight:5", "predelay:5", "vsnd:6", "weight:6", "predelay:6", "vsnd:7", "weight:7", "predelay:7"], ["impulseresponse"], [], 56, 58),
        new("control.AHREnvelope", "CMixEnvelopeTrigger", ["trigger"], ["out"], [], 9, 9),
        new("control.Automatic", "CMixControlAutomatic", [], [], ["PlayTime", "distance", "position.x", "position.y", "position.z"], 20, 20),
        new("control.Crossfade", "CMixControlCrossfade", ["valueIn", "fadeStart", "fadeEnd"], ["fadeInValue", "fadeOutValue"], [], 12, 12),
        new("control.Curve", "CMixControlCurve", ["valueIn"], ["valueOut", "inverseOut"], [], 6, 6),
        new("control.ImpulseResponse", "CMixImpulseResponseInput", [], ["impulseresponse"], [], 54, 56),
        new("control.ImpulseResponseDelay", "CMixDelayImpulseResponse", ["ir:In", "delay"], ["ir:Out"], [], -1, 59),
        new("control.Input", "CMixControlInput", [], ["value"], [], 4, 4),
        new("control.InputArray", "CMixControlInputArray", [], ["value"], [], 22, 22),
        new("control.InputBang", "CMixControlTransientInput", [], ["value"], [], 5, 5),
        new("control.Listener", "CMixControlListener", [], [], [], 21, 21),
        new("control.Max", "CMixControlMax", ["value1", "value2", "value3", "value4", "value5", "value6"], ["valueOut"], [], 11, 11),
        new("control.Meter", "CMixControlMeter", ["value"], ["value:Out"], [], 18, 18),
        new("control.Output", "CMixControlOutput", ["value"], [], [], 43, 45),
        new("control.Remap", "CMixControlRemap", ["valueIn", "inputMin", "inputMax", "outputMin", "outputMax"], ["valueOut", "inverseOut"], [], 10, 10),
        new("control.StackInput", "CMixControlStackInput", [], ["value"], [], 41, 43),
        new("effect.Input", "CMixEffectName", [], ["Effect Name"], [], 8, 8),
        new("remap.vsnd.impulseresponse", "CMixRemapVsndToImpulseResponse", ["vsnd", "predelay"], ["impulseresponse"], [], 55, 57),
        new("sys.Group", "CMixGroupBox", [], [], [], 33, 34),
        new("sys.Subgraph", "CMixSubgraph", [], [], [], 48, 50),
        new("sys.SubgraphSwitch", "CMixSubgraphSwitch", [], [], [], 49, 51),
        new("vsnd.Input", "CMixVsndName", [], ["Vsnd Name"], [], 34, 35),
    ];

    private static readonly Dictionary<string, NodeType> NodeTypesByName = NodeTypes.ToDictionary(type => type.Name, StringComparer.Ordinal);
}
