using System.Linq;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        private const float AnimatedCopiesTolerance = 1e-3f;
        private const float AnimatedMassTolerance = 1e-4f;
        private const int MaxAnimatedCopies = 64;

        /// <summary>
        /// Reads the springs of ringless <c>animated_length</c> joints, whose spans compile only into <c>m_SimdRodsAnim</c>:
        /// each slider from its lanes' relaxation, and the copies per joint from the node masses. A root pair that is also a
        /// spring target holds a suspender too when it carries two relaxations or the masses weigh it twice.
        /// </summary>
        private void ReadAnimatedSpans(List<BoneChainJoint> joints, Dictionary<int, BoneChainJoint> jointByNode, int rootNode,
            Func<int, List<int>> side, Func<float, float> slider)
        {
            if (joints.Count == 0)
            {
                return;
            }

            var relaxationsByPair = new Dictionary<(int, int), List<float>>();
            foreach (var rod in Index.AnimRods)
            {
                var relaxations = GetOrAdd(relaxationsByPair, rod.Pair);
                if (!relaxations.Exists(rf => MathF.Abs(rf - rod.RelaxationFactor) < ChainReadingTolerance))
                {
                    relaxations.Add(rod.RelaxationFactor);
                }
            }

            int Ancestor(int node, int levels)
            {
                for (var level = 0; level < levels && node >= 0; level++)
                {
                    node = jointByNode.TryGetValue(node, out var joint) ? joint.ParentNode : -1;
                }

                return node;
            }

            float Stiffness(List<float>? relaxations) => relaxations is null ? 0f : slider(relaxations.Max());

            var spans = new List<AnimatedSpans>(joints.Count);
            foreach (var joint in joints)
            {
                int[] targets = [Ancestor(joint.Node, 1), Ancestor(joint.Node, 2), Ancestor(joint.Node, 3)];
                List<float>? Span(int other)
                {
                    if (other < 0)
                    {
                        return null;
                    }

                    var relaxations = new List<float>();
                    foreach (var end in side(other))
                    {
                        if (!relaxationsByPair.TryGetValue(UnorderedPair(joint.Node, end), out var found))
                        {
                            return null;
                        }

                        relaxations.AddRange(found.Where(rf => !relaxations.Exists(seen => MathF.Abs(seen - rf) < ChainReadingTolerance)));
                    }

                    return relaxations.Count > 0 ? relaxations : null;
                }

                var bend = Span(targets[1]);
                var torsion = Span(targets[2]);
                var root = Span(rootNode);
                var rootIsTarget = targets.Contains(rootNode);

                joint.StretchStiffness = Stiffness(Span(targets[0]));
                joint.BendSpring = bend is not null;
                joint.BendStiffness = Stiffness(bend);
                joint.TorsionSpring = torsion is not null;
                joint.TorsionStiffness = Stiffness(torsion);
                joint.Suspender = !rootIsTarget ? Stiffness(root) : root is { Count: 2 } ? slider(root.Min()) : 0f;
                joint.ExtraIterations = 0;

                var pairs = new List<(int Other, bool RootTarget)>();
                foreach (var other in ((int[])[.. targets, rootNode]).Distinct())
                {
                    if (Span(other) is not null)
                    {
                        pairs.AddRange(side(other).Select(end => (end, other == rootNode && rootIsTarget)));
                    }
                }

                spans.Add(new AnimatedSpans(joint, pairs, rootIsTarget && root is { Count: 1 }, rootIsTarget && root is { Count: 2 }));
            }

            if (AnimatedCopies(spans) is not { } solution)
            {
                return;
            }

            for (var i = 0; i < spans.Count; i++)
            {
                var joint = spans[i].Joint;
                joint.ExtraIterations = solution.Copies[i] - 1;
                if (solution.Doubled[i] && spans[i].Pairs.Find(pair => pair.RootTarget) is { RootTarget: true } rootPair
                    && relaxationsByPair.TryGetValue(UnorderedPair(joint.Node, rootPair.Other), out var root))
                {
                    joint.Suspender = slider(root.Min());
                }
            }
        }

        /// <param name="Joint">The joint.</param>
        /// <param name="Pairs">The other end of each of its animated rods, and whether that end is a root spring target.</param>
        /// <param name="MayDouble">Whether its root pair may also hold a suspender at the spring's relaxation.</param>
        /// <param name="Doubled">Whether its root pair carries a second relaxation as a suspender.</param>
        private sealed record AnimatedSpans(BoneChainJoint Joint, List<(int Other, bool RootTarget)> Pairs, bool MayDouble, bool Doubled);

        /// <summary>
        /// Solves the copies per joint, and which root pairs also hold a suspender, that make every joint node's mass
        /// <c>8 * sum(copies * rest length)</c> over its animated rods, preferring the fewest distinct copy counts, then the
        /// fewest doubled root pairs; null where no integer solution matches.
        /// </summary>
        private (int[] Copies, bool[] Doubled)? AnimatedCopies(List<AnimatedSpans> spans)
        {
            var count = spans.Count;
            var rowOf = new Dictionary<int, int>(count);
            var mass = new double[count];
            for (var i = 0; i < count; i++)
            {
                var node = spans[i].Joint.Node;
                var invMass = InverseMassOf(node);
                if (invMass <= 0f || invMass >= 1f || RodEndpoints.Contains(node)
                    || (node < GeometricMasses.Length && GeometricMasses[node] > 0f))
                {
                    return null;
                }

                rowOf[node] = i;
                mass[i] = 1.0 / invMass;
            }

            var optional = Enumerable.Range(0, count).Where(i => spans[i].MayDouble).ToArray();
            ((int, int) Rank, int[] Copies, bool[] Doubled)? best = null;
            for (var mask = 0; mask < 1 << optional.Length; mask++)
            {
                bool[] doubled = [.. spans.Select(static span => span.Doubled)];
                for (var bit = 0; bit < optional.Length; bit++)
                {
                    doubled[optional[bit]] = (mask & (1 << bit)) != 0;
                }

                var matrix = new double[count, count];
                for (var j = 0; j < count; j++)
                {
                    foreach (var (other, rootTarget) in spans[j].Pairs)
                    {
                        var weight = RodMassPerUnitLength * Vector3.Distance(Index.InitPosePositions[spans[j].Joint.Node], Index.InitPosePositions[other])
                            * (rootTarget && doubled[j] ? 2.0 : 1.0);
                        matrix[j, j] += weight;
                        if (rowOf.TryGetValue(other, out var row))
                        {
                            matrix[row, j] += weight;
                        }
                    }
                }

                if (SolveLinear(matrix, mass) is not { } solved)
                {
                    continue;
                }

                var copies = new int[count];
                var fits = true;
                for (var j = 0; j < count && fits; j++)
                {
                    copies[j] = (int)Math.Round(solved[j]);
                    fits = copies[j] is >= 1 and <= MaxAnimatedCopies && Math.Abs(solved[j] - copies[j]) <= AnimatedCopiesTolerance;
                }

                for (var i = 0; i < count && fits; i++)
                {
                    var predicted = 0.0;
                    for (var j = 0; j < count; j++)
                    {
                        predicted += matrix[i, j] * copies[j];
                    }

                    fits = Math.Abs(predicted - mass[i]) <= AnimatedMassTolerance * mass[i];
                }

                if (fits)
                {
                    var rank = (copies.Distinct().Count(), doubled.Count(static d => d));
                    if (best is null || rank.CompareTo(best.Value.Rank) < 0)
                    {
                        best = (rank, copies, doubled);
                    }
                }
            }

            return best is { } found ? (found.Copies, found.Doubled) : null;
        }

        /// <summary>Solves the square system by Gaussian elimination with partial pivoting, or null when it is singular.</summary>
        private static double[]? SolveLinear(double[,] matrix, double[] rhs)
        {
            var n = rhs.Length;
            var a = (double[,])matrix.Clone();
            var b = (double[])rhs.Clone();
            for (var col = 0; col < n; col++)
            {
                var pivot = col;
                for (var row = col + 1; row < n; row++)
                {
                    if (Math.Abs(a[row, col]) > Math.Abs(a[pivot, col]))
                    {
                        pivot = row;
                    }
                }

                if (Math.Abs(a[pivot, col]) < 1e-9)
                {
                    return null;
                }

                if (pivot != col)
                {
                    for (var k = 0; k < n; k++)
                    {
                        (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);
                    }

                    (b[col], b[pivot]) = (b[pivot], b[col]);
                }

                for (var row = 0; row < n; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }

                    var factor = a[row, col] / a[col, col];
                    for (var k = col; k < n; k++)
                    {
                        a[row, k] -= factor * a[col, k];
                    }

                    b[row] -= factor * b[col];
                }
            }

            var x = new double[n];
            for (var i = 0; i < n; i++)
            {
                x[i] = b[i] / a[i, i];
            }

            return x;
        }
    }
}
