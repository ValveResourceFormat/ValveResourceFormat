using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropSplineTest
    {
        private const float Tolerance = 1e-3f;

        private static readonly Vector3[] CurvedPoints =
        [
            new(-400f, 0f, 0f),
            new(-200f, 32f, 0f),
            new(200f, -32f, 0f),
            new(400f, 0f, 0f),
        ];

    }
}
