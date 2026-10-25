namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>A run of control points bound to the models of a run of particles.</summary>
    /// <param name="firstControlPoint">The first control point of the run.</param>
    /// <param name="numControlPoints">How many control points the run holds at most.</param>
    /// <param name="firstSourcePoint">The index of the particle the first control point follows.</param>
    /// <param name="attachment">The model attachment the control points sit on, or null to sit on the particles.</param>
    internal sealed class ModelParticleControlPoints(int firstControlPoint, int numControlPoints, int firstSourcePoint, string? attachment)
    {
        /// <summary>The first control point of the run.</summary>
        public int FirstControlPoint => firstControlPoint;

        /// <summary>The index of the particle the first control point follows.</summary>
        public int FirstSourcePoint => firstSourcePoint;

        /// <summary>The model attachment the control points sit on, or null to sit on the particles.</summary>
        public string? Attachment => attachment;

        /// <summary>How many control points are bound while the system holds <paramref name="particleCount"/> particles.</summary>
        public int ActiveCount(int particleCount)
            => firstSourcePoint < 0 ? 0 : Math.Max(0, Math.Min(Math.Min(numControlPoints, particleCount - firstSourcePoint), ParticleSystemState.ControlPointCount - firstControlPoint));
    }
}
