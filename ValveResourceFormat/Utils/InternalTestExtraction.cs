using System.IO;
using System.Runtime.CompilerServices;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

[assembly: InternalsVisibleTo("Source2Viewer-CLI")]
[assembly: InternalsVisibleTo("Tests")]

namespace ValveResourceFormat.Utils
{
    internal class InternalTestExtraction
    {
        /// <summary>
        /// This method tries to run through all the code paths for a particular resource,
        /// which allows us to quickly find exceptions when running --stats using Decompiler over an entire game folder,
        /// as well it is used in tests to quickly verify.
        /// </summary>
        internal static void Test(Resource resource)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Model:
                    {
                        var model = (Model)resource.DataBlock;
                        model.GetEmbeddedAnimations();
                        model.GetEmbeddedMeshes();
                        model.GetEmbeddedPhys();

                        /* TODO: Getting first mesh index isn't working
                        if (model.Data.ContainsKey("m_modelSkeleton"))
                        {
                            var first = model.Data.GetIntegerArray("m_remappingTableStarts");
                            Skeleton.FromModelData(model.Data, (int)first[0]);
                        }
                        */

                        break;
                    }
                case ResourceType.Mesh:
                    {
                        var mesh = (Mesh)resource.DataBlock;
                        mesh.GetBounds();
                        break;
                    }

                case ResourceType.Particle:
                    {
                        var particle = (ParticleSystem)resource.DataBlock;
                        particle.GetChildParticleNames();
                        break;
                    }
            }

            try
            {
                using (FileExtract.Extract(resource, new NullFileLoader()))
                {
                    // Test extraction code flow
                }
            }
            catch (FileNotFoundException)
            {
                // ignore for now because we use null file loader, map extract throws
            }
        }
    }
}
