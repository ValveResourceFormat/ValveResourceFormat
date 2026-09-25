using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        internal const float ClothDragPointDampingScale = 30f;

        internal const float ClothSourceBaseGravity = 360f;

        private const float GoalDampingSolveMaxAttraction = 0.9999f;

        private const float GoalDampingSolveMinAttraction = 0.0001f;

        /// <summary>
        /// Recovers the source <c>goal_strength</c> from a node's compiled
        /// <c>flAnimationForceAttraction</c>, which the compiler writes as the cube of it.
        /// </summary>
        internal static float GoalStrengthFromAttraction(float forceAttraction)
            => MathF.Cbrt(Math.Clamp(forceAttraction, 0f, 1f));

        /// <summary>
        /// Gets the authored <c>ClothParams.goal_strength_bias</c>: the gap between the cube roots of the force and vertex
        /// attractions shared by most goal-damped nodes, or 0 when no such majority exists.
        /// </summary>
        internal float GoalStrengthBias => goalStrengthBias ??= ComputeGoalStrengthBias();

        private float? goalStrengthBias;

        private const float GoalStrengthBiasQuantum = 10000f;

        private const int GoalStrengthBiasMinNodes = 8;

        private const float GoalStrengthBiasMinShare = 0.5f;

        private float ComputeGoalStrengthBias()
        {
            var counts = new Dictionary<int, int>();
            var constraining = 0;
            for (var node = 0; node < NodeCount; node++)
            {
                var integrator = GetIntegrator(node);
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

            var top = counts.Keys.Max();
            return top > 0 && counts[top] + counts.GetValueOrDefault(top - 1) >= GoalStrengthBiasMinSupport
                ? top / GoalStrengthBiasQuantum
                : 0f;
        }

        private const int GoalStrengthBiasMinSupport = 3;

        /// <summary>
        /// Gets the <c>cloth_goal_strength_v2</c> paint for a compiled force attraction: its cube root less
        /// <see cref="GoalStrengthBias"/>. A saturated attraction keeps the plain cube root.
        /// </summary>
        internal float GoalStrengthPaint(float forceAttraction)
            => GoalStrengthBias <= 0f || forceAttraction >= 1f
                ? GoalStrengthFromAttraction(forceAttraction)
                : forceAttraction <= 0f
                    ? -MathF.Cbrt(GoalStrengthBias)
                    : Math.Clamp(GoalStrengthFromAttraction(forceAttraction) - GoalStrengthBias, 0f, 1f);

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

            var strength = Math.Max(GoalStrengthPaint(forceAttraction), 0f);
            return GoalDampingFromAttraction(strength * strength * strength, vertexAttraction);
        }

        /// <summary>
        /// Recovers the source <c>goal_damping</c> by inverting <c>va = 1 - ((1-fa) / (sqrt((1-fa)*fa + d*d) + d))^2 * fa</c>.
        /// </summary>
        internal static float GoalDampingFromAttraction(float forceAttraction, float vertexAttraction)
        {
            if (forceAttraction is >= GoalDampingSolveMaxAttraction or < GoalDampingSolveMinAttraction)
            {
                return Math.Clamp(vertexAttraction, 0f, 1f);
            }

            var t = MathF.Sqrt(Math.Clamp(1f - vertexAttraction, 0f, 1f) / forceAttraction);
            if (t <= 0f)
            {
                return 1f;
            }

            var s = (1f - forceAttraction) / t;
            return Math.Clamp((s * s - (1f - forceAttraction) * forceAttraction) / (2f * s), 0f, 1f);
        }

        private const float ClothRawGoalScale = 30f;

        private const uint NodeFlagGoalAttraction = 0x80;

        private const uint NodeFlagRawForceAttraction = 0x200;

        private const uint NodeFlagRawVertexAttraction = 0x400;

        /// <summary>
        /// Gets whether <paramref name="node"/> compiled on the goal-damped spring integrator rather than the raw one.
        /// </summary>
        internal bool UsesGoalDampedIntegrator(int node)
        {
            var dynamicIndex = node - StaticNodeCount;
            if (dynamicIndex >= 0 && (dynamicIndex >> 5) < GoalDampedSpringIntegrators.Length)
            {
                return (GoalDampedSpringIntegrators[dynamicIndex >> 5] & (1u << (dynamicIndex & 31))) != 0;
            }

            var flags = dynamicIndex >= 0 ? DynamicNodeFlags : StaticNodeFlags;
            if ((flags & (NodeFlagRawForceAttraction | NodeFlagRawVertexAttraction)) == 0)
            {
                return true;
            }

            if ((flags & NodeFlagGoalAttraction) == 0)
            {
                return false;
            }

            var integrator = GetIntegrator(node);
            return GoalSolveCanProduce(integrator.ForceAttraction, integrator.VertexAttraction);
        }

        /// <summary>Gets whether the goal-damped solve can produce this pair of attractions.</summary>
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
            var raw = new bool[NodeCount];
            var any = false;
            for (var node = 0; node < NodeCount; node++)
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
            for (var node = StaticNodeCount; node < NodeCount; node++)
            {
                var integrator = GetIntegrator(node);
                if (!raw[node])
                {
                    wasGoal |= integrator.ForceAttraction > 0f;
                    staysGoal |= integrator.ForceAttraction > 0f;
                }
                else if (integrator.ForceAttraction != 0f || integrator.VertexAttraction != 0f)
                {
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
