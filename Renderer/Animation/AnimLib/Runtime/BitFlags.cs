namespace ValveResourceFormat.Renderer.AnimLib;

partial struct BitFlags
{
    public bool IsFlagSet(uint flag)
    {
        return (Flags & flag) != 0;
    }
}
