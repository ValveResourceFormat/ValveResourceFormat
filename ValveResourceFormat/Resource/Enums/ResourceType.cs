namespace ValveResourceFormat
{
    /// <summary>
    /// Resource file types.
    /// </summary>
    /// <remarks>
    /// Friendly names are used
    /// </remarks>
    public enum ResourceType
    {
        /// <summary>
        /// Unknown resource type.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// Animgraph 1 animation file. Legacy, as all data is stored in the vmdl file.
        /// </summary>
        [Extension("vanim")]
        Animation,

        /// <summary>
        /// Animgraph 1 animation group. Legacy, as all data is stored in the vmdl file.
        /// </summary>
        [Extension("vagrp")]
        AnimationGroup,

        /// <summary>
        /// Animgraph 1 graph for animation behavior and states.
        /// </summary>
        [Extension("vanmgrph")]
        AnimationGraph,

        /// <summary>
        /// Animgraph 2 graph for animation behavior. Graph variations have a vnmgraph.+variationname suffix.
        /// </summary>
        [Extension("vnmgrph")]
        NmGraph,

        /// <summary>
        /// Animgraph 2 graph variation.
        /// </summary>
        [Extension("vnmvar")]
        NmGraphVariation,

        /// <summary>
        /// Animgraph 2 skeleton. Used for skeleton(s) and bonemask data.
        /// </summary>
        [Extension("vnmskel")]
        NmSkeleton,

        /// <summary>
        /// Animgraph 2 animation clip. Can contain events to respond from graphs.
        /// </summary>
        [Extension("vnmclip")]
        NmClip,

        /// <summary>
        /// Action list for surface property impact effects.
        /// </summary>
        [Extension("valst")]
        ActionList,

        /// <summary>
        /// Sequence group. Legacy, as all data is stored in the vmdl file.
        /// </summary>
        [Extension("vseq")]
        Sequence,

        /// <summary>
        /// Particle system created in the Particle Editor.
        /// </summary>
        [Extension("vpcf")]
        Particle,

        /// <summary>
        /// Material definition created in the Material Editor.
        /// </summary>
        [Extension("vmat")]
        Material,

        /// <summary>
        /// Sheet file.
        /// </summary>
        [Extension("vmks")]
        Sheet,

        /// <summary>
        /// Mesh geometry. Legacy, as all data is stored in the vmdl file.
        /// </summary>
        [Extension("vmesh")]
        Mesh,

        /// <summary>
        /// Compiled texture.
        /// </summary>
        [Extension("vtex")]
        Texture,

        /// <summary>
        /// 3D model created in the ModelDoc Editor.
        /// </summary>
        [Extension("vmdl")]
        Model,

        /// <summary>
        /// Physics collision mesh.
        /// </summary>
        [Extension("vphys")]
        PhysicsCollisionMesh,

        /// <summary>
        /// Sound file or sound container.
        /// </summary>
        [Extension("vsnd")]
        Sound,

        /// <summary>
        /// Morph set.
        /// </summary>
        [Extension("vmorf")]
        Morph,

        /// <summary>
        /// Resource manifest for preloading assets.
        /// </summary>
        [Extension("vrman")]
        ResourceManifest,

        /// <summary>
        /// World root file.
        /// </summary>
        [Extension("vwrld")]
        World,

        /// <summary>
        /// World node referencing map geometry and scene objects.
        /// </summary>
        [Extension("vwnod")]
        WorldNode,

        /// <summary>
        /// World visibility data stored in voxel clusters.
        /// </summary>
        [Extension("vvis")]
        WorldVisibility,

        /// <summary>
        /// Entity lump containing map entities.
        /// </summary>
        [Extension("vents")]
        EntityLump,

        /// <summary>
        /// Surface properties for materials.
        /// </summary>
        [Extension("vsurf")]
        SurfaceProperties,

        /// <summary>
        /// Sound event script defining sound events.
        /// </summary>
        [Extension("vsndevts")]
        SoundEventScript,

        /// <summary>
        /// Mix graph for audio mixing created in the VMix Tool.
        /// </summary>
        [Extension("vmix")]
        VMix,

        /// <summary>
        /// Sound stack script with rules and operators for sound events.
        /// </summary>
        [Extension("vsndstck")]
        SoundStackScript,

        /// <summary>
        /// Bitmap font.
        /// </summary>
        [Extension("vfont")]
        BitmapFont,

        /// <summary>
        /// Resource remap table.
        /// </summary>
        [Extension("vrmap")]
        ResourceRemapTable,

        /// <summary>
        /// Choreo scene file data combining multiple VCD files.
        /// </summary>
        [Extension("vcdlist")]
        ChoreoSceneFileData,

        /// <summary>
        /// Panorama UI element.
        /// </summary>
        /// <remarks>
        /// All Panorama* files are compiled just as CompilePanorama.
        /// vtxt is not a real extension.
        /// </remarks>
        [Extension("vtxt")]
        Panorama,

        /// <summary>
        /// Panorama CSS style defining UI appearance.
        /// </summary>
        [Extension("vcss")]
        PanoramaStyle,

        /// <summary>
        /// Panorama XML layout defining UI structure.
        /// </summary>
        [Extension("vxml")]
        PanoramaLayout,

        /// <summary>
        /// Panorama dynamic images manifest.
        /// </summary>
        [Extension("vpdi")]
        PanoramaDynamicImages,

        /// <summary>
        /// Panorama JavaScript.
        /// </summary>
        [Extension("vjs")]
        PanoramaScript,

        /// <summary>
        /// Panorama TypeScript.
        /// </summary>
        [Extension("vts")]
        PanoramaTypescript,

        /// <summary>
        /// Panorama SVG vector graphic.
        /// </summary>
        [Extension("vsvg")]
        PanoramaVectorGraphic,

        /// <summary>
        /// Legacy particle snapshot.
        /// </summary>
        [Extension("vpsf")]
        ParticleSnapshot,

        /// <summary>
        /// Particle snapshot reference.
        /// </summary>
        [Extension("vsnap")]
        Snap,

        /// <summary>
        /// Map definition.
        /// </summary>
        [Extension("vmap")]
        Map,

        /// <summary>
        /// Post-processing settings created in the Postprocessing Editor.
        /// </summary>
        [Extension("vpost")]
        PostProcessing,

        /// <summary>
        /// Generic data for game content like weapons, heroes, and abilities.
        /// </summary>
        [Extension("vdata")]
        VData,

        /// <summary>
        /// Composite material kit for CS2 weapon skins.
        /// </summary>
        [Extension("vcompmat")]
        CompositeMaterial,

        /// <summary>
        /// Response rules for voiceline playback.
        /// </summary>
        [Extension("vrr")]
        ResponseRules,

        /// <summary>
        /// CS2 economy item created in the CS2 Item Editor.
        /// </summary>
        [Extension("econitem")]
        EconItem,

        /// <summary>
        /// Artifact item from items_game.txt.
        /// </summary>
        [Extension("item")]
        ArtifactItem,

        /// <summary>
        /// Pulse graph definition created in the Pulse Editor.
        /// </summary>
        [Extension("pulse")]
        PulseGraphDef,

        /// <summary>
        /// Smart prop with defined rules and parameters in the VData Editor.
        /// </summary>
        [Extension("vsmart")]
        SmartProp,

        /// <summary>
        /// Processing graph instance. Precursor to legacy VMDL nodes.
        /// </summary>
        [Extension("vpram")]
        ProcessingGraphInstance,

        /// <summary>
        /// Dota 2 hero list.
        /// </summary>
        [Extension("herolist")]
        DotaHeroList,

        /// <summary>
        /// Dota 2 patch notes.
        /// </summary>
        [Extension("vdpn")]
        DotaPatchNotes,

        /// <summary>
        /// Dota 2 visual novels.
        /// </summary>
        [Extension("vdvn")]
        DotaVisualNovels,

        /// <summary>
        /// S&amp;box managed resource of any type.
        /// </summary>
        /// <remarks>
        /// Managed resources can have any extension.
        /// </remarks>
        [Extension("sbox")]
        SboxManagedResource,

        /// <summary>
        /// S&amp;box shader.
        /// </summary>
        [Extension("shader")]
        SboxShader,

        /// <summary>
        /// Compiled shader. Stored in shaders_{platform}_dir.vpk.
        /// </summary>
        [Extension("vcs")]
        Shader,
    }
}
