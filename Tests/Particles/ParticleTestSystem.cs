using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.IO;
using ValveResourceFormat.Particles;
using ValveResourceFormat.ResourceTypes;

namespace Tests.Particles
{
    /// <summary>Builds particle systems from KV3 text for simulation tests, without any game files.</summary>
    internal static class ParticleTestSystem
    {
        private const string Header = "<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:generic:version{7412167c-06e9-4698-aff2-e63eb59037e7} -->\n";

        /// <summary>Parses the body of a <c>CParticleSystemDefinition</c>, without its header.</summary>
        public static ParticleSystem Parse(string definition)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Header + definition));
            var data = KVSerializer.Create(KVSerializationFormat.KeyValues3Text).Deserialize(stream).Root;

            return ParticleSystem.Create(data);
        }

        /// <summary>Creates a runnable simulation of the definition.</summary>
        public static ParticleSystemSimulation Simulate(string definition)
            => new(Parse(definition), new NullFileLoader());
    }
}
