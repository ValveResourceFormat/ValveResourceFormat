using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// Recovers the <c>cloth_mass</c> paint of an authored-face proxy sheet from what each node's mass carries beyond
        /// its geometric term, or null when the sheet carries none.
        /// </summary>
        internal float[]? RecoverMassPaint(ProxyMesh proxy)
        {
            if (!proxy.UsesAuthoredFaces || HasExplicitMasses)
            {
                return null;
            }

            var count = proxy.Positions.Length;
            var geometric = GeometricNodeMasses();
            var paint = new float[count];
            var painted = new List<(int Vertex, float Tolerance)>();
            var clamped = 0;

            for (var v = 0; v < count; v++)
            {
                var node = proxy.NodeIndices[v];
                var invMass = node >= 0 && node < NodeInvMasses.Length ? NodeInvMasses[node] : 0f;
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

            if (painted.Count <= clamped)
            {
                return null;
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

                var rods = new List<(int A, int B, float Weight0)>();
                foreach (var (_, _, rod) in AuthoredFaceRods([.. vertexOfNode.Keys]))
                {
                    if (vertexOfNode.TryGetValue(rod.NodeA, out var a) && vertexOfNode.TryGetValue(rod.NodeB, out var b))
                    {
                        rods.Add((a, b, rod.Weight0));
                    }
                }

                paint = RefineMassPaint(paint, painted, rods);
            }

            return paint;
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

            var neighbours = new Dictionary<int, List<(int Other, double Difference)>>();
            foreach (var (a, b, weight) in rods)
            {
                if (a == b || !tolerance.ContainsKey(a) || !tolerance.ContainsKey(b) || weight <= 0f || weight >= 1f)
                {
                    continue;
                }

                var difference = Math.Log(weight / (1.0 - weight));
                (neighbours.TryGetValue(a, out var fromA) ? fromA : neighbours[a] = []).Add((b, difference));
                (neighbours.TryGetValue(b, out var fromB) ? fromB : neighbours[b] = []).Add((a, -difference));
            }

            var refined = (float[])paint.Clone();
            var seen = new HashSet<int>();
            foreach (var start in neighbours.Keys.Order())
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                var value = new Dictionary<int, double> { [start] = paint[start] };
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

                var anchor = component.Average(vertex => (double)paint[vertex]);
                for (var sweep = 0; sweep < MassPaintRefineSweeps; sweep++)
                {
                    var moved = 0.0;
                    foreach (var vertex in component)
                    {
                        var next = neighbours[vertex].Average(edge => value[edge.Other] - edge.Difference);
                        moved = Math.Max(moved, Math.Abs(next - value[vertex]));
                        value[vertex] = next;
                    }

                    var shift = anchor - component.Average(vertex => value[vertex]);
                    foreach (var vertex in component)
                    {
                        value[vertex] += shift;
                    }

                    if (moved < 1e-12)
                    {
                        break;
                    }
                }

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

        private const int MassPaintRefineSweeps = 400;

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

        private const float UniformMassPaintSteps = 16f;

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

        /// <summary>
        /// Gets whether the model was compiled with <c>ClothParams explicit_masses</c>.
        /// </summary>
        internal bool HasExplicitMasses => hasExplicitMasses ??= ComputeHasExplicitMasses();

        private bool? hasExplicitMasses;

        private bool ComputeHasExplicitMasses()
        {
            var end = FirstPositionDrivenNode > 0 && FirstPositionDrivenNode <= NodeInvMasses.Length
                ? FirstPositionDrivenNode
                : NodeInvMasses.Length;

            bool Simulated(int node) => node >= StaticNodeCount && node < end && NodeInvMasses[node] > 0f;

            float? shared = null;
            var uniform = true;
            var simulated = 0;
            for (var node = StaticNodeCount; node < end; node++)
            {
                var invMass = NodeInvMasses[node];
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
            foreach (var rod in Rods)
            {
                anyRod = true;
                if (!Simulated(rod.NodeA) || !Simulated(rod.NodeB))
                {
                    continue;
                }

                var (a, b) = (NodeInvMasses[rod.NodeA], NodeInvMasses[rod.NodeB]);
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
            => node >= 0 && node < NodeInvMasses.Length && NodeInvMasses[node] > 0f ? 1f / NodeInvMasses[node] : null;

        /// <summary>
        /// Recovers the authored <c>mass</c> of a chain joint from its node and ring nodes, or null when none can be read
        /// or they disagree.
        /// </summary>
        internal float? RecoverJointMassMultiplier(int joint)
        {
            float? multiplier = null;
            foreach (var node in JointMassNodes(joint))
            {
                if (IsStatic(node) || (!HasExplicitMasses && NodeInvMasses[node] == 1f))
                {
                    continue;
                }

                if ((HasExplicitMasses ? ExplicitMassOf(node) : ChainMassMultiplierOf(node))
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
        /// The <c>mass</c> a chain joint's own row has to state, or null where the chain's
        /// <paramref name="chainDefault"/> already states it and the row may omit the key.
        /// </summary>
        internal float? RecoverJointMass(int joint, float chainDefault)
            => RecoverJointMassMultiplier(joint) is { } value
                && MathF.Abs(value - chainDefault) > MassMultiplierTolerance * chainDefault
                ? value
                : null;

        /// <summary>
        /// Gets the <c>mass</c> shared by more than half of the chain's readable joints, else 1.
        /// </summary>
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

        /// <summary>Gets the mass multiplier of a chain joint's node.</summary>
        private float? ChainMassMultiplierOf(int node) => MassMultiplierOf(node, chainJoint: true);

        /// <summary>
        /// Gets the mass multiplier of a node over its geometric mass. A rod endpoint is read against the rod mass pass
        /// only for a chain joint or on a cloth without proxy-sheet nodes.
        /// </summary>
        private float? MassMultiplierOf(int node, bool chainJoint = false)
        {
            if (node < 0 || node >= NodeInvMasses.Length)
            {
                return null;
            }

            var invMass = NodeInvMasses[node];
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
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                if (CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal) && ParentNodeOf(node) == joint)
                {
                    yield return node;
                }
            }
        }

        private float[] GeometricMasses => geometricMasses ??= GeometricNodeMasses();

        private float[]? geometricMasses;

        private float[] RodMassPass => rodMassPass ??= GeometricNodeMassesWithRods();

        private float[]? rodMassPass;

        private bool HasProxyMeshNodes => hasProxyMeshNodes ??= CtrlNames.Any(static name => name.StartsWith("$cloth_m", StringComparison.Ordinal));

        private bool? hasProxyMeshNodes;

        private HashSet<int> RodEndpoints => rodEndpoints ??= Rods
            .SelectMany(static rod => new[] { rod.NodeA, rod.NodeB })
            .ToHashSet();

        private HashSet<int>? rodEndpoints;

        private const float MassMultiplierTolerance = 1e-3f;

        /// <summary>
        /// Gets the mass the compiler derives from the cloth's geometry per control node: the solve elements, the rods
        /// built from the authored elements that did not stay solve elements, and the volumetric selections.
        /// </summary>
        private float[] GeometricNodeMasses()
        {
            var mass = new float[InitPosePositions.Length];
            var elements = MassElements();
            AddElementNodeMasses(mass, elements);
            AddAuthoredRodNodeMasses(mass, elements);
            AddVolumetricNodeMasses(mass);
            return mass;
        }

        private void AddElementNodeMasses(float[] mass, List<int[]> elements)
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
                            * Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);
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
            var mass = new float[InitPosePositions.Length];
            AddElementNodeMasses(mass, MassElements());
            AddVolumetricNodeMasses(mass);

            var cycles = new List<int[]>();
            foreach (var face in SourceFaces)
            {
                if (face.Length is 3 or 4)
                {
                    cycles.Add(CompiledElementOrder(face, IsStatic, RestPositionOf));
                }
            }

            var derived = PredictBendRods([.. FoldWalkSolveElements(), .. SourceElementWalk()], IsStatic);

            var rodsOnPair = new Dictionary<(int, int), int>();
            foreach (var rod in Rods)
            {
                var pair = UnorderedPair(rod.NodeA, rod.NodeB);
                rodsOnPair[pair] = rodsOnPair.GetValueOrDefault(pair) + 1;
            }

            foreach (var cycle in cycles)
            {
                for (var j = 0; j < cycle.Length; j++)
                {
                    var (p, q) = (cycle[j], cycle[(j + 1) % cycle.Length]);
                    var edge = UnorderedPair(p, q);
                    if (rodsOnPair.GetValueOrDefault(edge) == 1)
                    {
                        derived.Remove(edge);
                    }
                }
            }

            foreach (var rod in Rods)
            {
                var (a, b) = UnorderedPair(rod.NodeA, rod.NodeB);
                if (b >= mass.Length
                    || rod.MaxDist >= UnboundedRodDistance || (FoldedAfterMass(rod) && derived.Remove((a, b))))
                {
                    continue;
                }

                var term = RodMassPerUnitLength * Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);
                mass[a] += term;
                mass[b] += term;
            }

            return mass;

            Vector3 RestPositionOf(int node)
                => node >= 0 && node < InitPosePositions.Length ? InitPosePositions[node] : Vector3.Zero;
        }

        /// <summary>
        /// Gets the solve elements in the corner order the compiler's fold walk meets them.
        /// </summary>
        private List<int[]> FoldWalkSolveElements()
        {
            var rings = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);
            for (var node = 0; node < CtrlNames.Length; node++)
            {
                var name = CtrlNames[node];
                var index = RingSuffixIndex(name);
                if (index >= 0 && name.StartsWith("$cc", StringComparison.Ordinal))
                {
                    var ring = name[..name.LastIndexOf('_')];
                    (rings.TryGetValue(ring, out var members) ? members : rings[ring] = [])[index] = node;
                }
            }

            Dictionary<int, int>? TwoWideRingOf(int a, int b)
            {
                var name = CtrlNames[a];
                return RingSuffixIndex(name) >= 0 && rings.TryGetValue(name[..name.LastIndexOf('_')], out var members)
                    && members.Count == 2 && members.ContainsKey(0) && members.ContainsKey(1)
                    && members.ContainsValue(a) && members.ContainsValue(b) && a != b
                    ? members
                    : null;
            }

            var elements = MassElements();
            for (var i = 0; i < elements.Count; i++)
            {
                var quad = elements[i];
                if (quad.Length == 4 && quad.Distinct().Count() == 4 && quad.All(node => node >= 0 && node < CtrlNames.Length)
                    && TwoWideRingOf(quad[0], quad[1]) is not null && TwoWideRingOf(quad[2], quad[3]) is { } far)
                {
                    elements[i] = [quad[0], quad[1], far[1], far[0]];
                }
            }

            return elements;
        }

        /// <summary>
        /// The faces the importer built into rods, in the order and corner order the compiler walks them for its
        /// fold rods. <c>m_SourceElems</c> packs each corner-count group from its end, so every group is read backwards.
        /// </summary>
        private IEnumerable<int[]> SourceElementWalk()
            => SourceFaces.Where(static face => face.Length == 3).Reverse()
                .Concat(SourceFaces.Where(static face => face.Length == 4).Reverse());

        /// <summary>
        /// Credits both ends of every distinct corner pair of the authored elements that are not solve elements with
        /// <see cref="RodMassPerUnitLength"/> per unit of rest length.
        /// </summary>
        private void AddAuthoredRodNodeMasses(float[] mass, List<int[]> elements)
        {
            var solved = new HashSet<(int, int, int, int)>(elements.Count);
            foreach (var element in elements)
            {
                solved.Add(CornerKey(element));
            }

            var sheetNodes = SheetNodes();

            var unbuilt = UnbuiltFaceDiagonals(sheetNodes, AuthoredFaceRods(sheetNodes)).ToHashSet();
            var rods = new Dictionary<(int A, int B), float>();
            foreach (var face in SourceFaces)
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

                        rods[(a, b)] = Vector3.Distance(InitPosePositions[a], InitPosePositions[b]);
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
        /// The elements the mass pass ran over, each as four corners with a triangle repeating its last
        /// one, rebuilt from the compiled surface by merging every triangle pair the compiler split an
        /// over-bent quad into back into that quad.
        /// </summary>
        private List<int[]> MassElements()
        {
            var elements = new List<int[]>(Quads.Length + Tris.Length);
            elements.AddRange(Quads);

            var (splitQuads, splitHalves) = MergeSplitQuads();
            foreach (var tri in Tris)
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
        /// Credits every node a volumetric selection covers with 12 per unit of the summed bounding-box
        /// extent of that selection's own nodes, scaled by how strongly the node belongs to it. A
        /// selection the solver does not solve volumetrically weighs nothing.
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

                    min = Vector3.Min(min, InitPosePositions[node]);
                    max = Vector3.Max(max, InitPosePositions[node]);
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

        private const float ElementMassPerUnitLength = 4f;

        private const float RodMassPerUnitLength = 8f;

        private const float VolumetricMassPerUnitExtent = 12f;

        private const float MinVolumetricSolveStrength = 1.1920929e-7f;

        private const float MinRecoverableMassPaintTerm = 0.05f;

        private const float MaxRecoverableMassPaintTerm = 1e6f;

        private const float MotionBiasTolerance = 1e-3f;

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
            var spanRods = Rods.Select(static rod => (rod.NodeA, rod.NodeB, rod.Weight0))
                .Concat(AnimRods.Select(static rod => (rod.NodeA, rod.NodeB, rod.Weight0)));
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
