using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.IO;
using static Tests.IO.FeModelBuilder;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace Tests.IO
{
    /// <summary>Recovering planarized collision shapes from their compiled planes.</summary>
    public class FeModelReconstructionShapeTest
    {
        /// <summary>
        /// Six planarized planes around a sphere recover its centre (1, 2, 3) and radius 4.
        /// </summary>
        [Test]
        public async Task PlanarizedSphereRecoversItsCentreAndRadius()
        {
            var cloth = PlanarizedGroup(SpherePlanes);

            var shapes = cloth.BuildPlanarizeCapsules();

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

        /// <summary>The six planes touching a sphere of radius 4 at (1, 2, 3), each through its node on an axis.</summary>
        private static (Vector3 Node, Vector3 Normal, float Offset)[] SpherePlanes =>
        [
            (new Vector3(7f, 2f, 3f), Vector3.UnitX, 5f), (new Vector3(-5f, 2f, 3f), -Vector3.UnitX, 3f),
            (new Vector3(1f, 8f, 3f), Vector3.UnitY, 6f), (new Vector3(1f, -4f, 3f), -Vector3.UnitY, 2f),
            (new Vector3(1f, 2f, 9f), Vector3.UnitZ, 7f), (new Vector3(1f, 2f, -3f), -Vector3.UnitZ, 1f),
        ];

        /// <summary>
        /// A planarized shape whose end caps coincide gets a 0.01 axis pointing away from the nodes it owns: here along
        /// -Z from the recovered centre (1, 2, 3).
        /// </summary>
        [Test]
        public async Task APlanarizedEndCapIsGivenAShortAxisAwayFromItsNodes()
        {
            var cloth = PlanarizedGroup(SpherePlanes[..5]);

            var shapes = cloth.BuildPlanarizeCapsules();

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
            var sphere = PlanarizedGroup(SpherePlanes);

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

        /// <summary>
        /// A free bone with one free node per plane in <paramref name="planes"/>, every node planarized onto the bone and
        /// selected by the vertex map <c>belt</c>.
        /// </summary>
        private static ClothReconstruction PlanarizedGroup((Vector3 Node, Vector3 Normal, float Offset)[] planes) => new FeModelBuilder
        {
            Names = ["bone", .. planes.Select(static (_, i) => $"n{i}")],
            Parents = [-1, .. planes.Select(static _ => 0)],
            Positions = [Vector3.Zero, .. planes.Select(static plane => plane.Node)],
            CollisionPlanes = [.. planes.Select(static (plane, i) => new FeCollisionPlane(0, i + 1, new RnPlane(plane.Normal, plane.Offset), 0f, 0f))],
            VertexMapValues = [.. planes.Select(static _ => (byte)255)],
            VertexMaps = [VertexMap("belt", 1, 0, 1, planes.Length)],
        }.Reconstruct();

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

        private static float WorstTurnedBoxPlaneMiss(CollisionBox box, Vector3[] locals, Vector3 half, Quaternion turn)
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

        private static ClothReconstruction PlanarizedColumns(float radius)
        {
            (float X, float Y, float NodeRadius, float[] Heights)[] columns =
            [
                (-8.311f, -11.225f, 4f, [-4.267f, -2.134f, 0f, 2.133f, 4.267f]),
                (-15.225f, -16.156f, 6f, [-4.638f, -2.319f, 0f, 2.319f, 4.638f]),
                (-22.147f, -21.075f, 8f, [-5.004f, -2.502f, 0f, 2.502f, 5.004f]),
            ];

            var nodes = new List<(Vector3 Node, float Radius, Vector3 Normal, float Offset)>();
            foreach (var (x, y, nodeRadius, heights) in columns)
            {
                foreach (var z in heights)
                {
                    var point = new Vector3(x, y, z);
                    var centre = new Vector3(0f, 0f, Math.Clamp(z, -4f, 4f));
                    var normal = Vector3.Normalize(point - centre);
                    var offset = Vector3.Dot(normal, centre) + radius;
                    if (Vector3.Dot(normal, point) - offset < nodeRadius)
                    {
                        offset = Vector3.Dot(normal, point);
                    }

                    nodes.Add((point, nodeRadius, normal, offset));
                }
            }

            return RadiusPlanarizedGroup("spine_2", nodes);
        }

        /// <summary>
        /// A static bone with one free node per entry in <paramref name="nodes"/>, each with its collision radius and its
        /// plane, all selected by the vertex map <c>vmap0</c>.
        /// </summary>
        private static ClothReconstruction RadiusPlanarizedGroup(string bone, List<(Vector3 Node, float Radius, Vector3 Normal, float Offset)> nodes)
            => new FeModelBuilder
            {
                Names = [bone, .. nodes.Select(static (_, i) => $"v{i + 1}")],
                StaticNodes = 1,
                NodeCollisionRadii = [.. nodes.Select(static node => node.Radius)],
                Positions = [Vector3.Zero, .. nodes.Select(static node => node.Node)],
                CollisionPlanes = [.. nodes.Select(static (node, i) => new FeCollisionPlane(0, i + 1, new RnPlane(node.Normal, node.Offset), 1f, 0f))],
                VertexMaps = [VertexMap("vmap0", 4164734239, 0, 1, nodes.Count)],
                VertexMapValues = [.. nodes.Select(static _ => (byte)255)],
            }.Reconstruct();

        /// <summary>
        /// A planarized box is recovered with planes drawn through nodes that reach or sit inside it; the clear planes
        /// alone recover it too.
        /// </summary>
        [Test]
        public async Task APlanarizedBoxKeepsThePlanesItsNodesReachThrough()
        {
            var half = new Vector3(2f, 3f, 4f);
            (Vector3 Node, float Radius)[] clear =
            [
                (new(6f, 0f, 0f), 1f), (new(-6f, 1f, 0f), 1f), (new(0f, 7f, 0f), 1f), (new(1f, -7f, 0f), 1f),
                (new(0f, 0f, 9f), 1f), (new(0f, 1f, -9f), 1f), (new(6f, 7f, 0f), 1f),
            ];
            (Vector3 Node, float Radius)[] reaching = [(new(2.5f, 1f, 1f), 1f), (new(0f, 3.4f, 0f), 2f), (new(1.9f, 0f, 0f), 1f)];

            var reached = PlanarizedBoxGroup([.. clear, .. reaching], half);
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

        private static ClothReconstruction PlanarizedBoxGroup((Vector3 Node, float Radius)[] nodes, Vector3 half)
        {
            var planes = new List<(Vector3 Node, float Radius, Vector3 Normal, float Offset)>();
            for (var i = 0; i < nodes.Length; i++)
            {
                var (node, radius) = nodes[i];
                var contact = Vector3.Clamp(node, -half, half);
                var reach = Vector3.Distance(node, contact);
                Vector3 normal;
                float offset;
                if (reach <= 0f)
                {
                    var toFace = half - Vector3.Abs(node);
                    normal = toFace.X <= toFace.Y && toFace.X <= toFace.Z ? new Vector3(MathF.Sign(node.X), 0f, 0f)
                        : toFace.Y <= toFace.Z ? new Vector3(0f, MathF.Sign(node.Y), 0f) : new Vector3(0f, 0f, MathF.Sign(node.Z));
                    offset = Vector3.Dot(normal, node);
                }
                else
                {
                    normal = (node - contact) / reach;
                    offset = reach < radius ? Vector3.Dot(normal, node) : Vector3.Dot(normal, contact) + radius;
                }

                planes.Add((node, radius, normal, offset));
            }

            return RadiusPlanarizedGroup("bone", planes);
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

        /// <summary>
        /// A box rigid without a <c>vSize</c> is skipped, and its siblings are still read.
        /// </summary>
        [Test]
        public async Task ABoxRigidWithoutASizeIsSkipped()
        {
            var cloth = new FeModelBuilder
            {
                Names = ["bone_0"],
                StaticNodes = 1,
                Positions = [Vector3.Zero],
                BoxRigids =
                [
                    new FeBoxRigid(FeTransform.Identity, 0, 0, null, -1, 0, null),
                    new FeBoxRigid(FeTransform.Identity, 0, 0, new Vector3(1f, 2f, 3f), -1, 0, null),
                ],
            }.Reconstruct();

            using (Assert.Multiple())
            {
                await Assert.That(cloth.CollisionShapes.Boxes.Count).IsEqualTo(1);
                await Assert.That(cloth.CollisionShapes.Boxes[0].Size).IsEqualTo(new Vector3(1f, 2f, 3f));
            }
        }
    }
}
