using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Types.ParticleRenderer.PreEmissionOperators;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleRenderer : IRenderer
    {
        public IEnumerable<IParticlePreEmissionOperator> PreEmissionOperators { get; private set; } = new List<IParticlePreEmissionOperator>();
        public IEnumerable<IParticleEmitter> Emitters { get; private set; } = new List<IParticleEmitter>();

        public IEnumerable<IParticleInitializer> Initializers { get; private set; } = new List<IParticleInitializer>();

        public IEnumerable<IParticleOperator> Operators { get; private set; } = new List<IParticleOperator>();

        public IEnumerable<IParticleRenderer> Renderers { get; private set; } = new List<IParticleRenderer>();

        public AABB LocalBoundingBox { get; private set; }

        public Vector3 Position
        {
            get => systemRenderState.GetControlPoint(0).Position;
            set
            {
                systemRenderState.SetControlPointValue(0, value);
                foreach (var child in childParticleRenderers)
                {
                    child.Position = value;
                }
            }
        }

        private readonly List<ParticleRenderer> childParticleRenderers;
        private readonly VrfGuiContext vrfGuiContext;
        private bool hasStarted;

        private readonly ParticleBag particleBag;
        private int particlesEmitted;
        private ParticleSystemRenderState systemRenderState;

        public ParticleRenderer(ParticleSystem particleSystem, VrfGuiContext vrfGuiContext)
        {
            childParticleRenderers = new List<ParticleRenderer>();
            this.vrfGuiContext = vrfGuiContext;

            var parser = new ParticleSystemDefinitionParser(particleSystem.Data);
            ParseDefinition(parser);

            systemRenderState = new ParticleSystemRenderState(parser)
            {
                EndEarly = false
            };

            particleBag = new ParticleBag(systemRenderState.MaxParticles, true);

            SetupEmitters(particleSystem.GetEmitters());
            SetupInitializers(particleSystem.GetInitializers());
            SetupOperators(particleSystem.GetOperators());
            SetupRenderers(particleSystem.GetRenderers());
            SetupPreEmissionOperators(particleSystem.GetPreEmissionOperators());

            SetupChildParticles(particleSystem.GetChildParticleNames(true));
        }

        private void ParseDefinition(ParticleSystemDefinitionParser parser)
        {
            LocalBoundingBox = new AABB(
                parser.Vector3("m_BoundingBoxMin", new Vector3(-10)),
                parser.Vector3("m_BoundingBoxMin", new Vector3(10))
            );
        }

        public void Start()
        {
            foreach (var emitter in Emitters)
            {
                emitter.Start(EmitParticle);
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Start();
            }
        }

        private void EmitParticle()
        {
            var index = particleBag.Add();
            if (index < 0)
            {
                Console.WriteLine("Out of space in particle bag");
                return;
            }

            if (systemRenderState.ParticleCount >= systemRenderState.MaxParticles)
            {
                return;
            }

            particleBag.LiveParticles[index].ParticleCount = particlesEmitted++;
            systemRenderState.ParticleCount += 1;
            InitializeParticle(ref particleBag.LiveParticles[index]);
        }

        private void InitializeParticle(ref Particle p)
        {
            p.Age = 0f;
            p.MarkedAsKilled = false;
            p.Position = systemRenderState.GetControlPoint(0).Position;

            foreach (var initializer in Initializers)
            {
                initializer.Initialize(ref p, systemRenderState);
            }
        }

        public void Stop()
        {
            foreach (var emitter in Emitters)
            {
                emitter.Stop();
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Stop();
            }
        }

        public void Restart()
        {
            Stop();
            systemRenderState.Age = 0;
            particleBag.Clear();
            Start();

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Restart();
            }
        }

        public void Update(float frameTime)
        {
            if (!hasStarted)
            {
                Start();
                hasStarted = true;
            }

            systemRenderState.Age += frameTime;

            foreach (var preEmissionOperator in PreEmissionOperators)
            {
                preEmissionOperator.Operate(ref systemRenderState, frameTime);
            }

            foreach (var emitter in Emitters)
            {
                emitter.Update(frameTime);
            }

            foreach (var particleOperator in Operators)
            {
                particleOperator.Update(particleBag.LiveParticles, frameTime, systemRenderState);
            }

            // Increase age of all particles
            for (var i = 0; i < particleBag.LiveParticles.Length; ++i)
            {
                particleBag.LiveParticles[i].Age += frameTime;
            }

            // Remove all dead particles
            particleBag.PruneExpired();

#if DEBUG
            // Some particles may not be being killed correctly,
            // break in debugger here to verify whether some operator is not marking particles as killed
            if (particleBag.Count > 5000)
            {
                System.Diagnostics.Debugger.Break();
            }
#endif

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.Update(frameTime);
                LocalBoundingBox = LocalBoundingBox.Union(childParticleRenderer.LocalBoundingBox);
            }

            // TODO: Is this the correct place for this because child particle renderers also check this
            if (systemRenderState.EndEarly && systemRenderState.Age > systemRenderState.Duration)
            {
                if (systemRenderState.DestroyInstantlyOnEnd)
                {
                    Restart();
                }
                else
                {
                    Stop();
                }
            }

            systemRenderState.ParticleCount = particleBag.Count;
        }

        public bool IsFinished()
            => Emitters.All(e => e.IsFinished)
            && particleBag.Count == 0
            && childParticleRenderers.All(r => r.IsFinished());

        public void Render(Camera camera, RenderPass renderPass)
        {
            if (renderPass == RenderPass.Translucent || renderPass == RenderPass.Both)
            {
                foreach (var childParticleRenderer in childParticleRenderers)
                {
                    childParticleRenderer.Render(camera, renderPass);
                }

                if (particleBag.Count > 0)
                {
                    foreach (var renderer in Renderers)
                    {
                        renderer.Render(particleBag, systemRenderState, camera.ViewProjectionMatrix, camera.CameraViewMatrix);
                    }
                }
            }
        }

        public IEnumerable<string> GetSupportedRenderModes()
            => Renderers
                .SelectMany(renderer => renderer.GetSupportedRenderModes())
                .Concat(childParticleRenderers.SelectMany(child => child.GetSupportedRenderModes()))
                .Distinct();

        public void SetRenderMode(string renderMode)
        {
            foreach (var renderer in Renderers)
            {
                renderer.SetRenderMode(renderMode);
            }

            foreach (var childParticleRenderer in childParticleRenderers)
            {
                childParticleRenderer.SetRenderMode(renderMode);
            }
        }

        private void SetupEmitters(IEnumerable<IKeyValueCollection> emitterData)
        {
            var emitters = new List<IParticleEmitter>();

            foreach (var emitterInfo in emitterData)
            {
                if (IsOperatorDisabled(emitterInfo))
                {
                    continue;
                }

                var emitterClass = emitterInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateEmitter(emitterClass, emitterInfo, out var emitter))
                {
                    emitters.Add(emitter);
                }
                else
                {
                    Console.WriteLine($"Unsupported emitter class '{emitterClass}'.");
                }
            }

            Emitters = emitters;
        }

        private void SetupInitializers(IEnumerable<IKeyValueCollection> initializerData)
        {
            var initializers = new List<IParticleInitializer>();

            foreach (var initializerInfo in initializerData)
            {
                if (IsOperatorDisabled(initializerInfo))
                {
                    continue;
                }

                var initializerClass = initializerInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateInitializer(initializerClass, initializerInfo, out var initializer))
                {
                    initializers.Add(initializer);
                }
                else
                {
                    Console.WriteLine($"Unsupported initializer class '{initializerClass}'.");
                }
            }

            Initializers = initializers;
        }

        private void SetupOperators(IEnumerable<IKeyValueCollection> operatorData)
        {
            var operators = new List<IParticleOperator>();

            foreach (var operatorInfo in operatorData)
            {
                if (IsOperatorDisabled(operatorInfo))
                {
                    continue;
                }

                var operatorClass = operatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateOperator(operatorClass, operatorInfo, out var @operator))
                {
                    operators.Add(@operator);
                }
                else
                {
                    Console.WriteLine($"Unsupported operator class '{operatorClass}'.");
                }
            }

            Operators = operators;
        }

        private void SetupRenderers(IEnumerable<IKeyValueCollection> rendererData)
        {
            var renderers = new List<IParticleRenderer>();

            foreach (var rendererInfo in rendererData)
            {
                if (IsOperatorDisabled(rendererInfo))
                {
                    continue;
                }

                var rendererClass = rendererInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreateRender(rendererClass, rendererInfo, vrfGuiContext, out var renderer))
                {
                    renderers.Add(renderer);
                }
                else
                {
                    Console.WriteLine($"Unsupported renderer class '{rendererClass}'.");
                }
            }

            Renderers = renderers;
        }
        private void SetupPreEmissionOperators(IEnumerable<IKeyValueCollection> preEmissionOperatorData)
        {
            var preEmissionOperators = new List<IParticlePreEmissionOperator>();

            foreach (var preEmissionOperatorInfo in preEmissionOperatorData)
            {
                if (IsOperatorDisabled(preEmissionOperatorInfo))
                {
                    continue;
                }

                var preEmissionOperatorClass = preEmissionOperatorInfo.GetProperty<string>("_class");
                if (ParticleControllerFactory.TryCreatePreEmissionOperator(preEmissionOperatorClass, preEmissionOperatorInfo, out var preEmissionOperator))
                {
                    preEmissionOperators.Add(preEmissionOperator);
                }
                else
                {
                    Console.WriteLine($"Unsupported pre-emission operator class '{preEmissionOperatorClass}'.");
                }
            }

            PreEmissionOperators = preEmissionOperators;
        }

        private void SetupChildParticles(IEnumerable<string> childNames)
        {
            foreach (var childName in childNames)
            {
                var childResource = vrfGuiContext.LoadFileByAnyMeansNecessary(childName + "_c");

                if (childResource == null)
                {
                    continue;
                }

                var childSystem = (ParticleSystem)childResource.DataBlock;

                childParticleRenderers.Add(new ParticleRenderer(childSystem, vrfGuiContext));
            }
        }

        private static bool IsOperatorDisabled(IKeyValueCollection op)
        {
            if (op.ContainsKey("m_bDisableOperator"))
            {
                return op.GetProperty<bool>("m_bDisableOperator");
            }
            // Skip ops that only run during endcap (currently unsupported)
            else if (op.ContainsKey("m_nOpEndCapState"))
            {
                var mode = op.GetEnumValue<ParticleEndCapMode>("m_nOpEndCapState");
                return mode == ParticleEndCapMode.PARTICLE_ENDCAP_ENDCAP_ON;
            }
            return false;
        }

        public void SetWireframe(bool isWireframe)
        {
            foreach (var renderer in Renderers)
            {
                renderer.SetWireframe(isWireframe);
            }
            foreach (var childRenderer in childParticleRenderers)
            {
                childRenderer.SetWireframe(isWireframe);
            }
        }
    }
}
