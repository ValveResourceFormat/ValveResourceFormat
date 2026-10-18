using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Copies one attribute from the parent system's particles to this system's particles every step,
    /// scaled for scalar and vector fields and copied as is for integer fields and bone indices. The
    /// walk over the parent list continues where the previous step left off, so with a different
    /// particle count on each side the parent a particle copies from shifts from step to step. A random
    /// distribution picks a new parent for every particle every step instead. Color and alpha clamp to
    /// [0, 1].
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_InheritFromParentParticles">C_OP_InheritFromParentParticles</seealso>
    class InheritFromParentParticles : ParticleFunctionOperator
    {
        private readonly float scale = 1f;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int increment = 1;
        private readonly bool randomDistribution;

        private int parentIndex;

        public InheritFromParentParticles(ParticleDefinitionParser parse) : base(parse)
        {
            scale = parse.Float("m_flScale", scale);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            increment = parse.Int32("m_nIncrement", increment);
            randomDistribution = parse.Boolean("m_bRandomDistribution", randomDistribution);
        }

        public override void Reset()
        {
            parentIndex = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState)
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var parentData = particleSystemState.ParentSystem?.Data;

            if (parentData == null || parentData.CurrentParticles.IsEmpty)
            {
                return;
            }

            var parentParticles = parentData.CurrentParticles;
            var lastIndex = parentParticles.Length - 1;

            foreach (ref var particle in particles.Current)
            {
                if (randomDistribution)
                {
                    parentIndex = particleSystemState.Random.NextIndex(parentParticles.Length);
                }
                else if (parentIndex > lastIndex)
                {
                    parentIndex = 0;
                }

                if ((uint)parentIndex < (uint)parentParticles.Length)
                {
                    CopyAttribute(ref particle, ref parentParticles[parentIndex], fieldOutput, fieldOutput, scale);
                }

                parentIndex += increment;
            }
        }

        /// <summary>
        /// Writes <paramref name="source"/>'s <paramref name="input"/> into <paramref name="target"/>'s
        /// <paramref name="output"/>, by the type of the output: integers and bone indices copied as is,
        /// everything else scaled, with color and alpha clamped to [0, 1].
        /// </summary>
        internal static void CopyAttribute(ref Particle target, ref Particle source, ParticleField input, ParticleField output, float scale)
        {
            if (output == ParticleField.BoneIndices)
            {
                target.SetVector(output, source.GetVector(input));
            }
            else if (output.FieldType() == "vector")
            {
                var value = source.GetVector(input) * scale;

                if (output.IsNormalizedField())
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                target.SetVector(output, value);
            }
            else if (output.IsIntegerAttribute())
            {
                target.SetScalar(output, source.GetScalar(input));
            }
            else
            {
                var value = source.GetScalar(input) * scale;

                if (output.IsAlpha())
                {
                    value = ParticleMath.ClampToUnit(value);
                }

                target.SetScalar(output, value);
            }
        }
    }
}
