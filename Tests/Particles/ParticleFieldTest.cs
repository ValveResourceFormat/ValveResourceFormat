using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Particles;

namespace Tests.Particles
{
    public class ParticleFieldTest
    {
        // Fields holding references the engine owns, which have no value to store in a viewer, and the
        // bone indices, which are kept apart from the scalars and vectors
        private static readonly ParticleField[] Unstored =
        [
            ParticleField.NoneDisabled,
            ParticleField.SceneObjectPointer, ParticleField.SceneObjectPointer2, ParticleField.RefCountedPointer,
            ParticleField.ModelHelperPointer, ParticleField.ModelHelperPointer2, ParticleField.ModelHelperPointer3, ParticleField.ModelHelperPointer4,
            ParticleField.BoneIndices,
        ];

        [Test]
        public async Task EveryValueFieldKeepsWhatIsWritten()
        {
            var lost = new List<ParticleField>();

            foreach (var field in Enum.GetValues<ParticleField>().Except(Unstored))
            {
                var particles = new ParticleSpan(new ParticleBuffer(1, ParticleBuffer.ValueFieldMask), 0, 1);

                if (ParticleBuffer.IsScalar(field))
                {
                    particles.Floats(field)[0] = 3f;

                    if (particles.ReadFloat(field, 0) != 3f)
                    {
                        lost.Add(field);
                    }
                }
                else if (ParticleBuffer.IsVector(field))
                {
                    particles.Vectors(field)[0] = new Vector3(1f, 2f, 3f);

                    if (particles.ReadVector(field, 0) != new Vector3(1f, 2f, 3f))
                    {
                        lost.Add(field);
                    }
                }
                else
                {
                    lost.Add(field);
                }
            }

            await Assert.That(lost).IsEmpty();
        }
    }
}
