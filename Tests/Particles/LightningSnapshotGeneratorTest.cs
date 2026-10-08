using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.PreEmissionOperators;

namespace Tests.Particles
{
    public class LightningSnapshotGeneratorTest
    {
        private static readonly LightningSnapshotGenerator.LightningSettings Settings = new()
        {
            Depth = 5,
            Offset = 20f,
            OffsetDecay = 0.5f,
            UvScale = 100f,
            SplitRate = 0.3f,
            RecursionSplitScale = 1f,
            BranchDistanceScale = 1f,
            BranchOffsetScale = 1f,
            BranchTwist = 0.5f,
            RadiusStart = 4f,
            RadiusEnd = 0f,
            MaxRows = LightningSnapshotGenerator.DefaultMaxRows,
        };

        private static readonly Vector3 Start = Vector3.Zero;
        private static readonly Vector3 End = new(0f, 0f, -400f);

        private static Func<float> Seeded(int seed)
        {
            var random = new Random(seed);
            return () => random.NextSingle();
        }

        private static T[] Column<T>(ParticleSnapshot snapshot, ParticleField field)
        {
            var name = ParticleSnapshot.GetSnapshotAttributeName(field);
            return (T[])snapshot.AttributeData.Single(column => column.Key.Name == name).Value;
        }

        [Test]
        public async Task SameRandomSequenceGivesSameBolt()
        {
            var first = LightningSnapshotGenerator.Build(Start, End, Settings, Seeded(7));
            var second = LightningSnapshotGenerator.Build(Start, End, Settings, Seeded(7));
            var other = LightningSnapshotGenerator.Build(Start, End, Settings, Seeded(8));

            using (Assert.Multiple())
            {
                await Assert.That(second.NumParticles).IsEqualTo(first.NumParticles);
                await Assert.That(Column<Vector3>(second, ParticleField.Position).SequenceEqual(Column<Vector3>(first, ParticleField.Position))).IsTrue();
                await Assert.That(Column<Vector3>(other, ParticleField.Position).SequenceEqual(Column<Vector3>(first, ParticleField.Position))).IsFalse();
            }
        }

        [Test]
        public async Task MainBoltRunsBetweenTheEndPoints()
        {
            var snapshot = LightningSnapshotGenerator.Build(Start, End, Settings with { SplitRate = 0f }, Seeded(1));
            var positions = Column<Vector3>(snapshot, ParticleField.Position);
            var radii = Column<float>(snapshot, ParticleField.Radius);
            var segments = Column<int>(snapshot, ParticleField.RopeSegmentId);

            // Displacement is perpendicular to the spans, so no point strays further than the offset
            // budget summed over every level
            var budget = Settings.Offset / (1f - Settings.OffsetDecay);
            var axis = Vector3.Normalize(End - Start);
            var furthest = positions.Max(p => Vector3.Distance(p, Start + (axis * Vector3.Dot(p - Start, axis))));

            using (Assert.Multiple())
            {
                await Assert.That(snapshot.NumParticles).IsEqualTo((uint)((1 << Settings.Depth) + 1));
                await Assert.That(positions[0]).IsEqualTo(Start);
                await Assert.That(positions[^1]).IsEqualTo(End);
                await Assert.That(radii[0]).IsEqualTo(Settings.RadiusStart);
                await Assert.That(radii[^1]).IsEqualTo(Settings.RadiusEnd);
                await Assert.That(segments.All(segment => segment == 0)).IsTrue();
                await Assert.That(furthest).IsLessThanOrEqualTo(budget);
                await Assert.That(furthest).IsGreaterThan(0f);
            }
        }

        [Test]
        public async Task BranchesAreSeparateRunsStartingOnEarlierRuns()
        {
            var snapshot = LightningSnapshotGenerator.Build(Start, End, Settings, Seeded(3));
            var positions = Column<Vector3>(snapshot, ParticleField.Position);
            var segments = Column<int>(snapshot, ParticleField.RopeSegmentId);
            var coordinates = Column<float>(snapshot, ParticleField.ScratchFloat);

            var runStarts = Enumerable.Range(0, segments.Length).Where(i => i == 0 || segments[i] != segments[i - 1]).ToList();

            // Ids are handed out in order, so a run never resumes once another has started
            var idsInOrder = runStarts.Select(i => segments[i]).SequenceEqual(Enumerable.Range(0, runStarts.Count));

            var branchesStartOnEarlierRuns = runStarts.Skip(1).All(start => positions.Take(start).Contains(positions[start]));
            var coordinatesRestartPerRun = runStarts.All(start => coordinates[start] == Settings.UvOffset);
            var coordinatesGrowAlongRuns = Enumerable.Range(1, segments.Length - 1)
                .Where(i => segments[i] == segments[i - 1])
                .All(i => coordinates[i] >= coordinates[i - 1]);

            using (Assert.Multiple())
            {
                await Assert.That(runStarts.Count).IsGreaterThan(1);
                await Assert.That(idsInOrder).IsTrue();
                await Assert.That(branchesStartOnEarlierRuns).IsTrue();
                await Assert.That(coordinatesRestartPerRun).IsTrue();
                await Assert.That(coordinatesGrowAlongRuns).IsTrue();
            }
        }

        [Test]
        public async Task DegenerateInputsYieldSafeSnapshots()
        {
            var coincident = LightningSnapshotGenerator.Build(Start, Start, Settings, Seeded(1));
            var noRecursion = LightningSnapshotGenerator.Build(Start, End, Settings with { Depth = 0 }, Seeded(1));
            var capped = LightningSnapshotGenerator.Build(Start, End, Settings with { Depth = 10, SplitRate = 1f, MaxRows = 50 }, Seeded(1));
            var cappedPositions = Column<Vector3>(capped, ParticleField.Position);

            using (Assert.Multiple())
            {
                await Assert.That(coincident.NumParticles).IsEqualTo(0u);
                await Assert.That(noRecursion.NumParticles).IsEqualTo(2u);
                await Assert.That(capped.NumParticles).IsLessThanOrEqualTo(50u);
                await Assert.That(cappedPositions.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z))).IsTrue();
            }
        }

        private const string LightningSystem = """
            {
                _class = "CParticleSystemDefinition"
                m_nBehaviorVersion = 12
                m_nMaxParticles = 5000
                m_PreEmissionOperators =
                [
                    {
                        _class = "C_OP_LightningSnapshotGenerator"
                        m_nCPStartPnt = 1
                        m_nCPEndPnt = 2
                        m_flSegments = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 6.0 }
                        m_flRadiusStart = { m_nType = "PF_TYPE_LITERAL" m_flLiteralValue = 3.0 }
                    },
                ]
                m_Emitters =
                [
                    {
                        _class = "C_OP_InstantaneousEmitter"
                        m_nSnapshotControlPoint = 0
                    },
                ]
                m_Initializers =
                [
                    {
                        _class = "C_INIT_InitFromCPSnapshot"
                        m_nLocalSpaceCP = -1
                    },
                    {
                        _class = "C_INIT_InitFromCPSnapshot"
                        m_nAttributeToWrite = 3
                    },
                ]
            }
            """;

        [Test]
        public async Task SnapshotEmitterSpawnsOneParticlePerBoltPoint()
        {
            var simulation = ParticleTestSystem.Simulate(LightningSystem);
            simulation.RenderState.SetControlPointValue(1, new Vector3(0f, 0f, 200f));
            simulation.RenderState.SetControlPointValue(2, new Vector3(50f, 0f, -200f));

            simulation.Update(1f / 60f, 0f);

            var snapshot = simulation.RenderState.GetControlPointSnapshot(0);
            await Assert.That(snapshot).IsNotNull();

            var positions = Column<Vector3>(snapshot!, ParticleField.Position);
            var segments = Column<int>(snapshot!, ParticleField.RopeSegmentId);
            var radii = Column<float>(snapshot!, ParticleField.Radius);
            var particles = simulation.Particles.Current.ToArray().OrderBy(p => p.UniqueParticleId).ToArray();

            var matches = Enumerable.Range(0, Math.Min(particles.Length, positions.Length)).All(i =>
                particles[i].Position == positions[i]
                && particles[i].RopeSegmentId == segments[i]
                && particles[i].Radius == radii[i]);

            using (Assert.Multiple())
            {
                await Assert.That(positions.Length).IsGreaterThan(1 << 6);
                await Assert.That(particles.Length).IsEqualTo(positions.Length);
                await Assert.That(positions[0]).IsEqualTo(new Vector3(0f, 0f, 200f));
                await Assert.That(matches).IsTrue();
                await Assert.That(segments.Distinct().Count()).IsGreaterThan(1);
            }
        }
    }
}
