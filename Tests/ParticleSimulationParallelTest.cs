using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Utils;
using ValveResourceFormat.ResourceTypes;

namespace Tests
{
    /// <summary>
    /// Pins the property the scene's threaded node update rests on: a particle system simulated
    /// alongside others runs exactly as it does on its own. Anything the systems turn out to share - a
    /// static, a dictionary, a random sequence - diverges here.
    /// </summary>
    public class ParticleSimulationParallelTest
    {
        private const float FrameTime = 1f / 60f;
        private const int Frames = 90;

        // Sampled through the run rather than at the end, so a burst effect is compared while it is
        // still alive and interference at any point in the run shows up rather than only what outlives it
        private const int SampleEveryFrames = 5;

        // Enough systems that the dispatch fans out over the pool several times rather than running inline
        private const int SystemsPerFile = 48;

        private static readonly string[] ParticleFiles =
        [
            "explosion_barrel_kv0_lz4.vpcf_c",
            "frostivus_throne_wraith_king_ambient_c_b.vpcf_c",
            "sequence_test.vpcf_c",
        ];

        public static IEnumerable<string> ParticleFileNames() => ParticleFiles;

        private sealed class SimulateWork(List<ParticleSystemSimulation> systems, string[] traces) : IParallelWork
        {
            public void Execute(int index) => traces[index] = RunAndTrace(systems[index]);
        }

        [Test]
        [MethodDataSource(nameof(ParticleFileNames))]
        public async Task ConcurrentSystemsMatchSerialSystems(string filename)
        {
            // Held open for the run: the systems keep reading the definition out of it
            using var resource = LoadParticleResource(filename);
            var particleSystem = (ParticleSystem?)resource.DataBlock;
            ArgumentNullException.ThrowIfNull(particleSystem);

            var serialSystems = new List<ParticleSystemSimulation>(SystemsPerFile);
            var parallelSystems = new List<ParticleSystemSimulation>(SystemsPerFile);

            for (var i = 0; i < SystemsPerFile; i++)
            {
                var (serial, parallel) = CreateSeedMatchedPair(particleSystem);
                serialSystems.Add(serial);
                parallelSystems.Add(parallel);
            }

            var serialTraces = new string[SystemsPerFile];

            for (var i = 0; i < SystemsPerFile; i++)
            {
                serialTraces[i] = RunAndTrace(serialSystems[i]);
            }

            var parallelTraces = new string[SystemsPerFile];

            using (var dispatch = new ParallelDispatch())
            {
                dispatch.Run(new SimulateWork(parallelSystems, parallelTraces), parallelSystems.Count);
            }

            for (var i = 0; i < SystemsPerFile; i++)
            {
                await Assert.That(parallelTraces[i]).IsEqualTo(serialTraces[i]);
            }

            // Guards against passing on nothing: a run that simulated no particles compares equal trivially
            await Assert.That(ParticleLinesIn(serialTraces[0])).IsGreaterThan(0);
        }

        private static Resource LoadParticleResource(string filename)
        {
            var file = Path.Combine(TestContext.TestDirectory!, "Files", filename);
            var resource = new Resource
            {
                FileName = file,
            };

            try
            {
                resource.Read(file);
            }
            catch
            {
                resource.Dispose();
                throw;
            }

            return resource;
        }

        /// <summary>
        /// Two systems that draw the same random sequence. A system seeds itself as it is built and
        /// nothing can set that seed, so a matching pair is found rather than made: systems are built
        /// until one repeats a seed already seen. Neither has been stepped, so their draw counts match too.
        /// </summary>
        private static (ParticleSystemSimulation Serial, ParticleSystemSimulation Parallel) CreateSeedMatchedPair(ParticleSystem particleSystem)
        {
            var bySeed = new Dictionary<int, ParticleSystemSimulation>();

            // The seed is 12 bits, so a repeat turns up within a few hundred systems
            for (var attempt = 0; attempt < 100000; attempt++)
            {
                var candidate = new ParticleSystemSimulation(particleSystem, new NullFileLoader());
                var seed = candidate.RenderState.Random.Seed;

                if (bySeed.TryGetValue(seed, out var matching))
                {
                    return (matching, candidate);
                }

                bySeed[seed] = candidate;
            }

            throw new InvalidOperationException("No two systems drew the same seed");
        }

        /// <summary>
        /// Runs the system and returns its state through the run as text, so a divergence names the
        /// frame and the value that differs rather than only failing.
        /// </summary>
        private static string RunAndTrace(ParticleSystemSimulation system)
        {
            var trace = new StringBuilder();

            for (var frame = 0; frame < Frames; frame++)
            {
                system.Update(FrameTime, frame * FrameTime);

                if (frame % SampleEveryFrames == 0)
                {
                    trace.Append(CultureInfo.InvariantCulture, $"frame {frame}\n");
                    Describe(system, trace, depth: 0);
                }
            }

            return trace.ToString();
        }

        /// <summary>
        /// Appends the system's state, and its children's. Children share their root's control points,
        /// so they are where a leak between two roots would surface.
        /// </summary>
        private static void Describe(ParticleSystemSimulation system, StringBuilder trace, int depth)
        {
            var state = system.RenderState;
            var controlPoint = system.GetControlPoint(0);

            trace.Append(CultureInfo.InvariantCulture, $"{depth} count {system.Particles.Count} age {state.Age:R} emitted {state.ParticleCount}");
            trace.Append(CultureInfo.InvariantCulture, $" endcap {state.InEndCap} frozen {state.Frozen} cp0 {controlPoint.Position:R}\n");

            foreach (ref readonly var particle in system.Particles.Current)
            {
                trace.Append(CultureInfo.InvariantCulture, $"  p {particle.UniqueParticleId} {particle.Position:R} {particle.Velocity:R}");
                trace.Append(CultureInfo.InvariantCulture, $" age {particle.Age:R} alpha {particle.Alpha:R} radius {particle.Radius:R} color {particle.Color:R}\n");
            }

            foreach (var child in system.Children)
            {
                Describe(child, trace, depth + 1);
            }
        }

        private static int ParticleLinesIn(string trace)
        {
            var lines = 0;
            var index = trace.IndexOf("\n  p ", StringComparison.Ordinal);

            while (index >= 0)
            {
                lines++;
                index = trace.IndexOf("\n  p ", index + 1, StringComparison.Ordinal);
            }

            return lines;
        }
    }
}
