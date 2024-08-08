
namespace ValveResourceFormat.CompiledShader;

public interface ICombo
{
    int BlockIndex { get; }
    string Name { get; }
    string Category { get; }
    int Arg0 { get; }
    int RangeMin { get; }
    int RangeMax { get; }
    int Arg3 { get; }
    int FeatureIndex { get; }
    int Arg5 { get; }

    void PrintByteDetail();
}
