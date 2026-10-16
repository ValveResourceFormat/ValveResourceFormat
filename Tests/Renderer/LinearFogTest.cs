using System.Threading.Tasks;
using ValveResourceFormat.Renderer.SceneEnvironment;

namespace Tests.Renderer
{
    public class LinearFogTest
    {
        private static readonly LinearFog Fog = new(Vector3.One, Start: 500f, End: 2000f, MaxDensity: 0.8f, Exponent: 2f);

        [Test]
        public async Task NothingIsFoggedBeforeTheStart()
        {
            await Assert.That(Fog.GetAmount(100f)).IsEqualTo(0f);
        }

        [Test]
        public async Task TheFractionOfTheWayIsRaisedToTheExponent()
        {
            await Assert.That(Fog.GetAmount(1250f)).IsEqualTo(0.25f).Within(1e-6f);
        }

        [Test]
        public async Task TheDensityIsCappedAtTheMaximum()
        {
            await Assert.That(Fog.GetAmount(1900f)).IsEqualTo(0.8f).Within(1e-6f);
            await Assert.That(Fog.GetAmount(float.PositiveInfinity)).IsEqualTo(0.8f).Within(1e-6f);
        }

        [Test]
        public async Task AFogThatThinsWithDistanceLeavesWhatIsInfinitelyFarClear()
        {
            var reversed = Fog with { Start = 2000f, End = 500f };

            await Assert.That(reversed.GetAmount(float.PositiveInfinity)).IsEqualTo(0f);
        }

        [Test]
        public async Task NoFogCoversNothing()
        {
            await Assert.That(LinearFog.None.GetAmount(float.PositiveInfinity)).IsEqualTo(0f);
        }
    }
}
