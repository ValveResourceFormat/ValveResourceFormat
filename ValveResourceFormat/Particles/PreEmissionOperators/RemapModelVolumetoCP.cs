using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Measures the model a control point carries, by its volume, dimensions, corners, radius or
    /// surface area, and writes the measure to one or two other control points, remapped from an
    /// input range to an output range where it is a single number. The measure is of the box
    /// around the model's own meshes, in the model's space.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapModelVolumetoCP">C_OP_RemapModelVolumetoCP</seealso>
    class RemapModelVolumetoCP : ParticleFunctionPreEmissionOperator
    {
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
            var (mins, maxs) = model != null ? ModelElements.Bounds(model) : (Vector3.Zero, Vector3.Zero);
            var size = maxs - mins;

            switch (boxType)
            {
                case BBoxVolumeType.BBOX_VOLUME:
                    var volume = size.Y * size.X * size.Z;

                    if (cubeRoot)
                    {
                        volume = MathF.Pow(volume, 0.33333334f);
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
                        particleSystemState.SetControlPointValue(outputControlPoint, mins);
                        particleSystemState.SetControlPointValue(outputMaxControlPoint, maxs);
                    }

                    break;
                case BBoxVolumeType.BBOX_RADIUS:
                    SetComponent(particleSystemState, Vector3.Distance(mins, (mins + maxs) * 0.5f));
                    break;
            }
        }

        private void SetComponent(ParticleSystemState particleSystemState, float value)
            => particleSystemState.SetControlPointValueComponent(outputControlPoint, field, MathUtils.RemapValClamped(value, inputMin, inputMax, outputMin, outputMax));
    }
}
