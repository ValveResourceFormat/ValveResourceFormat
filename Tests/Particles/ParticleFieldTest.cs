using System.Linq;
using System.Threading.Tasks;
using ValveResourceFormat.Particles;

namespace Tests.Particles
{
    public class ParticleFieldTest
    {
        // Fields holding references the engine owns, which have no value to store in a viewer
        private static readonly ParticleField[] Unstored =
        [
            ParticleField.NoneDisabled, ParticleField.HitboxIndex,
            ParticleField.SceneObjectPointer, ParticleField.SceneObjectPointer2, ParticleField.RefCountedPointer,
            ParticleField.ModelHelperPointer, ParticleField.ModelHelperPointer2, ParticleField.ModelHelperPointer3, ParticleField.ModelHelperPointer4,
            ParticleField.BoneIndices, ParticleField.BoneWeights,
        ];

        [Test]
        public async Task EveryValueFieldKeepsWhatIsWritten()
        {
            var lost = new System.Collections.Generic.List<ParticleField>();

            foreach (var field in Enum.GetValues<ParticleField>().Except(Unstored))
            {
                var particle = new Particle();

                switch (field.FieldType())
                {
                    case "float":
                        particle.SetScalar(field, 3f);
                        if (particle.GetScalar(field) != 3f)
                        {
                            lost.Add(field);
                        }

                        break;

                    case "vector":
                        particle.SetVector(field, new Vector3(1f, 2f, 3f));
                        if (particle.GetVector(field) != new Vector3(1f, 2f, 3f))
                        {
                            lost.Add(field);
                        }

                        break;
                }
            }

            await Assert.That(lost).IsEmpty();
        }
    }
}
