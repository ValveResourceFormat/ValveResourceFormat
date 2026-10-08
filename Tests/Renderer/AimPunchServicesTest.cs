using System.Threading.Tasks;
using ValveResourceFormat.Renderer.Gameplay;

namespace Tests.Renderer
{
    /// <summary>
    /// Pins the aim punch curve to reference values for the silenced M4A1-S and USP-S recoil patterns.
    /// </summary>
    public class AimPunchServicesTest
    {
        [Test]
        public async Task SingleKickRisesAndSettles()
        {
            var punch = new AimPunchServices();

            // usp_silencer, silencer on: every shot kicks straight up at 23
            var viewPunch = punch.Kick(10f, 0f, 23f);

            await Assert.That(viewPunch.X).IsEqualTo(-1.265f).Within(1e-4f);
            await Assert.That(viewPunch.Y).IsEqualTo(0f).Within(1e-4f);

            await Assert.That(punch.Sample(10f).X).IsEqualTo(0f);
            await Assert.That(punch.Sample(10.05f).X).IsEqualTo(-0.211f).Within(1e-3f);
            await Assert.That(punch.Sample(10.2f).X).IsEqualTo(-0.077f).Within(1e-3f);
            await Assert.That(punch.Sample(10.5f).X).IsEqualTo(0f);
        }

        [Test]
        public async Task SprayFollowsReference()
        {
            // The first ten kicks of m4a1_silencer's silencer on recoil pattern, fired at its 0.1s cycle time
            (float Angle, float Magnitude)[] kicks =
            [
                (-11.44f, 15.75f), (-0.52f, 15.14f), (18.48f, 16.07f), (-12.74f, 17.61f), (14.27f, 19.47f),
                (6.98f, 20.31f), (-22.32f, 20.69f), (-12.26f, 20.86f), (-37.05f, 20.94f), (2.66f, 20.97f),
            ];

            var punch = new AimPunchServices();

            for (var i = 0; i < kicks.Length; i++)
            {
                punch.Kick(10f + i * 0.1f, kicks[i].Angle, kicks[i].Magnitude);
            }

            var lastShot = 10.9f;

            (float Time, float Pitch, float Yaw)[] expected =
            [
                (0f, -2.635f, 0.805f),
                (0.05f, -3.000f, 0.737f),
                (0.2f, -1.988f, 0.397f),
                (0.5f, -0.046f, 0.009f),
                (0.8f, 0f, 0f),
            ];

            foreach (var (time, pitch, yaw) in expected)
            {
                var sample = punch.Sample(lastShot + time);

                await Assert.That(sample.X).IsEqualTo(pitch).Within(0.01f).Because($"pitch at +{time}s");
                await Assert.That(sample.Y).IsEqualTo(yaw).Within(0.01f).Because($"yaw at +{time}s");
            }
        }
    }
}
