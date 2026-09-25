using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>Recovers each proxy vertex's normal as the local +Z of its rest orientation.</summary>
        internal Vector3[] RecoverRestNormals(ProxyMesh proxy)
        {
            var normals = new Vector3[proxy.Positions.Length];
            for (var v = 0; v < normals.Length; v++)
            {
                var node = v < proxy.NodeIndices.Length ? proxy.NodeIndices[v] : -1;
                var rotation = node >= 0 && node < InitPoseRotations.Length
                    ? InitPoseRotations[node]
                    : Quaternion.Identity;

                var axis = Vector3.Transform(Vector3.UnitZ, rotation);
                normals[v] = axis.LengthSquared() > 1e-12f ? Vector3.Normalize(axis) : Vector3.UnitZ;
            }

            return normals;
        }

        /// <summary>
        /// Gets the rod of each edge and diagonal of the authored faces within <paramref name="nodes"/>: the record on
        /// that pair whose maximum is the endpoints' rest distance.
        /// </summary>
        private List<((int A, int B) Pair, bool Diagonal, Rod Rod)> AuthoredFaceRods(HashSet<int> nodes)
        {
            var byPair = new Dictionary<(int, int), List<Rod>>();
            foreach (var rod in Rods)
            {
                var key = UnorderedPair(rod.NodeA, rod.NodeB);
                (byPair.TryGetValue(key, out var list) ? list : byPair[key] = []).Add(rod);
            }

            var kinds = new Dictionary<(int, int), bool>();
            foreach (var face in SourceFaces)
            {
                if (face.Length is not (3 or 4) || !Array.TrueForAll(face, nodes.Contains))
                {
                    continue;
                }

                for (var i = 0; i < face.Length; i++)
                {
                    Add(face[i], face[(i + 1) % face.Length], false);
                }

                if (face.Length == 4)
                {
                    Add(face[0], face[2], true);
                    Add(face[1], face[3], true);
                }
            }

            var found = new List<((int, int), bool, Rod)>(kinds.Count);
            foreach (var (pair, diagonal) in kinds)
            {
                if (!byPair.TryGetValue(pair, out var candidates))
                {
                    continue;
                }

                var rest = Vector3.Distance(InitPosePositions[pair.Item1], InitPosePositions[pair.Item2]);
                var authored = candidates.Count == 1
                    ? candidates[0]
                    : candidates.MinBy(r => MathF.Abs(r.MaxDist - rest));
                if (MathF.Abs(authored.MaxDist - rest) <= FaceRodRestTolerance * MathF.Max(1f, rest))
                {
                    found.Add((pair, diagonal, authored));
                }
            }

            return found;

            void Add(int x, int y, bool diagonal)
            {
                if (x != y && x >= 0 && y >= 0 && x < InitPosePositions.Length && y < InitPosePositions.Length)
                {
                    kinds[UnorderedPair(x, y)] = diagonal;
                }
            }
        }

        private const float FaceRodRestTolerance = 1e-3f;

        /// <summary>
        /// Solves a per-node paint from pair sums <c>p[a] + p[b] = stated</c>, or null when they contradict each other
        /// or force a value outside <c>[0, <paramref name="upper"/>]</c>.
        /// </summary>
        /// <param name="stated">The pair sums, one per node pair.</param>
        /// <param name="fallback">The value an unpainted vertex has.</param>
        /// <param name="upper">The largest value a vertex may take.</param>
        /// <param name="chooseFree">
        /// Picks a component's free parameter from each node's sign and offset (<c>value = sign * free + offset</c>), or
        /// returns null to take the choice closest to <paramref name="fallback"/>.
        /// </param>
        private static Dictionary<int, float>? SolvePairSumPaint(Dictionary<(int A, int B), float> stated,
            float fallback, float upper,
            Func<IReadOnlyDictionary<int, float>, IReadOnlyDictionary<int, float>, float?>? chooseFree = null)
        {
            var adjacency = new Dictionary<int, List<(int Other, float Sum)>>();
            foreach (var ((a, b), sum) in stated)
            {
                (adjacency.TryGetValue(a, out var na) ? na : adjacency[a] = []).Add((b, sum));
                (adjacency.TryGetValue(b, out var nb) ? nb : adjacency[b] = []).Add((a, sum));
            }

            var solved = new Dictionary<int, float>(adjacency.Count);
            var sign = new Dictionary<int, float>(adjacency.Count);
            var offset = new Dictionary<int, float>(adjacency.Count);
            var component = new List<int>();
            var stack = new Stack<int>();

            foreach (var start in adjacency.Keys)
            {
                if (solved.ContainsKey(start))
                {
                    continue;
                }

                sign.Clear();
                offset.Clear();
                component.Clear();
                sign[start] = 1f;
                offset[start] = 0f;
                component.Add(start);
                stack.Push(start);

                float? pinned = null;
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    foreach (var (other, sum) in adjacency[node])
                    {
                        var otherSign = -sign[node];
                        var otherOffset = sum - offset[node];
                        if (sign.TryGetValue(other, out var known))
                        {
                            if (known != otherSign)
                            {
                                var forced = (otherOffset - offset[other]) / (known - otherSign);
                                if (pinned is { } already && MathF.Abs(already - forced) > PaintSolveTolerance)
                                {
                                    return null;
                                }

                                pinned ??= forced;
                            }
                            else if (MathF.Abs(offset[other] - otherOffset) > PaintSolveTolerance)
                            {
                                return null;
                            }

                            continue;
                        }

                        sign[other] = otherSign;
                        offset[other] = otherOffset;
                        component.Add(other);
                        stack.Push(other);
                    }
                }

                var free = pinned ?? chooseFree?.Invoke(sign, offset)
                    ?? component.Sum(node => sign[node] * (fallback - offset[node])) / component.Count;
                foreach (var node in component)
                {
                    var value = sign[node] * free + offset[node];
                    if (value < -PaintSolveTolerance || value > upper + PaintSolveTolerance)
                    {
                        return null;
                    }

                    solved[node] = Math.Clamp(value, 0f, upper);
                }
            }

            return solved;
        }

        private const float PaintSolveTolerance = 2e-3f;

        /// <summary>Gets the control nodes that are proxy-sheet vertices.</summary>
        private HashSet<int> SheetNodes()
        {
            var sheetNodes = new HashSet<int>();
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (IsProxyMeshNode(node))
                {
                    sheetNodes.Add(node);
                }
            }

            return sheetNodes;
        }

        /// <summary>
        /// Maps a per-node value onto <paramref name="proxy"/>'s vertices, or returns null when no vertex counts as
        /// <paramref name="painted"/>.
        /// </summary>
        internal static float[]? PaintPerVertex(ProxyMesh proxy, Func<int, float> valueOf, Func<float, bool> painted)
        {
            var paint = new float[proxy.NodeIndices.Length];
            var count = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                paint[v] = valueOf(proxy.NodeIndices[v]);
                if (painted(paint[v]))
                {
                    count++;
                }
            }

            return count > 0 ? paint : null;
        }

        /// <summary>
        /// Recovers the <c>cloth_antishrink</c> paint of a proxy sheet from its face rods, or null when it is uniformly
        /// <see cref="SheetAntishrinkDefault"/> or the rods contradict each other.
        /// </summary>
        internal float[]? RecoverAntishrinkPaint(ProxyMesh proxy)
        {
            var nodes = new HashSet<int>(proxy.NodeIndices);
            var stated = new Dictionary<(int A, int B), float>();
            foreach (var (pair, _, rod) in AuthoredFaceRods(nodes))
            {
                if (rod.MaxDist > FaceRodRestTolerance)
                {
                    stated[pair] = 2f * (rod.MinDist / rod.MaxDist);
                }
            }

            if (stated.Count == 0 || SolvePairSumPaint(stated, SheetAntishrinkDefault, 1f) is not { } solved)
            {
                return null;
            }

            return PaintPerVertex(proxy, node => solved.TryGetValue(node, out var value) ? value : SheetAntishrinkDefault,
                static value => MathF.Abs(value - SheetAntishrinkDefault) > PaintSolveTolerance);
        }

        /// <summary>The <c>cloth_antishrink</c> of a proxy-sheet vertex that is not painted.</summary>
        internal const float SheetAntishrinkDefault = 0.75f;

        /// <summary>
        /// Gets the per-node <c>cloth_shear_resistance</c> of the proxy sheets relative to the stiffest face diagonal's
        /// relaxation, or null when every diagonal states one value.
        /// </summary>
        internal (Dictionary<int, float> Paint, float BaseRelaxation)? ShearResistance
        {
            get
            {
                if (!hasShearResistance)
                {
                    shearResistance = SolveShearResistance();
                    hasShearResistance = true;
                }

                return shearResistance;
            }
        }

        private (Dictionary<int, float> Paint, float BaseRelaxation)? shearResistance;

        private bool hasShearResistance;

        private (Dictionary<int, float>, float)? SolveShearResistance()
        {
            var sheetNodes = SheetNodes();

            var faceRods = AuthoredFaceRods(sheetNodes);
            var diagonals = faceRods.Where(static entry => entry.Diagonal).ToList();
            var baseRelaxation = 0f;
            foreach (var (_, _, rod) in diagonals)
            {
                baseRelaxation = MathF.Max(baseRelaxation, UnstretchedRelaxation(rod));
            }

            if (baseRelaxation <= 0f)
            {
                return null;
            }

            var stated = new Dictionary<(int A, int B), float>(diagonals.Count);
            foreach (var (pair, _, rod) in diagonals)
            {
                stated[pair] = 2f * MathF.Cbrt(Math.Clamp(UnstretchedRelaxation(rod) / baseRelaxation, 0f, 1f));
            }

            var unbuilt = new HashSet<int>();
            foreach (var pair in UnbuiltFaceDiagonals(sheetNodes, faceRods))
            {
                stated[pair] = 0f;
                unbuilt.Add(pair.A);
                unbuilt.Add(pair.B);
            }

            float? ZeroAtUnbuilt(IReadOnlyDictionary<int, float> sign, IReadOnlyDictionary<int, float> offset)
            {
                foreach (var node in unbuilt)
                {
                    if (sign.TryGetValue(node, out var nodeSign))
                    {
                        return -offset[node] * nodeSign;
                    }
                }

                return null;
            }

            if (SolvePairSumPaint(stated, 1f, MaxStatedShearResistance, unbuilt.Count > 0 ? ZeroAtUnbuilt : null) is not { } solved
                || solved.Values.All(static value => MathF.Abs(value - 1f) <= PaintSolveTolerance))
            {
                return null;
            }

            return (solved, baseRelaxation);
        }

        /// <summary>
        /// The diagonals of the proxy quads that made their four edges but carry no rod at all along the diagonal. A span between two
        /// static nodes constrains nothing and is never built, so it neither counts against the face nor states anything itself.
        /// </summary>
        private IEnumerable<(int A, int B)> UnbuiltFaceDiagonals(HashSet<int> nodes,
            List<((int A, int B) Pair, bool Diagonal, Rod Rod)> faceRods)
        {
            var edges = faceRods.Where(static entry => !entry.Diagonal).Select(static entry => entry.Pair).ToHashSet();
            var rodPairs = Rods.Select(static rod => UnorderedPair(rod.NodeA, rod.NodeB)).ToHashSet();
            bool BothStatic((int A, int B) pair) => IsStatic(pair.A) && IsStatic(pair.B);
            bool Spans((int, int) pair) => edges.Contains(pair) || BothStatic(pair);

            foreach (var face in SourceFaces)
            {
                if (face.Length != 4 || !Array.TrueForAll(face, nodes.Contains)
                    || !Spans(UnorderedPair(face[0], face[1])) || !Spans(UnorderedPair(face[1], face[2]))
                    || !Spans(UnorderedPair(face[2], face[3])) || !Spans(UnorderedPair(face[3], face[0])))
                {
                    continue;
                }

                foreach (var diagonal in new[] { UnorderedPair(face[0], face[2]), UnorderedPair(face[1], face[3]) })
                {
                    if (!rodPairs.Contains(diagonal) && !BothStatic(diagonal))
                    {
                        yield return diagonal;
                    }
                }
            }
        }

        /// <summary>The largest <c>cloth_shear_resistance</c> a vertex can state.</summary>
        private const float MaxStatedShearResistance = 2f;

        /// <summary>
        /// Recovers the per-vertex <c>cloth_shear_resistance</c> paint of a proxy sheet, or null when the
        /// sheet's diagonals state one uniform value. See <see cref="ShearResistance"/>.
        /// </summary>
        internal float[]? RecoverShearResistancePaint(ProxyMesh proxy)
        {
            if (ShearResistance is not { } shear)
            {
                return null;
            }

            return PaintPerVertex(proxy, node => shear.Paint.TryGetValue(node, out var value) ? value : 1f,
                static value => MathF.Abs(value - 1f) > PaintSolveTolerance);
        }

        /// <summary>
        /// Gets the per-node <c>cloth_stretch</c> of the proxy sheets solved from their face edges, or null where they
        /// state none.
        /// </summary>
        internal Dictionary<int, float>? StretchPaint
        {
            get
            {
                if (!hasStretchPaint)
                {
                    stretchPaint = SolveStretchPaint();
                    hasStretchPaint = true;
                }

                return stretchPaint;
            }
        }

        private Dictionary<int, float>? stretchPaint;

        private bool hasStretchPaint;

        private Dictionary<int, float>? SolveStretchPaint()
        {
            var sheetNodes = SheetNodes();

            var thread = DefaultSurfaceStretch > 0f ? MathF.Exp(-DefaultSurfaceStretch) : 1f;
            var stated = new Dictionary<(int A, int B), float>();
            var diagonals = new List<(int A, int B, float Relaxation)>();
            foreach (var (pair, diagonal, rod) in AuthoredFaceRods(sheetNodes))
            {
                if (!diagonal)
                {
                    stated[pair] = 2f * (1f - MathF.Cbrt(Math.Clamp(rod.RelaxationFactor / thread, 0f, 1f)));
                }
                else
                {
                    diagonals.Add((pair.A, pair.B, rod.RelaxationFactor));
                }
            }

            if (stated.Count == 0
                || SolvePairSumPaint(stated, 0f, MaxStatedStretch, (sign, offset) => DiagonalStretchFree(diagonals, sign, offset)) is not { } solved
                || solved.Values.All(static value => value <= PaintSolveTolerance))
            {
                return null;
            }

            return solved;
        }

        /// <summary>
        /// Reads the free parameter the face edges leave on the stretch paint off the face diagonals, or null unless
        /// diagonals of both colours state one consistent value.
        /// </summary>
        private static float? DiagonalStretchFree(List<(int A, int B, float Relaxation)> diagonals,
            IReadOnlyDictionary<int, float> sign, IReadOnlyDictionary<int, float> offset)
        {
            double aa = 0, ab = 0, bb = 0, ay = 0, by = 0;
            var rows = new List<(double Open, double Colour, double Root)>();
            var colours = new HashSet<float>();
            foreach (var (a, b, relaxation) in diagonals)
            {
                if (relaxation <= 0f || !sign.TryGetValue(a, out var signA) || !sign.TryGetValue(b, out var signB) || signA != signB)
                {
                    continue;
                }

                double open = 1.0 - (0.5 * (offset[a] + offset[b]));
                double colour = -signA;
                var root = Math.Cbrt(relaxation);
                rows.Add((open, colour, root));
                colours.Add(signA);
                aa += open * open;
                ab += open * colour;
                bb += colour * colour;
                ay += open * root;
                by += colour * root;
            }

            var determinant = (aa * bb) - (ab * ab);
            if (colours.Count < 2 || Math.Abs(determinant) < 1e-12)
            {
                return null;
            }

            var factor = ((ay * bb) - (by * ab)) / determinant;
            var shifted = ((by * aa) - (ay * ab)) / determinant;
            if (factor <= 1e-6)
            {
                return null;
            }

            foreach (var (open, colour, root) in rows)
            {
                if (Math.Abs((factor * open) + (shifted * colour) - root) > PaintSolveTolerance * factor)
                {
                    return null;
                }
            }

            return (float)(shifted / factor);
        }

        /// <summary>
        /// The largest <c>cloth_stretch</c> a compiled sheet can state. The compiler clamps the cube of one minus the
        /// endpoints' MEAN, so one vertex may sit above 1 as long as its partner sits below.
        /// </summary>
        private const float MaxStatedStretch = 2f;

        /// <summary>
        /// A sheet rod's relaxation with the recovered <c>cloth_stretch</c> factor taken back out, which is what its
        /// shear terms and the model's own stretch scalars left on it.
        /// </summary>
        private float UnstretchedRelaxation(Rod rod)
        {
            if (StretchPaint is not { } paint)
            {
                return rod.RelaxationFactor;
            }

            var open = 1f - (0.5f * (paint.GetValueOrDefault(rod.NodeA) + paint.GetValueOrDefault(rod.NodeB)));
            var factor = Math.Clamp(open * open * open, 0f, 1f);
            return factor > 0f ? Math.Min(1f, rod.RelaxationFactor / factor) : rod.RelaxationFactor;
        }

        /// <summary>
        /// Recovers the per-vertex <c>cloth_stretch</c> paint of a proxy sheet, or null when the sheet carries none.
        /// See <see cref="StretchPaint"/>.
        /// </summary>
        internal float[]? RecoverStretchPaint(ProxyMesh proxy)
        {
            if (StretchPaint is not { } byNode)
            {
                return null;
            }

            return PaintPerVertex(proxy, byNode.GetValueOrDefault, static value => value > PaintSolveTolerance);
        }

        /// <summary>
        /// Recovers the <c>cloth_stray_radius</c> paint of a proxy sheet, or null when none of its vertices has one.
        /// Vertices owned by an independent chain are skipped.
        /// </summary>
        internal float[]? RecoverStrayRadiusPaint(ProxyMesh proxy)
        {
            if (AnimStrayRadii.Count == 0)
            {
                return null;
            }

            var chainNodes = IndependentChainCoveredNodes();

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                if (chainNodes.Contains(node))
                {
                    continue;
                }

                if (AnimStrayRadii.TryGetValue(node, out var stray))
                {
                    paint[v] = stray.MaxDistance;
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>
        /// The largest <c>cloth_stray_radius_stretchiness</c> a proxy vertex can carry and keep its
        /// stray radius: at or above it the compiler cancels the radius instead of relaxing it.
        /// </summary>
        private const float MaxProxyStrayStretchiness = 0.9999998f;

        /// <summary>
        /// Recovers the <c>cloth_stray_radius_stretchiness</c> paint of a proxy sheet, or null when none of its vertices
        /// has one. Vertices owned by an independent chain are skipped.
        /// </summary>
        internal float[]? RecoverStrayStretchinessPaint(ProxyMesh proxy)
        {
            if (AnimStrayRadii.Count == 0)
            {
                return null;
            }

            var chainNodes = IndependentChainCoveredNodes();

            var paint = new float[proxy.NodeIndices.Length];
            var painted = 0;
            for (var v = 0; v < paint.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                if (chainNodes.Contains(node) || !AnimStrayRadii.ContainsKey(node))
                {
                    continue;
                }

                var slack = 1f - GetStrayRelaxationFactor(node);
                var squared = slack * slack;
                var stretchiness = squared * squared;
                stretchiness *= stretchiness;
                stretchiness *= stretchiness;
                if (stretchiness > 0f)
                {
                    paint[v] = Math.Min(stretchiness, MaxProxyStrayStretchiness);
                    painted++;
                }
            }

            return painted > 0 ? paint : null;
        }

        /// <summary>
        /// Gets the joints of the <see cref="IndependentBoneChains"/> and the <c>$cc</c> nodes parented to them.
        /// </summary>
        private HashSet<int> IndependentChainCoveredNodes()
        {
            var chainBoneNodes = IndependentBoneChains().SelectMany(static c => c.Joints).Select(static j => j.Node).ToHashSet();
            if (chainBoneNodes.Count == 0)
            {
                return chainBoneNodes;
            }

            var covered = new HashSet<int>(chainBoneNodes);
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal) && chainBoneNodes.Contains(ParentNodeOf(node)))
                {
                    covered.Add(node);
                }
            }

            return covered;
        }

        /// <summary>
        /// Gets the authored <c>additional_shear_stretch</c> from the slackest rod between two sheet vertices, or from the
        /// <see cref="ShearResistance"/> base relaxation where the diagonals disagree.
        /// </summary>
        internal float AdditionalShearStretch
        {
            get
            {
                var slackest = float.MaxValue;
                if (ShearResistance is { } shear)
                {
                    slackest = shear.BaseRelaxation;
                }
                else
                {
                    foreach (var rod in Rods)
                    {
                        if (!IsProxyMeshNode(rod.NodeA) || !IsProxyMeshNode(rod.NodeB))
                        {
                            continue;
                        }

                        var relaxation = UnstretchedRelaxation(rod);
                        if (relaxation > 0f && relaxation < slackest)
                        {
                            slackest = relaxation;
                        }
                    }
                }

                if (slackest is float.MaxValue or >= 1f)
                {
                    return 0f;
                }

                return Math.Max(0f, -MathF.Log(slackest) - DefaultSurfaceStretch);
            }
        }
    }
}
