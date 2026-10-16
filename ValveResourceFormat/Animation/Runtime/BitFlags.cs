namespace ValveResourceFormat.Animation;

partial struct BitFlags
{
    public bool IsFlagSet(uint flag)
    {
        return (Flags & flag) != 0;
    }
}
