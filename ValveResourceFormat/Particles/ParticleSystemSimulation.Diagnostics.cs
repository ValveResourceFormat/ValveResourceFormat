namespace ValveResourceFormat.Particles
{
    /// <summary>What keeps a system's preview from matching the game, and counts over its tree.</summary>
    public partial class ParticleSystemSimulation
    {
        /// <summary>Classes that do nothing, or fall back, without world geometry to trace against.</summary>
        private static readonly HashSet<string> CollisionClasses =
        [
            "C_OP_WorldTraceConstraint",
            "C_INIT_PositionPlaceOnGround",
            "C_OP_MovementPlaceOnGround",
        ];

        // Recorded once each, from setup or from whatever draws the system; read by the UI thread
        private readonly List<ParticleDiagnostic> diagnostics = [];
        private readonly HashSet<(string Component, string Problem)> reportedDiagnostics = [];
        private readonly List<string> collisionUsers = [];

        /// <summary>
        /// Records a problem with this system. A problem already recorded for the same component is
        /// ignored, so something reported every frame shows once.
        /// </summary>
        /// <param name="severity">How much it matters.</param>
        /// <param name="component">The function, renderer or input concerned.</param>
        /// <param name="problem">What is wrong.</param>
        /// <param name="impact">What it does to the rendered effect.</param>
        /// <param name="resolution">What would fix it, or what input is missing.</param>
        public void ReportDiagnostic(ParticleDiagnosticSeverity severity, string component, string problem, string impact, string resolution)
        {
            lock (diagnostics)
            {
                if (reportedDiagnostics.Add((component, problem)))
                {
                    diagnostics.Add(new ParticleDiagnostic(severity, Name, component, problem, impact, resolution));
                }
            }
        }

        /// <summary>
        /// Collects the recorded problems of this system and every system below it, along with what
        /// its current state shows: systems emitting nothing, inputs they wait for, children not running.
        /// Each one carries the path from the root to its system.
        /// </summary>
        public List<ParticleDiagnostic> CollectDiagnostics()
        {
            var collected = new List<ParticleDiagnostic>();
            CollectDiagnostics(collected, ShortName(Name), parentState: null);
            return collected;
        }

        private void CollectDiagnostics(List<ParticleDiagnostic> collected, string path, ParticleSystemState? parentState)
        {
            lock (diagnostics)
            {
                foreach (var diagnostic in diagnostics)
                {
                    collected.Add(diagnostic with { Path = path });
                }
            }

            void Add(ParticleDiagnosticSeverity severity, string component, string problem, string impact, string resolution)
                => collected.Add(new ParticleDiagnostic(severity, Name, component, problem, impact, resolution) { Path = path });

            var running = parentState == null || ShouldRunAsChildOf(parentState);

            if (parentState != null && !running)
            {
                var reason = !childEnabled ? "is not among the children its parent picked to run"
                    : detailLevel > parentState.DetailLevel ? $"needs detail level {detailLevel} or higher"
                    : isEndCapChild ? "only runs during its parent's endcap"
                    : $"starts {startDelay:0.##}s into its parent's life";

                Add(ParticleDiagnosticSeverity.Info, "child system", $"Not running: it {reason}", "Draws nothing for now", "Raise the detail level, play the endcap or wait, as the reason says");
            }

            // Inputs and emission are judged only once the system has run a step, since a waiting child
            // has not yet had its pre-emission operators build what its emitters read
            var simulated = running && hasStarted && systemState.Age > 0f;

            foreach (var emitter in emitters)
            {
                if (simulated && emitter.DescribeMissingInput(systemState) is { } missing)
                {
                    Add(ParticleDiagnosticSeverity.Error, $"emitter {emitter.GetType().Name}", missing, "The emitter has nothing to emit from, so the system stays empty", "Supply the snapshot the game would bind, or load the effect from a map that does");
                }
            }

            if (simulated && systemState.Age > 0.5f && particlesEmitted == 0 && initialParticles == 0)
            {
                if (emitters.Count == 0)
                {
                    Add(ParticleDiagnosticSeverity.Info, "emitters", "Has no emitter of its own", "Holds no particles; it may only exist to run its children", "None needed if its children draw the effect");
                }
                else
                {
                    Add(ParticleDiagnosticSeverity.Warning, "emitters", $"Emitted no particles in {systemState.Age:0.0}s", "Nothing of this system is drawn", "Check its emitters' start times and inputs, and the other diagnostics of this system");
                }
            }

            if (collisionUsers.Count > 0 && !systemState.Collision.HasGeometry)
            {
                Add(ParticleDiagnosticSeverity.Info, string.Join(", ", collisionUsers), "No world geometry to trace against",
                    "Collisions and ground placement do nothing, so particles fall through or stay where they spawned",
                    "Enable the preview ground plane, or view the effect in a map");
            }

            foreach (var child in childSimulations)
            {
                child.CollectDiagnostics(collected, $"{path} > {ShortName(child.Name)}", systemState);
            }
        }

        /// <summary>Counts systems and particles over this system and every system below it.</summary>
        public ParticleSystemStatistics CollectStatistics()
        {
            var statistics = new ParticleSystemStatistics();
            CollectStatistics(ref statistics, parentState: null);
            return statistics;
        }

        private void CollectStatistics(ref ParticleSystemStatistics statistics, ParticleSystemState? parentState)
        {
            var active = hasStarted && (parentState == null || ShouldRunAsChildOf(parentState));
            var live = particleCollection.Count;

            statistics = statistics with
            {
                Systems = statistics.Systems + 1,
                ActiveSystems = statistics.ActiveSystems + (active ? 1 : 0),
                LiveParticles = statistics.LiveParticles + live,
                EmittedParticles = statistics.EmittedParticles + particlesEmitted,
                SystemsWithParticles = statistics.SystemsWithParticles + (live > 0 ? 1 : 0),
            };

            foreach (var child in childSimulations)
            {
                child.CollectStatistics(ref statistics, systemState);
            }
        }

        private static string ShortName(string name)
        {
            var slash = name.LastIndexOfAny(['/', '\\']);
            return slash >= 0 ? name[(slash + 1)..] : name;
        }
    }
}
