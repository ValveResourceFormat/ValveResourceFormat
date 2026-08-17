using System.Threading.Tasks;
using TUnit.Assertions.Enums;
using ValveResourceFormat.ResourceTypes.SmartProps;

namespace Tests.SmartProp
{
    public class SmartPropExpressionTest
    {
        private static readonly float[] MemberVector = [1f, 2f, 3f];
        private static readonly float[] MemberColor = [0.1f, 0.2f, 0.3f, 0.4f];
        private static readonly string[] MalformedExpressions =
        [
            "", "  ", "((", "1 +", "foo(", "1 2", "a..b", "v.q", "1 @ 2", ")", "?:", "1 ?",
        ];
    }
}
