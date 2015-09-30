using System;

namespace ValveResourceFormat
{
    [AttributeUsage(AttributeTargets.Field)]
    public class ExtensionAttribute : Attribute
    {
        public string Extension { get; set; }

        public ExtensionAttribute( string extension )
        {
            this.Extension = extension;
        }
    }

    // Friendly names are used
    public enum ResourceType
    {
        Unknown = 0,

        [Extension("vanim")]
        Animation,

        [Extension("vagrp")]
        AnimationGroup,

        [Extension("vseq")]
        Sequence,

        [Extension("vpcf")]
        Particle,

        [Extension("vmat")]
        Material,

        [Extension("vmks")]
        Sheet,

        [Extension("vmesh")]
        Mesh,

        [Extension("vtex")]
        Texture,

        [Extension("vmdl")]
        Model,

        [Extension("vphys")]
        PhysicsCollisionMesh,

        [Extension("vsnd")]
        Sound,

        [Extension("vmorf")]
        Morph,

        [Extension("vrman")]
        ResourceManifest,

        [Extension("vwrld")]
        World,

        [Extension("vwnod")]
        WorldNode,

        [Extension("vvis")]
        WorldVisibility,

        [Extension("vents")]
        EntityLump,

        [Extension("vsurf")]
        SurfaceProperties,

        [Extension("vsndevts")]
        SoundEventScript,

        [Extension("vsndstck")]
        SoundStackScript,

        [Extension("vfont")]
        BitmapFont,

        [Extension("vrmap")]
        ResourceRemapTable,

        // All Panorama* are compiled just as CompilePanorama
        Panorama,

        [Extension("vcss")]
        PanoramaStyle = Panorama,

        [Extension("vxml")]
        PanoramaLayout = Panorama,

        [Extension("vpdi")]
        PanoramaDynamicImages = Panorama,

        [Extension("vjs")]
        PanoramaScript = Panorama,

        [Extension("vpsf")]
        ParticleSnapshot,

        [Extension("vmap")]
        Map,

        // Mappings to match compiler identifier
        Psf = ParticleSnapshot,
        AnimGroup = AnimationGroup,
        VPhysXData = PhysicsCollisionMesh,
        Font = BitmapFont,
        RenderMesh = Mesh,
    }
}
