using System.Diagnostics;
using System.IO;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Utils
{
    internal class InternalTestExtraction
    {
        /// <summary>
        /// This method tries to run through all the code paths for a particular resource,
        /// which allows us to quickly find exceptions when running --stats using Decompiler over an entire game folder,
        /// and it is also used in tests to quickly verify resources.
        /// </summary>
        /// <param name="resource">The resource to test.</param>
        /// <param name="fileLoader">Optional file loader for loading dependencies. If null, uses <see cref="NullFileLoader"/>.</param>
        internal static void Test(Resource resource, IFileLoader? fileLoader = null)
        {
            switch (resource.ResourceType)
            {
                case ResourceType.Model:
                    {
                        var model = (Model?)resource.DataBlock;
                        Debug.Assert(model != null);
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
                        var mesh = (Mesh?)resource.DataBlock;
                        Debug.Assert(mesh != null);
                        mesh.GetBounds();
                        break;
                    }

                case ResourceType.Particle:
                    {
                        var particle = (ParticleSystem?)resource.DataBlock;
                        Debug.Assert(particle != null);
                        particle.GetChildParticleNames();
                        particle.GetChildParticleNames(true);
                        break;
                    }

                case ResourceType.PhysicsCollisionMesh:
                    {
                        var phys = (PhysAggregateData?)resource.DataBlock;
                        Debug.Assert(phys != null);
                        var bindPose = phys.BindPose;
                        break;
                    }

                case ResourceType.Morph:
                    {
                        var morph = (Morph?)resource.DataBlock;
                        Debug.Assert(morph != null);
                        morph.GetMorphDatas();
                        morph.GetFlexDescriptors();
                        morph.GetFlexVertexData();
                        break;
                    }

                case ResourceType.Material:
                    {
                        var material = (Material?)resource.DataBlock;
                        Debug.Assert(material != null);
                        material.GetShaderArguments();
                        var inputSig = material.InputSignature;
                        break;
                    }

                case ResourceType.EntityLump:
                    {
                        var entityLump = (EntityLump?)resource.DataBlock;
                        Debug.Assert(entityLump != null);
                        entityLump.ToForgeGameData();
                        break;
                    }

                case ResourceType.Texture:
                    {
                        var texture = (Texture?)resource.DataBlock;
                        Debug.Assert(texture != null);
                        texture.GetSpriteSheetData();
                        using var _ = texture.GenerateBitmap(mipLevel: (uint)Math.Max(texture.NumMipLevels - 2, 0));

                        // Intentional return to avoid calling TextureExtract,
                        // We only test extracting a smaller mip size, and avoid packing png
                        return;
                    }
            }

            try
            {
                // Test extraction code flow
                using var contentFile = FileExtract.Extract(resource, fileLoader ?? new NullFileLoader());

                foreach (var contentSubFile in contentFile.SubFiles)
                {
                    contentSubFile.Extract?.Invoke();
                }
            }
            catch (FileNotFoundException)
            {
                // ignore for now because we use null file loader, map extract throws
            }
        }
    }
}
