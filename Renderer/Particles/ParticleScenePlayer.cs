using ValveResourceFormat.Particles;
using ValveResourceFormat.Renderer.Entities;

namespace ValveResourceFormat.Renderer.Particles
{
    // Lets particle systems find the player in their scene, while the player is being played
    internal sealed class ParticleScenePlayer(Scene scene) : IParticlePlayer
    {
        private IPlayerController? Controller
            => scene.Player is { } player && player.EntitySystem.Player == player && player.Controller.IsActive
                ? player.Controller
                : null;

        public bool TryGetPosition(ParticleEntityPos position, out Vector3 value)
        {
            if (Controller is not { } controller)
            {
                value = default;
                return false;
            }

            value = position switch
            {
                ParticleEntityPos.PARTICLE_ABS_ORIGIN => controller.Position,
                ParticleEntityPos.PARTICLE_WORLDSPACE_CENTER => controller.HullCenter,
                _ => controller.EyePosition,
            };

            return true;
        }

        public Vector3 GetForward(bool eyes)
        {
            var forward = Controller?.ViewForward ?? Vector3.UnitX;

            return eyes ? forward : MathUtils.SafeNormalize(forward with { Z = 0f });
        }
    }
}
