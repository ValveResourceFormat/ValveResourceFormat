using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Measures the model a control point carries, by its volume, dimensions, corners, radius or
    /// surface area, and writes the measure to one or two other control points, remapped from an
    /// input range to an output range where it is a single number. The measure is of the box
    /// around the model's physics shapes, in the model's space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapModelVolumetoCP">C_OP_RemapModelVolumetoCP</seealso>
    class RemapModelVolumetoCP : ParticleFunctionPreEmissionOperator
    {
        /// <summary>Which measure of the box is written.</summary>
        /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/BBoxVolumeType_t">BBoxVolumeType_t</seealso>
        private enum BBoxVolumeType
        {
            BBOX_VOLUME = 0,
            BBOX_DIMENSIONS = 1,
            BBOX_MINS_MAXS = 2,
            BBOX_RADIUS = 3,
            BBOX_SURFACE_AREA = 4,
        }

        private readonly BBoxVolumeType boxType = BBoxVolumeType.BBOX_VOLUME;
        private readonly int inputControlPoint;
        private readonly int outputControlPoint = -1;
        private readonly int outputMaxControlPoint = -1;
        private readonly int field;
        private readonly float inputMin;
        private readonly float inputMax = 128f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;
        private readonly bool cubeRoot = true;

        /// <summary>Whether the volume is the box product, rather than the summed volume of the physics shapes.</summary>
        private readonly bool boxOnly = true;

        public RemapModelVolumetoCP(ParticleDefinitionParser parse) : base(parse)
        {
            boxType = parse.Enum("m_nBBoxType", boxType);
            inputControlPoint = parse.Int32("m_nInControlPointNumber", inputControlPoint);
            outputControlPoint = parse.Int32("m_nOutControlPointNumber", outputControlPoint);
            outputMaxControlPoint = parse.Int32("m_nOutControlPointMaxNumber", outputMaxControlPoint);
            field = Math.Clamp(parse.Int32("m_nField", field), 0, 2);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);
            cubeRoot = parse.Boolean("m_bCubeRoot", cubeRoot);
            boxOnly = parse.Boolean("m_bBBoxOnly", boxOnly);

            if (inputControlPoint == outputControlPoint)
            {
                outputControlPoint = -1;
            }
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            if (outputControlPoint < 0)
            {
                return;
            }

            var model = particleSystemState.GetControlPoint(inputControlPoint).ResolveModelObject()?.Model;
            var fileLoader = particleSystemState.Data?.FileLoader;
            var bounds = model != null ? ModelElements.PhysicsBounds(model, fileLoader) : default;
            var size = bounds.Size;

            switch (boxType)
            {
                case BBoxVolumeType.BBOX_VOLUME:
                    var volume = size.Y * size.X * size.Z;

                    if (!boxOnly && model != null)
                    {
                        var physicsVolume = ModelElements.PhysicsVolume(model, fileLoader);

                        if (physicsVolume != 0f)
                        {
                            volume = physicsVolume;
                        }
                    }

                    if (cubeRoot)
                    {
                        volume = MathF.Cbrt(volume);
                    }

                    SetComponent(particleSystemState, volume);
                    break;
                case BBoxVolumeType.BBOX_SURFACE_AREA:
                    SetComponent(particleSystemState, MathF.Sqrt(((size.Z * size.X) + (size.X * size.Y) + (size.Z * size.Y)) * 2f));
                    break;
                case BBoxVolumeType.BBOX_DIMENSIONS:
                    particleSystemState.SetControlPointValue(outputControlPoint, size);
                    break;
                case BBoxVolumeType.BBOX_MINS_MAXS:
                    if (outputMaxControlPoint >= 0)
                    {
                        particleSystemState.SetControlPointValue(outputControlPoint, bounds.Min);
                        particleSystemState.SetControlPointValue(outputMaxControlPoint, bounds.Max);
                    }

                    break;
                case BBoxVolumeType.BBOX_RADIUS:
                    SetComponent(particleSystemState, Vector3.Distance(bounds.Min, bounds.Center));
                    break;
            }
        }

        private void SetComponent(ParticleSystemState particleSystemState, float value)
            => particleSystemState.SetControlPointValueComponent(outputControlPoint, field, MathUtils.RemapValClamped(value, inputMin, inputMax, outputMin, outputMax));
    }
}
