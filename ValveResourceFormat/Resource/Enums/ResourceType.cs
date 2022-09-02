using System;

namespace ValveResourceFormat
{
    // Friendly names are used
    public enum ResourceType
    {
#pragma warning disable 1591
        Unknown = 0,

        [Extension("vanim")]
        Animation,

        [Extension("vagrp")]
        AnimationGroup,

        [Extension("vanmgrph")]
        AnimationGraph,

        [Extension("valst")]
        ActionList,

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

        [Extension("vmix")]
        VMix,

        [Extension("vsndstck")]
        SoundStackScript,

        [Extension("vfont")]
        BitmapFont,

        [Extension("vrmap")]
        ResourceRemapTable,

        [Extension("vcdlist")]
        ChoreoSceneFileData,

        // All Panorama* are compiled just as CompilePanorama
        // vtxt is not a real extension
        [Extension("vtxt")]
        Panorama,

        [Extension("vcss")]
        PanoramaStyle,

        [Extension("vxml")]
        PanoramaLayout,

        [Extension("vpdi")]
        PanoramaDynamicImages,

        [Extension("vjs")]
        PanoramaScript,

        [Extension("vts")]
        PanoramaTypescript,

        [Extension("vsvg")]
        PanoramaVectorGraphic,

        [Extension("vpsf")]
        ParticleSnapshot,

        [Extension("vmap")]
        Map,

        [Extension("vdata")]
        VData,

        [Extension("item")]
        ArtifactItem,

        [Extension("sbox")] // TODO: Managed resources can have any extension
        SboxManagedResource,
#pragma warning restore 1591
    }
}
