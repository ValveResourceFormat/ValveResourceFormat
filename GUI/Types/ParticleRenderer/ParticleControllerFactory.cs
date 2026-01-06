using System.Diagnostics.CodeAnalysis;
using GUI.Types.ParticleRenderer.Emitters;
using GUI.Types.ParticleRenderer.ForceGenerators;
using GUI.Types.ParticleRenderer.Initializers;
using GUI.Types.ParticleRenderer.Operators;
using GUI.Types.ParticleRenderer.PreEmissionOperators;
using GUI.Types.ParticleRenderer.Renderers;
using GUI.Utils;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.ParticleRenderer
{
    static class ParticleControllerFactory
    {
        // These can all be found in particle.dll

        // Register particle emitters
        private static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionEmitter>> EmitterDictionary
            = new()
            {
                ["C_OP_InstantaneousEmitter"] = (emitterInfo) => new InstantaneousEmitter(emitterInfo),
                ["C_OP_ContinuousEmitter"] = (emitterInfo) => new ContinuousEmitter(emitterInfo),
                ["C_OP_NoiseEmitter"] = (emitterInfo) => new NoiseEmitter(emitterInfo),
            };

        // Register particle initializers
        private static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionInitializer>> InitializerDictionary
            = new()
            {
                ["C_INIT_AddVectorToVector"] = initializerInfo => new AddVectorToVector(initializerInfo),
                ["C_INIT_CreateAlongPath"] = initializerInfo => new CreateAlongPath(initializerInfo),
                ["C_INIT_CreateOnGrid"] = initializerInfo => new CreateOnGrid(initializerInfo),
                ["C_INIT_CreateWithinBox"] = initializerInfo => new CreateWithinBox(initializerInfo),
                ["C_INIT_CreateWithinSphere"] = initializerInfo => new CreateWithinSphere(initializerInfo),
                ["C_INIT_CreateWithinSphereTransform"] = initializerInfo => new CreateWithinSphereTransform(initializerInfo),
                ["C_INIT_InitFloat"] = initializerInfo => new InitFloat(initializerInfo),
                ["C_INIT_InitFloatCollection"] = initializerInfo => new InitFloat(initializerInfo), // initfloat but the numberprovider has fewer options
                ["C_INIT_InitVec"] = initializerInfo => new InitVec(initializerInfo),
                ["C_INIT_InitialVelocityNoise"] = initializerInfo => new InitialVelocityNoise(initializerInfo),
                ["C_INIT_OffsetVectorToVector"] = initializerInfo => new OffsetVectorToVector(initializerInfo),
                ["C_INIT_PointList"] = initializerInfo => new PointList(initializerInfo),
                ["C_INIT_PositionOffset"] = initializerInfo => new PositionOffset(initializerInfo),
                ["C_INIT_RandomAlpha"] = initializerInfo => new RandomAlpha(initializerInfo),
                ["C_INIT_RandomColor"] = initializerInfo => new RandomColor(initializerInfo),
                ["C_INIT_RandomLifeTime"] = initializerInfo => new RandomLifeTime(initializerInfo),
                ["C_INIT_RandomRadius"] = initializerInfo => new RandomRadius(initializerInfo),
                ["C_INIT_RandomRotation"] = initializerInfo => new RandomRotation(initializerInfo),
                ["C_INIT_RandomRotationSpeed"] = initializerInfo => new RandomRotationSpeed(initializerInfo),
                ["C_INIT_RandomScalar"] = initializerInfo => new RandomScalar(initializerInfo),
                ["C_INIT_RandomSequence"] = initializerInfo => new RandomSequence(initializerInfo),
                ["C_INIT_RandomTrailLength"] = initializerInfo => new RandomTrailLength(initializerInfo),
                ["C_INIT_RandomVector"] = initializerInfo => new RandomVector(initializerInfo),
                ["C_INIT_RandomVectorComponent"] = initializerInfo => new RandomVectorComponent(initializerInfo),
                ["C_INIT_RandomYaw"] = initializerInfo => new RandomRotation(initializerInfo), // Same as RandomRotation
                ["C_INIT_RandomYawFlip"] = initializerInfo => new RandomYawFlip(initializerInfo),
                ["C_INIT_RemapScalar"] = initializerInfo => new RemapScalar(initializerInfo),
                ["C_INIT_RemapSpeedToScalar"] = initializerInfo => new RemapSpeedToScalar(initializerInfo),
                ["C_INIT_RemapParticleCountToScalar"] = initializerInfo => new RemapParticleCountToScalar(initializerInfo),
                ["C_INIT_RingWave"] = initializerInfo => new RingWave(initializerInfo),
                ["C_INIT_VelocityRadialRandom"] = initializerInfo => new VelocityRadialRandom(initializerInfo),
                ["C_INIT_VelocityRandom"] = initializerInfo => new VelocityRandom(initializerInfo),
            };

        // Register particle operators
        private static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionOperator>> OperatorDictionary
            = new()
            {
                ["C_OP_AlphaDecay"] = operatorInfo => new AlphaDecay(operatorInfo),
                ["C_OP_BasicMovement"] = operatorInfo => new BasicMovement(operatorInfo),
                ["C_OP_ClampScalar"] = operatorInfo => new ClampScalar(operatorInfo),
                ["C_OP_ColorInterpolate"] = operatorInfo => new ColorInterpolate(operatorInfo),
                ["C_OP_ColorInterpolateRandom"] = operatorInfo => new ColorInterpolateRandom(operatorInfo),
                ["C_OP_Decay"] = operatorInfo => new Decay(operatorInfo),
                ["C_OP_DistanceCull"] = operatorInfo => new DistanceCull(operatorInfo),
                ["C_OP_DistanceToCP"] = operatorInfo => new DistanceToCP(operatorInfo),
                ["C_OP_FadeAndKill"] = operatorInfo => new FadeAndKill(operatorInfo),
                ["C_OP_FadeAndKillForTracers"] = operatorInfo => new FadeAndKill(operatorInfo), // alias to C_OP_FadeAndKill
                ["C_OP_FadeIn"] = operatorInfo => new FadeInRandom(operatorInfo),
                ["C_OP_FadeInSimple"] = operatorInfo => new FadeInSimple(operatorInfo),
                ["C_OP_FadeOut"] = operatorInfo => new FadeOutRandom(operatorInfo),
                ["C_OP_FadeOutSimple"] = operatorInfo => new FadeOutSimple(operatorInfo),
                ["C_OP_InterpolateRadius"] = operatorInfo => new InterpolateRadius(operatorInfo),
                ["C_OP_LerpScalar"] = operatorInfo => new LerpScalar(operatorInfo),
                ["C_OP_LerpToOtherAttribute"] = operatorInfo => new LerpToOtherAttribute(operatorInfo),
                ["C_OP_LerpVector"] = operatorInfo => new LerpVector(operatorInfo),
                ["C_OP_MaxVelocity"] = operatorInfo => new MaxVelocity(operatorInfo),
                ["C_OP_Noise"] = operatorInfo => new Noise(operatorInfo),
                ["C_OP_NormalizeVector"] = operatorInfo => new NormalizeVector(operatorInfo),
                ["C_OP_OscillateScalar"] = operatorInfo => new OscillateScalar(operatorInfo),
                ["C_OP_OscillateScalarSimple"] = operatorInfo => new OscillateScalarSimple(operatorInfo),
                ["C_OP_OscillateVector"] = operatorInfo => new OscillateVector(operatorInfo),
                ["C_OP_OscillateVectorSimple"] = operatorInfo => new OscillateVectorSimple(operatorInfo),
                ["C_OP_PlaneCull"] = operatorInfo => new PlaneCull(operatorInfo),
                //["C_OP_PositionLock"] = operatorInfo => new PositionLock(operatorInfo), // This is breaking positioning effects, needs to be rewritten
                ["C_OP_QuantizeFloat"] = operatorInfo => new QuantizeFloat(operatorInfo),
                ["C_OP_RampScalarLinearSimple"] = operatorInfo => new RampScalarLinearSimple(operatorInfo),
                ["C_OP_RemapCrossProductOfTwoVectorsToVector"] = operatorInfo => new RemapCrossProductOfTwoVectorsToVector(operatorInfo),
                ["C_OP_RemapControlPointDirectionToVector"] = operatorInfo => new RemapControlPointDirectionToVector(operatorInfo),
                ["C_OP_RemapParticleCountToScalar"] = operatorInfo => new OpRemapParticleCountToScalar(operatorInfo),
                ["C_OP_RemapSpeed"] = operatorInfo => new RemapSpeed(operatorInfo),
                ["C_OP_RotateVector"] = operatorInfo => new RotateVector(operatorInfo),
                ["C_OP_SetAttributeToScalarExpression"] = operatorInfo => new SetAttributeToScalarExpression(operatorInfo),
                ["C_OP_SetFloat"] = operatorInfo => new SetFloat(operatorInfo),
                ["C_OP_SetFloatCollection"] = operatorInfo => new SetFloat(operatorInfo), // same as initfloatcollection
                ["C_OP_SetVec"] = operatorInfo => new SetVec(operatorInfo),
                ["C_OP_Spin"] = operatorInfo => new Spin(operatorInfo),
                ["C_OP_SpinUpdate"] = operatorInfo => new SpinUpdate(operatorInfo),
                ["C_OP_SpinYaw"] = operatorInfo => new SpinYaw(operatorInfo),
                ["C_OP_VelocityDecay"] = operatorInfo => new VelocityDecay(operatorInfo),
            };

        // Register particle force generators
        private static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionOperator>> ForceGeneratorDictionary
            = new()
            {
                ["C_OP_AttractToControlPoint"] = forceGeneratorInfo => new AttractToControlPoint(forceGeneratorInfo),
                ["C_OP_RandomForce"] = forceGeneratorInfo => new RandomForce(forceGeneratorInfo),
            };

        // Register particle renderers
        private static readonly Dictionary<string, Func<ParticleDefinitionParser, VrfGuiContext, ParticleFunctionRenderer>> RendererDictionary
            = new()
            {
                ["C_OP_RenderSprites"] = (rendererInfo, vrfGuiContext) => new RenderSprites(rendererInfo, vrfGuiContext),
                ["C_OP_RenderTrails"] = (rendererInfo, vrfGuiContext) => new RenderTrails(rendererInfo, vrfGuiContext),
            };

        // Register particle pre-emission operators (mostly stuff with control points)
        private static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionPreEmissionOperator>> PreEmissionOperatorDictionary
            = new()
            {
                ["C_OP_DistanceBetweenCPsToCP"] = preEmissionOperatorInfo => new DistanceBetweenCPsToCP(preEmissionOperatorInfo),
                ["C_OP_RampCPLinearRandom"] = preEmissionOperatorInfo => new RampCPLinearRandom(preEmissionOperatorInfo),
                ["C_OP_SetControlPointPositions"] = preEmissionOperatorInfo => new SetControlPointPositions(preEmissionOperatorInfo),
                ["C_OP_SetControlPointRotation"] = preEmissionOperatorInfo => new SetControlPointRotation(preEmissionOperatorInfo),
                ["C_OP_SetControlPointToVectorExpression"] = preEmissionOperatorInfo => new SetControlPointToVectorExpression(preEmissionOperatorInfo),
                ["C_OP_SetRandomControlPointPosition"] = preEmissionOperatorInfo => new SetRandomControlPointPosition(preEmissionOperatorInfo),
                ["C_OP_SetSingleControlPointPosition"] = preEmissionOperatorInfo => new SetSingleControlPointPosition(preEmissionOperatorInfo),
                ["C_OP_StopAfterCPDuration"] = preEmissionOperatorInfo => new StopAfterDuration(preEmissionOperatorInfo),
            };

        public static bool TryCreateEmitter(string name, KVObject emitterInfo, [MaybeNullWhen(false)] out ParticleFunctionEmitter emitter)
        {
            if (EmitterDictionary.TryGetValue(name, out var factory))
            {
                emitter = factory(new ParticleDefinitionParser(emitterInfo));
                return true;
            }

            emitter = default;
            return false;
        }

        public static bool TryCreateInitializer(string name, KVObject initializerInfo, [MaybeNullWhen(false)] out ParticleFunctionInitializer initializer)
        {
            if (InitializerDictionary.TryGetValue(name, out var factory))
            {
                initializer = factory(new ParticleDefinitionParser(initializerInfo));
                return true;
            }

            initializer = default;
            return false;
        }

        public static bool TryCreateOperator(string name, KVObject operatorInfo, [MaybeNullWhen(false)] out ParticleFunctionOperator @operator)
        {
            if (OperatorDictionary.TryGetValue(name, out var factory))
            {
                @operator = factory(new ParticleDefinitionParser(operatorInfo));
                return true;
            }

            @operator = default;
            return false;
        }

        public static bool TryCreateForceGenerator(string name, KVObject forceGeneratorInfo, [MaybeNullWhen(false)] out ParticleFunctionOperator @operator)
        {
            if (ForceGeneratorDictionary.TryGetValue(name, out var factory))
            {
                @operator = factory(new ParticleDefinitionParser(forceGeneratorInfo));
                return true;
            }

            @operator = default;
            return false;
        }

        public static bool TryCreateRender(string name, KVObject rendererInfo, VrfGuiContext vrfGuiContext, [MaybeNullWhen(false)] out ParticleFunctionRenderer renderer)
        {
            if (RendererDictionary.TryGetValue(name, out var factory))
            {
                renderer = factory(new ParticleDefinitionParser(rendererInfo), vrfGuiContext);
                return true;
            }

            renderer = default;
            return false;
        }
        public static bool TryCreatePreEmissionOperator(string name, KVObject preEmissionOperatorInfo, [MaybeNullWhen(false)] out ParticleFunctionPreEmissionOperator preEmissionOperator)
        {
            if (PreEmissionOperatorDictionary.TryGetValue(name, out var factory))
            {
                preEmissionOperator = factory(new ParticleDefinitionParser(preEmissionOperatorInfo));
                return true;
            }

            preEmissionOperator = default;
            return false;
        }
    }
}
