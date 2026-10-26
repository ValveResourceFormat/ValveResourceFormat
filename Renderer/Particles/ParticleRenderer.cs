using System.Linq;
using System.Runtime.CompilerServices;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Particles.Renderers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Draws a <see cref="ParticleSystemSimulation"/>. One of these mirrors each system in the
    /// simulation's child tree and owns only the renderers that system draws through. Particles,
    /// control points and timing belong to the simulation it watches.
    /// </summary>
    internal sealed class ParticleRenderer : IParticleSystemObserver
    {
        private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

        // Cables need 2 particles to render.
        private const int PrewarmParticleCount = 2;

        private readonly List<ParticleFunctionRenderer> renderers = [];
        private readonly List<ParticleRenderer> childRenderers = [];
        private readonly List<string> skippedRendererClasses = [];
        private readonly RendererContext rendererContext;
        private readonly ParticleSourceLights sourceLights;

        /// <summary>The view this system is drawn through when it is a screen-space effect, otherwise null.</summary>
        private readonly Camera? screenSpaceCamera;

        /// <summary>The system this draws.</summary>
        public ParticleSystemSimulation Simulation { get; }

        /// <summary>
        /// Whether this system draws over the screen in the screen-space layer rather than in the world.
        /// Systems nested under it decide for themselves.
        /// </summary>
        public bool IsScreenSpace => Simulation.IsScreenSpaceEffect;

        /// <summary>
        /// The passes this system draws in, unioned over its renderers and its children's. Fixed once
        /// the system is built, unless its material is overridden.
        /// </summary>
        public CustomRenderPasses Passes { get; private set; }

        /// <summary>
        /// Whether this system, or any system nested under it, soft-fades against the scene.
        /// </summary>
        public bool WantsSceneDepth { get; }

        /// <summary>
        /// Whether this system, or any system nested under it, draws the scene behind it refracted.
        /// </summary>
        public bool WantsSceneColor { get; }

        private SceneNode? ownerNode;

        /// <summary>
        /// The scene node this system renders under, when created for one. Renderers use its per-node
        /// lighting bindings.
        /// </summary>
        public SceneNode? OwnerNode
        {
            get => ownerNode;
            set
            {
                ownerNode = value;

                foreach (var renderer in renderers)
                {
                    renderer.OwnerNode = value;
                }

                foreach (var childRenderer in childRenderers)
                {
                    childRenderer.OwnerNode = value;
                }
            }
        }

        public ParticleRenderer(ParticleSystem particleSystem, Scene scene, ParticleSnapshot? particleSnapshot = null)
            : this(new ParticleSystemSimulation(particleSystem, scene.RendererContext.FileLoader, scene.RendererContext.Logger, particleSnapshot), scene.RendererContext, scene)
        {
        }

        private ParticleRenderer(ParticleSystemSimulation simulation, RendererContext rendererContext, Scene scene)
        {
            Simulation = simulation;
            this.rendererContext = rendererContext;
            sourceLights = new ParticleSourceLights(scene);

            simulation.Observer = this;
            simulation.RenderState.Player = scene.ParticlePlayer;

            SetupRenderers(simulation.Definition.GetArray("m_Renderers") ?? [], scene);

            if (IsScreenSpace)
            {
                screenSpaceCamera = new Camera();
            }

            foreach (var renderer in renderers)
            {
                ObservedEvents |= renderer.ObservedEvents;
                renderer.ScreenSpaceCamera = screenSpaceCamera;
            }

            simulation.RenderState.SequenceDurations = CollectSequenceDurations();
            simulation.RenderState.Lighting = scene.LightingInfo;
            simulation.RenderState.Collision = scene.ParticleCollision;

            foreach (var childSimulation in simulation.Children)
            {
                childRenderers.Add(new ParticleRenderer(childSimulation, rendererContext, scene));
            }

            Passes = CollectPasses();
            WantsSceneDepth = renderers.Any(static r => r.WantsSceneDepth) || childRenderers.Any(static c => c.WantsSceneDepth);
            WantsSceneColor = renderers.Any(static r => r.WantsSceneColor) || childRenderers.Any(static c => c.WantsSceneColor);
            CanRenderReplacement = renderers.Any(static r => r.CanRenderReplacement) || childRenderers.Any(static c => c.CanRenderReplacement);
        }

        private void SetupRenderers(IReadOnlyList<KVObject> rendererData, Scene scene)
        {
            var initialReads = new StrongBox<ulong>();

            foreach (var rendererInfo in rendererData)
            {
                var parse = new ParticleDefinitionParser(rendererInfo, rendererContext.Logger, initialReads, Simulation.BehaviorVersion);

                if (parse.Boolean("m_bDisableOperator", default))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetStringProperty("_class");
                var renderer = ParticleRendererFactory.Create(rendererClass, parse, rendererContext, scene);

                if (renderer == null)
                {
                    rendererContext.Logger.LogUniqueWarningFor(["renderer", rendererClass], UnsupportedClassWarning, "renderer", rendererClass, Simulation.Name);
                    skippedRendererClasses.Add(rendererClass);
                    continue;
                }

                renderers.Add(renderer);
            }

            var writtenFields = 0UL;
            var blendedFields = 0UL;

            foreach (var renderer in renderers)
            {
                writtenFields |= renderer.StoredFields;
                blendedFields |= renderer.BlendedFields;
            }

            Simulation.AddRendererFields(writtenFields, initialReads.Value, blendedFields);
        }

        /// <summary>
        /// How long the sequence each sequence number reaches on the first sheet this system's renderers
        /// offer runs, in frames, or null when none of them draws with one. A number no sequence answers
        /// to runs for 0.
        /// </summary>
        private float[]? CollectSequenceDurations()
        {
            foreach (var renderer in renderers)
            {
                if (renderer.SpriteSheet is not { } sheet)
                {
                    continue;
                }

                var durations = new float[sheet.SequenceIdCount];

                for (var id = 0; id < durations.Length; id++)
                {
                    var index = sheet.GetSequenceIndex(id);

                    if (index >= 0)
                    {
                        durations[id] = sheet.Sequences[index].TotalTime;
                    }
                }

                return durations;
            }

            return null;
        }

        private CustomRenderPasses CollectPasses()
        {
            var passes = CustomRenderPasses.None;

            foreach (var renderer in renderers)
            {
                if (!renderer.DrawsInPasses)
                {
                    continue;
                }

                if (IsScreenSpace)
                {
                    if (DrawsInScreenSpaceLayer(renderer))
                    {
                        passes |= CustomRenderPasses.ScreenSpace;
                    }

                    continue;
                }

                if (renderer.OnlyRenderInEffectsBloomPass)
                {
                    passes |= CustomRenderPasses.EffectsBloom;
                    continue;
                }

                passes |= renderer.OnlyRenderInEffectsWaterPass
                    ? CustomRenderPasses.WaterEffects
                    : renderer.Pass == RenderPass.Opaque
                        ? CustomRenderPasses.Opaque
                        : CustomRenderPasses.Translucent;

                if (renderer.CanRenderDepth)
                {
                    passes |= CustomRenderPasses.DepthOnly;
                }
            }

            foreach (var childRenderer in childRenderers)
            {
                passes |= childRenderer.Passes;
            }

            return passes;
        }

        /// <summary>The renderers of this system and the systems nested under it.</summary>
        public IEnumerable<ParticleFunctionRenderer> EnumerateRenderers()
            => renderers.Concat(childRenderers.SelectMany(static child => child.EnumerateRenderers()));

        /// <summary>The renderer classes of this system and the systems nested under it that could not be created, and so draw nothing.</summary>
        public IEnumerable<string> EnumerateSkippedRendererClasses()
            => skippedRendererClasses.Concat(childRenderers.SelectMany(static child => child.EnumerateSkippedRendererClasses()));

        /// <inheritdoc/>
        public void OnFrameSimulated(ParticleCollection particles, ParticleSystemState state)
        {
            foreach (var renderer in renderers)
            {
                using var scratch = ParticleScratch.Scope();
                renderer.Simulate(particles, state);
            }
        }

        /// <inheritdoc/>
        public void OnParticlesInitialized(ParticleCollection particles, ParticleSystemState state, int first, int count)
        {
            foreach (var renderer in renderers)
            {
                using var scratch = ParticleScratch.Scope();
                renderer.InitializeParticles(particles, state, first, count);
            }
        }

        /// <inheritdoc/>
        public ParticleEventType ObservedEvents { get; }

        /// <inheritdoc/>
        public void OnParticleEvent(ParticleCollection particles, ParticleEventType eventType, ReadOnlySpan<int> indices)
        {
            foreach (var renderer in renderers)
            {
                if ((renderer.ObservedEvents & eventType) != 0)
                {
                    using var scratch = ParticleScratch.Scope();
                    renderer.OnParticleEvent(particles, eventType, indices);
                }
            }
        }

        /// <inheritdoc/>
        public void OnLightSourceUpdated(ParticleLightSource light) => sourceLights.Update(light);

        /// <summary>
        /// Finishes the frame for this system's renderers and its children's, on one thread. Renderers
        /// out of draw distance or faded out are hidden; the rest finish the step when there was one.
        /// The lights the system places itself show while it is within draw distance and not finished.
        /// </summary>
        public void Act(Camera camera, bool stepped)
        {
            var withinDrawDistance = IsWithinDrawDistance(camera);

            foreach (var renderer in renderers)
            {
                if (!withinDrawDistance || renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    renderer.Hide(Simulation.Particles);
                }
                else if (stepped)
                {
                    using var scratch = ParticleScratch.Scope();
                    renderer.Act(Simulation.Particles, Simulation.RenderState);
                }
            }

            sourceLights.Apply(withinDrawDistance && !Simulation.IsFinished());

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.Act(camera, stepped);
            }
        }

        /// <summary>Hides what this system's renderers and its children's placed in the scene.</summary>
        public void Hide()
        {
            foreach (var renderer in renderers)
            {
                renderer.Hide(Simulation.Particles);
            }

            sourceLights.Apply(visible: false);

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.Hide();
            }
        }

        /// <summary>Uploads the vertex buffers of every renderer that draws this frame.</summary>
        public void UpdateBuffers(Camera camera)
        {
            foreach (var childRenderer in childRenderers)
            {
                if (!childRenderer.Simulation.ShouldRunAsChildOf(Simulation.RenderState))
                {
                    continue;
                }

                childRenderer.UpdateBuffers(camera);
            }

            if (Simulation.Particles.Count == 0)
            {
                return;
            }

            UpdateScreenSpaceCamera(camera);

            foreach (var renderer in renderers)
            {
                if (renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    continue;
                }

                using var scratch = ParticleScratch.Scope();
                renderer.UpdateBuffers(Simulation.Particles, Simulation.RenderState, camera);
            }
        }

        /// <summary>
        /// Draws the renderers belonging to <paramref name="pass"/>, this system's before its children's.
        /// </summary>
        public void Render(Camera camera, RenderPass pass, RenderLayer layer = RenderLayer.Scene)
        {
            RenderSelf(camera, pass, layer);

            foreach (var childRenderer in childRenderers)
            {
                if (!childRenderer.Simulation.ShouldRunAsChildOf(Simulation.RenderState))
                {
                    continue;
                }

                childRenderer.Render(camera, pass, layer);
            }
        }

        private void RenderSelf(Camera camera, RenderPass pass, RenderLayer layer)
        {
            if (!IsWithinDrawDistance(camera) || Simulation.Particles.Count == 0)
            {
                return;
            }

            var rendered = false;

            var depthPass = pass == RenderPass.DepthOnly;

            foreach (var renderer in renderers)
            {
                if (!InPass(renderer, pass, layer) || renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    continue;
                }

                using var scratch = ParticleScratch.Scope();

                if (depthPass)
                {
                    renderer.RenderDepth(Simulation.Particles, Simulation.RenderState, camera);
                }
                else
                {
                    renderer.Render(Simulation.Particles, Simulation.RenderState, camera);
                }

                rendered = true;
            }

            if (rendered)
            {
                PerfStats.Active.Count(Counter.ParticleSystem);
            }
        }

        /// <summary>
        /// Whether <paramref name="renderer"/> draws in <paramref name="pass"/> on <paramref name="layer"/>.
        /// A bloom-only renderer draws in the effects bloom layer and nowhere else, whatever pass its
        /// material would otherwise pick. Every other renderer of the system draws in the pass that draws
        /// everything at once. A screen-space system draws in the translucent pass of the screen-space layer
        /// and nowhere else, and no other system draws there.
        /// </summary>
        private bool InPass(ParticleFunctionRenderer renderer, RenderPass pass, RenderLayer layer)
        {
            if (IsScreenSpace || layer == RenderLayer.ScreenSpace)
            {
                return IsScreenSpace && layer == RenderLayer.ScreenSpace && pass == RenderPass.Translucent && DrawsInScreenSpaceLayer(renderer);
            }

            if (renderer.OnlyRenderInEffectsBloomPass || layer == RenderLayer.Bloom)
            {
                return renderer.OnlyRenderInEffectsBloomPass && layer == RenderLayer.Bloom && pass == RenderPass.Translucent;
            }

            var waterEffectsLayer = layer == RenderLayer.WaterEffects;

            return pass switch
            {
                RenderPass.DepthOnly => renderer.CanRenderDepth,
                RenderPass.All => !renderer.OnlyRenderInEffectsWaterPass,
                _ => renderer.Pass == pass && renderer.OnlyRenderInEffectsWaterPass == waterEffectsLayer,
            };
        }

        /// <summary>
        /// Whether a renderer of a screen-space system draws in the screen-space layer. Those that draw only
        /// into the world's effects maps draw nowhere.
        /// </summary>
        private static bool DrawsInScreenSpaceLayer(ParticleFunctionRenderer renderer)
            => !renderer.OnlyRenderInEffectsBloomPass && !renderer.OnlyRenderInEffectsWaterPass;

        /// <inheritdoc cref="ParticleFunctionRenderer.CanRenderReplacement"/>
        public bool CanRenderReplacement { get; }

        /// <summary>
        /// Draws the renderers belonging to <paramref name="pass"/> with a pass replacement shader,
        /// this system's before its children's.
        /// </summary>
        public void RenderReplacement(Shader replacement, uint objectId, RenderPass pass, Camera camera)
        {
            if (IsWithinDrawDistance(camera) && Simulation.Particles.Count > 0)
            {
                foreach (var renderer in renderers)
                {
                    if (!renderer.CanRenderReplacement || !InPass(renderer, pass, RenderLayer.Scene)
                        || renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                    {
                        continue;
                    }

                    renderer.RenderReplacement(replacement, objectId);
                }
            }

            foreach (var childRenderer in childRenderers)
            {
                if (!childRenderer.CanRenderReplacement || !childRenderer.Simulation.ShouldRunAsChildOf(Simulation.RenderState))
                {
                    continue;
                }

                childRenderer.RenderReplacement(replacement, objectId, pass, camera);
            }
        }

        /// <summary>
        /// Whether the camera is near enough for this system to draw and simulate. A screen-space system
        /// always is, since its bounds are not where it is placed.
        /// </summary>
        public bool IsWithinDrawDistance(Camera camera) => IsScreenSpace || Simulation.IsWithinDrawDistance(camera.Location);

        /// <summary>Whether this system, or any system nested under it, draws in the screen-space layer.</summary>
        public bool DrawsInScreenSpace => (Passes & CustomRenderPasses.ScreenSpace) != 0;

        /// <summary>Points the screen-space view at the viewport <paramref name="camera"/> draws into.</summary>
        private void UpdateScreenSpaceCamera(Camera camera)
        {
            if (screenSpaceCamera != null)
            {
                ParticleScreenSpaceView.Configure(screenSpaceCamera, camera.WindowSize);
            }
        }

        /// <summary>
        /// Force-renders each active sub-renderer once with temporary particles.
        /// </summary>
        public void Prewarm(Camera camera)
        {
            foreach (var childRenderer in childRenderers)
            {
                if (!childRenderer.Simulation.ShouldRunAsChildOf(Simulation.RenderState))
                {
                    continue;
                }

                childRenderer.Prewarm(camera);
            }

            if (renderers.Count == 0 || Simulation.BeginPrewarm(PrewarmParticleCount) is not { } scope)
            {
                return;
            }

            UpdateScreenSpaceCamera(camera);

            foreach (var renderer in renderers)
            {
                if (renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    continue;
                }

                using var scratch = ParticleScratch.Scope();
                renderer.UpdateBuffers(Simulation.Particles, Simulation.RenderState, camera);
                renderer.Render(Simulation.Particles, Simulation.RenderState, camera);
            }

            Simulation.EndPrewarm(scope);
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => renderers
                .SelectMany(static renderer => renderer.GetSupportedRenderModes())
                .Concat(childRenderers.SelectMany(static child => child.GetSupportedRenderModes()));

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in renderers)
            {
                renderer.SetRenderMode(renderMode);
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.SetRenderMode(renderMode);
            }
        }

        /// <summary>
        /// Replaces the texture every renderer in this system and its children draws with.
        /// </summary>
        public void SetTextureOverride(string textureName)
        {
            foreach (var renderer in renderers)
            {
                renderer.SetTextureOverride(textureName);
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.SetTextureOverride(textureName);
            }
        }

        public void SetTextureOverride(RenderTexture texture)
        {
            foreach (var renderer in renderers)
            {
                renderer.SetTextureOverride(texture);
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.SetTextureOverride(texture);
            }
        }

        /// <summary>
        /// Replaces the material every renderer in this system and its children draws with, and collects
        /// the passes again since the material decides them.
        /// </summary>
        public void SetMaterialOverride(RenderMaterial material)
        {
            foreach (var renderer in renderers)
            {
                renderer.SetMaterialOverride(material);
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.SetMaterialOverride(material);
            }

            Passes = CollectPasses();
        }

        public void Delete()
        {
            foreach (var renderer in renderers)
            {
                renderer.Delete();
            }

            sourceLights.Delete();

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.Delete();
            }
        }

        public AABB LocalBoundingBox => Simulation.LocalBoundingBox;
        public bool IsFinished() => Simulation.IsFinished();
        public bool HasFinishedEmitting() => Simulation.HasFinishedEmitting(emissionEndIsEnough: true, includeChildren: true);
        public ControlPoint MainControlPoint => Simulation.MainControlPoint;
        public ControlPoint GetControlPoint(int cp) => Simulation.GetControlPoint(cp);
        public void SetCameraPosition(Vector3 position) => Simulation.SetCameraPosition(position);
        public void SetDetailLevel(ParticleDetailLevel level) => Simulation.SetDetailLevel(level);
        public void Update(float frameTime, float worldTime) => Simulation.Update(frameTime, worldTime);
        public void Replay() => Simulation.Replay();
        public void Stop() => Simulation.Stop();
        public void PlayEndCap() => Simulation.PlayEndCap();
        public void PlayEndCapOnly() => Simulation.PlayEndCapOnly();
    }
}
