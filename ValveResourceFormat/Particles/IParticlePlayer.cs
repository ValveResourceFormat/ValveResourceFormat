using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles;

/// <summary>
/// The local player a particle system can query.
/// </summary>
public interface IParticlePlayer
{
    /// <summary>Gets a position on the player.</summary>
    /// <param name="position">Which position.</param>
    /// <param name="value">The world position.</param>
    /// <returns>False when there is no player.</returns>
    bool TryGetPosition(ParticleEntityPos position, out Vector3 value);

    /// <summary>Gets the direction the player faces.</summary>
    /// <param name="eyes">Whether to take the direction the eyes look along rather than the level one the body faces.</param>
    Vector3 GetForward(bool eyes);

    /// <summary>
    /// Gets the player's orientation, carrying +X to the direction it faces. Built around
    /// <see cref="GetForward"/> unless the player knows its full orientation.
    /// </summary>
    /// <param name="eyes">Whether to take the orientation of the eyes rather than the level one of the body.</param>
    Quaternion GetOrientation(bool eyes)
    {
        var forward = GetForward(eyes);

        if (forward == Vector3.Zero)
        {
            return Quaternion.Identity;
        }

        forward = Vector3.Normalize(forward);
        var (left, up) = ParticleMath.VectorVectors(forward);

        return Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            forward.X, forward.Y, forward.Z, 0f,
            left.X, left.Y, left.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            0f, 0f, 0f, 1f));
    }
}
