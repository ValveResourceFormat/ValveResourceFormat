using System.Linq;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles;
using ValveResourceFormat.Particles.Utils;
using ValveResourceFormat.Renderer.Particles.Renderers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Draws a <see cref="ParticleSystemSimulation"/>. One of these mirrors each system in the
    /// simulation's child tree, owning the renderers that system draws through and nothing else: the
    /// particles, control points and timing all belong to the simulation it watches.
    /// </summary>
    internal sealed class ParticleRenderer : IParticleSystemObserver
    {
        private const string UnsupportedClassWarning = "Unsupported {ComponentType} class '{ClassName}' {File}";

        // Cables need 2 particles to render.
        private const int PrewarmParticleCount = 2;

        private readonly List<ParticleFunctionRenderer> renderers = [];
        private readonly List<ParticleRenderer> childRenderers = [];
        private readonly RendererContext rendererContext;

        /// <summary>The system this draws.</summary>
        public ParticleSystemSimulation Simulation { get; }

        /// <summary>
        /// The passes this system draws in, unioned over its renderers and its children's. Fixed once
        /// the system is built.
        /// </summary>
        public CustomRenderPasses Passes { get; }

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

        public ParticleRenderer(ParticleSystem particleSystem, RendererContext rendererContext, Scene scene, ParticleSnapshot? particleSnapshot = null)
            : this(new ParticleSystemSimulation(particleSystem, rendererContext.FileLoader, rendererContext.Logger, particleSnapshot), rendererContext, scene)
        {
        }

        private ParticleRenderer(ParticleSystemSimulation simulation, RendererContext rendererContext, Scene scene)
        {
            Simulation = simulation;
            this.rendererContext = rendererContext;

            simulation.Observer = this;

            SetupRenderers(simulation.Definition.GetArray("m_Renderers") ?? [], scene);

            foreach (var childSimulation in simulation.Children)
            {
                childRenderers.Add(new ParticleRenderer(childSimulation, rendererContext, scene));
            }

            Passes = CollectPasses();
        }

        private void SetupRenderers(IReadOnlyList<KVObject> rendererData, Scene scene)
        {
            foreach (var rendererInfo in rendererData)
            {
                var parse = new ParticleDefinitionParser(rendererInfo, rendererContext.Logger, Simulation.BehaviorVersion);

                if (parse.Boolean("m_bDisableOperator", default))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetStringProperty("_class");
                var renderer = ParticleRendererFactory.Create(rendererClass, parse, rendererContext, scene);

                if (renderer == null)
                {
                    rendererContext.Logger.LogUniqueWarningFor(["renderer", rendererClass], UnsupportedClassWarning, "renderer", rendererClass, Simulation.Name);
                    continue;
                }

                renderers.Add(renderer);
            }
        }

        private CustomRenderPasses CollectPasses()
        {
            var passes = CustomRenderPasses.None;

            foreach (var renderer in renderers)
            {
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

        /// <inheritdoc/>
        public void OnFrameSimulated(ParticleCollection particles, ParticleSystemState state)
        {
            foreach (var renderer in renderers)
            {
                renderer.Simulate(particles, state);
            }
        }

        /// <summary>Finishes the step for this system's renderers and its children's, on one thread.</summary>
        public void Act()
        {
            foreach (var renderer in renderers)
            {
                renderer.Act(Simulation.Particles, Simulation.RenderState);
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.Act();
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

            foreach (var renderer in renderers)
            {
                if (renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    continue;
                }

                renderer.UpdateBuffers(Simulation.Particles, Simulation.RenderState, camera);
            }
        }

        /// <summary>
        /// Draws the renderers belonging to <paramref name="pass"/>.
        /// </summary>
        public void Render(Camera camera, RenderPass pass, bool waterEffectsLayer = false)
        {
            foreach (var childRenderer in childRenderers)
            {
                if (!childRenderer.Simulation.ShouldRunAsChildOf(Simulation.RenderState))
                {
                    continue;
                }

                childRenderer.Render(camera, pass, waterEffectsLayer);
            }

            if (!IsWithinDrawDistance(camera) || Simulation.Particles.Count == 0)
            {
                return;
            }

            var rendered = false;

            var depthPass = pass == RenderPass.DepthOnly;

            foreach (var renderer in renderers)
            {
                var inPass = depthPass
                    ? renderer.CanRenderDepth
                    : renderer.Pass == pass && renderer.OnlyRenderInEffectsWaterPass == waterEffectsLayer;

                if (!inPass || renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    continue;
                }

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
        /// Whether the camera is near enough for this system to draw and simulate.
        /// </summary>
        public bool IsWithinDrawDistance(Camera camera) => Simulation.IsWithinDrawDistance(camera.Location);

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

            foreach (var renderer in renderers)
            {
                if (renderer.GetOperatorRunStrength(Simulation.RenderState) <= 0.0f)
                {
                    continue;
                }

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
            => SetTextureOverride(rendererContext.MaterialLoader.GetTexture(textureName, srgbRead: true, streaming: true));

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

        // todo: set this when viewer checkbox is toggled
        public void SetWireframe(bool isWireframe)
        {
            foreach (var renderer in renderers)
            {
                renderer.SetWireframe(isWireframe);
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.SetWireframe(isWireframe);
            }
        }

        public void Delete()
        {
            foreach (var renderer in renderers)
            {
                renderer.Delete();
            }

            foreach (var childRenderer in childRenderers)
            {
                childRenderer.Delete();
            }
        }

        public AABB LocalBoundingBox => Simulation.LocalBoundingBox;
        public bool IsFinished() => Simulation.IsFinished();
        public bool HasFinishedEmitting(bool emissionEndIsEnough, bool includeChildren) => Simulation.HasFinishedEmitting(emissionEndIsEnough, includeChildren);
        public ControlPoint MainControlPoint { get => Simulation.MainControlPoint; set => Simulation.MainControlPoint = value; }
        public ControlPoint GetControlPoint(int cp) => Simulation.GetControlPoint(cp);
        public void SetCameraPosition(Vector3 position) => Simulation.SetCameraPosition(position);
        public void SetDetailLevel(ParticleDetailLevel level) => Simulation.SetDetailLevel(level);
        public void Update(float frameTime, float worldTime) => Simulation.Update(frameTime, worldTime);
        public void Restart() => Simulation.Restart();
        public void Replay() => Simulation.Replay();
        public void Stop() => Simulation.Stop();
        public void PlayEndCap() => Simulation.PlayEndCap();
        public void PlayEndCapOnly() => Simulation.PlayEndCapOnly();
    }
}
