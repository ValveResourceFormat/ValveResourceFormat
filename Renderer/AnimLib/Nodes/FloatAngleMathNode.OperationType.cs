namespace ValveResourceFormat.Renderer.AnimLib;

partial class FloatAngleMathNode
{
    internal enum OperationType : byte
    {
        ClampTo180 = 0,
        ClampTo360 = 1,
        FlipHemisphere = 2,
        FlipHemisphereNegate = 3,
    }
}
