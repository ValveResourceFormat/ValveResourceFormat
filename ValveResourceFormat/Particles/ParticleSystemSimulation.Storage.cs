using System.Runtime.CompilerServices;
using ValveResourceFormat.Particles.Emitters;
using ValveResourceFormat.Particles.Initializers;

namespace ValveResourceFormat.Particles
{
    public partial class ParticleSystemSimulation
    {
        /// <summary>The attributes the system itself writes to every new particle.</summary>
        private static readonly ulong SystemWrittenFields = ParticleField.ParticleId.Mask() | ParticleField.CreationTime.Mask();

        /// <summary>The attributes this system's functions and their inputs read, gathered while they are parsed.</summary>
        private readonly StrongBox<ulong> initialReads = new();

        /// <summary>The attributes written when particles are created: by the system, emitters, initializers and renderers.</summary>
        private ulong initializedFields = SystemWrittenFields;

        /// <summary>The attributes written as particles are simulated: by operators, forces, constraints and renderers.</summary>
        private ulong updatedFields;

        /// <summary>The attributes whose initial value the renderers and their inputs read.</summary>
        private ulong rendererInitialReads;

        /// <summary>Gathers what the functions write and read, then lays out the particle storage for it.</summary>
        private void DeclareFunctionFields()
        {
            foreach (var function in functions)
            {
                AddFunctionFields(function, initializes: function is ParticleFunctionEmitter or ParticleFunctionInitializer);
            }

            UpdateStorage();
        }

        /// <summary>
        /// Adds what a renderer drawing this system writes and reads, both when particles are created
        /// and as they are simulated, and what it blends between two steps besides the attributes every
        /// renderer blends, and lays out the particle storage again.
        /// </summary>
        internal void AddRendererFields(ulong writtenFields, ulong initialReads, ulong blendedFields)
        {
            initializedFields |= writtenFields;
            updatedFields |= writtenFields;
            rendererInitialReads |= initialReads;
            particleCollection.AddBlendedFields(blendedFields);

            UpdateStorage();
        }

        /// <summary>
        /// Adds what <paramref name="function"/> writes: when particles are created if it
        /// <paramref name="initializes"/> them, else as they are simulated.
        /// </summary>
        private void AddFunctionFields(ParticleFunction function, bool initializes)
        {
            var written = function.StoredFields;

            if (initializes)
            {
                initializedFields |= written;
            }
            else
            {
                updatedFields |= written;
            }

            initializedFields |= function.InitialWrittenFields;
            updatedFields |= function.LiveWrittenFields;
        }

        /// <summary>
        /// Stores what is written. The initial particles keep a copy of what is written both at creation
        /// and later and read as initial, share the current particles' arrays for what is only written at
        /// creation, and read the constant for the rest.
        /// </summary>
        private void UpdateStorage()
        {
            var readInitial = initialReads.Value | rendererInitialReads;
            var stored = initializedFields | updatedFields;
            var initialStored = updatedFields & initializedFields & readInitial;
            var initialShared = readInitial & initializedFields & ~updatedFields;

            particleCollection.SetStorage(stored, initialStored, initialShared);
        }
    }
}
