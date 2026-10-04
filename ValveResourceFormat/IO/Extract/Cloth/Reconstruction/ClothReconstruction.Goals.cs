using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>The compiled point damping of a node painted with full drag.</summary>
        internal const float ClothDragPointDampingScale = 30f;

        /// <summary>The compiled gravity of a node painted with a gravity scale of 1.</summary>
        internal const float ClothSourceBaseGravity = 360f;

        private const float ClothRawGoalScale = 30f;

        private const float GoalDampingSolveMaxAttraction = 0.9999f;
        private const float GoalDampingSolveMinAttraction = 0.0001f;

        // The bias is the most common cube root gap, rounded to this many steps per unit.
        private const float GoalStrengthBiasQuantum = 10000f;
        private const int GoalStrengthBiasMinNodes = 8;
        private const float GoalStrengthBiasMinShare = 0.5f;
        private const int GoalStrengthBiasMinSupport = 3;

        private const uint NodeFlagGoalAttraction = 0x80;
        private const uint NodeFlagRawForceAttraction = 0x200;
        private const uint NodeFlagRawVertexAttraction = 0x400;

        private float? goalStrengthBias;

        /// <summary>
        /// Recovers the source <c>goal_strength</c> from a compiled <c>flAnimationForceAttraction</c>, which is its cube.
        /// </summary>
        internal static float GoalStrengthFromAttraction(float forceAttraction)
            => MathF.Cbrt(MathUtils.Saturate(forceAttraction));

        /// <summary>
        /// Gets the authored <c>ClothParams.goal_strength_bias</c>: the gap between the cube roots of the force and vertex
        /// attractions shared by most goal-damped nodes, or 0 when no such majority exists.
        /// </summary>
        internal float GoalStrengthBias => goalStrengthBias ??= ComputeGoalStrengthBias();

        private float ComputeGoalStrengthBias()
        {
            var counts = new Dictionary<int, int>();
            var constraining = 0;
            for (var node = 0; node < Fe.NodeCount; node++)
            {
                var integrator = Fe.GetIntegrator(node);
                var fa = integrator.ForceAttraction;
                var va = integrator.VertexAttraction;
                if (fa <= 0f || fa >= 1f || va <= 0f || !UsesGoalDampedIntegrator(node))
                {
                    continue;
                }

                constraining++;
                var gap = (int)MathF.Round((MathF.Cbrt(fa) - MathF.Cbrt(va)) * GoalStrengthBiasQuantum);
                counts[gap] = counts.GetValueOrDefault(gap) + 1;
            }

            if (constraining < GoalStrengthBiasMinNodes)
            {
                return 0f;
            }

            var mode = 0;
            var agreeing = 0;
            foreach (var (gap, count) in counts)
            {
                if (count > agreeing)
                {
                    agreeing = count;
                    mode = gap;
                }
            }

            if (mode > 0 && agreeing >= GoalStrengthBiasMinShare * constraining)
            {
                return mode / GoalStrengthBiasQuantum;
            }

            // Without a majority, fall back to the largest gap when enough nodes land on it or one step below it.
            var top = counts.Keys.Max();
            return top > 0 && counts[top] + counts.GetValueOrDefault(top - 1) >= GoalStrengthBiasMinSupport
                ? top / GoalStrengthBiasQuantum
                : 0f;
        }

        /// <summary>
        /// Gets the <c>cloth_goal_strength_v2</c> paint for a compiled force attraction: its cube root less
        /// <see cref="GoalStrengthBias"/>. A saturated attraction keeps the plain cube root.
        /// </summary>
        internal float GoalStrengthPaint(float forceAttraction)
        {
            if (GoalStrengthBias <= 0f || forceAttraction >= 1f)
            {
                return GoalStrengthFromAttraction(forceAttraction);
            }

            if (forceAttraction <= 0f)
            {
                return -MathF.Cbrt(GoalStrengthBias);
            }

            return MathUtils.Saturate(GoalStrengthFromAttraction(forceAttraction) - GoalStrengthBias);
        }

        /// <summary>
        /// Gets the <c>cloth_goal_damping</c> paint that goes with <see cref="GoalStrengthPaint"/>, solved against the
        /// unbiased goal strength.
        /// </summary>
        internal float GoalDampingPaint(float forceAttraction, float vertexAttraction)
        {
            if (GoalStrengthBias <= 0f || forceAttraction >= 1f)
            {
                return GoalDampingFromAttraction(forceAttraction, vertexAttraction);
            }

            var strength = MathF.Max(GoalStrengthPaint(forceAttraction), 0f);
            return GoalDampingFromAttraction(strength * strength * strength, vertexAttraction);
        }

        /// <summary>
        /// Recovers the source <c>goal_damping</c> by inverting <c>va = 1 - ((1-fa) / (sqrt((1-fa)*fa + d*d) + d))^2 * fa</c>.
        /// </summary>
        internal static float GoalDampingFromAttraction(float forceAttraction, float vertexAttraction)
        {
            if (forceAttraction is >= GoalDampingSolveMaxAttraction or < GoalDampingSolveMinAttraction)
            {
                return MathUtils.Saturate(vertexAttraction);
            }

            var t = MathF.Sqrt(MathUtils.Saturate(1f - vertexAttraction) / forceAttraction);
            if (t <= 0f)
            {
                return 1f;
            }

            var s = (1f - forceAttraction) / t;
            return MathUtils.Saturate((s * s - (1f - forceAttraction) * forceAttraction) / (2f * s));
        }

        /// <summary>
        /// Gets whether <paramref name="node"/> compiled on the goal-damped spring integrator rather than the raw one.
        /// </summary>
        private bool UsesGoalDampedIntegrator(int node)
        {
            if (TryDynamicIndex(node, Fe.GoalDampedSpringIntegrators.Length * 32, out var dynamicIndex))
            {
                return MathUtils.GetBit(Fe.GoalDampedSpringIntegrators, dynamicIndex);
            }

            // Without the per-node bit, fall back to the flags of the node's static or dynamic group.
            var flags = dynamicIndex >= 0 ? Fe.DynamicNodeFlags : Fe.StaticNodeFlags;
            if ((flags & (NodeFlagRawForceAttraction | NodeFlagRawVertexAttraction)) == 0)
            {
                return true;
            }

            if ((flags & NodeFlagGoalAttraction) == 0)
            {
                return false;
            }

            var integrator = Fe.GetIntegrator(node);
            return GoalSolveCanProduce(integrator.ForceAttraction, integrator.VertexAttraction);
        }

        /// <summary>
        /// Gets <paramref name="node"/>'s index among the dynamic nodes, and whether it falls within a per-dynamic-node
        /// array of <paramref name="length"/> entries.
        /// </summary>
        private bool TryDynamicIndex(int node, int length, out int index)
        {
            index = node - Fe.StaticNodeCount;
            return index >= 0 && index < length;
        }

        private static bool GoalSolveCanProduce(float forceAttraction, float vertexAttraction)
        {
            if (forceAttraction is < 0f or > 1f || vertexAttraction is < 0f or > 1f)
            {
                return false;
            }

            return forceAttraction is >= GoalDampingSolveMaxAttraction or < GoalDampingSolveMinAttraction
                || vertexAttraction >= forceAttraction - 1e-4f;
        }

        /// <summary>
        /// Gets the nodes compiled on the raw integrator, or empty when re-authoring the rest as goal-damped would change
        /// whether the dynamic nodes hold both integrator kinds.
        /// </summary>
        private bool[] BuildRawGoalPaintNodes()
        {
            if (Fe.NodeCount <= 0)
            {
                return [];
            }

            var raw = new bool[Fe.NodeCount];
            var any = false;
            for (var node = 0; node < Fe.NodeCount; node++)
            {
                raw[node] = !UsesGoalDampedIntegrator(node);
                any |= raw[node];
            }

            if (!any)
            {
                return [];
            }

            var wasGoal = false;
            var wasRaw = false;
            var staysGoal = false;
            var staysRaw = false;
            for (var node = Math.Max(Fe.StaticNodeCount, 0); node < Fe.NodeCount; node++)
            {
                var integrator = Fe.GetIntegrator(node);
                if (!raw[node])
                {
                    wasGoal |= integrator.ForceAttraction > 0f;
                    staysGoal |= integrator.ForceAttraction > 0f;
                }
                else if (integrator.ForceAttraction != 0f || integrator.VertexAttraction != 0f)
                {
                    // Only proxy sheet nodes can keep raw attraction paints; the rest are re-authored as goal-damped.
                    wasRaw = true;
                    if (IsProxyMeshNode(node))
                    {
                        staysRaw = true;
                    }
                    else
                    {
                        staysGoal |= integrator.ForceAttraction > 0f;
                    }
                }
            }

            return (staysGoal && staysRaw) == (wasGoal && wasRaw) ? raw : [];
        }
    }
}
