namespace ValveResourceFormat
{
    // Friendly names are used
    public enum ResourceType
    {
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
        ChoreoDataList,

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

        [Extension("vsnap")]
        Snap,

        [Extension("vmap")]
        Map,

        [Extension("vpost")]
        PostProcessing,

        [Extension("vdata")]
        VData,

        [Extension("vcompmat")]
        CompositeMaterial,

        [Extension("vrr")]
        ResponseRules,

        [Extension("csgoitem")]
        CSGOItem,

        [Extension("econitem")]
        CSGOEconItem,

        [Extension("item")]
        ArtifactItem,

        [Extension("pulse")]
        PulseGraphDef,

        [Extension("vsmart")]
        SmartProp,

        [Extension("herolist")]
        DotaHeroList,

        [Extension("sbox")] // TODO: Managed resources can have any extension
        SboxManagedResource,

        [Extension("shader")]
        Shader, // This is for sbox
    }
}
