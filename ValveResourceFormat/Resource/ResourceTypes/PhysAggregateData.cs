using System;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class PhysAggregateData : KeyValuesOrNTRO
    {
        public PhysAggregateData()
        {
        }

        public PhysAggregateData(BlockType type) : base(type, "VPhysXAggregateData_t")
        {
        }
    }
}
