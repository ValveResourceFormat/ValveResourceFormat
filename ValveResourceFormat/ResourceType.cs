using System;

namespace ValveResourceFormat
{
    [AttributeUsage(AttributeTargets.Field)]
    class ExtensionAttribute : Attribute
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
        SequenceGroup,

        [Extension("vpcf")]
        ParticleSystem,

        [Extension("vmat")]
        Material,

        [Extension("vmks")]
        Sheet,

        [Extension("vmesh")]
        Mesh,

        [Extension("vtex")]
        CompiledTexture,

        [Extension("vmdl")]
        Model,

        [Extension("vphys")]
        PhysicsCollisionMesh,

        [Extension("vsnd")]
        Sound,

        [Extension("vmorf")]
        MorphSet,

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

        [Extension("vcss")]
        PanoramaStyle,

        [Extension("vxml")]
        PanoramaLayout,

        [Extension("vpdi")]
        PanoramaDynamicImages,

        [Extension("vjs")]
        PanoramaScript,

        [Extension("vpsf")]
        ParticleSnapshot,

        [Extension("vmap")]
        Map,
    }
}
