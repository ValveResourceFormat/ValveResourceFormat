using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Model;

internal sealed partial class MdlFile
{
    // Vertex animation deltas are fixed point with this scale unless the header gives one
    private const float DefaultVertAnimFixedPointScale = 1f / 4096f;

    public string[] FlexDescNames { get; private set; } = [];

    public StudioFlexController[] FlexControllers { get; private set; } = [];
    public string[] FlexControllerNames { get; private set; } = [];
    public string[] FlexControllerTypes { get; private set; } = [];

    public StudioFlexRule[] FlexRules { get; private set; } = [];
    public StudioFlexOp[][] FlexRuleOps { get; private set; } = [];

    /// <summary>The flexes of each mesh, indexed by body part, sub-model and mesh.</summary>
    public MeshFlex[][][][] MeshFlexes { get; private set; } = [];

    internal readonly record struct FlexVertex(int Index, float Side, Vector3 Delta, Vector3 NormalDelta, float Wrinkle);
    internal readonly record struct MeshFlex(StudioFlex Header, FlexVertex[] Vertices);

    private void ReadFlexData(byte[] data)
    {
        var descs = LumpReader<StudioFlexDesc>.ReadLumpAt(data, Hdr.FlexDescOffset, Hdr.NumFlexDesc);
        var descSize = Marshal.SizeOf<StudioFlexDesc>();
        FlexDescNames = new string[descs.Length];

        for (var i = 0; i < descs.Length; i++)
        {
            var start = Hdr.FlexDescOffset + ((long)i * descSize);
            FlexDescNames[i] = ReadCString(data, start + descs[i].FacsIndex);
        }

        FlexControllers = LumpReader<StudioFlexController>.ReadLumpAt(data, Hdr.FlexControllerOffset, Hdr.NumFlexControllers);
        FlexControllerNames = new string[FlexControllers.Length];
        FlexControllerTypes = new string[FlexControllers.Length];

        for (var i = 0; i < FlexControllers.Length; i++)
        {
            var start = Hdr.FlexControllerOffset + ((long)i * FlexControllerSize);
            FlexControllerNames[i] = ReadCString(data, start + FlexControllers[i].NameIndex);
            FlexControllerTypes[i] = ReadCString(data, start + FlexControllers[i].TypeIndex);
        }

        FlexRules = LumpReader<StudioFlexRule>.ReadLumpAt(data, Hdr.FlexRuleOffset, Hdr.NumFlexRules);
        FlexRuleOps = new StudioFlexOp[FlexRules.Length][];

        var ruleSize = Marshal.SizeOf<StudioFlexRule>();

        for (var i = 0; i < FlexRules.Length; i++)
        {
            var start = Hdr.FlexRuleOffset + ((long)i * ruleSize);
            FlexRuleOps[i] = LumpReader<StudioFlexOp>.ReadLumpAt(data, start + FlexRules[i].OpIndex, FlexRules[i].NumOps);
        }
    }

    private static readonly int FlexControllerSize = Marshal.SizeOf<StudioFlexController>();

    private MeshFlex[] ReadMeshFlexes(byte[] data, long meshStart, StudioMesh mesh)
    {
        if (mesh.NumFlexes <= 0)
        {
            return [];
        }

        var flexesStart = meshStart + mesh.FlexIndex;
        var headers = LumpReader<StudioFlex>.ReadLumpAt(data, flexesStart, mesh.NumFlexes);
        var headerSize = Marshal.SizeOf<StudioFlex>();
        var flexes = new MeshFlex[headers.Length];

        for (var i = 0; i < headers.Length; i++)
        {
            var start = flexesStart + ((long)i * headerSize);
            flexes[i] = new MeshFlex(headers[i], ReadVertAnims(data, start + headers[i].VertIndex, headers[i]));
        }

        return flexes;
    }

    private FlexVertex[] ReadVertAnims(byte[] data, long start, StudioFlex flex)
    {
        var scale = Hdr.VertAnimFixedPointScale > 0f ? Hdr.VertAnimFixedPointScale : DefaultVertAnimFixedPointScale;

        if (flex.VertAnimType == VertAnimTypeWrinkle)
        {
            var wrinkles = LumpReader<StudioVertAnimWrinkle>.ReadLumpAt(data, start, flex.NumVerts);
            var vertices = new FlexVertex[wrinkles.Length];

            for (var i = 0; i < vertices.Length; i++)
            {
                vertices[i] = ConvertVertAnim(wrinkles[i].Base, wrinkles[i].WrinkleDelta * scale);
            }

            return vertices;
        }

        var anims = LumpReader<StudioVertAnim>.ReadLumpAt(data, start, flex.NumVerts);
        var result = new FlexVertex[anims.Length];

        for (var i = 0; i < result.Length; i++)
        {
            result[i] = ConvertVertAnim(anims[i], 0f);
        }

        return result;
    }

    private static FlexVertex ConvertVertAnim(StudioVertAnim anim, float wrinkle)
    {
        var delta = new Vector3(ToFloat(anim.Delta0), ToFloat(anim.Delta1), ToFloat(anim.Delta2));
        var normalDelta = new Vector3(ToFloat(anim.NormalDelta0), ToFloat(anim.NormalDelta1), ToFloat(anim.NormalDelta2));

        return new FlexVertex(anim.Index, anim.Side / 255f, delta, normalDelta, wrinkle);
    }

    private static float ToFloat(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);

    private const byte VertAnimTypeWrinkle = 1;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioFlexDesc
    {
        public int FacsIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioFlexController
    {
        public int TypeIndex;
        public int NameIndex;
        public int LocalToGlobal;
        public float Min;
        public float Max;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioFlexRule
    {
        public int Flex;
        public int NumOps;
        public int OpIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioFlexOp
    {
        public int Op;
        public int Data;

        public readonly int Index => Data;
        public readonly float Value => BitConverter.Int32BitsToSingle(Data);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioFlex
    {
        public int FlexDesc;
        public float Target0;
        public float Target1;
        public float Target2;
        public float Target3;
        public int NumVerts;
        public int VertIndex;
        public int FlexPair;
        public byte VertAnimType;
        public byte _unused0;
        public byte _unused1;
        public byte _unused2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public int[] _unused;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioVertAnim
    {
        public ushort Index;
        public byte Speed;
        public byte Side;
        public ushort Delta0;
        public ushort Delta1;
        public ushort Delta2;
        public ushort NormalDelta0;
        public ushort NormalDelta1;
        public ushort NormalDelta2;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct StudioVertAnimWrinkle
    {
        public StudioVertAnim Base;
        public short WrinkleDelta;
    }
}
