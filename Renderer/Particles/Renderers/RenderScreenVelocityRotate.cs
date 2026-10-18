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
        public override void UpdateBuffers(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
            var view = camera.CameraViewMatrix;
            var forward = float.DegreesToRadians(forwardDegrees);

            foreach (ref var particle in particles.Current)
            {
                var screen = Vector3.Transform(particle.Position, view);

                if (!float.IsFinite(screen.X))
                {
                    continue;
                }

                var previous = Vector3.Transform(particle.PositionPrevious, view);

                particle.Rotation = particle.Rotation with { Z = MathF.Atan2(screen.Y - previous.Y, screen.X - previous.X) + forward };
            }
        }

        /// <inheritdoc/>
        public override void Render(ParticleCollection particles, ParticleSystemState systemState, Camera camera)
        {
        }
    }
}
