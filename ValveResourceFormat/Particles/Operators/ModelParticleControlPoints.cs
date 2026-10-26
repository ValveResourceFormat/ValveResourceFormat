namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>A run of control points bound to the models of a run of particles.</summary>
    /// <param name="firstControlPoint">The first control point of the run.</param>
    /// <param name="numControlPoints">How many control points the run holds at most.</param>
    /// <param name="firstSourcePoint">The index of the particle the first control point follows.</param>
    /// <param name="followAttachment">Whether the control points follow a model attachment instead of the particles.</param>
    /// <param name="attachment">The model attachment the control points sit on, or null when none is named.</param>
    internal sealed class ModelParticleControlPoints(int firstControlPoint, int numControlPoints, int firstSourcePoint, bool followAttachment, string? attachment)
    {
        /// <summary>The first control point of the run.</summary>
        public int FirstControlPoint => firstControlPoint;

        /// <summary>The index of the particle the first control point follows.</summary>
        public int FirstSourcePoint => firstSourcePoint;

        /// <summary>Whether the control points follow a model attachment instead of the particles, even when no attachment is named.</summary>
        public bool FollowAttachment => followAttachment;

        /// <summary>The model attachment the control points sit on, or null when none is named.</summary>
        public string? Attachment => attachment;

        /// <summary>How many control points are bound while the system holds <paramref name="particleCount"/> particles.</summary>
        public int ActiveCount(int particleCount)
            => firstSourcePoint < 0 ? 0 : Math.Max(0, Math.Min(Math.Min(numControlPoints, particleCount - firstSourcePoint), ParticleSystemState.ControlPointCount - firstControlPoint));
    }
}
