using System;
using System.Collections.Generic;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.Renderers;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    public class ParticleControllerFactory
    {
        // Register particle emitters
        private static readonly IDictionary<string, Func<IKeyValueCollection, IKeyValueCollection, IParticleEmitter>> EmitterDictionary
            = new Dictionary<string, Func<IKeyValueCollection, IKeyValueCollection, IParticleEmitter>>
            {
                ["C_OP_InstantaneousEmitter"] = (baseProperties, emitterInfo) => new InstantaneousEmitter(baseProperties, emitterInfo),
            };

        // Register particle initializers
        private static readonly IDictionary<string, Func<IKeyValueCollection, IParticleInitializer>> InitializerDictionary
            = new Dictionary<string, Func<IKeyValueCollection, IParticleInitializer>>
            {
                ["C_INIT_RandomLifeTime"] = initializerInfo => new RandomLifeTime(initializerInfo),
                ["C_INIT_CreateWithinSphere"] = initializerInfo => new CreateWithinSphere(initializerInfo),
            };

        // Register particle operators
        private static readonly IDictionary<string, Func<IKeyValueCollection, IParticleOperator>> OperatorDictionary
            = new Dictionary<string, Func<IKeyValueCollection, IParticleOperator>>
            {
                ["C_OP_Decay"] = emitterInfo => new Decay(emitterInfo),
                ["C_OP_BasicMovement"] = emitterInfo => new BasicMovement(emitterInfo),
                ["C_OP_InterpolateRadius"] = emitterInfo => new InterpolateRadius(emitterInfo),
                ["C_OP_FadeAndKill"] = emitterInfo => new FadeAndKill(emitterInfo),
            };

        // Register particle renderers
        private static readonly IDictionary<string, Func<IKeyValueCollection, VrfGuiContext, IParticleRenderer>> RendererDictionary
            = new Dictionary<string, Func<IKeyValueCollection, VrfGuiContext, IParticleRenderer>>
            {
                ["C_OP_RenderSprites"] = (rendererInfo, vrfGuiContext) => new RenderSprites(rendererInfo, vrfGuiContext),
            };

        public static bool TryCreateEmitter(string name, IKeyValueCollection baseProperties, IKeyValueCollection emitterInfo, out IParticleEmitter emitter)
        {
            if (EmitterDictionary.TryGetValue(name, out var factory))
            {
                emitter = factory(baseProperties, emitterInfo);
                return true;
            }

            emitter = default;
            return false;
        }

        public static bool TryCreateInitializer(string name, IKeyValueCollection initializerInfo, out IParticleInitializer initializer)
        {
            if (InitializerDictionary.TryGetValue(name, out var factory))
            {
                initializer = factory(initializerInfo);
                return true;
            }

            initializer = default;
            return false;
        }

        public static bool TryCreateOperator(string name, IKeyValueCollection operatorInfo, out IParticleOperator @operator)
        {
            if (OperatorDictionary.TryGetValue(name, out var factory))
            {
                @operator = factory(operatorInfo);
                return true;
            }

            @operator = default;
            return false;
        }

        public static bool TryCreateRender(string name, IKeyValueCollection rendererInfo, VrfGuiContext vrfGuiContext, out IParticleRenderer renderer)
        {
            if (RendererDictionary.TryGetValue(name, out var factory))
            {
                renderer = factory(rendererInfo, vrfGuiContext);
                return true;
            }

            renderer = default;
            return false;
        }
    }
}
