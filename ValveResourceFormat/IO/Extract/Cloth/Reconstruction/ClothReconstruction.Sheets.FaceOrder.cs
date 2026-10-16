using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        private const float DefaultQuadBendTolerance = 0.05f;

        // Rods whose inverse-mass sum is at or below this are dropped by the rod importer.
        private const float RodMassFloor = 1e-6f;

        internal static readonly Comparer<int[]> ShippedNodeComparer = Comparer<int[]>.Create(static (x, y) =>
        {
            for (var i = 0; i < x.Length && i < y.Length; i++)
            {
                if (x[i] != y[i])
                {
                    return x[i].CompareTo(y[i]);
                }
            }

            return x.Length.CompareTo(y.Length);
        });

        private float? quadBendTolerance;
        private int[]? surfaceNodeRanks;
        private (Dictionary<(int, int, int), int[]> Quads, HashSet<(int, int, int)> Halves)? splitQuads;

        /// <summary>
        /// Gets the <c>quad_bend_tolerance</c> the compiler split quads against: 0.05, unless a split quad bends by less,
        /// then the largest bend among the dynamic quads kept whole, or 0.
        /// </summary>
        internal float QuadBendTolerance => quadBendTolerance ??= ComputeQuadBendTolerance();

        /// <summary>
        /// Gets each node's BFS layer from the static set over the surface, the rank the compiler lays the dynamic
        /// node block out by.
        /// </summary>
        private int[] SurfaceNodeRanks => surfaceNodeRanks ??= BuildSurfaceNodeRanks();

        private float ComputeQuadBendTolerance()
        {
            bool Dynamic(int node) => node >= 0 && node < Index.InitPosePositions.Length && node < Fe.NodeInvMasses.Length
                && Fe.NodeInvMasses[node] != 0f;

            var rigid = new HashSet<(int, int)>();
            foreach (var rod in Index.Rods)
            {
                if (rod.MinDist == rod.MaxDist)
                {
                    rigid.Add(rod.Pair);
                }
            }

            var inPlace = new Dictionary<(int, int), List<int[]>>();
            var lowestSplit = float.MaxValue;
            foreach (var tri in Index.Tris)
            {
                if (tri.Length != 3)
                {
                    continue;
                }

                if (inPlace.TryGetValue((tri[0], tri[1]), out var firsts))
                {
                    foreach (var first in firsts)
                    {
                        int[] quad = [first[0], first[1], first[2], tri[2]];
                        var far = UnorderedPair(quad[1], quad[3]);
                        if (quad.Distinct().Count() == 4 && Array.TrueForAll(quad, Dynamic) && rigid.Contains(far))
                        {
                            lowestSplit = MathF.Min(lowestSplit, QuadBendSine(quad));
                        }
                    }
                }

                GetOrAdd(inPlace, (tri[0], tri[2])).Add(tri);
            }

            if (lowestSplit > DefaultQuadBendTolerance)
            {
                return DefaultQuadBendTolerance;
            }

            var highestKept = 0f;
            foreach (var quad in Index.Quads)
            {
                if (quad.Length == 4 && quad.Distinct().Count() == 4 && Array.TrueForAll(quad, Dynamic))
                {
                    highestKept = MathF.Max(highestKept, QuadBendSine(quad));
                }
            }

            return highestKept < lowestSplit ? highestKept : DefaultQuadBendTolerance;
        }

        /// <summary>Gets the first four corners of a face, the ones the compiler keeps of a wider polygon.</summary>
        private static int[] FirstCorners(int[] face) => face.Length > 4 ? face[..4] : face;

        private static (int, int, int) SortedTriKey(int[] tri)
        {
            var (a, b, c) = (tri[0], tri[1], tri[2]);
            if (a > b)
            {
                (a, b) = (b, a);
            }

            if (b > c)
            {
                (b, c) = (c, b);
            }

            if (a > b)
            {
                (a, b) = (b, a);
            }

            return (a, b, c);
        }

        /// <summary>
        /// Pairs the <see cref="FeModelIndex.Tris"/> the compiler split from bent quads back into those quads, keyed by
        /// <see cref="SortedTriKey"/>: the quad to export instead of the half that stayed in place, and the appended half to
        /// drop. Callers only read the result, which is computed once.
        /// </summary>
        private (Dictionary<(int, int, int), int[]> Quads, HashSet<(int, int, int)> Halves) MergeSplitQuads()
            => splitQuads ??= ComputeSplitQuads();

        private (Dictionary<(int, int, int), int[]> Quads, HashSet<(int, int, int)> Halves) ComputeSplitQuads()
        {
            var quads = new Dictionary<(int, int, int), int[]>();
            var halves = new HashSet<(int, int, int)>();
            if (Index.Tris.Length < 2 || Index.InitPosePositions.Length == 0)
            {
                return (quads, halves);
            }

            bool Usable(int node) => node >= 0 && node < Index.InitPosePositions.Length
                && node < Fe.NodeInvMasses.Length && !IsHingeRegeneratedProxy(node);

            var keys = new (int, int, int)[Index.Tris.Length];
            var ambiguous = new HashSet<(int, int, int)>();
            var distinct = new HashSet<(int, int, int)>();
            var byEdge = new Dictionary<(int, int), List<int>>();

            for (var i = 0; i < Index.Tris.Length; i++)
            {
                var tri = Index.Tris[i];
                if (tri.Length != 3)
                {
                    continue;
                }

                keys[i] = SortedTriKey(tri);
                if (!distinct.Add(keys[i]))
                {
                    ambiguous.Add(keys[i]);
                }

                if (!Array.TrueForAll(tri, Usable))
                {
                    continue;
                }

                for (var a = 0; a < 3; a++)
                {
                    for (var b = a + 1; b < 3; b++)
                    {
                        GetOrAdd(byEdge, UnorderedPair(tri[a], tri[b])).Add(i);
                    }
                }
            }

            var pairs = new List<(int InPlace, int Appended, int[] Quad)>();
            foreach (var (edge, sharing) in byEdge)
            {
                for (var x = 0; x < sharing.Count; x++)
                {
                    for (var y = x + 1; y < sharing.Count; y++)
                    {
                        var (inPlace, appended) = (sharing[x], sharing[y]);
                        var first = Index.Tris[inPlace];
                        var second = Index.Tris[appended];
                        if (first[0] != second[0] || first[2] != second[1])
                        {
                            continue;
                        }

                        int[] quad = [first[0], first[1], first[2], second[2]];
                        if (quad.Distinct().Count() != 4 || Array.Exists(quad, node => Fe.NodeInvMasses[node] == 0f))
                        {
                            continue;
                        }

                        var corners = Array.ConvertAll(quad, node => Index.InitPosePositions[node]);
                        if (PredictQuadSplit(corners, QuadBendTolerance) is not { } order)
                        {
                            continue;
                        }

                        var (d0, d1) = (quad[order[0]], quad[order[2]]);
                        if (UnorderedPair(d0, d1) != edge)
                        {
                            continue;
                        }

                        pairs.Add((inPlace, appended, quad));
                    }
                }
            }

            var claims = new Dictionary<int, int>();
            foreach (var (inPlace, appended, _) in pairs)
            {
                claims[inPlace] = claims.GetValueOrDefault(inPlace) + 1;
                claims[appended] = claims.GetValueOrDefault(appended) + 1;
            }

            foreach (var (inPlace, appended, quad) in pairs)
            {
                if (claims[inPlace] > 1 || claims[appended] > 1
                    || ambiguous.Contains(keys[inPlace]) || ambiguous.Contains(keys[appended]))
                {
                    continue;
                }

                quads[keys[inPlace]] = quad;
                halves.Add(keys[appended]);
            }

            return (quads, halves);
        }

        /// <summary>
        /// Gets the rods the compiler builds across the discarded diagonal of every fully dynamic quad it splits.
        /// </summary>
        internal static HashSet<(int, int)> BentQuadRodsFromFaces(IEnumerable<int[]> faces,
            Vector3[] positions, Func<int, bool> isStatic, float tolerance)
        {
            var rods = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                if (face.Length != 4 || face.Distinct().Count() != 4
                    || Array.Exists(face, node => node < 0 || node >= positions.Length || isStatic(node)))
                {
                    continue;
                }

                if (PredictQuadSplit(Array.ConvertAll(face, node => positions[node]), tolerance) is not { } order)
                {
                    continue;
                }

                var (a, b) = (face[order[1]], face[order[3]]);
                rods.Add(UnorderedPair(a, b));
            }

            return rods;
        }

        /// <summary>
        /// Gets the corner order the compiler splits a fully dynamic quad in at the given <c>quad_bend_tolerance</c>,
        /// emitting <c>(order[0], order[1], order[2])</c> and <c>(order[0], order[2], order[3])</c>, or null when it is
        /// kept whole.
        /// </summary>
        private static int[]? PredictQuadSplit(Vector3[] corners, float tolerance)
        {
            var bend = QuadBend(corners);
            return bend.Cross > bend.Normals * tolerance ? bend.Order : null;
        }

        /// <summary>
        /// Gets the quad's <see cref="MaximalQuadPairing"/> rotated onto its shorter diagonal, with the two lengths the
        /// compiler's bend test compares: the cross product of the two half normals, and the product of their lengths.
        /// </summary>
        private static (int[] Order, float Cross, float Normals) QuadBend(Vector3[] corners)
        {
            var order = MaximalQuadPairing(corners);
            if (Vector3.Distance(corners[order[0]], corners[order[2]])
                > Vector3.Distance(corners[order[1]], corners[order[3]]))
            {
                order = [order[1], order[2], order[3], order[0]];
            }

            var (a, b, c, d) = (corners[order[0]], corners[order[1]], corners[order[2]], corners[order[3]]);
            var n1 = MathUtils.TriangleCross(a, b, c);
            var n2 = MathUtils.TriangleCross(c, d, a);
            return (order, Vector3.Cross(n1, n2).Length(), n1.Length() * n2.Length());
        }

        /// <summary>Gets the sine of a quad's bend across its shorter diagonal at rest, zero when degenerate.</summary>
        private float QuadBendSine(int[] quad)
        {
            var bend = QuadBend(Array.ConvertAll(quad, node => Index.InitPosePositions[node]));
            return bend.Normals > 0f ? bend.Cross / bend.Normals : 0f;
        }

        private static int[] MaximalQuadPairing(Vector3[] corners)
        {
            int[][] pairings = [[0, 1, 2, 3], [0, 2, 3, 1], [0, 3, 1, 2]];
            var best = pairings[0];
            var bestSpan = -1f;
            foreach (var pairing in pairings)
            {
                var span = Vector3.Cross(corners[pairing[3]] - corners[pairing[1]],
                    corners[pairing[2]] - corners[pairing[0]]).Length();
                if (span > bestSpan)
                {
                    (best, bestSpan) = (pairing, span);
                }
            }

            return best;
        }

        /// <summary>
        /// Rotates each quad with one or two adjacent static corners into the declared corner order whose predicted bend
        /// rods best match the model's rods.
        /// </summary>
        private void RestoreStaticQuadCornerOrder(List<int[]> faces, List<int[]> rodFaces, int[] nodeIndices)
        {
            bool IsStatic(int local) => IsStaticLocal(nodeIndices, local);

            var choices = new List<(int Face, int[][] Orders)>();
            for (var i = 0; i < faces.Count; i++)
            {
                var orders = StaticQuadCornerOrders(faces[i], IsStatic);
                if (orders.Length > 1)
                {
                    choices.Add((i, orders));
                }
            }

            if (choices.Count == 0)
            {
                return;
            }

            var localOf = new Dictionary<int, int>(nodeIndices.Length);
            for (var i = 0; i < nodeIndices.Length; i++)
            {
                localOf[nodeIndices[i]] = i;
            }

            var shipped = new HashSet<(int, int)>();
            foreach (var rod in Index.Rods)
            {
                if (localOf.TryGetValue(rod.NodeA, out var a) && localOf.TryGetValue(rod.NodeB, out var b) && a != b)
                {
                    shipped.Add(UnorderedPair(a, b));
                }
            }

            var elements = new List<int[]>(faces.Count + rodFaces.Count);
            elements.AddRange(faces.Select(face => CompilerCornerCycle(face, IsStatic)));
            elements.AddRange(rodFaces);

            int Disagreement()
            {
                var predicted = PredictBendRods(elements, IsStatic);
                var count = 0;
                foreach (var pair in predicted)
                {
                    if (!shipped.Contains(pair))
                    {
                        count++;
                    }
                }

                foreach (var pair in shipped)
                {
                    if (!predicted.Contains(pair))
                    {
                        count++;
                    }
                }

                return count;
            }

            var chosen = new int[faces.Count];
            for (var pass = 0; pass < 4; pass++)
            {
                var changed = false;
                foreach (var (face, orders) in choices)
                {
                    var best = chosen[face];
                    var bestScore = int.MaxValue;
                    for (var c = 0; c < orders.Length; c++)
                    {
                        elements[face] = CompilerCornerCycle(orders[c], IsStatic);
                        var score = Disagreement();
                        if (score < bestScore || (score == bestScore && c == chosen[face]))
                        {
                            (best, bestScore) = (c, score);
                        }
                    }

                    elements[face] = CompilerCornerCycle(orders[best], IsStatic);
                    if (best != chosen[face])
                    {
                        chosen[face] = best;
                        changed = true;
                    }
                }

                if (!changed)
                {
                    break;
                }
            }

            foreach (var (face, orders) in choices)
            {
                faces[face] = orders[chosen[face]];
            }
        }

        /// <summary>
        /// Gets the corner orders a quad with one or two adjacent static corners can be declared in: its own, then the
        /// ones with its static corners in the middle.
        /// </summary>
        private static int[][] StaticQuadCornerOrders(int[] face, Func<int, bool> isStatic)
        {
            if (face.Length != 4)
            {
                return [face];
            }

            var statics = face.Count(isStatic);
            if (statics is 0 or > 2)
            {
                return [face];
            }

            var start = -1;
            for (var k = 0; k < 4; k++)
            {
                if (isStatic(face[k]) && !isStatic(face[(k + 3) % 4]))
                {
                    start = k;
                }
            }

            if (start < 0 || (statics == 2 && !isStatic(face[(start + 1) % 4])))
            {
                return [face];
            }

            var proper = new[] { face[start], face[(start + 1) % 4], face[(start + 2) % 4], face[(start + 3) % 4] };
            return statics == 2
                ? [face, [proper[3], proper[0], proper[1], proper[2]]]
                : [face, [proper[3], proper[0], proper[1], proper[2]], [proper[2], proper[3], proper[0], proper[1]]];
        }

        /// <summary>
        /// Chooses the order the surface faces are declared in so the importer creates the sheet's nodes in the order the
        /// shipped node array numbers them, and returns that order with its corners rotated.
        /// </summary>
        internal List<int[]> ChooseFaceDeclarationOrder(List<int[]> faces, int surfaceFaceCount, IReadOnlyList<int> nodeIndices)
        {
            var rotationLockedCount = Fe.RotLockStaticNodes;
            bool IsStatic(int local) => IsStaticLocal(nodeIndices, local);

            bool SimulatedAscend(List<int[]> order)
            {
                var created = new List<int>();
                var seen = new HashSet<int>();
                var neighbours = new Dictionary<int, HashSet<int>>();
                foreach (var face in order)
                {
                    foreach (var local in face)
                    {
                        if (!IsStatic(local) && seen.Add(local))
                        {
                            created.Add(local);
                        }
                    }

                    if (!Array.TrueForAll(face, IsStatic))
                    {
                        foreach (var local in face)
                        {
                            GetOrAdd(neighbours, local).UnionWith(face);
                        }
                    }
                }

                var level = new Dictionary<int, int>();
                var queue = new Queue<int>();
                foreach (var local in neighbours.Keys.Where(IsStatic))
                {
                    level[local] = 0;
                    queue.Enqueue(local);
                }

                while (queue.Count > 0)
                {
                    var a = queue.Dequeue();
                    foreach (var b in neighbours[a])
                    {
                        if (level.TryAdd(b, level[a] + 1))
                        {
                            queue.Enqueue(b);
                        }
                    }
                }

                var last = new Dictionary<int, int>();
                foreach (var local in created)
                {
                    var rank = level.GetValueOrDefault(local, int.MaxValue);
                    if (last.TryGetValue(rank, out var previous) && nodeIndices[local] < previous)
                    {
                        return false;
                    }

                    last[rank] = nodeIndices[local];
                }

                return true;
            }

            bool PinsAscend(List<int[]> order)
            {
                var created = new HashSet<int>();
                int[] last = [-1, -1];
                foreach (var face in order)
                {
                    var corners = FirstCorners(face);
                    if (Array.TrueForAll(corners, IsStatic))
                    {
                        continue;
                    }

                    var introduced = new List<int>();
                    foreach (var local in corners)
                    {
                        if (IsStatic(local) && created.Add(nodeIndices[local]))
                        {
                            introduced.Add(nodeIndices[local]);
                        }
                    }

                    introduced.Sort();
                    foreach (var node in introduced)
                    {
                        var group = node < rotationLockedCount ? 0 : 1;
                        if (node < last[group])
                        {
                            return false;
                        }

                        last[group] = node;
                    }
                }

                return true;
            }

            var slots = new List<int>();
            for (var i = 0; i < surfaceFaceCount && i < faces.Count; i++)
            {
                var corners = FirstCorners(faces[i]);
                if (Array.Exists(corners, IsStatic) && !Array.TrueForAll(corners, IsStatic))
                {
                    slots.Add(i);
                }
            }

            var carriers = slots.Select(i => faces[i])
                .OrderBy(face => FirstCorners(face).Where(IsStatic).Min(local => nodeIndices[local]))
                .ToList();
            var byLowestPin = new List<int[]>(faces);
            for (var k = 0; k < slots.Count; k++)
            {
                byLowestPin[slots[k]] = carriers[k];
            }

            var head = Math.Min(surfaceFaceCount, faces.Count);
            var byShippedNodes = faces.Take(head)
                .OrderBy(face => [.. face.Select(local => nodeIndices[local]).Order()], ShippedNodeComparer)
                .Concat(faces.Skip(head))
                .ToList();

            List<int[]>[] candidates = [[.. faces], byShippedNodes, byLowestPin];
            foreach (var order in candidates)
            {
                DeclareFacesInStaticNodeOrder(order, surfaceFaceCount, nodeIndices);
            }

            return Array.Find(candidates, order => PinsAscend(order) && SimulatedAscend(order))
                ?? Array.Find([candidates[0], candidates[2], candidates[1]], PinsAscend)
                ?? candidates[0];
        }

        private bool IsStaticLocal(IReadOnlyList<int> nodeIndices, int local)
            => local >= 0 && local < nodeIndices.Count && Index.IsStatic(nodeIndices[local]);

        /// <summary>
        /// Rotates fully dynamic surface quads one corner back where that makes the compiler's mass pass reproduce the
        /// shipped inverse masses bit for bit, and returns the faces with those rotations.
        /// </summary>
        internal static List<int[]> RotateQuadsToShippedMasses(List<int[]> faces, int surfaceFaceCount,
            IReadOnlyList<int> nodeIndices, IReadOnlyList<Vector3> positions, IReadOnlyList<float> invMasses)
        {
            if (faces.Count != surfaceFaceCount)
            {
                return faces;
            }

            bool IsStatic(int local) => nodeIndices[local] >= invMasses.Count || invMasses[nodeIndices[local]] == 0f;
            float SquaredDistance(int a, int b)
            {
                var d = positions[nodeIndices[b]] - positions[nodeIndices[a]];
                return (d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z);
            }

            bool Flippable(int[] face) => face.Length == 4 && face.Distinct().Count() == 4
                && !Array.Exists(face, IsStatic) && SquaredDistance(face[0], face[2]) < SquaredDistance(face[1], face[3]);

            List<int> Created(List<int[]> order)
            {
                var seen = new HashSet<int>();
                var created = new List<int>();
                foreach (var face in order)
                {
                    foreach (var local in face)
                    {
                        if (!IsStatic(local) && seen.Add(local))
                        {
                            created.Add(local);
                        }
                    }
                }

                return created;
            }

            HashSet<int> Missed(List<int[]> order)
            {
                var mass = new float[nodeIndices.Count];
                foreach (var face in order)
                {
                    var corners = FirstCorners(face);
                    if (corners.Length < 3 || Array.TrueForAll(corners, IsStatic))
                    {
                        continue;
                    }

                    var cycle = CompilerCornerCycle(corners, IsStatic);
                    var element = cycle.Length == 4 ? cycle : [cycle[0], cycle[1], cycle[2], cycle[2]];
                    for (var k = 1; k < 4; k++)
                    {
                        for (var j = 0; j < k; j++)
                        {
                            if (element[j] != element[k])
                            {
                                var term = MathF.Sqrt(SquaredDistance(element[j], element[k])) * 4f;
                                mass[element[k]] += term;
                                mass[element[j]] += term;
                            }
                        }
                    }
                }

                var missed = new HashSet<int>();
                foreach (var local in Created(order))
                {
                    if (BitConverter.SingleToInt32Bits(1f / mass[local]) != BitConverter.SingleToInt32Bits(invMasses[nodeIndices[local]]))
                    {
                        missed.Add(local);
                    }
                }

                return missed;
            }

            var missed = Missed(faces);
            var reachable = faces.Where(Flippable).SelectMany(static face => face).ToHashSet();
            if (missed.Count == 0 || !missed.All(reachable.Contains))
            {
                return faces;
            }

            var creation = Created(faces);
            var current = new List<int[]>(faces);
            var flipped = new bool[faces.Count];
            for (var changed = true; changed && missed.Count > 0;)
            {
                changed = false;
                for (var i = 0; i < current.Count && missed.Count > 0; i++)
                {
                    var face = current[i];
                    if (flipped[i] || !Flippable(face) || !Array.Exists(face, missed.Contains))
                    {
                        continue;
                    }

                    current[i] = [face[3], face[0], face[1], face[2]];
                    if (Created(current).SequenceEqual(creation) && Missed(current) is var trialMissed
                        && trialMissed.Count < missed.Count)
                    {
                        (missed, flipped[i], changed) = (trialMissed, true, true);
                    }
                    else
                    {
                        current[i] = face;
                    }
                }
            }

            return missed.Count == 0 ? current : faces;
        }

        /// <summary>
        /// Rotates each surface face so the sheet introduces its static vertices in the order the shipped node array
        /// numbers them.
        /// </summary>
        private void DeclareFacesInStaticNodeOrder(List<int[]> faces, int rotatableFaceCount, IReadOnlyList<int> nodeIndices)
        {
            bool IsStatic(int local) => IsStaticLocal(nodeIndices, local);

            var created = new HashSet<int>();
            for (var i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                var corners = FirstCorners(face);
                if (Array.TrueForAll(corners, IsStatic))
                {
                    continue;
                }

                if (i < rotatableFaceCount && corners.Length == face.Length
                    && RotateToStaticNodeOrder(face, created, nodeIndices, IsStatic) is { } rotated)
                {
                    faces[i] = corners = rotated;
                }

                foreach (var corner in corners)
                {
                    if (IsStatic(corner))
                    {
                        created.Add(corner);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the rotation of a face that introduces its new static corners in ascending node order without changing its
        /// <see cref="CompilerCornerCycle"/>, or null when there is none or none is needed.
        /// </summary>
        private int[]? RotateToStaticNodeOrder(int[] face, HashSet<int> created, IReadOnlyList<int> nodeIndices, Func<int, bool> isStatic)
        {
            var introduced = new List<int>(2);
            foreach (var corner in face)
            {
                if (isStatic(corner) && !created.Contains(corner) && !introduced.Contains(corner))
                {
                    introduced.Add(corner);
                }
            }

            if (introduced.Count < 2)
            {
                return null;
            }

            var rotationLocked = Fe.RotLockStaticNodes;
            if (introduced.Exists(corner => nodeIndices[corner] < rotationLocked != (nodeIndices[introduced[0]] < rotationLocked)))
            {
                return null;
            }

            var wanted = introduced.OrderBy(corner => nodeIndices[corner]).ToArray();
            if (introduced.SequenceEqual(wanted))
            {
                return null;
            }

            var cycle = CompilerCornerCycle(face, isStatic);
            for (var start = 1; start < face.Length; start++)
            {
                var rotated = new int[face.Length];
                for (var k = 0; k < face.Length; k++)
                {
                    rotated[k] = face[(start + k) % face.Length];
                }

                if (!CompilerCornerCycle(rotated, isStatic).SequenceEqual(cycle))
                {
                    continue;
                }

                if (Array.FindAll(rotated, introduced.Contains).Distinct().SequenceEqual(wanted))
                {
                    return rotated;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the corner order a declared face reaches the compiler's element array in, after import canonicalisation
        /// and the mass pass's static-first partition.
        /// </summary>
        private static int[] CompilerCornerCycle(int[] face, Func<int, bool> isStatic)
        {
            var n = face.Length;

            if (n == 4 && face[2] != face[3] && face[1] != face[0])
            {
                if (!isStatic(face[0]) && !isStatic(face[2]) && isStatic(face[1]) && isStatic(face[3]))
                {
                    return [face[3], face[1], face[2], face[0]];
                }

                if (!isStatic(face[1]) && !isStatic(face[3]) && isStatic(face[0]) && isStatic(face[2]))
                {
                    return [face[0], face[2], face[1], face[3]];
                }
            }

            var start = 0;
            for (var k = n - 1; k >= 0; k--)
            {
                if (!isStatic(face[k]))
                {
                    start = (k + 1) % n;
                    break;
                }
            }

            var cycle = new int[n];
            for (var k = 0; k < n; k++)
            {
                cycle[k] = face[(start + k) % n];
            }

            var leading = 0;
            while (leading < n && isStatic(cycle[leading]))
            {
                leading++;
            }

            var trailingStatic = false;
            for (var k = leading; k < n && !trailingStatic; k++)
            {
                trailingStatic = isStatic(cycle[k]);
            }

            if (trailingStatic)
            {
                cycle = [.. cycle.Where(isStatic), .. cycle.Where(corner => !isStatic(corner))];
            }

            return cycle;
        }

        /// <summary>
        /// Gets the corner order the compiler pairs bend rods in: <see cref="CompilerCornerCycle"/>, then the convexity
        /// swap applied to a quad whose two leading corners are static.
        /// </summary>
        private static int[] CompiledElementOrder(int[] face, Func<int, bool> isStatic, Func<int, Vector3> positionOf)
        {
            var cycle = CompilerCornerCycle(face, isStatic);
            if (cycle.Length != 4 || !isStatic(cycle[0]) || !isStatic(cycle[1])
                || isStatic(cycle[2]) || isStatic(cycle[3]))
            {
                return cycle;
            }

            var edge0 = positionOf(cycle[1]) - positionOf(cycle[0]);
            var edge2 = positionOf(cycle[2]) - positionOf(cycle[3]);
            if (Vector3.Dot(edge2, edge0) < 0f)
            {
                (cycle[2], cycle[3]) = (cycle[3], cycle[2]);
            }

            return cycle;
        }

        /// <summary>
        /// Gets the bend rods <c>add_stiffness_rods</c> derives from the first four corners of faces in declared order.
        /// </summary>
        internal static HashSet<(int, int)> BendRodsFromSurface(IEnumerable<int[]> faces, Func<int, bool> isStatic)
            => PredictBendRods([.. faces.Select(FirstCorners)], isStatic);

        /// <summary>
        /// Gets the bend rods the compiler derives from declared faces, each taken in its
        /// <see cref="CompilerCornerCycle"/> order, which decides the corners a hinge pairs.
        /// </summary>
        internal static HashSet<(int, int)> BendRodsFromDeclaredFaces(IEnumerable<int[]> faces, Func<int, bool> isStatic)
            => PredictBendRods([.. faces.Select(face => CompilerCornerCycle(FirstCorners(face), isStatic))], isStatic);

        private static HashSet<(int, int)> PredictBendRods(List<int[]> elements, Func<int, bool> isStatic)
        {
            var rods = new HashSet<(int, int)>();
            foreach (var (_, nodeA, nodeB) in BendRodGenerators(elements))
            {
                if (nodeA != nodeB && !(isStatic(nodeA) && isStatic(nodeB)))
                {
                    rods.Add(UnorderedPair(nodeA, nodeB));
                }
            }

            return rods;
        }

        /// <summary>
        /// Gets every bend rod the compiler derives from a surface as its hinge edge and far corners. An edge pairs with the
        /// earliest open element listing it, same direction first; a third element listing it opens it again.
        /// </summary>
        internal static IEnumerable<((int, int) Hinge, int NodeA, int NodeB)> BendRodGenerators(IEnumerable<int[]> elements)
        {
            var open = new Dictionary<(int, int), (int Near, int Far)>();
            foreach (var e in elements.Select(FirstCorners))
            {
                var n = e.Length;
                for (var j = 0; j < n; j++)
                {
                    var n0 = e[j];
                    var n1 = e[(j + 1) % n];
                    var n2 = n == 3 ? e[(j + 2) % 3] : e[(j + 2) % 4];
                    var n3 = n == 3 ? n2 : e[(j + 3) % 4];
                    var hinge = UnorderedPair(n0, n1);
                    if (open.Remove((n0, n1), out var same))
                    {
                        yield return (hinge, n2, same.Near);
                        yield return (hinge, n3, same.Far);
                    }
                    else if (open.Remove((n1, n0), out var opposite))
                    {
                        yield return (hinge, n2, opposite.Far);
                        yield return (hinge, n3, opposite.Near);
                    }
                    else
                    {
                        open[(n0, n1)] = (n2, n3);
                    }
                }
            }
        }

        private List<int[]> TakeAuthoredFaces(Dictionary<int, int> localOf, int[] nodeIndices,
            out List<int> truncatedTail)
        {
            truncatedTail = [];
            var faces = new List<int[]>();
            var triangleElements = Index.SourceTriangleCount;
            var triangles = 0;
            for (var i = 0; i < Index.SourceFaces.Length; i++)
            {
                var face = Index.SourceFaces[i];
                if (SpansProxyMeshes(face))
                {
                    continue;
                }

                if (Array.TrueForAll(face, localOf.ContainsKey))
                {
                    faces.Add(face);
                    if (i < triangleElements)
                    {
                        triangles++;
                    }
                }
            }

            if (faces.Count == 0)
            {
                return [];
            }

            var shipped = new HashSet<(int, int)>();
            foreach (var rod in Index.Rods)
            {
                shipped.Add(rod.Pair);
            }

            foreach (var (a, b) in FaceEdges(faces))
            {
                if (!shipped.Contains((a, b)) && InverseMassOf(a) + InverseMassOf(b) > RodMassFloor)
                {
                    return [];
                }
            }

            var covered = new HashSet<int>();
            foreach (var face in faces)
            {
                covered.UnionWith(face);
            }

            if (!AppendTruncatedCorners(faces, nodeIndices, covered, shipped, truncatedTail))
            {
                return [];
            }

            faces.Reverse();
            MergeFacesInNodeCreationOrder(faces, triangles);
            return [.. faces.Select(face => face.Select(corner => localOf[corner]).ToArray())];
        }

        /// <summary>Gets each face's consecutive corner pairs, so never a quad's diagonals.</summary>
        private static HashSet<(int, int)> FaceEdges(IEnumerable<int[]> faces)
        {
            var edges = new HashSet<(int, int)>();
            foreach (var face in faces)
            {
                for (var k = 0; k < face.Length; k++)
                {
                    var (a, b) = (face[k], face[(k + 1) % face.Length]);
                    if (a != b)
                    {
                        edges.Add(UnorderedPair(a, b));
                    }
                }
            }

            return edges;
        }

        /// <summary>
        /// Interleaves the quad and triangle runs of a recovered surface in the authored declaration order, adding a
        /// node as an extra corner of an earlier wider polygon where that polygon must have introduced it.
        /// </summary>
        private void MergeFacesInNodeCreationOrder(List<int[]> faces, int triangleCount)
        {
            var quadCount = faces.Count - triangleCount;
            if (quadCount <= 0 || triangleCount <= 0)
            {
                return;
            }

            var rank = SurfaceNodeRanks;
            if (IntroducesInCompiledOrder(faces, rank))
            {
                return;
            }

            var merged = MergeRuns(faces, quadCount, rank, true, out var extras);
            if (extras > 0 && !IntroducesInCompiledOrder(merged, rank))
            {
                merged = MergeRuns(faces, quadCount, rank, false, out _);
            }

            faces.Clear();
            faces.AddRange(merged);
        }

        private Dictionary<int, List<int>> CompiledRunsByRank(IEnumerable<int[]> faces, int[] rank)
        {
            var runs = new Dictionary<int, List<int>>();
            var covered = new SortedSet<int>();
            foreach (var face in faces)
            {
                foreach (var corner in face)
                {
                    if (corner >= Fe.StaticNodes && corner < rank.Length)
                    {
                        covered.Add(corner);
                    }
                }
            }

            foreach (var node in covered)
            {
                GetOrAdd(runs, rank[node]).Add(node);
            }

            return runs;
        }

        /// <summary>
        /// Gets the pinned nodes the surface introduces, in the order the compiled node array numbers them.
        /// </summary>
        private List<int> CompiledPinnedRun(List<int[]> faces)
            => [.. new SortedSet<int>(faces.SelectMany(PinnedRunCorners))];

        private IEnumerable<int> PinnedRunCorners(int[] face)
        {
            var corners = FirstCorners(face);
            return Array.TrueForAll(corners, static corner => corner < 0)
                || Array.TrueForAll(corners, corner => corner < Fe.RotLockStaticNodes)
                ? []
                : corners.Where(corner => corner >= 0 && corner < Fe.RotLockStaticNodes);
        }

        /// <summary>
        /// Gets whether walking the faces introduces nodes in compiled node order, both the simulated nodes per rank
        /// (each face's corners taken as a set) and the rotation-locked pinned nodes as one run.
        /// </summary>
        private bool IntroducesInCompiledOrder(List<int[]> faces, int[] rank)
        {
            var pending = CompiledRunsByRank(faces, rank);
            var pinned = CompiledPinnedRun(faces);
            var seen = new HashSet<int>();
            var seenPinned = new HashSet<int>();
            foreach (var face in faces)
            {
                var fresh = FreshRankedCorners(face, rank, seen);
                var cursor = new Dictionary<int, int>(2);
                foreach (var node in fresh)
                {
                    var run = pending[rank[node]];
                    var from = cursor.GetValueOrDefault(rank[node]);
                    if (from >= run.Count || run[from] != node)
                    {
                        return false;
                    }

                    cursor[rank[node]] = from + 1;
                }

                var freshPinned = PinnedRunCorners(face).Where(corner => seenPinned.Add(corner)).ToList();
                if (!LeadsPinnedRun(pinned, freshPinned))
                {
                    return false;
                }

                foreach (var corner in freshPinned)
                {
                    pinned.Remove(corner);
                }

                foreach (var corner in fresh)
                {
                    seen.Add(corner);
                    pending[rank[corner]].Remove(corner);
                }
            }

            return true;
        }

        /// <summary>Gets whether <paramref name="fresh"/> is empty or exactly the head of the pending pinned run.</summary>
        private static bool LeadsPinnedRun(List<int> pinned, List<int> fresh)
            => fresh.Count == 0 || (pinned.Count >= fresh.Count && pinned.Take(fresh.Count).ToHashSet().SetEquals(fresh));

        /// <summary>Gets the dynamic ranked corners of a face not yet in <paramref name="seen"/>.</summary>
        private List<int> FreshRankedCorners(int[] face, int[] rank, HashSet<int> seen)
        {
            var fresh = new List<int>(4);
            foreach (var corner in face)
            {
                if (corner >= Fe.StaticNodes && corner < rank.Length && !seen.Contains(corner) && !fresh.Contains(corner))
                {
                    fresh.Add(corner);
                }
            }

            return fresh;
        }

        private List<int[]> MergeRuns(List<int[]> faces, int quadCount, int[] rank, bool allowExtraCorners,
            out int extraCorners)
        {
            var pending = CompiledRunsByRank(faces, rank);
            var pinned = CompiledPinnedRun(faces);
            var created = new HashSet<int>();
            var createdPinned = new HashSet<int>();
            var placedAt = new Dictionary<int, int>();
            var merged = new List<int[]>(faces.Count);
            var quad = 0;
            var triangle = quadCount;
            var lastWide = -1;
            extraCorners = 0;

            List<int> Introduced(int face) => FreshRankedCorners(faces[face], rank, created);

            List<int> IntroducedPinned(int face)
                => [.. PinnedRunCorners(faces[face]).Where(corner => !createdPinned.Contains(corner)).Distinct()];

            bool IsNextPinned(int face) => LeadsPinnedRun(pinned, IntroducedPinned(face));

            List<int>? Preceding(List<int> fresh)
            {
                var cursor = new Dictionary<int, int>(2);
                var missing = new List<int>();
                foreach (var node in fresh)
                {
                    if (missing.Contains(node))
                    {
                        continue;
                    }

                    if (!pending.TryGetValue(rank[node], out var run))
                    {
                        return null;
                    }

                    var from = cursor.GetValueOrDefault(rank[node]);
                    var at = run.IndexOf(node, from);
                    if (at < 0)
                    {
                        return null;
                    }

                    if (at > from && lastWide < placedAt.GetValueOrDefault(rank[node], -1))
                    {
                        return null;
                    }

                    for (var k = from; k < at; k++)
                    {
                        missing.Add(run[k]);
                    }

                    cursor[rank[node]] = at + 1;
                }

                return missing;
            }

            void Create(int node, int face)
            {
                created.Add(node);
                pending[rank[node]].Remove(node);
                placedAt[rank[node]] = face;
            }

            while (quad < quadCount || triangle < faces.Count)
            {
                int[] heads = (quad < quadCount, triangle < faces.Count) switch
                {
                    (true, true) => [0, 1],
                    (true, false) => [0],
                    _ => [1],
                };
                int Face(int run) => run == 0 ? quad : triangle;

                var taken = -1;
                var fewest = int.MaxValue;
                foreach (var head in heads)
                {
                    var fresh = Introduced(Face(head));
                    if (fresh.Count > 0 && fresh.Count < fewest && IsNextPinned(Face(head))
                        && Preceding(fresh) is { Count: 0 })
                    {
                        taken = head;
                        fewest = fresh.Count;
                    }
                }

                if (taken < 0)
                {
                    foreach (var head in heads)
                    {
                        if (Introduced(Face(head)).Count == 0 && IsNextPinned(Face(head)))
                        {
                            taken = head;
                            break;
                        }
                    }
                }

                if (taken < 0 && allowExtraCorners && lastWide >= 0)
                {
                    List<int>? shortest = null;
                    foreach (var head in heads)
                    {
                        var fresh = Introduced(Face(head));
                        if (fresh.Count == 0 || !IsNextPinned(Face(head)))
                        {
                            continue;
                        }

                        var ahead = Preceding(fresh);
                        if (ahead is not null && ahead.Count > 0
                            && (shortest is null || ahead.Count < shortest.Count))
                        {
                            shortest = ahead;
                            taken = head;
                        }
                    }

                    if (shortest is not null)
                    {
                        merged[lastWide] = [.. merged[lastWide], .. shortest];
                        foreach (var node in shortest)
                        {
                            Create(node, lastWide);
                        }

                        extraCorners += shortest.Count;
                    }
                }

                taken = taken < 0 ? heads[0] : taken;
                var take = Face(taken);
                foreach (var corner in Introduced(take))
                {
                    Create(corner, merged.Count);
                }

                foreach (var corner in IntroducedPinned(take))
                {
                    createdPinned.Add(corner);
                    pinned.Remove(corner);
                }

                merged.Add(faces[take]);
                if (faces[take].Length >= 4)
                {
                    lastWide = merged.Count - 1;
                }

                if (taken == 0)
                {
                    quad++;
                }
                else
                {
                    triangle++;
                }
            }

            return merged;
        }

        private int[] BuildSurfaceNodeRanks()
        {
            var count = Fe.CtrlName.Length;
            var neighbours = new HashSet<int>[count];

            void Link(int a, int b)
            {
                if (a == b || a < 0 || b < 0 || a >= count || b >= count)
                {
                    return;
                }

                (neighbours[a] ??= []).Add(b);
                (neighbours[b] ??= []).Add(a);
            }

            void LinkFace(int[] face)
            {
                for (var i = 0; i < face.Length; i++)
                {
                    for (var j = i + 1; j < face.Length; j++)
                    {
                        Link(face[i], face[j]);
                    }
                }
            }

            foreach (var quad in Index.Quads)
            {
                LinkFace(quad);
            }

            foreach (var tri in Index.Tris)
            {
                LinkFace(tri);
            }

            foreach (var face in Index.SourceFaces)
            {
                LinkFace(face);
            }

            foreach (var (a, b) in Index.SourceSprings)
            {
                Link(a, b);
            }

            var rank = new int[count];
            Array.Fill(rank, int.MaxValue);
            var queue = new Queue<int>();
            for (var node = 0; node < Fe.StaticNodes && node < count; node++)
            {
                rank[node] = 0;
                queue.Enqueue(node);
            }

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var next in neighbours[node] ?? [])
                {
                    if (rank[next] == int.MaxValue)
                    {
                        rank[next] = rank[node] + 1;
                        queue.Enqueue(next);
                    }
                }
            }

            return rank;
        }

        /// <summary>
        /// Appends each unfaced, unrodded vertex past the fourth corner of the nearest quad, the corners the compiler
        /// truncates from a larger polygon. Returns false when a vertex cannot be placed that way.
        /// </summary>
        private bool AppendTruncatedCorners(List<int[]> faces, int[] nodeIndices, HashSet<int> covered,
            HashSet<(int, int)> shipped, List<int> truncatedTail)
        {
            var unfaced = Array.FindAll(nodeIndices, node => !covered.Contains(node));
            if (unfaced.Length == 0)
            {
                return true;
            }

            var roddedNodes = new HashSet<int>();
            foreach (var (a, b) in shipped)
            {
                roddedNodes.Add(a);
                roddedNodes.Add(b);
            }

            var quads = faces.FindAll(static face => face.Length == 4);
            if (quads.Count == 0)
            {
                return false;
            }

            var centre = quads.ConvertAll(face =>
            {
                var sum = Vector3.Zero;
                foreach (var corner in face)
                {
                    sum += Index.InitPosePositions[corner];
                }

                return sum / face.Length;
            });

            var appended = new Dictionary<int[], List<int>>();
            foreach (var node in unfaced)
            {
                if (roddedNodes.Contains(node) || node >= Index.InitPosePositions.Length)
                {
                    return false;
                }

                var nearest = 0;
                for (var i = 1; i < quads.Count; i++)
                {
                    if (Vector3.DistanceSquared(Index.InitPosePositions[node], centre[i])
                        < Vector3.DistanceSquared(Index.InitPosePositions[node], centre[nearest]))
                    {
                        nearest = i;
                    }
                }

                GetOrAdd(appended, quads[nearest]).Add(node);
                truncatedTail.Add(node);
            }

            for (var i = 0; i < faces.Count; i++)
            {
                if (appended.TryGetValue(faces[i], out var extra))
                {
                    faces[i] = [.. faces[i], .. extra];
                }
            }

            return true;
        }
    }
}
