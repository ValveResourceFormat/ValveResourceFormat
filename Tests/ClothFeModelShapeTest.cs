using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;

namespace Tests
{
    /// <summary>Recovering planarized collision shapes from their compiled planes.</summary>
    public class ClothFeModelShapeTest : ClothTestFixtures
    {
        /// <summary>
        /// Six planarized planes around a sphere recover its centre (1, 2, 3) and radius 4.
        /// </summary>
        [Test]
        public async Task PlanarizedSphereRecoversItsCentreAndRadius()
        {
            var feModel = SyntheticCloth.Model(
                ["bone", "n0", "n1", "n2", "n3", "n4", "n5"], staticNodes: 0, parents: [-1, 0, 0, 0, 0, 0, 0],
                poses: [new(0f, 0f, 0f), new(7f, 2f, 3f), new(-5f, 2f, 3f), new(1f, 8f, 3f), new(1f, -4f, 3f), new(1f, 2f, 9f),
                    new(1f, 2f, -3f)],
                body: $$"""
                    m_CollisionPlanes =
                    [
                        {{Plane(1, "1.0, 0.0, 0.0", 5f)}}
                        {{Plane(2, "-1.0, 0.0, 0.0", 3f)}}
                        {{Plane(3, "0.0, 1.0, 0.0", 6f)}}
                        {{Plane(4, "0.0, -1.0, 0.0", 2f)}}
                        {{Plane(5, "0.0, 0.0, 1.0", 7f)}}
                        {{Plane(6, "0.0, 0.0, -1.0", 1f)}}
                    ]
                    m_VertexMapValues = [ 255, 255, 255, 255, 255, 255 ]
                    m_VertexMaps =
                    [
                        {
                            sName = "belt"
                            nNameHash = 1
                            nVertexBase = 1
                            nVertexCount = 6
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                    """);

            var shapes = feModel.BuildPlanarizeCapsules();

            await Assert.That(shapes.Count).IsEqualTo(1);

            using (Assert.Multiple())
            {
                await Assert.That(shapes[0].Planarize).IsTrue();
                await Assert.That(shapes[0].ParentBone).IsEqualTo("bone");
                await Assert.That(shapes[0].VertexMap).IsEqualTo("belt");
                await Assert.That(shapes[0].Radius0).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(shapes[0].Radius1).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(shapes[0].Point0.X).IsEqualTo(1f).Within(1e-3f);
                await Assert.That(shapes[0].Point0.Y).IsEqualTo(2f).Within(1e-3f);
                await Assert.That(shapes[0].Point0.Z).IsEqualTo(3f).Within(1e-3f);
                await Assert.That(Vector3.Distance(shapes[0].Point0, shapes[0].Point1)).IsLessThan(1e-3f);
            }
        }

        private static string Plane(int node, string normal, float offset)
            => $"{{ nCtrlParent = 0 nChildNode = {node} flStickiness = 0.0 flStrength = 0.0 "
                + $"m_Plane = {{ m_vNormal = [ {normal} ] m_flOffset = {SyntheticCloth.Num(offset)} }} }},";

        /// <summary>
        /// A planarized shape whose end caps coincide gets a 0.01 axis pointing away from the nodes it owns: here along
        /// -Z from the recovered centre (1, 2, 3).
        /// </summary>
        [Test]
        public async Task APlanarizedEndCapIsGivenAShortAxisAwayFromItsNodes()
        {
            var feModel = SyntheticCloth.Model(
                ["bone", "n0", "n1", "n2", "n3", "n4"], staticNodes: 0, parents: [-1, 0, 0, 0, 0, 0],
                poses: [new(0f, 0f, 0f), new(7f, 2f, 3f), new(-5f, 2f, 3f), new(1f, 8f, 3f), new(1f, -4f, 3f), new(1f, 2f, 9f)],
                body: $$"""
                    m_CollisionPlanes =
                    [
                        {{Plane(1, "1.0, 0.0, 0.0", 5f)}}
                        {{Plane(2, "-1.0, 0.0, 0.0", 3f)}}
                        {{Plane(3, "0.0, 1.0, 0.0", 6f)}}
                        {{Plane(4, "0.0, -1.0, 0.0", 2f)}}
                        {{Plane(5, "0.0, 0.0, 1.0", 7f)}}
                    ]
                    m_VertexMapValues = [ 255, 255, 255, 255, 255 ]
                    m_VertexMaps =
                    [
                        {
                            sName = "belt"
                            nNameHash = 1
                            nVertexBase = 1
                            nVertexCount = 5
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                    """);

            var shapes = feModel.BuildPlanarizeCapsules();

            await Assert.That(shapes.Count).IsEqualTo(1);

            using (Assert.Multiple())
            {
                await Assert.That(shapes[0].Radius0).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(Vector3.Distance(shapes[0].Point0, new Vector3(1f, 2f, 3f)))
                    .IsLessThan(1e-3f);
                await Assert.That(Vector3.Distance(shapes[0].Point0, shapes[0].Point1))
                    .IsEqualTo(0.01f).Within(1e-4f);
                await Assert.That((shapes[0].Point1 - shapes[0].Point0).Z).IsLessThan(0f);
            }
        }

        /// <summary>
        /// Planarized planes standing off a box's faces, edges and corner recover that box, not a capsule; planes at a
        /// sphere's six axis points recover a capsule instead.
        /// </summary>
        [Test]
        public async Task APlanarizedBoxIsRecoveredFromItsContactPoints()
        {
            var half = new Vector3(2f, 3f, 4f);
            Vector3[] nodes =
            [
                new(6f, 0f, 0f), new(6f, 1f, 1f), new(6f, -1f, -2f), new(0f, 7f, 0f), new(0f, -7f, 0f),
                new(0f, 0f, 9f), new(0f, 0f, -9f), new(6f, 7f, 0f), new(6f, 7f, 9f),
            ];
            var box = PlanarizedGroup([.. nodes.Select(node =>
            {
                var contact = Vector3.Clamp(node, -half, half);
                var normal = Vector3.Normalize(node - contact);
                return (node, normal, Vector3.Dot(normal, contact));
            })]);
            var sphere = PlanarizedGroup(
            [
                (new Vector3(7f, 2f, 3f), Vector3.UnitX, 5f), (new Vector3(-5f, 2f, 3f), -Vector3.UnitX, 3f),
                (new Vector3(1f, 8f, 3f), Vector3.UnitY, 6f), (new Vector3(1f, -4f, 3f), -Vector3.UnitY, 2f),
                (new Vector3(1f, 2f, 9f), Vector3.UnitZ, 7f), (new Vector3(1f, 2f, -3f), -Vector3.UnitZ, 1f),
            ]);

            var boxes = box.BuildPlanarizeBoxes();

            using (Assert.Multiple())
            {
                await Assert.That(box.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(boxes.Count).IsEqualTo(1);
                await Assert.That(sphere.BuildPlanarizeBoxes()).IsEmpty();
                await Assert.That(sphere.BuildPlanarizeCapsules().Count).IsEqualTo(1);
            }

            using (Assert.Multiple())
            {
                await Assert.That(boxes[0].Planarize).IsTrue();
                await Assert.That(boxes[0].VertexMap).IsEqualTo("belt");
                await Assert.That(boxes[0].Origin.Length()).IsLessThan(1e-3f);
                await Assert.That(Vector3.Distance(boxes[0].Size, half)).IsLessThan(1e-3f);
            }
        }

        private static FeModel PlanarizedGroup(IReadOnlyList<(Vector3 Node, Vector3 Normal, float Offset)> planes)
        {
            var count = planes.Count;
            var names = string.Join(", ", Enumerable.Range(0, count).Select(static i => $"\"n{i}\""));
            var poses = string.Concat(planes.Select(static p => SyntheticCloth.Pose(p.Node.X, p.Node.Y, p.Node.Z)));
            var records = string.Concat(planes.Select(static (p, i) => Plane(i + 1,
                $"{SyntheticCloth.Num(p.Normal.X)}, {SyntheticCloth.Num(p.Normal.Y)}, {SyntheticCloth.Num(p.Normal.Z)}",
                p.Offset)));

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ "bone", {{names}} ]
                    m_SkelParents = [ -1, {{string.Join(", ", Enumerable.Repeat("0", count))}} ]
                    m_nNodeCount = {{count + 1}}
                    m_nStaticNodes = 0
                    m_NodeInvMasses = [ {{string.Join(", ", Enumerable.Repeat("1.0", count + 1))}} ]
                    m_InitPose = [ {{SyntheticCloth.Pose(0f, 0f, 0f)}} {{poses}} ]
                    m_CollisionPlanes = [ {{records}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", count))}} ]
                    m_VertexMaps =
                    [
                        {
                            sName = "belt"
                            nNameHash = 1
                            nVertexBase = 1
                            nVertexCount = {{count}}
                            nMapOffset = 0
                            nScaleSourceNode = -1
                            flVolumetricSolveStrength = 0.0
                            vCenterOfMass = [ 0.0, 0.0, 0.0 ]
                        },
                    ]
                }
                """);
        }

        /// <summary>
        /// A planarized box turned in its parent's frame is recovered in its own axes, also from one edge and a corner
        /// alone; the unturned box keeps the parent's axes.
        /// </summary>
        [Test]
        public async Task ATurnedPlanarizedBoxIsRecoveredInItsOwnFrame()
        {
            var half = new Vector3(2f, 3f, 4f);
            var turn = Quaternion.CreateFromYawPitchRoll(0.3f, 0.5f, 0.2f);
            Vector3[] faces =
            [
                new(6f, 0f, 0f), new(6f, 1f, 1f), new(6f, -1f, -2f), new(0f, 7f, 0f), new(0f, -7f, 0f),
                new(0f, 0f, 9f), new(0f, 0f, -9f), new(6f, 7f, 0f), new(6f, 7f, 9f),
            ];
            Vector3[] edge = [new(-5f, -6f, -3f), new(-6f, -5f, -1f), new(-4f, -7f, 1f), new(-7f, -6f, 2f), new(-5f, -5f, -6f)];

            var turned = PlanarizedGroup([.. faces.Select(local => TurnedBoxPlane(local, half, turn))]);
            var edgeOnly = PlanarizedGroup([.. edge.Select(local => TurnedBoxPlane(local, half, turn))]);
            var control = PlanarizedGroup([.. faces.Select(local => TurnedBoxPlane(local, half, Quaternion.Identity))]);

            var turnedBoxes = turned.BuildPlanarizeBoxes();
            var edgeBoxes = edgeOnly.BuildPlanarizeBoxes();
            var controlBoxes = control.BuildPlanarizeBoxes();

            using (Assert.Multiple())
            {
                await Assert.That(turned.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(edgeOnly.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(turnedBoxes.Count).IsEqualTo(1);
                await Assert.That(edgeBoxes.Count).IsEqualTo(1);
                await Assert.That(controlBoxes.Count).IsEqualTo(1);
            }

            using (Assert.Multiple())
            {
                await Assert.That(controlBoxes[0].Rotation).IsEqualTo(Quaternion.Identity);
                await Assert.That(Math.Abs(Quaternion.Dot(turnedBoxes[0].Rotation, Quaternion.Identity))).IsLessThan(0.999f);
                await Assert.That(WorstTurnedBoxPlaneMiss(turnedBoxes[0], faces, half, turn)).IsLessThan(1e-3f);
                await Assert.That(WorstTurnedBoxPlaneMiss(edgeBoxes[0], edge, half, turn)).IsLessThan(1e-3f);
            }
        }

        private static (Vector3 Node, Vector3 Normal, float Offset) TurnedBoxPlane(Vector3 local, Vector3 half, Quaternion turn)
        {
            var contact = Vector3.Clamp(local, -half, half);
            var normal = Vector3.Transform(Vector3.Normalize(local - contact), turn);
            return (Vector3.Transform(local, turn), normal, Vector3.Dot(normal, Vector3.Transform(contact, turn)));
        }

        private static float WorstTurnedBoxPlaneMiss(FeModel.CollisionBox box, Vector3[] locals, Vector3 half, Quaternion turn)
        {
            var toBox = Quaternion.Conjugate(box.Rotation);
            var worst = 0f;
            foreach (var local in locals)
            {
                var (node, normal, offset) = TurnedBoxPlane(local, half, turn);
                var inBox = Vector3.Transform(node - box.Origin, toBox);
                var contact = Vector3.Clamp(inBox, -box.Size, box.Size);
                var fitNormal = Vector3.Transform(Vector3.Normalize(inBox - contact), box.Rotation);
                var fitOffset = Vector3.Dot(fitNormal, Vector3.Transform(contact, box.Rotation) + box.Origin);
                worst = Math.Max(worst, Math.Max((fitNormal - normal).Length(), Math.Abs(fitOffset - offset)));
            }

            return worst;
        }

        /// <summary>
        /// A planarized capsule whose nearest column is clamped through its nodes is still recovered at its radius on
        /// its own axis; an unclamped capsule is recovered as well.
        /// </summary>
        [Test]
        public async Task APlanarizedCapsuleOverAClampedColumnIsFitOnItsOwnAxis()
        {
            var clamped = PlanarizedColumns(14f).BuildPlanarizeCapsules();
            var open = PlanarizedColumns(8f).BuildPlanarizeCapsules();

            using (Assert.Multiple())
            {
                await Assert.That(clamped.Count).IsEqualTo(1);
                await Assert.That(clamped[0].Radius0).IsEqualTo(14f).Within(1e-2f);
                await Assert.That(open.Count).IsEqualTo(1);
                await Assert.That(open[0].Radius0).IsEqualTo(8f).Within(1e-2f);
            }
        }

        private static FeModel PlanarizedColumns(float radius)
        {
            (float X, float Y, float NodeRadius, float[] Heights)[] columns =
            [
                (-8.311f, -11.225f, 4f, [-4.267f, -2.134f, 0f, 2.133f, 4.267f]),
                (-15.225f, -16.156f, 6f, [-4.638f, -2.319f, 0f, 2.319f, 4.638f]),
                (-22.147f, -21.075f, 8f, [-5.004f, -2.502f, 0f, 2.502f, 5.004f]),
            ];

            var names = new StringBuilder("\"spine_2\"");
            var poses = new StringBuilder(SyntheticCloth.Pose(0f, 0f, 0f));
            var radii = new List<string>();
            var planes = new StringBuilder();
            var node = 0;
            foreach (var (x, y, nodeRadius, heights) in columns)
            {
                foreach (var z in heights)
                {
                    node++;
                    var point = new Vector3(x, y, z);
                    var centre = new Vector3(0f, 0f, Math.Clamp(z, -4f, 4f));
                    var normal = Vector3.Normalize(point - centre);
                    var offset = Vector3.Dot(normal, centre) + radius;
                    if (Vector3.Dot(normal, point) - offset < nodeRadius)
                    {
                        offset = Vector3.Dot(normal, point);
                    }

                    names.Append(CultureInfo.InvariantCulture, $", \"v{node}\"");
                    poses.Append(SyntheticCloth.Pose(x, y, z));
                    radii.Add(SyntheticCloth.Num(nodeRadius));
                    planes.Append(CultureInfo.InvariantCulture,
                        $"{{ nCtrlParent = 0 nChildNode = {node} m_Plane = {{ m_vNormal = [ {SyntheticCloth.Num(normal.X)}, {SyntheticCloth.Num(normal.Y)}, {SyntheticCloth.Num(normal.Z)} ] m_flOffset = {SyntheticCloth.Num(offset)} }} flStrength = 1.0 }},");
                }
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{names}} ]
                    m_nNodeCount = {{node + 1}}
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0{{string.Concat(Enumerable.Repeat(", 1.0", node))}} ]
                    m_NodeCollisionRadii = [ {{string.Join(", ", radii)}} ]
                    m_InitPose = [ {{poses}} ]
                    m_CollisionPlanes = [ {{planes}} ]
                    m_VertexMaps = [ {{VertexMapEntry("vmap0", 4164734239, 0, 1, node)}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", node))}} ]
                }
                """);
        }

        /// <summary>
        /// A planarized box is recovered with planes drawn through nodes that reach or sit inside it; the clear planes
        /// alone recover it too.
        /// </summary>
        [Test]
        public async Task APlanarizedBoxKeepsThePlanesItsNodesReachThrough()
        {
            Vector3 half = new(2f, 3f, 4f);
            (Vector3 Node, float Radius)[] clear =
            [
                (new(6f, 0f, 0f), 1f), (new(-6f, 1f, 0f), 1f), (new(0f, 7f, 0f), 1f), (new(1f, -7f, 0f), 1f),
                (new(0f, 0f, 9f), 1f), (new(0f, 1f, -9f), 1f), (new(6f, 7f, 0f), 1f),
            ];
            (Vector3 Node, float Radius)[] reaching = [(new(2.5f, 1f, 1f), 1f), (new(0f, 3.4f, 0f), 2f), (new(1.9f, 0f, 0f), 1f)];

            FeModel reached = PlanarizedBoxGroup([.. clear, .. reaching], half);
            var boxes = reached.BuildPlanarizeBoxes();
            var control = PlanarizedBoxGroup(clear, half).BuildPlanarizeBoxes();

            using (Assert.Multiple())
            {
                await Assert.That(reached.BuildPlanarizeCapsules()).IsEmpty();
                await Assert.That(boxes.Count).IsEqualTo(1);
                await Assert.That(control.Count).IsEqualTo(1);
            }

            using (Assert.Multiple())
            {
                await Assert.That(boxes[0].Origin.Length()).IsLessThan(1e-3f);
                await Assert.That(Vector3.Distance(boxes[0].Size, half)).IsLessThan(1e-3f);
            }
        }

        private static FeModel PlanarizedBoxGroup((Vector3 Node, float Radius)[] nodes, Vector3 half)
        {
            StringBuilder names = new("\"bone\"");
            StringBuilder poses = new(SyntheticCloth.Pose(0f, 0f, 0f));
            List<string> radii = [];
            StringBuilder planes = new();
            for (int i = 0; i < nodes.Length; i++)
            {
                (Vector3 node, float radius) = nodes[i];
                Vector3 contact = Vector3.Clamp(node, -half, half);
                float reach = Vector3.Distance(node, contact);
                Vector3 normal;
                float offset;
                if (reach <= 0f)
                {
                    Vector3 toFace = half - Vector3.Abs(node);
                    normal = toFace.X <= toFace.Y && toFace.X <= toFace.Z ? new Vector3(MathF.Sign(node.X), 0f, 0f)
                        : toFace.Y <= toFace.Z ? new Vector3(0f, MathF.Sign(node.Y), 0f) : new Vector3(0f, 0f, MathF.Sign(node.Z));
                    offset = Vector3.Dot(normal, node);
                }
                else
                {
                    normal = (node - contact) / reach;
                    offset = reach < radius ? Vector3.Dot(normal, node) : Vector3.Dot(normal, contact) + radius;
                }

                names.Append(CultureInfo.InvariantCulture, $", \"v{i + 1}\"");
                poses.Append(SyntheticCloth.Pose(node.X, node.Y, node.Z));
                radii.Add(SyntheticCloth.Num(radius));
                planes.Append(CultureInfo.InvariantCulture,
                    $"{{ nCtrlParent = 0 nChildNode = {i + 1} m_Plane = {{ m_vNormal = [ {SyntheticCloth.Num(normal.X)}, {SyntheticCloth.Num(normal.Y)}, {SyntheticCloth.Num(normal.Z)} ] m_flOffset = {SyntheticCloth.Num(offset)} }} flStrength = 1.0 }},");
            }

            return SyntheticCloth.Parse($$"""
                {
                    m_CtrlName = [ {{names}} ]
                    m_nNodeCount = {{nodes.Length + 1}}
                    m_nStaticNodes = 1
                    m_NodeInvMasses = [ 0.0{{string.Concat(Enumerable.Repeat(", 1.0", nodes.Length))}} ]
                    m_NodeCollisionRadii = [ {{string.Join(", ", radii)}} ]
                    m_InitPose = [ {{poses}} ]
                    m_CollisionPlanes = [ {{planes}} ]
                    m_VertexMaps = [ {{VertexMapEntry("vmap0", 4164734239, 0, 1, nodes.Length)}} ]
                    m_VertexMapValues = [ {{string.Join(", ", Enumerable.Repeat("255", nodes.Length))}} ]
                }
                """);
        }

        /// <summary>
        /// A capsule whose planes are side contacts square to its axis is still recovered, at radius 4.
        /// </summary>
        [Test]
        public async Task ACapsuleIsRecoveredFromSideContactsWhoseNormalsSpanNoCone()
        {
            var sideContacts = PlanarizedGroup(
            [
                (new Vector3(-8.311506f, -11.224525f, 4.267012f),
                    new Vector3(-0.595091f, -0.803658f, 0f), 4f),
                (new Vector3(-15.225058f, -16.15545f, 4.63776f),
                    new Vector3(-0.685841f, -0.727751f, 0f), 4f),
                (new Vector3(-22.147151f, -21.074872f, 5.004368f),
                    new Vector3(-0.72441f, -0.689337f, 0.006685f), 4.032088f),
            ]);

            var shapes = sideContacts.BuildPlanarizeCapsules();

            using (Assert.Multiple())
            {
                await Assert.That(shapes.Count).IsEqualTo(1);
                await Assert.That(shapes[0].Planarize).IsTrue();
                await Assert.That(shapes[0].Radius0).IsEqualTo(4f).Within(1e-3f);
                await Assert.That(shapes[0].Radius1).IsEqualTo(4f).Within(1e-3f);
            }
        }
    }
}
