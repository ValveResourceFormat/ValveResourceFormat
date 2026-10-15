using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using ValveKeyValue;
using ValveResourceFormat.Particles.Constraints;
using ValveResourceFormat.Particles.Emitters;
using ValveResourceFormat.Particles.ForceGenerators;
using ValveResourceFormat.Particles.Initializers;
using ValveResourceFormat.Particles.Operators;
using ValveResourceFormat.Particles.PreEmissionOperators;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Factory for creating particle function instances (emitters, initializers, operators, renderers) by their Source 2 class name.
    /// </summary>
    static class ParticleControllerFactory
    {
        // These can all be found in particle.dll

        // Below, dictionaries and TryCreateX methods are ordered to match the CS2 particle editor (PET):
        // pre-emission first, then emit/init/operate, forces, constraints, and renderers last.

        // Register particle pre-emission operators (mostly stuff with control points)
        internal static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionPreEmissionOperator>> PreEmissionOperatorDictionary
            = new()
            {
                ["C_OP_ChooseRandomChildrenInGroup"] = preEmissionOperatorInfo => new ChooseRandomChildrenInGroup(preEmissionOperatorInfo),
                ["C_OP_ControlPointToRadialScreenSpace"] = preEmissionOperatorInfo => new ControlPointToRadialScreenSpace(preEmissionOperatorInfo),
                ["C_OP_DistanceBetweenCPsToCP"] = preEmissionOperatorInfo => new DistanceBetweenCPsToCP(preEmissionOperatorInfo),
                ["C_OP_DriveCPFromGlobalSoundFloat"] = preEmissionOperatorInfo => new DriveCPFromGlobalSoundFloat(preEmissionOperatorInfo),
                ["C_OP_EnableChildrenFromParentParticleCount"] = preEmissionOperatorInfo => new EnableChildrenFromParentParticleCount(preEmissionOperatorInfo),
                ["C_OP_ForceControlPointStub"] = preEmissionOperatorInfo => new ForceControlPointStub(preEmissionOperatorInfo),
                ["C_OP_HSVShiftToCP"] = preEmissionOperatorInfo => new HSVShiftToCP(preEmissionOperatorInfo),
                ["C_OP_LightningSnapshotGenerator"] = preEmissionOperatorInfo => new LightningSnapshotGenerator(preEmissionOperatorInfo),
                ["C_OP_RampCPLinearRandom"] = preEmissionOperatorInfo => new RampCPLinearRandom(preEmissionOperatorInfo),
                ["C_OP_RemapAverageScalarValuetoCP"] = preEmissionOperatorInfo => new RemapAverageScalarValuetoCP(preEmissionOperatorInfo),
                ["C_OP_RemapBoundingVolumetoCP"] = preEmissionOperatorInfo => new RemapBoundingVolumetoCP(preEmissionOperatorInfo),
                ["C_OP_RemapCPtoCP"] = preEmissionOperatorInfo => new RemapCPtoCP(preEmissionOperatorInfo),
                ["C_OP_RemapDotProductToCP"] = preEmissionOperatorInfo => new RemapDotProductToCP(preEmissionOperatorInfo),
                ["C_OP_RemapSpeedtoCP"] = preEmissionOperatorInfo => new RemapSpeedtoCP(preEmissionOperatorInfo),
                ["C_OP_RepeatedTriggerChildGroup"] = preEmissionOperatorInfo => new RepeatedTriggerChildGroup(preEmissionOperatorInfo),
                ["C_OP_SelectivelyEnableChildren"] = preEmissionOperatorInfo => new SelectivelyEnableChildren(preEmissionOperatorInfo),
                ["C_OP_SetControlPointFieldFromVectorExpression"] = preEmissionOperatorInfo => new SetControlPointFieldFromVectorExpression(preEmissionOperatorInfo),
                ["C_OP_SetControlPointFieldToScalarExpression"] = preEmissionOperatorInfo => new SetControlPointFieldToScalarExpression(preEmissionOperatorInfo),
                ["C_OP_SetControlPointFieldToWater"] = preEmissionOperatorInfo => new SetControlPointFieldToWater(preEmissionOperatorInfo),
                ["C_OP_SetControlPointOrientationToCPVelocity"] = preEmissionOperatorInfo => new SetControlPointOrientationToCPVelocity(preEmissionOperatorInfo),
                ["C_OP_SetControlPointPositionToRandomActiveCP"] = preEmissionOperatorInfo => new SetControlPointPositionToRandomActiveCP(preEmissionOperatorInfo),
                ["C_OP_SetCPOrientationToPointAtCP"] = preEmissionOperatorInfo => new SetCPOrientationToPointAtCP(preEmissionOperatorInfo),
                ["C_OP_SetControlPointPositions"] = preEmissionOperatorInfo => new SetControlPointPositions(preEmissionOperatorInfo),
                ["C_OP_SetControlPointRotation"] = preEmissionOperatorInfo => new SetControlPointRotation(preEmissionOperatorInfo),
                ["C_OP_SetControlPointToCenter"] = preEmissionOperatorInfo => new SetControlPointToCenter(preEmissionOperatorInfo),
                ["C_OP_SetControlPointToPlayer"] = preEmissionOperatorInfo => new SetControlPointToPlayer(preEmissionOperatorInfo),
                ["C_OP_SetControlPointToVectorExpression"] = preEmissionOperatorInfo => new SetControlPointToVectorExpression(preEmissionOperatorInfo),
                ["C_OP_SetControlPointOrientation"] = preEmissionOperatorInfo => new SetControlPointOrientation(preEmissionOperatorInfo),
                ["C_OP_PlayEndCapWhenFinished"] = preEmissionOperatorInfo => new PlayEndCapWhenFinished(preEmissionOperatorInfo),
                ["C_OP_SetGravityToCP"] = preEmissionOperatorInfo => new SetGravityToCP(preEmissionOperatorInfo),
                ["C_OP_SetParentControlPointsToChildCP"] = preEmissionOperatorInfo => new SetParentControlPointsToChildCP(preEmissionOperatorInfo),
                ["C_OP_SetRandomControlPointPosition"] = preEmissionOperatorInfo => new SetRandomControlPointPosition(preEmissionOperatorInfo),
                ["C_OP_SetSimulationRate"] = preEmissionOperatorInfo => new SetSimulationRate(preEmissionOperatorInfo),
                ["C_OP_SetSingleControlPointPosition"] = preEmissionOperatorInfo => new SetSingleControlPointPosition(preEmissionOperatorInfo),
                ["C_OP_StopAfterCPDuration"] = preEmissionOperatorInfo => new StopAfterDuration(preEmissionOperatorInfo),
            };

        // Register particle emitters
        internal static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionEmitter>> EmitterDictionary
            = new()
            {
                ["C_OP_InstantaneousEmitter"] = (emitterInfo) => new InstantaneousEmitter(emitterInfo),
                ["C_OP_ContinuousEmitter"] = (emitterInfo) => new ContinuousEmitter(emitterInfo),
                ["C_OP_MaintainEmitter"] = emitterInfo => new MaintainEmitter(emitterInfo),
                ["C_OP_NoiseEmitter"] = (emitterInfo) => new NoiseEmitter(emitterInfo),
            };

        // Register particle initializers
        internal static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionInitializer>> InitializerDictionary
            = new()
            {
                ["C_INIT_AgeNoise"] = initializerInfo => new AgeNoise(initializerInfo),
                ["C_INIT_ColorLitPerParticle"] = initializerInfo => new ColorLitPerParticle(initializerInfo),
                ["C_INIT_CreateAlongPath"] = initializerInfo => new CreateAlongPath(initializerInfo),
                ["C_INIT_CreateInEpitrochoid"] = initializerInfo => new CreateInEpitrochoid(initializerInfo),
                ["C_INIT_CreateOnGrid"] = initializerInfo => new CreateOnGrid(initializerInfo),
                ["C_INIT_CreateParticleImpulse"] = initializerInfo => new CreateParticleImpulse(initializerInfo),
                ["C_INIT_CreatePhyllotaxis"] = initializerInfo => new CreatePhyllotaxis(initializerInfo),
                ["C_INIT_CreateSequentialPath"] = initializerInfo => new CreateSequentialPath(initializerInfo),
                ["C_INIT_CreateSequentialPathV2"] = initializerInfo => new CreateSequentialPathV2(initializerInfo),
                ["C_INIT_CreateSpiralSphere"] = initializerInfo => new CreateSpiralSphere(initializerInfo),
                ["C_INIT_CreateWithinBox"] = initializerInfo => new CreateWithinBox(initializerInfo),
                ["C_INIT_CreateWithinCone"] = initializerInfo => new CreateWithinCone(initializerInfo),
                ["C_INIT_CreateWithinSphereTransform"] = initializerInfo => new CreateWithinSphereTransform(initializerInfo),
                ["C_INIT_CreateFromCPs"] = initializerInfo => new CreateFromCPs(initializerInfo),
                ["C_INIT_CreateFromParentParticles"] = initializerInfo => new CreateFromParentParticles(initializerInfo),
                ["C_INIT_CreationNoise"] = initializerInfo => new CreationNoise(initializerInfo),
                ["C_INIT_DistanceCull"] = initializerInfo => new Initializers.DistanceCull(initializerInfo),
                ["C_INIT_DistanceToCPInit"] = initializerInfo => new DistanceToCPInit(initializerInfo),
                ["C_INIT_DistanceToNeighborCull"] = initializerInfo => new DistanceToNeighborCull(initializerInfo),
                ["C_INIT_InheritFromParentParticles"] = initializerInfo => new InheritFromParentParticles(initializerInfo),
                ["C_INIT_GlobalScale"] = initializerInfo => new GlobalScale(initializerInfo),
                ["C_INIT_InitFromCPSnapshot"] = initializerInfo => new InitFromCPSnapshot(initializerInfo),
                ["C_INIT_InitFromParentKilled"] = initializerInfo => new InitFromParentKilled(initializerInfo),
                ["C_INIT_InitFloat"] = initializerInfo => new InitFloat(initializerInfo),
                ["C_INIT_InitFloatCollection"] = initializerInfo => new InitFloatCollection(initializerInfo),
                ["C_INIT_InitVec"] = initializerInfo => new InitVec(initializerInfo),
                ["C_INIT_InitVecCollection"] = initializerInfo => new InitVecCollection(initializerInfo),
                ["C_INIT_InitialVelocityNoise"] = initializerInfo => new InitialVelocityNoise(initializerInfo),
                ["C_INIT_Orient2DRelToCP"] = initializerInfo => new Initializers.Orient2DRelToCP(initializerInfo),
                ["C_INIT_PlaneCull"] = initializerInfo => new Initializers.PlaneCull(initializerInfo),
                ["C_INIT_PointList"] = initializerInfo => new PointList(initializerInfo),
                ["C_INIT_PositionOffset"] = initializerInfo => new PositionOffset(initializerInfo),
                ["C_INIT_PositionOffsetToCP"] = initializerInfo => new PositionOffsetToCP(initializerInfo),
                ["C_INIT_PositionPlaceOnGround"] = initializerInfo => new PositionPlaceOnGround(initializerInfo),
                ["C_INIT_PositionWarp"] = initializerInfo => new PositionWarp(initializerInfo),
                ["C_INIT_PositionWarpScalar"] = initializerInfo => new PositionWarpScalar(initializerInfo),
                ["C_INIT_QuantizeFloat"] = initializerInfo => new Initializers.QuantizeFloat(initializerInfo),
                ["C_INIT_RandomColor"] = initializerInfo => new RandomColor(initializerInfo),
                ["C_INIT_RandomSequence"] = initializerInfo => new RandomSequence(initializerInfo),
                ["C_INIT_ScaleVelocity"] = initializerInfo => new ScaleVelocity(initializerInfo),
                ["C_INIT_SequenceLifeTime"] = initializerInfo => new SequenceLifeTime(initializerInfo),
                ["C_INIT_RandomSecondSequence"] = initializerInfo => new RandomSecondSequence(initializerInfo),
                ["C_INIT_RandomVectorComponent"] = initializerInfo => new RandomVectorComponent(initializerInfo),
                ["C_INIT_RandomYawFlip"] = initializerInfo => new RandomYawFlip(initializerInfo),
                ["C_INIT_NormalAlignToCP"] = initializerInfo => new NormalAlignToCP(initializerInfo),
                ["C_INIT_MoveBetweenPoints"] = initializerInfo => new MoveBetweenPoints(initializerInfo),
                ["C_INIT_NormalOffset"] = initializerInfo => new NormalOffset(initializerInfo),
                ["C_INIT_RemapInitialDirectionToTransformToVector"] = initializerInfo => new RemapInitialDirectionToTransformToVector(initializerInfo),
                ["C_INIT_RemapInitialTransformDirectionToRotation"] = initializerInfo => new RemapInitialTransformDirectionToRotation(initializerInfo),
                ["C_INIT_RemapInitialVisibilityScalar"] = initializerInfo => new RemapInitialVisibilityScalar(initializerInfo),
                ["C_INIT_RemapQAnglesToRotation"] = initializerInfo => new RemapQAnglesToRotation(initializerInfo),
                ["C_INIT_RemapScalarToVector"] = initializerInfo => new RemapScalarToVector(initializerInfo),
                ["C_INIT_RemapTransformOrientationToRotations"] = initializerInfo => new Initializers.RemapTransformOrientationToRotations(initializerInfo),
                ["C_INIT_RemapTransformToVector"] = initializerInfo => new RemapTransformToVector(initializerInfo),
                ["C_INIT_RemapParticleCountToScalar"] = initializerInfo => new Initializers.RemapParticleCountToScalar(initializerInfo),
                ["C_INIT_RingWave"] = initializerInfo => new RingWave(initializerInfo),
                ["C_INIT_SequenceFromCP"] = initializerInfo => new SequenceFromCP(initializerInfo),
                ["C_INIT_SetAttributeToScalarExpression"] = initializerInfo => new Initializers.SetAttributeToScalarExpression(initializerInfo),
                ["C_INIT_SetFloatAttributeToVectorExpression"] = initializerInfo => new Initializers.SetFloatAttributeToVectorExpression(initializerInfo),
                ["C_INIT_SetVectorAttributeToVectorExpression"] = initializerInfo => new Initializers.SetVectorAttributeToVectorExpression(initializerInfo),
                ["C_INIT_VelocityFromCP"] = initializerInfo => new VelocityFromCP(initializerInfo),
                ["C_INIT_InheritVelocity"] = initializerInfo => new InheritVelocity(initializerInfo),
                ["C_INIT_VelocityFromNormal"] = initializerInfo => new VelocityFromNormal(initializerInfo),
                ["C_INIT_VelocityRadialRandom"] = initializerInfo => new VelocityRadialRandom(initializerInfo),
                ["C_INIT_VelocityRandom"] = initializerInfo => new VelocityRandom(initializerInfo),
            };

        // Register particle operators
        internal static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionOperator>> OperatorDictionary
            = new()
            {
                ["C_OP_AlphaDecay"] = operatorInfo => new AlphaDecay(operatorInfo),
                ["C_OP_BasicMovement"] = operatorInfo => new BasicMovement(operatorInfo),
                ["C_OP_CalculateVectorAttribute"] = operatorInfo => new CalculateVectorAttribute(operatorInfo),
                ["C_OP_ChladniWave"] = operatorInfo => new ChladniWave(operatorInfo),
                ["C_OP_ClampScalar"] = operatorInfo => new ClampScalar(operatorInfo),
                ["C_OP_ClampVector"] = operatorInfo => new ClampVector(operatorInfo),
                ["C_OP_ColorAdjustHSL"] = operatorInfo => new ColorAdjustHSL(operatorInfo),
                ["C_OP_ColorInterpolate"] = operatorInfo => new ColorInterpolate(operatorInfo),
                ["C_OP_ColorInterpolateRandom"] = operatorInfo => new ColorInterpolateRandom(operatorInfo),
                ["C_OP_CPOffsetToPercentageBetweenCPs"] = operatorInfo => new CPOffsetToPercentageBetweenCPs(operatorInfo),
                ["C_OP_Cull"] = operatorInfo => new Cull(operatorInfo),
                ["C_OP_CycleScalar"] = operatorInfo => new CycleScalar(operatorInfo),
                ["C_OP_ControlpointLight"] = operatorInfo => new ControlpointLight(operatorInfo),
                ["C_OP_CylindricalDistanceToTransform"] = operatorInfo => new CylindricalDistanceToTransform(operatorInfo),
                ["C_OP_DampenToCP"] = operatorInfo => new DampenToCP(operatorInfo),
                ["C_OP_Decay"] = operatorInfo => new Decay(operatorInfo),
                ["C_OP_DecayClampCount"] = operatorInfo => new DecayClampCount(operatorInfo),
                ["C_OP_DecayMaintainCount"] = operatorInfo => new DecayMaintainCount(operatorInfo),
                ["C_OP_DecayOffscreen"] = operatorInfo => new DecayOffscreen(operatorInfo),
                ["C_OP_DifferencePreviousParticle"] = operatorInfo => new DifferencePreviousParticle(operatorInfo),
                ["C_OP_DistanceBetweenTransforms"] = operatorInfo => new DistanceBetweenTransforms(operatorInfo),
                ["C_OP_DistanceBetweenVecs"] = operatorInfo => new DistanceBetweenVecs(operatorInfo),
                ["C_OP_DistanceCull"] = operatorInfo => new Operators.DistanceCull(operatorInfo),
                ["C_OP_DistanceToTransform"] = operatorInfo => new DistanceToTransform(operatorInfo),
                ["C_OP_DragRelativeToPlane"] = operatorInfo => new DragRelativeToPlane(operatorInfo),
                ["C_OP_EndCapDecay"] = operatorInfo => new EndCapDecay(operatorInfo),
                ["C_OP_EndCapTimedDecay"] = operatorInfo => new EndCapTimedDecay(operatorInfo),
                ["C_OP_EndCapTimedFreeze"] = operatorInfo => new EndCapTimedFreeze(operatorInfo),
                ["C_OP_FadeAndKill"] = operatorInfo => new FadeAndKill(operatorInfo),
                ["C_OP_FadeAndKillForTracers"] = operatorInfo => new FadeAndKillForTracers(operatorInfo),
                ["C_OP_FadeIn"] = operatorInfo => new FadeInRandom(operatorInfo),
                ["C_OP_FadeInSimple"] = operatorInfo => new FadeInSimple(operatorInfo),
                ["C_OP_FadeOut"] = operatorInfo => new FadeOutRandom(operatorInfo),
                ["C_OP_FadeOutSimple"] = operatorInfo => new FadeOutSimple(operatorInfo),
                ["C_OP_InterpolateRadius"] = operatorInfo => new InterpolateRadius(operatorInfo),
                ["C_OP_LagCompensation"] = operatorInfo => new LagCompensation(operatorInfo),
                ["C_OP_LazyCullCompareFloat"] = operatorInfo => new LazyCullCompareFloat(operatorInfo),
                ["C_OP_LerpEndCapScalar"] = operatorInfo => new LerpEndCapScalar(operatorInfo),
                ["C_OP_LerpEndCapVector"] = operatorInfo => new LerpEndCapVector(operatorInfo),
                ["C_OP_LerpScalar"] = operatorInfo => new LerpScalar(operatorInfo),
                ["C_OP_LerpToInitialPosition"] = operatorInfo => new LerpToInitialPosition(operatorInfo),
                ["C_OP_LerpToOtherAttribute"] = operatorInfo => new LerpToOtherAttribute(operatorInfo),
                ["C_OP_LerpVector"] = operatorInfo => new LerpVector(operatorInfo),
                ["C_OP_LockPoints"] = operatorInfo => new LockPoints(operatorInfo),
                ["C_OP_LockToSavedSequentialPath"] = operatorInfo => new LockToSavedSequentialPath(operatorInfo),
                ["C_OP_LockToSavedSequentialPathV2"] = operatorInfo => new LockToSavedSequentialPathV2(operatorInfo),
                ["C_OP_MaintainSequentialPath"] = operatorInfo => new MaintainSequentialPath(operatorInfo),
                ["C_OP_MaxVelocity"] = operatorInfo => new MaxVelocity(operatorInfo),
                ["C_OP_MovementLoopInsideSphere"] = operatorInfo => new MovementLoopInsideSphere(operatorInfo),
                ["C_OP_MovementMaintainOffset"] = operatorInfo => new MovementMaintainOffset(operatorInfo),
                ["C_OP_MovementPlaceOnGround"] = operatorInfo => new MovementPlaceOnGround(operatorInfo),
                ["C_OP_MovementRotateParticleAroundAxis"] = operatorInfo => new MovementRotateParticleAroundAxis(operatorInfo),
                ["C_OP_Noise"] = operatorInfo => new Noise(operatorInfo),
                ["C_OP_OrientTo2dDirection"] = operatorInfo => new OrientTo2dDirection(operatorInfo),
                ["C_OP_RadiusDecay"] = operatorInfo => new RadiusDecay(operatorInfo),
                ["C_OP_RemapDotProductToScalar"] = operatorInfo => new RemapDotProductToScalar(operatorInfo),
                ["C_OP_VectorNoise"] = operatorInfo => new VectorNoise(operatorInfo),
                ["C_OP_NormalLock"] = operatorInfo => new NormalLock(operatorInfo),
                ["C_OP_NormalizeVector"] = operatorInfo => new NormalizeVector(operatorInfo),
                ["C_OP_Orient2DRelToCP"] = operatorInfo => new Operators.Orient2DRelToCP(operatorInfo),
                ["C_OP_OscillateScalar"] = operatorInfo => new OscillateScalar(operatorInfo),
                ["C_OP_OscillateScalarSimple"] = operatorInfo => new OscillateScalarSimple(operatorInfo),
                ["C_OP_OscillateVector"] = operatorInfo => new OscillateVector(operatorInfo),
                ["C_OP_OscillateVectorSimple"] = operatorInfo => new OscillateVectorSimple(operatorInfo),
                ["C_OP_PercentageBetweenTransformLerpCPs"] = operatorInfo => new PercentageBetweenTransformLerpCPs(operatorInfo),
                ["C_OP_PercentageBetweenTransforms"] = operatorInfo => new PercentageBetweenTransforms(operatorInfo),
                ["C_OP_PercentageBetweenTransformsVector"] = operatorInfo => new PercentageBetweenTransformsVector(operatorInfo),
                ["C_OP_PinParticleToCP"] = operatorInfo => new PinParticleToCP(operatorInfo),
                ["C_OP_PlaneCull"] = operatorInfo => new Operators.PlaneCull(operatorInfo),
                ["C_OP_PointVectorAtNextParticle"] = operatorInfo => new PointVectorAtNextParticle(operatorInfo),
                ["C_OP_RestartAfterDuration"] = operatorInfo => new RestartAfterDuration(operatorInfo),
                ["C_OP_PositionLock"] = operatorInfo => new PositionLock(operatorInfo),
                ["C_OP_QuantizeFloat"] = operatorInfo => new Operators.QuantizeFloat(operatorInfo),
                ["C_OP_RampScalarLinear"] = operatorInfo => new RampScalarLinear(operatorInfo),
                ["C_OP_RampScalarLinearSimple"] = operatorInfo => new RampScalarLinearSimple(operatorInfo),
                ["C_OP_RampScalarSpline"] = operatorInfo => new RampScalarSpline(operatorInfo),
                ["C_OP_RampScalarSplineSimple"] = operatorInfo => new RampScalarSplineSimple(operatorInfo),
                ["C_OP_ReadFromNeighboringParticle"] = operatorInfo => new ReadFromNeighboringParticle(operatorInfo),
                ["C_OP_RemapCrossProductOfTwoVectorsToVector"] = operatorInfo => new RemapCrossProductOfTwoVectorsToVector(operatorInfo),
                ["C_OP_RemapControlPointDirectionToVector"] = operatorInfo => new RemapControlPointDirectionToVector(operatorInfo),
                ["C_OP_RemapCPtoScalar"] = operatorInfo => new RemapCPtoScalar(operatorInfo),
                ["C_OP_RemapCPtoVector"] = operatorInfo => new RemapCPtoVector(operatorInfo),
                ["C_OP_RemapCPVelocityToVector"] = operatorInfo => new RemapCPVelocityToVector(operatorInfo),
                ["C_OP_RemapControlPointOrientationToRotation"] = operatorInfo => new RemapControlPointOrientationToRotation(operatorInfo),
                ["C_OP_RemapDirectionToCPToVector"] = operatorInfo => new RemapDirectionToCPToVector(operatorInfo),
                ["C_OP_RemapDistanceToLineSegmentToScalar"] = operatorInfo => new RemapDistanceToLineSegmentToScalar(operatorInfo),
                ["C_OP_RemapDistanceToLineSegmentToVector"] = operatorInfo => new RemapDistanceToLineSegmentToVector(operatorInfo),
                ["C_OP_SetCPtoVector"] = operatorInfo => new SetCPtoVector(operatorInfo),
                ["C_OP_RemapParticleCountToScalar"] = operatorInfo => new Operators.RemapParticleCountToScalar(operatorInfo),
                ["C_OP_RemapParticleCountOnScalarEndCap"] = operatorInfo => new RemapParticleCountOnScalarEndCap(operatorInfo),
                ["C_OP_ReinitializeScalarEndCap"] = operatorInfo => new ReinitializeScalarEndCap(operatorInfo),
                ["C_OP_RemapScalar"] = operatorInfo => new Operators.RemapScalar(operatorInfo),
                ["C_OP_RemapScalarEndCap"] = operatorInfo => new RemapScalarEndCap(operatorInfo),
                ["C_OP_RemapScalarOnceTimed"] = operatorInfo => new RemapScalarOnceTimed(operatorInfo),
                ["C_OP_RemapSpeed"] = operatorInfo => new RemapSpeed(operatorInfo),
                ["C_OP_RemapVelocityToVector"] = operatorInfo => new RemapVelocityToVector(operatorInfo),
                ["C_OP_RemapTransformOrientationToRotations"] = operatorInfo => new Operators.RemapTransformOrientationToRotations(operatorInfo),
                ["C_OP_RemapTransformOrientationToYaw"] = operatorInfo => new RemapTransformOrientationToYaw(operatorInfo),
                ["C_OP_RemapTransformToVelocity"] = operatorInfo => new RemapTransformToVelocity(operatorInfo),
                ["C_OP_RemapTransformVisibilityToScalar"] = operatorInfo => new RemapTransformVisibilityToScalar(operatorInfo),
                ["C_OP_RemapTransformVisibilityToVector"] = operatorInfo => new RemapTransformVisibilityToVector(operatorInfo),
                ["C_OP_RemapVectorComponentToScalar"] = operatorInfo => new RemapVectorComponentToScalar(operatorInfo),
                ["C_OP_RemapVectortoCP"] = operatorInfo => new RemapVectortoCP(operatorInfo),
                ["C_OP_RemapVisibilityScalar"] = operatorInfo => new RemapVisibilityScalar(operatorInfo),
                ["C_OP_RotateVector"] = operatorInfo => new RotateVector(operatorInfo),
                ["C_OP_SetAttributeToScalarExpression"] = operatorInfo => new Operators.SetAttributeToScalarExpression(operatorInfo),
                ["C_OP_SetCPOrientationToDirection"] = operatorInfo => new SetCPOrientationToDirection(operatorInfo),
                ["C_OP_SetChildControlPoints"] = operatorInfo => new SetChildControlPoints(operatorInfo),
                ["C_OP_SetControlPointsToParticle"] = operatorInfo => new SetControlPointsToParticle(operatorInfo),
                ["C_OP_SetFloat"] = operatorInfo => new SetFloat(operatorInfo),
                ["C_OP_SetFloatAttributeToVectorExpression"] = operatorInfo => new Operators.SetFloatAttributeToVectorExpression(operatorInfo),
                ["C_OP_SetFloatCollection"] = operatorInfo => new SetFloatCollection(operatorInfo),
                ["C_OP_SetFromCPSnapshot"] = operatorInfo => new SetFromCPSnapshot(operatorInfo),
                ["C_OP_SetPerChildControlPoint"] = operatorInfo => new SetPerChildControlPoint(operatorInfo),
                ["C_OP_SetPerChildControlPointFromAttribute"] = operatorInfo => new SetPerChildControlPointFromAttribute(operatorInfo),
                ["C_OP_SetToCP"] = operatorInfo => new SetToCP(operatorInfo),
                ["C_OP_SetUserEvent"] = operatorInfo => new SetUserEvent(operatorInfo),
                ["C_OP_SetVec"] = operatorInfo => new SetVec(operatorInfo),
                ["C_OP_SetVectorAttributeToVectorExpression"] = operatorInfo => new Operators.SetVectorAttributeToVectorExpression(operatorInfo),
                ["C_OP_Spin"] = operatorInfo => new Spin(operatorInfo),
                ["C_OP_SpinUpdate"] = operatorInfo => new SpinUpdate(operatorInfo),
                ["C_OP_SpinYaw"] = operatorInfo => new SpinYaw(operatorInfo),
                ["C_OP_TeleportBeam"] = operatorInfo => new TeleportBeam(operatorInfo),
                ["C_OP_VelocityDecay"] = operatorInfo => new VelocityDecay(operatorInfo),
                ["C_OP_VelocityMatchingForce"] = operatorInfo => new VelocityMatchingForce(operatorInfo),
            };

        // Register particle force generators
        internal static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionForceGenerator>> ForceGeneratorDictionary
            = new()
            {
                ["C_OP_AttractToControlPoint"] = forceGeneratorInfo => new AttractToControlPoint(forceGeneratorInfo),
                ["C_OP_CPVelocityForce"] = forceGeneratorInfo => new CPVelocityForce(forceGeneratorInfo),
                ["C_OP_CurlNoiseForce"] = forceGeneratorInfo => new CurlNoiseForce(forceGeneratorInfo),
                ["C_OP_ExternalGameImpulseForce"] = forceGeneratorInfo => new ExternalGameImpulseForce(forceGeneratorInfo),
                ["C_OP_ExternalWindForce"] = forceGeneratorInfo => new ExternalWindForce(forceGeneratorInfo),
                ["C_OP_ForceBasedOnDistanceToPlane"] = forceGeneratorInfo => new ForceBasedOnDistanceToPlane(forceGeneratorInfo),
                ["C_OP_IntraParticleForce"] = forceGeneratorInfo => new IntraParticleForce(forceGeneratorInfo),
                ["C_OP_LocalAccelerationForce"] = forceGeneratorInfo => new LocalAccelerationForce(forceGeneratorInfo),
                ["C_OP_ParentVortices"] = forceGeneratorInfo => new ParentVortices(forceGeneratorInfo),
                ["C_OP_PerParticleForce"] = forceGeneratorInfo => new PerParticleForce(forceGeneratorInfo),
                ["C_OP_RandomForce"] = forceGeneratorInfo => new RandomForce(forceGeneratorInfo),
                ["C_OP_TimeVaryingForce"] = forceGeneratorInfo => new TimeVaryingForce(forceGeneratorInfo),
                ["C_OP_TurbulenceForce"] = forceGeneratorInfo => new TurbulenceForce(forceGeneratorInfo),
                ["C_OP_TwistAroundAxis"] = forceGeneratorInfo => new TwistAroundAxis(forceGeneratorInfo),
                ["C_OP_WindForce"] = forceGeneratorInfo => new WindForce(forceGeneratorInfo),
            };

        // Register particle constraints (run after operators each frame to relax particle positions)
        internal static readonly Dictionary<string, Func<ParticleDefinitionParser, ParticleFunctionConstraint>> ConstraintDictionary
            = new()
            {
                ["C_OP_ConstrainDistance"] = constraintInfo => new ConstrainDistance(constraintInfo),
                ["C_OP_PlanarConstraint"] = constraintInfo => new PlanarConstraint(constraintInfo),
                ["C_OP_RopeSpringConstraint"] = constraintInfo => new RopeSpringConstraint(constraintInfo),
                ["C_OP_WorldTraceConstraint"] = constraintInfo => new WorldTraceConstraint(constraintInfo),
            };

        /// <summary>
        /// Shared lookup+construct logic behind every TryCreateX method below.
        /// </summary>
        private static bool TryCreate<TFunction>(
            Dictionary<string, Func<ParticleDefinitionParser, TFunction>> dictionary,
            string name,
            KVObject info,
            ILogger logger,
            int behaviorVersion,
            [MaybeNullWhen(false)] out TFunction function)
        {
            if (dictionary.TryGetValue(name, out var factory))
            {
                function = factory(new ParticleDefinitionParser(info, logger, behaviorVersion));
                return true;
            }

            function = default;
            return false;
        }

        /// <summary>
        /// Attempts to create a particle pre-emission operator by its Source 2 class name.
        /// </summary>
        /// <returns><see langword="true"/> if the pre-emission operator type is supported; otherwise <see langword="false"/>.</returns>
        public static bool TryCreatePreEmissionOperator(string name, KVObject preEmissionOperatorInfo, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out ParticleFunctionPreEmissionOperator preEmissionOperator)
            => TryCreate(PreEmissionOperatorDictionary, name, preEmissionOperatorInfo, logger, behaviorVersion, out preEmissionOperator);

        /// <summary>
        /// Attempts to create a particle emitter by its Source 2 class name.
        /// </summary>
        /// <returns><see langword="true"/> if the emitter type is supported; otherwise <see langword="false"/>.</returns>
        public static bool TryCreateEmitter(string name, KVObject emitterInfo, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out ParticleFunctionEmitter emitter)
            => TryCreate(EmitterDictionary, name, emitterInfo, logger, behaviorVersion, out emitter);

        /// <summary>
        /// Attempts to create a particle initializer by its Source 2 class name.
        /// </summary>
        /// <returns><see langword="true"/> if the initializer type is supported; otherwise <see langword="false"/>.</returns>
        public static bool TryCreateInitializer(string name, KVObject initializerInfo, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out ParticleFunctionInitializer initializer)
            => TryCreate(InitializerDictionary, name, initializerInfo, logger, behaviorVersion, out initializer);

        /// <summary>
        /// Attempts to create a particle operator by its Source 2 class name.
        /// </summary>
        /// <returns><see langword="true"/> if the operator type is supported; otherwise <see langword="false"/>.</returns>
        public static bool TryCreateOperator(string name, KVObject operatorInfo, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out ParticleFunctionOperator @operator)
            => TryCreate(OperatorDictionary, name, operatorInfo, logger, behaviorVersion, out @operator);

        /// <summary>
        /// Attempts to create a particle force generator by its Source 2 class name.
        /// </summary>
        /// <returns><see langword="true"/> if the force generator type is supported; otherwise <see langword="false"/>.</returns>
        public static bool TryCreateForceGenerator(string name, KVObject forceGeneratorInfo, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out ParticleFunctionForceGenerator forceGenerator)
            => TryCreate(ForceGeneratorDictionary, name, forceGeneratorInfo, logger, behaviorVersion, out forceGenerator);

        /// <summary>
        /// Attempts to create a particle constraint by its Source 2 class name.
        /// </summary>
        /// <returns><see langword="true"/> if the constraint type is supported; otherwise <see langword="false"/>.</returns>
        public static bool TryCreateConstraint(string name, KVObject constraintInfo, ILogger logger, int behaviorVersion, [MaybeNullWhen(false)] out ParticleFunctionConstraint constraint)
            => TryCreate(ConstraintDictionary, name, constraintInfo, logger, behaviorVersion, out constraint);

    }
}
