namespace ValveResourceFormat.Source1.Bsp
{
    internal sealed class DisplacementManager(BspFile bsp)
    {
        private readonly Dictionary<int, Displacement> displacements = [];

        public Displacement this[int index]
        {
            get
            {
                if (!displacements.TryGetValue(index, out var displacement))
                {
                    displacement = new Displacement(bsp, index);
                    displacements.Add(index, displacement);
                }

                return displacement;
            }
        }
    }
}
