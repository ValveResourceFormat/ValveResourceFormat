using System.Collections;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes particle attributes from a control-point-associated snapshot (.vsnap) file.
    /// Each particle reads data at its index from the snapshot, wrapping around when the particle
    /// count exceeds the snapshot size.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFromCPSnapshot">C_INIT_InitFromCPSnapshot</seealso>
    class InitFromCPSnapshot : ParticleFunctionInitializer
    {
        private readonly int ControlPointNumber;
        private readonly ParticleField AttributeToRead;
        private readonly ParticleField AttributeToWrite;
        private readonly int LocalSpaceCP = -1;
        private readonly bool Random;
        private readonly bool Reverse;

        // Cached snapshot lookup
        private ParticleSnapshot? cachedSnapshot;
        private bool snapshotResolved;
        private string? readAttributeName;
        private IEnumerable? readAttributeData;

        public InitFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            ControlPointNumber = parse.Int32("m_nControlPointNumber", 0);
            AttributeToWrite = parse.ParticleField("m_nAttributeToWrite", ParticleField.Position);
            AttributeToRead = parse.ParticleField("m_nAttributeToRead", AttributeToWrite);
            LocalSpaceCP = parse.Int32("m_nLocalSpaceCP", LocalSpaceCP);
            Random = parse.Boolean("m_bRandom", false);
            Reverse = parse.Boolean("m_bReverse", false);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            if (!snapshotResolved)
            {
                ResolveSnapshot(particleSystemState);
            }

            if (cachedSnapshot == null || readAttributeData == null)
            {
                return particle;
            }

            var numParticles = (int)cachedSnapshot.NumParticles;

            if (numParticles == 0)
            {
                return particle;
            }

            int idx;

            if (Random)
            {
                idx = (int)ParticleCollection.RandomBetween(particle.ParticleID, 0, numParticles - 1);
            }
            else if (Reverse)
            {
                idx = (numParticles - 1 - particle.ParticleID % numParticles) % numParticles;
            }
            else
            {
                idx = particle.ParticleID % numParticles;
            }

            var fieldType = AttributeToWrite.FieldType();

            if (fieldType == "vector" && readAttributeData is Vector3[] vectorArray && idx < vectorArray.Length)
            {
                var value = vectorArray[idx];

                if (LocalSpaceCP >= 0)
                {
                    value += particleSystemState.GetControlPoint(LocalSpaceCP).Position;
                }

                particle.SetVector(AttributeToWrite, value);

                if (AttributeToWrite == ParticleField.Position)
                {
                    particle.PositionPrevious = value;
                }
            }
            else if (fieldType == "float" && readAttributeData is float[] floatArray && idx < floatArray.Length)
            {
                particle.SetScalar(AttributeToWrite, floatArray[idx]);
            }

            return particle;
        }

        private void ResolveSnapshot(ParticleSystemRenderState particleSystemState)
        {
            snapshotResolved = true;
            cachedSnapshot = particleSystemState.Data?.GetControlPointSnapshot(ControlPointNumber);

            if (cachedSnapshot == null)
            {
                return;
            }

            readAttributeName = GetSnapshotAttributeName(AttributeToRead);

            if (readAttributeName == null)
            {
                return;
            }

            foreach (var ((name, _), data) in cachedSnapshot.AttributeData)
            {
                if (name == readAttributeName)
                {
                    readAttributeData = data;
                    return;
                }
            }
        }

        private static string? GetSnapshotAttributeName(ParticleField field) => field switch
        {
            ParticleField.Position => "position",
            ParticleField.LifeDuration => "lifespan",
            ParticleField.PositionPrevious => "velocity",
            ParticleField.Radius => "radius",
            ParticleField.Roll => "rotation",
            ParticleField.RollSpeed => "rotation_speed",
            ParticleField.Color => "color",
            ParticleField.Alpha => "opacity",
            ParticleField.CreationTime => "creation_time",
            ParticleField.SequenceNumber => "sequence_number",
            ParticleField.TrailLength => "trail_length",
            ParticleField.ParticleId => "particle_id",
            ParticleField.Yaw => "yaw",
            ParticleField.SecondSequenceNumber => "sequence_number1",
            ParticleField.HitboxIndex => "hitbox",
            ParticleField.HitboxOffsetPosition => "hitbox_offset",
            ParticleField.AlphaAlternate => "alpha2",
            ParticleField.ScratchVector => "scratch_vec",
            ParticleField.ScratchFloat => "scratch_float",
            ParticleField.Pitch => "pitch",
            ParticleField.Normal => "normal",
            ParticleField.GlowRgb => "glow_rgb",
            ParticleField.GlowAlpha => "glow_alpha",
            ParticleField.ForceScale => "force_scale",
            ParticleField.ManualAnimationFrame => "manual_animation_frame",
            ParticleField.ShaderExtraData1 => "shader_extra_data_1",
            ParticleField.ShaderExtraData2 => "shader_extra_data_2",
            _ => null,
        };
    }
}
