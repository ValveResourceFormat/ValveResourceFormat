using ValveResourceFormat.Particles;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// Draws nothing. Every frame it turns each particle's roll to the direction the particle moved on
    /// screen over its last step, offset by the forward angle, for the renderers after it this frame and
    /// all of them the next. The roll it writes stays on the particle like any other.
    /// </summary>
    /// <remarks>
    /// The rotate rate is authored but has no effect.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RenderScreenVelocityRotate">C_OP_RenderScreenVelocityRotate</seealso>
    internal class RenderScreenVelocityRotate : ParticleFunctionRenderer
    {
        private readonly float forwardDegrees = -90f;

        public RenderScreenVelocityRotate(ParticleDefinitionParser parse) : base(parse)
        {
            forwardDegrees = parse.Float("m_flForwardDegrees", forwardDegrees);
        }

        /// <inheritdoc/>
        public override bool DrawsInPasses => false;

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Roll.Mask();

        /// <inheritdoc/>
        public override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            var view = camera.CameraViewMatrix;
            var forward = float.DegreesToRadians(forwardDegrees);

            var current = particles.Current;
            var positions = current.ReadVectors(ParticleField.Position);
            var previousPositions = current.ReadVectors(ParticleField.PositionPrevious);
            var rolls = current.Floats(ParticleField.Roll);

            for (var i = 0; i < current.Length; i++)
            {
                var screen = Vector3.Transform(positions[i], view);

                if (!float.IsFinite(screen.X))
                {
                    continue;
                }

                var previous = Vector3.Transform(previousPositions[i], view);

                rolls[i] = MathF.Atan2(screen.Y - previous.Y, screen.X - previous.X) + forward;
            }
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }
    }
}
