using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        private const float ElementMassPerUnitLength = 4f;
        private const float RodMassPerUnitLength = 8f;
        private const float VolumetricMassPerUnitExtent = 12f;
        private const float MinVolumetricSolveStrength = 1.1920929e-7f;
        private const float VoronoiAreaScale = 0.125f;
        private const float VoronoiMassWitnessMargin = 1e-4f;

        private const float MinRecoverableMassPaintTerm = 0.05f;
        private const float MaxRecoverableMassPaintTerm = 1e6f;
        private const float NegligibleMassPaint = -30f;
        private const int MassPaintRefineSweeps = 400;
        private const float UniformMassPaintSteps = 16f;

        private const float MassMultiplierTolerance = 1e-3f;
        private const float MotionBiasTolerance = 1e-3f;

        /// <summary>
        /// Recovers the <c>cloth_mass</c> paint of an authored-face proxy sheet from what each node's mass carries beyond
        /// its geometric term, or null when there is none. Under the <see cref="HasVoronoiElementMasses">Voronoi element
        /// mass pass</see> sheets of compiled elements are read too, and vertices without a reading get a negligible paint.
        /// </summary>
        internal float[]? RecoverMassPaint(ProxyMesh proxy)
        {
            if (HasExplicitMasses)
            {
                return null;
            }

            var voronoi = HasVoronoiElementMasses;
            if (!proxy.UsesAuthoredFaces && !voronoi)
            {
                return null;
            }

            var count = proxy.Positions.Length;
            var geometric = voronoi ? VoronoiMasses.Mass : GeometricMasses;
            var paint = new float[count];
            if (voronoi)
            {
                Array.Fill(paint, NegligibleMassPaint);
            }

            var painted = new List<(int Vertex, float Tolerance)>();
            var clamped = 0;

            for (var v = 0; v < count; v++)
            {
                var node = proxy.NodeIndices[v];
                var invMass = InverseMassOf(node);
                if (invMass <= 0f || invMass >= 1f)
                {
                    continue;
                }

                if (node >= geometric.Length || geometric[node] <= 0f)
                {
                    clamped++;
                    continue;
                }

                var residual = 1f / invMass - geometric[node];

                if (residual <= MinRecoverableMassPaintTerm || residual > MaxRecoverableMassPaintTerm)
                {
                    clamped++;
                    continue;
                }

                paint[v] = MathF.Log(residual);
                painted.Add((v, (MathF.BitIncrement(invMass) - invMass) / (invMass * invMass) / residual));
            }

            if (voronoi ? painted.Count == 0 : painted.Count <= clamped)
            {
                return null;
            }

            if (voronoi)
            {
                return PinVoronoiMassPaint(proxy, paint, painted, VoronoiMasses.Bracketed);
            }

            if (UniformMassPaint([.. painted.Select(p => (paint[p.Vertex], p.Tolerance))]) is { } shared)
            {
                foreach (var (vertex, _) in painted)
                {
                    paint[vertex] = shared;
                }
            }
            else
            {
                var vertexOfNode = new Dictionary<int, int>();
                foreach (var (vertex, _) in painted)
                {
                    vertexOfNode[proxy.NodeIndices[vertex]] = vertex;
                }

                paint = RefineMassPaint(paint, painted, FaceRodsBetween(vertexOfNode));
            }

            return paint;
        }

        /// <summary>
        /// Solves the <c>cloth_mass</c> paint of a sheet under the Voronoi element mass pass from its face rods, whose
        /// weights depend only on the endpoints' mass paint, so each states <c>paint[b] - paint[a] = ln(w / (1 - w))</c>.
        /// Each connected set is shifted by the median offset of its readings on nodes outside bracketed elements; a set
        /// without such a reading keeps its readings.
        /// </summary>
        private float[] PinVoronoiMassPaint(ProxyMesh proxy, float[] paint, List<(int Vertex, float Tolerance)> painted,
            HashSet<int> bracketed)
        {
            var vertexOfNode = new Dictionary<int, int>();
            for (var v = 0; v < proxy.NodeIndices.Length; v++)
            {
                var node = proxy.NodeIndices[v];
                if (InverseMassOf(node) is > 0f and < 1f)
                {
                    vertexOfNode[node] = v;
                }
            }

            var neighbours = MassPaintNeighbours(FaceRodsBetween(vertexOfNode), static _ => true);
            var anchors = painted.Select(static p => p.Vertex)
                .Where(vertex => !bracketed.Contains(proxy.NodeIndices[vertex]))
                .ToHashSet();
            var pinned = (float[])paint.Clone();
            foreach (var (component, value) in MassPaintComponents(neighbours, static _ => 0.0))
            {
                RelaxMassPaint(component, neighbours, value, null);
                var offsets = component.Where(anchors.Contains).Select(vertex => paint[vertex] - value[vertex]).Order().ToList();
                if (offsets.Count == 0)
                {
                    continue;
                }

                var offset = offsets[(offsets.Count - 1) / 2];
                foreach (var vertex in component)
                {
                    pinned[vertex] = (float)(value[vertex] + offset);
                }
            }

            return pinned;
        }

        /// <summary>Gets the authored face rods between the nodes of <paramref name="vertexOfNode"/>, as weighted vertex pairs.</summary>
        private List<(int A, int B, float Weight0)> FaceRodsBetween(Dictionary<int, int> vertexOfNode)
        {
            var rods = new List<(int A, int B, float Weight0)>();
            foreach (var (_, _, rod) in AuthoredFaceRods([.. vertexOfNode.Keys]))
            {
                if (vertexOfNode.TryGetValue(rod.NodeA, out var a) && vertexOfNode.TryGetValue(rod.NodeB, out var b))
                {
                    rods.Add((a, b, rod.Weight0));
                }
            }

            return rods;
        }

        /// <summary>
        /// Refines <c>cloth_mass</c> readings with the face rods between painted vertices, each of which states
        /// <c>paint[b] - paint[a] = ln(w / (1 - w))</c>. A connected set keeps its readings unless every refined value
        /// stays within <see cref="UniformMassPaintSteps"/> of them.
        /// </summary>
        internal static float[] RefineMassPaint(float[] paint, IReadOnlyList<(int Vertex, float Tolerance)> painted,
            IReadOnlyList<(int A, int B, float Weight0)> rods)
        {
            var tolerance = new Dictionary<int, float>(painted.Count);
            foreach (var (vertex, step) in painted)
            {
                tolerance[vertex] = step;
            }

            var neighbours = MassPaintNeighbours(rods, tolerance.ContainsKey);
            var refined = (float[])paint.Clone();
            foreach (var (component, value) in MassPaintComponents(neighbours, start => paint[start]))
            {
                RelaxMassPaint(component, neighbours, value, component.Average(vertex => (double)paint[vertex]));
                if (component.TrueForAll(vertex =>
                    Math.Abs(value[vertex] - paint[vertex]) <= UniformMassPaintSteps * tolerance[vertex]))
                {
                    foreach (var vertex in component)
                    {
                        refined[vertex] = (float)value[vertex];
                    }
                }
            }

            return refined;
        }

        /// <summary>
        /// Gets each painted vertex's face-rod neighbours with the paint difference <c>ln(w / (1 - w))</c> the rod states.
        /// </summary>
        private static Dictionary<int, List<(int Other, double Difference)>> MassPaintNeighbours(
            IEnumerable<(int A, int B, float Weight0)> rods, Func<int, bool> painted)
        {
            var neighbours = new Dictionary<int, List<(int Other, double Difference)>>();
            foreach (var (a, b, weight) in rods)
            {
                if (a == b || !painted(a) || !painted(b) || !float.IsFinite(weight) || weight <= 0f || weight >= 1f)
                {
                    continue;
                }

                var difference = Math.Log(weight / (1.0 - weight));
                GetOrAdd(neighbours, a).Add((b, difference));
                GetOrAdd(neighbours, b).Add((a, -difference));
            }

            return neighbours;
        }

        /// <summary>
        /// Walks each connected set of <paramref name="neighbours"/> from its lowest vertex, giving every vertex the value its
        /// path of rod differences reaches from <paramref name="seed"/>.
        /// </summary>
        private static IEnumerable<(List<int> Component, Dictionary<int, double> Value)> MassPaintComponents(
            Dictionary<int, List<(int Other, double Difference)>> neighbours, Func<int, double> seed)
        {
            var seen = new HashSet<int>();
            foreach (var start in neighbours.Keys.Order())
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                var value = new Dictionary<int, double> { [start] = seed(start) };
                var component = new List<int> { start };
                for (var i = 0; i < component.Count; i++)
                {
                    foreach (var (other, difference) in neighbours[component[i]])
                    {
                        if (seen.Add(other))
                        {
                            value[other] = value[component[i]] + difference;
                            component.Add(other);
                        }
                    }
                }

                yield return (component, value);
            }
        }

        /// <summary>
        /// Relaxes a component's values towards every rod difference at once, keeping their mean at
        /// <paramref name="mean"/> when one is given.
        /// </summary>
        private static void RelaxMassPaint(List<int> component, Dictionary<int, List<(int Other, double Difference)>> neighbours,
            Dictionary<int, double> value, double? mean)
        {
            for (var sweep = 0; sweep < MassPaintRefineSweeps; sweep++)
            {
                var moved = 0.0;
                foreach (var vertex in component)
                {
                    var next = neighbours[vertex].Average(edge => value[edge.Other] - edge.Difference);
                    moved = Math.Max(moved, Math.Abs(next - value[vertex]));
                    value[vertex] = next;
                }

                if (mean is { } anchor)
                {
                    var shift = anchor - component.Average(vertex => value[vertex]);
                    foreach (var vertex in component)
                    {
                        value[vertex] += shift;
                    }
                }

                if (moved < 1e-12)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Gets the median of the <c>cloth_mass</c> readings when every reading lies within
        /// <see cref="UniformMassPaintSteps"/> of its tolerance from it, else null.
        /// </summary>
        internal static float? UniformMassPaint(IReadOnlyList<(float Value, float Tolerance)> readings)
        {
            if (readings.Count < 2)
            {
                return null;
            }

            var sorted = readings.Select(static reading => reading.Value).Order().ToList();
            var median = sorted[(sorted.Count - 1) / 2];
            foreach (var (value, tolerance) in readings)
            {
                if (MathF.Abs(value - median) > UniformMassPaintSteps * tolerance)
                {
                    return null;
                }
            }

            return median;
        }

        /// <summary>
        /// Recovers the authored <c>mass</c> multiplier of a cloth node, or null when it is the default 1 or cannot be read.
        /// </summary>
        internal float? RecoverMassMultiplier(int node)
        {
            var multiplier = HasExplicitMasses ? ExplicitMassOf(node) : MassMultiplierOf(node);
            return multiplier is { } value && MathF.Abs(value - 1f) > MassMultiplierTolerance
                ? value
                : null;
        }

        /// <summary>Gets whether the model was compiled with <c>ClothParams explicit_masses</c>.</summary>
        internal bool HasExplicitMasses => hasExplicitMasses ??= ComputeHasExplicitMasses();

        private bool? hasExplicitMasses;

        private bool ComputeHasExplicitMasses()
        {
            var end = FirstPositionDrivenNode > 0 && FirstPositionDrivenNode <= Fe.NodeInvMasses.Length
                ? FirstPositionDrivenNode
                : Fe.NodeInvMasses.Length;

            bool Simulated(int node) => node >= Fe.StaticNodeCount && node < end && Fe.NodeInvMasses[node] > 0f;

            float? shared = null;
            var uniform = true;
            var simulated = 0;
            for (var node = Fe.StaticNodeCount; node < end; node++)
            {
                var invMass = Fe.NodeInvMasses[node];
                if (invMass <= 0f)
                {
                    continue;
                }

                var mass = 1f / invMass;
                if (invMass < 0.05f || invMass > 20f
                    || MathF.Abs(mass - MathF.Round(mass, 2)) > 1e-3f * MathF.Max(1f, mass))
                {
                    return false;
                }

                if (shared is { } first && MathF.Abs(invMass - first) > 1e-6f * MathF.Max(invMass, first))
                {
                    uniform = false;
                }

                shared ??= invMass;
                simulated++;
            }

            var anyRod = false;
            var unequal = 0;
            var proportional = 0;
            foreach (var rod in Fe.Rods)
            {
                anyRod = true;
                if (!Simulated(rod.NodeA) || !Simulated(rod.NodeB))
                {
                    continue;
                }

                var (a, b) = (Fe.NodeInvMasses[rod.NodeA], Fe.NodeInvMasses[rod.NodeB]);
                if (MathF.Abs(a - b) <= 1e-6f * MathF.Max(a, b))
                {
                    continue;
                }

                unequal++;
                if (MathF.Abs(rod.Weight0 - a / (a + b)) <= 2e-5f)
                {
                    proportional++;
                }
            }

            return simulated > 0 && anyRod && (unequal > 0 ? proportional > 0 : uniform);
        }

        private float? ExplicitMassOf(int node)
            => node >= 0 && node < Fe.NodeInvMasses.Length && Fe.NodeInvMasses[node] > 0f ? 1f / Fe.NodeInvMasses[node] : null;

        /// <summary>
        /// Recovers the authored <c>mass</c> of a chain joint from its node and ring nodes, or null when none can be read
        /// or they disagree.
        /// </summary>
        internal float? RecoverJointMassMultiplier(int joint)
        {
            float? multiplier = null;
            foreach (var node in JointMassNodes(joint))
            {
                if (Fe.IsStatic(node) || (!HasExplicitMasses && InverseMassOf(node) == 1f))
                {
                    continue;
                }

                if ((HasExplicitMasses ? ExplicitMassOf(node) : MassMultiplierOf(node, chainJoint: true))
                    is not { } nodeMultiplier)
                {
                    return null;
                }

                if (multiplier is { } first && MathF.Abs(nodeMultiplier - first) > MassMultiplierTolerance * first)
                {
                    return null;
                }

                multiplier ??= nodeMultiplier;
            }

            return multiplier;
        }

        /// <summary>
        /// Gets the <c>mass</c> a chain joint's row has to state, or null where <paramref name="chainDefault"/> already
        /// covers it.
        /// </summary>
        internal float? RecoverJointMass(int joint, float chainDefault)
            => RecoverJointMassMultiplier(joint) is { } value
                && MathF.Abs(value - chainDefault) > MassMultiplierTolerance * chainDefault
                ? value
                : null;

        /// <summary>Gets the <c>mass</c> shared by more than half of the chain's readable joints, else 1.</summary>
        internal float RecoverChainMassDefault(BoneChain chain)
        {
            var readings = new List<float>();
            foreach (var joint in chain.Joints)
            {
                if (RecoverJointMassMultiplier(joint.Node) is { } value)
                {
                    readings.Add(value);
                }
            }

            var common = 1f;
            var best = 0;
            foreach (var candidate in readings)
            {
                var shared = 0;
                foreach (var value in readings)
                {
                    if (MathF.Abs(value - candidate) <= MassMultiplierTolerance * candidate)
                    {
                        shared++;
                    }
                }

                if (shared > best)
                {
                    (best, common) = (shared, candidate);
                }
            }

            return best * 2 > readings.Count ? common : 1f;
        }

        /// <summary>
        /// Gets the mass multiplier of a node over its geometric mass. A rod endpoint is read against the rod mass pass
        /// only for a chain joint or on a cloth without proxy-sheet nodes.
        /// </summary>
        private float? MassMultiplierOf(int node, bool chainJoint = false)
        {
            if (node < 0 || node >= Fe.NodeInvMasses.Length)
            {
                return null;
            }

            var invMass = Fe.NodeInvMasses[node];
            if (invMass <= 0f || invMass == 1f)
            {
                return null;
            }

            float[] geometric;
            if (!RodEndpoints.Contains(node))
            {
                geometric = GeometricMasses;
            }
            else if (chainJoint || !HasProxyMeshNodes)
            {
                geometric = RodMassPass;
            }
            else
            {
                return null;
            }

            if (node >= geometric.Length || geometric[node] <= 0f)
            {
                return null;
            }

            var ratio = 1f / invMass / geometric[node];
            return ratio > 0f ? MathF.Sqrt(ratio) : null;
        }

        private IEnumerable<int> JointMassNodes(int joint)
        {
            yield return joint;
            if (RingNodesByParent.TryGetValue(joint, out var rings))
            {
                foreach (var node in rings)
                {
                    yield return node;
                }
            }
        }

        /// <summary>Gets the <c>$cc</c> ring nodes under each <see cref="ParentNodeOf"/> parent, in node order.</summary>
        private Dictionary<int, List<int>> RingNodesByParent => ringNodesByParent ??= BuildRingNodesByParent();

        private Dictionary<int, List<int>>? ringNodesByParent;

        private Dictionary<int, List<int>> BuildRingNodesByParent()
        {
            var rings = new Dictionary<int, List<int>>();
            for (var node = 0; node < Fe.CtrlNames.Length; node++)
            {
                if (IsRingNode(node))
                {
                    GetOrAdd(rings, ParentNodeOf(node)).Add(node);
                }
            }

            return rings;
        }

        private float[] GeometricMasses => geometricMasses ??= GeometricNodeMasses();

        private float[]? geometricMasses;

        private float[] RodMassPass => rodMassPass ??= GeometricNodeMassesWithRods();

        private float[]? rodMassPass;

        private bool HasProxyMeshNodes => hasProxyMeshNodes ??= Fe.CtrlNames.Any(static name => name.StartsWith(ProxyNamePrefix, StringComparison.Ordinal));

        private bool? hasProxyMeshNodes;

        private HashSet<int> RodEndpoints => rodEndpoints ??= [.. Fe.Rods.SelectMany(static rod => new[] { rod.NodeA, rod.NodeB })];

        private HashSet<int>? rodEndpoints;

        /// <summary>
        /// Gets the mass the compiler derives from the cloth's geometry per control node: the solve elements, the rods
        /// built from the authored elements that did not stay solve elements, and the volumetric selections.
        /// </summary>
        private float[] GeometricNodeMasses()
        {
            var mass = new float[Fe.InitPosePositions.Length];
            AddElementNodeMasses(mass, MassElements);
            AddAuthoredRodNodeMasses(mass, MassElements);
            AddVolumetricNodeMasses(mass);
            return mass;
        }

        /// <summary>
        /// Gets whether the element mass pass lumped mixed Voronoi areas, as it can on models compiled without
        /// <c>m_SkelParents</c>. A proxy sheet node lighter than its corner-pair element term alone reveals it.
        /// </summary>
        private bool HasVoronoiElementMasses => hasVoronoiElementMasses ??= ComputeHasVoronoiElementMasses();

        private bool? hasVoronoiElementMasses;

        private bool ComputeHasVoronoiElementMasses()
        {
            if (HasCompiledSkelParents)
            {
                return false;
            }

            var term = new float[Fe.InitPosePositions.Length];
            AddElementNodeMasses(term, MassElements);
            for (var node = 0; node < term.Length && node < Fe.NodeInvMasses.Length && node < Fe.CtrlNames.Length; node++)
            {
                var invMass = Fe.NodeInvMasses[node];
                if (invMass > 0f && Fe.CtrlNames[node].StartsWith(ProxyNamePrefix, StringComparison.Ordinal)
                    && 1f / invMass < term[node] * (1f - VoronoiMassWitnessMargin))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the geometric masses under the Voronoi element mass pass: each element's lumped area plus the authored rod
        /// term, with no volumetric term. A quad whose corner cycle may have turned after the mass pass (one with a static
        /// corner, or one merged back from its split halves) takes the mean of both fan diagonals and lists its corners as
        /// bracketed.
        /// </summary>
        private (float[] Mass, HashSet<int> Bracketed) VoronoiMasses => voronoiMasses ??= VoronoiNodeMasses();

        private (float[] Mass, HashSet<int> Bracketed)? voronoiMasses;

        private (float[] Mass, HashSet<int> Bracketed) VoronoiNodeMasses()
        {
            var mass = new float[Fe.InitPosePositions.Length];
            var bracketed = new HashSet<int>();
            var elements = MassElements;
            for (var e = 0; e < elements.Count; e++)
            {
                var element = elements[e];
                if (Array.Exists(element, node => node < 0 || node >= mass.Length))
                {
                    continue;
                }

                if (element[2] == element[3] || (e < Fe.Quads.Length && !Array.Exists(element, Fe.IsStatic)))
                {
                    AddVoronoiFan(mass, element, 1f);
                    continue;
                }

                AddVoronoiFan(mass, element, 0.5f);
                AddVoronoiFan(mass, [element[1], element[2], element[3], element[0]], 0.5f);
                bracketed.UnionWith(element);
            }

            AddAuthoredRodNodeMasses(mass, elements);
            return (mass, bracketed);
        }

        private void AddVoronoiFan(float[] mass, int[] corners, float share)
        {
            AddVoronoiTriangle(mass, corners[0], corners[1], corners[2], share);
            AddVoronoiTriangle(mass, corners[0], corners[2], corners[3], share);
        }

        private void AddVoronoiTriangle(float[] mass, int a, int b, int c, float share)
        {
            if (b == c)
            {
                return;
            }

            var ab = Fe.InitPosePositions[b] - Fe.InitPosePositions[a];
            var ac = Fe.InitPosePositions[c] - Fe.InitPosePositions[a];
            var bc = Fe.InitPosePositions[c] - Fe.InitPosePositions[b];
            var n = Vector3.Cross(ab, ac).Length();
            if (n <= 0f)
            {
                return;
            }

            var cotA = Vector3.Dot(ab, ac) / n;
            var cotB = Vector3.Dot(-ab, bc) / n;
            var cotC = Vector3.Dot(bc, ac) / n;
            if (cotA <= 0f || cotB <= 0f || cotC <= 0f)
            {
                var quarter = n * VoronoiAreaScale * share;
                mass[a] += quarter;
                mass[b] += quarter;
                mass[c] += quarter;
                mass[cotA <= cotB ? (cotA <= cotC ? a : c) : (cotB <= cotC ? b : c)] += quarter;
                return;
            }

            var eAB = ab.LengthSquared() * cotC * VoronoiAreaScale;
            var eAC = ac.LengthSquared() * cotB * VoronoiAreaScale;
            var eBC = bc.LengthSquared() * cotA * VoronoiAreaScale;
            mass[a] += share * (eAB + eAC);
            mass[b] += share * (eBC + eAB);
            mass[c] += share * (eBC + eAC);
        }

        private void AddElementNodeMasses(float[] mass, IReadOnlyList<int[]> elements)
        {
            foreach (var element in elements)
            {
                for (var k = 1; k < 4; k++)
                {
                    for (var j = 0; j < k; j++)
                    {
                        var (a, b) = (element[j], element[k]);
                        if (a == b || a < 0 || b < 0 || a >= mass.Length || b >= mass.Length)
                        {
                            continue;
                        }

                        var term = ElementMassPerUnitLength
                            * Vector3.Distance(Fe.InitPosePositions[a], Fe.InitPosePositions[b]);
                        mass[a] += term;
                        mass[b] += term;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the geometric masses of a cloth with no proxy sheet, where every shipped rod weighs except those the
        /// compiler folded across shared edges or left unbounded.
        /// </summary>
        private float[] GeometricNodeMassesWithRods()
        {
            var mass = new float[Fe.InitPosePositions.Length];
            AddElementNodeMasses(mass, MassElements);
            AddVolumetricNodeMasses(mass);

            var cycles = new List<int[]>();
            foreach (var face in Fe.SourceFaces)
            {
                if (face.Length is 3 or 4)
                {
                    cycles.Add(CompiledElementOrder(face, Fe.IsStatic, RestPositionOf));
                }
            }

            var derived = new HashSet<(int, int)>(SurfaceFanPairs);

            foreach (var cycle in cycles)
            {
                for (var j = 0; j < cycle.Length; j++)
                {
                    var (p, q) = (cycle[j], cycle[(j + 1) % cycle.Length]);
                    var edge = UnorderedPair(p, q);
                    if (RodsByPair.TryGetValue(edge, out var rods) && rods.Count == 1)
                    {
                        derived.Remove(edge);
                    }
                }
            }

            foreach (var rod in Fe.Rods)
            {
                var (a, b) = UnorderedPair(rod.NodeA, rod.NodeB);
                if (b >= mass.Length
                    || rod.MaxDist >= UnboundedRodDistance || (FoldedAfterMass(rod) && derived.Remove((a, b))))
                {
                    continue;
                }

                var term = RodMassPerUnitLength * Vector3.Distance(Fe.InitPosePositions[a], Fe.InitPosePositions[b]);
                mass[a] += term;
                mass[b] += term;
            }

            return mass;

            Vector3 RestPositionOf(int node)
                => node >= 0 && node < Fe.InitPosePositions.Length ? Fe.InitPosePositions[node] : Vector3.Zero;
        }

        /// <summary>Gets the solve elements in the corner order the compiler's fold walk meets them.</summary>
        private List<int[]> FoldWalkSolveElements()
        {
            var rings = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
            for (var node = 0; node < Fe.CtrlNames.Length; node++)
            {
                var name = Fe.CtrlNames[node];
                var index = RingSuffixIndex(name);
                if (index >= 0 && name.StartsWith(RingNodePrefix, StringComparison.Ordinal))
                {
                    var ring = name[..name.LastIndexOf('_')];
                    GetOrAdd(rings, ring)[index] = node;
                }
            }

            Dictionary<int, int>? TwoWideRingOf(int a, int b)
            {
                var name = Fe.CtrlNames[a];
                return RingSuffixIndex(name) >= 0 && rings.TryGetValue(name[..name.LastIndexOf('_')], out var members)
                    && members.Count == 2 && members.ContainsKey(0) && members.ContainsKey(1)
                    && members.ContainsValue(a) && members.ContainsValue(b) && a != b
                    ? members
                    : null;
            }

            var elements = new List<int[]>(MassElements);
            for (var i = 0; i < elements.Count; i++)
            {
                var quad = elements[i];
                if (quad.Length == 4 && quad.Distinct().Count() == 4 && quad.All(node => node >= 0 && node < Fe.CtrlNames.Length)
                    && TwoWideRingOf(quad[0], quad[1]) is not null && TwoWideRingOf(quad[2], quad[3]) is { } far)
                {
                    elements[i] = [quad[0], quad[1], far[1], far[0]];
                }
            }

            return elements;
        }

        /// <summary>
        /// Gets the faces the importer built into rods, in the order and corner order the compiler walks them for its
        /// fold rods. <c>m_SourceElems</c> packs each corner-count group from its end, so every group is read backwards.
        /// </summary>
        private IEnumerable<int[]> SourceElementWalk()
            => Fe.SourceFaces.Where(static face => face.Length == 3).Reverse()
                .Concat(Fe.SourceFaces.Where(static face => face.Length == 4).Reverse());

        /// <summary>
        /// Credits both ends of every distinct corner pair of the authored elements that are not solve elements with
        /// <see cref="RodMassPerUnitLength"/> per unit of rest length.
        /// </summary>
        private void AddAuthoredRodNodeMasses(float[] mass, IReadOnlyList<int[]> elements)
        {
            var solved = new HashSet<(int, int, int, int)>(elements.Count);
            foreach (var element in elements)
            {
                solved.Add(CornerKey(element));
            }

            var unbuilt = UnbuiltFaceDiagonals().ToHashSet();
            var rods = new Dictionary<(int A, int B), float>();
            foreach (var face in Fe.SourceFaces)
            {
                if (face.Length < 3 || solved.Contains(CornerKey(face)))
                {
                    continue;
                }

                for (var i = 0; i < face.Length; i++)
                {
                    for (var j = i + 1; j < face.Length; j++)
                    {
                        var (a, b) = UnorderedPair(face[i], face[j]);
                        if (a == b || a < 0 || b >= mass.Length || unbuilt.Contains((a, b)))
                        {
                            continue;
                        }

                        rods[(a, b)] = Vector3.Distance(Fe.InitPosePositions[a], Fe.InitPosePositions[b]);
                    }
                }
            }

            foreach (var ((a, b), length) in rods)
            {
                var term = RodMassPerUnitLength * length;
                mass[a] += term;
                mass[b] += term;
            }
        }

        private static (int, int, int, int) CornerKey(int[] corners)
        {
            var sorted = corners.Distinct().Order().ToArray();
            return (sorted.Length > 0 ? sorted[0] : -1, sorted.Length > 1 ? sorted[1] : -1,
                sorted.Length > 2 ? sorted[2] : -1, sorted.Length > 3 ? sorted[3] : -1);
        }

        /// <summary>
        /// Gets the elements the mass pass ran over as four corners each, a triangle repeating its last, with every
        /// over-bent quad the compiler split into two triangles merged back.
        /// </summary>
        private IReadOnlyList<int[]> MassElements => massElements ??= BuildMassElements();

        private List<int[]>? massElements;

        private List<int[]> BuildMassElements()
        {
            var elements = new List<int[]>(Fe.Quads.Length + Fe.Tris.Length);
            elements.AddRange(Fe.Quads);

            var (splitQuads, splitHalves) = MergeSplitQuads();
            foreach (var tri in Fe.Tris)
            {
                var key = SortedTriKey(tri);
                if (splitHalves.Contains(key))
                {
                    continue;
                }

                elements.Add(splitQuads.TryGetValue(key, out var quad) ? quad : [tri[0], tri[1], tri[2], tri[2]]);
            }

            return elements;
        }

        /// <summary>
        /// Credits every node a volumetric selection covers with <see cref="VolumetricMassPerUnitExtent"/> per unit of the
        /// summed bounding-box extent of the selection, scaled by the node's weight in it. A selection that is not solved
        /// volumetrically weighs nothing.
        /// </summary>
        private void AddVolumetricNodeMasses(float[] mass)
        {
            foreach (var map in VertexMaps)
            {
                if (map.VolumetricSolveStrength < MinVolumetricSolveStrength)
                {
                    continue;
                }

                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                var covered = false;
                for (var i = 0; i < map.Weights.Length; i++)
                {
                    var node = map.VertexBase + i;
                    if (map.Weights[i] <= 0f || node < 0 || node >= mass.Length)
                    {
                        continue;
                    }

                    min = Vector3.Min(min, Fe.InitPosePositions[node]);
                    max = Vector3.Max(max, Fe.InitPosePositions[node]);
                    covered = true;
                }

                if (!covered)
                {
                    continue;
                }

                var extent = max - min;
                var term = VolumetricMassPerUnitExtent * (extent.X + extent.Y + extent.Z);
                for (var i = 0; i < map.Weights.Length; i++)
                {
                    var node = map.VertexBase + i;
                    if (map.Weights[i] > 0f && node >= 0 && node < mass.Length)
                    {
                        mass[node] += map.Weights[i] * term;
                    }
                }
            }
        }

        /// <summary>
        /// Recovers the <c>motion_bias</c> of a chain joint from the weights of the rods between it and its parent, or
        /// null where they carry no reading.
        /// </summary>
        internal float? GetMotionBias(BoneChainJoint joint)
        {
            if (joint.ParentNode < 0)
            {
                return null;
            }

            var parentMass = InverseMassOf(joint.ParentNode);
            var jointMass = InverseMassOf(joint.Node);
            if (parentMass <= 0f || jointMass <= 0f)
            {
                return null;
            }

            if (!HasExplicitMasses)
            {
                parentMass = 1f / (RecoverJointMassMultiplier(joint.ParentNode) ?? 1f);
                jointMass = 1f / (RecoverJointMassMultiplier(joint.Node) ?? 1f);
            }

            float? bias = null;
            var spanRods = Fe.Rods.Select(static rod => (rod.NodeA, rod.NodeB, rod.Weight0))
                .Concat(Fe.AnimRods.Select(static rod => (rod.NodeA, rod.NodeB, rod.Weight0)));
            foreach (var (nodeA, nodeB, weight0) in spanRods)
            {
                var weight = nodeA == joint.ParentNode && nodeB == joint.Node ? weight0
                    : nodeB == joint.ParentNode && nodeA == joint.Node ? 1f - weight0
                    : float.NaN;
                if (float.IsNaN(weight))
                {
                    continue;
                }

                var reading = weight <= 0f ? 1f : weight >= 1f ? -1f : Bias(weight);
                if (bias is { } seen && MathF.Abs(seen - reading) > MotionBiasTolerance)
                {
                    return null;
                }

                bias ??= reading;
            }

            return bias is { } value && MathF.Abs(value) > MotionBiasTolerance ? value : null;

            float Bias(float weight)
            {
                var ratio = weight * jointMass / ((1f - weight) * parentMass);
                return ratio <= 1f ? 1f - ratio : -(1f - (1f / ratio));
            }
        }
    }
}
